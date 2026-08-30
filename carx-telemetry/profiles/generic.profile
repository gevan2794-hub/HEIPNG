# generic.profile -- catch-all fallback.
#
# Matches any Unity game. Declares no powertrain channels, so what you get is exactly
# what the engine itself can tell us: speed, body-frame accelerations, rotation rates,
# attitude, position and chassis slip angle. That is already enough to drive a motion
# rig and most ShakeIt effects.
#
# To add RPM / gear / pedals for a game, copy this file, raise the priority, narrow the
# match, and fill in [channels] using the in-game dump key (default F9).

[profile]
name     = Generic Unity
match    = *
priority = 0

[locator]
# A component whose type name matches this marks its GameObject as a candidate car.
vehicleType       = (?i)(car|vehicle|drift|racer|chassis)
# Cars have wheels; debris does not.
minWheelColliders = 4

[channels]
# Intentionally empty. Engine-derived channels are always emitted.
