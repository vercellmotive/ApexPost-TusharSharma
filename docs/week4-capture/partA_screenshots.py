#!/usr/bin/env python3
"""Takes real screenshots of the Part A harness running in this macOS Terminal window.

Run it from the repository root, in Terminal (not inside an IDE):

    python3 docs/week4-capture/partA_screenshots.py

It builds ApexPost.Courier.Offline.Harness, then runs it several times. Each run starts on a cleared screen,
and the answers are typed into the harness through a pseudo-terminal, character by character, exactly as if
you typed them, so the window shows a real session. At each checkpoint it saves a picture of this Terminal
window with macOS `screencapture`. Nothing is drawn or edited afterwards.

The first time, macOS asks to let Terminal record the screen (System Settings > Privacy & Security >
Screen & System Audio Recording). Allow it, quit and reopen Terminal, and run the script again.

The pictures go to docs/week4-partA/screenshots/, with a plain-text transcript of everything shown.
"""
import argparse
import os
import pty
import re
import select
import shutil
import subprocess
import sys
import time

CLEAR = "\033[2J\033[3J\033[H"
DIM = "\033[2m"
RESET = "\033[0m"
ANSI = re.compile(r"\x1b\[[0-9;?]*[A-Za-z]")
SCAN_ID = re.compile(r"Saved (scan::[0-9a-f-]{36}) on this device")

transcript = []


def show(text):
    """Writes to this Terminal window and keeps a plain copy for the transcript."""
    sys.stdout.write(text)
    sys.stdout.flush()
    transcript.append(ANSI.sub("", text).replace("\r", ""))


def osascript(script):
    return subprocess.run(["osascript", "-e", script], capture_output=True, text=True)


def fit_terminal_window():
    """Makes this Terminal window large, so a whole step fits in one picture."""
    desktop = osascript('tell application "Finder" to get bounds of window of desktop').stdout.strip()
    try:
        _, _, width, height = [int(value) for value in desktop.split(",")]
    except ValueError:
        width, height = 1440, 900
    right = min(width - 20, 1400)
    bottom = height - 20
    osascript(f'tell application "Terminal" to set bounds of front window to {{20, 40, {right}, {bottom}}}')


def capture(path):
    """Saves a picture of the front Terminal window. Falls back to its screen rectangle."""
    time.sleep(0.8)  # let Terminal finish drawing
    window_id = osascript('tell application "Terminal" to id of front window').stdout.strip()
    if window_id.isdigit():
        result = subprocess.run(["screencapture", "-x", "-o", "-l", window_id, path])
        if result.returncode == 0 and os.path.exists(path):
            return
    bounds = osascript('tell application "Terminal" to get bounds of front window').stdout.strip()
    left, top, right, bottom = [int(value) for value in bounds.split(",")]
    subprocess.run(["screencapture", "-x", "-R", f"{left},{top},{right - left},{bottom - top}", path], check=True)


class Harness:
    """One run of the real harness in a pseudo-terminal, so typed input is echoed like a keyboard session."""

    def __init__(self, command, data_dir, typing_delay):
        self.typing_delay = typing_delay
        self.output = ""
        show(f"$ {' '.join(command + ['--data', data_dir])}\n")
        self.pid, self.fd = pty.fork()
        if self.pid == 0:
            os.execvp(command[0], command + ["--data", data_dir])
        self.pump(quiet=1.5, limit=60)  # start-up and the first menu

    def pump(self, quiet=1.0, limit=15.0):
        """Copies the harness output to this window until it has been quiet for `quiet` seconds."""
        deadline = time.time() + limit
        last = time.time()
        while time.time() < deadline:
            ready, _, _ = select.select([self.fd], [], [], 0.1)
            if ready:
                try:
                    data = os.read(self.fd, 4096)
                except OSError:
                    return
                if not data:
                    return
                text = data.decode("utf-8", "replace")
                self.output += text
                show(text)
                last = time.time()
            elif time.time() - last >= quiet:
                return

    @property
    def scan_ids(self):
        return list(dict.fromkeys(SCAN_ID.findall(ANSI.sub("", self.output))))

    def answer(self, *lines, settle=1.2):
        """Types each line and presses Return, like a person at the keyboard."""
        for line in lines:
            for character in line:
                os.write(self.fd, character.encode())
                self.pump(quiet=self.typing_delay, limit=self.typing_delay * 4)
            os.write(self.fd, b"\r")
            self.pump(quiet=0.35, limit=5)
        self.pump(quiet=settle, limit=20)

    def close(self):
        try:
            os.waitpid(self.pid, 0)
        except ChildProcessError:
            pass
        os.close(self.fd)


