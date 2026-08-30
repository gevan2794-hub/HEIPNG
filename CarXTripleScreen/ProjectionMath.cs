using System;
using UnityEngine;

namespace CarXTripleScreen
{
    /// <summary>
    /// Physical description of the rig. All lengths in millimetres.
    ///
    /// Units cancel out of every formula in this file (each one is a ratio of
    /// two lengths, or is scaled by near/d), so millimetres never need
    /// converting to Unity world units.
    /// </summary>
    public struct RigGeometry
    {
        public float ScreenWidthMm;
        public float ScreenHeightMm;
        public float BezelWidthMm;
        public float EyeDistanceMm;
        public float SideAngleDeg;
        public float EyeOffsetXMm;
        public float EyeOffsetYMm;
    }

    /// <summary>
    /// One flat panel, in eye space: the eye sits at the origin with +X right,
    /// +Y up and +Z forward. That matches Unity's transform-local convention,
    /// so eye space is exactly the local space of the parent (gameplay) camera.
    ///
    /// Corners follow the Kooima naming: Pa bottom-left, Pb bottom-right,
    /// Pc top-left, all as seen by the viewer.
    /// </summary>
    public struct ScreenQuad
    {
        public Vector3 Pa;
        public Vector3 Pb;
        public Vector3 Pc;

        public Vector3 Pd { get { return Pb + (Pc - Pa); } }        // top-right
        public Vector3 Center { get { return Pa + 0.5f * (Pb - Pa) + 0.5f * (Pc - Pa); } }

        public Vector3[] Corners { get { return new[] { Pa, Pb, Pc, Pd }; } }
    }

    /// <summary>Everything needed to drive one camera at one panel.</summary>
    public struct PanelSolution
    {
        public bool Valid;
        public string Error;

        // Tier 1
        public Quaternion LocalRotation;      // relative to the rig / parent camera
        public float VerticalFovDeg;
        public float HorizontalFovDeg;

        // Tier 2
        public Matrix4x4 Projection;
        public Matrix4x4 EyeToView;           // eye space -> OpenGL view space

        // Diagnostics
        public float YawDeg;
        public float PitchDeg;
        public float HorizontalAsymmetryDeg;  // 0 when the eye is on the panel's axis
        public float VerticalAsymmetryDeg;
        public float AspectMismatch;          // 1.0 == physical aspect matches pixel aspect
    }

    public static class ProjectionMath
    {
        public const int Left = 0;
        public const int Center = 1;
        public const int Right = 2;

        /// <summary>
        /// Build the three panels in eye space.
        ///
        /// The centre panel lies on the plane z = EyeDistanceMm. Each side panel
        /// hinges at the corresponding vertical edge of the centre panel and
        /// rotates inward (toward the eye) by SideAngleDeg. BezelWidthMm is the
        /// dead strip at that seam - the combined bezel of both panels - and is
        /// consumed along the side panel's surface before its glass begins.
        /// Modelling the bezel as real surface area is what makes the world
        /// "continue behind" the seam instead of duplicating across it.
        ///
        /// Panels are assumed vertical (no pitch/roll). A rig with tilted panels
        /// needs tier 2 plus hand-authored corners.
        /// </summary>
        public static ScreenQuad[] BuildRig(RigGeometry g)
        {
            float w = g.ScreenWidthMm;
            float h = g.ScreenHeightMm;
            float d = g.EyeDistanceMm;
            float b = g.BezelWidthMm;
            float th = g.SideAngleDeg * Mathf.Deg2Rad;

            // Centre-panel edges in eye space. The eye is offset from the panel
            // centre by (EyeOffsetX, EyeOffsetY), so the panel shifts the other way.
            float xl = -g.EyeOffsetXMm - w * 0.5f;
            float xr = -g.EyeOffsetXMm + w * 0.5f;
            float yb = -g.EyeOffsetYMm - h * 0.5f;
            float yt = -g.EyeOffsetYMm + h * 0.5f;

            Vector3 up = new Vector3(0f, h, 0f);

            ScreenQuad center;
            center.Pa = new Vector3(xl, yb, d);
            center.Pb = new Vector3(xr, yb, d);
            center.Pc = new Vector3(xl, yt, d);

            // Surface directions of the side panels, walking outward from the seam.
            // Rotating inward pulls the far edge toward the eye, hence -sin on Z.
            Vector3 uR = new Vector3(Mathf.Cos(th), 0f, -Mathf.Sin(th));
            Vector3 uL = new Vector3(-Mathf.Cos(th), 0f, -Mathf.Sin(th));

            Vector3 hingeR = new Vector3(xr, yb, d);
            ScreenQuad right;
            right.Pa = hingeR + uR * b;             // glass starts past the bezel
            right.Pb = hingeR + uR * (b + w);
            right.Pc = right.Pa + up;

            // On the left panel the outer edge is the viewer's left, so Pa is the
            // far corner and Pb is the one against the seam.
            Vector3 hingeL = new Vector3(xl, yb, d);
            ScreenQuad left;
            left.Pa = hingeL + uL * (b + w);
            left.Pb = hingeL + uL * b;
            left.Pc = left.Pa + up;

            return new[] { left, center, right };
        }

