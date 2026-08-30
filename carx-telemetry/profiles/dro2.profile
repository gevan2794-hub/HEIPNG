# dro2.profile -- CarX Drift Racing Online 2 (Steam 1826420).
#
# STATUS: TEMPLATE. This game is in Early Access and its internals are a moving target,
# so no field names are asserted here. The locator settings and the engine-derived
# channels work without any of this; fill in [channels] with the F9 dump to add RPM,
# gear and pedals. Expect to redo it after significant updates -- that is a text edit,
# not a rebuild.
#
# If this build is IL2CPP (check for a GameAssembly.dll next to the exe), you need the
# BepInEx 6 IL2CPP release and the IL2CPP build of this mod. See docs/FINDING-FIELDS.md
# for dumping IL2CPP metadata with Cpp2IL / Il2CppDumper to get real names.

[profile]
name     = CarX Drift Racing Online 2
match    = (?i)carx.?drift.?racing.?online.?2
priority = 20

[locator]
vehicleType       = (?i)(car|vehicle|drift|chassis)
# If the game marks the locally driven car with its own component, naming it here makes
# the search exact instead of heuristic -- worth finding in a dump.
# playerType      = (?i)(localplayer|playercar|driverinput)
minWheelColliders = 4

[channels]
# --- powertrain ---
# Rpm        = <Component>.<field>
# MaxRpm     = <Component>.<field>
# Gear       = <Component>.<field>

# --- driver inputs (0..1) ---
# Throttle   = <Component>.<field>
# Brake      = <Component>.<field>
# Handbrake  = <Component>.<field>
# SteerAngle = <Component>.<field>

# --- per-wheel ---
# TireSlip[] = <Component>.wheels[*].<field>

# --- scoring ---
# DriftScore = <Component>.<field>
