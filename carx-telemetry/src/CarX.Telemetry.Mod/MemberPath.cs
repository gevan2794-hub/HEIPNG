using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;

namespace CarX.Telemetry.Mod
{
    /// <summary>
    /// A late-bound accessor described by a string in a profile file, e.g.
    ///
    ///     Rpm       = CarController.engine.rpm
    ///     Throttle  = CarController.gasInput
    ///     Gear      = CarController.gearBox.currentGear
    ///     SteerAngle= CarController.steerAngle * 1.0 + 0.0
    ///     TireSlip[]= CarController.wheels[*].slipAngle
    ///
    /// The first segment names a Component type to look up on the vehicle (or its
    /// children); the rest is a chain of fields, properties, zero-arg methods
    /// (<c>Foo()</c>) and indexers (<c>wheels[2]</c>). Everything is resolved by name at
    /// runtime, so a game update that only shuffles field offsets costs nothing, and one
    /// that renames a field costs a text edit rather than a rebuild.
    /// </summary>
    public sealed class MemberPath
    {
        private static readonly Regex StepPattern = new Regex(
            @"^(?<name>[A-Za-z_][A-Za-z0-9_]*)(?<call>\(\))?(?:\[(?<index>\d+|\*)\])?$",
            RegexOptions.Compiled);

        private readonly List<Step> _steps = new List<Step>();

        /// <summary>Type name of the component the chain starts from.</summary>
        public string ComponentTypeName { get; private set; }

        public double Scale { get; private set; } = 1.0;
        public double Offset { get; private set; }

        /// <summary>
        /// True when the path contains <c>[*]</c>, meaning it resolves to four values
        /// (FL/FR/RL/RR) rather than one.
        /// </summary>
        public bool IsWheelArray { get; private set; }

        public string Raw { get; private set; }

        private MemberPath() { }

        public static bool TryParse(string raw, out MemberPath path, out string error)
        {
            path = null;
            error = null;

            if (string.IsNullOrWhiteSpace(raw)) { error = "empty path"; return false; }

            var result = new MemberPath { Raw = raw.Trim() };
            var expression = result.Raw;

            // Trailing "+ c" then "* k", peeled off right to left so the scale binds tighter.
            expression = PeelSuffix(expression, '+', v => result.Offset = v, ref error);
            if (error != null) return false;
            expression = PeelSuffix(expression, '*', v => result.Scale = v, ref error);
            if (error != null) return false;

            var segments = expression.Trim().Split('.');
            if (segments.Length < 2)
            {
                error = "expected at least <ComponentType>.<member>";
                return false;
            }

            result.ComponentTypeName = segments[0].Trim();
            if (result.ComponentTypeName.Length == 0) { error = "missing component type"; return false; }

            for (var i = 1; i < segments.Length; i++)
            {
                var match = StepPattern.Match(segments[i].Trim());
                if (!match.Success) { error = "bad path segment '" + segments[i] + "'"; return false; }

                var step = new Step
                {
                    Name = match.Groups["name"].Value,
                    IsCall = match.Groups["call"].Success,
                    Index = -1
                };

                if (match.Groups["index"].Success)
                {
                    var index = match.Groups["index"].Value;
                    if (index == "*")
                    {
                        if (result.IsWheelArray) { error = "only one [*] wildcard is allowed"; return false; }
                        result.IsWheelArray = true;
                        step.IsWildcard = true;
                    }
                    else
                    {
                        step.Index = int.Parse(index, CultureInfo.InvariantCulture);
                    }
                }

                result._steps.Add(step);
            }

            path = result;
            return true;
        }

