# Wire protocol

One UTF-8 JSON object per UDP datagram, no framing, no acknowledgement. Default port
**20777**, default destination **127.0.0.1**.

```json
{"Sequence":1421,"TimestampMs":23701.5,"InCar":true,"SpeedKph":79.2,"Rpm":5310,"Gear":3,"SlipAngle":28.4}
```

Design notes:

- **Flat.** Values are numbers, strings or booleans — never nested objects or arrays.
  This is what makes the stream readable by SimHub's UDPConnector plugin with no code,
  and it keeps the in-game serialiser small enough to hand-roll (pulling a JSON library
  into the game's own Unity domain is a good way to get an assembly version conflict).
- **Open set.** Consumers must treat every channel as optional and ignore unknown keys.
  A game that cannot supply RPM simply omits `Rpm`; a profile that adds a new channel
  needs no change on the SimHub side.
- **Lossy by design.** UDP, fire-and-forget, no retransmission. A dropped frame is 16ms
  of nothing. `Sequence` is a monotonic counter so a receiver can *measure* loss without
  trying to repair it; it resets when the game restarts.
- **`InCar`** is false in menus and while no car is spawned. Consumers should idle rather
  than hold the last value on screen.

## Channels

Always present when `InCar` is true — derived from Unity's own `Rigidbody`/`Transform`,
so they work on any CarX title (and any other Unity racing game) with no per-game setup:

| Channel | Unit | Notes |
|---|---|---|
| `SpeedMs` `SpeedKph` `SpeedMph` | m/s, km/h, mph | Magnitude of the velocity vector |
| `VelocitySurge` `VelocitySway` `VelocityHeave` | m/s | Body frame: +Z forward, +X right, +Y up |
| `AccelSurgeG` `AccelSwayG` `AccelHeaveG` | g | Body frame, gravity included, so heave reads ~1.0 at rest |
| `YawRate` `PitchRate` `RollRate` | deg/s | Body frame |
| `Yaw` `Pitch` `Roll` | deg | Wrapped to (-180, 180] |
| `SlipAngle` | deg | `atan2(lateral, forward)` — the angle between where the car points and where it is going. Zero below 1 m/s. Sign follows +X (right) |
| `PositionX` `PositionY` `PositionZ` | m | World space |

Present only when a game profile supplies them:

| Channel | Unit |
|---|---|
| `Rpm` `MaxRpm` `IdleRpm` | rpm |
| `Gear` | -1 reverse, 0 neutral, 1..n |
| `Throttle` `Brake` `Clutch` `Handbrake` | 0..1 |
| `SteerAngle` | deg, negative = left |
| `TurboBoost` `Fuel` | game units |
| `DriftScore` `DriftCombo` | game units |
| `LapNumber` `CurrentLapTime` `LastLapTime` `BestLapTime` | count, seconds |

Per-wheel channels carry an `FL`/`FR`/`RL`/`RR` suffix — `TireSlipRR`, `WheelSpeedFL`,
`TireTempRL`, `SuspTravelFR`, `WheelGroundedRR`.

Strings: `GameName`, `ProfileName`, `CarName`, `TrackName`.

## Adding a channel

Pick a name, emit it, done. There is no registry and no version negotiation: the mod
sends what it has, the receiver publishes what it gets. Names in
`src/CarX.Telemetry.Shared/TelemetryFrame.cs` are the canonical spellings — add to that
list rather than renaming an existing entry, since dashboards bind to these strings.
