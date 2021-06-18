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
using System.Threading;
using System.Net;
using System.IO;
using System.Diagnostics;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Security.Authentication;

using WebServer.WebSockets;

namespace WebServer.HTTP
{
    public class Server
    {
        // Public Properties
        public string ServerName { get; set; }

        public string Location
        {
            get
            {
                return $"{(SSLEnabled ? "https" : "http")}://{EndPoint.Address.MapToIPv4()}:{EndPoint.Port}";
            }
        }

        private IPEndPoint _EndPoint = null;

        public IPEndPoint EndPoint
        {
            get => _EndPoint;
            set {
                if (_IsActive)
                {
                    throw new Exception();
                }
                _EndPoint = value;
                _Server = new TcpListener(_EndPoint.Address, _EndPoint.Port);
            }
        }

        public Boolean KeepAliveEnabled { get; set; }

        public Boolean SSLEnabled { get; set; }

        /// <summary>
        /// The SSL Certificate filename in PFX format.
        /// </summary>
        /// <remarks>
        /// You can convert an OpenSSL PEM (Cert and Key file) using
        /// `openssl pkcs12 -export -out certificate.pfx -inkey privateKey.key -in certificate.crt -certfile CACert.crt`
        /// </remarks>
        public string SSLCertificateFileName { get; set; }

        // Privates
        private TcpListener _Server;
        private Thread _Listener;

        private Boolean _Quit = false;
        private Boolean _IsActive = false;

        private X509Certificate2 _CertInUse;

        // Events
        public delegate void RequestReceivedHandler(Client Sender, Request Request);
        public event RequestReceivedHandler RequestReceived;

        public event WebSocket.ConnectivityHandler WebSocketConnected;
        public event WebSocket.ConnectivityHandler WebSocketDisconnected;
        public event WebSocket.MessageReceivedHandler WebSocketMessage;

        public delegate void LoggingHandler(Server Sender, string FormattedMessage, LOG_TYPE Type, object[] MessageArgs);
        public event LoggingHandler ServerLog;

        // Constructor
        public Server(IPEndPoint EndPoint = null)
        {
            SSLEnabled = false;
            this.EndPoint = EndPoint;
            ServerName = "dotnet-webserver";
        }

        /// <summary>
        /// Actively listens for new clients
        /// </summary>
        protected void Listen()
        {
            _Server.Start();
            _IsActive = true;
            while (!_Quit)
            {
                TcpClient Client = null;
                try
                {
                    Client = new TcpClient();
                    Client.Client = _Server.AcceptSocket();
                    //Client = _Server.AcceptTcpClient();
                }
                catch (/*Socket*/Exception e)
                {
                    // Ignore when quiting since the socket
                    // will have to be interrupted
                    if (!_Quit)
                    {
                        Log(LOG_TYPE.WARNING, e.Message);
                        return;
                    }
                }

                if (Client != null) AcceptClient(new Client(Client, this));
            }
        }

        /// <summary>
        /// Moves every accepted TcpClient stream into a new thread for processing.
        /// </summary>
        /// <param name="Client"></param>
        protected void AcceptClient(Client Connection)
        {
            // Never bother with empty requests
            if (Connection == null) return;
            (new Thread(() => ProcessClient(Connection))).Start();
        }

