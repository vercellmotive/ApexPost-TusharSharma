"""Drives the real console harness through two scripted sessions and saves what it printed.

Usage: python3 make_evidence.py <harness .dll> <output folder> <compiled render tool>

It writes the full transcripts (.txt) and renders excerpts to PNG with render.swift. The PNGs are drawings of the
captured console text, not window captures.
"""
import os
import re
import shutil
import subprocess
import sys
import threading
import time

DLL, OUT, RENDER = sys.argv[1:4]
MENU_HEADER = "=== ApexPost courier offline harness ==="


class Session:
    def __init__(self, data_dir):
        shutil.rmtree(data_dir, ignore_errors=True)
        self.data_dir = data_dir
        self.proc = subprocess.Popen(
            ["dotnet", DLL, "--data", data_dir],
            stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, bufsize=1)
        self.lines, self.ids = [], []
        self.reader = threading.Thread(target=self._read, daemon=True)
        self.reader.start()
        time.sleep(2.5)

    def _read(self):
        for raw in self.proc.stdout:
            line = raw.rstrip("\n")
            self.lines.append(line)
            found = re.search(r"Saved (scan::[0-9a-f-]{36}) on this device", line)
            if found:
                self.ids.append(found.group(1))

    def act(self, *inputs, settle=0.9):
        """Types the given lines, waits for the output to settle and returns what was printed, without the next menu."""
        start = len(self.lines)
        for text in inputs:
            text = re.sub(r"\{ID:(\d)\}", lambda m: self.ids[int(m.group(1)) - 1], text)
            self.proc.stdin.write(text + "\n")
            self.proc.stdin.flush()
            time.sleep(0.08)
        time.sleep(settle)
        printed = self.lines[start:]
        if MENU_HEADER in printed:
            cut = printed.index(MENU_HEADER)
            printed = printed[:cut - 1] if cut > 0 and printed[cut - 1] == "" else printed[:cut]
        return printed

    def finish(self):
        self.proc.stdin.close()
        self.proc.wait(timeout=30)
        self.reader.join(5)


def render(name, title, lines):
    source = os.path.join(OUT, name + ".txt.tmp")
    with open(source, "w") as handle:
        handle.write("\n".join(lines) + "\n")
    subprocess.run([RENDER, source, os.path.join(OUT, name + ".png"), title], check=True)
    os.remove(source)


os.makedirs(OUT, exist_ok=True)

# Session A: the three SQL++ queries, the outbox and a failed upload.
a = Session("/tmp/apexpost-demo-a")
a.act("1", "APX-2026-001", "1", "C-1001", "Sydney NSW", "Picked up at depot", settle=1.2)
time.sleep(1.1)
a.act("1", "APX-2026-001", "2", "C-1001", "Parramatta NSW", "Loaded on van 12", settle=1.2)
time.sleep(1.1)
a.act("1", "APX-2026-002", "1", "C-1001", "Sydney NSW", "[fail] torn label", settle=1.2)
time.sleep(1.1)
add4 = a.act("1", "APX-2026-001", "3", "C-1001", "Penrith NSW", "Left with neighbour", settle=1.2)
time.sleep(1.1)
add5 = a.act("1", "APX-2026-003", "1", "C-2002", "Melbourne VIC", "", settle=1.2)
find = a.act("2", "APX-2026-001")
pending = a.act("3")
sync = a.act("4", settle=1.6)
pending_after = a.act("3")
find_failed = a.act("2", "APX-2026-002")
a.act("6")
a.finish()
with open(os.path.join(OUT, "query-results.txt"), "w") as handle:
    handle.write("\n".join(a.lines) + "\n")

render("query-1-scans-for-a-tracking-number", "Option 2: Find by Tracking Number  (SQL++ with $trackingNumber)", find)
render("query-2-pending-scans", "Option 3: List Pending  (SQL++ with $syncStatus)", pending)
render("query-3-scans-per-scan-type", "Option 1: Add Scan, then the count per scanType for the courier  (SQL++ GROUP BY)", add4 + [""] + add5)
render("query-4-sync-and-failed-scan", "Option 4: Simulate Sync, then List Pending and Find again", sync + [""] + pending_after + [""] + find_failed)

# Session B: the change listener log and the live pending count.
b = Session("/tmp/apexpost-demo-b")
parts = []
parts += b.act("1", "APX-2026-010", "1", "C-1001", "Sydney NSW", "", settle=1.2)
parts += [""] + b.act("1", "APX-2026-010", "2", "C-1001", "Parramatta NSW", "", settle=1.2)
parts += [""] + b.act("1", "APX-2026-011", "1", "C-1001", "Sydney NSW", "[fail] torn label", settle=1.2)
parts += [""] + b.act("4", settle=1.6)
parts += [""] + b.act("5", "{ID:1}", settle=1.2)
parts += [""] + b.act("6")
b.finish()
with open(os.path.join(b.data_dir, "listener.log")) as handle:
    log_file = handle.read().rstrip("\n").split("\n")
with open(os.path.join(OUT, "listener-log.txt"), "w") as handle:
    handle.write("\n".join(b.lines) + "\n\n--- contents of listener.log in the data folder ---\n" + "\n".join(log_file) + "\n")
render("listener-log", "Change listener: changed document ids and the live pending count", parts + ["", "--- contents of listener.log ---"] + log_file)

# The query plans: which index each statement uses.
explain = subprocess.run(["dotnet", DLL, "--data", "/tmp/apexpost-demo-x", "--explain"], capture_output=True, text=True).stdout.rstrip("\n").split("\n")
with open(os.path.join(OUT, "query-plans.txt"), "w") as handle:
    handle.write("\n".join(explain) + "\n")
render("query-plans", "Value indexes in use: harness --explain", explain)
shutil.rmtree("/tmp/apexpost-demo-x", ignore_errors=True)
print("done")
