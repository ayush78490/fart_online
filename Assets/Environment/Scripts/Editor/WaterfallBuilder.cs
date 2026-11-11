using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using FartOnline.Environment;
using FartOnline.Water;

namespace FartOnline.EnvironmentEditor
{
    /// <summary>
    /// Builds the course waterfalls:
    ///  * Wall cascades: re-sample the exact cliff surface the terrain uses (SidePoints) and offset it 0.25 m towards
    ///    the water, so the sheet hugs the rock and can never intersect it. Runs from the plateau edge to the river.
    ///  * Free falls (chute lip, landmark): ballistic arc sheets.
    /// Each waterfall gets an impact foam spot (drawn by the river shader), capped mist + splash particles and a wet zone on the terrain.
    /// </summary>
    public static class WaterfallBuilder
    {
        public enum Kind { Wall, Lip, Landmark }

        public struct Spec
        {
            public string name;
            public Kind kind;
            public float distance;   // along the river (m)
            public int side;         // wall falls: -1 left / +1 right
            public float width;      // m
        }

        /// <summary>Result of geometry generation (world space).</summary>
        public class Built
        {
            public Spec spec;
            public Mesh sheet;
            public Vector3 impact, top;
            public Vector3 facing;     // horizontal direction the sheet faces (towards the viewer / river)
            public float height;
        }

        // ------------------------------------------------------------------ default placements
        static float SectionStart(RiverPath path, string sec)
        {
            foreach (var s in path.Samples) if (s.section == sec) return s.distance;
            return 0f;
        }

        public static List<Spec> DefaultSpecs(RiverPath path)
        {
            var list = new List<Spec>();
            void Wall(string n, string sec, float off, int side, float w) =>
                list.Add(new Spec { name = n, kind = Kind.Wall, distance = SectionStart(path, sec) + off, side = side, width = w });
            // C - waterfall basin: one big cascade + two secondary
            Wall("WF_Basin_Main", "C", 12f, +1, 7f);
            Wall("WF_Basin_Left", "C", 30f, -1, 5f);
            Wall("WF_Basin_Small", "C", 44f, +1, 3f);
            // B - thin trickles down the canyon walls
            Wall("WF_Canyon_Trickle_A", "B", 14f, -1, 1.6f);
            Wall("WF_Canyon_Trickle_B", "B", 40f, +1, 1.4f);
            // F - arena side falls
            Wall("WF_Arena_Left", "F", 12f, -1, 4f);
            Wall("WF_Arena_Right", "F", 30f, +1, 3.5f);
            // E - the chute pours over its lip into the landing basin
            float lip = 0f;
            var ss = path.Samples;
            for (int i = 1; i < ss.Count; i++) if (ss[i - 1].hasWater && !ss[i].hasWater) { lip = ss[i - 1].distance; break; }
            if (lip > 0f) list.Add(new Spec { name = "WF_Chute_Lip", kind = Kind.Lip, distance = lip, width = 0f });
            // F - landmark falls over the closing cliff at the end of the course
            list.Add(new Spec { name = "WF_Landmark", kind = Kind.Landmark, distance = path.Length, width = 12f });
            return list;
        }

        // ------------------------------------------------------------------ geometry
        public static Built Build(RiverPath path, CourseSettings set, Spec spec)
        {
            switch (spec.kind)
            {
                case Kind.Wall: return BuildWall(path, set, spec);
                case Kind.Lip:
                {
                    var s = path.SampleAtDistance(spec.distance);
                    var after = path.SampleAtDistance(spec.distance + 6f);
                    Vector3 fwd = Vector3.ProjectOnPlane(s.tangent, Vector3.up).normalized;
                    float w = s.width * set.riverWidthScale + 1.2f;
                    float drop = s.position.y - after.position.y + 0.3f;
                    spec.width = w;
                    return BuildFall(spec, s.position + Vector3.up * 0.05f, s.right, fwd, w, drop, s.flowSpeed * 0.9f);
                }
                default:
                {
                    var s = path.SampleAtDistance(spec.distance);
                    Vector3 back = -Vector3.ProjectOnPlane(s.tangent, Vector3.up).normalized;     // falls towards the course
                    float wallTop = s.position.y + s.wallHeight * set.canyonHeightScale + 1f;
                    Vector3 lipPos = s.position - back * 2.5f;
                    lipPos.y = wallTop;
                    return BuildFall(spec, lipPos, Vector3.Cross(Vector3.up, back), back, spec.width, wallTop - s.position.y + 0.3f, 2.2f);
                }
            }
        }

