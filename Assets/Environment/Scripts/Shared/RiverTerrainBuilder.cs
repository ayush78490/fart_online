using System.Collections.Generic;
using UnityEngine;
using FartOnline.Environment;

namespace FartOnline.EnvironmentEditor
{
    /// <summary>
    /// Sweeps the river cross-section along the RiverPath:
    ///  * Terrain: ONE continuous profile per sample (left plateau, wall, shore, bank, riverbed, ... right plateau),
    ///    so bed, banks and walls can never separate. Cut into chunks along the river for culling.
    ///  * Water: a ribbon per wet span whose edges tuck under the banks.
    /// Vertex colours (terrain): R = strata/variation, G = wetness, B = vegetation mask (shore + plateau).
    /// Vertex colours (water): R = shallow (near banks), G = flow speed / 10, B = turbulence, A = 1.
    /// </summary>
    public static class RiverTerrainBuilder
    {
        public const int WallRows = 12;

        public struct ProfilePoint { public float d, h, disp; public Color c; }

        /// <summary>Cross-section for one side (s = +1 right, -1 left), ordered from the centreline outwards.</summary>
        public static List<ProfilePoint> SideProfile(RiverSample smp, CourseSettings set, int side, int seed)
        {
            float hw = smp.width * 0.5f * set.riverWidthScale;
            float depth = set.riverDepth;
            float wallH = smp.wallHeight * set.canyonHeightScale;
            float foot = hw + Mathf.Max(1.2f, smp.bankFlat);
            var list = new List<ProfilePoint>();
            float disp = 0f;   // weight of the world-space wall displacement (0 at the waterline, 1 on cliffs)
            void P(float d, float h, float strata, float wet, float veg) => list.Add(new ProfilePoint { d = d, h = h, disp = disp, c = new Color(strata, wet, veg, 1f) });

            Vector3 key = smp.position * 0.07f + new Vector3(side * 17.3f, 0, 0);
            float n1 = Noise.Fbm(key, seed);
            P(Mathf.Max(0.5f, hw - 2.2f), -depth, 0.2f, 1f, 0f);                  // riverbed shoulder
            P(hw - 0.4f, -0.9f, 0.25f, 1f, 0f);                                  // underwater bank
            P(hw + 0.4f, -0.15f, 0.3f, 1f, 0f);                                  // water edge (water ribbon tucks under here)
            float beach = Mathf.Clamp01((smp.bankFlat - 1.5f) / 4f);            // 0 on narrow banks, 1 on real beaches
            P(hw + 1.1f, 0.35f, 0.35f, 0.8f, 0f);                                // shoreline lip
            // shore / beach (always emitted: strips need a fixed-size profile; on narrow banks it sits between lip and foot)
            P(Mathf.Lerp(hw + 1.1f, foot, 0.55f), 0.45f + 0.15f * n1, 0.35f, 0.25f, 0.9f * beach);
            P(foot, 0.6f + 0.2f * n1, 0.4f, 0.15f, 0.5f * beach);                 // wall foot
            // canyon wall: bulging strata, two ledges at varying heights, optional overhang (negative wallRun)
            float ledge1 = 0.32f + 0.12f * Noise.Value(key * 0.5f, seed + 3);
            float ledge2 = 0.68f + 0.1f * Noise.Value(key * 0.5f, seed + 4);
            for (int k = 1; k <= WallRows; k++)
            {
                float f = k / (float)WallRows;
                float h = wallH * f;
                float run = smp.wallRun * Mathf.SmoothStep(0f, 1f, f);
                float bulge = 0.9f * Mathf.Sin(f * Mathf.PI * 2.3f + n1 * 3f) * Mathf.Sin(f * Mathf.PI)
                              + 0.6f * Noise.Fbm(key + new Vector3(0, h * 0.15f, 0), seed + 7);
                // ledges: the wall steps out just below each ledge height and recedes above it
                float l1 = Mathf.Exp(-Mathf.Pow((f - ledge1) / 0.06f, 2f)) - 0.6f * Mathf.Exp(-Mathf.Pow((f - ledge1 - 0.08f) / 0.05f, 2f));
                float l2 = Mathf.Exp(-Mathf.Pow((f - ledge2) / 0.05f, 2f)) - 0.6f * Mathf.Exp(-Mathf.Pow((f - ledge2 - 0.07f) / 0.04f, 2f));
                bulge -= 1.4f * l1 + 1.1f * l2;
                float strata = 0.5f + 0.5f * Mathf.Sin(h * 0.9f + n1 * 4f);
                disp = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(h / 3f));
                float vegLedge = Mathf.Max(Mathf.Clamp01(l1), Mathf.Clamp01(l2)) * 0.8f;
                P(foot + run + bulge, h, strata, 0f, k == WallRows ? 0.7f : vegLedge);
            }
            float top = foot + smp.wallRun + 2.5f;
            disp = 1f;
            P(top + 4f, wallH + 0.8f, 0.6f, 0f, 1f);                             // plateau edge
            P(top + 26f, wallH + 2.5f + 2f * n1, 0.6f, 0f, 1f);                  // back plateau
            return list;
        }

