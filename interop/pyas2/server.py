"""
AS2 station built on pyas2lib for the interoperability tests of AS24Net.

It answers on one port:
  POST /as2       AS2 endpoint: messages of AS24Net (answered with a synchronous MDN or posting an asynchronous one)
                  and asynchronous MDNs for the messages sent from here
  POST /send      sends a message to AS24Net with the options in the JSON body and returns the outcome
  GET  /mdn?id=   the asynchronous MDN of a message sent from here, once it came
  GET  /received  the messages received, with the SHA-256 of their payload
  GET  /health    200 when running

Any AS2 name is accepted as ours and every station uses the same key pair (/certs/pyas2.pfx), so that the test
runner can use a partner per case without configuring anything here.
"""

import base64
import hashlib
import json
import logging
import os
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import parse_qs, urlparse

import requests
from pyas2lib import Mdn, Message, Organization, Partner

logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")
log = logging.getLogger("pyas2")

CERTS = os.environ.get("CERTS", "/certs")
KEY = open(os.path.join(CERTS, "pyas2.pfx"), "rb").read()
KEY_PASS = os.environ.get("KEY_PASS", "interop")
AS24NET_CERT = open(os.path.join(CERTS, "as24net.pem"), "rb").read()
AS24NET_URL = os.environ.get("AS24NET_URL", "http://as24net:8080/as2")
MDN_URL = os.environ.get("MDN_URL", "http://pyas2:8000/as2")

_lock = threading.Lock()
_orgs = {}
_sent = {}
_mdns = {}
_received = []


def organization(as2_name):
    with _lock:
        if as2_name not in _orgs:
            _orgs[as2_name] = Organization(as2_name=as2_name, sign_key=KEY, sign_key_pass=KEY_PASS,
                                           decrypt_key=KEY, decrypt_key_pass=KEY_PASS, mdn_url=MDN_URL)
        return _orgs[as2_name]


def as24net(as2_name, **options):
    # The certificate is self-signed, so it is trusted as it is.
    return Partner(as2_name=as2_name, verify_cert=AS24NET_CERT, encrypt_cert=AS24NET_CERT, validate_certs=False, **options)


def raw_request(headers, body):
    """pyas2lib parses the HTTP headers followed by the body."""
    head = "".join(f"{name}: {value}\r\n" for name, value in headers.items())
    return head.encode() + b"\r\n" + body


def is_mdn(headers, body):
    content_type = headers.get("Content-Type", "").lower()
    return "multipart/report" in content_type or (
        "multipart/signed" in content_type and b"disposition-notification" in body[:8000].lower())


def parse_mdn(raw):
    mdn = Mdn()
    status, detail = mdn.parse(raw, find_message_cb=lambda message_id, recipient: _sent.get(message_id))
    return mdn.orig_message_id, {"status": status, "detail": detail}


