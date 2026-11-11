using System;
using System.Collections.Generic;
using UnityEngine;

namespace FartOnline.Game.Endless
{
    public enum ObstacleKind : byte
    {
        Boulder, RockPair, Barrels, Crates, Log, BuoyLine, Gate, Pendulum, Sweeper, Barrier, Spinner,
        /// <summary>Hyper-Casual Obstacle Pack traps (animated in place; their footprint is their full reach).</summary>
        Press, SpikeTrap, Buzzsaw,
        /// <summary>Animated hazards inspired by obstacle-course rafting games (original geometry).</summary>
        BankRoller, StarSpinner, Puffer, Tentacle, Windmill,
        /// <summary>Timed killers: bank flamethrower jets, whirlpools that pull the raft in, bank cannons lobbing balls.</summary>
        FireJet, Whirlpool, Mortar,
        /// <summary>Spiked-log hazards (red rollers, sea mines, spike bridges): a swinging spiked log under a gantry,
        /// a cluster of floating spiked mines, a spiked roller rolling on the water while it slides sideways, and a bridge
        /// with spiked logs hanging down to the water everywhere except the gap.</summary>
        SpikeSwing, MineField, SpikeLogRoll, SpikeBridge,
        /// <summary>A spiked log on a lift between two posts in the gap of a spike-bridge row: wait until it opens.</summary>
        WaitGate
    }

    /// <summary>One obstacle in river coordinates (distance along the river, lateral x in metres, + = right).</summary>
    [Serializable]
    public struct ObstaclePlacement
    {
        public ObstacleKind kind;
        public float distance;
        public float x;
        /// <summary>Kind-specific size: radius (rocks), length (logs, spinner, barrier travel), reach (sweeper).</summary>
        public float size;
        /// <summary>Kind-specific: yaw (logs), inner edge (buoy lines), opening (gate).</summary>
        public float extra;
        /// <summary>-1 = left bank, +1 = right bank (bank-anchored kinds).</summary>
        public int side;
        public float phase;
        public int variant;
        /// <summary>Conservative lateral footprint: no part of the obstacle is ever outside [minX, maxX], whatever the
        /// time (moving parts included).</summary>
        public float minX, maxX;
    }

    /// <summary>An obstacle row: the guaranteed safe gap and the obstacles around it.</summary>
    public class ObstacleRow
    {
        public int index;
        public float distance;
        public float safeX;
        public float gap;
        public readonly List<ObstaclePlacement> items = new List<ObstaclePlacement>();

        public float GapMin => safeX - gap * 0.5f;
        public float GapMax => safeX + gap * 0.5f;
    }

    /// <summary>
    /// Deterministic obstacle layout for the endless river (same seed = same river on every machine).
    ///
    /// Guarantee: every row keeps a SAFE GAP at least the raft's diameter plus a margin wide, and every obstacle's
    /// footprint (the whole sweep of moving parts, at any time) stays outside it. Around each row the safe path holds
    /// the gap's centre for <see cref="Hold"/> metres (longer than any obstacle's reach along the flow plus the raft
    /// radius); between rows it moves sideways no more than the raft can travel in the time the current gives it
    /// (<see cref="MaxShift"/>). So there is always a way through, at every speed, for any seed. Other lanes may be open
    /// too, or blocked only part of the time by moving obstacles.
    /// Difficulty starts at <see cref="StartDifficulty"/> and rises with distance: rows get closer (about 19 m at the
    /// start, 16 m at 1.5 km, 14.5 m by 4 km), gaps narrower, more of the obstacles move (72% at the start), and every
    /// moving hazard runs faster (<see cref="HazardSpeed"/>, 1.6x to 2.35x). Speed never changes a
    /// footprint (each is the whole sweep), so the guarantee holds at any speed.
    /// </summary>
    public class ObstaclePlanner
    {
        /// <summary>Half the width of the open water (m).</summary>
        public readonly float halfWidth;
        public readonly float raftRadius;
        /// <summary>Spiked rollers line both banks (see <see cref="EndlessRiver"/>): the safe gap keeps this far (m) from
        /// each bank, plus a margin, so the guaranteed lane never touches them.</summary>
        public readonly float edgeGuard;
        /// <summary>Narrowest safe gap allowed, beyond the raft's diameter (m).</summary>
        public const float MinGapMargin = 0.9f;
        /// <summary>Along-flow distance either side of a row over which the safe path stays at the row's gap (m).</summary>
        public const float Hold = 7f;
        /// <summary>Conservative sideways speed of a raft steered by one player (m/s) and the time to react (s).</summary>
        public const float LateralSpeed = 2.0f, ReactionTime = 1.2f;
        /// <summary>Fire jets: how far inside the bank line the flame starts (nozzle mouth).</summary>
        public const float FireStart = 0.55f;

