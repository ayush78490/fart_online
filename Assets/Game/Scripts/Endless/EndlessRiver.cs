using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using FartOnline.Environment;
using FartOnline.EnvironmentEditor;
using FartOnline.Obstacles;

namespace FartOnline.Game.Endless
{
    /// <summary>
    /// The endless river. Grows the scene's <see cref="RiverPath"/> ahead of the raft (a seeded, gently meandering
    /// spline) and trims it behind, keeping distances absolute, so buoyancy, current, the chase camera and the AI keep
    /// working unchanged. Every <see cref="chunkLength"/> metres it builds a chunk at runtime: canyon terrain (the same
    /// cross-section as the hand-tuned course, with a collider), the water ribbon, bank and plateau dressing, and the
    /// obstacle rows planned by <see cref="ObstaclePlanner"/>. Chunks far behind are destroyed.
    /// Everything follows from the seed, so host and clients build the same river.
    /// </summary>
    [DefaultExecutionOrder(-80)]
    public class EndlessRiver : MonoBehaviour
    {
        public static EndlessRiver Instance { get; private set; }

        [Header("Scene")]
        public RiverPath path;
        public CourseSettings settings;
        [Tooltip("What the river is built around (the shared raft)")] public Transform target;
        [Tooltip("Moved to the start of every new river (the run's start point)")] public Transform startPoint;

        [Header("Look")]
        public Material terrainMaterial;
        public Material waterMaterial;
        public GameObject[] shoreProps, plateauProps, rockProps;

        [Header("Obstacle prefabs")]
        public GameObject[] boulders;
        public GameObject barrels, crates, log, buoy, buoyRope, gatePost, gateBanner, pendulumFrame, pendulumSwing, sweeper, barrier, spinner;

        [Header("Hyper-Casual Obstacle Pack (used when assigned; fitted to each planned footprint)")]
        public GameObject[] packCrystals;
        public GameObject packMine, packBarricade, packJersey, packRopeFence, packPole, packSpikeWall, packBlade, packBuzzsaw, packPress, packSpikeTrap;
        [Tooltip("Scenery traps on the banks (no colliders): flamethrowers, crossbow")] public GameObject[] packBankTraps;

        [Header("Animated hazards (procedural, original)")]
        public GameObject spikeRoller;
        public GameObject starSpinner, puffer, tentacle, windmill;
        [Tooltip("Timed killers: bank flamethrower, whirlpool with a grinder, bank cannon")] public GameObject fireJet, whirlpool, mortar;
        [Tooltip("Spiked logs: swinging roller, floating mine, rolling log, bridge deck and the logs hanging from it")]
        public GameObject spikeSwing, spikeMine, spikeLogRoll, spikeBridge, spikeCurtain;
        [Tooltip("Spiked roller segment lining the banks (6 m along the flow): touching the river's edge ends the run")]
        public GameObject edgeRoller;
        [Tooltip("Spiked log on a lift in the gap of some bridge rows: wait for it to open")] public GameObject waitGate;
        [Tooltip("Edge rollers reach this far into the water from each bank (m); the safe gap keeps clear of it")]
        public float edgeGuard = 1.75f;

        [Header("Bright bank decor (no colliders)")]
        public GameObject[] cliffBlocks;
        public GameObject fence, cascadeSplash;
        [Tooltip("Env/Waterfall sheet for the little bank waterfalls")] public Material cascadeMaterial;
        [Range(0f, 1f)] public float cascadeChance = 0.3f;

        [Header("River shape")]
        [Tooltip("Largest heading change between two control points (deg). 28 deg per 22 m is a ~45 m turn radius: sharp, but the canyon walls never fold")] public float maxTurnPerPoint = 28f;
        [Tooltip("Bends swing up to this far from straight downstream (deg)")] public float maxBend = 85f;
        [Tooltip("Metres between waterfall drops (min, max)")] public Vector2 dropEvery = new Vector2(420f, 700f);
        [Tooltip("Height of a waterfall drop (min, max)")] public Vector2 dropHeight = new Vector2(2.2f, 3.6f);
        [Tooltip("No obstacle rows this close before / after a drop (m)")] public float dropQuiet = 34f;
        [Tooltip("Pack obstacles stand on a base this far under the water (m)")] public float packSink = 0.5f;

        [Header("River")]
        public float waterWidth = 15f;
        public float segmentLength = 22f;
        public float chunkLength = 24f;
        public float buildAhead = 230f;
        public float keepBehind = 60f;
        [Tooltip("Where the raft starts (m along the river)")] public float startDistance = 45f;
        [Tooltip("First obstacle row, measured from the river origin (m)")] public float firstObstacle = 75f;
        public float startFlow = 3.4f, maxFlow = 5.2f;
        public float raftRadius = 2.4f;

        public int Seed { get; private set; }
        public ObstaclePlanner Planner { get; private set; }
        public float BuiltTo => builtTo;
        public int ChunkCount => chunks.Count;
        /// <summary>Raft distance along the river (absolute), updated every frame.</summary>
        public float TargetDistance { get; private set; }

        class Chunk { public GameObject go; public Transform decor; public float d0, d1; public readonly List<Object> owned = new List<Object>(); }

        [Header("Performance")]
        [Tooltip("Static-batch each chunk's terrain and decor into a few combined meshes (far fewer draw calls)")] public bool batchStatic = true;
        [Tooltip("Spin the bank rollers. Off (mobile): they hold still and join the chunk's static batch, saving ~50 draws")] public bool spinEdgeRollers = true;

        readonly List<Chunk> chunks = new List<Chunk>();
        System.Random shapeRng;
        float heading, builtTo, bendTarget, nextDropAt;
        int bendLeft, holdHeading;
        readonly List<Vector2> quietZones = new List<Vector2>();
        int pointIndex, chunkIndex;
        bool ready;
        static readonly Dictionary<GameObject, float> rockRadius = new Dictionary<GameObject, float>();

        public float HalfWidth => waterWidth * 0.5f * (settings != null ? settings.riverWidthScale : 1f);

        void Awake()
        {
            Instance = this;
            if (path == null) path = FindAnyObjectByType<RiverPath>();
            // a river saved into the scene by accident would sit on top of the new one (and its rollers would kill
            // the raft in mid-river): always start clean
            for (int i = transform.childCount - 1; i >= 0; i--)
                if (transform.GetChild(i).name.StartsWith("RiverChunk_")) { transform.GetChild(i).gameObject.SetActive(false); Destroy(transform.GetChild(i).gameObject); }
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            KinematicObstacle.ClockSource = null;
        }

        /// <summary>Downstream current at a distance: speeds up with difficulty.</summary>
        public float FlowAt(float distance) => Mathf.Lerp(startFlow, maxFlow, ObstaclePlanner.Difficulty(distance - startDistance));

        /// <summary>Guaranteed safe lateral position (m, + = right) at a distance along the river.</summary>
        public float SafeLaneX(float distance) => Planner != null ? Planner.SafeX(distance) : 0f;

        /// <summary>Metres travelled from the start for a world position.</summary>
        public float Score(Vector3 worldPos) => ready ? Mathf.Max(0f, path.Closest(worldPos, out _).distance - startDistance) : 0f;

