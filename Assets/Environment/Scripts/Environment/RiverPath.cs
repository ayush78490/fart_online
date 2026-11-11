using System;
using System.Collections.Generic;
using UnityEngine;

namespace FartOnline.Environment
{
    /// <summary>One editable control point of the river. Position.y is the water surface height.</summary>
    [Serializable]
    public struct RiverPoint
    {
        public Vector3 position;
        [Tooltip("Water width at this point (m)")] public float width;
        [Tooltip("Flat shore between the water edge and the foot of the canyon wall (m)")] public float bankFlat;
        [Tooltip("Canyon wall height above the water (m)")] public float wallHeight;
        [Tooltip("Horizontal run of the wall from its foot to its top (m); small = steep, negative = overhang")] public float wallRun;
        [Tooltip("False for dry spans (e.g. the jump gap): no water surface, no buoyancy")] public bool hasWater;
        [Tooltip("Downstream current speed (m/s)")] public float flowSpeed;
        public string section;

        public RiverPoint(Vector3 p, float w, float flat, float wallH, float run, float flow, string sec, bool water = true)
        {
            position = p; width = w; bankFlat = flat; wallHeight = wallH; wallRun = run; flowSpeed = flow; section = sec; hasWater = water;
        }
    }

    /// <summary>A resolved point on the river spline.</summary>
    public struct RiverSample
    {
        public Vector3 position;      // centreline at the water surface
        public Vector3 tangent;       // normalised, downstream (horizontal component dominates)
        public Vector3 right;         // horizontal, perpendicular to the flow
        public float width, bankFlat, wallHeight, wallRun, flowSpeed;
        public bool hasWater;
        public float distance;        // metres along the river from the start
        public float t;               // spline parameter (segment index + fraction)
        public string section;
    }

    /// <summary>
    /// Spline-driven river layout (centripetal Catmull-Rom through the control points). Drives the water mesh,
    /// banks, canyon walls, obstacle and checkpoint placement, buoyancy, current and camera look-ahead.
    /// </summary>
    [ExecuteAlways]
    public class RiverPath : MonoBehaviour
    {
        public List<RiverPoint> points = new List<RiverPoint>();
        [Tooltip("Spacing of the cached samples used for meshes and queries (m)")] public float sampleSpacing = 1.0f;
        [Header("Gizmos")]
        public bool drawGizmos = true;
        [Tooltip("Half-width of the raft clearance envelope drawn along the centreline (m)")] public float raftClearanceHalfWidth = 2.95f;

        [NonSerialized] List<RiverSample> cache;
        [NonSerialized] int cacheHash;
        /// <summary>Distance of the first control point from the river's origin. Grows when an endless river trims
        /// the points behind the raft, so every distance stays absolute (score, chunk and obstacle positions).</summary>
        [NonSerialized] public float distanceOffset;
        /// <summary>Distance of the first sample (equals <see cref="distanceOffset"/>).</summary>
        public float StartDistance { get { var s = Samples; return s.Count > 0 ? s[0].distance : distanceOffset; } }

        public float Length { get { var s = Samples; return s.Count > 0 ? s[s.Count - 1].distance : 0f; } }

        public List<RiverSample> Samples
        {
            get
            {
                int h = ComputeHash();
                if (cache == null || h != cacheHash) { cache = BuildSamples(sampleSpacing); cacheHash = h; }
                return cache;
            }
        }

        public void Invalidate() => cache = null;

        int ComputeHash()
        {
            unchecked
            {
                int h = points.Count * 397 ^ sampleSpacing.GetHashCode() ^ distanceOffset.GetHashCode() * 3;
                foreach (var p in points)
                    h = h * 31 + p.position.GetHashCode() + p.width.GetHashCode() * 7 + p.wallHeight.GetHashCode() * 13 +
                        p.bankFlat.GetHashCode() * 17 + p.hasWater.GetHashCode() + p.wallRun.GetHashCode() * 19 + p.flowSpeed.GetHashCode() * 23;
                h ^= transform.localToWorldMatrix.GetHashCode();
                return h;
            }
        }

