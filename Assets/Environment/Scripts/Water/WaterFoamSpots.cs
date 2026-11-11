using System.Collections.Generic;
using UnityEngine;

namespace FartOnline.Water
{
    /// <summary>
    /// Publishes waterfall impact foam to the river shader as a global array (_FoamSpots, max 16), so impact foam
    /// blends into the water with no extra transparent geometry. Waterfalls register themselves.
    /// </summary>
    public static class WaterFoamSpots
    {
        public const int Max = 16;
        static readonly List<Waterfall> active = new List<Waterfall>();
        static readonly Vector4[] buffer = new Vector4[Max];
        static readonly int SpotsId = Shader.PropertyToID("_FoamSpots");
        static readonly int CountId = Shader.PropertyToID("_FoamSpotCount");

        public static void Register(Waterfall w) { if (!active.Contains(w)) active.Add(w); Upload(); }
        public static void Unregister(Waterfall w) { active.Remove(w); Upload(); }

        public static void Upload()
        {
            int n = 0;
            for (int i = 0; i < active.Count && n < Max; i++)
            {
                var w = active[i];
                if (w == null || !w.isActiveAndEnabled || w.foam == null) continue;
                buffer[n++] = w.FoamSpot;
            }
            for (int i = n; i < Max; i++) buffer[i] = Vector4.zero;
            Shader.SetGlobalVectorArray(SpotsId, buffer);
            Shader.SetGlobalFloat(CountId, n);
        }
    }
}
