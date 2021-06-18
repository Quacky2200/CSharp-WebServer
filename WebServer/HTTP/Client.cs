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
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Security.Authentication;
using System.Diagnostics;

namespace WebServer.HTTP
{
    public class Client : TcpClient
    {
        public Stream Stream { get; private set; }

        public Boolean KeepAlive { get; set; }

        public Server Server { get; private set; }

        public Boolean Disposed { get; private set; }

        public Boolean Closed { get; private set; }

        private int KeepAliveMax;
        private int KeepAliveCount;
        private int KeepAliveTimeout;

        /// <summary>
        /// Creates a new HttpClient instance (a TcpClient wrapper)
        /// </summary>
        /// <param name="Connection"></param>
        public Client(TcpClient Connection, Server Server)
        {
            this.Client = Connection.Client;
            this.Server = Server;
            this.KeepAlive = true;
            Stream = base.GetStream();
            ReceiveTimeout = 10000;
            SendTimeout = 10000;
        }

        /// <summary>
        /// Closes the HttpClient
        /// </summary>
        public new void Close()
        {
            if (Closed || Disposed) return;
            Stream.Close();
            Stream.Dispose();
            Stream = null;
            base.Close();
            base.Dispose();
            Closed = Disposed = true;
        }

        /// <summary>
        /// Upgrades the current socket to work with an SslStream using the certificate given, to allow HTTPS.
        /// </summary>
        /// <remarks>Must be surrounded with try-catches.</remarks>
        /// <param name="Certificate">The certificate to verify the host</param>
        /// <returns>The upgraded stream.</returns>
        public Stream UpgradeStream(X509Certificate Certificate)
        {
            Stream Stream = this.Stream;
            SslStream SslStream = new SslStream(Stream, false);

            try
            {
                SslStream.AuthenticateAsServer(Certificate, false, SslProtocols.Tls12, false);
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
                Close();
                return null;
                //throw e;
            }

            Stream = SslStream;

            return Stream;
        }

        /// <summary>
        /// Receives a request from the HTTP client.
        /// </summary>
        /// <returns></returns>
        public Request Read()
        {
            Stream Stream = this.Stream;
            if (Stream == null) return null;

            List<byte> Bytes = new List<byte>();
            byte[] Data;
            int Size = ReceiveBufferSize;

            while (Size >= ReceiveBufferSize)
            {
                Data = new byte[ReceiveBufferSize];
                try
                {
                    Size = Stream.Read(Data, 0, ReceiveBufferSize);
                }
                catch (IOException)
                {
                    // Disconnect on IOException
                    Close();
                    return null;
                }
                Bytes.AddRange(Data);
            }

            string RawHttpMessage = Encoding.UTF8.GetString(Bytes.ToArray()).TrimEnd("\0".ToCharArray());

            Request Parsed = Request.Parse(this, RawHttpMessage);

            // If KeepAlive is allowed
            string ConnectionHeader = null;
            if (KeepAlive && Parsed != null && (ConnectionHeader = Parsed.GetHeader("Connection")) != null)
            {
                ConnectionHeader = ConnectionHeader.ToLower();
                if (ConnectionHeader.IndexOf("keep-alive") == -1)
                {
                    KeepAlive = false;
                }
                else
                {
                    int Index = -1;
                    if ((Index = ConnectionHeader.IndexOf("timeout=")) > -1)
                    {
                        string Timeout = ConnectionHeader.Substring(Index);
                        Timeout = Timeout.Substring(0, Math.Max(Math.Min(Timeout.IndexOf(','), Timeout.Length), 0));
                    }
                    else
                    {
                        ReceiveTimeout = 0;
                    }

                    if ((Index = ConnectionHeader.IndexOf("max=")) > 0)
                    {
                        //KeepAliveMax = ConnectionHeader.Substring(Index);
                    }
                }
            }

            return Parsed;
        }

        /// <summary>
        /// Receives a request from the HTTP client (alias of read).
        /// </summary>
        /// <returns></returns>
        public Request Receive()
        {
            return Read();
        }

        /// <summary>
        /// Sends a response to the HTTP client.
        /// </summary>
        /// <param name="Response"></param>
        public void Write(Response Response)
        {
            Stream Stream = this.Stream;
            if (Stream == null) return;

            if (!Response.Headers.ContainsKey("Upgrade"))
            {
                Response.Headers.Add("Content-Length", Response.Content.Length);
                if (!Response.Headers.ContainsKey("Connection")) {
                    Response.Headers.Add("Connection", (KeepAlive ? "Keep-Alive" : "Close"));
                }
                Response.Headers.Add("Server", Server.ServerName);
            }

            byte[] Headers = Response.GetHeaderBytes();
            
            long Length = Headers.Length + Response.Content.Length;
            
            int Size = (Length < SendBufferSize ? (int)Length : SendBufferSize);
            byte[] Chunk;

            int Offset = 0, ChunkSize = 0, Sent;
            while (Offset < Length)
            {
                Chunk = new byte[Size];

                ChunkSize = 0;

                if (Offset < Headers.Length)
                {
                    ChunkSize = (int)Math.Min(Headers.Length - Offset, Size);
                    Array.Copy(Headers, Offset, Chunk, 0, ChunkSize);
                }

                if (Offset + ChunkSize >= Headers.Length)
                {
                    ChunkSize += Response.Content.Read(Chunk, ChunkSize, Size - ChunkSize);
                }

                try
                {
                    if (ChunkSize < Size)
                    {
                        // We must resize when we're ending with less than the buffer
                        // size as we otherwise send a bunch of zeros that we don't need
                        byte[] Temp = Chunk;
                        Chunk = new byte[ChunkSize];
                        Array.Copy(Temp, Chunk, ChunkSize);
                    }
                    Sent = Client.Send(Chunk, 0);
                }
                catch (Exception Ex)
                {
                    Stream.Close();
                    Server.Log(Server.LOG_TYPE.INFO, $"Client connection failed to write chunk ({Offset}/{Length}): {Ex}");
                    return;
                }

                Offset += Sent;
           }
            
           if (!KeepAlive)
           {
                Close();
                Stream.Close();
           }
        }

        /// <summary>
        /// Sends a response to the HTTP client (alias of Write).
        /// </summary>
        /// <param name="Response"></param>
        public void Send(Response Response)
        {
            Write(Response);
        }
    }
}
