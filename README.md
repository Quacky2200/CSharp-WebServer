# CSharp-WebServer (Mono)
This is a basic C# web server originally created for a portable dotnet library. I wrote the web server class within a couple of hours and slowly improved parts of it. I then decided it would be fun to try and make it serve files and potentially have the ability to create web scripts. I found that Jint could be a potentially useful library.

I had always wondered if I could create a HTTP server when I was in college and made a game that was to use sockets but in the end created my own protocol but then never completed it. At least I don't have to continue wondering.

None of it is configurable as to Apache or Nginx, but it can be used in dotnet libraries without the need to use the HTTP listener (which requires admin privileges).

**Note** Whilst this web server understands a basic HTTP message, no compression mechanisms exist, no encryption (HTTPS), and no partial requests, nor web sockets are supported.
