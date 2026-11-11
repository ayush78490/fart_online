using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FartOnline.Pig.EditorTools
{
    /// <summary>Import settings for PigCharacter_Game.fbx and its textures (applied on every (re)import).</summary>
    public class PigCharacterImporter : AssetPostprocessor
    {
        public const string Root = "Assets/Characters/Pig";
        public const string FbxPath = Root + "/Models/PigCharacter_Game.fbx";
        public static readonly string[] LoopingClips = { "Idle_Breathing", "Walk", "Run" };

        bool IsPigModel => assetPath.Replace('\\', '/') == FbxPath;

        void OnPreprocessModel()
        {
            if (!IsPigModel) return;
            var mi = (ModelImporter)assetImporter;
            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.bakeAxisConversion = true;                         // Blender Z-up -> Unity Y-up baked into the data
            mi.importBlendShapes = true;
            mi.importBlendShapeNormals = ModelImporterNormals.Calculate;
            mi.importNormals = ModelImporterNormals.Import;
            mi.importTangents = ModelImporterTangents.CalculateMikk;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importVisibility = false;
            mi.isReadable = false;
            mi.meshCompression = ModelImporterMeshCompression.Off;
            mi.skinWeights = ModelImporterSkinWeights.Standard;    // 4 bones per vertex (weights were limited to 4)
            mi.animationType = ModelImporterAnimationType.Generic;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.optimizeGameObjects = false;                       // face controller / springs need the bone transforms
            mi.importAnimation = true;
            mi.resampleCurves = true;
            mi.animationCompression = ModelImporterAnimationCompression.KeyframeReduction;
            mi.animationRotationError = 0.1f;
            mi.animationPositionError = 0.1f;
            mi.animationScaleError = 0.1f;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            mi.materialLocation = ModelImporterMaterialLocation.InPrefab;
            foreach (var n in PigCharacterSetup.FbxMaterialNames)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>($"{Root}/Materials/{n}.mat");
                if (m != null) mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), n), m);
            }
        }

        void OnPreprocessAnimation()
        {
            if (!IsPigModel) return;
            var mi = (ModelImporter)assetImporter;
            var clips = mi.defaultClipAnimations;
            foreach (var c in clips)
            {
                string n = c.name;
                int bar = n.LastIndexOf('|');
                if (bar >= 0) n = n.Substring(bar + 1);
                c.name = n;
                c.takeName = c.takeName;
                c.loopTime = LoopingClips.Contains(n);
                c.loopPose = false;
                c.lockRootRotation = false;
                c.keepOriginalOrientation = true;
                c.keepOriginalPositionY = true;
                c.keepOriginalPositionXZ = true;
            }
            mi.clipAnimations = clips;
        }

        void OnPreprocessTexture()
        {
            string p = assetPath.Replace('\\', '/');
            if (!p.StartsWith(Root + "/Textures/")) return;
            var ti = (TextureImporter)assetImporter;
            ti.sRGBTexture = !p.EndsWith("T_Pig_SkinMask.png");   // detail = colour-ish multiplier, mask = data
            ti.mipmapEnabled = true;
            ti.maxTextureSize = 1024;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
        }
    }
}
