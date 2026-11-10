#!/usr/bin/env python3
"""Локальный сервер веб-сборки Hakaton: раздаёт эту папку и открывает игру в браузере."""
import http.server
import os
import socket
import socketserver
import sys
import threading
import webbrowser

ROOT = os.path.dirname(os.path.abspath(__file__))
TYPES = {
    ".html": "text/html; charset=utf-8", ".js": "application/javascript", ".wasm": "application/wasm",
    ".data": "application/octet-stream", ".json": "application/json", ".css": "text/css",
    ".png": "image/png", ".jpg": "image/jpeg", ".ico": "image/x-icon", ".svg": "image/svg+xml",
}


def stripped(path):
    for suffix in (".unityweb", ".gz", ".br"):
        if path.endswith(suffix):
            path = path[: -len(suffix)]
    return path


class Handler(http.server.SimpleHTTPRequestHandler):
    def __init__(self, *args, **kwargs):
        super().__init__(*args, directory=ROOT, **kwargs)

    def guess_type(self, path):
        return TYPES.get(os.path.splitext(stripped(path))[1].lower()) or super().guess_type(path)

    def end_headers(self):
        # Compressed parts of the build: the browser unpacks them itself (faster than the loader's fallback).
        path = self.translate_path(self.path.split("?")[0])
        if path.endswith(".br"):
            self.send_header("Content-Encoding", "br")
        elif path.endswith(".gz") or (path.endswith(".unityweb") and self.gzipped(path)):
            self.send_header("Content-Encoding", "gzip")
        self.send_header("Cache-Control", "no-cache")
        super().end_headers()

    @staticmethod
    def gzipped(path):
        try:
            with open(path, "rb") as f:
                return f.read(2) == b"\x1f\x8b"
        except OSError:
            return False

    def log_message(self, *args):
        pass


class Server(socketserver.ThreadingMixIn, http.server.HTTPServer):
    daemon_threads = True
    allow_reuse_address = True


def free_port(start):
    for port in range(start, start + 50):
        with socket.socket() as s:
            try:
                s.bind(("127.0.0.1", port))
                return port
            except OSError:
                continue
    sys.exit("Нет свободного порта")


port = free_port(int(sys.argv[1]) if len(sys.argv) > 1 else 8800)
server = Server(("127.0.0.1", port), Handler)
url = f"http://localhost:{port}/"
print(f"Игра: {url}")
print("Чтобы остановить сервер, закрой это окно или нажми Ctrl+C.")
threading.Timer(0.6, lambda: webbrowser.open(url)).start()
try:
    server.serve_forever()
except KeyboardInterrupt:
    pass