        readonly System.Random rng;
        readonly Func<float, float> flowAt;
        readonly List<ObstacleRow> rows = new List<ObstacleRow>();
        float next;
        float lastSafeX;
        int count;

        public IReadOnlyList<ObstacleRow> Rows => rows;
        /// <summary>Distances where no row may be placed (waterfall drops and their run-out).</summary>
        public Func<float, bool> IsQuiet;
        public float NextDistance => next;

        public ObstaclePlanner(int seed, float firstRow, float halfWidth, float raftRadius, Func<float, float> flowAt, float edgeGuard = 0f)
        {
            this.edgeGuard = edgeGuard;
            rng = new System.Random(seed * 7919 + 17);
            next = firstRow;
            this.halfWidth = halfWidth;
            this.raftRadius = raftRadius;
            this.flowAt = flowAt;
        }

        // ------------------------------------------------------------------ difficulty curves (0.3 at the start, 1 at 1.5 km)
        /// <summary>Base difficulty at the very first metre: the river is already dangerous from the start.</summary>
        public const float StartDifficulty = 0.3f;
        public static float Difficulty(float distance) => Mathf.Clamp01(StartDifficulty + (1f - StartDifficulty) * distance / 1500f);
        public static float Spacing(float d01) => Mathf.Lerp(20.5f, 16f, d01);
        /// <summary>Beyond 1.5 km: 0 at 1.5 km, 1 at 4 km.</summary>
        public static float Overdrive(float distance) => Mathf.Clamp01((distance - 1500f) / 2500f);
        /// <summary>Row spacing at a distance, including the overdrive past 1.5 km (never under 2 x Hold + 0.5).</summary>
        public static float SpacingAt(float distance) => Mathf.Lerp(Spacing(Difficulty(distance)), 14.5f, Overdrive(distance));
        /// <summary>Speed multiplier for every moving hazard of a row: 1.6x at the start, 2.0x at 1.5 km, 2.35x at 4 km.</summary>
        public static float HazardSpeed(float distance) => 1.45f + 0.55f * Difficulty(distance) + 0.35f * Overdrive(distance);
        /// <summary>Danger level shown to players (1 at the start, +1 every 400 m, max 10).</summary>
        public static int DangerLevel(float score) => Mathf.Min(10, 1 + Mathf.FloorToInt(score / 400f));
        /// <summary>Safe gap: the raft's diameter + 1.8 m at the start, + 1.0 m at full difficulty (a tight squeeze).</summary>
        public float Gap(float d01) => Mathf.Lerp(raftRadius * 2f + 1.8f, raftRadius * 2f + 1.0f, d01);

        /// <summary>Largest sideways move of the safe path allowed between two rows <paramref name="spacing"/> metres apart.</summary>
        public static float MaxShift(float spacing, float flow)
        {
            float t = Mathf.Max(0f, spacing - 2f * Hold) / Mathf.Max(0.5f, flow);
            return LateralSpeed * Mathf.Max(0f, t - ReactionTime);
        }

