using System;
using System.Collections.Generic;
using CarX.Telemetry.Mod;
using static CarX.Telemetry.Tests.Harness;

namespace CarX.Telemetry.Tests
{
    public static class Program
    {
        public static int Main()
        {
            Console.WriteLine("CarX Telemetry Bridge -- logic tests");

            JsonRoundTrip();
            JsonRejectsGarbage();
            PathParsing();
            PathEvaluation();
            ProfileParsing();
            ProfileSelection();

            return Report();
        }

        // ---------------------------------------------------------------- FlatJson

        private static void JsonRoundTrip()
        {
            Section("FlatJson round trip");

            var frame = new TelemetryFrame();
            frame.Reset(4242, 1234.5, true);
            frame.Set(Channels.Rpm, 5310.25);
            frame.Set(Channels.SlipAngle, -28.5);
            frame.Set(Channels.Gear, 3);
            frame.Set(Channels.CarName, "Toyota \"Drift\" AE86");
            frame.Set(Channels.TrackName, "Line\tBreak\nTrack");

            var json = FlatJson.Write(frame);
            var numbers = new Dictionary<string, double>();
            var strings = new Dictionary<string, string>();

            Check(FlatJson.TryRead(json, numbers, strings), "writer output parses back");
            Equal(4242, numbers["Sequence"], "sequence survives");
            Equal(1234.5, numbers["TimestampMs"], "timestamp survives");
            Equal(1, numbers["InCar"], "InCar=true reads as 1");
            Equal(5310.25, numbers[Channels.Rpm], "positive fraction survives");
            Equal(-28.5, numbers[Channels.SlipAngle], "negative value survives");
            Equal("Toyota \"Drift\" AE86", strings[Channels.CarName], "embedded quotes survive escaping");
            Equal("Line\tBreak\nTrack", strings[Channels.TrackName], "tab and newline survive escaping");

            // Every consumer here is a dashboard: a non-finite value must not be able to
            // produce a packet that fails to parse and takes the whole frame with it.
            var nonFinite = new TelemetryFrame();
            nonFinite.Reset(1, 0, true);
            nonFinite.Set("Nan", double.NaN);
            nonFinite.Set("Inf", double.PositiveInfinity);
            nonFinite.Set("NegInf", double.NegativeInfinity);

            var sanitised = new Dictionary<string, double>();
            Check(FlatJson.TryRead(FlatJson.Write(nonFinite), sanitised, new Dictionary<string, string>()),
                "NaN/Infinity still produce parseable JSON");
            Equal(0, sanitised["Nan"], "NaN becomes 0");
            Equal(0, sanitised["Inf"], "+Infinity becomes 0");
            Equal(0, sanitised["NegInf"], "-Infinity becomes 0");

            var menu = new TelemetryFrame();
            menu.Reset(9, 1, false);
            var menuNumbers = new Dictionary<string, double>();
            Check(FlatJson.TryRead(FlatJson.Write(menu), menuNumbers, new Dictionary<string, string>()),
                "a frame with no channels parses");
            Equal(0, menuNumbers["InCar"], "InCar=false reads as 0");
        }

        private static void JsonRejectsGarbage()
        {
            Section("FlatJson rejects malformed input without throwing");

            var cases = new[]
            {
                ("", "empty string"),
                ("null", "bare null"),
                ("[1,2,3]", "an array"),
                ("{", "unterminated object"),
                ("{\"a\"}", "key with no value"),
                ("{\"a\":}", "missing value"),
                ("{\"a\":1,}", "trailing comma"),
                ("{\"a\":1 \"b\":2}", "missing comma"),
                ("{\"a\":--3}", "malformed number"),
                ("{\"a\":maybe}", "unknown literal"),
                ("{\"unterminated:1}", "unterminated key"),
                ("{\"a\":\"\\q\"}", "invalid escape"),
                ("{\"a\":\"\\u00\"}", "truncated unicode escape"),
            };

            foreach (var (input, description) in cases)
            {
                var threw = false;
                var accepted = false;
                try
                {
                    accepted = FlatJson.TryRead(input, new Dictionary<string, double>(), new Dictionary<string, string>());
                }
                catch
                {
                    threw = true;
                }

                Check(!threw && !accepted, $"rejects {description}");
            }

            var valid = new Dictionary<string, double>();
            var validStrings = new Dictionary<string, string>();
            Check(FlatJson.TryRead("{}", valid, validStrings), "accepts an empty object");
            Check(FlatJson.TryRead("  { \"a\" : 1.5e2 , \"b\" : \"\\u0041\" }  ", valid, validStrings),
                "accepts whitespace, exponents and unicode escapes");
            Equal(150, valid["a"], "exponent notation parses");
            Equal("A", validStrings["b"], "\\u0041 decodes to A");
        }

        // -------------------------------------------------------------- MemberPath

