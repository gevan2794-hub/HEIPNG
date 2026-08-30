#!/usr/bin/env python3
"""Listen for CarX telemetry frames and print them, to check the mod before SimHub is involved.

    python3 tools/monitor.py --port 20777             # live one-line summary
    python3 tools/monitor.py --port 20777 --channels  # list every channel the mod is sending
    python3 tools/monitor.py --port 20777 --raw       # dump full frames as JSON
"""

import argparse
import json
import socket
import sys
import time

SUMMARY = ("SpeedKph", "Rpm", "Gear", "SlipAngle", "YawRate", "AccelSwayG", "Throttle", "Brake")


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--port", type=int, default=20777, help="UDP port to listen on (default: %(default)s)")
    parser.add_argument("--raw", action="store_true", help="print each frame as full JSON")
    parser.add_argument("--channels", action="store_true", help="print the channel list once, then exit")
    parser.add_argument("--timeout", type=float, default=10.0, help="seconds to wait for the first frame")
    args = parser.parse_args()

    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    sock.bind(("0.0.0.0", args.port))
    sock.settimeout(args.timeout)

    print(f"listening on UDP {args.port} ...", file=sys.stderr)

    received = 0
    rejected = 0
    lost = 0
    expected = None
    last_print = 0.0

    try:
        while True:
            try:
                datagram, _ = sock.recvfrom(65535)
            except socket.timeout:
                print(f"no frames for {args.timeout:g}s -- is the game running with the mod loaded?", file=sys.stderr)
                return 1

            try:
                frame = json.loads(datagram.decode("utf-8"))
            except (UnicodeDecodeError, json.JSONDecodeError) as exc:
                rejected += 1
                print(f"unparseable packet ({exc})", file=sys.stderr)
                continue

            received += 1

            sequence = frame.get("Sequence")
            if isinstance(sequence, (int, float)):
                sequence = int(sequence)
                if expected is not None and sequence > expected:
                    lost += sequence - expected
                expected = sequence + 1

            if args.channels:
                print(f"{len(frame)} channels in this frame:\n")
                for key in sorted(frame):
                    print(f"  {key:<24} {frame[key]!r}")
                return 0

            if args.raw:
                print(json.dumps(frame, indent=2, sort_keys=True))
                continue

            # Throttle the console to something readable rather than 60 lines a second.
            now = time.monotonic()
            if now - last_print < 0.1:
                continue
            last_print = now

            parts = []
            for name in SUMMARY:
                value = frame.get(name)
                parts.append(f"{name}={value:8.2f}" if isinstance(value, (int, float)) else f"{name}=  --   ")

            status = "in car" if frame.get("InCar") else "menu  "
            print(f"\r[{status}] " + " ".join(parts) + f"  rx={received} lost={lost} bad={rejected}", end="", flush=True)
    except KeyboardInterrupt:
        print()
        return 0
    finally:
        sock.close()


if __name__ == "__main__":
    sys.exit(main())
