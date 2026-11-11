using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using FartOnline.Environment;
using FartOnline.EnvironmentEditor;
using FartOnline.Game.Endless;
using FartOnline.Obstacles;
using A = FartOnline.GameEditor.AnimatedObstacleBuilder;

namespace FartOnline.GameEditor
{
    /// <summary>
    /// Spiked-log hazards and bright bank decor for the endless river (original procedural geometry, toy colours):
    ///  * PF_Obs_SpikeSwing    red spiked roller hanging from the gantry on ropes, swinging along the flow and spinning
    ///  * PF_Obs_SpikeMine     floating red sea mine with white spikes (bobs); placed in clusters (MineField)
    ///  * PF_Obs_SpikeLogRoll  spiked roller floating across the stream, spinning while it slides sideways
    ///  * PF_Obs_SpikeBridge   wooden plank bridge over the whole river (scenery, posts on the banks)
    ///  * PF_Obs_SpikeCurtain  spiked log hanging from that bridge down to the water (stretched to the bank)
    ///  * PF_Deco_CliffBlocks_A/B/C  stacked red-orange stone blocks with grass tops (no colliders)
    ///  * PF_Deco_Fence        4 m wooden fence segment for the plateau edge (no colliders)
    ///  * PF_Deco_CascadeSplash foam ring and mist at the foot of a bank waterfall
    /// Built in river space (+Z downstream, +X river right, y = 0 at the water). Called from
    /// <see cref="AnimatedObstacleBuilder.Build"/>.
    /// </summary>
    public static class SpikeHazardBuilder
    {
        const float BridgeH = 4.45f;           // pivot height of the hanging logs (underside of the bridge deck)
        const float HalfWidth = 7.5f;          // matches EndlessRiver.waterWidth / 2 (pendulum gantry uses the same)

        static Material SpikeRed => A.Toy("SpikeRed", A.UiHex(0xE5352B), 0.5f);
        static Material SpikeRedDark => A.Toy("SpikeRedDark", A.UiHex(0xA51F1A), 0.45f);
        static Material SpikeWhite => A.Toy("SpikeWhite", A.UiHex(0xFBF7EF), 0.6f);
        static Material Rope => A.Toy("Rope", A.UiHex(0xD2AE72), 0.15f);
        static Material PlankLight => A.Toy("WoodLight", A.UiHex(0xC98B50), 0.2f);
        static Material WoodDark => A.Toy("WoodDark", A.UiHex(0x7A4A2A), 0.2f);
        static Material Foam => A.Toy("Foam", new Color(0.9f, 0.97f, 1f, 1f), 0.7f);

        public static void Build()
        {
            SpikeSwing();
            SpikeMine();
            SpikeLogRoll();
            SpikeBridge();
            SpikeCurtain();
            EdgeRoller();
            WaitGate();
            CliffBlocks();
            Fence();
            CascadeSplash();
        }

