using System.Collections.Generic;
using UnityEngine;
using FartOnline.Environment;

namespace FartOnline.EnvironmentEditor
{
    /// <summary>
    /// The default (original) route of the tropical raft course: six connected sections, about 400 m long.
    /// Position.y is the water surface. Widths assume the 4.2 m test raft (5.9 m diagonal).
    /// The generator writes these into the scene's RiverPath only on "Reset Layout"; afterwards the RiverPath in
    /// the scene is the source of truth and can be edited by hand.
    /// </summary>
    public static class CourseLayout
    {
        public static List<RiverPoint> Default()
        {
            var p = new List<RiverPoint>();
            void Add(float x, float y, float z, float w, float flat, float wallH, float run, float flow, string sec, bool water = true)
                => p.Add(new RiverPoint(new Vector3(x, y, z), w, flat, wallH, run, flow, sec, water));

            // A - jungle starting lagoon: broad and calm, wide sandy shore, cliffs framing the back
            Add(0, 0, -30, 8, 5, 20, 6, 0f, "A");                    // closed back cove behind the start gate
            Add(0, 0, -12, 26, 10, 16, 12, 0.4f, "A");
            Add(0, 0, 8, 30, 9, 18, 12, 1.0f, "A");
            Add(2, 0, 30, 26, 7, 20, 10, 1.4f, "A");
            Add(3, 0, 48, 16, 3, 22, 6, 2.0f, "A");
            // B - narrow canyon passage: tall walls right at the water, slight overhangs
            Add(4, 0, 62, 10, 0.6f, 26, 2.5f, 3.0f, "B");
            Add(7, 0, 80, 9.5f, 0.5f, 28, -1.5f, 3.2f, "B");
            Add(4, 0, 98, 9.5f, 0.5f, 28, 1.5f, 3.2f, "B");
            Add(0, 0, 114, 11, 0.8f, 26, -1.0f, 3.0f, "B");
            // C - waterfall basin: wide turbulent pool under tall cliffs
            Add(-5, 0, 134, 24, 3, 30, 4, 2.2f, "C");
            Add(-8, 0, 154, 28, 4, 32, 4, 1.8f, "C");
            Add(-4, 0, 174, 16, 2, 28, 4, 2.4f, "C");
            // D - rapids and S-curve: tight turns between raised rock banks
            Add(6, 0, 190, 10.5f, 1.5f, 9, 3, 3.6f, "D");
            Add(19, 0, 203, 10.5f, 1.5f, 10, 3, 3.8f, "D");
            Add(22, 0, 220, 10.5f, 1.5f, 10, 3, 3.8f, "D");
            Add(10, 0, 236, 10.5f, 1.5f, 10, 3, 3.8f, "D");
            Add(4, 0, 252, 11, 1.5f, 12, 3, 3.6f, "D");
            // E - water ramp: a chute drops 4 m to a rock kicker, then a dry gap over the falls, landing basin below
            Add(4, 0, 266, 11, 1.5f, 16, 3, 5.0f, "E");
            Add(4, -2, 279, 10, 1.2f, 18, 3, 7.5f, "E");
            Add(4, -4, 290, 10, 1.2f, 20, 3, 8.5f, "E");
            // dry gap (falls). A river segment is wet only if BOTH ends are wet, so wet points sit close on either side:
            // dry span ≈ 290 → 297 (about 7 m), clearable at speed with farting, too far for a lazy drift
            Add(4, -7, 293.5f, 12, 1.2f, 24, 3, 0f, "E", false);
            Add(4, -7, 297, 18, 2, 25, 4, 2.5f, "E");                // landing: water resumes here
            Add(4, -7, 303, 22, 3, 26, 4, 2.5f, "E");                // landing basin
            Add(3, -7, 320, 24, 4, 28, 4, 2.2f, "E");
            // F - final obstacle arena: big canyon chamber, finish gate, landmark falls behind it
            Add(0, -7, 340, 22, 5, 32, 4, 2.0f, "F");
            Add(-2, -7, 362, 24, 6, 36, 4, 1.8f, "F");
            Add(0, -7, 384, 22, 6, 38, 4, 1.4f, "F");
            Add(0, -7, 404, 26, 8, 40, 6, 0.6f, "F");
            Add(0, -7, 426, 10, 3, 46, 3, 0f, "F");                   // closing cove under the landmark falls
            return p;
        }
    }
}
