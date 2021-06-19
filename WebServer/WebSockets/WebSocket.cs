/**
 *    This program is free software: you can redistribute it and/or modify
 *    it under the terms of the GNU General Public License as published by
 *    the Free Software Foundation, either version 3 of the License, or
 *    (at your option) any later version.
 *
 *    This program is distributed in the hope that it will be useful,
 *    but WITHOUT ANY WARRANTY; without even the implied warranty of
 *    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 *    GNU General Public License for more details.
 *
 *    You should have received a copy of the GNU General Public License
 *    along with this program.  If not, see <https://www.gnu.org/licenses/>.
 */
using System;
using System.Collections.Generic;
using System.Text;
using System.Security.Cryptography;
using System.IO;
using System.Threading;

using WebServer.HTTP;

namespace WebServer.WebSockets
{
    public class WebSocket
    {
        private Client Client;

        public bool Upgraded { get; private set; }

        public string UpgradeError { get; private set; }

        public string Protocol { get; private set; }

        //private Thread ReadingThread;

        private string GenerateAcceptKey(string ProvidedKey)
        {
            string MagicString = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

            return Convert.ToBase64String(
                SHA1.Create().ComputeHash(Encoding.UTF8.GetBytes(ProvidedKey + MagicString))
            );
        }

        public enum FrameOpCode
        {
            CONTINUE_FRAME = 0,
            TEXT_FRAME = 1,
            BINARY_FRAME = 2,
            RESERVED_3 = 3,
            RESERVED_4 = 4,
            RESERVED_5 = 5,
            RESERVED_6 = 6,
            RESERVED_7 = 7,
            CLOSE_FRAME = 8,
            PING_FRAME = 9,
            PONG_FRAME = 10,
            RESERVED_11 = 11,
            RESERVED_12 = 12,
            RESERVED_13 = 13,
            RESERVED_14 = 14,
            RESERVED_15 = 15,
        }

        public enum FrameFIN
        {
            CONTINUE = 0,
            FINISHED = 1
        }

        public delegate void ConnectivityHandler(WebSocket WebSocket);
        public event ConnectivityHandler OnWebSocketConnected;
        public event ConnectivityHandler OnWebSocketDisconnected;

        public delegate void MessageReceivedHandler(WebSocket Sender, MessageEventArgs E);
        public event MessageReceivedHandler MessageReceived;

        public WebSocket(Client Client, Request Request)
        {
            // TODO: Add support for WebSocket clients in same class?!?
            if (Request.GetHeader("Connection") != "Upgrade")
            {
                Upgraded = false;
                UpgradeError = "Connection doesn't wish to upgrade";
                return;
            }

            if (Request.GetHeader("Upgrade") != "websocket")
            {
                Upgraded = false;
                UpgradeError = "No websocket upgrade specified";
                return;
            }

            string Key;
            if ((Key = Request.GetHeader("Sec-WebSocket-Key").Trim()) == null)
            {
                Upgraded = false;
                UpgradeError = "No websocket key specified";
                return;
            }

            string Version;
            if ((Version = Request.GetHeader("Sec-WebSocket-Version")) == null)
            {
                Upgraded = false;
                UpgradeError = "No websocket version specified";
                return;
            }

            if (Version != "13")
            {
                Upgraded = false;
                UpgradeError = $"Unsupported version (Version={Version})";
                return;
            }

            Request.Client.KeepAlive = true;

            Response Response = new Response();

            // Set status manually as Response.sendStatus sets the content
            // and we don't want to send any mistaken data after the headers

            Response.Status = 101; // Switching Protocols
            Response.Headers.Add("Connection", "Upgrade");
            Response.Headers.Add("Upgrade", "websocket");
            Response.Headers.Add("Sec-WebSocket-Accept", GenerateAcceptKey(Key));

            Client.Send(Response);

            Upgraded = true;

            Client.ReceiveTimeout = 0;
            Client.SendTimeout = 0;

            this.Client = Client;
        }

        /// <summary>
        /// Convert bits to integers, making sure to use network byte mode
        /// </summary>
        /// <param name="Bytes"></param>
        /// <param name="Offset"></param>
        /// <param name="Length"></param>
        /// <returns></returns>
        private long ConvertInt(byte[] Bytes, int Offset, int Length)
        {
            byte[] Convert = new byte[Length];
            for (int i = Offset; i < Length + Offset; i++) Convert[i - Offset] = Bytes[i];

            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(Convert);
            }

            switch (Length * 8)
            {
                case 16: // 2
                    return BitConverter.ToInt16(Convert, 0);
                case 32: // 4
                    return BitConverter.ToInt32(Convert, 0);
                case 64: // 8
                    return BitConverter.ToInt64(Convert, 0);
                default:
                    throw new OverflowException("Conversion cannot continue");
            }
        }