        float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
        bool Chance(float p) => rng.NextDouble() < p;

        /// <summary>Plans the next row.</summary>
        /// <summary>Distance of the next row after skipping quiet zones (no row is generated).</summary>
        public float NextFreeDistance()
        {
            for (int guard = 0; IsQuiet != null && IsQuiet(next) && guard < 400; guard++) next += 4f;
            return next;
        }

        public ObstacleRow Next()
        {
            float d = NextFreeDistance();
            float d01 = Difficulty(d);
            float spacing = Mathf.Max(2f * Hold + 0.5f, SpacingAt(d) * R(0.92f, 1.15f));
            float gap = Gap(d01);
            float limit = halfWidth - edgeGuard - gap * 0.5f - 0.3f;
            float shift = rows.Count == 0 ? limit : MaxShift(d - rows[rows.Count - 1].distance, flowAt(d));
            float lo = Mathf.Max(-limit, lastSafeX - shift), hi = Mathf.Min(limit, lastSafeX + shift);
            float safe = Mathf.Clamp(R(lo, hi), -limit, limit);

            var row = new ObstacleRow { index = count++, distance = d, safeX = safe, gap = gap };
            // most obstacles are animated from the first row (static rocks and crates are the minority)
            float moving = Mathf.Lerp(Mathf.Lerp(0.72f, 0.88f, d01), 0.95f, Overdrive(d));
            bool closed = safe - gap * 0.5f > -halfWidth + 1f && safe + gap * 0.5f < halfWidth - 1f;
            if (closed && Chance(Mathf.Lerp(0.2f, 0.32f, d01)))
            {
                if (Chance(0.6f))
                {
                    SpikeBridge(row);
                    if (d > 150f && Chance(Mathf.Lerp(0.45f, 0.65f, d01))) WaitGate(row);
                }
                else Gate(row);
            }
            else
            {
                float fill = 1f;
                Fill(row, -halfWidth, row.GapMin, -1, fill, moving);
                Fill(row, row.GapMax, halfWidth, +1, fill, moving);
                if (row.items.Count == 0) Fill(row, -halfWidth, row.GapMin, -1, 1f, moving);   // never an empty row
                if (row.items.Count == 0) Fill(row, row.GapMax, halfWidth, +1, 1f, moving);
                Plug(row, -halfWidth + edgeGuard, row.GapMin);
                Plug(row, row.GapMax, halfWidth - edgeGuard);
            }
            Validate(row);
            rows.Add(row);
            lastSafeX = safe;
            next = d + spacing;
            return row;
        }

        /// <summary>Drops rows that are entirely behind <paramref name="distance"/>.</summary>
        public void Forget(float distance)
        {
            int n = 0;
            while (n < rows.Count - 2 && rows[n + 1].distance + Hold < distance) n++;
            if (n > 0) rows.RemoveRange(0, n);
        }

        /// <summary>Lateral position of the guaranteed safe path at a distance (holds at rows, ramps between them).</summary>
        public float SafeX(float distance)
        {
            if (rows.Count == 0) return 0f;
            if (distance <= rows[0].distance) return rows[0].safeX;
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                if (distance <= r.distance + Hold) return distance >= r.distance - Hold ? r.safeX : Ramp(rows[i - 1], r, distance);
            }
            return rows[rows.Count - 1].safeX;
        }

        static float Ramp(ObstacleRow a, ObstacleRow b, float distance)
        {
            float t = Mathf.InverseLerp(a.distance + Hold, b.distance - Hold, distance);
            return Mathf.Lerp(a.safeX, b.safeX, Mathf.SmoothStep(0f, 1f, t));
        }

