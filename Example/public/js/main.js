(document.onreadystatechange = function () {
    if (document.readyState != "complete") return;

    var address = 'ws://' + location.host.toString() + location.pathname.toString();
    var socket = new WebSocket(address);

    var cursor = document.createElement("div");
    cursor.style.position = "fixed";
    cursor.style.top = "0px";
    cursor.style.left = "0px";
    document.body.appendChild(cursor);

    socket.onopen = function () {
        console.log("Web socket connected");
    };

    socket.onmessage = function (e) {
        // e.data
        if (e.data.indexOf('Cursor') > -1) {
            cursor.textContent = "Websocket: " + e.data;
        } else {
            console.log("Socket said: " + e.data.toString());
        }
        //console.log("Web socket message event:", e);
    };

    socket.onerror = function (e) {
        console.error("Unable to talk to web socket");
    };

    socket.onclose = function (e) {
        console.log("Web socket closed");
    };

    setInterval(function () {
        socket.send("Hello World");
    }, 5e3);
})();