#!/usr/bin/env python3
"""Runs the server side of the Part D demo video and walks you through the app side, scene by scene.

Run it from the repository root in macOS Terminal, with SQL Server running and the Android emulator open:

    python3 docs/week4-capture/partD_demo.py --deploy-app

What it does, in order:
  * starts the real ApexPost.Api (https profile) and waits until it answers;
  * with --deploy-app, builds the Debug courier app and installs it on the running Android emulator;
  * tells you when to start the screen recording (Cmd+Shift+5);
  * for each scene, prints what to do in the emulator and a line you can say, then waits for Return;
  * between scenes it asks the API what it holds (scans received, 401/403 answers), so the video shows the
    server's side too. It stops the API for the circuit-breaker scene and starts it again for the recovery.

Tokens come from the API's own Development-only /dev/token. The partner key is read from your user secrets
and is never printed. HTTPS is verified against the ASP.NET Core development certificate (exported for this
run and deleted afterwards); certificate checks are never turned off.
"""
import argparse
import datetime
import json
import os
import shutil
import signal
import ssl
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.request
import uuid

BOLD = "\033[1m"
DIM = "\033[2m"
GREEN = "\033[32m"
RED = "\033[31m"
YELLOW = "\033[33m"
CYAN = "\033[36m"
RESET = "\033[0m"