        /// <summary>
        /// Tier 1: a symmetric frustum aimed at the panel's centre.
        ///
        /// Exact for the centre panel with a centred eye. For the side panels it
        /// is exact only when the panel is perpendicular to the eye-to-panel-centre
        /// ray (the ideal "cylindrical" arrangement that SideAngleDeg assumes);
        /// HorizontalAsymmetryDeg reports how far from that the real rig sits.
        ///
        /// Horizontal extent drives the FOV, because horizontal continuity across
        /// the seams is what the eye actually notices. AspectMismatch reports how
        /// much vertical accuracy that costs - it is 1.0 when the measured
        /// physical aspect matches the panel's pixel aspect, and any meaningful
        /// deviation means a mismeasured panel or non-square pixels.
        /// </summary>
        public static PanelSolution SolveTier1(ScreenQuad q, float viewportAspect)
        {
            PanelSolution s = new PanelSolution();

            Vector3 c = q.Center;
            if (c.sqrMagnitude < 1e-6f || c.z <= 1e-4f)
            {
                s.Error = "panel centre is at or behind the eye - check EyeDistanceMm / SideAngleDeg";
                return s;
            }
            if (viewportAspect <= 1e-4f)
            {
                s.Error = "viewport aspect is zero - camera has no pixels";
                return s;
            }

            Quaternion rot = Quaternion.LookRotation(c.normalized, Vector3.up);
            Quaternion inv = Quaternion.Inverse(rot);

            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;

            Vector3[] corners = q.Corners;
            for (int i = 0; i < corners.Length; i++)
            {
                Vector3 p = inv * corners[i];
                if (p.z <= 1e-4f)
                {
                    s.Error = "a panel corner is at or behind the eye - geometry is degenerate";
                    return s;
                }
                float tx = p.x / p.z;
                float ty = p.y / p.z;
                if (tx < minX) minX = tx;
                if (tx > maxX) maxX = tx;
                if (ty < minY) minY = ty;
                if (ty > maxY) maxY = ty;
            }

            float tanH = Mathf.Max(Mathf.Abs(minX), Mathf.Abs(maxX));
            float tanV = Mathf.Max(Mathf.Abs(minY), Mathf.Abs(maxY));

            // Unity's fieldOfView is vertical; horizontal follows from the aspect.
            float tanVFromH = tanH / viewportAspect;

            s.Valid = true;
            s.LocalRotation = rot;
            s.VerticalFovDeg = 2f * Mathf.Atan(tanVFromH) * Mathf.Rad2Deg;
            s.HorizontalFovDeg = 2f * Mathf.Atan(tanH) * Mathf.Rad2Deg;
            s.AspectMismatch = tanV > 1e-6f ? tanVFromH / tanV : 1f;

            Vector3 e = rot.eulerAngles;
            s.YawDeg = Mathf.DeltaAngle(0f, e.y);
            s.PitchDeg = Mathf.DeltaAngle(0f, e.x);

            // A symmetric frustum fits perfectly only when the panel's angular
            // bounds are symmetric about the aim direction.
            s.HorizontalAsymmetryDeg = Mathf.Abs(Mathf.Atan(maxX) + Mathf.Atan(minX)) * Mathf.Rad2Deg;
            s.VerticalAsymmetryDeg = Mathf.Abs(Mathf.Atan(maxY) + Mathf.Atan(minY)) * Mathf.Rad2Deg;
            return s;
        }