        // ------------------------------------------------------------------ shared geometry
        /// <summary>Spiked roller along X, centred on the origin: body, darker bands, yellow end caps, white spikes.</summary>
        static void Roller(Transform parent, string name, float len, float r, float spike, int rows, int around, int sides = 20, int spikeSides = 7)
        {
            float half = len * 0.5f;
            var body = new MeshKit(); var bands = new MeshKit(); var caps = new MeshKit(); var spikes = new MeshKit();
            A.Cylinder(body, new Vector3(-half, 0f, 0f), new Vector3(half, 0f, 0f), r, r, sides);
            int nb = Mathf.Max(2, Mathf.RoundToInt(len / 1.5f));
            for (int i = 0; i <= nb; i++)
            {
                float x = Mathf.Lerp(-half + 0.25f, half - 0.25f, i / (float)nb);
                A.Cylinder(bands, new Vector3(x - 0.08f, 0f, 0f), new Vector3(x + 0.08f, 0f, 0f), r + 0.05f, r + 0.05f, sides);
            }
            foreach (int sd in new[] { -1, 1 })
                A.Cylinder(caps, new Vector3(sd * half, 0f, 0f), new Vector3(sd * (half + 0.12f), 0f, 0f), r + 0.07f, r * 0.7f, sides);
            for (int row = 0; row < rows; row++)
            {
                float x = Mathf.Lerp(-half + 0.35f, half - 0.35f, rows == 1 ? 0.5f : row / (float)(rows - 1));
                for (int i = 0; i < around; i++)
                {
                    float a = (i + (row % 2) * 0.5f) / around * Mathf.PI * 2f;
                    Vector3 d = new Vector3(0f, Mathf.Cos(a), Mathf.Sin(a));
                    A.Cone(spikes, new Vector3(x, 0f, 0f) + d * (r - 0.03f), new Vector3(x, 0f, 0f) + d * (r + spike), 0.13f, spikeSides);
                }
            }
            A.Part(parent, "Obs_" + name + "Body", body, SpikeRed);
            A.Part(parent, "Obs_" + name + "Bands", bands, SpikeRedDark);
            A.Part(parent, "Obs_" + name + "Caps", caps, A.Yellow);
            A.Part(parent, "Obs_" + name + "Spikes", spikes, SpikeWhite);
        }

        static void Ropes(Transform parent, string name, float x, Vector3 bottom, float radius = 0.055f)
        {
            var k = new MeshKit();
            foreach (int sd in new[] { -1, 1 })
                A.Cylinder(k, new Vector3(sd * x, 0.05f, 0f), bottom + new Vector3(sd * x, 0f, 0f), radius, radius, 6);
            A.Part(parent, name, k, Rope, false);
        }

        static Transform Child(Transform parent, string name, Vector3 pos)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = pos;
            return t;
        }

        // ------------------------------------------------------------------ hazards
        /// <summary>Swinging spiked log: pivot = root (hung under the pendulum gantry), Log child 5 m along X (scaled by
        /// the river to the planned length), roller centre 3.6 m under the pivot, spinning while it swings.</summary>
        static void SpikeSwing()
        {
            var root = A.Root("PF_Obs_SpikeSwing", "a swinging spiked log");
            var pend = A.Kinematic<Pendulum>(root);
            pend.length = 3.6f; pend.amplitude = 48f; pend.period = 2.8f;
            var log = Child(root.transform, "Log", Vector3.zero);
            Vector3 lp = Vector3.down * 3.6f;
            var spin = Child(log, "Spin", lp);
            var vs = spin.gameObject.AddComponent<VisualSpin>();
            vs.axis = Vector3.right; vs.degreesPerSecond = 200f;
            Roller(spin, "SwingRoller", 5f, 0.48f, 0.42f, 9, 8);
            Ropes(log, "Obs_SwingRollerRopes", 1.7f, lp + Vector3.up * 0.4f);
            var cap = log.gameObject.AddComponent<CapsuleCollider>();
            cap.direction = 0; cap.radius = 0.9f; cap.height = 5.4f; cap.center = lp;
            A.Save(root);
        }