        // ------------------------------------------------------------------ spline evaluation
        Vector3 P(int i) => transform.TransformPoint(points[Mathf.Clamp(i, 0, points.Count - 1)].position);

        /// <summary>Centripetal Catmull-Rom position for parameter t in [0, points.Count - 1].</summary>
        public Vector3 Evaluate(float t)
        {
            int n = points.Count;
            if (n == 0) return transform.position;
            if (n == 1) return P(0);
            t = Mathf.Clamp(t, 0f, n - 1);
            int i = Mathf.Min(Mathf.FloorToInt(t), n - 2);
            float u = t - i;
            Vector3 p0 = i > 0 ? P(i - 1) : P(0) * 2f - P(1);
            Vector3 p1 = P(i), p2 = P(i + 1);
            Vector3 p3 = i + 2 < n ? P(i + 2) : P(n - 1) * 2f - P(n - 2);
            return CentripetalCR(p0, p1, p2, p3, u);
        }

        static Vector3 CentripetalCR(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float u)
        {
            float t0 = 0f;
            float t1 = t0 + Mathf.Pow(Mathf.Max(1e-4f, Vector3.Distance(p0, p1)), 0.5f);
            float t2 = t1 + Mathf.Pow(Mathf.Max(1e-4f, Vector3.Distance(p1, p2)), 0.5f);
            float t3 = t2 + Mathf.Pow(Mathf.Max(1e-4f, Vector3.Distance(p2, p3)), 0.5f);
            float t = Mathf.Lerp(t1, t2, u);
            Vector3 a1 = (t1 - t) / (t1 - t0) * p0 + (t - t0) / (t1 - t0) * p1;
            Vector3 a2 = (t2 - t) / (t2 - t1) * p1 + (t - t1) / (t2 - t1) * p2;
            Vector3 a3 = (t3 - t) / (t3 - t2) * p2 + (t - t2) / (t3 - t2) * p3;
            Vector3 b1 = (t2 - t) / (t2 - t0) * a1 + (t - t0) / (t2 - t0) * a2;
            Vector3 b2 = (t3 - t) / (t3 - t1) * a2 + (t - t1) / (t3 - t1) * a3;
            return (t2 - t) / (t2 - t1) * b1 + (t - t1) / (t2 - t1) * b2;
        }

        static float Smooth(float a, float b, float u) => Mathf.Lerp(a, b, u * u * (3f - 2f * u));

        RiverSample Resolve(float t)
        {
            int n = points.Count;
            int i = Mathf.Clamp(Mathf.FloorToInt(t), 0, Mathf.Max(0, n - 2));
            float u = Mathf.Clamp01(t - i);
            var a = points[i];
            var b = points[Mathf.Min(i + 1, n - 1)];
            var s = new RiverSample
            {
                t = t,
                position = Evaluate(t),
                width = Smooth(a.width, b.width, u),
                bankFlat = Smooth(a.bankFlat, b.bankFlat, u),
                wallHeight = Smooth(a.wallHeight, b.wallHeight, u),
                wallRun = Smooth(a.wallRun, b.wallRun, u),
                flowSpeed = Smooth(a.flowSpeed, b.flowSpeed, u),
                hasWater = a.hasWater && b.hasWater,
                section = u < 0.5f ? a.section : b.section,
            };
            Vector3 d = Evaluate(Mathf.Min(t + 0.01f, n - 1)) - Evaluate(Mathf.Max(t - 0.01f, 0f));
            s.tangent = d.sqrMagnitude > 1e-8f ? d.normalized : transform.forward;
            Vector3 flat = new Vector3(s.tangent.x, 0f, s.tangent.z);
            s.right = flat.sqrMagnitude > 1e-8f ? Vector3.Cross(Vector3.up, flat.normalized) : transform.right;
            return s;
        }

