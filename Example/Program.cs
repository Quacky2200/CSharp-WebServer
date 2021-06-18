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
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.IO;

using WebServer.HTTP;
using WebServer.Routing;
using WebServer.WebSockets;

// Cursor Position
using System.Windows.Forms;
using System.Drawing;

// Process.Start
using System.Diagnostics;

namespace Example
{
    class Program
    {
        private static Server HttpServer;
        private static Router Router;

        private readonly static List<WebSocket> WebSockets = new List<WebSocket>();

        private static readonly String WebDirectory = Path.Combine(Directory.GetCurrentDirectory(), "public");

        public static void Main(string[] args)
        {
            bool Secure = false;
            IPEndPoint Address = new IPEndPoint(
                IPAddress.Loopback, // for 0.0.0.0 use IPAddress.Any
                0
            );

            if (Secure)
            {
                // Secure HTTP Server example:
                Address.Port = 443;
                HttpServer = new Server(Address);
                HttpServer.SSLEnabled = true;
                HttpServer.SSLCertificateFileName = Path.Combine(Directory.GetCurrentDirectory(), "certificate.pfx");
            }
            else
            {
                Address.Port = 8888;
                HttpServer = new Server(Address);
            }

            // Old method to respond to incoming http requests.
            // A router can achieve similar functionality with rules (e.g. .Get/.Post)
            // Only enabled for logging requests to console
            HttpServer.RequestReceived += ProcessWebServerResponse;
            Router = new Router(HttpServer);

            HttpServer.WebSocketConnected += Server_WebSocketConnected;
            HttpServer.WebSocketMessage += Server_WebSocketMessage;
            HttpServer.WebSocketDisconnected += Server_WebSocketDisconnected;

            Router.Get("*.cjs", Router_Jint);
            // Example router rule with variable
            // Router.Get("/status/:service", Router_GetStatus);
            Router.Get("/.*", Router_GetPublic);

            HttpServer.Start();
            Console.WriteLine($"Server listening and available at {HttpServer.Location}");
            Process.Start(HttpServer.Location);

            (new Thread(Cursor_Update)).Start();

            while (true) Thread.Sleep(1000);
        }

        private static void Cursor_Update()
        {
            Point LastPoint = new Point();
            while (true)
            {
                Thread.Sleep(100);

                Point Position = Cursor.Position;
                if (Position.X == LastPoint.X && Position.Y == LastPoint.Y) continue;
                LastPoint = Position;

                foreach (WebSocket Socket in WebSockets) Socket.Write($"Cursor,X={Position.X},Y={Position.Y}");
            }
        }

        private static void Server_WebSocketConnected(WebServer.WebSockets.WebSocket WebSocket)
        {
            Console.WriteLine($"> (WebSocket Connected)");
            int Idx = WebSockets.IndexOf(WebSocket);
            if (Idx == -1) WebSockets.Add(WebSocket);
        }

        private static void Server_WebSocketDisconnected(WebServer.WebSockets.WebSocket WebSocket)
        {
            Console.WriteLine($"> (WebSocket Disconnected)");
            int Idx = WebSockets.IndexOf(WebSocket);
            if (Idx > -1) WebSockets.RemoveAt(Idx);
        }

        private static void Server_WebSocketMessage(WebServer.WebSockets.WebSocket Sender, WebServer.WebSockets.MessageEventArgs E)
        {
            string Message = E.GetString();
            string Reply = $"Echo,{Message}";

            Console.WriteLine($"> (WebSocket) {Message}");
            Sender.Write(Reply);
            Console.WriteLine($"< (WebSocket) {Reply}");
        }

        private static void Router_Jint(Client Sender, RouteEventArgs E)
        {
            string FilePath = Path.Combine(WebDirectory, E.Request.Path.TrimStart('/'));

            Boolean Exists = File.Exists(FilePath);
            Boolean WithinDir = Path.GetFullPath(FilePath).Contains(WebDirectory);
            Boolean IsDir = Directory.Exists(FilePath);

            if (!(Exists && WithinDir && !IsDir))
            {
                Sender.Send(Response.SendStatus(400));
                return;
            }

            //Sender.Send(Response.SendFile(FilePath, false));
            string JS = File.ReadAllText(E.Request.Path);
            Jint.Engine Engine = new Jint.Engine(cfg => cfg.AllowClr(typeof(Server).Assembly).CatchClrExceptions());

            Engine.SetValue("Request", E.Request);
            Engine.SetValue("Response", new Response());
            /* try
             {
                 Engine.Execute(JS);
             }
             catch (Exception Ex)
             {
                 Console.WriteLine("Uncaught exception! " + Ex.Message);

                 Req.Client.Send(Response.SendText("500 - Internal Error").GetBytes());
             }*/
            Engine.Execute(JS);
        }

