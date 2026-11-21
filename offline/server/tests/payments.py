"""Integration checks against the disposable loopback service and PostgreSQL in CI."""
import concurrent.futures
import hashlib
import json
import subprocess
import time
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

BASE = "http://127.0.0.1:5017"

def sql(query):
    return subprocess.check_output(
        ["psql", "-XAt", "-v", "ON_ERROR_STOP=1", "-c", query], text=True
    ).strip()

def notify(invoice, amount):
    signature = hashlib.md5(f"{amount}:{invoice}:test-result".encode()).hexdigest()
    params = urllib.parse.urlencode(dict(InvId=invoice, OutSum=amount, SignatureValue=signature))
    try:
        with urllib.request.urlopen(BASE + "/payment/robokassa/result?" + params, timeout=20) as response:
            return response.status, response.read().decode()
    except urllib.error.HTTPError as error:
        return error.code, error.read().decode()

for attempt in range(60):
    try:
        urllib.request.urlopen(BASE + "/health", timeout=1).close()
        break
    except OSError:
        time.sleep(1)
else:
    raise RuntimeError("test server did not start")

request = urllib.request.Request(BASE + "/purchase/start",
    data=json.dumps({"email": "buyer@example.org"}).encode(),
    headers={"Content-Type": "application/json"})
with urllib.request.urlopen(request) as response:
    invoice = int(json.load(response)["invoiceId"])

for amount in ("498.99", "499.01"):
    assert notify(invoice, amount)[0] == 400
assert sql(f"SELECT status FROM payments WHERE invoice_id={invoice}") == "pending"
assert sql("SELECT count(*) FROM licenses") == "0"

# Keep the first delivery in flight while the other callbacks arrive.
sql("""CREATE FUNCTION delay_mark() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN PERFORM pg_sleep(0.4); RETURN NEW; END $$;
CREATE TRIGGER delay_delivery BEFORE UPDATE ON licenses
FOR EACH ROW EXECUTE FUNCTION delay_mark();""")
with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
    responses = list(pool.map(lambda _: notify(invoice, "499.00"), range(4)))
assert all(response == (200, f"OK{invoice}") for response in responses), responses
assert notify(invoice, "499.0000") == (200, f"OK{invoice}")
assert sql("SELECT count(*) FROM licenses WHERE email_sent_at IS NOT NULL") == "1"
assert sql(f"SELECT status FROM payments WHERE invoice_id={invoice}") == "paid"
assert Path("/tmp/license-regression.log").read_text().count("получил бы ключ") == 1
print("Payment amount, concurrent delivery and retry regressions passed.")
