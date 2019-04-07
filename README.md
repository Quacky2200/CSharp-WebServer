# CSharp-WebServer (Mono)
This is a basic C# web server originally created for a portable dotnet library. I wrote the web server class within a couple of hours and slowly improved parts of it. I then decided it would be fun to try and make it serve files and potentially have the ability to create web scripts.

To extend functionality from serving files; I found that Jint could be a potentially useful library, and perhaps a later improvement could be some configuration options, as well as FastCGI support which could allow PHP to be used than just C#.

I had always wondered if I could create a HTTP server when I was in college (and now I have). Beforehand; I made a game that was to use sockets using my own UTP protocol.

This project is not as configurable in comparison to Apache or Nginx, but it can be used in dotnet libraries without the need to use the HTTP listener (which requires admin privileges) - which was the main reason for this software.

Basic HTTPS support is now supported using an SslStream. KeepAlive was attempted but does not follow the protocol closely and is currently buggy until more time is put towards making this as usable as possible. Changes we made to also send files using a Stream rather than placing it into a memory buffer (as was done first for prototyping).

Things Missing (To Do):
- Partial Request support
- Web Sockets
- Compression Mechanisms (Use GZipStream within System.IO.Compression)
- HTTP 2
- FastCGI support
- Configuration Options
- ASP.NET project support using HttpContext (without IIS/Kestrel)
- More Jint functionality with examples

**HTTP 2** From research into HTTP 2, it would be hard to include due to the use of SslStream which currently [does not allow custom application protocols to be added](https://github.com/dotnet/corefx/issues/4721) (e.g. h2 for HTTP2 and http/1.1). There is apparently a way to upgrade the connection to a HTTPS 2 connection using the 'Upgrade' header, and would require more time to design this kind of approach due to the main protocol difference between v1 (text) and v2 (binary streams).