        // ------------------------------------------------------------------ patterns
        /// <summary>A painted gate around the gap with buoy lines to both banks: the gap is the only way through.</summary>
        void Gate(ObstacleRow row)
        {
            float post = 0.35f;
            Add(row, new ObstaclePlacement { kind = ObstacleKind.Gate, x = row.safeX, extra = row.gap + 2f * post, minX = row.GapMin - post * 2f, maxX = row.GapMax + post * 2f, side = 0 },
                skipCheck: true);   // the gate frame straddles the gap; only its posts (outside the gap) are solid
            BuoyLine(row, -1, row.GapMin - post * 2f);
            BuoyLine(row, +1, row.GapMax + post * 2f);
        }

        /// <summary>
        /// Closes every other lane: each stretch of open water between [a, b] that no footprint covers gets a line of
        /// spiked mines, so the safe gap is the only easy way through (moving hazards still leave timing windows).
        /// </summary>
        void Plug(ObstacleRow row, float a, float b)
        {
            const float pad = 0.6f, mineFoot = 1.8f, step = 1.75f;
            var taken = new List<Vector2>();
            foreach (var q in row.items) taken.Add(new Vector2(q.minX - pad, q.maxX + pad));
            taken.Add(new Vector2(row.GapMin - 0.25f, row.GapMax + 0.25f));
            taken.Sort((u, v) => u.x.CompareTo(v.x));
            float cursor = a;
            var free = new List<Vector2>();
            foreach (var t in taken)
            {
                if (t.y <= cursor) continue;
                if (t.x > cursor) free.Add(new Vector2(cursor, Mathf.Min(t.x, b)));
                cursor = Mathf.Max(cursor, t.y);
                if (cursor >= b) break;
            }
            if (cursor < b) free.Add(new Vector2(cursor, b));
            foreach (var f in free)
            {
                float w = f.y - f.x;
                if (w < mineFoot) continue;
                int n = Mathf.Clamp(Mathf.FloorToInt((w - mineFoot) / step) + 1, 1, 5);
                float foot = (n - 1) * step + mineFoot;
                float x = (f.x + f.y) * 0.5f;
                Add(row, new ObstaclePlacement { kind = ObstacleKind.MineField, x = x, size = n, extra = step, phase = R(0f, 10f), variant = rng.Next(1000),
                                                 minX = x - foot * 0.5f, maxX = x + foot * 0.5f });
            }
        }

        /// <summary>The only gap is closed by a spiked log that lifts open for a few seconds now and then (the open
        /// window is longer than the raft needs to pass at the slowest current); posts stand just outside the gap.</summary>
        void WaitGate(ObstacleRow row)
        {
            Add(row, new ObstaclePlacement { kind = ObstacleKind.WaitGate, x = row.safeX, size = row.gap + 0.7f, phase = R(0f, 10f),
                                             minX = row.GapMin - 0.55f, maxX = row.GapMax + 0.55f }, skipCheck: true);
        }

        /// <summary>A wooden bridge over the whole river with spiked logs hanging to the water on both sides of the gap.</summary>
        void SpikeBridge(ObstacleRow row)
        {
            const float pad = 0.3f;
            for (int side = -1; side <= 1; side += 2)
            {
                float inner = side < 0 ? row.GapMin - pad - 0.7f : row.GapMax + pad + 0.7f;
                float bank = side * halfWidth;
                if (Mathf.Abs(bank - inner) < 0.8f) continue;
                Add(row, new ObstaclePlacement { kind = ObstacleKind.SpikeBridge, side = side, x = (bank + inner) * 0.5f, extra = inner, phase = R(0f, 10f),
                                                 minX = Mathf.Min(bank, inner) - 0.7f, maxX = Mathf.Max(bank, inner) + 0.7f });
            }
        }

        void BuoyLine(ObstacleRow row, int side, float inner)
        {
            float bank = side * halfWidth;
            if (Mathf.Abs(bank - inner) < 0.6f) return;
            Add(row, new ObstaclePlacement { kind = ObstacleKind.BuoyLine, side = side, x = (bank + inner) * 0.5f, extra = inner,
                                             minX = Mathf.Min(bank, inner) - 0.5f, maxX = Mathf.Max(bank, inner) + 0.5f });
        }