        static Built BuildWall(RiverPath path, CourseSettings set, Spec spec)
        {
            var samples = path.Samples;
            var rows = new List<Vector3[]>();
            float d0 = spec.distance - spec.width * 0.5f, d1 = spec.distance + spec.width * 0.5f;
            foreach (var s in samples)
            {
                if (s.distance < d0 - 0.5f || s.distance > d1 + 0.5f) continue;
                var pts = RiverTerrainBuilder.SidePoints(s, set, spec.side, out var prof);
                // plateau edge (index Count-2) down to the water edge
                int top = prof.Count - 2;
                var col = new Vector3[top - RiverTerrainBuilder.WaterEdgeIndex + 1];
                for (int k = top, j = 0; k >= RiverTerrainBuilder.WaterEdgeIndex; k--, j++) col[j] = pts[k];
                rows.Add(col);
            }
            if (rows.Count < 2) return null;
            int nr = rows.Count, nc = rows[0].Length;
            var k2 = new MeshKit();
            var idx = new int[nr, nc];
            float[] arc = new float[nc];
            for (int j = 1; j < nc; j++) arc[j] = arc[j - 1] + Vector3.Distance(rows[nr / 2][j - 1], rows[nr / 2][j]);
            Vector3 river = path.SampleAtDistance(spec.distance).position;
            for (int i = 0; i < nr; i++)
                for (int j = 0; j < nc; j++)
                {
                    // surface normal from the grid, pointed towards the river, offset so the sheet floats on the rock
                    Vector3 du = rows[Mathf.Min(i + 1, nr - 1)][j] - rows[Mathf.Max(i - 1, 0)][j];
                    Vector3 dv = rows[i][Mathf.Min(j + 1, nc - 1)] - rows[i][Mathf.Max(j - 1, 0)];
                    Vector3 n = Vector3.Cross(du, dv).normalized;
                    Vector3 toRiver = river - rows[i][j]; toRiver.y = 0f;
                    if (Vector3.Dot(n, toRiver) < 0f) n = -n;
                    Vector3 p = rows[i][j] + n * 0.25f;
                    float u = i / (float)(nr - 1);
                    float edge = Mathf.Pow(Mathf.Sin(u * Mathf.PI), 0.45f);
                    float topFade = Mathf.Clamp01(j / 1.5f);
                    float bottom = Mathf.InverseLerp(nc - 4, nc - 1, j);
                    idx[i, j] = k2.Add(p, new Vector2(u * spec.width / 4f, arc[j] * 0.3f), new Color(bottom, 0, 0, edge * topFade));
                }
            for (int i = 0; i < nr - 1; i++)
                for (int j = 0; j < nc - 1; j++) k2.Quad(idx[i, j], idx[i + 1, j], idx[i + 1, j + 1], idx[i, j + 1]);
            Vector3 impact = rows[nr / 2][nc - 1];
            impact.y = path.SampleAtDistance(spec.distance).position.y;
            Vector3 face = river - impact; face.y = 0f;
            k2.OrientRange(0, k2.tri.Count, face.normalized + Vector3.up * 0.1f);
            var b = new Built { spec = spec, sheet = k2.Build(spec.name), impact = impact, top = rows[nr / 2][0], facing = face.normalized };
            b.height = b.top.y - impact.y;
            return b;
        }

        static Built BuildFall(Spec spec, Vector3 lip, Vector3 right, Vector3 fwd, float width, float drop, float speed)
        {
            const int cols = 12, rowsN = 18;
            float g = 9.81f;
            float tTot = Mathf.Sqrt(2f * Mathf.Max(0.5f, drop) / g);
            var k = new MeshKit();
            var idx = new int[cols + 1, rowsN + 1];
            float arcLen = 0f;
            Vector3 prevC = lip;
            for (int r = 0; r <= rowsN; r++)
            {
                float t = tTot * r / rowsN;
                Vector3 c = lip + fwd * speed * t + Vector3.down * 0.5f * g * t * t;
                arcLen += Vector3.Distance(prevC, c);
                prevC = c;
                float spread = 1f + 0.12f * (r / (float)rowsN);              // sheet widens slightly as it falls
                for (int i = 0; i <= cols; i++)
                {
                    float u = i / (float)cols;
                    Vector3 p = c + right * (u - 0.5f) * width * spread;
                    float edge = Mathf.Pow(Mathf.Sin(u * Mathf.PI), 0.4f);
                    float bottom = Mathf.InverseLerp(rowsN - 3, rowsN, r);
                    idx[i, r] = k.Add(p, new Vector2(u * width / 4f, arcLen * 0.3f), new Color(bottom, 0, 0, edge * Mathf.Clamp01(r / 1.2f + 0.35f)));
                }
            }
            for (int i = 0; i < cols; i++)
                for (int r = 0; r < rowsN; r++) k.Quad(idx[i, r], idx[i + 1, r], idx[i + 1, r + 1], idx[i, r + 1]);
            Vector3 impact = prevC;
            k.OrientRange(0, k.tri.Count, -fwd);
            var b = new Built { spec = spec, sheet = k.Build(spec.name), impact = impact, top = lip, facing = -fwd, height = drop };
            b.spec.width = width;
            return b;
        }