        /// <summary>
        /// Tier 2: generalised off-axis projection (Kooima). Exact for any eye
        /// position and any panel placement.
        ///
        /// Handedness: the published derivation takes vn = cross(vr, vu), which
        /// points away from the viewer. Unity's view space looks down -Z, so vn
        /// is taken as cross(vu, vr) here - pointing back toward the eye - which
        /// makes d positive and makes EyeToView reduce to the familiar
        /// Matrix4x4.Scale(1, 1, -1) for a centred, axis-aligned panel. That
        /// identity is the cheapest available check that the sign is right.
        /// </summary>
        public static PanelSolution SolveTier2(ScreenQuad q, Vector3 eye, float near, float far, float viewportAspect)
        {
            // Reuse tier 1 for the transform + diagnostics; tier 2 overrides the
            // matrices but the clone transform should still roughly aim at the
            // panel, because culling, shadows and audio read the transform.
            PanelSolution s = SolveTier1(q, viewportAspect);
            if (!s.Valid) return s;

            Vector3 vr = (q.Pb - q.Pa).normalized;
            Vector3 vu = (q.Pc - q.Pa).normalized;
            Vector3 vn = Vector3.Cross(vu, vr).normalized;

            Vector3 va = q.Pa - eye;
            Vector3 vb = q.Pb - eye;
            Vector3 vc = q.Pc - eye;

            float d = -Vector3.Dot(vn, va);
            if (d < 1e-4f)
            {
                s.Valid = false;
                s.Error = "eye is on or behind the panel plane (d <= 0)";
                return s;
            }
            if (near <= 0f || far <= near)
            {
                s.Valid = false;
                s.Error = "invalid near/far clip planes";
                return s;
            }

            float k = near / d;
            float l = Vector3.Dot(vr, va) * k;
            float r = Vector3.Dot(vr, vb) * k;
            float b = Vector3.Dot(vu, va) * k;
            float t = Vector3.Dot(vu, vc) * k;

            if (Mathf.Abs(r - l) < 1e-9f || Mathf.Abs(t - b) < 1e-9f)
            {
                s.Valid = false;
                s.Error = "degenerate frustum bounds";
                return s;
            }

            s.Projection = Matrix4x4.Frustum(l, r, b, t, near, far);

            Matrix4x4 v = Matrix4x4.identity;
            v.SetRow(0, new Vector4(vr.x, vr.y, vr.z, -Vector3.Dot(vr, eye)));
            v.SetRow(1, new Vector4(vu.x, vu.y, vu.z, -Vector3.Dot(vu, eye)));
            v.SetRow(2, new Vector4(vn.x, vn.y, vn.z, -Vector3.Dot(vn, eye)));
            v.SetRow(3, new Vector4(0f, 0f, 0f, 1f));
            s.EyeToView = v;

            return s;
        }

        /// <summary>
        /// The SideAngleDeg that would make each side panel perpendicular to the
        /// ray from the eye to its own centre - i.e. the angle at which tier 1
        /// becomes exact. Printed as a hint when asymmetry is large.
        /// </summary>
        public static float IdealSideAngleDeg(RigGeometry g)
        {
            // Solved by iteration: the ideal angle equals the direction angle to
            // the panel centre, which itself depends on the angle. Converges in
            // a handful of steps for any sane rig.
            float th = g.SideAngleDeg;
            for (int i = 0; i < 64; i++)
            {
                RigGeometry probe = g;
                probe.SideAngleDeg = th;
                ScreenQuad[] r = BuildRig(probe);
                Vector3 c = r[Right].Center;
                if (c.z <= 1e-4f) break;
                float aim = Mathf.Atan2(c.x, c.z) * Mathf.Rad2Deg;
                if (Mathf.Abs(aim - th) < 1e-4f) return aim;
                th = Mathf.Lerp(th, aim, 0.5f);
            }
            return th;
        }
    }
}