        /// <summary>Samples roughly every <paramref name="spacing"/> metres of arc length.</summary>
        public List<RiverSample> BuildSamples(float spacing)
        {
            var list = new List<RiverSample>();
            int n = points.Count;
            if (n < 2) return list;
            // dense pass to measure arc length
            int dense = (n - 1) * 64;
            var ts = new float[dense + 1];
            var acc = new float[dense + 1];
            Vector3 prev = Evaluate(0f);
            for (int k = 0; k <= dense; k++)
            {
                float t = (n - 1) * k / (float)dense;
                Vector3 p = Evaluate(t);
                ts[k] = t;
                acc[k] = k == 0 ? 0f : acc[k - 1] + Vector3.Distance(prev, p);
                prev = p;
            }
            float total = acc[dense];
            int count = Mathf.Max(2, Mathf.CeilToInt(total / Mathf.Max(0.1f, spacing)) + 1);
            int j = 0;
            for (int c = 0; c < count; c++)
            {
                float target = total * c / (count - 1);
                while (j < dense - 1 && acc[j + 1] < target) j++;
                float seg = acc[j + 1] - acc[j];
                float f = seg > 1e-6f ? (target - acc[j]) / seg : 0f;
                var s = Resolve(Mathf.Lerp(ts[j], ts[j + 1], f));
                s.distance = target + distanceOffset;
                list.Add(s);
            }
            return list;
        }

        // ------------------------------------------------------------------ queries
        /// <summary>Closest cached sample to a world position (horizontal distance). Returns its index.</summary>
        public int ClosestIndex(Vector3 worldPos)
        {
            var s = Samples;
            int best = 0;
            float bd = float.MaxValue;
            for (int i = 0; i < s.Count; i++)
            {
                Vector3 d = s[i].position - worldPos;
                d.y *= 0.25f;                                          // prefer horizontal proximity
                float m = d.sqrMagnitude;
                if (m < bd) { bd = m; best = i; }
            }
            return best;
        }

        /// <summary>Interpolated sample nearest to <paramref name="worldPos"/> plus the signed lateral offset (m, + = right).</summary>
        public RiverSample Closest(Vector3 worldPos, out float lateral)
        {
            var s = Samples;
            if (s.Count < 2) { lateral = 0f; return new RiverSample { position = transform.position, tangent = Vector3.forward, right = Vector3.right }; }
            int i = ClosestIndex(worldPos);
            int i0 = Mathf.Max(0, i - 1), i1 = Mathf.Min(s.Count - 1, i + 1);
            // project onto the neighbouring segment for a smooth result
            RiverSample a = s[i0], b = s[i1];
            Vector3 ab = b.position - a.position; ab.y = 0;
            Vector3 ap = worldPos - a.position; ap.y = 0;
            float f = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector3.Dot(ap, ab) / ab.sqrMagnitude) : 0f;
            var r = Lerp(a, b, f);
            lateral = Vector3.Dot(worldPos - r.position, r.right);
            return r;
        }

        static RiverSample Lerp(RiverSample a, RiverSample b, float f)
        {
            return new RiverSample
            {
                position = Vector3.Lerp(a.position, b.position, f),
                tangent = Vector3.Slerp(a.tangent, b.tangent, f).normalized,
                right = Vector3.Slerp(a.right, b.right, f).normalized,
                width = Mathf.Lerp(a.width, b.width, f),
                bankFlat = Mathf.Lerp(a.bankFlat, b.bankFlat, f),
                wallHeight = Mathf.Lerp(a.wallHeight, b.wallHeight, f),
                wallRun = Mathf.Lerp(a.wallRun, b.wallRun, f),
                flowSpeed = Mathf.Lerp(a.flowSpeed, b.flowSpeed, f),
                hasWater = f < 0.5f ? a.hasWater : b.hasWater,
                distance = Mathf.Lerp(a.distance, b.distance, f),
                t = Mathf.Lerp(a.t, b.t, f),
                section = f < 0.5f ? a.section : b.section,
            };
        }

