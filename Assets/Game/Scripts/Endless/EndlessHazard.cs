using UnityEngine;

namespace FartOnline.Game.Endless
{
    /// <summary>
    /// Marks an obstacle as deadly in the endless game: when the raft touches any collider under this object the run
    /// is over (<see cref="RaftRecoveryManager.Crash"/>). Banks and canyon walls are not hazards; they only bounce.
    /// </summary>
    public class EndlessHazard : MonoBehaviour
    {
        [Tooltip("Shown on the game over screen: \"You crashed into ...\"")] public string label = "an obstacle";
    }
}
