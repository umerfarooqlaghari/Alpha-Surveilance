#!/usr/bin/env python3
"""
Tunnel Orchestrator for Alpha Surveillance RTSP Ingestion.

Starts local MediaMTX RTSP server, establishes a reverse TCP tunnel via Pinggy,
constructs the public RTSP streaming URL for Larix Broadcaster (mobile camera),
and dispatches the configuration to the administrator via AWS SES.
"""

import atexit
import os
import re
import signal
import socket
import subprocess
import sys
import threading
import time
from typing import Optional

import boto3
from botocore.exceptions import ClientError
from dotenv import load_dotenv
import pexpect

# ──────────────────────────────────────────────────────────────────────────────
# Path Resolution & Environment Loading
# ──────────────────────────────────────────────────────────────────────────────

CURRENT_DIR = os.path.dirname(os.path.abspath(__file__))

# Determine if we are inside vision-inference-service or project root
if os.path.basename(CURRENT_DIR) == "vision-inference-service":
    SERVICE_DIR = CURRENT_DIR
    ROOT_DIR = os.path.dirname(CURRENT_DIR)
else:
    SERVICE_DIR = os.path.join(CURRENT_DIR, "vision-inference-service")
    ROOT_DIR = CURRENT_DIR

# Load environment files in order of precedence:
# 1. Project root .env (contains AWS_ACCESS_KEY_ID, AWS_SECRET_ACCESS_KEY)
# 2. Service .env
# 3. Service .env.local (developer overrides)
env_candidates = [
    os.path.join(ROOT_DIR, ".env"),
    os.path.join(SERVICE_DIR, ".env"),
    os.path.join(SERVICE_DIR, ".env.local"),
]

for env_path in env_candidates:
    if os.path.exists(env_path):
        load_dotenv(env_path, override=False)

# ──────────────────────────────────────────────────────────────────────────────
# Configuration
# ──────────────────────────────────────────────────────────────────────────────

AWS_REGION = os.getenv("AWS_REGION", "us-east-1")
AWS_KEY = os.getenv("AWS_ACCESS_KEY_ID")
AWS_SECRET = os.getenv("AWS_SECRET_ACCESS_KEY")

# Email settings
# In AWS SES, Source must be a verified email/domain. Default in Alpha Surveillance is info@alpha-devs.cloud
EMAIL_SENDER = os.getenv("EMAIL_SENDER", "info@alpha-devs.cloud")
EMAIL_RECEIVER = os.getenv("EMAIL_RECEIVER", "umer.farooq@alpha-devs.cloud")

# Stream identifier (paths configured in mediamtx.yml: 'live' and 'mystream')
STREAM_NAME = os.getenv("STREAM_NAME", "live")
RTSP_LOCAL_PORT = int(os.getenv("RTSP_LOCAL_PORT", "8554"))

# MediaMTX config file resolution
config_candidates = [
    os.path.join(SERVICE_DIR, "mediamtx.yml"),
    os.path.join(ROOT_DIR, "mediamtx.yml"),
    os.path.join(os.getcwd(), "mediamtx.yml"),
]

MEDIAMTX_CONFIG = next((p for p in config_candidates if os.path.exists(p)), None)

# Process handle for cleanup
_mediamtx_proc: Optional[subprocess.Popen] = None


def is_port_in_use(port: int) -> bool:
    """Checks whether local port is already bound by an active service."""
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as s:
        s.settimeout(0.5)
        return s.connect_ex(("127.0.0.1", port)) == 0


def cleanup_processes():
    """Ensures MediaMTX child process is terminated on exit."""
    global _mediamtx_proc
    if _mediamtx_proc and _mediamtx_proc.poll() is None:
        print("\n[+] Terminating local MediaMTX server...")
        try:
            _mediamtx_proc.terminate()
            _mediamtx_proc.wait(timeout=2)
        except Exception:
            _mediamtx_proc.kill()
        _mediamtx_proc = None


atexit.register(cleanup_processes)


def signal_handler(sig, frame):
    cleanup_processes()
    sys.exit(0)


signal.signal(signal.SIGINT, signal_handler)
signal.signal(signal.SIGTERM, signal_handler)


