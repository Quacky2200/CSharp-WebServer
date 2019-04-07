using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Net;
using System.Text;
using System.IO;
using System.Diagnostics;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Net.Security;
using System.Security.Authentication;
using System.Text.RegularExpressions;
using System.Web;

namespace JAWS
{

    public class HttpServer
    {

        public string ServerName
        {
            get; set;
        }

        private TcpListener _Server;
        private Thread _Listener;
        //private List<Thread> _Threads = new List<Thread>();

        private Boolean _Quit = false;
        private Boolean _IsActive = false;

        private X509Certificate2 _CertInUse;

        public delegate void OnRequestHandler(HttpClient Sender, Request Request);
        public event OnRequestHandler OnRequest;

        public delegate void OnLogHandler(HttpServer Sender, string FormattedMessage, LOG_TYPE Type, object[] MessageArgs);
        public event OnLogHandler OnLog;

        public HttpServer(IPEndPoint EndPoint = null)
        {
            SSLEnabled = false;
            if (EndPoint == null)
            {
                EndPoint = new IPEndPoint(IPAddress.Loopback, 8888);
            }
            _Server = new TcpListener(EndPoint.Address, EndPoint.Port);
            ServerName = "dotnet-jaws";
        }

        public Boolean SSLEnabled
        {
            get; set;
        }

        public Boolean KeepAliveEnabled
        {
            get; set;
        }

        /// <summary>
        /// The SSL Certificate filename in PFX format.
        /// </summary>
        /// <remarks>
        /// You can convert an OpenSSL PEM (Cert and Key file) using
        /// `openssl pkcs12 -export -out certificate.pfx -inkey privateKey.key -in certificate.crt -certfile CACert.crt`
        /// </remarks>
        public string SSLCertificateFileName
        {
            get;
            set;
        }

        /// <summary>
        /// Moves every accepted TcpClient stream into a new thread for processing.
        /// </summary>
        /// <param name="Client"></param>
        protected void ProcessClient(HttpClient Connection)
        {
            // Processing thread
            if (Connection == null)
            {
                // Never bother with empty requests
                return;
            }

            //int ThreadID = _Threads.Count;
            Thread Current = new Thread(() =>
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

                while (Connection != null && !Connection.Closed)
                {
                    //Console.WriteLine("Threads: " + _Threads.Count);

                    Request Request = null;
                    try
                    {
                        Request = Connection.Read();
                    }
                    catch (IOException e)
                    {
                        Log(LOG_TYPE.EXCEPTION, "Connection was forcibly closed - {0}", e);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.GetType().ToString() + " has thrown " + e.Message);
                    }

                    if (Request == null)
                    {
                        // Empty connection, perhaps a different protocol attempt
                        Connection.Close();
                        break;
                    }

                    if (OnRequest != null)
                    {
                        OnRequest(Connection, Request);
                    }
                    else
                    {
                        Connection.Write(Response.SendStatus(500));
                    }
                    Request = null;
                }

                //Connection.Close();
                Connection = null;
                
                //_Threads.Remove(_Threads[ThreadID]);
                GC.Collect(2, GCCollectionMode.Forced);
            });

            Current.Start();
            //_Threads.Add(Current);

        }

        /// <summary>
        /// Starts the web server
        /// </summary>
        public void Start()
        {
            // Main listener thread
            if (SSLEnabled && SSLCertificateFileName != null && File.Exists(SSLCertificateFileName))
            {
                _CertInUse = new X509Certificate2(File.ReadAllBytes(SSLCertificateFileName), "");
            }
            else if (SSLEnabled && SSLCertificateFileName != null)
            {
                Log(LOG_TYPE.ERROR, "SSL Certificate cannot be found.");
            }
            _Listener = new Thread(() =>
            {
                _Server.Start();
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
                    if (Client != null)
                    {
                        ProcessClient(new HttpClient(Client, this));
                    }
                }
            });
            _Listener.Start();
            _IsActive = true;
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
            if (OnLog == null)
            {
                Debug.WriteLine(Formatted);
            }
            else
            {
                OnLog(this, Formatted, LogType, Value);
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
            //foreach (Thread Thread in _Threads)
            //{
            //    Thread.Abort();
            //}
            //_Threads.Clear();
            _Server.Stop();
            _Listener.Abort();
            _IsActive = false;
        }

    }
}