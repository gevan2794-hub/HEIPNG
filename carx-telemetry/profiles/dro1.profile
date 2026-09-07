# dro1.profile -- CarX Drift Racing Online (Steam 635260), moddable branch.
#
# Filled in by reading the game's own assemblies, not by dumping in game:
#   Assembly-CSharp.dll      -> RaceCar : BaseCar is the player's car component
#   CarX.Plugins.Core.dll    -> CarX.Car is the physics/powertrain MonoBehaviour
#
# Channels address components by their SIMPLE type name, so CarX.Car is "Car".

[profile]
name     = CarX Drift Racing Online
# The game reports its product name as "Drift Racing Online" -- no "CarX" -- so the
# old pattern never matched and it fell through to the generic profile.
match    = (?i)(carx.?)?drift.?racing.?online(?!.?2)
priority = 10

[locator]
# RaceCar is the player's car; CarAI drives the opponents. Both carry RaceCar, so the
# camera and Player-tag bonuses in the scorer are what separate them.
vehicleType       = (?i)racecar
# CarX ships ZERO Unity WheelColliders -- it has its own tyre model (CarX.Car,
# TiresConfig, WheelIndex[]). Requiring 4 made the locator reject every car in the
# scene, which is why nothing was ever found. The car does have a real Rigidbody
# (BaseCar.getRigidbody -> CarX.Car.getRigidbody), so the engine-derived channels work.
minWheelColliders = 0

[channels]
# --- powertrain ---
Rpm        = Car.rpm
Gear       = Car.gear
IdleRpm    = Car.engineIdleRPM
MaxRpm     = Car.engineCutRPM
TurboBoost = Car.engineTurboPressure

# --- driver inputs ---
Brake      = Car.brake
Handbrake  = Car.handbrake
Clutch     = Car.clutch
SteerAngle = Car.steerAngle

# Throttle has no obviously-named property on CarX.Car; it is not "gas" or "throttle".
# Left out rather than guessed. Everything else here is read off the real assembly.
