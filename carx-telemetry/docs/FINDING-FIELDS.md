# Finding the fields for a game profile

The mod gives you speed, accelerations, rotation rates, attitude and slip angle out of
the box, on any CarX title, because those come from Unity's own `Rigidbody` and
`Transform`. RPM, gear, pedals and drift score are the game's own variables, and nobody
outside CarX knows what they are called — so you look them up once and write them into a
profile.

This is the only part of the job that needs doing per game, and it is a text edit.

## Route 1 — the in-game dump (works on any build)

1. Install the mod, launch the game, get into a car.
2. Hold a state you can read off the HUD: sit at idle, then hold a steady ~3000rpm in 2nd.
3. Press **F9** (configurable). The BepInEx console prints every component on the car and
   every numeric field/property with its live value:

   ```
   [Info : CarX Telemetry Bridge] === vehicle dump: PlayerCar (matched via CarController) ===
   [Info : CarX Telemetry Bridge]   [Game.Vehicles.CarController]
   [Info : CarX Telemetry Bridge]     CarController.engineRpm = 3014.882
   [Info : CarX Telemetry Bridge]     CarController.gasInput = 0.41
   [Info : CarX Telemetry Bridge]     CarController.currentGear = 2
   ```

4. Match values against the HUD, then write the winners into your profile:

   ```ini
   [channels]
   Rpm      = CarController.engineRpm
   Throttle = CarController.gasInput
   Gear     = CarController.currentGear
   ```

5. Restart the game — profiles are read at startup.

Dump twice in different states (idle vs. revving, straight vs. mid-drift) and diff them.
The fields that changed the way you expect are the ones you want; that is much faster
than reading a single dump top to bottom.

## Route 2 — read the game's own assemblies (faster, when available)

**Mono games** (a `<Game>_Data/Managed/Assembly-CSharp.dll` next to the exe — CarX Drift
Racing Online 1 is one): open that DLL in [dnSpy](https://github.com/dnSpyEx/dnSpy) or
[ILSpy](https://github.com/icsharpcode/ILSpy) and read the car controller class directly.
Field names are right there, and you can see which one is the engine and which is a
display-smoothed copy.

**IL2CPP games** (a `GameAssembly.dll` next to the exe — likely for current CarX Drift
Racing Online 2 builds): names are compiled away, but metadata usually survives. Run
[Cpp2IL](https://github.com/SamboyCoding/Cpp2IL) or
[Il2CppDumper](https://github.com/Perfare/Il2CppDumper) over `GameAssembly.dll` +
`global-metadata.dat` to recover a browsable set of stub assemblies, then read those the
same way. If the build is stripped of metadata, fall back to Route 1 — the dump works
either way, since it reflects over whatever the runtime actually has.

## Path syntax

```
<Channel> = <ComponentTypeName>.<member>[.<member>...] [* scale] [+ offset]
```

- The first segment is a **component type name** (just the name, not the namespace),
  looked up on the car's GameObject or any of its children.
- Subsequent segments are fields, properties, or zero-argument methods written as
  `Foo()`. A bare `Foo` also matches a `GetFoo()` method.
- Index into a collection with `wheels[2]`, or across all four wheels with `wheels[*]`,
  which requires declaring the channel as `TireSlip[]`.
- `* k` scales and `+ c` offsets. Use these for unit conversions — e.g. `* 0.01` if the
  game stores throttle as 0..100, or `* -1` to flip a steering sign. To subtract, add a
  negative: `+ -0.5`.

Examples:

```ini
Rpm         = CarController.engine.rpm
Throttle    = CarController.inputs.gas * 0.01
SteerAngle  = CarController.steerAngle * -1
Gear        = CarController.gearBox.currentGear
TireSlip[]  = CarController.wheels[*].slipAngle
DriftScore  = DriftScoreController.GetTotalScore()
```

## Locator settings

`[locator]` narrows the search for the player's car. The default heuristic scores every
non-kinematic `Rigidbody` in the scene by how many `WheelCollider`s hang off it, its
mass, whether a camera is parented to it, and whether any of its components match
`vehicleType`. That is usually enough.

If the game marks the locally driven car with a component of its own — something like
`LocalPlayerCar` or `PlayerInputController` — name it in `playerType` and the search
becomes exact instead of heuristic. Worth hunting for in a dump; it is the single
biggest reliability win in a profile.

## When a game update breaks it

Symptom: the dash still shows speed and slip angle, but RPM and gear go to zero, and the
BepInEx log says `channel 'Rpm' unresolved: path did not resolve to a number`.

Fix: dump again, find the renamed field, edit the profile. No rebuild, no reinstall.
That is the whole reason bindings live in a text file.
