using System;
using System.Collections.Generic;
using System.IO;
using ByteDance.PICO.SpatialAdapter;
using UnityEngine;

namespace SpaceBall.Editor
{
    /// <summary>Finishes this scene's GLB before Play-to-PICO sends it.</summary>
    public static class OrbitalPicoPreviewResources
    {
        const string SceneAssetPath = "Assets/SpaceBall/Scenes/OrbitalCourier_Prototype.unity";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void Install()
        {
            SpatialAdapterRuntime.OnSendResourceFiles -= PrepareScene;
            SpatialAdapterRuntime.OnSendResourceFiles += PrepareScene;
        }

        static void PrepareScene(List<string> filenames)
        {
            var scene = SpatialAdapterRuntime.Instance.sceneData;
            if (!string.Equals(scene.scenePath, SceneAssetPath, StringComparison.Ordinal)) return;

            // The SDK's exportedScenePath is relative to Temp, not Assets. Use
            // its actual path so an unsaved-scene .dirty.glb is handled as well.
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string tempRoot = Path.Combine(projectRoot, "Temp");
            string scenePath = Path.GetFullPath(Path.Combine(tempRoot, scene.exportedScenePath));
            string normalPath = Path.GetFullPath(Path.Combine(tempRoot, "SpatialAdapterExport", SceneAssetPath + ".glb"));
            string dirtyPath = Path.ChangeExtension(normalPath, ".dirty.glb");
            if (!string.Equals(scenePath, normalPath, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(scenePath, dirtyPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Unexpected Orbital Courier preview export path: " + scenePath);

            string reportPath = Path.Combine(projectRoot, "Builds", "SpaceBall", "Art-Native-PBR",
                "preview-resource-dedup.json");

            // AfterAssembliesLoaded subscribes before the SDK's BeforeSceneLoad
            // file sender. Restore both the embedded scene materials and the
            // standalone resources used by runtime sharedMaterial changes.
            var colors = new List<string> { OrbitalLinearColorExport.Restore(scenePath) };
            var materialExports = new List<string>();
            string materialDirectory = Path.GetFullPath(Path.Combine(tempRoot,
                "SpatialAdapterExport", "Assets", "SpaceBall", "Materials"));
            foreach (string filename in filenames)
            {
                string resource = Path.GetFullPath(Path.Combine(tempRoot, filename));
                if (string.Equals(Path.GetDirectoryName(resource), materialDirectory, StringComparison.OrdinalIgnoreCase) &&
                    resource.EndsWith(".mat.glb", StringComparison.OrdinalIgnoreCase))
                {
                    materialExports.Add(resource);
                    colors.Add(OrbitalLinearColorExport.Restore(resource));
                }
            }
            string colorFolder = Path.Combine(projectRoot, "Builds", "SpaceBall", "Visual-Hierarchy");
            Directory.CreateDirectory(colorFolder);
            File.WriteAllText(Path.Combine(colorFolder, "preview-color-restoration.json"), "[" + string.Join(",", colors) + "]");

            // This synchronous SDK callback runs inside RegisterAllResources.
            // OnSceneLoaded queues the main scene's SendFile/LoadScene only after
            // it returns. Leave failures visible and never send a partial result.
            Debug.Log(OrbitalLinearOcclusionExport.Restore(scenePath));
            foreach (string material in materialExports)
                if (OrbitalLinearOcclusionExport.IsLunarMaterialFile(material))
                    Debug.Log(OrbitalLinearOcclusionExport.Restore(material));
            Debug.Log(OrbitalGlbTextureDeduplicator.Optimize(scenePath, reportPath));
        }
    }
}
