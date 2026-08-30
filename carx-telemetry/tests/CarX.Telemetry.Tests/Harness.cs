using System;
using System.Collections.Generic;

namespace CarX.Telemetry.Tests
{
    /// <summary>
    /// A deliberately tiny assertion harness. A test framework would be another package
    /// to restore in an environment that may not reach a feed, and this needs to run
    /// anywhere the SDK does.
    /// </summary>
    public static class Harness
    {
        private static readonly List<string> Failures = new List<string>();
        private static int _checks;
        private static string _section = "";

        public static void Section(string name)
        {
            _section = name;
            Console.WriteLine();
            Console.WriteLine("-- " + name);
        }

        public static void Check(bool condition, string what)
        {
            _checks++;
            if (condition)
            {
                Console.WriteLine("   ok   " + what);
                return;
            }

            Console.WriteLine("   FAIL " + what);
            Failures.Add(_section + ": " + what);
        }

        public static void Equal(double expected, double actual, string what, double tolerance = 1e-9)
        {
            Check(Math.Abs(expected - actual) <= tolerance, $"{what} (expected {expected}, got {actual})");
        }

        public static void Equal(string expected, string actual, string what)
        {
            Check(string.Equals(expected, actual, StringComparison.Ordinal),
                $"{what} (expected '{expected}', got '{actual}')");
        }

        public static int Report()
        {
            Console.WriteLine();
            if (Failures.Count == 0)
            {
                Console.WriteLine($"all {_checks} checks passed");
                return 0;
            }

            Console.WriteLine($"{Failures.Count} of {_checks} checks FAILED:");
            foreach (var failure in Failures) Console.WriteLine("  - " + failure);
            return 1;
        }
    }
}
