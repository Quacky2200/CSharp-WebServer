using System;
using System.Threading;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.IO;
using JAWS;

namespace WebServer
{
    abstract class Runnable
    {
        public abstract Boolean IsSuitable(String Path);
        public abstract void Run(String Path, Request Req, Response Res);
    }

    class JavascriptRunnable : Runnable
    {
        public override Boolean IsSuitable(String Path)
        {
            return (new System.IO.FileInfo(Path)).Extension == ".cjs";
        }

        public override void Run(string Path, Request Req, Response Res)
        {
            string JS = System.IO.File.ReadAllText(Path);
            Jint.Engine Engine = new Jint.Engine(cfg => cfg.AllowClr(typeof(HttpServer).Assembly).CatchClrExceptions());
            Engine.SetValue("Request", Req);
            Engine.SetValue("Response", Res);
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
    }

    class MainClass
    {
        private static int PORT = 8888;
        private static HttpServer WS;
        private static Boolean Quit = false;
        private static readonly String WebDirectory = Path.Combine(System.IO.Directory.GetCurrentDirectory(), "serve");
        private static readonly List<Runnable> Runnables = new List<Runnable> {
            new JavascriptRunnable()
        };
        private static readonly List<string> Indexers = new List<string> {
            "index.cjs",
            "index.html"
        };


        public static void Main(string[] args)
        {
            PORT = 443;
            WS = new HttpServer(new IPEndPoint(System.Net.IPAddress.Any, PORT));
            WS.SSLEnabled = true;
            WS.SSLCertificateFileName = "C:\\Users\\Matthew`\\Downloads\\15470771_cert.pfx";
            WS.OnRequest += new HttpServer.OnRequestHandler(ProcessWebServerResponse);
            WS.Start();
            Console.WriteLine("Listening on port " + PORT);
            while (!Quit)
            {
                Thread.Sleep(1000);
            }
        }

        public static void ProcessWebServerResponse(HttpClient sender, Request Request)
        {
            string FilePath = Path.Combine(WebDirectory, Request.Path.TrimStart('/'));

            Console.WriteLine(Request.Method + ' ' + Request.Path);

            Boolean Exists = File.Exists(FilePath);
            Boolean WithinDir = IsWithinWebDirectory(FilePath);
            Boolean IsDir = Directory.Exists(FilePath);
            Runnable Runnable = GetRunnable(FilePath);

            // If the file exists, within a servable folder and a runnable
            if (Exists && WithinDir && Runnable != null && !IsDir)
            {
                Runnable.Run(FilePath, Request, new Response());
            }
            // If request for a file, within a servable folder and not a runnable
            else if (Exists && WithinDir && Runnable == null && !IsDir)
            {
                sender.Send(Response.SendFile(FilePath, false));
            }
            // If request for a directory, try finding index files
            else if (!Exists && WithinDir && IsDir)
            {
                foreach (string Index in Indexers)
                {
                    string File = Path.Combine(FilePath, Index);
                    Runnable = GetRunnable(File);
                    Exists = System.IO.File.Exists(File);
                    if (Exists && Runnable != null)
                    {
                        Runnable.Run(File, Request, new Response());
                        return;
                    }
                    else if (Exists)
                    {
                        sender.Send(Response.SendFile(File, false));
                        return;
                    }
                }
                string ListingHtml = "<!DOCTYPE html><html><style>{0}</style><body>{1}</body></html>";
                string StyleHtml = "body {font-family: sans-serif;}";
                string BodyHtml = "<ul>";
                List<string> Listing = Directory.EnumerateDirectories(FilePath).ToList();
                Listing.AddRange(Directory.EnumerateFiles(FilePath));
                Listing.Sort();
                BodyHtml += "<h1>Directory Listing for " + Path.GetFullPath(FilePath).Replace(WebDirectory, "") + "</h1>";
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
                    BodyHtml += "<li><a href=\"" + Ref + "\">" + Name + "</a></li>";
                }
                if (Listing.Count == 0)
                {
                    BodyHtml += "<p>This is an empty directory.</p>";
                }
                BodyHtml += "</ul>";
                ListingHtml = String.Format(ListingHtml, StyleHtml, BodyHtml);
                sender.Send(Response.SendHtml(ListingHtml));
            }
            // If the file exists or doesn't, we should always forbid any access
            // when not within the servable directory
            else if (!WithinDir)
            {
                sender.Send(Response.SendStatus(403));
            }
            // If the file, or directory doesn't exist inside the servable directory
            else
            {
                // !Exists && !WithinDir, or !Exists && WithinDir (and !IsDir?)
                sender.Send(Response.SendStatus(404));
            }
        }

        public static Runnable GetRunnable(string Path)
        {
            foreach (Runnable Runner in Runnables)
            {
                if (Runner.IsSuitable(Path))
                {
                    return Runner;
                }
            }
            return null;
        }

        public static Boolean IsWithinWebDirectory(string Path)
        {
            return System.IO.Path.GetFullPath(Path).Contains(WebDirectory);
        }
    }
}
