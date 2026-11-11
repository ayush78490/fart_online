using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FartOnline.GameEditor
{
    /// <summary>
    /// Import rules for the UI skin and the sound library (both loaded at runtime from Resources):
    /// - <c>Assets/Game/UI/Resources/UI/*.png</c> become uncompressed-quality UI sprites with 9-slice borders taken
    ///   from <see cref="Borders"/> (left, bottom, right, top in source pixels), so panels and buttons stretch cleanly.
    /// - <c>Assets/Game/Audio/Resources/Music</c> streams as Vorbis; short SFX decompress on load.
    /// Menu <i>Tools > Fart Online > Reimport UI and Audio</i> re-applies the rules after editing this table.
    /// </summary>
    public class UiAudioImport : AssetPostprocessor
    {
        const string UiDir = "Assets/Game/UI/Resources/UI/";
        const string AudioDir = "Assets/Game/Audio/Resources/";

        static readonly Dictionary<string, Vector4> Borders = new Dictionary<string, Vector4>
        {
            { "Panel_Main", new Vector4(82, 82, 82, 82) },
            { "Pill_Brown", new Vector4(80, 80, 80, 80) },
            { "Bar_Back", new Vector4(28, 28, 28, 28) },
            { "Bar_Fill", new Vector4(28, 28, 28, 28) },
            { "Bar_Border", new Vector4(36, 36, 36, 36) },
            { "Square_DarkBrown", new Vector4(50, 50, 50, 50) },
            { "Btn_Green_Large", new Vector4(64, 44, 40, 56) },
            { "Btn_Green_Medium", new Vector4(64, 44, 40, 56) },
            { "Btn_Green_Small", new Vector4(64, 44, 40, 56) },
            { "Btn_Red_Large", new Vector4(64, 44, 40, 56) },
            { "Btn_Red_Medium", new Vector4(64, 44, 40, 56) },
            { "Btn_Red_Small", new Vector4(64, 44, 40, 56) },
            { "Btn_Wood", new Vector4(36, 48, 36, 30) },
            { "Btn_White", new Vector4(36, 48, 36, 30) },
            { "Btn_WoodRed", new Vector4(36, 48, 36, 30) },
            { "Card_Wood", new Vector4(40, 50, 40, 96) },
            { "Container1", new Vector4(56, 64, 56, 56) },
            { "Plank_Wide", new Vector4(60, 40, 60, 40) },
            { "Note_Paper", new Vector4(84, 24, 30, 24) },
            { "Slot", new Vector4(36, 40, 36, 32) },
            { "Slot_Selected", new Vector4(36, 40, 36, 32) },
        };

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(UiDir) || !assetPath.EndsWith(".png")) return;
            var ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.mipmapEnabled = false;
            ti.alphaIsTransparency = true;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.filterMode = FilterMode.Bilinear;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
            ti.spritePixelsPerUnit = 100f;
            var settings = new TextureImporterSettings();
            ti.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteGenerateFallbackPhysicsShape = false;
            ti.SetTextureSettings(settings);
            string name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            ti.spriteBorder = Borders.TryGetValue(name, out var b) ? b : Vector4.zero;
        }

        void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith(AudioDir)) return;
            var ai = (AudioImporter)assetImporter;
            bool music = assetPath.Contains("/Music/") || assetPath.Contains("/RiverLoop/");
            var s = ai.defaultSampleSettings;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = music ? 0.55f : 0.7f;
            s.loadType = music ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
            ai.defaultSampleSettings = s;
            ai.forceToMono = !music;
            ai.loadInBackground = music;
        }

        [MenuItem("Tools/Fart Online/Reimport UI and Audio")]
        public static void ReimportAll()
        {
            foreach (var dir in new[] { UiDir.TrimEnd('/'), AudioDir.TrimEnd('/') })
                foreach (var guid in AssetDatabase.FindAssets("", new[] { dir }))
                {
                    string p = AssetDatabase.GUIDToAssetPath(guid);
                    if (!AssetDatabase.IsValidFolder(p)) AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate);
                }
        }
    }
}