        /// <summary>Wet area around a waterfall: capsule from bottom to top with a radius (m).</summary>
        public struct WetZone
        {
            public Vector3 bottom, top;
            public float radius;
            public float Factor(Vector3 p)
            {
                Vector3 ab = top - bottom;
                float t = Mathf.Clamp01(Vector3.Dot(p - bottom, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
                float d = Vector3.Distance(p, bottom + ab * t);
                return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(radius * 0.45f, radius, d));
            }
        }

        public static int SideSeed(CourseSettings set, int side) => side < 0 ? set.seed : set.seed + 101;

        /// <summary>World-space terrain points of one side at one sample, exactly as the terrain mesh uses them.</summary>
        public static Vector3[] SidePoints(RiverSample s, CourseSettings set, int side, out List<ProfilePoint> profile)
        {
            profile = SideProfile(s, set, side, SideSeed(set, side));
            var pts = new Vector3[profile.Count];
            for (int i = 0; i < profile.Count; i++)
                pts[i] = Displace(ToWorld(s, profile[i].d, profile[i].h, side), s, side, profile[i].disp, set.seed);
            return pts;
        }

        /// <summary>Index of the water-edge point in a side profile (waterfalls run from the plateau edge down to here).</summary>
        public const int WaterEdgeIndex = 2;

        public static Vector3 ToWorld(RiverSample s, float d, float h, int side)
        {
            Vector3 p = s.position + s.right * (d * side);
            p.y = s.position.y + h;
            return p;
        }

        /// <summary>World-space cliff displacement, sampled at the point itself so it varies along the river too,
        /// plus vertical crack grooves. Pushes outward (away from the water) so the channel never narrows.</summary>
        public static Vector3 Displace(Vector3 p, RiverSample s, int side, float weight, int seed)
        {
            if (weight <= 0f) return p;
            float n = Noise.Fbm(p * 0.085f, seed + 41);
            float crack = Mathf.Pow(Mathf.Abs(Noise.Value(new Vector3(p.x * 0.35f, p.y * 0.04f, p.z * 0.35f), seed + 43)), 6f);
            float push = (n * 0.5f + 0.5f) * 2.6f + crack * 1.8f;
            Vector3 q = p + s.right * side * push * weight;
            q.y += Noise.Value(p * 0.12f, seed + 47) * 0.6f * weight;
            return q;
        }

        // ------------------------------------------------------------------ terrain
        /// <summary>Visual terrain (collision = false) or a lighter collision version (every 2nd sample, no far
        /// plateau; identical vertices on the shared rows so visuals and collision agree).</summary>
        public static List<Mesh> BuildTerrain(RiverPath path, CourseSettings set, int chunkSamples = 48, bool collision = false,
                                              List<WetZone> wetZones = null)
        {
            var all = path.Samples;
            var samples = new List<RiverSample>();
            for (int i = 0; i < all.Count; i++)
                if (!collision || i % 2 == 0 || i == all.Count - 1) samples.Add(all[i]);
            if (collision) chunkSamples /= 2;
            int seed = set.seed;
            // full row = reversed left profile + centre + right profile
            var rows = new List<Vector3[]>();
            var rowCols = new List<Color[]>();
            foreach (var s in samples)
            {
                var L = SideProfile(s, set, -1, seed);
                var R = SideProfile(s, set, +1, seed + 101);
                if (collision) { L.RemoveAt(L.Count - 1); R.RemoveAt(R.Count - 1); }
                var pts = new List<Vector3>();
                var cols = new List<Color>();
                for (int i = L.Count - 1; i >= 0; i--) { pts.Add(Displace(ToWorld(s, L[i].d, L[i].h, -1), s, -1, L[i].disp, seed)); cols.Add(L[i].c); }
                pts.Add(ToWorld(s, 0f, -set.riverDepth - 0.15f, 1)); cols.Add(new Color(0.2f, 1f, 0f, 1f));
                for (int i = 0; i < R.Count; i++) { pts.Add(Displace(ToWorld(s, R[i].d, R[i].h, 1), s, 1, R[i].disp, seed)); cols.Add(R[i].c); }
                if (wetZones != null && wetZones.Count > 0)
                    for (int j = 0; j < pts.Count; j++)
                    {
                        float w = 0f;
                        foreach (var z in wetZones) w = Mathf.Max(w, z.Factor(pts[j]));
                        if (w <= 0f) continue;
                        var c = cols[j];
                        c.g = Mathf.Max(c.g, w * 0.85f);            // wet: darker + glossier in Env/StylizedSurface
                        c.b *= 1f - w;                               // no grass under the spray
                        cols[j] = c;
                    }
                rows.Add(pts.ToArray());
                rowCols.Add(cols.ToArray());
            }
            int rowLen = rows[0].Length;
            var meshes = new List<Mesh>();
            int chunk = 0;
            for (int start = 0; start < samples.Count - 1; start += chunkSamples)
            {
                int end = Mathf.Min(samples.Count - 1, start + chunkSamples);
                var k = new MeshKit();
                int[] prev = null;
                for (int i = start; i <= end; i++)
                {
                    var row = new int[rowLen];
                    for (int j = 0; j < rowLen; j++)
                        row[j] = k.Add(rows[i][j], new Vector2(j, samples[i].distance * 0.25f), rowCols[i][j]);
                    if (prev != null) k.Strip(prev, row);
                    prev = row;
                }
                k.OrientRange(0, k.tri.Count, Vector3.up);                 // bed + plateaus dominate: face up
                // closing cliffs at both ends of the course, facing into the course
                if (start == 0) AddEndCap(k, rows[0], rowCols[0], -samples[0].tangent, samples[0], set);
                if (end == samples.Count - 1) AddEndCap(k, rows[end], rowCols[end], samples[end].tangent, samples[end], set);
                meshes.Add(k.Build((collision ? "TerrainCol_" : "Terrain_") + chunk.ToString("00")));
                chunk++;
            }
            return meshes;
        }