class Handler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def log_message(self, format, *args):
        log.info("%s %s", self.address_string(), format % args)

    def reply(self, status, body=b"", headers=None, content_type="application/json"):
        if isinstance(body, (dict, list)):
            body = json.dumps(body).encode()
        self.send_response(status)
        for name, value in (headers or {}).items():
            self.send_header(name, value)
        if not headers or "Content-Type" not in headers:
            self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def body(self):
        if "chunked" in self.headers.get("Transfer-Encoding", "").lower():
            data = b""
            while True:
                size = int(self.rfile.readline().split(b";")[0].strip(), 16)
                if size == 0:
                    self.rfile.readline()
                    return data
                data += self.rfile.read(size)
                self.rfile.readline()
        return self.rfile.read(int(self.headers.get("Content-Length", 0)))

    def do_GET(self):
        url = urlparse(self.path)
        if url.path == "/health":
            self.reply(200, {"status": "ok"})
        elif url.path == "/received":
            with _lock:
                self.reply(200, list(_received))
        elif url.path == "/mdn":
            message_id = parse_qs(url.query).get("id", [""])[0].strip("<>")
            with _lock:
                result = _mdns.get(message_id)
            self.reply(200 if result else 404, result or {"error": "no MDN yet"})
        else:
            self.reply(404, {"error": "not found"})

    def do_POST(self):
        url = urlparse(self.path)
        if url.path == "/as2":
            self.receive()
        elif url.path == "/send":
            self.send()
        else:
            self.reply(404, {"error": "not found"})

    def receive(self):
        body = self.body()
        headers = {name: value for name, value in self.headers.items()}
        raw = raw_request(headers, body)

        if is_mdn(headers, body):
            message_id, result = parse_mdn(raw)
            log.info("Asynchronous MDN for %s: %s", message_id, result)
            with _lock:
                _mdns[message_id] = result
            self.reply(200, b"", content_type="text/plain")
            return

        message = Message()
        status, exception, mdn = message.parse(
            raw,
            find_org_cb=organization,
            # Whatever AS24Net sends is accepted; the signature is verified when there is one.
            find_partner_cb=lambda as2_name: as24net(as2_name),
            find_message_cb=lambda message_id, partner_id: None)

        payload = message.content if status == "processed" and message.payload is not None else b""
        entry = {
            "messageId": message.message_id,
            "from": headers.get("AS2-From"),
            "to": headers.get("AS2-To"),
            "status": status,
            "error": str(exception[0]) if exception else None,
            "signed": bool(message.signed),
            "encrypted": bool(message.encrypted),
            "compressed": bool(message.compressed),
            "sha256": hashlib.sha256(payload).hexdigest() if payload else None,
            "size": len(payload),
        }
        log.info("Received %s", entry)
        with _lock:
            _received.append(entry)

        if mdn is None:
            self.reply(200, b"", content_type="text/plain")
        elif mdn.mdn_mode == "SYNC":
            self.reply(200, mdn.content, headers=dict(mdn.headers))
        else:
            self.reply(200, b"", content_type="text/plain")
            threading.Thread(target=post_mdn, args=(mdn,), daemon=True).start()

    def send(self):
        options = json.loads(self.body())
        partner = as24net(
            options["to"],
            sign=options.get("sign", False),
            digest_alg=options.get("digest", "sha256"),
            encrypt=options.get("encrypt", False),
            enc_alg=options.get("encryption", "aes_256_cbc"),
            compress=options.get("compress", False),
            mdn_mode=options.get("mdnMode"),
            mdn_digest_alg=options.get("mdnDigest"))
        message = Message(organization(options["from"]), partner)
        payload = base64.b64decode(options["payload"])
        message.build(payload, filename=options.get("fileName"), subject=options.get("subject", "AS2 interop test"),
                      content_type=options.get("contentType", "application/edi-consent"))
        with _lock:
            _sent[message.message_id] = message

        started = time.monotonic()
        response = requests.post(options.get("url", AS24NET_URL), data=message.content, headers=message.headers, timeout=60)
        result = {
            "messageId": message.message_id,
            "httpStatus": response.status_code,
            "seconds": round(time.monotonic() - started, 3),
            "mic": message.mic.decode() if isinstance(message.mic, bytes) else message.mic,
        }
        if options.get("mdnMode") == "SYNC" and response.content:
            _, result["mdn"] = parse_mdn(raw_request(response.headers, response.content))
        log.info("Sent %s", result)
        self.reply(200, result)


def post_mdn(mdn):
    try:
        response = requests.post(mdn.mdn_url, data=mdn.content, headers=dict(mdn.headers), timeout=60)
        log.info("Asynchronous MDN posted to %s: HTTP %s", mdn.mdn_url, response.status_code)
    except Exception:
        log.exception("Posting the asynchronous MDN to %s failed", mdn.mdn_url)


if __name__ == "__main__":
    port = int(os.environ.get("PORT", "8000"))
    log.info("pyas2lib station on port %s, AS24Net at %s", port, AS24NET_URL)
    ThreadingHTTPServer(("0.0.0.0", port), Handler).serve_forever()
