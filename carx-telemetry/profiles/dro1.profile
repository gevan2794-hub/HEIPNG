# dro1.profile -- CarX Drift Racing Online (Steam 635260, Unity 2019, Mono).
#
# STATUS: locator settings are game-specific; the [channels] block is a TEMPLATE.
# The field names below are commented out because they have NOT been verified against a
# current build -- do not assume they are right. Fill them in yourself:
#
#   1. Launch with the mod installed, get into a car, hold a known state
#      (e.g. sit at idle, then hold ~3000rpm in 2nd gear).
#   2. Press F9. The BepInEx console prints every component on the car and every
#      numeric field with its live value.
#   3. Find the field whose value matches the in-game HUD, and uncomment/edit the
#      matching line below using  <ComponentTypeName>.<field chain>  syntax.
#   4. Save; restart the game (profiles are read at startup).
#
# Because this game is Mono, you can also just open Assembly-CSharp.dll in dnSpy or
# ILSpy and read the real field names straight out of the car controller class, which is
# faster than hunting through a dump. See docs/FINDING-FIELDS.md.

[profile]
name     = CarX Drift Racing Online
match    = (?i)carx.?drift.?racing.?online(?!.?2)
priority = 10

[locator]
vehicleType       = (?i)(car|vehicle|drift)
minWheelColliders = 4

[channels]
# --- powertrain ---
# Rpm        = CarController.engineRpm
# MaxRpm     = CarController.maxRpm
# IdleRpm    = CarController.idleRpm
# Gear       = CarController.currentGear
# TurboBoost = CarController.turboPressure

# --- driver inputs, expected 0..1 (add "* 0.01" if the game stores 0..100) ---
# Throttle   = CarController.gasInput
# Brake      = CarController.brakeInput
# Clutch     = CarController.clutchInput
# Handbrake  = CarController.handbrakeInput
# SteerAngle = CarController.steerAngle

# --- per-wheel, expanded to FL/FR/RL/RR in array order ---
# TireSlip[]      = CarController.wheels[*].slipAngle
# WheelSpeed[]    = CarController.wheels[*].rpm
# SuspTravel[]    = CarController.wheels[*].suspensionTravel
# WheelGrounded[] = CarController.wheels[*].isGrounded

# --- scoring ---
# DriftScore = DriftScoreController.totalScore
# DriftCombo = DriftScoreController.comboMultiplier
