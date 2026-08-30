#!/usr/bin/env python3
"""Send synthetic CarX telemetry, so the SimHub side can be built and tuned without the game.

Drives a car in a steady drift around a circle: speed, yaw rate and slip angle all move,
the engine sweeps through the rev range and shifts, and the accelerations are consistent
with the motion. Enough to see a dashboard come alive and to set up ShakeIt effects.

    python3 tools/fake_game.py --port 20777
    python3 tools/fake_game.py --port 20777 --rate 60 --duration 30
"""

import argparse
import json
import math
import socket
import time

GRAVITY = 9.80665
WHEELS = ("FL", "FR", "RL", "RR")


def build_frame(sequence, elapsed):
    """One plausible frame of a car mid-drift, as the mod would emit it."""
    # A 40 m radius circle at ~22 m/s, with the tail hung out ~28 degrees.
    radius = 40.0
    speed = 22.0 + 3.0 * math.sin(elapsed * 0.35)
    heading = elapsed * speed / radius
    slip_angle = 28.0 + 6.0 * math.sin(elapsed * 0.9)

    lateral_g = (speed * speed / radius) / GRAVITY
    yaw_rate = math.degrees(speed / radius)

    # ~2.2s per gear, revs sweeping idle->redline, four gears.
    gear = 1 + int(elapsed / 2.2) % 4
    rev_fraction = (elapsed / 2.2) % 1.0
    rpm = 1200.0 + rev_fraction * 6300.0

    throttle = 0.55 + 0.45 * abs(math.sin(elapsed * 1.3))
    brake = max(0.0, -math.sin(elapsed * 0.7)) * 0.3

    frame = {
        "Sequence": sequence,
        "TimestampMs": round(elapsed * 1000.0, 3),
        "InCar": True,

        "GameName": "Fake CarX",
        "ProfileName": "synthetic",
        "CarName": "Test Mule",
        "TrackName": "Circle",

        "SpeedMs": speed,
        "SpeedKph": speed * 3.6,
        "SpeedMph": speed * 2.2369362920544,

        "VelocitySurge": speed * math.cos(math.radians(slip_angle)),
        "VelocitySway": speed * math.sin(math.radians(slip_angle)),
        "VelocityHeave": 0.0,

        "AccelSurgeG": 0.25 * math.sin(elapsed * 1.1),
        "AccelSwayG": lateral_g,
        "AccelHeaveG": 1.0 + 0.05 * math.sin(elapsed * 4.0),

        "YawRate": yaw_rate,
        "PitchRate": 1.5 * math.sin(elapsed * 2.0),
        "RollRate": 2.0 * math.sin(elapsed * 1.7),

        "Yaw": ((math.degrees(heading) + 180.0) % 360.0) - 180.0,
        "Pitch": 1.2 * math.sin(elapsed * 0.8),
        "Roll": -3.5 * (lateral_g / 1.2),

        "SlipAngle": slip_angle,

        "PositionX": radius * math.cos(heading),
        "PositionY": 0.0,
        "PositionZ": radius * math.sin(heading),

        "Rpm": rpm,
        "MaxRpm": 7500.0,
        "IdleRpm": 900.0,
        "Gear": float(gear),
        "Throttle": throttle,
        "Brake": brake,
        "Clutch": 0.0,
        "Handbrake": 0.0,
        "SteerAngle": -18.0 + 8.0 * math.sin(elapsed * 0.9),
        "TurboBoost": 0.4 + 0.3 * throttle,
        "Fuel": max(0.0, 45.0 - elapsed * 0.05),

        "DriftScore": float(int(elapsed * 137)),
        "DriftCombo": 1.0 + (elapsed % 8.0) / 2.0,
    }

    for index, wheel in enumerate(WHEELS):
        rear = index >= 2
        frame["TireSlip" + wheel] = slip_angle * (1.4 if rear else 0.7)
        frame["WheelSpeed" + wheel] = speed / 0.32 * (1.35 if rear else 1.0)
        frame["TireTemp" + wheel] = 65.0 + (25.0 if rear else 8.0)
        frame["SuspTravel" + wheel] = 0.08 + 0.02 * math.sin(elapsed * 3.0 + index)
        frame["WheelGrounded" + wheel] = 1.0

    return frame


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--host", default="127.0.0.1", help="destination host (default: %(default)s)")
    parser.add_argument("--port", type=int, default=20777, help="destination UDP port (default: %(default)s)")
    parser.add_argument("--rate", type=float, default=60.0, help="frames per second (default: %(default)s)")
    parser.add_argument("--duration", type=float, default=0.0, help="seconds to run, 0 = until interrupted")
    args = parser.parse_args()

    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    period = 1.0 / args.rate
    started = time.monotonic()
    sequence = 0

    print(f"sending synthetic telemetry to {args.host}:{args.port} at {args.rate:g}Hz (ctrl-c to stop)")
    try:
        while True:
            elapsed = time.monotonic() - started
            if args.duration and elapsed >= args.duration:
                break

            sequence += 1
            payload = json.dumps(build_frame(sequence, elapsed), separators=(",", ":"))
            sock.sendto(payload.encode("utf-8"), (args.host, args.port))

            time.sleep(max(0.0, period - ((time.monotonic() - started) % period)))
    except KeyboardInterrupt:
        pass
    finally:
        sock.close()

    print(f"sent {sequence} frames")


if __name__ == "__main__":
    main()