        private static void Router_GetPublic(Client Sender, RouteEventArgs E)
        {
            string FilePath = Path.Combine(WebDirectory, E.Request.Path.TrimStart('/'));
            string RelativePath = Path.GetFullPath(FilePath).Replace(WebDirectory, "").Replace("\\", "/");

            string DefaultHTML = @"
                <!DOCTYPE html>
                <html lang='en'>
                <head>
                    <title>{title}</title>
                    <style>{style}</style>
                </head>
                <body>{body}</body>
            </html>";
            string StyleHTML = "body {font-family: sans-serif;}";

            Boolean Exists = File.Exists(FilePath);
            Boolean WithinDir = Path.GetFullPath(FilePath).Contains(WebDirectory);
            Boolean IsDir = Directory.Exists(FilePath);

            // If request for a file and within a servable folder
            if (Exists && WithinDir && !IsDir)
            {
                Sender.Send(Response.SendFile(FilePath, false));
            }
            else if (!Exists && WithinDir && IsDir)
            {
                // If request for a directory, try finding index files
                string IndexPath = Path.GetFullPath(Path.Combine(FilePath, "index.html"));
                if (File.Exists(IndexPath))
                {
                    Sender.Send(Response.SendFile(IndexPath, false));
                }

                string Title = $"Directory Listing for {RelativePath}";
                string BodyHTML = $"<h1>{Title}</h1><ul>";

                List<string> Listing = Directory.EnumerateDirectories(FilePath).ToList();
                Listing.AddRange(Directory.EnumerateFiles(FilePath));
                Listing.Sort();

                foreach (string Item in Listing)
                {
                    char Seperator = Path.DirectorySeparatorChar;
                    string LocalPath = Path.GetFullPath(Item);
                    string Ref = LocalPath.Replace(WebDirectory, "");
                    if (Seperator.ToString() != "/")
                    {
                        Ref = Ref.Replace(Seperator.ToString(), "/");
                    }
                    string Name = LocalPath.Replace(FilePath, "");

                    if (Directory.Exists(Item))
                    {
                        Name += Seperator.ToString();
                    }
                    Name = Name.TrimStart(Seperator.ToString().ToCharArray());
                    BodyHTML += $"<li><a href=\"{Ref}\">{Name}</a></li>";
                }

                if (Listing.Count == 0)
                {
                    BodyHTML += "<p>This is an empty directory.</p>";
                }

                BodyHTML += "</ul>";

                Sender.Send(Response.SendHtml(DefaultHTML
                    .Replace("{title}", Title)
                    .Replace("{style}", StyleHTML)
                    .Replace("{body}", BodyHTML)
                ));
            }
            // If the file exists or doesn't, we should always forbid any access
            // when not within the servable directory
            else if (!WithinDir)
            {
                string Title = "403 - Forbidden";
                Response ResponseObj = Response.SendStatus(403);
                ResponseObj.SetHtml(
                   DefaultHTML
                    .Replace("{title}", Title)
                    .Replace("{style}", StyleHTML)
                    .Replace("{body}", $"<h1>{Title}</h1>")
                );

                Sender.Send(ResponseObj);
            }
            // If the file, or directory doesn't exist inside the servable directory
            else
            {
                // !Exists && !WithinDir, or !Exists && WithinDir (and !IsDir?)
                string Title = "404 - Not Found";
                Response ResponseObj = Response.SendStatus(404);
                ResponseObj.SetHtml(
                   DefaultHTML
                    .Replace("{title}", Title)
                    .Replace("{style}", StyleHTML)
                    .Replace("{body}", $"<h1>{Title}</h1>")
                );

                Sender.Send(ResponseObj);
            }
        }

        private static void ProcessWebServerResponse(Client Sender, Request Request)
        {
            Console.WriteLine(Request.Method + ' ' + Request.Path);
            // Traditional way of identifying a web server request with manual requests and no router
            // Sender.Send(Response.SendStatus(404));
        }

    }
}
