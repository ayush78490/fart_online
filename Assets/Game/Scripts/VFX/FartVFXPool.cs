using System.Collections.Generic;
using UnityEngine;

namespace FartOnline.Game
{
    /// <summary>
    /// Pre-instantiates a few of each burst variant and recycles them, so firing never allocates or destroys
    /// GameObjects. When every instance of a variant is busy, the oldest one is restarted.
    /// </summary>
    public class FartVFXPool : MonoBehaviour
    {
        [System.Serializable]
        public struct Variant { public ThrustKind kind; public bool lateral; public FartBurstVFX prefab; public int count; }

        public Variant[] variants;
        readonly Dictionary<int, List<FartBurstVFX>> pools = new Dictionary<int, List<FartBurstVFX>>();

        public static FartVFXPool Instance { get; private set; }

        static int Key(ThrustKind k, bool lateral) => (int)k * 2 + (lateral ? 1 : 0);

        void Awake()
        {
            Instance = this;
            foreach (var v in variants)
            {
                if (v.prefab == null) continue;
                var list = new List<FartBurstVFX>();
                for (int i = 0; i < Mathf.Max(1, v.count); i++)
                {
                    var fx = Instantiate(v.prefab, transform);
                    fx.name = v.prefab.name + "_" + i;
                    fx.gameObject.SetActive(false);
                    list.Add(fx);
                }
                pools[Key(v.kind, v.lateral)] = list;
            }
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        public FartBurstVFX Spawn(ThrustKind kind, bool lateral, Vector3 pos, Vector3 gasDir, float strength, Color accent, Vector3 raftVelocity)
        {
            if (!pools.TryGetValue(Key(kind, lateral), out var list) && !pools.TryGetValue(Key(kind, false), out list)) return null;
            FartBurstVFX pick = null;
            float oldest = float.MaxValue;
            foreach (var fx in list)
            {
                if (!fx.gameObject.activeSelf || fx.FreeAt <= Time.time) { pick = fx; break; }
                if (fx.FreeAt < oldest) { oldest = fx.FreeAt; pick = fx; }
            }
            pick.gameObject.SetActive(true);
            pick.Play(pos, gasDir, strength, accent, raftVelocity);
            return pick;
        }

        void Update()
        {
            foreach (var list in pools.Values)
                foreach (var fx in list)
                    if (fx.gameObject.activeSelf && fx.FreeAt <= Time.time) fx.gameObject.SetActive(false);
        }
    }
}
