"""Exercise both shops through their signed endpoints and a loopback Robokassa stub."""
import hashlib
import hmac
import json
import subprocess
import threading
import time
from datetime import datetime, timedelta, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib import error, parse, request

states = {}
checked = set()
reject = False

class Provider(BaseHTTPRequestHandler):
    def do_POST(self):
        form = parse.parse_qs(self.rfile.read(int(self.headers["Content-Length"])).decode())
        body = "недостаточно средств" if reject else "OK" + form["InvoiceID"][0]
        self.send_response(200)
        self.end_headers()
        self.wfile.write(body.encode())

    def do_GET(self):
        query = parse.parse_qs(parse.urlsplit(self.path).query)
        invoice = query["InvoiceID"][0]
        expected = hashlib.md5(f'{query["MerchantLogin"][0]}:{invoice}:test-result'.encode()).hexdigest()
        assert query["Signature"][0].lower() == expected
        state = states[invoice]
        checked.add(invoice)
        self.send_response(200)
        self.end_headers()
        self.wfile.write(f"<OperationStateResponse><Result><Code>0</Code></Result><State><Code>{state}</Code></State></OperationStateResponse>".encode())

def sql(query):
    return subprocess.check_output(["psql", "-XAtq", "-v", "ON_ERROR_STOP=1", "-c", query], text=True).strip()

def call(route, payload):
    data = json.dumps(payload).encode()
    ts = str(int(time.time()))
    sig = hmac.new(b"test-service", ts.encode() + b"." + data, hashlib.sha256).hexdigest()
    req = request.Request("http://127.0.0.1:5017/" + route, data=data,
        headers={"Content-Type": "application/json", "X-Timestamp": ts, "X-Signature": sig})
    try:
        with request.urlopen(req) as reply:
            return reply.status, json.load(reply)
    except error.HTTPError as exc:
        return exc.code, None

def period(days):
    return (datetime.now(timezone.utc) + timedelta(days=days)).isoformat()

server = ThreadingHTTPServer(("127.0.0.1", 5018), Provider)
threading.Thread(target=server.serve_forever, daemon=True).start()
for uid, (shop, scenario) in enumerate([("board", "immediate"), ("tariffs", "late"),
                                        ("board", "pending"), ("tariffs", "paid"),
                                        ("board", "late")], start=10):
    payload = dict(userId=uid, planCode="test", planName="Тест", days=30, amount=100,
                   autoRenew=True, email=f"buyer{uid}@example.org")
    status, result = call(shop + "/invoice", payload)
    assert status == 200, (shop, status)
    parent = int(result["invoiceId"])
    sql(f"UPDATE payments SET status='paid' WHERE invoice_id={parent}")
    payload.update(previousInvoiceId=parent, endsAt=period(2))
    if shop == "tariffs" and scenario == "late":
        for _ in range(2):
            assert call("tariffs/renewal-notice", payload)[0] == 200
    payload["endsAt"] = period(0.5)
    reject = scenario == "immediate"
    for _ in range(2 if reject else 1):
        status, result = call(shop + "/recurring", payload)
        if reject:
            assert status == 402
        else:
            assert status == 200
            states[result["invoiceId"]] = {"late": 10, "pending": 5, "paid": 100}[scenario]

sql("UPDATE payments SET next_status_check=now() WHERE renewal_end IS NOT NULL")
for _ in range(90):
    log = Path("/tmp/license-regression.log").read_text()
    if checked == set(states) and log.count("— автопродление не прошло.") == 3:
        break
    time.sleep(1)
assert checked == set(states), (checked, states)
assert log.count("— автопродление не прошло.") == 3
assert log.count("— предстоящее продление тарифа.") == 1
assert "https://school-pi.online/billing/my" in log
assert "https://board.school-pi.online/plan" in log
assert "Причина: недостаточно средств" in log
assert "По этому счёту повторного списания не будет" in log
assert int(sql("SELECT count(*) FROM payments WHERE failure_notice_end IS NOT NULL")) == 3
print("Advance, immediate, late, pending, successful and duplicate notice checks passed.")