        /// <summary>Floating spiked sea mine (red dome half under water, white spikes, yellow belt). Max horizontal reach 0.85 m.</summary>
        static void SpikeMine()
        {
            var root = A.Root("PF_Obs_SpikeMine", "a spiked mine");
            var pulse = A.Kinematic<PulseObstacle>(root);
            pulse.period = 3.2f; pulse.minScale = 1f; pulse.bob = 0.12f; pulse.turnSpeed = 18f;
            var body = new MeshKit(); var belt = new MeshKit(); var spikes = new MeshKit();
            Vector3 c = new Vector3(0f, 0.05f, 0f);
            A.Sphere(body, c, new Vector3(0.56f, 0.52f, 0.56f), 3);
            A.Cylinder(belt, c + Vector3.down * 0.07f, c + Vector3.up * 0.07f, 0.585f, 0.585f, 24);
            int n = 18;
            for (int i = 0; i < n; i++)
            {
                float yv = 1f - (i + 0.5f) / n * 0.78f;          // upper part only (elevation >= ~13 degrees)
                float rad = Mathf.Sqrt(Mathf.Max(0f, 1f - yv * yv));
                float a = i * 2.39996f;
                Vector3 d = new Vector3(Mathf.Cos(a) * rad, yv, Mathf.Sin(a) * rad).normalized;
                Vector3 s = c + Vector3.Scale(d, new Vector3(0.56f, 0.52f, 0.56f));
                A.Cone(spikes, s - d * 0.04f, s + d * 0.3f, 0.11f, 7);
            }
            A.Part(root.transform, "Obs_MineBody", body, SpikeRed);
            A.Part(root.transform, "Obs_MineBelt", belt, A.Yellow);
            A.Part(root.transform, "Obs_MineSpikes", spikes, SpikeWhite);
            var sc = root.AddComponent<SphereCollider>();
            sc.radius = 0.78f; sc.center = c + Vector3.up * 0.1f;
            A.Save(root);
        }

        /// <summary>Spiked roller lying across the stream on the water: slides along local +X (travel set by the river),
        /// spinning on its axle. Body child (4 m along X, scaled to the planned length) carries the collider.</summary>
        static void SpikeLogRoll()
        {
            var root = A.Root("PF_Obs_SpikeLogRoll", "a rolling spiked log");
            var mb = A.Kinematic<MovingBarrier>(root);
            mb.moveTime = 1.6f; mb.pauseTime = 0.45f; mb.travel = Vector3.right * 2f;
            var body = Child(root.transform, "Body", new Vector3(0f, 0.3f, 0f));
            var spin = Child(body, "Spin", Vector3.zero);
            var vs = spin.gameObject.AddComponent<VisualSpin>();
            vs.axis = Vector3.right; vs.degreesPerSecond = -260f;
            Roller(spin, "LogRoll", 4f, 0.45f, 0.42f, 8, 8);
            var cap = body.gameObject.AddComponent<CapsuleCollider>();
            cap.direction = 0; cap.radius = 0.86f; cap.height = 4.3f;
            // splash ring where it churns the water
            var ring = new MeshKit();
            A.Annulus(ring, 0.04f, 0.0f, 1f, 20);
            var ringGo = A.Part(root.transform, "Obs_LogRollFoam", ring, Foam, false);
            ringGo.transform.localScale = new Vector3(2.4f, 1f, 0.9f);
            A.Save(root);
        }

