using UnityEngine;

namespace FartOnline.Obstacles
{
    /// <summary>Trigger that sends the raft back to its last checkpoint (out-of-bounds, deadly falls).</summary>
    [RequireComponent(typeof(BoxCollider))]
    public class HazardZone : MonoBehaviour
    {
        void Awake() => GetComponent<BoxCollider>().isTrigger = true;

        void OnTriggerEnter(Collider other)
        {
            var raft = CourseRaftLookup.From(other);
            if (raft != null && raft.IsAuthoritative) raft.Wipeout("hazard:" + name);
        }

        void OnDrawGizmos()
        {
            var b = GetComponent<BoxCollider>();
            Gizmos.color = new Color(1f, 0.1f, 0.1f, 0.25f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawCube(b.center, b.size);
        }
    }
}