        /// <summary>Fills [a, b] (one side of the gap; <paramref name="side"/> = which bank it touches) with up to two obstacles.</summary>
        void Fill(ObstacleRow row, float a, float b, int side, float chance, float moving)
        {
            const float margin = 0.25f;
            if (b - a < 1.2f || !Chance(chance)) return;
            float gapEdge = side < 0 ? b - margin : a + margin;     // closest the footprint may come to the gap
            float bank = side * halfWidth;

            int tries = 0, placed = 0, want = (b - a) > 8f && Chance(0.6f) ? 3 : (b - a) > 4.5f && Chance(0.9f) ? 2 : 1;
            while (placed < want && tries++ < 16)
            {
                bool dynamic = Chance(moving);
                ObstaclePlacement p;
                if (!Pick(dynamic, side, bank, gapEdge, out p)) continue;
                if (p.maxX > row.GapMin && p.minX < row.GapMax) continue;      // belt and braces: never touch the gap
                // keep two obstacles of the same row apart
                bool overlap = false;
                foreach (var q in row.items) if (p.minX < q.maxX + 0.6f && p.maxX > q.minX - 0.6f) overlap = true;
                if (overlap) continue;
                Add(row, p);
                placed++;
            }
        }

        /// <summary>Picks an obstacle whose footprint fits between the bank and the gap edge on one side.</summary>
        bool Pick(bool dynamic, int side, float bank, float gapEdge, out ObstaclePlacement p)
        {
            p = default;
            float room = Mathf.Abs(gapEdge - bank) + 0.5f;     // usable width from (slightly under) the bank to the gap edge
            float Pos(float halfFoot) => side < 0 ? R(bank - 0.5f + halfFoot, gapEdge - halfFoot) : R(gapEdge + halfFoot, bank + 0.5f - halfFoot);
            int kind = dynamic ? rng.Next(18) : rng.Next(8);
            if (dynamic)
            {
                switch (kind)
                {
                    case 0:   // sweeper arm from a bank post (wipes up to its reach into the river)
                    {
                        float reach = R(4f, 5.5f);
                        if (reach + 1.4f > room) return false;
                        float edge = bank - side * (reach + 0.5f);
                        p = new ObstaclePlacement { kind = ObstacleKind.Sweeper, side = side, size = reach, x = bank, phase = R(0f, 10f),
                                                    minX = Mathf.Min(bank, edge) - 0.4f, maxX = Mathf.Max(bank, edge) + 0.4f };
                        return true;
                    }
                    case 1:   // pendulum log swinging along the flow
                    {
                        float len = R(3.5f, 5f);
                        if (len + 0.6f > room) return false;
                        float x = Pos(len * 0.5f + 0.3f);
                        p = new ObstaclePlacement { kind = ObstacleKind.Pendulum, x = x, size = len, phase = R(0f, 10f), minX = x - len * 0.5f - 0.3f, maxX = x + len * 0.5f + 0.3f };
                        return true;
                    }
                    case 2:   // spiked wall sliding sideways
                    {
                        const float m = 1.8f;     // half depth of the spiked wall across the flow
                        float travel = R(2f, 4f);
                        if (travel + 2f * m > room) return false;
                        float start = side < 0 ? R(bank - 0.5f + m, gapEdge - travel - m) : R(gapEdge + m, bank + 0.5f - travel - m);
                        p = new ObstaclePlacement { kind = ObstacleKind.Barrier, x = start, size = travel, phase = R(0f, 10f),
                                                    minX = start - m, maxX = start + travel + m };
                        return true;
                    }
                    case 7:   // spiked roller spinning along the bank, half in the water
                    {
                        const float inner = 1.75f;
                        if (inner + 0.6f > room) return false;
                        p = new ObstaclePlacement { kind = ObstacleKind.BankRoller, side = side, x = bank, phase = R(0f, 10f),
                                                    minX = Mathf.Min(bank - side * inner, bank + side * 1f), maxX = Mathf.Max(bank - side * inner, bank + side * 1f) };
                        return true;
                    }
                    case 8:   // spinning spiked star in the stream
                    {
                        float arm = R(2.1f, 2.9f);
                        float r = arm + 0.55f;
                        if (2f * r + 0.4f > room) return false;
                        float x = Pos(r + 0.2f);
                        p = new ObstaclePlacement { kind = ObstacleKind.StarSpinner, x = x, size = arm, phase = R(0f, 10f), side = Chance(0.5f) ? 1 : -1,
                                                    minX = x - r - 0.2f, maxX = x + r + 0.2f };
                        return true;
                    }
                    case 9:   // inflating puffer mine
                    {
                        float r = R(1.4f, 1.9f);
                        if (2f * r + 0.4f > room) return false;
                        float x = Pos(r + 0.2f);
                        p = new ObstaclePlacement { kind = ObstacleKind.Puffer, x = x, size = r, phase = R(0f, 10f), minX = x - r - 0.2f, maxX = x + r + 0.2f };
                        return true;
                    }
                    case 10:  // kraken tentacle rising by the bank and leaning into the river
                    {
                        const float baseIn = 0.7f, reach = 4.2f;
                        float edge = baseIn + reach + 0.5f;
                        if (edge + 0.3f > room) return false;
                        p = new ObstaclePlacement { kind = ObstacleKind.Tentacle, side = side, x = bank - side * baseIn, size = reach, phase = R(0f, 10f),
                                                    minX = Mathf.Min(bank - side * edge, bank + side * 1f), maxX = Mathf.Max(bank - side * edge, bank + side * 1f) };
                        return true;
                    }
                    case 11:  // windmill on the bank, blades dipping into the river
                    {
                        const float inner = 3.6f;
                        if (inner + 0.3f > room) return false;
                        p = new ObstaclePlacement { kind = ObstacleKind.Windmill, side = side, x = bank, phase = R(0f, 10f),
                                                    minX = Mathf.Min(bank - side * inner, bank + side * 1f), maxX = Mathf.Max(bank - side * inner, bank + side * 1f) };
                        return true;
                    }
                    case 12:  // flamethrower on the bank: the jet reaches up to 1.06 x its length past the nozzle
                    {
                        float len = R(3.0f, 5.2f);
                        float edge = FireStart + 1.06f * len + 0.35f;
                        if (edge + 0.3f > room) return false;
                        p = new ObstaclePlacement { kind = ObstacleKind.FireJet, side = side, x = bank, size = len, phase = R(0f, 10f),
                                                    minX = Mathf.Min(bank - side * edge, bank + side * 1.6f), maxX = Mathf.Max(bank - side * edge, bank + side * 1.6f) };
                        return true;
                    }
                    case 13:  // whirlpool: the whole swirl is the footprint (the pull starts when the raft's rim enters it)
                    {
                        float r = R(2.6f, 3.7f);
                        if (2f * r + 0.4f > room) return false;
                        float x = Pos(r + 0.2f);
                        p = new ObstaclePlacement { kind = ObstacleKind.Whirlpool, x = x, size = r, phase = R(0f, 10f), side = Chance(0.5f) ? 1 : -1,
                                                    minX = x - r - 0.2f, maxX = x + r + 0.2f };
                        return true;
                    }
                    case 14:  // bank cannon: the ball lands inside the target circle; footprint = target circle out to the bank
                    {
                        float r = R(1.35f, 1.8f);
                        if (2f * r + 1.2f > room) return false;
                        float tx = Pos(r + 0.3f);
                        float inner = tx - side * (r + 0.3f);
                        p = new ObstaclePlacement { kind = ObstacleKind.Mortar, side = side, x = bank, extra = tx, size = r, phase = R(0f, 10f),
                                                    minX = Mathf.Min(inner, bank + side * 2f), maxX = Mathf.Max(inner, bank + side * 2f) };
                        return true;
                    }
                    case 15:  // spiked log swinging under a gantry (along the flow)
                    case 16:
                    {
                        float len = R(3.2f, 5.2f);
                        float half = len * 0.5f + 0.7f;     // spikes and end caps stick out past the log's length
                        if (2f * half + 0.2f > room) return false;
                        float x = Pos(half);
                        p = new ObstaclePlacement { kind = ObstacleKind.SpikeSwing, x = x, size = len, phase = R(0f, 10f), minX = x - half, maxX = x + half };
                        return true;
                    }
                    case 17:  // spiked roller rolling on the water and sliding sideways
                    {
                        float len = R(2.8f, 4.2f), travel = R(1.5f, 3.5f);
                        float half = len * 0.5f + 0.5f;
                        if (travel + 2f * half > room) return false;
                        float start = side < 0 ? R(bank - 0.5f + half, gapEdge - travel - half) : R(gapEdge + half, bank + 0.5f - travel - half);
                        p = new ObstaclePlacement { kind = ObstacleKind.SpikeLogRoll, x = start, size = len, extra = travel, phase = R(0f, 10f),
                                                    minX = start - half, maxX = start + travel + half };
                        return true;
                    }
                    case 4:   // stamping press
                    case 5:   // pop-up spike pad
                    case 6:   // floating buzzsaw
                    {
                        float r = kind == 4 ? R(1.5f, 1.9f) : kind == 5 ? R(1.5f, 2f) : R(1.4f, 1.8f);
                        if (2f * r + 0.4f > room) return false;
                        float x = Pos(r + 0.2f);
                        var k = kind == 4 ? ObstacleKind.Press : kind == 5 ? ObstacleKind.SpikeTrap : ObstacleKind.Buzzsaw;
                        p = new ObstaclePlacement { kind = k, x = x, size = r, phase = R(0f, 360f), minX = x - r - 0.2f, maxX = x + r + 0.2f };
                        return true;
                    }
                    default:  // spinning log on a buoy
                    {
                        float len = R(3.5f, 5f);
                        if (len + 0.6f > room) return false;
                        float x = Pos(len * 0.5f + 0.3f);
                        p = new ObstaclePlacement { kind = ObstacleKind.Spinner, x = x, size = len, phase = R(0f, 10f), side = Chance(0.5f) ? 1 : -1,
                                                    minX = x - len * 0.5f - 0.3f, maxX = x + len * 0.5f + 0.3f };
                        return true;
                    }
                }
            }
            switch (kind)
            {
                case 0:   // boulder
                {
                    float r = R(1.1f, 2.2f);
                    if (2f * r + 0.4f > room) return false;
                    float x = Pos(r + 0.2f);
                    p = new ObstaclePlacement { kind = ObstacleKind.Boulder, x = x, size = r, variant = rng.Next(3), phase = R(0f, 360f), minX = x - r - 0.2f, maxX = x + r + 0.2f };
                    return true;
                }
                case 1:   // two rocks side by side
                {
                    float r = R(0.9f, 1.4f);
                    float foot = 3.4f * r + 0.4f;
                    if (foot > room) return false;
                    float x = Pos(foot * 0.5f);
                    p = new ObstaclePlacement { kind = ObstacleKind.RockPair, x = x, size = r, variant = rng.Next(3), phase = R(0f, 360f), minX = x - foot * 0.5f, maxX = x + foot * 0.5f };
                    return true;
                }
                case 2:   // floating barrels
                {
                    if (3.0f > room) return false;
                    float x = Pos(1.5f);
                    p = new ObstaclePlacement { kind = ObstacleKind.Barrels, x = x, phase = R(0f, 360f), minX = x - 1.5f, maxX = x + 1.5f };
                    return true;
                }
                case 3:   // striped barricade
                {
                    if (3.4f > room) return false;
                    float x = Pos(1.7f);
                    p = new ObstaclePlacement { kind = ObstacleKind.Crates, x = x, phase = R(-25f, 25f), minX = x - 1.7f, maxX = x + 1.7f };
                    return true;
                }
                case 4:   // drifting log, angled
                {
                    float len = R(3.5f, 6f), yaw = R(-35f, 35f);
                    float half = len * 0.5f * Mathf.Abs(Mathf.Cos(yaw * Mathf.Deg2Rad)) + 0.45f;
                    if (2f * half > room) return false;
                    float x = Pos(half);
                    p = new ObstaclePlacement { kind = ObstacleKind.Log, x = x, size = len, extra = yaw, minX = x - half, maxX = x + half };
                    return true;
                }
                case 6:   // a cluster of floating spiked sea mines, staggered along the flow
                case 7:
                {
                    const float step = 1.75f, r = 0.9f;
                    int n = 2 + rng.Next(3);
                    while (n > 1 && (n - 1) * step + 2f * r > room) n--;
                    float foot = (n - 1) * step + 2f * r;
                    if (foot > room) return false;
                    float x = Pos(foot * 0.5f);
                    p = new ObstaclePlacement { kind = ObstacleKind.MineField, x = x, size = n, extra = step, phase = R(0f, 10f), variant = rng.Next(1000),
                                                minX = x - foot * 0.5f, maxX = x + foot * 0.5f };
                    return true;
                }
                default:  // buoy line from the bank to near the gap
                {
                    // the fence must reach well past the bank rollers, or it would block nothing
                    float reach = edgeGuard + 1.2f;
                    float inner = side < 0 ? R(Mathf.Max(bank + reach, gapEdge - 3f), gapEdge - 0.5f) : R(gapEdge + 0.5f, Mathf.Min(bank - reach, gapEdge + 3f));
                    if (Mathf.Abs(inner - bank) < Mathf.Max(1.5f, reach) || (side < 0 ? inner > gapEdge - 0.5f : inner < gapEdge + 0.5f)) return false;
                    p = new ObstaclePlacement { kind = ObstacleKind.BuoyLine, side = side, x = (bank + inner) * 0.5f, extra = inner,
                                                minX = Mathf.Min(bank, inner) - 0.5f, maxX = Mathf.Max(bank, inner) + 0.5f };
                    return true;
                }
            }
        }

