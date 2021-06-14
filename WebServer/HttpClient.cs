using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Security.Authentication;
using System.Diagnostics;

namespace JAWS
{
    public class HttpClient : TcpClient
    {

        private Stream _Stream;
        private HttpServer _Server;
        private Boolean _Disposed, _Closed = false;

        public Boolean KeepAlive
        {
            get; set;
        }

        public HttpServer Server
        {
            get
            {
                return _Server;
            }
        }

        public Boolean Disposed { get { return _Disposed; } }

        public Boolean Closed { get { return _Closed; } }

        private int KeepAliveMax;
        private int KeepAliveCount;
        private int KeepAliveTimeout;

        /// <summary>
        /// Returns the current stream for read/write.
        /// </summary>
        /// <returns>The active stream</returns>
        public new Stream GetStream()
        {
            if (_Stream == null)
            {
                _Stream = base.GetStream();
            }
            return _Stream;
        }

        /// <summary>
        /// Upgrades the current socket to work with an SslStream using the certificate given, to allow HTTPS.
        /// </summary>
        /// <remarks>Must be surrounded with try-catches.</remarks>
        /// <param name="Certificate">The certificate to verify the host</param>
        /// <returns>The upgraded stream.</returns>
        public Stream UpgradeStream(X509Certificate Certificate)
        {
            Stream Stream = GetStream();
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

            _Stream = SslStream;

            return _Stream;
        }

        /// <summary>
        /// Closes the HttpClient
        /// </summary>
        public new void Close()
        {
            if (_Closed || _Disposed)
            {
                return;
            }
            _Stream.Close();
            _Stream.Dispose();
            _Stream = null;
            base.Close();
            base.Dispose();
            _Closed = _Disposed = true;
        }

        /// <summary>
        /// Creates a new HttpClient instance (a TcpClient wrapper)
        /// </summary>
        /// <param name="Connection"></param>
        public HttpClient(TcpClient Connection, HttpServer Server)
        {
            Client = Connection.Client;
            _Server = Server;
            KeepAlive = true;
        }

        /// <summary>
        /// Receives a request from the HTTP client.
        /// </summary>
        /// <returns></returns>
        public Request Read()
        {
            ReceiveTimeout = 1000;
            Stream Stream = GetStream();
            if (Stream == null)
            {
                return null;
            }
            List<byte> Bytes = new List<byte>();
            byte[] Data;
            int Size = ReceiveBufferSize;

            while (Size >= ReceiveBufferSize)
            {
                Data = new byte[ReceiveBufferSize];
                Size = Stream.Read(Data, 0, ReceiveBufferSize);
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
                    } else
                    {
                        // Make sure the socket gets a timeout when we're keeping the session alive.
                        ReceiveTimeout = 5000;
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
            Stream Stream = GetStream();
            ReceiveTimeout = 10000;

            Response.Headers.Add("Content-Length", Response.Content.Length);
            if (!Response.Headers.ContainsKey("Connection")) {
                Response.Headers.Add("Connection", (KeepAlive ? "Keep-Alive" : "Close"));
            }
            Response.Headers.Add("Server", Server.ServerName);

// Commented out 14th June 2021. Fixed multi-content byte issue bug where bytes are not sent correctly causing corrupt responses
//             byte[] Headers = Response.GetHeaderBytes();
//             long Length = Headers.Length + Response.Content.Length;
//             long Size = Math.Min(Length, SendBufferSize);
//             byte[] Chunk = new byte[Size];

//             long Offset = 0;
//             while (Offset < Length)
//             {
//                 int CurrentChunkWidth = 0;
//                 int RestChunkWidth = 0;
//                 if (Offset < Headers.Length)
//                 {
//                     CurrentChunkWidth = (int)Math.Min(Headers.Length - Offset, Size);
//                     Array.Copy(Headers, Offset, Chunk, 0, CurrentChunkWidth);
//                 }

//                 if (CurrentChunkWidth < Size)
//                 {
//                     RestChunkWidth = (int)Math.Min(Response.Content.Length - Response.Content.Position, Size - CurrentChunkWidth);
//                     Response.Content.Read(Chunk, CurrentChunkWidth, RestChunkWidth);
//                 }

//                 int Sent = CurrentChunkWidth + RestChunkWidth;
//                 try
//                 {
//                     Stream.Write(Chunk, 0, Sent);
//                 }
//                 catch (Exception e)
//                 {
//                     // The client was disconnected during this, only show as information
//                     // since it's likely due to being closed.
//                     Server.Log(HttpServer.LOG_TYPE.INFO, "The client has unexpectedly disconnected.", e.Message);
//                     //if (e.Message.IndexOf("forcibly closed") > -1)
//                     //{
//                     //    Server.Log(HttpServer.LOG_TYPE.WARNING, "The connection was forcibly closed, are we sending bytes correctly?");
//                     //}
//                     return;
//                 }

//                 Offset += Sent;
//             }
            byte[] Headers = Response.GetHeaderBytes();
            
            long Length = Headers.Length + Response.Content.Length;
            
            int Size = (Length < BUFFER_SIZE ? (int)Length : BUFFER_SIZE);
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
                    //Server.Log(HttpServer.LOG_TYPE.INFO, "The client has unexpectedly disconnected.", e.Message);
                    Server.Log(HttpServer.LOG_TYPE.INFO, $"Client connection failed to write chunk ({Offset}/{Length}): {Ex}");
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
