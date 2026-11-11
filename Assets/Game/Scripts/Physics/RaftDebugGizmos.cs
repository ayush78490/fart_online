using UnityEngine;

namespace FartOnline.Game
{
    /// <summary>Scene-view debugging for the shared raft: centre of mass, seats, thrust origins and live thrust vectors.</summary>
    public class RaftDebugGizmos : MonoBehaviour
    {
        public SharedRaftController raft;
        public float forceScale = 1f / 600f;

        void OnDrawGizmos()
        {
            if (raft == null || raft.seats == null) return;
            var rb = raft.GetComponent<Rigidbody>();
            Vector3 com = Application.isPlaying ? rb.worldCenterOfMass : raft.transform.TransformPoint(raft.tuning != null ? raft.tuning.centerOfMass : Vector3.zero);
            Gizmos.color = Color.white;
            Gizmos.DrawWireSphere(com, 0.15f);
            foreach (var s in raft.seats.Seats)
            {
                if (s == null) continue;
                var t = s.Thrust != null ? s.Thrust : s.GetComponent<FartThrustController>();
                if (t == null) continue;
                Vector3 o = t.OriginWorld;
                Gizmos.color = s.IsOccupied ? s.PlayerColor : new Color(1f, 1f, 1f, 0.3f);
                Gizmos.DrawWireCube(s.transform.position, new Vector3(0.6f, 0.05f, 0.6f));
                if (raft.tuning != null) { Gizmos.color = new Color(1f, 1f, 1f, 0.25f); DrawCircle(raft.transform, raft.tuning.seatRadius, 0.3f); Gizmos.color = s.IsOccupied ? s.PlayerColor : new Color(1f, 1f, 1f, 0.3f); }
                Gizmos.DrawLine(com, o);
                if (!Application.isPlaying || !t.State.sustaining) continue;
                Vector3 f = raft.transform.TransformDirection(new Vector3(t.State.direction.x, 0f, t.State.direction.y)) * (raft.tuning != null ? raft.tuning.thrust : 1500f);
                Gizmos.color = new Color(0.6f, 1f, 0.2f);
                Gizmos.DrawLine(o, o + f * forceScale);           // thrust on the raft
                Gizmos.color = new Color(0.4f, 0.6f, 0.1f);
                Gizmos.DrawLine(o, o - f.normalized * 0.8f);       // gas leaves the other way
            }
        }

        static void DrawCircle(Transform t, float r, float h)
        {
            Vector3 prev = t.TransformPoint(new Vector3(0f, h, r));
            for (int i = 1; i <= 32; i++)
            {
                float a = i / 32f * Mathf.PI * 2f;
                Vector3 p = t.TransformPoint(new Vector3(Mathf.Sin(a) * r, h, Mathf.Cos(a) * r));
                Gizmos.DrawLine(prev, p);
                prev = p;
            }
        }
    }
}