        // ------------------------------------------------------------------ lifecycle
        /// <summary>Builds a brand-new river for <paramref name="seed"/> and moves the start point onto it.</summary>
        public void ResetRiver(int seed)
        {
            Seed = seed;
            foreach (var c in chunks) DestroyChunk(c);
            chunks.Clear();
            if (pending != null) { DestroyChunk(pending); pending = null; }
            path.points.Clear();
            path.distanceOffset = 0f;
            path.Invalidate();
            shapeRng = new System.Random(seed);
            heading = bendTarget = 0f;
            bendLeft = holdHeading = 0;
            pointIndex = chunkIndex = 0;
            quietZones.Clear();
            nextDropAt = startDistance + (dropEvery.x + dropEvery.y) * 0.5f;
            builtTo = 0f;
            Planner = new ObstaclePlanner(seed, firstObstacle, HalfWidth, raftRadius, FlowAt, edgeRoller != null ? edgeGuard : 0f) { IsQuiet = IsQuiet };
            edgeSegments.Clear();
            ready = true;
            Ensure(startDistance, 100);
            if (startPoint != null)
            {
                var s = path.SampleAtDistance(startDistance);
                startPoint.SetPositionAndRotation(s.position, Quaternion.LookRotation(Flat(s.tangent), Vector3.up));
            }
            TargetDistance = startDistance;
        }

        /// <summary>Tests: builds (and trims) the river as if the raft were at <paramref name="distance"/>.</summary>
        public void BuildUpTo(float distance)
        {
            TargetDistance = distance;
            Ensure(distance, 1000);
        }

        void Update()
        {
            if (!ready || target == null) return;
            TargetDistance = path.Closest(target.position, out _).distance;
            Ensure(TargetDistance, 1);            // one chunk per frame: no build spikes while playing
        }

        void Ensure(float around, int maxChunks = 3)
        {
            while (path.points.Count < 4 || path.Length < around + buildAhead + 2.5f * segmentLength) AppendPoint();
            float limit = Mathf.Min(around + buildAhead, path.Length - 2f * segmentLength);
            if (maxChunks == 1)
            {
                // while playing, a chunk is built over two frames (geometry and decor, then obstacles and batching),
                // and one step per frame, so the frame time never spikes
                if (pending != null) { FinishChunk(pending); pending = null; }
                else if (builtTo + chunkLength <= limit) { pending = StartChunk(builtTo, builtTo + chunkLength); builtTo += chunkLength; }
            }
            else
            {
                if (pending != null) { FinishChunk(pending); pending = null; }
                int built = 0;
                while (builtTo + chunkLength <= limit && built++ < maxChunks)
                {
                    FinishChunk(StartChunk(builtTo, builtTo + chunkLength));
                    builtTo += chunkLength;
                }
            }
            // forget what is far behind
            float behind = around - keepBehind;
            for (int i = chunks.Count - 1; i >= 0; i--)
                if (chunks[i].d1 < behind) { DestroyChunk(chunks[i]); chunks.RemoveAt(i); }
            int trim = 0;
            while (trim < path.points.Count - 6 && path.DistanceAtT(trim + 2) < behind - segmentLength) trim++;
            if (trim >= 2) path.TrimFront(trim);
            Planner.Forget(behind);
        }

        /// <summary>True inside a waterfall's quiet zone (no obstacle rows).</summary>
        public bool IsQuiet(float distance)
        {
            foreach (var z in quietZones) if (distance >= z.x && distance <= z.y) return true;
            return false;
        }

        /// <summary>Waterfall drops planned so far (distance of the lip, distance of the foot).</summary>
        public IReadOnlyList<Vector2> QuietZones => quietZones;

        /// <summary>
        /// Adds the next control point. The heading follows planned bends: sharp sweeps of 50-85 degrees that swing back
        /// the other way (S-curves and 90-degree sweeps, never upstream) or short straights, turning at most <see cref="maxTurnPerPoint"/> per point so the
        /// canyon never folds over itself. Every <see cref="dropEvery"/> metres a waterfall drop is inserted on a straight:
        /// a lip, then a steep chute a few metres lower, with a quiet zone (no obstacles) around it.
        /// </summary>
        void AppendPoint()
        {
            float R(float a, float b) => a + (float)shapeRng.NextDouble() * (b - a);
            var pts = path.points;
            if (pts.Count == 0)
            {
                pts.Add(Point(Vector3.zero, 0f));
                pointIndex++;
                return;
            }
            Vector3 last = pts[pts.Count - 1].position;
            float approx = path.Length > 0f ? path.Length : pointIndex * segmentLength;
            if (pointIndex > 3 && holdHeading <= 0)     // the first bend starts ~25 m after the start line
            {
                if (--bendLeft <= 0)
                {
                    bool sweep = shapeRng.NextDouble() < 0.9;
                    // big sweeping turns: keep turning the same way until past 50 degrees, then swing back
                    float sign = Mathf.Abs(heading) > 50f ? -Mathf.Sign(heading)
                               : Mathf.Abs(heading) > 15f && shapeRng.NextDouble() < 0.55 ? Mathf.Sign(heading)
                               : (shapeRng.NextDouble() < 0.5 ? -1f : 1f);
                    bendTarget = sweep ? sign * R(50f, maxBend) : R(-10f, 10f);
                    bendLeft = shapeRng.Next(2, 5);       // short holds: one sharp turn after another
                }
                if (approx + segmentLength * 3f > nextDropAt) bendTarget = heading;     // no turning into a waterfall
                heading = Mathf.MoveTowards(heading, bendTarget, maxTurnPerPoint * R(0.85f, 1f));
            }
            if (holdHeading > 0) holdHeading--;
            Vector3 dir = Quaternion.Euler(0f, heading, 0f) * Vector3.forward;

            // waterfall drop: straighten, then lip -> steep chute -> run-out
            if (approx > nextDropAt && pointIndex > 6)
            {
                float h = R(dropHeight.x, dropHeight.y);
                Vector3 lip = last + dir * segmentLength;
                Vector3 foot = lip + dir * 7.5f + Vector3.down * h;
                pts.Add(Point(lip, 1.6f));
                pts.Add(Point(foot, 3f));
                pointIndex += 2;
                path.Invalidate();
                float dLip = path.DistanceAtT(pts.Count - 2), dFoot = path.DistanceAtT(pts.Count - 1);
                quietZones.Add(new Vector2(dLip - segmentLength - dropQuiet, dFoot + dropQuiet));
                if (quietZones.Count > 8) quietZones.RemoveAt(0);
                nextDropAt = dFoot + R(dropEvery.x, dropEvery.y);
                holdHeading = 2;                  // run straight out of the plunge pool
                bendTarget = heading;
                return;
            }
            pts.Add(Point(last + dir * segmentLength, 0f));
            pointIndex++;
        }

        RiverPoint Point(Vector3 pos, float flowBoost)
        {
            float R(float a, float b) => a + (float)shapeRng.NextDouble() * (b - a);
            float approx = pointIndex * segmentLength;
            return new RiverPoint(pos, waterWidth, R(1.6f, 5f), R(11f, 19f), R(0.5f, 3.5f), FlowAt(approx) + flowBoost, "E");
        }

        void DestroyChunk(Chunk c)
        {
            if (c.go != null) { c.go.SetActive(false); Destroy(c.go); }   // inactive now: no stale colliders this frame
            foreach (var o in c.owned) if (o != null) Destroy(o);
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
        }

        // ------------------------------------------------------------------ chunk geometry
        /// <summary>Longest single chunk build so far (ms), for the perf probe.</summary>
        public static float MaxBuildMs;
        Chunk pending;