        private static string PeelSuffix(string expression, char op, Action<double> assign, ref string error)
        {
            var at = expression.LastIndexOf(op);
            if (at < 0) return expression;

            var tail = expression.Substring(at + 1).Trim();
            if (!double.TryParse(tail, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                error = "'" + op + "' must be followed by a number, got '" + tail + "'";
                return expression;
            }

            assign(value);
            return expression.Substring(0, at);
        }

        /// <summary>
        /// Walks the chain from an already-resolved component instance. Returns false if
        /// any link is missing or null -- a car that has no gearbox yet is normal during
        /// spawn, not an error worth logging every frame.
        /// </summary>
        public bool TryEvaluate(object root, out double value)
        {
            value = 0;
            if (!TryWalk(root, -1, out var leaf)) return false;
            if (!TryCoerce(leaf, out value)) return false;
            value = value * Scale + Offset;
            return true;
        }

        /// <summary>Evaluates a <c>[*]</c> path at one wheel index.</summary>
        public bool TryEvaluateAt(object root, int wildcardIndex, out double value)
        {
            value = 0;
            if (!TryWalk(root, wildcardIndex, out var leaf)) return false;
            if (!TryCoerce(leaf, out value)) return false;
            value = value * Scale + Offset;
            return true;
        }

        private bool TryWalk(object current, int wildcardIndex, out object leaf)
        {
            leaf = null;

            foreach (var step in _steps)
            {
                if (current == null) return false;

                if (!TryReadMember(current, step, out current)) return false;

                var index = step.IsWildcard ? wildcardIndex : step.Index;
                if (index >= 0 && !TryIndex(current, index, out current)) return false;
            }

            leaf = current;
            return leaf != null;
        }

        private static bool TryReadMember(object target, Step step, out object value)
        {
            value = null;
            var members = MemberCache.For(target.GetType());

            if (step.IsCall)
            {
                if (!members.Methods.TryGetValue(step.Name, out var method)) return false;
                try { value = method.Invoke(target, null); }
                catch { return false; }
                return true;
            }

            if (members.Fields.TryGetValue(step.Name, out var field))
            {
                try { value = field.GetValue(target); } catch { return false; }
                return true;
            }

            if (members.Properties.TryGetValue(step.Name, out var property))
            {
                try { value = property.GetValue(target, null); } catch { return false; }
                return true;
            }

            // Some games expose the value only as a getter method; accept Foo for GetFoo().
            if (members.Methods.TryGetValue(step.Name, out var fallback))
            {
                try { value = fallback.Invoke(target, null); } catch { return false; }
                return true;
            }

            return false;
        }

        private static bool TryIndex(object container, int index, out object value)
        {
            value = null;
            if (container == null || index < 0) return false;

            if (container is IList list)
            {
                if (index >= list.Count) return false;
                value = list[index];
                return value != null;
            }

            // IEnumerable covers Unity's own collection wrappers, which are not always IList.
            if (container is IEnumerable sequence)
            {
                var position = 0;
                foreach (var item in sequence)
                {
                    if (position++ != index) continue;
                    value = item;
                    return value != null;
                }
            }

            return false;
        }

        private static bool TryCoerce(object raw, out double value)
        {
            value = 0;
            switch (raw)
            {
                case double d: value = d; return true;
                case float f: value = f; return true;
                case int i: value = i; return true;
                case long l: value = l; return true;
                case short s: value = s; return true;
                case byte b: value = b; return true;
                case sbyte sb: value = sb; return true;
                case uint ui: value = ui; return true;
                case ulong ul: value = ul; return true;
                case ushort us: value = us; return true;
                case decimal m: value = (double)m; return true;
                case bool flag: value = flag ? 1 : 0; return true;
                case Enum e: value = Convert.ToDouble(e, CultureInfo.InvariantCulture); return true;
            }

            return false;
        }

        private struct Step
        {
            public string Name;
            public bool IsCall;
            public bool IsWildcard;
            public int Index;
        }

        /// <summary>
        /// Name-to-member lookups are the expensive part of reflection, so they are done
        /// once per type. The actual GetValue calls are cheap enough at 60Hz.
        /// </summary>
        private sealed class MemberCache
        {
            private const BindingFlags Flags =
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;

            private static readonly Dictionary<Type, MemberCache> Cache = new Dictionary<Type, MemberCache>();

            public readonly Dictionary<string, FieldInfo> Fields = new Dictionary<string, FieldInfo>(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, PropertyInfo> Properties = new Dictionary<string, PropertyInfo>(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, MethodInfo> Methods = new Dictionary<string, MethodInfo>(StringComparer.OrdinalIgnoreCase);

            public static MemberCache For(Type type)
            {
                lock (Cache)
                {
                    if (Cache.TryGetValue(type, out var cached)) return cached;

                    var built = new MemberCache();
                    for (var t = type; t != null && t != typeof(object); t = t.BaseType)
                    {
                        foreach (var field in t.GetFields(Flags))
                            if (!built.Fields.ContainsKey(field.Name)) built.Fields[field.Name] = field;

                        foreach (var property in t.GetProperties(Flags))
                            if (property.CanRead && property.GetIndexParameters().Length == 0 &&
                                !built.Properties.ContainsKey(property.Name))
                                built.Properties[property.Name] = property;

                        foreach (var method in t.GetMethods(Flags))
                        {
                            if (method.GetParameters().Length != 0 || method.ReturnType == typeof(void)) continue;

                            if (!built.Methods.ContainsKey(method.Name)) built.Methods[method.Name] = method;

                            // Let "Rpm" also match "GetRpm()".
                            if (method.Name.StartsWith("Get", StringComparison.Ordinal) && method.Name.Length > 3)
                            {
                                var bare = method.Name.Substring(3);
                                if (!built.Methods.ContainsKey(bare)) built.Methods[bare] = method;
                            }
                        }
                    }

                    Cache[type] = built;
                    return built;
                }
            }
        }
    }
}
