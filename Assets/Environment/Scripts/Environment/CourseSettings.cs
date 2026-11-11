using UnityEngine;

namespace FartOnline.Environment
{
    /// <summary>All editable generation parameters for the tropical raft course (used by the Tropical Course Generator).</summary>
    [CreateAssetMenu(menuName = "FartOnline/Tropical Course Settings", fileName = "TropicalCourseSettings")]
    public class CourseSettings : ScriptableObject
    {
        [Header("Determinism")]
        public int seed = 1337;

        [Header("Layout scale")]
        [Range(0.7f, 1.6f)] public float riverWidthScale = 1f;
        [Range(0.5f, 2f)] public float canyonHeightScale = 1f;
        [Tooltip("Depth of the riverbed below the water surface (m)")] public float riverDepth = 2.6f;

        [Header("Raft")]
        [Tooltip("Raft footprint used for clearance checks (m)")] public Vector2 raftSize = new Vector2(4.2f, 4.2f);

        [Header("Dressing density")]
        [Range(0f, 2f)] public float vegetationDensity = 1f;
        [Range(0.5f, 2f)] public float rockScale = 1f;
        [Range(0f, 2f)] public float rockDensity = 1f;
        [Tooltip("Minimum spacing between obstacles along the river (m)")] public float obstacleSpacing = 18f;

        [Header("Water")]
        public Color waterDeep = new Color(0.02f, 0.42f, 0.52f);
        public Color waterShallow = new Color(0.20f, 0.85f, 0.80f);
        public Color waterFoam = new Color(0.95f, 1f, 1f);
        [Range(0f, 3f)] public float flowSpeedScale = 1f;

        [Header("Rock palette")]
        public Color rockRed = new Color(0.72f, 0.33f, 0.20f);
        public Color rockOrange = new Color(0.86f, 0.52f, 0.28f);
        public Color rockDark = new Color(0.38f, 0.24f, 0.18f);
        public Color rockWet = new Color(0.30f, 0.20f, 0.16f);

        [Header("Vegetation palette")]
        public Color grass = new Color(0.36f, 0.66f, 0.20f);
        public Color leaves = new Color(0.20f, 0.58f, 0.22f);
        public Color leavesAlt = new Color(0.45f, 0.72f, 0.18f);

        public float MinRaftPassage => Mathf.Sqrt(raftSize.x * raftSize.x + raftSize.y * raftSize.y);   // diagonal
    }
}
