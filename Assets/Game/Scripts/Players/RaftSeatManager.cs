using System;
using UnityEngine;

namespace FartOnline.Game
{
    /// <summary>
    /// Owns the four seats of the round shared raft and hands them to players. Every seat sits on a circle facing
    /// the centre at an angle (degrees clockwise from the bow). Players walk along the rim with A/D; they cannot pass
    /// through each other (minimum spacing), so with a full crew everyone can only shift a little. Where a player sits
    /// decides where their fart pushes the raft: sitting at the back pushes it forward, on the right pushes it left.
    /// Starting angles depend on the crew size (all start around the stern so the first push goes downstream).
    /// </summary>
    public class RaftSeatManager : MonoBehaviour
    {
        public PlayerSeatController[] seats = new PlayerSeatController[4];
        public RaftTuning tuning;
        [Tooltip("Seat height above the raft origin (deck top)")] public float seatHeight = 0.3f;
        public float[] soloAngles = { 180f };
        public float[] duoAngles = { 150f, 210f };
        public float[] trioAngles = { 130f, 180f, 230f };
        public float[] quadAngles = { 105f, 155f, 205f, 255f };

        public static readonly Color[] PlayerColors =
        {
            new Color(0.95f, 0.36f, 0.30f), // coral
            new Color(0.25f, 0.62f, 0.98f), // sky
            new Color(1.00f, 0.80f, 0.20f), // sunflower
            new Color(0.70f, 0.42f, 0.95f), // violet
        };

        public PlayerSeatController[] Seats => seats;
        public float SeatRadius => tuning != null ? tuning.seatRadius : 1.65f;
        public event Action LayoutChanged;

        public int OccupiedCount
        {
            get { int n = 0; foreach (var s in seats) if (s != null && s.IsOccupied) n++; return n; }
        }

        void Awake()
        {
            for (int i = 0; i < seats.Length; i++)
            {
                seats[i].seatIndex = i;
                seats[i].OccupancyChanged += _ => ApplyLayout();
            }
            ApplyLayout();
        }

        public PlayerSeatController FirstFree()
        {
            foreach (var s in seats) if (!s.IsOccupied) return s;
            return null;
        }

        public PlayerSeatController SeatOf(ulong clientId)
        {
            foreach (var s in seats) if (s.IsOccupied && !s.IsAI && s.OwnerClientId == clientId) return s;
            return null;
        }

        public void VacateAll()
        {
            foreach (var s in seats) s.Vacate();
        }

        /// <summary>Puts occupied seats at the starting angles for the crew size (deterministic: same on every peer).</summary>
        public void ApplyLayout()
        {
            int n = OccupiedCount;
            float[] layout = n <= 1 ? soloAngles : n == 2 ? duoAngles : n == 3 ? trioAngles : quadAngles;
            int k = 0;
            for (int i = 0; i < seats.Length; i++)
            {
                var s = seats[i];
                float a = s.IsOccupied && k < layout.Length ? layout[k++] : 45f + 90f * i;
                s.SetAngle(a, SeatRadius, seatHeight);
                s.Moving = 0f;
            }
            LayoutChanged?.Invoke();
        }

        /// <summary>
        /// Authority: walks a seat along the rim by <paramref name="deltaDeg"/>, stopping at the minimum spacing from
        /// the nearest occupied neighbour in that direction. Returns the angle actually moved.
        /// </summary>
        public float MoveSeat(PlayerSeatController seat, float deltaDeg)
        {
            if (Mathf.Abs(deltaDeg) < 1e-5f) return 0f;
            float spacing = tuning != null ? tuning.minSeatSpacing : 42f;
            float allowed = deltaDeg;
            foreach (var o in seats)
            {
                if (o == seat || !o.IsOccupied) continue;
                // signed gap from this seat to the other, measured in the direction of travel
                float gap = RimMath.Wrap((o.Angle - seat.Angle) * Mathf.Sign(deltaDeg));
                float room = Mathf.Max(0f, gap - spacing);
                if (Mathf.Abs(allowed) > room) allowed = room * Mathf.Sign(deltaDeg);
            }
            seat.SetAngle(seat.Angle + allowed, SeatRadius, seatHeight);
            return allowed;
        }

        /// <summary>Clients: place a seat at the replicated angle.</summary>
        public void SetReplicatedAngle(PlayerSeatController seat, float angle) => seat.SetAngle(angle, SeatRadius, seatHeight);
    }
}