        private byte[] ConvertInt(long Int)
        {
            byte[] Convert;

            if (Int < Int16.MaxValue)
            {
                Convert = new byte[2];
                Int16 Scale = (Int16)Int;
                Convert = BitConverter.GetBytes(Scale);
            }
            else if (Int < Int32.MaxValue)
            {
                Convert = new byte[4];
                Int32 Scale = (Int32)Int;
                Convert = BitConverter.GetBytes(Scale);
            }
            else if (Int < Int64.MaxValue)
            {
                Convert = new byte[8];
                Int64 Scale = (Int64)Int;
                Convert = BitConverter.GetBytes(Scale);
            }
            else
            {
                throw new OverflowException("Conversion cannot continue");
            }

            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(Convert);
            }

            return Convert;
        }

        public void Read()
        {
            if (OnWebSocketConnected != null) OnWebSocketConnected(this);

            Stream Stream = Client.GetStream();
            MemoryStream Buffer = new MemoryStream();
            FrameOpCode LastRecvOpCode = 0;

            while (Client.Connected)
            {
                if (Client.Available == 0)
                {
                    Thread.Sleep(1);
                    continue;
                }

                int ReceivedBytesAvailable = Client.Available;

                byte[] ReceivedBytes = new byte[ReceivedBytesAvailable];
                byte[] DecodedBytes;
                byte[] Masks = new byte[4];

                ReceivedBytesAvailable = Stream.Read(ReceivedBytes, 0, ReceivedBytesAvailable);

                FrameFIN FIN = (FrameFIN)((ReceivedBytes[0] & 0b10000000) != 0 ? FrameFIN.FINISHED : FrameFIN.CONTINUE);
                FrameOpCode OpCode = (FrameOpCode)(ReceivedBytes[0] & 0b0001111 /* 15 */);

                bool Masked = (ReceivedBytes[1] & 0b10000000) != 0;
                long PayloadLength = ReceivedBytes[1] - 0b10000000;

                if (!Masked) break; // Must close here (section 5.1 of spec)

                if (OpCode == FrameOpCode.CONTINUE_FRAME) OpCode = LastRecvOpCode;

                if (OpCode.ToString().Contains("RESERVED")) break;

                int Offset = 2;

                if (PayloadLength == 127)
                {
                    PayloadLength = ConvertInt(ReceivedBytes, Offset, 8);
                    Offset += 8; // 2 first bits + 64 bit number (8 bytes)
                }
                else if (PayloadLength == 126)
                {
                    PayloadLength = ConvertInt(ReceivedBytes, Offset, 2);
                    Offset += 2; // 2 first bits + 16 bit number (2 bytes)
                }

                // Masks only used for server reads (client sends masked data)
                // Servers should not send masked data

                // Mask uses a 32 bit number (4 bytes)
                for (int Idx = 0; Idx < 4; Idx++)
                {
                    Masks[Idx] = ReceivedBytes[Offset + Idx];
                }

                Offset += 4; // Masks used up

                DecodedBytes = new byte[PayloadLength];

                // Decode bytes
                for (int Idx = 0; Idx < PayloadLength; ++Idx)
                {
                    DecodedBytes[Idx] = (byte)(ReceivedBytes[Offset + Idx] ^ Masks[Idx % 4]);
                }

                // Note: Since we read all available bytes, we may potentially read bytes of the next message,
                //       so it may be best to append the latest bytes to it. If this happens, this code will
                //       need to be updated to support this scenario.

                if (OpCode == FrameOpCode.CLOSE_FRAME) break;
                if (OpCode == FrameOpCode.PONG_FRAME) continue;
                if (OpCode == FrameOpCode.PING_FRAME)
                {
                    this.SendPong();
                    continue;
                }

                LastRecvOpCode = OpCode;

                // Write message to the buffer;
                Offset = 0;
                while (Offset < PayloadLength)
                {
                    if (PayloadLength - Offset >= int.MaxValue)
                    {
                        Buffer.Write(DecodedBytes, Offset, int.MaxValue);
                        Offset += int.MaxValue;
                    }
                    else
                    {
                        int Rest = (int)(PayloadLength - Offset);
                        Buffer.Write(DecodedBytes, Offset, Rest);
                        Offset += Rest;
                    }
                }

                if (FIN == FrameFIN.FINISHED && MessageReceived != null)
                {
                    MessageReceived(this, new MessageEventArgs(OpCode, Buffer));
                    Buffer = new MemoryStream();
                } /* else FIN == FrameFIN.CONTINUE */
            }

            Client.Close();

            if (OnWebSocketDisconnected != null) OnWebSocketDisconnected(this);
        }

        private void SendPong()
        {
            this.Write(null, FrameOpCode.PONG_FRAME);
        }

        private void SendPing()
        {
            // TODO: Create an optional/configurable keep alive send ping thread?
            this.Write(null, FrameOpCode.PING_FRAME);
        }

        private void Close()
        {
            this.Write(null, FrameOpCode.CLOSE_FRAME);
            Client.Close();
        }

        public void Write(string Message)
        {
            Write(Encoding.UTF8.GetBytes(Message), FrameOpCode.TEXT_FRAME);
        }

        public void Write(byte[] Bytes, FrameOpCode OpCode = FrameOpCode.BINARY_FRAME)
        {
            Stream Stream = Client.GetStream();
            List<byte[]> Frames = new List<byte[]>();

            if (OpCode == FrameOpCode.TEXT_FRAME || OpCode == FrameOpCode.BINARY_FRAME)
            {
                if (Bytes == null) return;

                // Arbitrary to us (not specific to spec), since spec could hold a 64bit length
                // yet VS array lengths are integers and thus would have an overflow...
                int MaxPayloadLength = int.MaxValue - 1;

                long FrameCount = Math.Max(Bytes.Length / MaxPayloadLength, 1);

                int Offset = 0;
                for (int FrameIdx = 0; FrameIdx < FrameCount; FrameIdx++)
                {
                    int PayloadLength;
                    if (Bytes.Length > MaxPayloadLength)
                    {
                        PayloadLength = Math.Min(MaxPayloadLength, Bytes.Length - (FrameIdx * MaxPayloadLength));
                    }
                    else
                    {
                        PayloadLength = Bytes.Length;
                    }

                    // Frame byte size
                    int FrameSize = PayloadLength;
                    int OffsetFrameSize = 0;

                    // Bit 1
                    bool FinBit = FrameIdx == FrameCount - 1;
                    FrameOpCode FrameOpCode = FrameIdx > 0 ? FrameOpCode.CONTINUE_FRAME : OpCode;

                    // Bit 2
                    // TODO: Support a client WS too?
                    bool Masked = false; // Only clients send masked data as part of spec 5.1
                    int Length = PayloadLength; // real length by default (< 126)
                    byte[] ExtendedLength = null;
                    if (PayloadLength > Int16.MaxValue)
                    {
                        Length = 127; // Set int64 length flag
                        ExtendedLength = ConvertInt(PayloadLength);
                        OffsetFrameSize += 8;
                    }
                    else if (PayloadLength >= 126)
                    {
                        Length = 126; // Set int16 length flag
                        ExtendedLength = ConvertInt(PayloadLength);
                        OffsetFrameSize += 2;
                    } /* else Length = PayloadLength (<126) */

                    // Frame size is +payload, +2 byte header and +4 byte (32 bit) mask
                    OffsetFrameSize += 2;
                    FrameSize += OffsetFrameSize; // + 4;

                    // Lets start making a frame :)
                    byte[] FrameBytes = new byte[FrameSize];

                    FrameBytes[0] = (byte)((FinBit ? 128 : 0) | (int)OpCode);
                    FrameBytes[1] = (byte)(Masked ? 128 : 0 | Length);

                    // Add optional extended length
                    if (ExtendedLength != null)
                    {
                        if (ExtendedLength.Length > OffsetFrameSize)
                        {
                            // Likely an issue with ConvertInt at this point?
                            throw new Exception("Payload extended length miscalculation");
                        }

                        for (int Idx = 0; Idx < ExtendedLength.Length; Idx++)
                        {
                            FrameBytes[2 + Idx] = ExtendedLength[Idx];
                        }
                    }

                    // Add Masks
                    //byte[] Masks = new byte[4];
                    //(new Random()).NextBytes(Masks);
                    //for (int Idx = 0; Idx < 4; Idx++)
                    //{
                    //    //Masks[Idx] = (byte)(new Random()).Next(255);
                    //    FrameBytes[OffsetFrameSize + Idx] = Masks[Idx];
                    //}
                    //OffsetFrameSize += 4;

                    // Add encoded XOR bytes, only if mask is involved (WS clients senders only)
                    for (int Idx = 0; Idx < PayloadLength; Idx++)
                    {
                        FrameBytes[OffsetFrameSize + Idx] = (byte)(Bytes[Offset + Idx]); // ^ Masks[Idx % 4]);
                    }

                    Offset += PayloadLength;

                    Frames.Add(FrameBytes);
                }
            }
            else
            {
                if (OpCode.ToString().Contains("RESERVED")) throw new Exception("Unable to send a reserved opcode frame");
                if (OpCode == FrameOpCode.CONTINUE_FRAME) throw new Exception("Unable to send continue frames outright");

                Frames.Add(new byte[2] {
                    (byte)((int)FrameFIN.FINISHED | (int)OpCode),
                    0 // No mask for clients & no length in control frames
                });
            }

            // Write all frames to client
            foreach (byte[] Frame in Frames)
            {
                if (Client.Connected && Stream.CanWrite) Stream.Write(Frame, 0, Frame.Length);
            }
        }
    }
}