        static void AddEndCap(MeshKit k, Vector3[] row, Color[] cols, Vector3 outward, RiverSample s, CourseSettings set)
        {
            float capH = s.position.y + s.wallHeight * set.canyonHeightScale + 2f;
            Vector3 o = Vector3.ProjectOnPlane(outward, Vector3.up).normalized;
            var a = new int[row.Length];
            var b = new int[row.Length];
            for (int j = 0; j < row.Length; j++)
            {
                a[j] = k.Add(row[j], Vector2.zero, cols[j]);
                Vector3 up = row[j] + o * 4f;
                up.y = Mathf.Max(capH, row[j].y + 1f);
                b[j] = k.Add(up, Vector2.zero, new Color(0.55f, 0f, 0.3f, 1f));
            }
            int t0 = k.tri.Count;
            k.Strip(a, b);
            k.OrientRange(t0, k.tri.Count, -o);
        }

        // ------------------------------------------------------------------ water
        /// <summary>Turbulence per sample, smooth along the river: curvature over a +-6 m window plus speed, then a
        /// moving average. (Per-row curvature from adjacent samples is noisy and showed up as stripes across the water.)</summary>
        public static float[] Turbulence(List<RiverSample> samples)
        {
            int n = samples.Count;
            var raw = new float[n];
            for (int i = 0; i < n; i++)
            {
                var a = samples[Mathf.Max(0, i - 6)].tangent;
                var b = samples[Mathf.Min(n - 1, i + 6)].tangent;
                float curv = Vector3.Angle(Vector3.ProjectOnPlane(a, Vector3.up), Vector3.ProjectOnPlane(b, Vector3.up)) / 45f;
                var s = samples[i];
                raw[i] = Mathf.Clamp01(s.flowSpeed / 8f * 0.7f + curv * 0.5f + (s.section == "C" ? 0.25f : 0f));
            }
            var smooth = new float[n];
            const int w = 8;
            for (int i = 0; i < n; i++)
            {
                float sum = 0f; int c = 0;
                for (int k = Mathf.Max(0, i - w); k <= Mathf.Min(n - 1, i + w); k++) { sum += raw[k]; c++; }
                smooth[i] = sum / c;
            }
            return smooth;
        }

        public static List<Mesh> BuildWater(RiverPath path, CourseSettings set, int across = 14)
        {
            var samples = path.Samples;
            var turbulence = Turbulence(samples);
            var meshes = new List<Mesh>();
            int i = 0, span = 0;
            while (i < samples.Count)
            {
                while (i < samples.Count && !samples[i].hasWater) i++;
                if (i >= samples.Count) break;
                var k = new MeshKit();
                int[] prev = null;
                for (; i < samples.Count && samples[i].hasWater; i++)
                {
                    var s = samples[i];
                    float hw = s.width * 0.5f * set.riverWidthScale + 0.8f;    // tuck under the banks
                    float turb = turbulence[i];
                    var row = new int[across + 1];
                    for (int j = 0; j <= across; j++)
                    {
                        float u = j / (float)across;
                        float lat = Mathf.Lerp(-hw, hw, u);
                        float edge = 1f - Mathf.Clamp01((hw - Mathf.Abs(lat)) / Mathf.Max(1.5f, hw * 0.45f));
                        Vector3 p = s.position + s.right * lat;
                        row[j] = k.Add(p, new Vector2(lat * 0.25f, s.distance * 0.25f), new Vector2(s.flowSpeed * set.flowSpeedScale, s.width),
                                       new Color(edge, Mathf.Clamp01(s.flowSpeed / 10f), turb, 1f));
                    }
                    if (prev != null) k.Strip(prev, row);
                    prev = row;
                }
                if (k.v.Count > 0)
                {
                    k.OrientRange(0, k.tri.Count, Vector3.up);
                    meshes.Add(k.Build("Water_" + span.ToString("00")));
                }
                span++;
            }
            return meshes;
        }
    }
}