        private static void PathParsing()
        {
            Section("MemberPath parsing");

            Check(MemberPath.TryParse("CarController.engineRpm", out var simple, out _), "parses a simple path");
            Equal("CarController", simple.ComponentTypeName, "component type extracted");
            Equal(1.0, simple.Scale, "default scale is 1");
            Equal(0.0, simple.Offset, "default offset is 0");
            Check(!simple.IsWheelArray, "not a wheel array");

            Check(MemberPath.TryParse("Car.inputs.gas * 0.01", out var scaled, out _), "parses a scale suffix");
            Equal(0.01, scaled.Scale, "scale parsed");

            Check(MemberPath.TryParse("Car.temp * 2 + -40", out var affine, out _), "parses scale and negative offset");
            Equal(2.0, affine.Scale, "scale parsed alongside offset");
            Equal(-40.0, affine.Offset, "negative offset parsed");

            Check(MemberPath.TryParse("Car.wheels[*].slip", out var wildcard, out _), "parses a wheel wildcard");
            Check(wildcard.IsWheelArray, "wildcard flagged as a wheel array");

            Check(!MemberPath.TryParse("", out _, out _), "rejects an empty path");
            Check(!MemberPath.TryParse("JustAType", out _, out _), "rejects a path with no member");
            Check(!MemberPath.TryParse("Car.a[*].b[*]", out _, out _), "rejects two wildcards");
            Check(!MemberPath.TryParse("Car.field * abc", out _, out _), "rejects a non-numeric scale");
            Check(!MemberPath.TryParse("Car.bad-name", out _, out _), "rejects an invalid identifier");
        }

        private enum Drivetrain { Fwd = 0, Rwd = 1, Awd = 2 }

        private sealed class Wheel
        {
            public float slip;
            public bool grounded = true;
        }

        private sealed class Engine
        {
            public float rpm = 3000f;
        }

        private sealed class FakeCar
        {
            public Engine engine = new Engine();
            public int currentGear = 3;
            public float gasInput = 0.42f;
            private readonly float _hidden = 7.5f;
            public bool handbrake = true;
            public Drivetrain layout = Drivetrain.Rwd;
            public string badge = "AE86";
            public Engine nullEngine = null;

            public double MaxRpm { get; } = 7500;
            public float GetBoost() => 1.25f;
            public float Throws => throw new InvalidOperationException("this property is booby-trapped");

            public List<Wheel> wheels = new List<Wheel>
            {
                new Wheel { slip = 1f }, new Wheel { slip = 2f },
                new Wheel { slip = 3f }, new Wheel { slip = 4f }
            };

            public float PrivateProbe() => _hidden;
        }

        private static void PathEvaluation()
        {
            Section("MemberPath evaluation");

            var car = new FakeCar();

            Check(Evaluate(car, "Car.engine.rpm", out var rpm), "resolves a nested field");
            Equal(3000, rpm, "nested field value");

            Check(Evaluate(car, "Car.currentGear", out var gear), "resolves an int field");
            Equal(3, gear, "int coerced to double");

            Check(Evaluate(car, "Car.MaxRpm", out var maxRpm), "resolves a property");
            Equal(7500, maxRpm, "property value");

            Check(Evaluate(car, "Car.GetBoost()", out var boost), "resolves an explicit method call");
            Equal(1.25, boost, "method return value");

            Check(Evaluate(car, "Car.Boost", out var bareBoost), "bare name falls back to GetBoost()");
            Equal(1.25, bareBoost, "Get-prefixed method value");

            Check(Evaluate(car, "Car.handbrake", out var handbrake), "resolves a bool");
            Equal(1, handbrake, "true coerces to 1");

            Check(Evaluate(car, "Car.layout", out var layout), "resolves an enum");
            Equal(1, layout, "enum coerces to its numeric value");

            Check(Evaluate(car, "Car._hidden", out var hidden), "resolves a private field");
            Equal(7.5, hidden, "private field value");

            Check(Evaluate(car, "Car.gasInput * 100", out var scaled), "applies scale");
            Equal(42, scaled, "scaled value", 1e-4);

            Check(Evaluate(car, "Car.engine.rpm * 0.001 + 10", out var affine), "applies scale and offset");
            Equal(13, affine, "affine value", 1e-6);

            Check(Evaluate(car, "Car.wheels[2].slip", out var indexed), "resolves a fixed index");
            Equal(3, indexed, "indexed value");

            // Failures that must be quiet: a car mid-spawn legitimately has null links,
            // and a profile written for another version legitimately names missing fields.
            Check(!Evaluate(car, "Car.nope", out _), "missing member fails quietly");
            Check(!Evaluate(car, "Car.nullEngine.rpm", out _), "null mid-chain fails quietly");
            Check(!Evaluate(car, "Car.badge", out _), "a string member is not a number");
            Check(!Evaluate(car, "Car.wheels[99].slip", out _), "out-of-range index fails quietly");
            Check(!Evaluate(car, "Car.Throws", out _), "a throwing property fails quietly");

            Check(MemberPath.TryParse("Car.wheels[*].slip", out var wildcard, out _), "wildcard path parses");
            var wheelValues = new List<double>();
            for (var i = 0; i < 4; i++)
            {
                Check(wildcard.TryEvaluateAt(car, i, out var value), $"wildcard resolves at index {i}");
                wheelValues.Add(value);
            }
            Equal(1, wheelValues[0], "wheel 0");
            Equal(4, wheelValues[3], "wheel 3");
            Check(!wildcard.TryEvaluateAt(car, 7, out _), "wildcard past the end fails quietly");
        }