        /// <summary>Water surface height under a point. False when the point is outside the water or over a dry span.</summary>
        public bool TryGetWater(Vector3 worldPos, out float height, out Vector3 flow)
        {
            if (points.Count < 2) { height = 0f; flow = Vector3.zero; return false; }
            var s = Closest(worldPos, out float lateral);
            height = s.position.y;
            flow = s.tangent * s.flowSpeed;
            return s.hasWater && Mathf.Abs(lateral) <= s.width * 0.5f + 0.75f;
        }

        public RiverSample SampleAtDistance(float distance)
        {
            var s = Samples;
            if (s.Count == 0) return default;
            distance = Mathf.Clamp(distance, s[0].distance, s[s.Count - 1].distance);
            int lo = 0, hi = s.Count - 1;
            while (hi - lo > 1) { int m = (lo + hi) / 2; if (s[m].distance < distance) lo = m; else hi = m; }
            float seg = s[hi].distance - s[lo].distance;
            return Lerp(s[lo], s[hi], seg > 1e-6f ? (distance - s[lo].distance) / seg : 0f);
        }

        /// <summary>Absolute distance of spline parameter t (interpolated from the cached samples).</summary>
        public float DistanceAtT(float t)
        {
            var s = Samples;
            if (s.Count == 0) return distanceOffset;
            int lo = 0, hi = s.Count - 1;
            if (t <= s[0].t) return s[0].distance;
            if (t >= s[hi].t) return s[hi].distance;
            while (hi - lo > 1) { int m = (lo + hi) / 2; if (s[m].t < t) lo = m; else hi = m; }
            float seg = s[hi].t - s[lo].t;
            return Mathf.Lerp(s[lo].distance, s[hi].distance, seg > 1e-6f ? (t - s[lo].t) / seg : 0f);
        }

        /// <summary>
        /// Endless rivers: drops the first <paramref name="count"/> control points. Removing a point only reshapes the
        /// new first segment (it loses its left neighbour), so distances are re-anchored on the control point after it,
        /// which keeps every distance downstream exactly as it was.
        /// </summary>
        public void TrimFront(int count)
        {
            count = Mathf.Min(count, points.Count - 4);
            if (count <= 0) return;
            float anchor = DistanceAtT(count + 1);
            points.RemoveRange(0, count);
            distanceOffset = 0f;
            Invalidate();
            float now = DistanceAtT(1);
            distanceOffset = anchor - now;
            Invalidate();
        }

        // ------------------------------------------------------------------ gizmos
        void OnDrawGizmos()
        {
            if (!drawGizmos || points.Count < 2) return;
            var s = Samples;
            for (int i = 1; i < s.Count; i++)
            {
                var a = s[i - 1]; var b = s[i];
                Gizmos.color = a.hasWater ? new Color(0.1f, 0.9f, 1f) : new Color(1f, 0.4f, 0.1f);
                Gizmos.DrawLine(a.position, b.position);
                Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.6f);
                Gizmos.DrawLine(a.position + a.right * a.width * 0.5f, b.position + b.right * b.width * 0.5f);
                Gizmos.DrawLine(a.position - a.right * a.width * 0.5f, b.position - b.right * b.width * 0.5f);
                Gizmos.color = new Color(1f, 0.9f, 0.1f, 0.8f);                 // raft clearance envelope
                Gizmos.DrawLine(a.position + a.right * raftClearanceHalfWidth, b.position + b.right * raftClearanceHalfWidth);
                Gizmos.DrawLine(a.position - a.right * raftClearanceHalfWidth, b.position - b.right * raftClearanceHalfWidth);
            }
            Gizmos.color = Color.white;
            foreach (var p in points) Gizmos.DrawWireSphere(transform.TransformPoint(p.position), 0.6f);
#if UNITY_EDITOR
            string last = null;
            foreach (var p in points)
                if (p.section != last) { UnityEditor.Handles.Label(transform.TransformPoint(p.position) + Vector3.up * 4f, "Section " + p.section); last = p.section; }
#endif
        }
    }
}
