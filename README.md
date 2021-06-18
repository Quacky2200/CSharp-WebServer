# CSharp WebServer
This is a portable C# web server library. It was first created within one WebServer.cs file in a few hours, and has been expanding and improving slowly over time.

You can:
- Serve a directory
- Experiment with the example demo
- Extend to serve API requests with JSON, XML, etc...
- Import this library to your program easily
- Use SSL HTTPS with a PFX certificate
- Use websockets
- Create routes

The example demo does have the start of a Jint instance which could become useful in various projects...

This project is not as configurable in comparison to Apache or Nginx, but it can be used in dotnet libraries without the need to use the HTTP listener (which requires admin privileges) - which was the main reason for this software.

Basic HTTPS support is now supported using an SslStream. KeepAlive was attempted but does not follow the protocol closely at this time.

Responses (text, files, etc) are streamed to the client than fully loaded into memory beforehand (the original commit).

Things Missing (To Do):
- Partial Request support
- Compression Mechanisms (Use GZipStream within System.IO.Compression)
- HTTP 2
- FastCGI support
- Configuration Options
- ASP.NET project support using HttpContext (without IIS/Kestrel)
- More Jint functionality with examples

**HTTP 2** From research into HTTP 2, it would be hard to include due to the use of SslStream which currently [does not allow custom application protocols to be added](https://github.com/dotnet/corefx/issues/4721) (e.g. h2 for HTTP2 and http/1.1). There is apparently a way to upgrade the connection to a HTTPS 2 connection using the 'Upgrade' header, and would require more time to design this kind of approach due to the main protocol difference between v1 (text) and v2 (binary streams).
