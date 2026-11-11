using UnityEngine;
using UnityEngine.Events;

namespace FartOnline.Obstacles
{
    /// <summary>Finish line trigger: fires onFinished once, plays the celebration particles and reports the time.</summary>
    [RequireComponent(typeof(BoxCollider))]
    public class FinishGate : MonoBehaviour
    {
        public ParticleSystem celebration;
        public UnityEvent onFinished;
        [Tooltip("Optional one-shot; the scene works silently without it")] public AudioSource finishAudio;
        public bool Finished { get; private set; }

        void Awake() => GetComponent<BoxCollider>().isTrigger = true;

        void OnTriggerEnter(Collider other)
        {
            if (Finished) return;
            var raft = CourseRaftLookup.From(other);
            if (raft == null || !raft.IsAuthoritative) return;
            Celebrate();
            CourseProgress.ReportFinish();
            AnyFinished?.Invoke(this);
            Debug.Log($"[TropicalCourse] finish! time {CourseProgress.FinishTime:F1}s");
        }

        /// <summary>Raised on the authority when the raft crosses the finish line.</summary>
        public static event System.Action<FinishGate> AnyFinished;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => AnyFinished = null;

        /// <summary>Visual + audio celebration; also called on network clients.</summary>
        public void Celebrate()
        {
            if (Finished) return;
            Finished = true;
            if (celebration != null) celebration.Play();
            if (finishAudio != null) finishAudio.Play();
            onFinished?.Invoke();
        }

        public void ResetGate() => Finished = false;

        void OnDrawGizmos()
        {
            var b = GetComponent<BoxCollider>();
            Gizmos.color = new Color(0.1f, 1f, 0.4f, 0.3f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawCube(b.center, b.size);
        }
    }
}
