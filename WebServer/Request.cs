using System;
using System.Collections.Generic;
using System.Linq;

namespace JAWS
{
    public class Request
    {
        public Dictionary<String, String> Headers = new Dictionary<string, string>();
        public string Method;
        public string Path;
        public string QueryString;
        public string HttpVersion;
        public string Body;
        public HttpClient Client;

        public static Request Parse(HttpClient Client, string RawHttpMessage)
        {
            if (RawHttpMessage == "")
            {
                Client.Close();
                return null;
            }

            Request Req = new Request();
            List<string> HttpMessage = RawHttpMessage.Split(Char.Parse("\n")).ToList();

            string[] Action = HttpMessage[0].TrimEnd(Char.Parse("\r")).Split(Char.Parse(" "));
            if (Action.Length != 3)
            {
                Client.Close();
                return null;
                //throw new InvalidOperationException("Too many fields in HTTP1/1 spec?");
            }

            int Count = HttpMessage.Count;
            for (int i = 1; i < Count; i++)
            {
                if (HttpMessage[i] == "\r")
                {
                    HttpMessage.RemoveRange(0, i + 1);
                    break;
                }
                string[] Header = HttpMessage[i].TrimEnd(Char.Parse("\r")).Split(Char.Parse(":"));
                string key = Header[0];
                Header[0] = "";
                Req.Headers.Add(key, String.Join(":", Header).TrimStart(Char.Parse(":")).TrimStart(Char.Parse(" ")));
            }

            Req.Method = Action[0].ToUpper();
            string[] Path = Action[1].Split(Char.Parse("?"));
            Req.Path = Path[0];
            Path[0] = "";
            Req.QueryString = String.Join("?", Path).TrimStart(Char.Parse("?"));
            Path = null;
            Req.HttpVersion = Action[2];
            Action = null;
            Req.Body = String.Join("\n", HttpMessage.ToArray());
            HttpMessage = null;
            RawHttpMessage = null;
            Req.Client = Client;

            return Req;
        }

        /// <summary>
        /// Gets the header from the specified key, or null
        /// </summary>
        /// <param name="Key">The header to retrieve</param>
        /// <returns></returns>
        public string GetHeader(string Key)
        {
            string Value;
            if (Headers.TryGetValue(Key, out Value))
            {
                return Value;
            }
            else if (Headers.TryGetValue(Key.ToLower(), out Value))
            {
                return Value;
            }
            return null;
        }


    }
}