def run_mediamtx():
    """Starts MediaMTX in a separate background thread if not already running."""
    global _mediamtx_proc

    if is_port_in_use(RTSP_LOCAL_PORT):
        print(f"[!] Port {RTSP_LOCAL_PORT} is already active. Reusing running RTSP/MediaMTX service.")
        return

    if not MEDIAMTX_CONFIG:
        print("[-] Warning: mediamtx.yml not found. Attempting to start mediamtx with default configuration...")
        cmd = ["mediamtx"]
    else:
        print(f"[+] Launching MediaMTX server using config at: {MEDIAMTX_CONFIG}")
        cmd = ["mediamtx", MEDIAMTX_CONFIG]

    try:
        _mediamtx_proc = subprocess.Popen(
            cmd,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.PIPE
        )
        time.sleep(1.0)
        if _mediamtx_proc.poll() is not None:
            stderr = _mediamtx_proc.stderr.read().decode("utf-8", errors="replace") if _mediamtx_proc.stderr else ""
            print(f"[-] MediaMTX failed to start: {stderr}")
    except FileNotFoundError:
        print("[-] Error: 'mediamtx' command not found. Ensure it is installed (e.g. brew install mediamtx).")
    except Exception as e:
        print(f"[-] Failed to start MediaMTX: {e}")


def send_ses_email(mobile_rtsp_url: str):
    """Sends the formatted RTSP links to the admin via AWS SES."""
    print(f"[+] Sending RTSP URL to {EMAIL_RECEIVER} (Sender: {EMAIL_SENDER}) via AWS SES...")

    client_kwargs = {"region_name": AWS_REGION}
    if AWS_KEY and AWS_SECRET:
        client_kwargs["aws_access_key_id"] = AWS_KEY
        client_kwargs["aws_secret_access_key"] = AWS_SECRET

    client = boto3.client("ses", **client_kwargs)

    docker_rtsp_url = f"rtsp://host.docker.internal:{RTSP_LOCAL_PORT}/{STREAM_NAME}"
    localhost_rtsp_url = f"rtsp://localhost:{RTSP_LOCAL_PORT}/{STREAM_NAME}"

    subject = "🚀 Alpha Surveillance — Live RTSP Mobile Stream Ready"
    body_text = f"""Alpha Surveillance Tunnel Orchestrator
===============================================

Your automated RTSP pipeline is live and ready for mobile streaming.

1. iPhone / Android Larix Broadcaster URL (Publish to this):
   {mobile_rtsp_url}

2. Dashboard / Vision Inference Pipeline URL:
   {docker_rtsp_url}

3. Localhost Stream URL (VLC / QuickTime):
   {localhost_rtsp_url}

Larix Broadcaster Setup Instructions:
1. Open Larix Broadcaster on your phone.
2. Go to Settings (gear icon) -> Connections -> New Connection.
3. Name: Alpha Mobile Camera
4. URL: {mobile_rtsp_url}
5. Target format: RTSP (Auto / TCP recommended)
6. Tap Save, select the connection, and press the red record button to broadcast.
"""

    body_html = f"""
    <html>
      <head>
        <style>
          body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; color: #1e293b; background-color: #f8fafc; padding: 24px; }}
          .card {{ background: #ffffff; border-radius: 12px; padding: 24px; border: 1px solid #e2e8f0; max-width: 600px; margin: 0 auto; box-shadow: 0 4px 6px -1px rgba(0, 0, 0, 0.1); }}
          .header {{ display: flex; align-items: center; border-bottom: 2px solid #e2e8f0; padding-bottom: 12px; margin-bottom: 20px; }}
          h2 {{ margin: 0; color: #0f172a; font-size: 20px; }}
          .url-box {{ background-color: #0f172a; color: #38bdf8; font-family: monospace; font-size: 14px; padding: 12px 16px; border-radius: 8px; word-break: break-all; margin: 8px 0 16px 0; }}
          .label {{ font-size: 13px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.05em; color: #64748b; }}
          ol {{ padding-left: 20px; line-height: 1.6; color: #334155; font-size: 14px; }}
        </style>
      </head>
      <body>
        <div class="card">
          <div class="header">
            <h2>🚀 Alpha Surveillance — Live RTSP Mobile Stream</h2>
          </div>
          <p>Your reverse RTSP tunnel is now online. Connect your mobile phone camera using Larix Broadcaster:</p>
          
          <div class="label">Mobile Larix Broadcaster (Publish URL)</div>
          <div class="url-box">{mobile_rtsp_url}</div>

          <div class="label">Dashboard / Vision Inference (Consumer URL)</div>
          <div class="url-box">{docker_rtsp_url}</div>

          <div class="label">Localhost VLC Test Stream</div>
          <div class="url-box">{localhost_rtsp_url}</div>

          <h3 style="color: #0f172a; font-size: 16px; margin-top: 24px;">Larix Broadcaster Setup:</h3>
          <ol>
            <li>Open <strong>Larix Broadcaster</strong> on your phone.</li>
            <li>Go to <strong>Settings &rarr; Connections &rarr; New Connection</strong>.</li>
            <li>Paste the <strong>Publish URL</strong> above into the URL field.</li>
            <li>Save, toggle it active, and hit the red broadcast button.</li>
          </ol>
        </div>
      </body>
    </html>
    """

    try:
        response = client.send_email(
            Destination={"ToAddresses": [EMAIL_RECEIVER]},
            Message={
                "Body": {
                    "Text": {"Charset": "UTF-8", "Data": body_text},
                    "Html": {"Charset": "UTF-8", "Data": body_html},
                },
                "Subject": {"Charset": "UTF-8", "Data": subject},
            },
            Source=EMAIL_SENDER,
        )
        print(f"[+] Email sent successfully! Message ID: {response['MessageId']}")
    except ClientError as e:
        print(f"[-] AWS SES Email failed: {e.response['Error']['Message']}")
        print(f"[i] Fallback: You can manually copy the RTSP link:")
        print(f"    Mobile (Larix): {mobile_rtsp_url}")
        print(f"    Pipeline:       {docker_rtsp_url}")
    except Exception as e:
        print(f"[-] Email dispatch error: {e}")