class Api:
    """The API process and the HTTP calls the demo makes to it."""

    def __init__(self, base_url, project, log_path, start_command=None):
        self.base_url = base_url.rstrip("/")
        self.project = project
        self.log_path = log_path
        self.start_command = start_command
        self.process = None
        self.context = None
        self.cert_dir = None
        self.tokens = {}

    # ----- TLS: trust the development certificate for this run only -----
    def prepare_tls(self):
        if not self.base_url.startswith("https://"):
            return
        self.cert_dir = tempfile.mkdtemp(prefix="apexpost-devcert-")
        pem = os.path.join(self.cert_dir, "devcert.pem")
        exported = subprocess.run(
            ["dotnet", "dev-certs", "https", "--export-path", pem, "--format", "Pem", "--no-password"],
            capture_output=True, text=True)
        if exported.returncode == 0 and os.path.exists(pem):
            self.context = ssl.create_default_context(cafile=pem)
        else:
            print(f"{YELLOW}Could not export the development certificate; using the system trust store.{RESET}")
            self.context = ssl.create_default_context()

    def cleanup(self):
        if self.cert_dir:
            shutil.rmtree(self.cert_dir, ignore_errors=True)

    # ----- HTTP -----
    def call(self, method, path, token=None, body=None, headers=None):
        request = urllib.request.Request(self.base_url + path, method=method)
        request.add_header("Accept", "application/json")
        if token:
            request.add_header("Authorization", "Bearer " + token)
        for name, value in (headers or {}).items():
            request.add_header(name, value)
        data = None
        if body is not None:
            data = json.dumps(body).encode()
            request.add_header("Content-Type", "application/json")
        try:
            with urllib.request.urlopen(request, data=data, timeout=15, context=self.context) as response:
                return response.status, _json(response.read())
        except urllib.error.HTTPError as error:
            return error.code, _json(error.read())

    def is_up(self):
        try:
            self.call("GET", "/api/v2/shipments")
            return True
        except (urllib.error.URLError, ConnectionError, TimeoutError, OSError):
            return False

    def token(self, name, roles, courier_id=None):
        if name not in self.tokens:
            body = {"roles": roles}
            if courier_id:
                body["courierId"] = courier_id
            status, payload = self.call("POST", "/dev/token", body=body)
            if status != 200:
                sys.exit(f"{RED}/dev/token answered {status}. Is the API running in Development from this machine?{RESET}")
            self.tokens[name] = payload["accessToken"]
        return self.tokens[name]

    # ----- the process -----
    def start(self):
        command = self.start_command or ["dotnet", "run", "--project", self.project, "--launch-profile", "https"]
        log = open(self.log_path, "a")
        log.write(f"\n===== started {datetime.datetime.now():%H:%M:%S} =====\n")
        log.flush()
        self.process = subprocess.Popen(command, stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
        print(f"{DIM}Starting the API (log: {self.log_path})...{RESET}", end="", flush=True)
        deadline = time.time() + 240
        while time.time() < deadline:
            if self.process.poll() is not None:
                print()
                sys.exit(f"{RED}The API stopped while starting. Last lines of the log:{RESET}\n" + _tail(self.log_path))
            if self.is_up():
                print(f" {GREEN}up at {self.base_url}{RESET}")
                return
            print(".", end="", flush=True)
            time.sleep(2)
        sys.exit(f"{RED}The API did not answer within 4 minutes.{RESET}\n" + _tail(self.log_path))

    def stop(self):
        if not self.process or self.process.poll() is not None:
            return
        for sig, wait in ((signal.SIGINT, 15), (signal.SIGTERM, 5), (signal.SIGKILL, 5)):
            try:
                os.killpg(self.process.pid, sig)
            except ProcessLookupError:
                return
            try:
                self.process.wait(timeout=wait)
                break
            except subprocess.TimeoutExpired:
                continue
        # Wait until the port no longer answers, so the app really sees the server go away.
        for _ in range(20):
            if not self.is_up():
                return
            time.sleep(0.5)


def _json(raw):
    try:
        return json.loads(raw.decode() or "null")
    except ValueError:
        return raw.decode(errors="replace")


def _tail(path, lines=25):
    try:
        return "".join(open(path, encoding="utf-8", errors="replace").readlines()[-lines:])
    except OSError:
        return "(no log)"


def read_user_secrets(project):
    """Returns the user secrets as a dict. The values are never printed."""
    listed = subprocess.run(["dotnet", "user-secrets", "list", "--project", project], capture_output=True, text=True)
    secrets = {}
    for line in listed.stdout.splitlines():
        if " = " in line:
            key, value = line.split(" = ", 1)
            secrets[key.strip()] = value.strip()
    return secrets


def card(number, title, steps, say=None):
    """Prints one scene's instructions and waits for Return."""
    width = 78
    print()
    print(f"{CYAN}{'=' * width}{RESET}")
    print(f"{BOLD}{CYAN} Scene {number}: {title}{RESET}")
    print(f"{CYAN}{'-' * width}{RESET}")
    for step in steps:
        print(f"  • {step}")
    if say:
        print(f"{DIM}  Say: {say}{RESET}")
    print(f"{CYAN}{'=' * width}{RESET}")
    input(f"{BOLD}Press Return when this scene is done...{RESET}")


def show_result(label, status, payload, expected):
    colour = GREEN if status == expected else RED
    detail = ""
    if isinstance(payload, dict):
        detail = payload.get("detail") or payload.get("title") or ""
        if not detail and "destinationCity" in payload:
            detail = f"status {payload.get('status')}, city {payload.get('destinationCity')} (no recipient details)"
        elif not detail and "trackingNumber" in payload:
            detail = f"status {payload.get('status')}"
    print(f"  {label:<70} {colour}{BOLD}{status}{RESET}  {DIM}{detail}{RESET}")


def show_scans(api, tracking):
    status, scans = api.call("GET", f"/api/v2/shipments/{tracking}/scans", token=api.token("admin", ["Admin"]))
    if status == 401:  # a token from before a restart; ask for a fresh one
        api.tokens.pop("admin", None)
        status, scans = api.call("GET", f"/api/v2/shipments/{tracking}/scans", token=api.token("admin", ["Admin"]))
    if status != 200 or not isinstance(scans, list):
        print(f"  {RED}Could not read the scans ({status}).{RESET}")
        return 0
    print(f"  {BOLD}The API holds {len(scans)} scan(s) for {tracking}{RESET}" + (", latest:" if scans else "."))
    for scan in scans[-3:]:
        print(f"    {scan.get('scannedAtUtc', '')[:19]}  {scan.get('scanType', ''):<10} "
              f"{scan.get('courierId', ''):<8} {scan.get('location', '')}")
    return len(scans)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--base-url", default="https://localhost:7180")
    parser.add_argument("--tracking", default="APX-10000001", help="the seeded shipment the scans go to")
    parser.add_argument("--courier", default="CR-1001")
    parser.add_argument("--deploy-app", action="store_true", help="build the Debug app and run it on the open emulator")
    parser.add_argument("--open-swagger", action="store_true", help="open Swagger UI in the browser for scene 4")
    parser.add_argument("--api-command", help="start the API with this command instead (for testing)")
    parser.add_argument("--skip-secrets-check", action="store_true", help="for testing")
    args = parser.parse_args()

    project = "ApexPost.Api"
    if not args.api_command and not os.path.isdir(project):
        sys.exit("Run this from the repository root (the folder with ApexPost.slnx).")

    partner_key = None
    if not args.skip_secrets_check:
        secrets = read_user_secrets(project)
        if "Jwt:SigningKey" not in secrets:
            sys.exit("Jwt:SigningKey is not set. See README.md, Week 4 Part D, for the two user-secrets commands.")
        partner_key = next((v for k, v in secrets.items() if k.startswith("PartnerApiKeys:Keys:")), None)
        if not partner_key:
            print(f"{YELLOW}No partner key in user secrets: the valid-key request in scene 4 is skipped.{RESET}")
    else:
        partner_key = os.environ.get("APEXPOST_DEMO_PARTNER_KEY")

    api = Api(args.base_url, project, os.path.join(tempfile.gettempdir(), "apexpost-demo-api.log"),
              args.api_command.split() if args.api_command else None)
    api.prepare_tls()
    try:
        if api.is_up():
            sys.exit(f"{YELLOW}An API is already answering at {args.base_url}. Stop it first (Ctrl+C in its window): "
                     f"this script has to stop and restart it for the circuit-breaker scene.{RESET}")
        api.start()

        if args.deploy_app:
            print(f"{DIM}Building the Debug courier app and installing it on the emulator (a few minutes the first time)...{RESET}")
            subprocess.run(["dotnet", "build", "ApexPost.Courier/ApexPost.Courier.csproj", "-f", "net10.0-android",
                            "-p:CourierPlatforms=net10.0-android", "-t:Run", "-nologo", "-v", "q"], check=True)

        input(f"\n{BOLD}Start the screen recording now (Cmd+Shift+5, then Record), then press Return...{RESET}")

        card(1, "What this is", [
            "Show the emulator with the ApexPost Courier app, and this Terminal window beside it.",
        ], say="The .NET MAUI courier app calls the v2 API over HTTPS with a JWT, through IHttpClientFactory "
               "and a Polly pipeline, and keeps scans in Couchbase Lite when it is offline.")

        card(2, "Login", [
            f"Open ApexPost Courier. Type courier id {args.courier} and tap 'Get development token (Debug build)'.",
            "The app opens on New Scan.",
        ], say="The token comes from the API's Development-only /dev/token and is kept only in SecureStorage.")

        print(f"\n{DIM}Before going offline:{RESET}")
        before = show_scans(api, args.tracking)

        card(3, "Scans saved in airplane mode", [
            "Swipe down from the top of the emulator and turn on Airplane mode.",
            "The orange banner 'Server unavailable – working offline' appears.",
            f"On New Scan, save two scans for {args.tracking}: Pickup at 'Sydney depot', then InTransit at 'Parramatta hub'.",
            "Open Pending: the count shows 2, kept up to date by the Couchbase Lite change listener.",
        ], say="With no network the scans are saved on the device first, as Pending. Nothing is lost.")

        print(f"\n{DIM}While the device is offline, the server still has:{RESET}")
        show_scans(api, args.tracking)

        card(4, "Automatic sync after reconnecting", [
            "Turn Airplane mode off.",
            "Watch Pending drop to 0: Connectivity.ConnectivityChanged starts the upload, in batches of 20.",
            "Open Sync Status: it shows the last sync time and the circuit Closed.",
        ], say="When the connection comes back the app uploads the Pending scans by itself and marks them Synced.")

        print(f"\n{DIM}After reconnecting, the server has:{RESET}")
        after = show_scans(api, args.tracking)
        if after > before:
            print(f"  {GREEN}{after - before} new scan(s) arrived from courier {args.courier}.{RESET}")

        if args.open_swagger:
            subprocess.run(["open", f"{args.base_url}/swagger"])
        print(f"\n{BOLD}Scene 5: 401 and 403 from the API{RESET}")
        courier = api.token("courier", ["Courier"], args.courier)
        show_result("GET  /api/v2/shipments/{0}  (no token)".format(args.tracking),
                    *api.call("GET", f"/api/v2/shipments/{args.tracking}"), expected=401)
        show_result("GET  /api/v2/shipments/{0}  (invalid token)".format(args.tracking),
                    *api.call("GET", f"/api/v2/shipments/{args.tracking}", token="not.a.valid-token"), expected=401)
        show_result("GET  /api/v2/shipments/{0}  (courier token)".format(args.tracking),
                    *api.call("GET", f"/api/v2/shipments/{args.tracking}", token=courier), expected=200)
        show_result("DELETE /api/v2/shipments/APX-10000005  (courier token, Admin only)",
                    *api.call("DELETE", "/api/v2/shipments/APX-10000005", token=courier), expected=403)
        foreign = {"scans": [{"scanId": f"scan::{uuid.uuid4()}", "scanType": "Pickup", "courierId": "CR-2002",
                              "location": "Depot", "scannedAtUtc": datetime.datetime.now(datetime.timezone.utc).isoformat()}]}
        show_result(f"POST /api/v2/shipments/{args.tracking}/scans  (scan under CR-2002)",
                    *api.call("POST", f"/api/v2/shipments/{args.tracking}/scans", token=courier, body=foreign), expected=403)
        show_result(f"GET  /api/v2/partner/shipments/{args.tracking}  (wrong API key)",
                    *api.call("GET", f"/api/v2/partner/shipments/{args.tracking}",
                              headers={"X-Api-Key": "not-a-valid-partner-key-0123456789abcdef"}), expected=401)
        if partner_key:
            show_result(f"GET  /api/v2/partner/shipments/{args.tracking}  (valid API key)",
                        *api.call("GET", f"/api/v2/partner/shipments/{args.tracking}",
                                  headers={"X-Api-Key": partner_key}), expected=200)
        input(f"{DIM}  Say: a missing or invalid token is 401, the wrong role or another courier's id is 403, "
              f"all as ProblemDetails. Press Return to continue...{RESET}")

        print(f"\n{YELLOW}{BOLD}Stopping the API for the circuit-breaker scene...{RESET}")
        api.stop()
        print(f"{YELLOW}API stopped at {datetime.datetime.now():%H:%M:%S}.{RESET}")
        card(6, "Circuit breaker opens while the API is stopped", [
            "Make sure Airplane mode is OFF (the device is online, only the server is gone).",
            f"Save one more scan for {args.tracking}, Delivered at 'Front door' with signature 'Sam Example'.",
            "The upload retries with backoff, then fails: the banner 'Server unavailable – working offline' shows.",
            "Open Sync Status: the circuit is Open, and the scan stays Pending.",
        ], say="Retries use exponential backoff with jitter. After repeated failures the circuit opens, the app "
               "stops calling the server and keeps saving scans locally.")

        api.start()
        print(f"{DIM}The circuit stays open for 30 seconds before it lets a trial call through.{RESET}")
        for left in range(30, 0, -1):
            print(f"\r  {left:2d} s ", end="", flush=True)
            time.sleep(1)
        print()
        card(7, "Recovery", [
            "On Sync Status tap 'Sync now'. The trial call succeeds: the circuit goes Half-open, then Closed.",
            "The banner disappears and Pending drops to 0.",
        ], say="Once the API is back the circuit closes and the waiting scan is uploaded.")

        print(f"\n{DIM}The server now has:{RESET}")
        show_scans(api, args.tracking)
        print(f"\n{GREEN}{BOLD}All scenes done. Stop the screen recording (the stop button in the menu bar).{RESET}")
    finally:
        api.stop()
        api.cleanup()
        print(f"{DIM}API stopped. Its log is at {api.log_path}{RESET}")


if __name__ == "__main__":
    main()
