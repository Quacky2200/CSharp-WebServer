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
using System.IO;
using System.Linq;
using System.Text;

namespace WebServer.HTTP
{
    public class Response
    {
        public int Status = 200;
        private string StatusName = "";
        public string ContentType = "text/plain";
        public Stream Content = new MemoryStream();

        public static Dictionary<int, string> StatusCodes = new Dictionary<int, string>
        {
            [100] = "Continue",
            [101] = "Switching Protocols",
            [102] = "Processing",
            [200] = "OK",
            [201] = "Created",
            [202] = "Accepted",
            [203] = "Non-Authoritative Information",
            [204] = "No Content",
            [205] = "Reset Content",
            [206] = "Partial Content",
            [207] = "Multi-Status",
            [208] = "Already Reported",
            [226] = "IM Used",
            [300] = "Multiple Choices",
            [301] = "Moved Permanently",
            [302] = "Found",
            [303] = "See Other",
            [304] = "Not Modified",
            [305] = "Use Proxy",
            [306] = "Switch Proxy",
            [307] = "Temporary Redirect",
            [308] = "Permanent Redirect",
            [400] = "Bad Request",
            [401] = "Unauthorized",
            [402] = "Payment Required",
            [403] = "Forbidden",
            [404] = "Not Found",
            [405] = "Method Not Allowed",
            [406] = "Not Acceptable",
            [407] = "Proxy Authentication Required",
            [408] = "Request Timeout",
            [409] = "Conflict",
            [410] = "Gone",
            [411] = "Length Required",
            [412] = "Precondition Failed",
            [413] = "Request Entity Too Large",
            [414] = "Request-URI Too Long",
            [415] = "Unsupported Media Type",
            [416] = "Requested Range Not Satisfiable",
            [417] = "Expectation Failed",
            [418] = "I\"m a teapot",
            [419] = "Authentication Timeout",
            [420] = "Enhance Your Calm",
            [420] = "Method Failure",
            [422] = "Unprocessable Entity",
            [423] = "Locked",
            [424] = "Failed Dependency",
            [425] = "Unordered Collection",
            [426] = "Upgrade Required",
            [428] = "Precondition Required",
            [429] = "Too Many Requests",
            [431] = "Request Header Fields Too Large",
            [444] = "No Response",
            [449] = "Retry With",
            [450] = "Blocked by Windows Parental Controls",
            [451] = "Redirect",
            [451] = "Unavailable For Legal Reasons",
            [494] = "Request Header Too Large",
            [495] = "Cert Error",
            [496] = "No Cert",
            [497] = "HTTP to HTTPS",
            [499] = "Client Closed Request",
            [500] = "Internal Server Error",
            [501] = "Not Implemented",
            [502] = "Bad Gateway",
            [503] = "Service Unavailable",
            [504] = "Gateway Timeout",
            [505] = "HTTP Version Not Supported",
            [506] = "Variant Also Negotiates",
            [507] = "Insufficient Storage",
            [508] = "Loop Detected",
            [509] = "Bandwidth Limit Exceeded",
            [510] = "Not Extended",
            [511] = "Network Authentication Required",
            [598] = "Network read timeout error",
            [599] = "Network connect timeout error",
        };

        public Dictionary<string, dynamic> Headers = new Dictionary<string, dynamic>();

        public byte[] GetHeaderBytes()
        {
            ResolveStatus();
            string HttpMessage = String.Format("{0} {1} {2}", "HTTP/1.1", Status, StatusName) + "\r\n";
            List<string> HeaderStrings = new List<string>();
            List<string> HeaderKeys = Headers.Keys.ToList();

            if (Content.Length > 0)
            {
                //Headers.Add("Content-Length", Content.Length);
                
                if (!Headers.ContainsKey("Content-Type"))
                {
                    Headers.Add("Content-Type", ContentType);
                }

                if (!Headers.ContainsKey("Content-Disposition"))
                {
                    Headers.Add("Content-Disposition", "inline");
                }
            }

            // Headers.Add("Connection", "close");
            foreach (KeyValuePair<string, dynamic> KVP in Headers)
            {
                HeaderStrings.Add(KVP.Key + ": " + KVP.Value + "\r\n");
            }
            HttpMessage += String.Join("", HeaderStrings) + "\r\n";
            List<byte> HttpBytes = Encoding.UTF8.GetBytes(HttpMessage).ToList();
            //HttpBytes.AddRange(Content);
            return HttpBytes.ToArray();
        }

        protected void ResolveStatus()
        {
            
            if (!StatusCodes.ContainsKey(Status))
            {
                Status = 500;
                StatusName = StatusCodes[500];
                Content = new MemoryStream(Encoding.ASCII.GetBytes(StatusName));
                // No need to add more as of yet.
                //Debug.WriteLine("Invalid server HTTP code");
            }
            else
            {
                StatusName = StatusCodes[Status];
            }
        }

        public void LoadFile(String FilePath)
        {
            if (File.Exists(FilePath))
            {
                // Efficiently send a file using a stream!
                Content = File.OpenRead(FilePath); //.ReadAllBytes(FilePath);
                FileInfo info = new FileInfo(FilePath);
                if (info.Extension != "")
                {
                    ContentType = MimeTypes.GetMimeType(info.Extension);
                }
                else
                {
                    ContentType = "application/octet-stream";
                }
            }
            else
            {
                throw new FileNotFoundException("File not found at path: " + FilePath);
            }
        }

        public void SetText(String Text)
        {
            Content = new MemoryStream(Encoding.UTF8.GetBytes(Text));
        }

        public void SetHtml(String Html)
        {
            Content = new MemoryStream(Encoding.UTF8.GetBytes(Html));
            ContentType = "text/html";
        }
        
        public void SetContent(byte[] Bytes)
        {
            Content = new MemoryStream(Bytes);
        }

        public static Response SendFile(String FilePath, Boolean ForceDownload = true)
        {
            Response Result = new Response();
            Result.LoadFile(FilePath);
            if (ForceDownload == true)
            {
                FileInfo info = new FileInfo(FilePath);
                Result.Headers.Add("Content-Disposition", "attachment; filename=" + info.Name);
            }
            return Result;
        }

        public static Response SendText(String Text)
        {
            Response Result = new Response();
            Result.SetText(Text);
            return Result;
        }

        public static Response SendHtml(String Text)
        {
            Response Result = new Response();
            Result.SetHtml(Text);
            return Result;
        }

        public static Response Redirect(String Location, Boolean Permanent = false)
        {
            Response R = new Response();
            if (File.Exists(Location))
            {
                throw new Exception("Will not redirect to a filepath!");
            }
            R.Headers.Clear();
            R.Headers.Add("Location", Location);
            R.Status = (Permanent ? 301 : 302);
            R.ResolveStatus();
            return R;
        }

        public static Response SendStatus(int Status)
        {
            Response Response = new Response();
            Response.Status = Status;
            Response.ResolveStatus();
            Response.SetText(String.Format("{0} - {1}", Status, Response.StatusCodes[Status]));
            return Response;
        }

    }
}