        Chunk StartChunk(float d0, float d1)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var c = new Chunk { d0 = d0, d1 = d1 };
            c.go = new GameObject("RiverChunk_" + chunkIndex.ToString("000"));
            c.go.transform.SetParent(transform, false);
            c.decor = new GameObject("Static").transform;           // never moves: static-batched once the chunk is built
            c.decor.SetParent(c.go.transform, false);
            int seed = settings.seed;
            var terrain = new MeshKit();
            var water = new MeshKit();
            int[] prevT = null, prevW = null;
            int rows = Mathf.Max(2, Mathf.RoundToInt(d1 - d0));
            const int across = 14;
            for (int i = 0; i <= rows; i++)
            {
                float d = Mathf.Lerp(d0, d1, i / (float)rows);
                var s = path.SampleAtDistance(d);
                // terrain row: reversed left profile + centre + right profile (exactly the course builder's profile)
                var L = RiverTerrainBuilder.SideProfile(s, settings, -1, seed);
                var Rp = RiverTerrainBuilder.SideProfile(s, settings, +1, seed + 101);
                var row = new int[L.Count + Rp.Count + 1];
                int n = 0;
                for (int j = L.Count - 1; j >= 0; j--)
                    row[n++] = terrain.Add(RiverTerrainBuilder.Displace(RiverTerrainBuilder.ToWorld(s, L[j].d, L[j].h, -1), s, -1, L[j].disp, seed), new Vector2(n, d * 0.25f), L[j].c);
                row[n++] = terrain.Add(RiverTerrainBuilder.ToWorld(s, 0f, -settings.riverDepth - 0.15f, 1), new Vector2(n, d * 0.25f), new Color(0.2f, 1f, 0f, 1f));
                for (int j = 0; j < Rp.Count; j++)
                    row[n++] = terrain.Add(RiverTerrainBuilder.Displace(RiverTerrainBuilder.ToWorld(s, Rp[j].d, Rp[j].h, 1), s, 1, Rp[j].disp, seed), new Vector2(n, d * 0.25f), Rp[j].c);
                if (prevT != null) terrain.Strip(prevT, row);
                prevT = row;

                // water row
                float hw = s.width * 0.5f * settings.riverWidthScale + 0.8f;
                var a = path.SampleAtDistance(d - 6f);
                var b = path.SampleAtDistance(d + 6f);
                float curv = Vector3.Angle(Flat(a.tangent), Flat(b.tangent)) / 45f;
                float turb = Mathf.Clamp01(s.flowSpeed / 8f * 0.7f + curv * 0.5f);
                var wrow = new int[across + 1];
                for (int j = 0; j <= across; j++)
                {
                    float lat = Mathf.Lerp(-hw, hw, j / (float)across);
                    float edge = 1f - Mathf.Clamp01((hw - Mathf.Abs(lat)) / Mathf.Max(1.5f, hw * 0.45f));
                    wrow[j] = water.Add(s.position + s.right * lat, new Vector2(lat * 0.25f, d * 0.25f), new Vector2(s.flowSpeed * settings.flowSpeedScale, s.width),
                                        new Color(edge, Mathf.Clamp01(s.flowSpeed / 10f), turb, 1f));
                }
                if (prevW != null) water.Strip(prevW, wrow);
                prevW = wrow;
            }
            terrain.OrientRange(0, terrain.tri.Count, Vector3.up);
            water.OrientRange(0, water.tri.Count, Vector3.up);

            var tMesh = terrain.Build("EndlessTerrain");
            var tGo = new GameObject("Terrain");
            tGo.transform.SetParent(c.decor, false);
            tGo.AddComponent<MeshFilter>().sharedMesh = tMesh;
            tGo.AddComponent<MeshRenderer>().sharedMaterial = terrainMaterial;
            tGo.AddComponent<MeshCollider>().sharedMesh = tMesh;
            var wMesh = water.Build("EndlessWater");
            var wGo = new GameObject("Water");
            wGo.transform.SetParent(c.go.transform, false);
            wGo.AddComponent<MeshFilter>().sharedMesh = wMesh;
            var wr = wGo.AddComponent<MeshRenderer>();
            wr.sharedMaterial = waterMaterial;
            wr.shadowCastingMode = ShadowCastingMode.Off;
            c.owned.Add(tMesh);
            c.owned.Add(wMesh);

            var rng = new System.Random(Seed * 92821 + chunkIndex * 613 + 7);
            Dress(c.decor, d0, d1, rng);
            Decorate(c, d0, d1, new System.Random(Seed * 15485863 + chunkIndex * 977 + 3));
            chunkIndex++;
            MaxBuildMs = Mathf.Max(MaxBuildMs, (float)watch.Elapsed.TotalMilliseconds);
            return c;
        }