        // ------------------------------------------------------------------ particles
        public static Texture2D SoftDot()
        {
            string path = CourseGenerator.Root + "/Textures/T_SoftDot.png";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex != null) return tex;
            MeshKit.EnsureFolder(CourseGenerator.Root + "/Textures");
            const int n = 64;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n * 0.5f, n * 0.5f)) / (n * 0.5f);
                    float a = Mathf.Clamp01(1f - d);
                    t.SetPixel(x, y, new Color(1, 1, 1, a * a * (3 - 2 * a)));
                }
            File.WriteAllBytes(Path.GetFullPath(path), t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path);
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled = true;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        public static Material ParticleMaterial()
        {
            string path = CourseGenerator.MatDir + "/M_Foam.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetTexture("_BaseMap", SoftDot());
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent + 10;
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        static ParticleSystem Particles(Transform parent, string name, Material mat, float rate, Vector2 size, Vector2 life,
                                        Vector2 speed, float gravity, float alpha, Vector3 boxSize, float coneAngle, int max)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
            main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
            main.startColor = new Color(1f, 1f, 1f, alpha);
            main.gravityModifier = gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = max;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var em = ps.emission;
            em.rateOverTime = rate;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Cone;
            sh.angle = coneAngle;
            sh.radius = Mathf.Max(0.1f, boxSize.x * 0.5f);
            sh.scale = new Vector3(1f, Mathf.Max(0.05f, boxSize.z / Mathf.Max(0.1f, boxSize.x)), 1f);
            sh.rotation = new Vector3(-90f, 0f, 0f);                         // emit upwards
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.4f));
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.maxParticleSize = 0.8f;
            ps.Play();
            return ps;
        }

        // ------------------------------------------------------------------ scene objects
        public static GameObject CreateObject(Transform parent, Built b, Material sheetMat, Material particleMat)
        {
            var go = new GameObject(b.spec.name);
            go.transform.SetParent(parent, false);
            var wf = go.AddComponent<Waterfall>();
            wf.height = b.height;
            wf.width = b.spec.width;
            var sheetMesh = MeshKit.SaveAsset(b.sheet, CourseGenerator.MeshDir + "/" + b.spec.name + ".asset");
            var sheetGo = new GameObject("Sheet");
            sheetGo.transform.SetParent(go.transform, false);
            sheetGo.AddComponent<MeshFilter>().sharedMesh = sheetMesh;
            var sr = sheetGo.AddComponent<MeshRenderer>();
            sr.sharedMaterial = sheetMat;
            sr.shadowCastingMode = ShadowCastingMode.Off;
            wf.sheet = sr;
            wf.flowSpeed = Mathf.Lerp(1.2f, 2.4f, Mathf.InverseLerp(3f, 40f, b.height));
            // impact foam anchor: the river shader draws the foam (WaterFoamSpots), no extra geometry
            var foam = new GameObject("FoamSpot");
            foam.transform.SetParent(go.transform, false);
            foam.transform.position = b.impact;
            wf.foam = foam.transform;
            wf.foamRadius = Mathf.Clamp(b.spec.width * 0.6f + 1.5f, 2f, 9f);
            // mist + splash (bounded particle counts)
            var fx = new GameObject("FX").transform;
            fx.SetParent(go.transform, false);
            fx.position = b.impact;
            fx.rotation = Quaternion.LookRotation(b.facing, Vector3.up);
            float w = Mathf.Max(1f, b.spec.width);
            float big = Mathf.InverseLerp(3f, 40f, b.height);
            wf.mist = Particles(fx, "Mist", particleMat, Mathf.Clamp(w * 2f, 3f, 24f), new Vector2(2f, 4.5f) * (1f + big),
                                new Vector2(2.5f, 4f), new Vector2(0.4f, 1.4f), -0.02f, 0.22f, new Vector3(w, 0f, 1.5f), 15f, 70);
            wf.splash = Particles(fx, "Splash", particleMat, Mathf.Clamp(w * 5f, 6f, 45f), new Vector2(0.25f, 0.7f),
                                  new Vector2(0.6f, 1.0f), new Vector2(2.5f, 5f) * (0.8f + big), 1.2f, 0.75f, new Vector3(w, 0f, 1f), 28f, 140);
            wf.Apply();
            return go;
        }
    }
}
