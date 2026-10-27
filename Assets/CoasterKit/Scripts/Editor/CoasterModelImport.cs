// CoasterModelImport.cs — must be inside a folder called "Editor".
// Sets the right import settings the first time you import the Blender models:
//   Sunny*.fbx              -> Rig: Humanoid (so she can sit and wave), axis fixed for Blender
//   CoasterCar*.fbx / DesertSerpentCart*.fbx -> axis fixed for Blender, no animation
using UnityEditor;

public class CoasterModelImport : AssetPostprocessor
{
    void OnPreprocessModel()
    {
        if (!assetImporter.importSettingsMissing) return;          // only on the first import - your own changes are kept
        string file = System.IO.Path.GetFileNameWithoutExtension(assetPath);
        bool sunny = file.StartsWith("Sunny");
        bool cart = file.StartsWith("DesertSerpentCart") || file.StartsWith("CoasterCar");
        if (!sunny && !cart) return;
        var mi = (ModelImporter)assetImporter;
        mi.bakeAxisConversion = true;
        mi.importCameras = false;
        mi.importLights = false;
        mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
        if (sunny)
        {
            mi.animationType = ModelImporterAnimationType.Human;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.importAnimation = false;
        }
        else
        {
            mi.animationType = ModelImporterAnimationType.None;
            mi.importAnimation = false;
        }
    }
}