        private static bool Evaluate(object root, string path, out double value)
        {
            value = 0;
            return MemberPath.TryParse(path, out var parsed, out _) && parsed.TryEvaluate(root, out value);
        }

        // ------------------------------------------------------------- GameProfile

        private static void ProfileParsing()
        {
            Section("GameProfile parsing");

            const string text = @"
# a comment
; another comment

[profile]
name     = Test Game
match    = (?i)testgame
priority = 7

[locator]
vehicleType       = (?i)(car|vehicle)
playerType        = (?i)localplayer
minWheelColliders = 3

[channels]
Rpm        = CarController.engineRpm
Throttle   = CarController.gas * 0.01
TireSlip[] = CarController.wheels[*].slip
";

            var profile = GameProfile.Parse(text, "test.profile");

            Equal("Test Game", profile.Name, "name parsed");
            Equal(7, profile.Priority, "priority parsed");
            Equal(3, profile.MinWheelColliders, "minWheelColliders parsed");
            Check(profile.Match.IsMatch("TestGame"), "match regex is case-insensitive as written");
            Check(profile.PlayerType != null && profile.PlayerType.IsMatch("LocalPlayerCar"), "playerType parsed");
            Check(profile.Channels.Count == 2, $"two scalar channels parsed (got {profile.Channels.Count})");
            Check(profile.WheelChannels.Count == 1, $"one wheel channel parsed (got {profile.WheelChannels.Count})");
            Check(profile.Warnings.Count == 0, $"clean profile produces no warnings (got {profile.Warnings.Count})");

            // A typo in a profile must degrade to a warning, never take the mod down.
            const string broken = @"
[profile]
name = Broken
priority = notanumber

[locator]
vehicleType = (unclosed
unknownKey = 1

[channels]
Good      = Car.rpm
NoWheel[] = Car.rpm
Wheel     = Car.wheels[*].slip
Bad       = notapath
JustNoise
";
            var brokenProfile = GameProfile.Parse(broken, "broken.profile");

            Equal("Broken", brokenProfile.Name, "name still parsed despite later errors");
            Check(brokenProfile.Channels.Count == 1, $"only the good channel is kept (got {brokenProfile.Channels.Count})");
            Check(brokenProfile.WheelChannels.Count == 0, "mismatched wheel channels are dropped");
            Check(brokenProfile.Warnings.Count >= 6,
                $"every problem is reported (got {brokenProfile.Warnings.Count} warnings)");

            var empty = GameProfile.Parse("", "empty.profile");
            Check(empty.Channels.Count == 0, "an empty profile parses to no channels");
            Check(empty.Match.IsMatch("anything"), "an empty profile matches everything by default");
        }

        private static void ProfileSelection()
        {
            Section("GameProfile selection");

            var generic = GameProfile.Parse("[profile]\nname = Generic\nmatch = *\npriority = 0\n", "g");
            var dro1 = GameProfile.Parse("[profile]\nname = DRO1\nmatch = (?i)carx.?drift.?racing.?online(?!.?2)\npriority = 10\n", "1");
            var dro2 = GameProfile.Parse("[profile]\nname = DRO2\nmatch = (?i)carx.?drift.?racing.?online.?2\npriority = 20\n", "2");
            var all = new[] { generic, dro1, dro2 };

            Equal("DRO2", GameProfile.Select(all, "CarX Drift Racing Online 2").Name, "DRO2 wins for DRO2");
            Equal("DRO1", GameProfile.Select(all, "CarX Drift Racing Online").Name,
                "DRO1 wins for DRO1 (the negative lookahead keeps DRO2 out)");
            Equal("Generic", GameProfile.Select(all, "Some Other Racing Game").Name, "generic catches everything else");
            Equal("DRO2", GameProfile.Select(all, "", "CarXDriftRacingOnline2").Name,
                "process name is matched when the product name is blank");
            Check(GameProfile.Select(Array.Empty<GameProfile>(), "anything") == null, "no profiles selects nothing");
        }
    }
}
