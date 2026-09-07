# dro1.profile -- CarX Drift Racing Online (Steam 635260), moddable branch.
#
# Verified against a live F8 dump on a ToyotaGT86, not guessed. The physics component
# is CARXCar (the game's subclass of CarX.Car, same CARX-prefix pattern as
# CARXFollowCamera). Components are addressed by SIMPLE type name.

[profile]
name     = CarX Drift Racing Online
# The game reports its product name as "Drift Racing Online" -- no "CarX".
match    = (?i)(carx.?)?drift.?racing.?online(?!.?2)
priority = 10

[locator]
# RaceCar sits on the player's car. CarAI is present on it too, so AI is not a
# discriminator. InteriorMinimap and MirrorCameraHold only exist on the car you are
# actually driving, so they serve as the local-player marker (worth +500).
vehicleType       = (?i)racecar
playerType        = (?i)^(interiorminimap|mirrorcamerahold)$
# CarX ships ZERO Unity WheelColliders -- it has its own tyre model. Requiring 4 made
# the locator reject every car in the scene. The car does carry a real Rigidbody.
minWheelColliders = 0

[channels]
# --- powertrain ---
Rpm        = CARXCar.rpm
Gear       = CARXCar.gear
IdleRpm    = CARXCar.engineIdleRPM
# NOT engineCutRPM: that reads 300 and is the stall cutoff, not a redline. The
# gearbox upshift limit (7000) is the useful upper reference for gauges and shift lights.
MaxRpm     = CARXCar.gearBoxUpLimitRPM
TurboBoost = CARXCar.engineTurboPressure

# --- driver inputs ---
# Throttle is called 'accelerate' here -- not gas, not throttle.
Throttle   = CARXCar.accelerate
Brake      = CARXCar.brake
Handbrake  = CARXCar.handbrake
Clutch     = CARXCar.clutch
SteerAngle = CARXCar.steerAngle

# --- extras worth having for ShakeIt engine effects ---
EngineTorque = CARXCar.engineCurTorque
EnginePower  = CARXCar.engineCurPower
EngineLoad   = CARXCar.load