        void Add(ObstacleRow row, ObstaclePlacement p, bool skipCheck = false)
        {
            p.distance = row.distance;
            if (!skipCheck && p.maxX > row.GapMin && p.minX < row.GapMax)
                throw new InvalidOperationException($"[ObstaclePlanner] {p.kind} [{p.minX:F2},{p.maxX:F2}] would block the safe gap [{row.GapMin:F2},{row.GapMax:F2}] at {row.distance:F0} m");
            row.items.Add(p);
        }

        /// <summary>Checks the row's guarantee (gap inside the water, wide enough, no footprint inside it).</summary>
        public void Validate(ObstacleRow row)
        {
            if (row.gap < raftRadius * 2f + MinGapMargin) throw new InvalidOperationException("[ObstaclePlanner] gap too narrow");
            if (row.GapMin < -halfWidth + edgeGuard || row.GapMax > halfWidth - edgeGuard) throw new InvalidOperationException("[ObstaclePlanner] gap touches the bank rollers");
            if (row.GapMin < -halfWidth || row.GapMax > halfWidth) throw new InvalidOperationException("[ObstaclePlanner] gap outside the water");
            foreach (var p in row.items)
                if (p.kind != ObstacleKind.Gate && p.kind != ObstacleKind.WaitGate && p.maxX > row.GapMin && p.minX < row.GapMax)
                    throw new InvalidOperationException($"[ObstaclePlanner] {p.kind} blocks the gap at {row.distance:F0} m");
        }
    }
}