        /// <summary>Plank bridge over the whole river (posts beyond the banks). Pure scenery: the hanging logs are separate.</summary>
        static void SpikeBridge()
        {
            var root = A.Root("PF_Obs_SpikeBridge", null);
            float span = HalfWidth + 1.2f;
            var posts = new MeshKit(); var planks = new MeshKit(); var beams = new MeshKit(); var rail = new MeshKit();
            foreach (int sd in new[] { -1, 1 })
                foreach (float z in new[] { -1.05f, 1.05f })
                    A.Cylinder(posts, new Vector3(sd * span, -2.2f, z), new Vector3(sd * span, BridgeH + 1.5f, z), 0.3f, 0.26f, 12);
            int i = 0;
            for (float x = -span - 0.5f; x <= span + 0.5f; x += 0.48f, i++)
                PropFactory.RoundedBox(planks, new Vector3(x, BridgeH + 0.32f + (i % 2) * 0.02f, (i % 3 - 1) * 0.04f), new Vector3(0.44f, 0.16f, 2.5f), 0.05f, Color.white, Quaternion.Euler(0f, (i % 5 - 2) * 0.8f, 0f), 1);
            foreach (float z in new[] { -1.0f, 1.0f })
            {
                PropFactory.RoundedBox(beams, new Vector3(0f, BridgeH + 0.1f, z), new Vector3(2f * span + 1.4f, 0.3f, 0.3f), 0.08f, Color.white, Quaternion.identity, 1);
                PropFactory.RoundedBox(rail, new Vector3(0f, BridgeH + 1.35f, z * 1.18f), new Vector3(2f * span + 0.8f, 0.14f, 0.14f), 0.05f, Color.white, Quaternion.identity, 1);
                PropFactory.RoundedBox(rail, new Vector3(0f, BridgeH + 0.9f, z * 1.18f), new Vector3(2f * span + 0.8f, 0.1f, 0.1f), 0.04f, Color.white, Quaternion.identity, 1);
                for (float x = -span + 1.8f; x < span - 1f; x += 2.4f)
                    A.Cylinder(rail, new Vector3(x, BridgeH + 0.35f, z * 1.18f), new Vector3(x, BridgeH + 1.45f, z * 1.18f), 0.08f, 0.08f, 8);
            }
            A.Part(root.transform, "Obs_BridgePosts", posts, A.Wood);
            A.Part(root.transform, "Obs_BridgePlanks", planks, PlankLight);
            A.Part(root.transform, "Obs_BridgeBeams", beams, WoodDark);
            A.Part(root.transform, "Obs_BridgeRail", rail, WoodDark);
            // red-and-white warning flags on the posts
            var flags = new MeshKit(); var flagW = new MeshKit();
            foreach (int sd in new[] { -1, 1 })
            {
                Vector3 b = new Vector3(sd * span, BridgeH + 1.55f, -1.05f);
                PropFactory.RoundedBox(flags, b + new Vector3(-sd * 0.4f, 0.25f, 0f), new Vector3(0.8f, 0.5f, 0.04f), 0.02f, Color.white, Quaternion.identity, 1);
                PropFactory.RoundedBox(flagW, b + new Vector3(-sd * 0.4f, 0.25f, -0.01f), new Vector3(0.82f, 0.14f, 0.045f), 0.02f, Color.white, Quaternion.identity, 1);
            }
            A.Part(root.transform, "Obs_BridgeFlags", flags, SpikeRed, false);
            A.Part(root.transform, "Obs_BridgeFlagStripe", flagW, SpikeWhite, false);
            foreach (int sd in new[] { -1, 1 })
            {
                var holder = Child(root.transform, "Col_Bank" + (sd < 0 ? "L" : "R"), new Vector3(sd * span, 1.5f, 0f));
                holder.gameObject.AddComponent<BoxCollider>().size = new Vector3(0.7f, 7.4f, 2.6f);
            }
            A.Save(root);
        }

        /// <summary>Spiked log hanging under the bridge (pivot = root at the deck's underside), 4 m along X before scaling,
        /// centre 1 m above the water; sways a little along the flow and turns slowly.</summary>
        static void SpikeCurtain()
        {
            var root = A.Root("PF_Obs_SpikeCurtain", "a spiked log under the bridge");
            var pend = A.Kinematic<Pendulum>(root);
            pend.length = 3.45f; pend.amplitude = 7f; pend.period = 2.3f;
            var log = Child(root.transform, "Log", Vector3.zero);
            Vector3 lp = Vector3.down * 3.45f;
            var spin = Child(log, "Spin", lp);
            var vs = spin.gameObject.AddComponent<VisualSpin>();
            vs.axis = Vector3.right; vs.degreesPerSecond = 70f;
            Roller(spin, "CurtainRoller", 4f, 0.4f, 0.38f, 7, 8);
            Ropes(log, "Obs_CurtainRopes", 1.4f, lp + Vector3.up * 0.35f, 0.05f);
            var cap = log.gameObject.AddComponent<CapsuleCollider>();
            cap.direction = 0; cap.radius = 0.8f; cap.height = 4f; cap.center = lp;
            A.Save(root);
        }

