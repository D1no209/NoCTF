import os
import select
import socket
import socketserver


class CallbackProxy(socketserver.BaseRequestHandler):
    def handle(self):
        with socket.create_connection(("host.docker.internal", int(os.environ["API_PORT"])), timeout=10) as upstream:
            upstream.settimeout(None)
            peers = {self.request: upstream, upstream: self.request}
            while True:
                readable, _, _ = select.select(list(peers), [], [], 30)
                if not readable:
                    return
                for source in readable:
                    data = source.recv(65536)
                    if not data:
                        return
                    peers[source].sendall(data)


with socketserver.ThreadingTCPServer(("0.0.0.0", int(os.environ["API_PORT"])), CallbackProxy) as server:
    server.serve_forever()
