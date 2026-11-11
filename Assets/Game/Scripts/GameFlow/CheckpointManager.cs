using System.Linq;
using UnityEngine;
using FartOnline.Environment;
using FartOnline.Obstacles;

namespace FartOnline.Game
{
    /// <summary>
    /// Knows every checkpoint and the finish gate of the course, resets them for a new run and converts the raft's
    /// position into course progress (0..1 along the river). Activation itself happens in the trigger components;
    /// on network clients <see cref="ApplyReplicated"/> mirrors the server's last checkpoint.
    /// </summary>
    public class CheckpointManager : MonoBehaviour
    {
        public RiverPath river;
        public Checkpoint[] checkpoints;
        public FinishGate finish;

        public int Count => checkpoints != null ? checkpoints.Length : 0;
        float startDistance, finishDistance;

        void Awake()
        {
            if (river == null) river = FindAnyObjectByType<RiverPath>();
            if (checkpoints == null || checkpoints.Length == 0)
                checkpoints = FindObjectsByType<Checkpoint>(FindObjectsInactive.Exclude).OrderBy(c => c.index).ToArray();
            if (finish == null) finish = FindAnyObjectByType<FinishGate>();
            if (river != null)
            {
                startDistance = checkpoints.Length > 0 ? river.Closest(checkpoints[0].transform.position, out _).distance : 0f;
                finishDistance = finish != null ? river.Closest(finish.transform.position, out _).distance : river.Length;
            }
        }

        public void ResetAll()
        {
            foreach (var c in checkpoints) c.SetActivated(false);
            if (finish != null) finish.ResetGate();
        }

        public float Progress01(Vector3 raftPosition)
        {
            if (river == null || finishDistance <= startDistance) return 0f;
            float d = river.Closest(raftPosition, out _).distance;
            return Mathf.Clamp01((d - startDistance) / (finishDistance - startDistance));
        }

        public Checkpoint Get(int index) => checkpoints.FirstOrDefault(c => c.index == index);

        /// <summary>Clients: raise flags up to the server's last checkpoint; celebrate the finish.</summary>
        public void ApplyReplicated(int lastIndex, bool finished)
        {
            foreach (var c in checkpoints) c.SetActivated(c.index <= lastIndex);
            if (finished && finish != null) finish.Celebrate();
            if (!finished && finish != null) finish.ResetGate();
        }
    }
}
