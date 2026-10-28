using UnityEditor;

namespace Voxelforge.Editor
{
    /// <summary>
    /// Import settings for the Blender foliage (Roadmap 2.6) and props (3.5): the decoration renderer draws the raw
    /// meshes with their vertex colours and custom normals, so Unity must keep those exactly and add nothing else.
    /// </summary>
    public sealed class FoliageImportSettings : AssetPostprocessor
    {
        static readonly string[] ModelDirs = { "Assets/_Voxelforge/Art/Models/Foliage/", "Assets/_Voxelforge/Art/Models/Props/" };
        static readonly string[] TextureDirs = { "Assets/_Voxelforge/Art/Textures/Foliage/", "Assets/_Voxelforge/Art/Textures/Props/" };

        static bool IsIn(string path, string[] dirs)
        {
            foreach (string dir in dirs)
            {
                if (path.StartsWith(dir))
                    return true;
            }
            return false;
        }

        void OnPreprocessModel()
        {
            if (!IsIn(assetPath, ModelDirs) || !assetPath.EndsWith(".fbx"))
                return;

            var importer = (ModelImporter)assetImporter;
            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.bakeAxisConversion = true;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.None;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.isReadable = false;
            importer.importBlendShapes = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importVisibility = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importAnimation = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
        }

        void OnPreprocessTexture()
        {
            if (!IsIn(assetPath, TextureDirs))
                return;

            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true; // Bleeds leaf colour into the cut-out, so edges don't go dark.
            importer.mipmapEnabled = true;
            importer.mipMapsPreserveCoverage = true; // Far-away leaves keep their fullness instead of thinning out.
            importer.alphaTestReferenceValue = 0.5f;
            importer.wrapMode = UnityEngine.TextureWrapMode.Clamp;
            importer.anisoLevel = 4;
            importer.maxTextureSize = 2048;
        }
    }
}
