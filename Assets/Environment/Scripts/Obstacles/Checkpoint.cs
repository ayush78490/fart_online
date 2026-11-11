using UnityEngine;
using UnityEngine.Events;

namespace FartOnline.Obstacles
{
    /// <summary>
    /// Trigger volume across the river. When the raft enters, it becomes the raft's respawn point, the flag
    /// raises and onActivated fires (hook for audio/UI). Checkpoints are ordered by <see cref="index"/>.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class Checkpoint : MonoBehaviour
    {
        public int index;
        [Tooltip("Where the raft respawns (defaults to this transform)")] public Transform respawnPoint;
        [Tooltip("Visual flag raised on activation (optional)")] public Transform flag;
        public float flagRaise = 1.6f;
        public UnityEvent onActivated;
        [Tooltip("Optional one-shot; the scene works silently without it")] public AudioSource activationAudio;

        public bool Activated { get; private set; }
        Vector3 flagDown;

        void Awake()
        {
            GetComponent<BoxCollider>().isTrigger = true;
            if (flag != null) flagDown = flag.localPosition;
        }

        void OnTriggerEnter(Collider other)
        {
            var raft = CourseRaftLookup.From(other);
            if (raft == null || !raft.IsAuthoritative) return;
            raft.ReachCheckpoint(this, RespawnPoint);
            if (Activated) return;                    // never re-triggers while the raft stays inside (or comes back)
            SetActivated(true);
            CourseProgress.Report(this);
            AnyActivated?.Invoke(this);
        }

        public Transform RespawnPoint => respawnPoint != null ? respawnPoint : transform;

        /// <summary>Raised on the authority when a checkpoint is reached for the first time.</summary>
        public static event System.Action<Checkpoint> AnyActivated;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => AnyActivated = null;

        /// <summary>Visual + audio state; also called on network clients when the authority reports activation.</summary>
        public void SetActivated(bool on)
        {
            if (Activated == on) return;
            Activated = on;
            if (flag != null) flag.localPosition = flagDown + (on ? Vector3.up * flagRaise : Vector3.zero);
            if (on)
            {
                if (activationAudio != null) activationAudio.Play();
                onActivated?.Invoke();
            }
        }

        void OnDrawGizmos()
        {
            var b = GetComponent<BoxCollider>();
            Gizmos.color = Activated ? new Color(0.2f, 1f, 0.3f, 0.35f) : new Color(1f, 0.9f, 0.1f, 0.25f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawCube(b.center, b.size);
            Gizmos.matrix = Matrix4x4.identity;
            if (respawnPoint != null) { Gizmos.color = Color.green; Gizmos.DrawWireSphere(respawnPoint.position, 0.5f); }
        }
    }

    /// <summary>Static course progress (checkpoints reached, finish time). Read by UI / game modes.</summary>
    public static class CourseProgress
    {
        public static int LastCheckpoint { get; private set; } = -1;
        public static float StartTime { get; private set; }
        public static float FinishTime { get; private set; } = -1f;
        public static event System.Action<int> CheckpointReached;
        public static event System.Action<float> Finished;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { LastCheckpoint = -1; StartTime = 0f; FinishTime = -1f; CheckpointReached = null; Finished = null; }

        public static void Report(Checkpoint c)
        {
            if (c.index == 0) StartTime = Time.time;
            if (c.index > LastCheckpoint) { LastCheckpoint = c.index; CheckpointReached?.Invoke(c.index); }
        }

        public static void ResetRun() { LastCheckpoint = -1; StartTime = Time.time; FinishTime = -1f; }

        public static void ReportFinish()
        {
            if (FinishTime >= 0f) return;
            FinishTime = Time.time - StartTime;
            Finished?.Invoke(FinishTime);
        }
    }
}
