using System;
using System.Collections.Generic;
using UnityEngine;

namespace CarX.Telemetry.Mod
{
    /// <summary>
    /// Finds the player's car in the live scene without knowing anything about the game's
    /// class names, then holds onto it until it is destroyed (car swap, scene change).
    ///
    /// The search is deliberately heuristic rather than hard-coded: a car in any Unity
    /// racing game is a Rigidbody with wheels under it, and that stays true across CarX
    /// versions even when every script name changes. A profile can narrow the search, but
    /// the fallback is meant to work on a title nobody has written a profile for yet.
    /// </summary>
    public sealed class VehicleLocator
    {
        private readonly Action<string> _log;

        private Rigidbody _body;
        private Transform _transform;
        private float _nextSearchTime;

        /// <summary>Components on the located car, keyed by type name, for MemberPath roots.</summary>
        private readonly Dictionary<string, Component> _components = new Dictionary<string, Component>(StringComparer.OrdinalIgnoreCase);

        public VehicleLocator(Action<string> log) => _log = log ?? (_ => { });

        public Rigidbody Body => _body;
        public Transform Transform => _transform;
        public bool HasVehicle => _body != null && _transform != null;

        /// <summary>Type name of the component that won the search, for the log and the UI.</summary>
        public string MatchedComponent { get; private set; }

        public Component GetComponentByTypeName(string typeName)
        {
            if (typeName == null) return null;
            return _components.TryGetValue(typeName, out var component) ? component : null;
        }

        /// <summary>
        /// Called every frame. Cheap when a car is already held; the expensive scene scan
        /// is rate-limited so that sitting in a menu does not cost anything measurable.
        /// </summary>
        public void Update(GameProfile profile, float now, float searchIntervalSeconds)
        {
            // Unity's overloaded == means a destroyed object compares equal to null here,
            // which is exactly the signal we want for "the car went away".
            if (_body != null && _transform != null) return;

            if (_body != null || _transform != null) Release();
            if (now < _nextSearchTime) return;

            _nextSearchTime = now + searchIntervalSeconds;
            Search(profile);
        }

        public void Release()
        {
            _body = null;
            _transform = null;
            MatchedComponent = null;
            _components.Clear();
        }

        private void Search(GameProfile profile)
        {
            Rigidbody best = null;
            Component bestMatch = null;
            var bestScore = int.MinValue;

            var bodies = UnityEngine.Object.FindObjectsOfType<Rigidbody>();
            foreach (var body in bodies)
            {
                if (body == null || body.isKinematic) continue;

                var score = ScoreCandidate(body, profile, out var match);
                if (score <= 0 || score <= bestScore) continue;

                bestScore = score;
                best = body;
                bestMatch = match;
            }

            if (best == null) return;

            _body = best;
            _transform = best.transform;
            MatchedComponent = bestMatch != null ? bestMatch.GetType().Name : "(none)";

            _components.Clear();
            foreach (var component in best.GetComponentsInChildren<Component>(true))
            {
                if (component == null) continue;
                var name = component.GetType().Name;
                // First one wins: the component on the root is more likely the controller
                // than a same-named script on some child prop.
                if (!_components.ContainsKey(name)) _components[name] = component;
            }

            _log($"located vehicle '{best.name}' via {MatchedComponent} " +
                 $"(score {bestScore}, {_components.Count} components visible)");
        }

        /// <summary>
        /// Scores how much a Rigidbody looks like the player's car. Zero or less means
        /// "not a car". The weights matter only relative to each other.
        /// </summary>
        private static int ScoreCandidate(Rigidbody body, GameProfile profile, out Component match)
        {
            match = null;

            var wheels = body.GetComponentsInChildren<WheelCollider>(true);
            if (wheels.Length < profile.MinWheelColliders) return 0;

            var score = wheels.Length * 10;

            // A car is heavy; debris and ragdolls are not.
            if (body.mass >= 500f) score += 20;

            foreach (var component in body.GetComponentsInChildren<Component>(true))
            {
                if (component == null) continue;
                var typeName = component.GetType().Name;

                if (profile.PlayerType != null && profile.PlayerType.IsMatch(typeName))
                {
                    // An explicit "this is the local player" marker outranks everything else.
                    score += 500;
                    match = component;
                    continue;
                }

                if (!profile.VehicleType.IsMatch(typeName)) continue;
                score += 30;
                if (match == null) match = component;
            }

            // A camera parented to the car is a strong hint that this is the one being driven.
            if (body.GetComponentInChildren<Camera>(true) != null) score += 60;

            // CompareTag throws if the project never declared the tag, which is not
            // something a telemetry mod should care about.
            try { if (body.CompareTag("Player")) score += 200; } catch { }

            return score;
        }
    }
}
