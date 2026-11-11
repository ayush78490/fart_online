using UnityEngine;

namespace FartOnline.Game
{
    /// <summary>
    /// A 128x128 tiling value-noise texture, bound globally as <c>_FONoiseTex</c> for the FO_MOBILE shader paths
    /// (Env/StylizedWater, Env/StylizedSurface). One bilinear fetch replaces 4 to 24 hash evaluations per pixel or
    /// vertex, which is what kept the canyon and water from hitting 60 fps on an Adreno 619.
    /// The lattice is 8 cells per tile, so a shader samples it at <c>p / 8</c> to match <c>vnoise(p)</c>.
    ///   R: soft 2-octave noise (0.65 + 0.35), matching softWaves()
    ///   G: 3-octave fbm (0.55 + 0.3 + 0.15), matching fbm3()
    ///   B: single-octave value noise, matching vnoise()
    ///   A: a second soft noise (other seed)
    /// </summary>
    public static class MobileNoise
    {
        public const int Size = 128;
        const int Cells = 8;
        static Texture2D tex;
        static readonly int NoiseId = Shader.PropertyToID("_FONoiseTex");

        public static Texture2D Texture
        {
            get
            {
                if (tex == null) tex = Build();
                return tex;
            }
        }

        public static void Bind() => Shader.SetGlobalTexture(NoiseId, Texture);

        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }

        /// <summary>Tiling value noise: <paramref name="cells"/> lattice cells across the tile.</summary>
        static float Value(float u, float v, int cells, int seed)
        {
            float x = u * cells, y = v * cells;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            int xa = ((x0 % cells) + cells) % cells, xb = (xa + 1) % cells;
            int ya = ((y0 % cells) + cells) % cells, yb = (ya + 1) % cells;
            float a = Mathf.Lerp(Hash(xa, ya, seed), Hash(xb, ya, seed), fx);
            float b = Mathf.Lerp(Hash(xa, yb, seed), Hash(xb, yb, seed), fx);
            return Mathf.Lerp(a, b, fy);
        }

        static Texture2D Build()
        {
            var t = new Texture2D(Size, Size, TextureFormat.RGBA32, true, true)
            {
                name = "FO_MobileNoise",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 0,
                hideFlags = HideFlags.DontSave,
            };
            var px = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float u = (x + 0.5f) / Size, v = (y + 0.5f) / Size;
                    float soft = Value(u, v, Cells, 11) * 0.65f + Value(u, v, Cells * 2, 12) * 0.35f;
                    float fbm = Value(u, v, Cells, 21) * 0.55f + Value(u, v, Cells * 2, 22) * 0.3f + Value(u, v, Cells * 4, 23) * 0.15f;
                    float one = Value(u, v, Cells, 31);
                    float soft2 = Value(u, v, Cells, 41) * 0.65f + Value(u, v, Cells * 2, 42) * 0.35f;
                    px[y * Size + x] = new Color32((byte)(soft * 255f), (byte)(fbm * 255f), (byte)(one * 255f), (byte)(soft2 * 255f));
                }
            t.SetPixels32(px);
            t.Apply(true, true);                                 // mipmaps, then release the CPU copy
            return t;
        }
    }
}
