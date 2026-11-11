using UnityEngine;

namespace FartOnline.Obstacles
{
    /// <summary>
    /// What course triggers (checkpoints, finish, hazards) need from a raft. Implemented by the environment's
    /// TestRaft and by the game's RaftRecoveryManager, so the course works with either.
    /// Triggers act only when <see cref="IsAuthoritative"/> (on network clients the raft is replicated, not simulated).
    /// </summary>
    public interface ICourseRaft
    {
        bool IsAuthoritative { get; }
        void ReachCheckpoint(Checkpoint checkpoint, Transform respawnPoint);
        void Wipeout(string reason);
    }

    public static class CourseRaftLookup
    {
        public static ICourseRaft From(Collider other)
        {
            var rb = other.attachedRigidbody;
            return rb != null ? rb.GetComponent<ICourseRaft>() : null;
        }
    }
}