        void FinishChunk(Chunk c)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            float d0 = c.d0, d1 = c.d1;
            EdgeRollers(c, d0, d1);
            while (Planner.NextFreeDistance() < d1)
            {
                var r = Planner.Next();
                SpawnRow(r, c.go.transform);
                ClearEdgesFor(r);
            }
            if (batchStatic) Batch(c);
            chunks.Add(c);
            MaxBuildMs = Mathf.Max(MaxBuildMs, (float)watch.Elapsed.TotalMilliseconds);
        }

        /// <summary>Combines the chunk's static meshes (terrain, plants, rocks, decor) per material. The combined meshes
        /// are destroyed with the chunk, so the endless river never leaks them.</summary>
        static void Batch(Chunk c)
        {
            StaticBatchingUtility.Combine(c.decor.gameObject);
            foreach (var mf in c.decor.GetComponentsInChildren<MeshFilter>())
            {
                var m = mf.sharedMesh;
                if (m != null && m.name.StartsWith("Combined Mesh") && !c.owned.Contains(m)) c.owned.Add(m);
            }
        }

        // ------------------------------------------------------------------ deadly banks
        struct EdgeSeg { public float d0, d1; public int side; public GameObject go; }
        readonly List<EdgeSeg> edgeSegments = new List<EdgeSeg>();
        const float EdgeSegLength = 9f, EdgeSegStep = 7.5f;

        /// <summary>Lines both banks of the chunk with spinning spiked rollers (6 m segments, 0.3 m apart, too close for
        /// the raft to slip between), so scraping the river's edge ends the run.</summary>
        void EdgeRollers(Chunk c, float d0, float d1)
        {
            if (edgeRoller == null) return;
            edgeSegments.RemoveAll(e => e.go == null);
            float hw = HalfWidth;
            for (int side = -1; side <= 1; side += 2)
            {
                // walk along this bank's own curve (inner banks of bends are shorter than the centreline) and lay a
                // segment over every ~7.5 m of bank, stretched to its chord, so a roller never sticks out into the river
                Vector3 Bank(float d) { var smp = path.SampleAtDistance(d); return smp.position + smp.right * side * (hw - 0.35f); }
                Vector3 segStart = Bank(d0), prev = segStart;
                float startD = d0, acc = 0f;
                for (float d = d0 + 0.75f; d <= d1 + 0.001f; d += 0.75f)
                {
                    Vector3 p = Bank(d);
                    acc += Vector3.Distance(p, prev);
                    prev = p;
                    bool last = d + 0.75f > d1 + 0.001f;
                    if (acc < EdgeSegStep && !(last && acc > 2f)) continue;
                    Vector3 chord = p - segStart;
                    float len = chord.magnitude;
                    if (len > 0.5f)
                    {
                        Vector3 mid = (segStart + p) * 0.5f + Vector3.up * 0.3f;
                        var go = Instantiate(edgeRoller, mid, Quaternion.LookRotation(chord, Vector3.up), spinEdgeRollers ? c.go.transform : c.decor);
                        go.transform.localScale = new Vector3(1f, 1f, Mathf.Max(0.2f, (len - 0.25f) / EdgeSegLength));
                        var spin = go.GetComponentInChildren<VisualSpin>();
                        if (spin != null && !spinEdgeRollers) Destroy(spin);
                        else if (spin != null) { spin.degreesPerSecond = Mathf.Abs(spin.degreesPerSecond) * -side; spin.phaseOffset = d * 0.37f; }
                        edgeSegments.Add(new EdgeSeg { d0 = startD, d1 = d, side = side, go = go });
                    }
                    segStart = p; startD = d; acc = 0f;
                }
            }
            // rows planned earlier (previous chunk) may reach into this one
            foreach (var r in Planner.Rows) if (r.distance > d0 - 8f) ClearEdgesFor(r);
        }

        /// <summary>Removes edge rollers where a row's own obstacle already reaches that bank (sweeper posts, fences,
        /// bridges, cannons...), so the two never overlap.</summary>
        void ClearEdgesFor(ObstacleRow r)
        {
            if (edgeSegments.Count == 0) return;
            float hw = HalfWidth;
            bool left = false, right = false;
            foreach (var it in r.items)
            {
                if (!BankAnchored(it.kind)) continue;          // floating hazards may overlap the rollers: both are deadly
                if (it.minX < -hw + 0.6f) left = true;
                if (it.maxX > hw - 0.6f) right = true;
            }
            if (!left && !right) return;
            for (int i = edgeSegments.Count - 1; i >= 0; i--)
            {
                var e = edgeSegments[i];
                if (e.go == null) { edgeSegments.RemoveAt(i); continue; }
                if ((e.side < 0 ? left : right) && e.d1 > r.distance - 3.5f && e.d0 < r.distance + 3.5f)
                {
                    e.go.SetActive(false);
                    Destroy(e.go);
                    edgeSegments.RemoveAt(i);
                }
            }
        }

        static bool BankAnchored(ObstacleKind k) =>
            k == ObstacleKind.Sweeper || k == ObstacleKind.Tentacle || k == ObstacleKind.Windmill || k == ObstacleKind.FireJet || k == ObstacleKind.Mortar ||
            k == ObstacleKind.BankRoller || k == ObstacleKind.BuoyLine || k == ObstacleKind.Gate || k == ObstacleKind.SpikeBridge;

        // ------------------------------------------------------------------ dressing
        void Dress(Transform parent, float d0, float d1, System.Random r)
        {
            float R(float a, float b) => a + (float)r.NextDouble() * (b - a);
            GameObject Pick(GameObject[] set) => set != null && set.Length > 0 ? set[r.Next(set.Length)] : null;
            int seed = settings.seed;
            for (int side = -1; side <= 1; side += 2)
            {
                int sideSeed = side < 0 ? seed : seed + 101;
                // shore: plants on beaches, pebbles and small rocks on narrow banks (lush: the banks read as jungle)
                for (int k = 0; k < 7; k++)
                {
                    var s = path.SampleAtDistance(R(d0, d1));
                    var prof = RiverTerrainBuilder.SideProfile(s, settings, side, sideSeed);
                    float t = R(0.2f, 0.9f);
                    float dd = Mathf.Lerp(prof[3].d, prof[5].d, t), hh = Mathf.Lerp(prof[3].h, prof[5].h, t);
                    Vector3 p = RiverTerrainBuilder.ToWorld(s, dd, hh - 0.15f, side);
                    var prefab = s.bankFlat > 2.4f ? Pick(shoreProps) : Pick(rockProps);
                    Place(prefab, parent, p, R(0f, 360f), R(0.7f, 1.25f));
                }
                // rocks at the foot of the wall
                if (r.NextDouble() < 0.6)
                {
                    var s = path.SampleAtDistance(R(d0, d1));
                    var prof = RiverTerrainBuilder.SideProfile(s, settings, side, sideSeed);
                    Vector3 p = RiverTerrainBuilder.ToWorld(s, prof[5].d + 0.4f, prof[5].h - 0.3f, side);
                    Place(Pick(rockProps), parent, p, R(0f, 360f), R(1.1f, 2.2f));
                }
                // trap props on the bank, aimed at the river (scenery only)
                if (packBankTraps != null && packBankTraps.Length > 0 && r.NextDouble() < 0.35)
                {
                    var s = path.SampleAtDistance(R(d0, d1));
                    var prof = RiverTerrainBuilder.SideProfile(s, settings, side, sideSeed);
                    Vector3 p = RiverTerrainBuilder.ToWorld(s, Mathf.Lerp(prof[4].d, prof[5].d, 0.3f), Mathf.Lerp(prof[4].h, prof[5].h, 0.3f) - 0.1f, side);
                    var go = Instantiate(packBankTraps[r.Next(packBankTraps.Length)], p, Quaternion.LookRotation(-s.right * side, Vector3.up), parent.parent);   // may animate: not static
                    go.transform.localScale *= 1.6f;
                }
                // bushes along the cliff top, so the canyon rim is green
                for (int k = 0; k < 4; k++)
                {
                    var s = path.SampleAtDistance(R(d0, d1));
                    var prof = RiverTerrainBuilder.SideProfile(s, settings, side, sideSeed);
                    var top = prof[prof.Count - 2];
                    var lip = prof[prof.Count - 3];
                    float t = R(0.0f, 0.35f);
                    Vector3 p = RiverTerrainBuilder.ToWorld(s, Mathf.Lerp(lip.d, top.d, t), Mathf.Lerp(lip.h, top.h, t) - 0.2f, side);
                    p = RiverTerrainBuilder.Displace(p, s, side, 1f, seed);
                    Place(Pick(shoreProps), parent, p, R(0f, 360f), R(1.1f, 1.8f));
                }
                // trees on the plateau above the canyon
                for (int k = 0; k < 3; k++)
                {
                    var s = path.SampleAtDistance(R(d0, d1));
                    var prof = RiverTerrainBuilder.SideProfile(s, settings, side, sideSeed);
                    var top = prof[prof.Count - 2];
                    var back = prof[prof.Count - 1];
                    float t = R(0.05f, 0.75f);
                    Vector3 p = RiverTerrainBuilder.ToWorld(s, Mathf.Lerp(top.d, back.d, t), Mathf.Lerp(top.h, back.h, t) - 0.4f, side);
                    p = RiverTerrainBuilder.Displace(p, s, side, 1f, seed);
                    Place(Pick(plateauProps), parent, p, R(0f, 360f), R(0.85f, 1.4f));
                }
            }
        }

        /// <summary>
        /// Bright cartoon banks: chunky stone block stacks at the wall foot and on the cliff top, wooden fences along the
        /// plateau edge and, now and then, a little waterfall pouring off the cliff into the river. Own random stream, so
        /// the obstacle layout does not change when the decor does.
        /// </summary>
        void Decorate(Chunk c, float d0, float d1, System.Random r)
        {
            float R(float a, float b) => a + (float)r.NextDouble() * (b - a);
            Transform parent = c.decor;
            for (int side = -1; side <= 1; side += 2)
            {
                if (cliffBlocks != null && cliffBlocks.Length > 0)
                {
                    // at the wall foot, half buried in the wall
                    for (int k = 0; k < 2; k++)
                    {
                        if (r.NextDouble() > 0.75) continue;
                        var s = path.SampleAtDistance(R(d0, d1));
                        var pts = RiverTerrainBuilder.SidePoints(s, settings, side, out var prof);
                        Vector3 foot = pts[5] + s.right * side * R(0.6f, 1.6f) + Vector3.down * 0.4f;
                        var go = Instantiate(cliffBlocks[r.Next(cliffBlocks.Length)], foot, Quaternion.LookRotation(Flat(s.tangent)) * Quaternion.Euler(0f, R(-12f, 12f), 0f), parent);
                        go.transform.localScale *= R(0.75f, 1.25f);
                    }
                    // on the cliff top, overhanging the edge a little (terraced look)
                    if (r.NextDouble() < 0.6)
                    {
                        var s = path.SampleAtDistance(R(d0, d1));
                        var pts = RiverTerrainBuilder.SidePoints(s, settings, side, out var prof);
                        Vector3 top = pts[pts.Length - 3] + s.right * side * R(1.2f, 2.5f) + Vector3.down * R(1.2f, 2.2f);
                        var go = Instantiate(cliffBlocks[r.Next(cliffBlocks.Length)], top, Quaternion.LookRotation(Flat(s.tangent)) * Quaternion.Euler(0f, R(-20f, 20f), 0f), parent);
                        go.transform.localScale *= R(0.9f, 1.5f);
                    }
                }
                // fences along the plateau edge
                if (fence != null && r.NextDouble() < 0.45)
                {
                    float start = R(d0, d1 - 8f);
                    for (int k = 0; k < 3; k++)
                    {
                        var s = path.SampleAtDistance(start + k * 4.05f);
                        var pts = RiverTerrainBuilder.SidePoints(s, settings, side, out var prof);
                        Vector3 p = Vector3.Lerp(pts[pts.Length - 2], pts[pts.Length - 1], 0.06f) + Vector3.down * 0.1f;
                        Vector3 t = Flat(s.tangent);
                        Instantiate(fence, p, Quaternion.LookRotation(Vector3.Cross(t, Vector3.up), Vector3.up), parent);
                    }
                }
                // a bank waterfall pouring off the cliff
                if (cascadeMaterial != null && r.NextDouble() < cascadeChance)
                    Cascade(c, path.SampleAtDistance(R(d0 + 3f, d1 - 3f)), side, R(1.6f, 3.4f));
            }
        }

        /// <summary>
        /// A waterfall sheet from the plateau edge down to the river. It hangs just in front of the most protruding rock
        /// below each height (sampled across its width), so it never cuts into the bulges and ledges of the wall.
        /// Mesh layout as Env/Waterfall expects: uv.x across, uv.y = metres from the top x 0.3, colour a = edge fade,
        /// r = bottom foam.
        /// </summary>
        void Cascade(Chunk c, RiverSample s, int side, float width)
        {
            Vector3 right = s.right * side;
            Vector3 fwd = Flat(s.tangent);
            // lateral envelope of the wall (distance from the centreline, toward the bank) at each height
            var walls = new List<Vector2>();
            foreach (float off in new[] { -width * 0.5f, 0f, width * 0.5f })
            {
                var ss = path.SampleAtDistance(s.distance + off);
                var pts = RiverTerrainBuilder.SidePoints(ss, settings, side, out _);
                for (int i = 2; i < pts.Length - 1; i++)
                    walls.Add(new Vector2(Vector3.Dot(pts[i] - s.position, right), pts[i].y - s.position.y));
            }
            var lastPts = RiverTerrainBuilder.SidePoints(s, settings, side, out _);
            float topH = lastPts[lastPts.Length - 2].y - s.position.y;
            float lipLat = Vector3.Dot(lastPts[lastPts.Length - 3] - s.position, right);
            float waterLat = HalfWidth - 0.2f;
            const float step = 0.55f;
            int rows = Mathf.Max(3, Mathf.CeilToInt((topH + 0.1f) / step) + 2);
            var k = new MeshKit();
            int[] prev = null;
            float dist = 0f, lastLat = lipLat + 1.2f, lastH = topH + 0.05f;
            const int cols = 5;
            for (int i = 0; i < rows; i++)
            {
                float h, lat;
                if (i == 0) { h = topH + 0.05f; lat = lipLat + 1.2f; }          // water running over the plateau edge
                else
                {
                    h = Mathf.Max(-0.12f, topH - (i - 1) * step);
                    float env = float.MaxValue;
                    foreach (var w in walls) if (w.y >= h - 0.6f) env = Mathf.Min(env, w.x);
                    lat = Mathf.Min(lastLat, env - 0.45f);
                    if (h <= -0.11f) lat = Mathf.Min(lat, waterLat);
                }
                dist += Mathf.Sqrt((lat - lastLat) * (lat - lastLat) + (h - lastH) * (h - lastH));
                lastLat = lat; lastH = h;
                var row = new int[cols];
                for (int j = 0; j < cols; j++)
                {
                    float u = j / (float)(cols - 1);
                    float edge = Mathf.Sin(u * Mathf.PI);
                    Vector3 pos = s.position + right * lat + Vector3.up * h + fwd * ((u - 0.5f) * width * (1f + 0.25f * i / rows));
                    float foam = Mathf.Clamp01(1f - h / 1.6f);
                    row[j] = k.Add(pos, new Vector2(u, dist * 0.3f), new Color(foam, 0f, 0f, Mathf.Clamp01(edge * 1.6f)));
                }
                if (prev != null) k.Strip(prev, row);
                prev = row;
                if (h <= -0.11f) break;
            }
            var mesh = k.Build("BankCascade");
            mesh.RecalculateNormals();
            var go = new GameObject("BankWaterfall");
            go.transform.SetParent(c.decor, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = cascadeMaterial;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            c.owned.Add(mesh);
            if (cascadeSplash != null)
            {
                var sp = Instantiate(cascadeSplash, s.position + right * Mathf.Min(lastLat, waterLat - 0.3f) + Vector3.up * 0.02f, Quaternion.LookRotation(fwd), c.decor);
                sp.transform.localScale = Vector3.one * Mathf.Clamp(width / 2.4f, 0.7f, 1.4f);
            }
        }

        static void Place(GameObject prefab, Transform parent, Vector3 pos, float yaw, float scale)
        {
            if (prefab == null) return;
            var go = Instantiate(prefab, pos, Quaternion.Euler(0f, yaw, 0f), parent);
            go.transform.localScale *= scale;
        }

        // ------------------------------------------------------------------ obstacles
        void SpawnRow(ObstacleRow row, Transform parent)
        {
            var s = path.SampleAtDistance(row.distance);
            Vector3 fwd = Flat(s.tangent);
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            Quaternion rot = Quaternion.LookRotation(fwd, Vector3.up);
            Vector3 At(float x) => s.position + right * x;
            var holder = new GameObject("Row_" + row.index.ToString("000")).transform;
            holder.SetParent(parent, false);
            bool frame = false;
            float hw = HalfWidth;
            foreach (var p in row.items)
            {
                switch (p.kind)
                {
                    case ObstacleKind.Boulder:
                        if (Has(packCrystals)) Fit(packCrystals[Mathf.Abs(p.variant) % packCrystals.Length], holder, At(p.x), rot * Quaternion.Euler(0f, p.phase, 0f), p.size, "an ice crystal");
                        else Rock(holder, At(p.x), p.size, p.variant, p.phase, rot);
                        break;
                    case ObstacleKind.RockPair:
                        if (Has(packCrystals))
                        {
                            Fit(packCrystals[packCrystals.Length - 1], holder, At(p.x - 0.7f * p.size), rot * Quaternion.Euler(0f, p.phase, 0f), p.size, "an ice crystal");
                            Fit(packCrystals[packCrystals.Length - 1], holder, At(p.x + 0.9f * p.size), rot * Quaternion.Euler(0f, p.phase + 130f, 0f), p.size * 0.75f, "an ice crystal");
                        }
                        else
                        {
                            Rock(holder, At(p.x - 0.7f * p.size), p.size, p.variant, p.phase, rot);
                            Rock(holder, At(p.x + 0.9f * p.size), p.size * 0.75f, p.variant + 1, p.phase + 130f, rot);
                        }
                        break;
                    case ObstacleKind.Barrels:
                        if (packMine != null)
                            for (int m = 0; m < 3; m++)      // three spiked sea mines inside the 1.45 m circle
                            {
                                float a = p.phase * Mathf.Deg2Rad + m * Mathf.PI * 2f / 3f;
                                Fit(packMine, holder, At(p.x) + (right * Mathf.Cos(a) + fwd * Mathf.Sin(a)) * 0.78f, rot, 0.62f, "a spiked mine", 0.35f);
                            }
                        else Spawn(barrels, holder, At(p.x), rot * Quaternion.Euler(0f, p.phase, 0f), "floating barrels");
                        break;
                    case ObstacleKind.Crates:
                        if (packBarricade != null) Fit(packBarricade, holder, At(p.x), rot * Quaternion.Euler(0f, p.phase, 0f), 1.65f, "a barricade");
                        else Spawn(crates, holder, At(p.x), rot * Quaternion.Euler(0f, p.phase, 0f), "a crate stack");
                        break;
                    case ObstacleKind.Log:
                    {
                        if (packJersey != null) Stretch(packJersey, holder, At(p.x), rot * Quaternion.Euler(0f, p.extra, 0f), p.size, 1.25f, "a concrete barrier");
                        else
                        {
                            var go = Spawn(log, holder, At(p.x), rot * Quaternion.Euler(0f, p.extra, 0f), "a drifting log");
                            if (go != null) go.transform.localScale = new Vector3(p.size / 6f, 1f, 1f);
                        }
                        break;
                    }
                    case ObstacleKind.BuoyLine:
                        if (packRopeFence != null) RopeFence(holder, At(p.side * (hw + 0.3f)), At(p.extra), rot);
                        else BuoyLine(holder, At(p.side * (hw + 0.3f)), At(p.extra), rot);
                        break;
                    case ObstacleKind.Press:
                        Fit(packPress, holder, At(p.x), rot * Quaternion.Euler(0f, p.phase, 0f), p.size, "a stamping press", packSink, true);
                        break;
                    case ObstacleKind.SpikeTrap:
                        Fit(packSpikeTrap, holder, At(p.x), rot * Quaternion.Euler(0f, p.phase, 0f), p.size, "a spike trap", 0f, true, 1f, 0.45f);
                        break;
                    case ObstacleKind.Buzzsaw:
                        Fit(packBuzzsaw, holder, At(p.x), rot * Quaternion.Euler(0f, p.phase, 0f), p.size, "a buzzsaw", 0.1f, true);
                        break;
                    case ObstacleKind.Gate:
                    {
                        float half = p.extra * 0.5f;
                        if (packPole != null)
                        {
                            Fit(packPole, holder, At(p.x - half), rot, 0.3f, "a gate pole", packSink, true, 3.2f);
                            Fit(packPole, holder, At(p.x + half), rot, 0.3f, "a gate pole", packSink, true, 3.2f);
                        }
                        else
                        {
                            Spawn(gatePost, holder, At(p.x - half), rot, "a gate post");
                            Spawn(gatePost, holder, At(p.x + half), rot, "a gate post");
                        }
                        var banner = Spawn(gateBanner, holder, At(p.x), rot, null);
                        if (banner != null) banner.transform.localScale = new Vector3(p.extra + 0.6f, 1f, 1f);
                        break;
                    }
                    case ObstacleKind.Pendulum:
                    {
                        if (!frame) { Spawn(pendulumFrame, holder, At(0f), rot, null); frame = true; }
                        var go = Spawn(pendulumSwing, holder, At(p.x) + Vector3.up * 4.55f, rot, "a swinging log");
                        if (go != null)
                        {
                            var log = go.transform.Find("Log");
                            if (log != null) log.localScale = new Vector3(p.size / 5f, 1f, 1f);
                            go.GetComponent<KinematicObstacle>().phaseOffset = p.phase;
                        }
                        break;
                    }
                    case ObstacleKind.Sweeper:
                    {
                        var go = Spawn(sweeper, holder, At(p.side * (hw + 0.2f)), Quaternion.LookRotation(-right * p.side, Vector3.up), "a sweeper arm");
                        if (go != null)
                        {
                            var ko = go.GetComponentInChildren<RotatingSweeper>();
                            ko.phaseOffset = p.phase;
                            ko.reach = p.size;
                            var arm = ko.transform.Find("Arm");
                            if (arm != null) arm.localScale = new Vector3(1f, 1f, p.size / 5f);
                        }
                        break;
                    }
                    case ObstacleKind.Barrier:
                    {
                        var go = Spawn(barrier, holder, At(p.x), rot, "a sliding spiked wall");
                        if (go != null)
                        {
                            var mb = go.GetComponent<MovingBarrier>();
                            mb.travel = Vector3.right * p.size;
                            mb.phaseOffset = p.phase;
                            if (packSpikeWall != null)
                            {
                                // the pack's spiked wall rides on the sliding body (long side along the flow)
                                foreach (var mr in go.GetComponentsInChildren<MeshRenderer>()) mr.enabled = false;
                                var wall = Fit(packSpikeWall, go.transform, go.transform.position, rot * Quaternion.Euler(0f, 90f, 0f), 1.75f, null, packSink, false);
                                foreach (var c in go.GetComponents<Collider>()) Destroy(c);
                                wall.transform.SetParent(go.transform, true);
                            }
                        }
                        break;
                    }
                    case ObstacleKind.BankRoller:
                    {
                        var go = Spawn(spikeRoller, holder, At(p.side * (hw - 0.35f)) + Vector3.up * 0.3f, rot, "a spiked roller");
                        if (go != null) go.GetComponentInChildren<KinematicObstacle>().phaseOffset = p.phase;
                        break;
                    }
                    case ObstacleKind.StarSpinner:
                    {
                        var go = Spawn(starSpinner, holder, At(p.x), rot, "a spinning spike star");
                        if (go != null)
                        {
                            go.transform.localScale = Vector3.one * (p.size / 2.5f);
                            var spin = go.GetComponent<SpinObstacle>();
                            spin.phaseOffset = p.phase;
                            spin.degreesPerSecond = Mathf.Abs(spin.degreesPerSecond) * p.side;
                        }
                        break;
                    }
                    case ObstacleKind.Puffer:
                    {
                        var go = Spawn(puffer, holder, At(p.x), rot, "a puffer mine");
                        if (go != null)
                        {
                            go.transform.localScale = Vector3.one * (p.size / 1.5f);
                            go.GetComponent<KinematicObstacle>().phaseOffset = p.phase;
                        }
                        break;
                    }
                    case ObstacleKind.Tentacle:
                    {
                        var go = Spawn(tentacle, holder, At(p.x) + Vector3.down * 0.4f, Quaternion.LookRotation(-right * p.side, Vector3.up), "a kraken tentacle");
                        if (go != null) go.GetComponent<KinematicObstacle>().phaseOffset = p.phase;
                        break;
                    }
                    case ObstacleKind.Windmill:
                    {
                        var go = Spawn(windmill, holder, At(p.side * (hw + 1.6f)), rot, "windmill blades");
                        if (go != null) foreach (var k in go.GetComponentsInChildren<KinematicObstacle>()) k.phaseOffset = p.phase;
                        break;
                    }
                    case ObstacleKind.FireJet:
                    {
                        var go = Spawn(fireJet, holder, At(p.side * (hw + 0.2f)), Quaternion.LookRotation(-right * p.side, Vector3.up), "a fire jet");
                        if (go != null)
                        {
                            var fj = go.GetComponent<FireJetObstacle>();
                            fj.reach = p.size;
                            fj.phaseOffset = p.phase;
                        }
                        break;
                    }
                    case ObstacleKind.Whirlpool:
                    {
                        var go = Spawn(whirlpool, holder, At(p.x), rot, "a whirlpool grinder");
                        if (go != null)
                        {
                            go.transform.localScale = Vector3.one * (p.size / 3.5f);
                            var spin = go.GetComponent<SpinObstacle>();
                            spin.phaseOffset = p.phase;
                            spin.degreesPerSecond = Mathf.Abs(spin.degreesPerSecond) * p.side;
                            go.GetComponent<WhirlpoolPull>().direction = p.side;
                        }
                        break;
                    }
                    case ObstacleKind.Mortar:
                    {
                        Vector3 basePos = At(p.side * (hw + 1.1f));
                        var go = Spawn(mortar, holder, basePos, Quaternion.LookRotation(-right * p.side, Vector3.up), "a cannonball");
                        if (go != null)
                        {
                            var m = go.GetComponent<MortarObstacle>();
                            m.targetDistance = Mathf.Abs(p.side * (hw + 1.1f) - p.extra);
                            m.zoneScale = p.size / 1.6f;
                            m.phaseOffset = p.phase;
                        }
                        break;
                    }
                    case ObstacleKind.SpikeSwing:
                    {
                        if (!frame) { Spawn(pendulumFrame, holder, At(0f), rot, null); frame = true; }
                        var go = Spawn(spikeSwing, holder, At(p.x) + Vector3.up * 4.55f, rot, "a swinging spiked log");
                        if (go != null)
                        {
                            var log = go.transform.Find("Log");
                            if (log != null) log.localScale = new Vector3(p.size / 5f, 1f, 1f);
                            go.GetComponent<KinematicObstacle>().phaseOffset = p.phase;
                            foreach (var vs in go.GetComponentsInChildren<VisualSpin>()) vs.phaseOffset = p.phase;
                        }
                        break;
                    }
                    case ObstacleKind.MineField:
                    {
                        int n = Mathf.Max(1, Mathf.RoundToInt(p.size));
                        float first = p.x - (n - 1) * p.extra * 0.5f;
                        for (int m = 0; m < n; m++)
                        {
                            float along = ((p.variant >> m) & 1) == 0 ? -0.9f : 0.9f;      // staggered along the flow
                            var go = Spawn(spikeMine, holder, At(first + m * p.extra) + fwd * along, rot * Quaternion.Euler(0f, p.phase * 37f + m * 71f, 0f), "a spiked mine");
                            if (go != null) go.GetComponent<KinematicObstacle>().phaseOffset = p.phase + m * 0.8f;
                        }
                        break;
                    }
                    case ObstacleKind.SpikeLogRoll:
                    {
                        var go = Spawn(spikeLogRoll, holder, At(p.x), rot, "a rolling spiked log");
                        if (go != null)
                        {
                            var mb = go.GetComponent<MovingBarrier>();
                            mb.travel = Vector3.right * p.extra;
                            mb.phaseOffset = p.phase;
                            var body = go.transform.Find("Body");
                            if (body != null) body.localScale = new Vector3(p.size / 4f, 1f, 1f);
                            var foam = go.transform.Find("Obs_LogRollFoam");
                            if (foam != null) foam.localScale = new Vector3(p.size * 0.6f, 1f, 0.9f);
                            foreach (var vs in go.GetComponentsInChildren<VisualSpin>()) vs.phaseOffset = p.phase;
                        }
                        break;
                    }
                    case ObstacleKind.SpikeBridge:
                    {
                        if (!frame) { Spawn(spikeBridge, holder, At(0f), rot, null); frame = true; }
                        float bank = p.side * (hw + 0.6f);
                        float len = Mathf.Abs(bank - p.extra);
                        var go = Spawn(spikeCurtain, holder, At((bank + p.extra) * 0.5f) + Vector3.up * 4.45f, rot, "a spiked log under the bridge");
                        if (go != null)
                        {
                            var log = go.transform.Find("Log");
                            if (log != null) log.localScale = new Vector3(len / 4f, 1f, 1f);
                            go.GetComponent<KinematicObstacle>().phaseOffset = p.phase;
                            foreach (var vs in go.GetComponentsInChildren<VisualSpin>()) vs.phaseOffset = p.phase;
                        }
                        break;
                    }
                    case ObstacleKind.WaitGate:
                    {
                        var go = Spawn(waitGate, holder, At(p.x), rot, "a closing spike gate");
                        if (go != null)
                        {
                            float half = p.size * 0.5f;
                            var pl = go.transform.Find("PostL"); if (pl != null) pl.localPosition = new Vector3(-half, 0f, 0f);
                            var pr = go.transform.Find("PostR"); if (pr != null) pr.localPosition = new Vector3(half, 0f, 0f);
                            var log = go.transform.Find("Lift/Log");
                            if (log != null) log.localScale = new Vector3(p.size / 4f, 1f, 1f);
                            var gate = go.GetComponentInChildren<WaitGateObstacle>();
                            gate.phaseOffset = p.phase;
                            gate.riverDistance = row.distance;
                        }
                        break;
                    }
                    case ObstacleKind.Spinner when packBlade != null:
                        Fit(packBlade, holder, At(p.x), rot * Quaternion.Euler(0f, p.phase * 30f, 0f), p.size * 0.5f + 0.25f, "a spinning blade", packSink, true);
                        break;
                    case ObstacleKind.Spinner:
                    {
                        var go = Spawn(spinner, holder, At(p.x), rot, "a spinning log");
                        if (go != null)
                        {
                            var ko = go.GetComponent<RotatingSweeper>();
                            ko.phaseOffset = p.phase;
                            ko.degreesPerSecond = Mathf.Abs(ko.degreesPerSecond) * p.side;
                            var arm = go.transform.Find("Arm");
                            if (arm != null) arm.localScale = new Vector3(p.size / 5f, 1f, 1f);
                        }
                        break;
                    }
                }
            }
            SpeedUp(holder, row.distance);
        }

        /// <summary>Danger ramp: every moving hazard of a row (clock-driven and pack animators) runs faster downstream.</summary>
        static void SpeedUp(Transform holder, float distance)
        {
            float k = ObstaclePlanner.HazardSpeed(distance);
            foreach (var ko in holder.GetComponentsInChildren<KinematicObstacle>(true))
                if (!(ko is WaitGateObstacle)) ko.speed *= k;            // wait gates keep their tested open window
            foreach (var an in holder.GetComponentsInChildren<Animator>(true)) an.speed *= k;
        }

        static GameObject Spawn(GameObject prefab, Transform parent, Vector3 pos, Quaternion rot, string hazardLabel)
        {
            if (prefab == null) return null;
            var go = Instantiate(prefab, pos, rot, parent);
            if (hazardLabel != null)
            {
                var h = go.GetComponent<EndlessHazard>();
                if (h == null) h = go.AddComponent<EndlessHazard>();
                h.label = hazardLabel;
            }
            return go;
        }

        static bool Has(GameObject[] a) => a != null && a.Length > 0 && a[0] != null;

        struct PackBounds { public Bounds b; public float radius; }
        static readonly Dictionary<GameObject, PackBounds> packBounds = new Dictionary<GameObject, PackBounds>();

        /// <summary>Bounds of a prefab in its own space (renderers and colliders), plus the circumscribed horizontal radius around the pivot.</summary>
        static PackBounds Measure(GameObject prefab)
        {
            if (packBounds.TryGetValue(prefab, out var pb)) return pb;
            var probe = Instantiate(prefab, new Vector3(0f, -5000f, 0f), Quaternion.identity);
            bool first = true;
            var b = new Bounds();
            foreach (var r in probe.GetComponentsInChildren<Renderer>())
            {
                if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds);
            }
            // colliders can be bigger than the mesh (the pack's ice wall box is): the footprint must cover them too
            Physics.SyncTransforms();
            foreach (var c in probe.GetComponentsInChildren<Collider>())
            {
                if (first) { b = c.bounds; first = false; } else b.Encapsulate(c.bounds);
            }
            b.center -= new Vector3(0f, -5000f, 0f);
            float ax = Mathf.Max(Mathf.Abs(b.min.x), Mathf.Abs(b.max.x)), az = Mathf.Max(Mathf.Abs(b.min.z), Mathf.Abs(b.max.z));
            pb = new PackBounds { b = b, radius = Mathf.Max(0.05f, Mathf.Sqrt(ax * ax + az * az)) };
            DestroyImmediate(probe);
            packBounds[prefab] = pb;
            return pb;
        }

        /// <summary>
        /// Places a pack prefab scaled uniformly so that, whatever its rotation, it stays inside a circle of
        /// <paramref name="radius"/> around <paramref name="pos"/> (the planner's footprint). Its base sits
        /// <paramref name="sink"/> m under the water. Guarantees a solid collider and marks it as a hazard.
        /// <paramref name="heightBoost"/> stretches it vertically (tall gate poles); <paramref name="topAbove"/>, when set,
        /// places its top that far above the water instead (pieces whose mechanism hangs below the pivot).
        /// </summary>
        GameObject Fit(GameObject prefab, Transform parent, Vector3 pos, Quaternion rot, float radius, string hazardLabel, float sink = -1f, bool solid = true, float heightBoost = 1f, float topAbove = float.NaN)
        {
            if (prefab == null) return null;
            if (sink < 0f) sink = packSink;
            var pb = Measure(prefab);
            float k = radius / pb.radius;
            var go = Instantiate(prefab, pos, rot, parent);
            go.transform.localScale = Vector3.Scale(prefab.transform.localScale, new Vector3(k, k * heightBoost, k));
            go.transform.position = float.IsNaN(topAbove) ? pos + Vector3.up * (-sink - pb.b.min.y * k * heightBoost)
                                                           : pos + Vector3.up * (topAbove - pb.b.max.y * k * heightBoost);
            if (solid && !HasSolidCollider(go))
            {
                var bc = go.AddComponent<BoxCollider>();
                bc.center = pb.b.center;
                bc.size = pb.b.size;
            }
            if (hazardLabel != null)
            {
                var h = go.GetComponent<EndlessHazard>();
                if (h == null) h = go.AddComponent<EndlessHazard>();
                h.label = hazardLabel;
            }
            return go;
        }

        static bool HasSolidCollider(GameObject go)
        {
            foreach (var c in go.GetComponentsInChildren<Collider>()) if (!c.isTrigger) return true;
            return false;
        }

        /// <summary>A long pack piece stretched along its X axis to <paramref name="length"/> (jersey barrier, rope fence).</summary>
        GameObject Stretch(GameObject prefab, Transform parent, Vector3 pos, Quaternion rot, float length, float thickness, string hazardLabel)
        {
            var pb = Measure(prefab);
            float sx = length / Mathf.Max(0.1f, pb.b.size.x);
            float sz = thickness / Mathf.Max(0.1f, pb.b.size.z);
            float sy = Mathf.Max(1.2f, sz);
            var go = Instantiate(prefab, pos, rot, parent);
            go.transform.localScale = Vector3.Scale(prefab.transform.localScale, new Vector3(sx, sy, sz));
            go.transform.position = pos + Vector3.up * (-packSink - pb.b.min.y * sy) - rot * new Vector3(pb.b.center.x * sx, 0f, pb.b.center.z * sz);
            if (!HasSolidCollider(go)) { var bc = go.AddComponent<BoxCollider>(); bc.center = pb.b.center; bc.size = pb.b.size; }
            var h = go.GetComponent<EndlessHazard>();
            if (h == null) h = go.AddComponent<EndlessHazard>();
            h.label = hazardLabel;
            return go;
        }

        /// <summary>Rope fence from the bank to <paramref name="inner"/>, made of pack fence segments (posts in the water).</summary>
        void RopeFence(Transform parent, Vector3 bank, Vector3 inner, Quaternion rot)
        {
            Vector3 d = inner - bank;
            float len = d.magnitude;
            int n = Mathf.Max(1, Mathf.CeilToInt(len / 4.5f));
            Quaternion along = Quaternion.LookRotation(Vector3.Cross(d.normalized, Vector3.up), Vector3.up);   // segment X axis along the fence
            float seg = len / n;
            for (int i = 0; i < n; i++)
            {
                Vector3 c = bank + d * ((i + 0.5f) / n);
                var go = Stretch(packRopeFence, parent, c, along, seg + 0.1f, 0.55f, "a rope fence");
                // fill under the rope so the raft can't slip between the posts
                var bc = go.AddComponent<BoxCollider>();
                var pb = Measure(packRopeFence);
                bc.center = new Vector3(pb.b.center.x, pb.b.min.y + pb.b.size.y * 0.5f, pb.b.center.z);
                bc.size = new Vector3(pb.b.size.x, pb.b.size.y, Mathf.Max(pb.b.size.z, 0.4f));
            }
        }

        void BuoyLine(Transform parent, Vector3 bank, Vector3 inner, Quaternion rot)
        {
            Vector3 d = inner - bank;
            float len = d.magnitude;
            int n = Mathf.Max(2, Mathf.CeilToInt(len / 1.25f) + 1);
            for (int i = 0; i < n; i++)
                Spawn(buoy, parent, Vector3.Lerp(bank, inner, i / (float)(n - 1)), rot, "a buoy line");
            if (buoyRope != null)
            {
                var rope = Instantiate(buoyRope, (bank + inner) * 0.5f, Quaternion.LookRotation(d.normalized, Vector3.up), parent);
                rope.transform.localScale = new Vector3(1f, 1f, len);
            }
        }

        void Rock(Transform parent, Vector3 pos, float radius, int variant, float yaw, Quaternion rot)
        {
            if (boulders == null || boulders.Length == 0) return;
            var prefab = boulders[Mathf.Abs(variant) % boulders.Length];
            if (!rockRadius.TryGetValue(prefab, out float local))
            {
                // circumscribed horizontal radius of the prefab, so the rock never pokes out of its planned footprint
                var mf = prefab.GetComponentInChildren<MeshFilter>();
                var e = mf.sharedMesh.bounds.extents;
                var sc = mf.transform.lossyScale;
                local = Mathf.Sqrt(e.x * e.x * sc.x * sc.x + e.z * e.z * sc.z * sc.z) + Mathf.Max(Mathf.Abs(mf.sharedMesh.bounds.center.x * sc.x), Mathf.Abs(mf.sharedMesh.bounds.center.z * sc.z));
                rockRadius[prefab] = local;
            }
            float k = radius / Mathf.Max(0.1f, local);
            var go = Instantiate(prefab, pos, rot * Quaternion.Euler(0f, yaw, 0f), parent);
            go.transform.localScale = prefab.transform.localScale * k;
            go.transform.position += Vector3.down * 0.35f * k;
            var h = go.AddComponent<EndlessHazard>();
            h.label = "a boulder";
        }
    }
}