def add_scan(harness, tracking, scan_type, courier, location, notes=""):
    harness.answer("1", tracking, scan_type, courier, location, notes)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--out", default="docs/week4-partA/screenshots", help="folder for the pictures")
    parser.add_argument("--harness-cmd", help="run this command instead of building the harness (for testing)")
    parser.add_argument("--typing-delay", type=float, default=0.03, help="seconds between typed characters")
    parser.add_argument("--no-capture", action="store_true", help="run the sessions without taking pictures")
    args = parser.parse_args()

    if args.harness_cmd:
        command = args.harness_cmd.split()
    else:
        project = "ApexPost.Courier.Offline.Harness"
        if not os.path.isdir(project):
            sys.exit("Run this from the repository root (the folder with ApexPost.slnx).")
        print("Building the harness...")
        subprocess.run(["dotnet", "build", f"{project}/{project}.csproj", "-c", "Release", "-nologo", "-v", "q"], check=True)
        command = ["dotnet", f"{project}/bin/Release/net10.0/{project}.dll"]

    os.makedirs(args.out, exist_ok=True)
    data_a = "/tmp/apexpost-capture-a"
    data_b = "/tmp/apexpost-capture-b"
    data_x = "/tmp/apexpost-capture-x"
    for folder in (data_a, data_b, data_x):
        shutil.rmtree(folder, ignore_errors=True)

    if not args.no_capture:
        fit_terminal_window()

    def picture(name):
        if not args.no_capture:
            capture(os.path.join(args.out, name))
        transcript.append(f"\n[picture saved: {name}]\n")

    def run(data_dir):
        show(CLEAR)
        return Harness(command, data_dir, args.typing_delay)

    # 1. Three scans saved offline first (not pictured), so the queries have data.
    h = run(data_a)
    add_scan(h, "APX-20260001", "1", "CR-1001", "Sydney NSW", "Picked up at depot")
    add_scan(h, "APX-20260001", "2", "CR-1001", "Parramatta NSW", "Loaded on van 12")
    add_scan(h, "APX-20260002", "1", "CR-1001", "Sydney NSW", "[fail] torn label")
    h.answer("6")
    h.close()

    # 2. Add Scan: each save shows the number of scans per scanType for that courier (SQL++ GROUP BY, $courierId).
    h = run(data_a)
    add_scan(h, "APX-20260001", "3", "CR-1001", "Penrith NSW", "Left with neighbour")
    add_scan(h, "APX-20260003", "1", "CR-2002", "Melbourne VIC")
    picture("1-add-scan-count-per-scan-type.png")
    h.answer("6")
    h.close()

    # 3. Find by Tracking Number: all scans for one tracking number, ordered by scannedAtUtc ($trackingNumber).
    h = run(data_a)
    h.answer("2", "APX-20260001")
    picture("2-find-by-tracking-number.png")
    h.answer("6")
    h.close()

    # 4. List Pending: all scans where syncStatus = Pending ($syncStatus).
    h = run(data_a)
    h.answer("3")
    picture("3-list-pending.png")
    h.answer("6")
    h.close()

    # 5. Simulate Sync: the outbox marks scans Synced or Failed; List Pending is then empty and Find shows Failed.
    h = run(data_a)
    h.answer("4", settle=2.0)
    h.answer("3")
    h.answer("2", "APX-20260002")
    picture("4-simulate-sync-then-pending-and-failed.png")
    h.answer("6")
    h.close()

    # 6. Change listener: the ids of changed documents and the live pending count, as scans are added.
    h = run(data_b)
    add_scan(h, "APX-20260010", "1", "CR-1001", "Sydney NSW")
    add_scan(h, "APX-20260010", "2", "CR-1001", "Parramatta NSW")
    add_scan(h, "APX-20260011", "1", "CR-1001", "Sydney NSW", "[fail] torn label")
    picture("5-listener-while-adding.png")
    h.answer("6")
    h.close()

    # 7. Change listener during a sync and a delete, then the listener.log file in the data folder.
    h = run(data_b)
    h.answer("4", settle=2.0)
    first_id = None
    log_path = os.path.join(data_b, "listener.log")
    if os.path.exists(log_path):
        found = re.findall(r"scan::[0-9a-f-]{36}", open(log_path, encoding="utf-8").read())
        first_id = found[0] if found else None
    if first_id:
        h.answer("5", first_id)
    h.answer("6")
    h.close()
    show(f"\n$ cat {log_path}\n")
    if os.path.exists(log_path):
        show(open(log_path, encoding="utf-8").read())
    picture("6-listener-sync-delete-and-log-file.png")

    # 8. The query plans: which value index each SQL++ statement uses.
    show(CLEAR)
    show(f"$ {' '.join(command + ['--data', data_x, '--explain'])}\n")
    plans = subprocess.run(command + ["--data", data_x, "--explain"], capture_output=True, text=True)
    show(plans.stdout + plans.stderr)
    picture("7-query-plans.png")

    with open(os.path.join(args.out, "transcript.txt"), "w", encoding="utf-8") as handle:
        handle.write("".join(transcript))
    for folder in (data_a, data_b, data_x):
        shutil.rmtree(folder, ignore_errors=True)
    print(f"\n{DIM}Done. Pictures and transcript.txt are in {args.out}{RESET}")


if __name__ == "__main__":
    main()