def monitor_and_tunnel():
    """Manages the SSH reverse tunnel connection via Pinggy and parses the allocated TCP route."""
    # -o StrictHostKeyChecking=no and -o UserKnownHostsFile=/dev/null avoid interactive SSH host verification blocks
    ssh_cmd = (
        f"ssh -p 443 -o StrictHostKeyChecking=no -o UserKnownHostsFile=/dev/null "
        f"-o ServerAliveInterval=30 -R0:localhost:{RTSP_LOCAL_PORT} tcp@free.pinggy.io"
    )
    print("[+] Initializing Pinggy SSH TCP Tunnel...")

    child = pexpect.spawn(ssh_cmd, encoding="utf-8", timeout=30)
    child.logfile_read = sys.stdout

    try:
        # Pinggy initially presents a password prompt, or in some cases the banner directly
        patterns = [
            r"[Pp]assword:",
            r"tcp://([a-zA-Z0-9\.\-]+:\d+)",
            pexpect.EOF,
            pexpect.TIMEOUT,
        ]

        index = child.expect(patterns, timeout=15)
        captured_rtsp_url = None

        if index == 0:
            time.sleep(0.3)
            child.sendline("1234")
            print("\n[+] Authentication sent automatically.")
            # Once authenticated, Pinggy renders an interactive curses TUI.
            # Do NOT use readline() here because curses uses ANSI cursor escapes rather than newlines (\r\n),
            # which causes readline() to hang until timeout. Instead, expect the tcp:// URL directly.
            url_idx = child.expect([r"tcp://([a-zA-Z0-9\.\-]+:\d+)", pexpect.EOF, pexpect.TIMEOUT], timeout=20)
            if url_idx == 0:
                tcp_url = child.match.group(0)
                captured_rtsp_url = tcp_url.replace("tcp://", "rtsp://") + f"/{STREAM_NAME}"
        elif index == 1:
            print("\n[+] Direct banner detected.")
            tcp_url = child.match.group(0)
            captured_rtsp_url = tcp_url.replace("tcp://", "rtsp://") + f"/{STREAM_NAME}"

        if captured_rtsp_url:
            print(f"\n[+] Captured Tunnel Route: {tcp_url}")
            print(f"[+] Generated RTSP Route: {captured_rtsp_url}")
            
            # Print big clear banner in console
            print("\n" + "=" * 60)
            print("📱 IPPHONE / ANDROID LARIX BROADCASTER URL:")
            print(f"   {captured_rtsp_url}")
            print("\n🖥️  DASHBOARD / PIPELINE RTSP URL:")
            print(f"   rtsp://host.docker.internal:{RTSP_LOCAL_PORT}/{STREAM_NAME}")
            print("=" * 60 + "\n")

            send_ses_email(captured_rtsp_url)
        else:
            print("[-] Warning: Tunnel URL pattern not detected within initial timeout window.")

        print("\n[+] Tunnel is active. Keep this terminal open to maintain the stream. Press Ctrl+C to exit.")
        if sys.stdin.isatty():
            try:
                child.interact()
            except (KeyboardInterrupt, OSError):
                pass
        else:
            child.wait()

    except Exception as e:
        print(f"\n[-] Automation runtime error: {str(e)}")
    finally:
        child.close(force=True)


def main():
    print("=" * 60)
    print("🎥 Alpha Surveillance — RTSP Tunnel Orchestrator")
    print("=" * 60)
    print(f"[*] Stream Name:    /{STREAM_NAME}")
    print(f"[*] Local RTSP:     localhost:{RTSP_LOCAL_PORT}")
    print(f"[*] Email Receiver: {EMAIL_RECEIVER}")
    print(f"[*] Email Sender:   {EMAIL_SENDER}")
    print(f"[*] AWS Region:     {AWS_REGION}")
    print("=" * 60)

    mediamtx_thread = threading.Thread(target=run_mediamtx, daemon=True)
    mediamtx_thread.start()

    time.sleep(1.5)
    monitor_and_tunnel()


if __name__ == "__main__":
    main()
