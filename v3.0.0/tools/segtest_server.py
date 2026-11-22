import os, re, sys
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

ROOT = sys.argv[1]
PORT = int(sys.argv[2])
NO_RANGE = len(sys.argv) > 3 and sys.argv[3] == "norange"

class H(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def log_message(self, *a):
        pass

    def _file(self):
        name = self.path.split("?")[0].lstrip("/")
        p = os.path.join(ROOT, name)
        return p if os.path.isfile(p) else None

    def do_HEAD(self):
        p = self._file()
        if not p:
            self.send_response(404); self.send_header("Content-Length", "0"); self.end_headers(); return
        size = os.path.getsize(p)
        self.send_response(200)
        self.send_header("Content-Length", str(size))
        self.send_header("Accept-Ranges", "bytes" if not NO_RANGE else "none")
        self.send_header("ETag", '"test-etag-%d"' % size)
        self.end_headers()

    def do_GET(self):
        p = self._file()
        if not p:
            self.send_response(404); self.send_header("Content-Length", "0"); self.end_headers(); return
        size = os.path.getsize(p)
        rng = self.headers.get("Range")
        if rng and not NO_RANGE:
            m = re.match(r"bytes=(\d+)-(\d*)", rng.strip())
            if not m:
                self.send_response(416); self.send_header("Content-Length", "0"); self.end_headers(); return
            start = int(m.group(1))
            end = int(m.group(2)) if m.group(2) else size - 1
            if start >= size:
                self.send_response(416); self.send_header("Content-Length", "0"); self.end_headers(); return
            end = min(end, size - 1)
            length = end - start + 1
            self.send_response(206)
            self.send_header("Content-Type", "application/octet-stream")
            self.send_header("Content-Length", str(length))
            self.send_header("Content-Range", "bytes %d-%d/%d" % (start, end, size))
            self.send_header("Accept-Ranges", "bytes")
            self.send_header("ETag", '"test-etag-%d"' % size)
            self.end_headers()
            with open(p, "rb") as f:
                f.seek(start)
                left = length
                while left > 0:
                    buf = f.read(min(1 << 20, left))
                    if not buf:
                        break
                    try:
                        self.wfile.write(buf)
                    except Exception:
                        return
                    left -= len(buf)
            return
        self.send_response(200)
        self.send_header("Content-Type", "application/octet-stream")
        self.send_header("Content-Length", str(size))
        self.send_header("Accept-Ranges", "bytes" if not NO_RANGE else "none")
        self.send_header("ETag", '"test-etag-%d"' % size)
        self.end_headers()
        with open(p, "rb") as f:
            while True:
                buf = f.read(1 << 20)
                if not buf:
                    break
                try:
                    self.wfile.write(buf)
                except Exception:
                    return

ThreadingHTTPServer(("127.0.0.1", PORT), H).serve_forever()