        /// <summary>
        /// Processes an accepted TcpClient stream
        /// </summary>
        /// <param name="Client"></param>
        protected void ProcessClient(Client Connection)
        {
            // Do once
            if (SSLEnabled && _CertInUse != null)
            {
                try
                {
                    Connection.UpgradeStream(_CertInUse);
                }
                catch (AuthenticationException e)
                {
                    Log(LOG_TYPE.EXCEPTION, "SSL Exception: {0}", e.Message);
                    Connection.Close();
                    Connection = null;
                }
                catch (IOException e)
                {
                    //Log(LOG_TYPE.EXCEPTION, "SSL Connection was closed unexpectedly", e);
                    Debug.WriteLine(e);
                    Connection = null;
                }
            }
            else if (SSLEnabled)
            {
                Log(LOG_TYPE.WARNING, "SSL is enabled but no certificate is in use. Requests will be served over HTTP.");
            }

            while (Connection != null && !Connection.Closed && Connection.Connected)
            {
                Request Request = null;
                try
                {
                    Request = Connection.Read();
                }
                catch (Exception e)
                {
                    Log(LOG_TYPE.EXCEPTION, "Connection encountered exception during read:", e);
                }

                // Empty connection, perhaps a different protocol attempt
                if (Request == null) break;

                if (Request.GetHeader("Upgrade") == "websocket")
                {
                    WebSocket WebSocket = new WebSocket(Connection, Request);

                    if (!WebSocket.Upgraded)
                    {
                        Log(LOG_TYPE.WARNING, "Web Socket upgrade failed: " + WebSocket.UpgradeError);
                        Connection.Close();
                        Request = null;
                        return;
                    }

                    if (WebSocketConnected != null) WebSocket.OnWebSocketConnected += WebSocketConnected;
                    if (WebSocketDisconnected != null) WebSocket.OnWebSocketDisconnected += WebSocketDisconnected;
                    if (WebSocketMessage != null) WebSocket.MessageReceived += WebSocketMessage;

                    WebSocket.Read();
                    return;
                }

                if (RequestReceived == null)
                {
                    Connection.Write(Response.SendStatus(500));
                    Connection.Close();
                    return;
                }

                RequestReceived(Connection, Request);
                Request = null;
            }

            if (!Connection.Closed) Connection.Close();
            Connection = null;

            GC.Collect(2, GCCollectionMode.Forced);
        }

        /// <summary>
        /// Starts the web server
        /// </summary>
        public void Start()
        {
            if (SSLEnabled && SSLCertificateFileName != null && File.Exists(SSLCertificateFileName))
            {
                _CertInUse = new X509Certificate2(File.ReadAllBytes(SSLCertificateFileName), "");
            }
            else if (SSLEnabled && SSLCertificateFileName != null)
            {
                Log(LOG_TYPE.ERROR, "SSL Certificate cannot be found.");
            }

            _Listener = new Thread(Listen);
            _Listener.Start();
        }

        /// <summary>
        /// Logging types to help seperate different kinds of messages
        /// </summary>
        public enum LOG_TYPE
        {
            ERROR = 1,
            EXCEPTION = 2,
            WARNING = 3,
            INFO = 4
        }

        /// <summary>
        /// Log operations for kinds of logging. Objects must support the 'toString()' function.
        /// </summary>
        /// <param name="Type">The type of log given</param>
        /// <param name="Value">The log message</param>
        public void Log(LOG_TYPE LogType = LOG_TYPE.WARNING, params object[] Value)
        {
            string Type = "";
            string Message = "";

            Type += LogType.ToString();

            for (int i = 0; i < Value.Length; i++)
            {
                string Val = Value[i].ToString();
                if (Val != "")
                {
                    Message += " " + Val;
                }
            }

            string Formatted = String.Format("[{0}, {1}] {2}", Type, DateTime.Now.ToString("G"), Message);
            if (ServerLog != null)
            {
                ServerLog(this, Formatted, LogType, Value);
            }
            else
            {
                Debug.WriteLine(Formatted);
            }
        }

        /// <summary>
        /// Reports whether the server is actively listening and accepting connections.
        /// </summary>
        /// <returns>True or False</returns>
        public Boolean IsActive()
        {
            return _IsActive;
        }

        /// <summary>
        /// Stops the web server and kills all threads (not graceful).
        /// </summary>
        public void Stop()
        {
            _Quit = true;
            _Server.Stop();
            _Listener.Abort();
            _IsActive = false;
        }

    }
}