        /// <summary>Bank roller segment: a 9 m red spiked roller along the flow (+Z), spinning, on two posts. Static collider
        /// (axis 0.35 m inside the bank line, spikes reach 1.25 m into the water); the river lines both banks with it.</summary>
        static void EdgeRoller()
        {
            var root = A.Root("PF_Obs_EdgeRoller", "a spiked bank roller");
            var spin = Child(root.transform, "Spin", Vector3.zero);
            spin.localRotation = Quaternion.Euler(0f, 90f, 0f);
            var vs = spin.gameObject.AddComponent<VisualSpin>();
            vs.axis = Vector3.right; vs.degreesPerSecond = 160f;
            Roller(spin, "EdgeRoller", 8.8f, 0.5f, 0.4f, 12, 6, 12, 5);         // low-poly: dozens line the banks
            var cap = root.AddComponent<CapsuleCollider>();
            cap.direction = 2; cap.radius = 0.86f; cap.height = 9.2f;
            A.Save(root);
        }

        /// <summary>
        /// Wait gate under a spike bridge: two striped posts (static, placed at the gap's edges by the river) and a spiked
        /// log between them on a lift (<see cref="WaitGateObstacle"/>). Closed: log centre 0.75 m above the water; open:
        /// 2.75 m higher (bottom ~2.6 m, clear of the crew). Log 4 m along X before scaling to the gap.
        /// </summary>
        static void WaitGate()
        {
            var root = A.Root("PF_Obs_WaitGate", "a closing spike gate");
            foreach (var name in new[] { "PostL", "PostR" })
            {
                var post = Child(root.transform, name, new Vector3(name == "PostL" ? -2f : 2f, 0f, 0f));
                var k = new MeshKit(); var stripes = new MeshKit();
                A.Cylinder(k, new Vector3(0f, -1.5f, 0f), new Vector3(0f, BridgeH + 0.1f, 0f), 0.2f, 0.2f, 12);
                for (float y = -0.2f; y < BridgeH; y += 0.9f)
                    A.Cylinder(stripes, new Vector3(0f, y, 0f), new Vector3(0f, y + 0.45f, 0f), 0.215f, 0.215f, 12);
                A.Part(post, "Obs_WaitGatePost", k, SpikeWhite);
                A.Part(post, "Obs_WaitGateStripes", stripes, SpikeRed);
                var col = post.gameObject.AddComponent<CapsuleCollider>();
                col.direction = 1; col.radius = 0.22f; col.height = BridgeH + 1.6f; col.center = new Vector3(0f, (BridgeH - 1.5f) * 0.5f, 0f);
            }
            var liftGo = Child(root.transform, "Lift", new Vector3(0f, 0.75f, 0f));
            var gate = A.Kinematic<WaitGateObstacle>(liftGo.gameObject);
            gate.closedTime = 2.2f; gate.moveTime = 0.45f; gate.openTime = 2.6f; gate.lift = 2.75f;
            var log = Child(liftGo, "Log", Vector3.zero);
            var spin = Child(log, "Spin", Vector3.zero);
            var vs = spin.gameObject.AddComponent<VisualSpin>();
            vs.axis = Vector3.right; vs.degreesPerSecond = 120f;
            Roller(spin, "WaitGateRoller", 4f, 0.45f, 0.4f, 7, 8);
            var cap = log.gameObject.AddComponent<CapsuleCollider>();
            cap.direction = 0; cap.radius = 0.85f; cap.height = 4f;
            A.Save(root);
        }

        // ------------------------------------------------------------------ decor (no colliders)
        static Material RockMat(string name) => AssetDatabase.LoadAssetAtPath<Material>(CourseGenerator.MatDir + "/" + name + ".mat");

