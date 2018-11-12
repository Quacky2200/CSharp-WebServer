using System;
using System.Threading;
using System.Collections.Generic;
using System.Linq;

namespace WebServer
{
    abstract class Runnable
    {
        public abstract Boolean IsSuitable(String Path);
        public abstract void Run(String Path, HTTPServer.Request Req, HTTPServer.Response Res);
    }

    class JavascriptRunnable : Runnable
    {
        public override Boolean IsSuitable(String Path)
        {
            return (new System.IO.FileInfo(Path)).Extension == ".cjs";
        }

        public override void Run(string Path, HTTPServer.Request Req, HTTPServer.Response Res)
        {
            string JS = System.IO.File.ReadAllText(Path);
            Jint.Engine Engine = new Jint.Engine(cfg => cfg.AllowClr(typeof(HTTPServer).Assembly).CatchClrExceptions());
            Engine.SetValue("Request", Req);
            Engine.SetValue("Response", Res);
           /* try
            {
                Engine.Execute(JS);
            }
            catch (Exception Ex)
            {
                Console.WriteLine("Uncaught exception! " + Ex.Message);
                Req.Client.Send(HTTPServer.Response.SendText("500 - Internal Error").GetBytes());
            }*/
            Engine.Execute(JS);
        }
    }

    class MainClass
    {
        private const int PORT = 8888;
        private static HTTPServer WS = new HTTPServer(System.Net.IPAddress.Any, PORT);
        private static Boolean Quit = false;
        private static readonly String WebDirectory = System.IO.Directory.GetCurrentDirectory() + "/serve";
        private static readonly List<Runnable> Runnables = new List<Runnable> {
            new JavascriptRunnable()
        };
        private static readonly List<string> Indexers = new List<string> {
            "index.cjs",
            "index.html"
        };


        public static void Main(string[] args)
        {
            WS.OnRequest += new HTTPServer.OnRequestHandler(ProcessWebServerResponse);
            WS.Start();
            Console.WriteLine("Listening on port " + PORT);
            while (!Quit)
            {
                Thread.Sleep(1000);
            }
        }

        public static void ProcessWebServerResponse(HTTPServer sender, HTTPServer.Request Request)
        {
            string FilePath = WebDirectory + Request.Path;

            Console.WriteLine("Accessing: " + FilePath);

            Boolean Exists = System.IO.File.Exists(FilePath);
            Boolean WithinDir = IsWithinWebDirectory(FilePath);
            Boolean IsDir = System.IO.Directory.Exists(FilePath);
            Runnable Runnable = GetRunnable(FilePath);

            // If the file exists, within a servable folder and a runnable
            if (Exists && WithinDir && Runnable != null && !IsDir)
            {
                Runnable.Run(FilePath, Request, new HTTPServer.Response());
            }
            // If request for a file, within a servable folder and not a runnable
            else if (Exists && WithinDir && Runnable == null && !IsDir)
            {
                sender.Send(Request.Client, HTTPServer.Response.SendFile(FilePath, false));
            }
            // If request for a directory, try finding index files
            else if (!Exists && WithinDir && IsDir)
            {
                foreach (string Index in Indexers)
                {
                    string File = FilePath + Index;
                    Runnable = GetRunnable(File);
                    Exists = System.IO.File.Exists(File);
                    if (Exists && Runnable != null)
                    {
                        Runnable.Run(File, Request, new HTTPServer.Response());
                        return;
                    }
                    else if (Exists)
                    {
                        sender.Send(Request.Client, HTTPServer.Response.SendFile(File, false));
                        return;
                    }
                }
                string ListingHtml = "<!DOCTYPE html><html><style>{0}</style><body>{1}</body></html>";
                string StyleHtml = "body {font-family: sans-serif;}";
                string BodyHtml = "<ul>";
                List<string> Listing = System.IO.Directory.EnumerateDirectories(FilePath).ToList();
                Listing.AddRange(System.IO.Directory.EnumerateFiles(FilePath));
                Listing.Sort();
                BodyHtml += "<h1>Directory Listing for " + System.IO.Path.GetFullPath(FilePath).Replace(WebDirectory, "") + "</h1>";
                foreach (string Item in Listing)
                {
                    char Seperator = System.IO.Path.DirectorySeparatorChar;
                    string LocalPath = System.IO.Path.GetFullPath(Item);
                    string Ref = LocalPath.Replace(WebDirectory, "");
                    if (Seperator.ToString() != "/")
                    {
                        Ref = Ref.Replace(Seperator.ToString(), "/");
                    }
                    string Name = LocalPath.Replace(FilePath, "");
                    if (System.IO.Directory.Exists(Item))
                    {
                        Name += "/";
                    }
                    Name = Name.TrimStart("/".ToCharArray()[0]);
                    BodyHtml += "<li><a href=\"" + Ref + "\">" + Name + "</a></li>";
                }
                if (Listing.Count == 0)
                {
                    BodyHtml += "<p>This is an empty directory.</p>";
                }
                BodyHtml += "</ul>";
                ListingHtml = String.Format(ListingHtml, StyleHtml, BodyHtml);
                sender.Send(Request.Client, HTTPServer.Response.SendHtml(ListingHtml));
            }
            // If the file exists or doesn't, we should always forbid any access
            // when not within the servable directory
            else if (!WithinDir)
            {
                HTTPServer.Response Response = new HTTPServer.Response();
                Response.Status = 403;
                Response.SetText("403 - Forbidden");
                sender.Send(Request.Client, Response);
            }
            // If the file, or directory doesn't exist inside the servable directory
            else
            {
                // !Exists && !WithinDir, or !Exists && WithinDir (and !IsDir?)
                HTTPServer.Response Response = new HTTPServer.Response();
                Response.Status = 404;
                Response.SetText("404 - Not Found");
                sender.Send(Request.Client, Response);
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
