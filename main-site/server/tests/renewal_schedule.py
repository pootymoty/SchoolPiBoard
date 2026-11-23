from http.server import BaseHTTPRequestHandler, HTTPServer
import json
import subprocess
import threading
import time
from pathlib import Path

calls = []

class Keys(BaseHTTPRequestHandler):
    def do_POST(self):
        body = json.loads(self.rfile.read(int(self.headers["Content-Length"])))
        calls.append((self.path, body["userId"]))
        assert body["endsAt"] and body["previousInvoiceId"] == body["userId"]
        self.send_response(200)
        self.end_headers()
        self.wfile.write(b'{"sent":true}' if self.path.endswith("notice") else b'{"invoiceId":"999"}')

def sql(query):
    return subprocess.check_output(["psql", "-XAtq", "-v", "ON_ERROR_STOP=1", "-c", query], text=True).strip()

sql(Path("publish/sql/001_init.sql").read_text())
for uid, days, enabled in [(1,2,True),(2,0.5,True),(3,4,True),(4,-1,True),(5,2,False),(6,2,True)]:
    sql(f"""INSERT INTO subscriptions(id,external_user_id,plan,starts_at,ends_at,auto_renew,invoice_id)
    VALUES(gen_random_uuid(),{uid},'tutor_basic',now()-interval '29 days',now()+interval '{days} days',{enabled},{uid})""")
sql("UPDATE subscriptions SET renewal_notice_at=now() WHERE external_user_id=6")
stub = HTTPServer(("127.0.0.1", 5018), Keys)
threading.Thread(target=stub.serve_forever, daemon=True).start()
with subprocess.Popen(["dotnet", "SchoolPi.Tariffs.dll"], cwd="publish") as service:
    try:
        for _ in range(60):
            if sql("SELECT count(*) FROM payments WHERE invoice_id=999") == "1" and sql(
                "SELECT count(*) FROM subscriptions WHERE external_user_id=1 AND renewal_notice_at IS NOT NULL") == "1":
                break
            assert service.poll() is None
            time.sleep(1)
        assert sorted(calls) == [("/tariffs/recurring", 2), ("/tariffs/renewal-notice", 1)], calls
        assert sql("SELECT auto_renew FROM subscriptions WHERE external_user_id=1") == "t"
        assert sql("SELECT count(*) FROM payments WHERE invoice_id=999") == "1"
    finally:
        service.terminate()
print("Renewal notice window, charge window, disabled, expired and previously notified cases passed.")