        /// <summary>Stacked chunky stone blocks with grass on their tops (Env/StylizedSurface rock: vertex B = grass mask).</summary>
        static void CliffBlocks()
        {
            var specs = new[]
            {
                new[] { new Vector4(0f, 0f, 4.2f, 2.6f), new Vector4(0.5f, 2.5f, 3.2f, 2.2f), new Vector4(-0.3f, 4.6f, 2f, 1.5f) },
                new[] { new Vector4(0f, 0f, 3.4f, 3.4f), new Vector4(-0.7f, 3.3f, 2.4f, 1.8f) },
                new[] { new Vector4(0f, 0f, 5f, 1.8f), new Vector4(1.1f, 1.7f, 2.6f, 2.4f), new Vector4(-1.3f, 1.7f, 1.8f, 1.2f), new Vector4(0.9f, 4f, 1.6f, 1.2f) },
            };
            string[] mats = { "M_Rock_Orange", "M_Rock_Red", "M_Rock_Orange" };
            var rng = new System.Random(5);
            for (int v = 0; v < specs.Length; v++)
            {
                var root = A.Root("PF_Deco_CliffBlocks_" + (char)('A' + v), null);
                var k = new MeshKit();
                foreach (var b in specs[v])
                {
                    float depth = b.z * (0.7f + (float)rng.NextDouble() * 0.3f);
                    var col = new Color(0.25f + (float)rng.NextDouble() * 0.6f, 0f, 1f, 0f);
                    PropFactory.RoundedBox(k, new Vector3(b.x, b.y + b.w * 0.5f, ((float)rng.NextDouble() - 0.5f) * 0.6f), new Vector3(b.z, b.w, depth), 0.32f, col,
                                           Quaternion.Euler(0f, ((float)rng.NextDouble() - 0.5f) * 14f, 0f), 2);
                }
                var go = new GameObject("Blocks");
                go.transform.SetParent(root.transform, false);
                var mesh = k.Build("Deco_CliffBlocks_" + (char)('A' + v));
                mesh.RecalculateNormals();
                go.AddComponent<MeshFilter>().sharedMesh = MeshKit.SaveAsset(mesh, EndlessBuilder.MeshDir + "/" + mesh.name + ".asset");
                go.AddComponent<MeshRenderer>().sharedMaterial = RockMat(mats[v]);
                A.Save(root);
            }
        }

        static void Fence()
        {
            var root = A.Root("PF_Deco_Fence", null);
            var posts = new MeshKit(); var rails = new MeshKit();
            foreach (float x in new[] { -2f, 0f, 2f })
                A.Cylinder(posts, new Vector3(x, -0.4f, 0f), new Vector3(x, 1.15f, 0f), 0.1f, 0.09f, 8);
            PropFactory.RoundedBox(rails, new Vector3(0f, 0.95f, 0.06f), new Vector3(4.2f, 0.14f, 0.07f), 0.03f, Color.white, Quaternion.identity, 1);
            PropFactory.RoundedBox(rails, new Vector3(0f, 0.5f, 0.06f), new Vector3(4.2f, 0.12f, 0.07f), 0.03f, Color.white, Quaternion.Euler(0f, 0f, 3f), 1);
            A.Part(root.transform, "Deco_FencePosts", posts, WoodDark);
            A.Part(root.transform, "Deco_FenceRails", rails, PlankLight);
            A.Save(root);
        }

        /// <summary>White foam ring and drifting mist where a bank waterfall hits the river.</summary>
        static void CascadeSplash()
        {
            var root = A.Root("PF_Deco_CascadeSplash", null);
            var ring = new MeshKit();
            A.Annulus(ring, 0.06f, 0f, 1.3f, 24);
            A.Part(root.transform, "Deco_CascadeFoam", ring, Foam, false);
            var psGo = new GameObject("Mist");
            psGo.transform.SetParent(root.transform, false);
            var ps = psGo.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true; main.playOnAwake = true; main.maxParticles = 40;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.6f, 1.3f);
            main.startColor = new Color(1f, 1f, 1f, 0.85f);
            main.gravityModifier = 0.35f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = 22f;
            var sh = ps.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Hemisphere; sh.radius = 0.8f;
            sh.rotation = new Vector3(-90f, 0f, 0f);
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.7f, 1f, 1.6f));
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.85f, 0.95f, 1f), 1f) },
                      new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.6f, 0.4f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = psGo.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = WaterfallBuilder.ParticleMaterial();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            A.Save(root);
        }
    }
}
