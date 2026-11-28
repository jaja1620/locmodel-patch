# -*- coding: utf-8 -*-
"""本地回归用静态服务：支持 Range(206)、Content-Length、HEAD。
用法: python range_server.py <服务根目录> <端口> <记录文件>
所有请求路径按 <根目录>/<path> 映射，不做目录列举。
"""
import os
import sys
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

ROOT = os.path.abspath(sys.argv[1])
PORT = int(sys.argv[2])
LOG = sys.argv[3]
_lock = threading.Lock()


def log(msg):
    with _lock:
        with open(LOG, "a", encoding="utf-8") as f:
            f.write("%s %s\n" % (time.strftime("%H:%M:%S"), msg))


class H(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def _resolve(self):
        p = self.path.split("?", 1)[0]
        p = p.lstrip("/")
        full = os.path.abspath(os.path.join(ROOT, p))
        if not full.startswith(ROOT):
            return None
        return full

    def _size(self, full):
        try:
            return os.path.getsize(full)
        except OSError:
            return -1

    def do_HEAD(self):
        full = self._resolve()
        n = self._size(full) if full and os.path.isfile(full) else -1
        log("HEAD %s -> %s" % (self.path, n))
        if n < 0:
            self.send_response(404)
            self.send_header("Content-Length", "0")
            self.end_headers()
            return
        self.send_response(200)
        self.send_header("Content-Length", str(n))
        self.send_header("Accept-Ranges", "bytes")
        self.end_headers()

    def do_GET(self):
        full = self._resolve()
        n = self._size(full) if full and os.path.isfile(full) else -1
        if n < 0:
            log("GET404 %s" % self.path)
            self.send_response(404)
            self.send_header("Content-Length", "0")
            self.end_headers()
            return
        rng = self.headers.get("Range")
        start, end = 0, n - 1
        partial = False
        if rng and rng.startswith("bytes="):
            seg = rng[6:].split(",")[0]
            a, _, b = seg.partition("-")
            try:
                if a.strip():
                    start = int(a)
                    if b.strip():
                        end = min(int(b), n - 1)
                    partial = True
                elif b.strip():  # bytes=-N
                    start = max(0, n - int(b))
                    end = n - 1
                    partial = True
            except ValueError:
                partial = False
        if start >= n:
            self.send_response(416)
            self.send_header("Content-Range", "bytes */%d" % n)
            self.send_header("Content-Length", "0")
            self.end_headers()
            return
        length = end - start + 1
        log("GET %s range=%s -> %d..%d/%d" % (self.path, rng, start, end, n))
        self.send_response(206 if partial else 200)
        self.send_header("Content-Length", str(length))
        self.send_header("Accept-Ranges", "bytes")
        if partial:
            self.send_header("Content-Range", "bytes %d-%d/%d" % (start, end, n))
        self.end_headers()
        with open(full, "rb") as f:
            f.seek(start)
            left = length
            while left > 0:
                chunk = f.read(min(262144, left))
                if not chunk:
                    break
                try:
                    self.wfile.write(chunk)
                except (BrokenPipeError, ConnectionResetError):
                    return
                left -= len(chunk)

    def log_message(self, fmt, *a):
        pass


if __name__ == "__main__":
    os.makedirs(ROOT, exist_ok=True)
    srv = ThreadingHTTPServer(("127.0.0.1", PORT), H)
    srv.daemon_threads = True
    log("listen %d root=%s" % (PORT, ROOT))
    srv.serve_forever()
