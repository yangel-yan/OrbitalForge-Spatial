using System;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEngine;

namespace SpaceBall.Editor
{
    /// <summary>Keep Android launcher branding consistent across all icon families.</summary>
    public static class OrbitalBranding
    {
        public const string ProductName = "星轨重构";
        public const string IconPath = "Assets/SpaceBall/Art/Brand/AI/OrbitalCourierIcon-Candidate.png";

        public static void Apply()
        {
            var importer = AssetImporter.GetAtPath(IconPath) as TextureImporter;
            if (importer != null && (importer.textureCompression != TextureImporterCompression.Uncompressed ||
                importer.mipmapEnabled || importer.maxTextureSize < 1024 || importer.GetPlatformTextureSettings("Android").overridden))
            {
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.mipmapEnabled = false;
                importer.maxTextureSize = 1024;
                importer.ClearPlatformTextureSettings("Android");
                importer.SaveAndReimport();
            }
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            if (icon == null || icon.width < 512 || icon.height < 512)
                throw new InvalidOperationException("The Orbital Forge launcher icon is missing or undersized: " + IconPath);

            // Keep both game-specific keys when the Editor's product identity changes.
            // Android storage remains under the unchanged application identifier.
            const string key = "SpaceBall.Journey.v1";
            bool mainExists = PlayerPrefs.HasKey(key), backupExists = PlayerPrefs.HasKey(key + ".backup");
            string main = PlayerPrefs.GetString(key, ""), backup = PlayerPrefs.GetString(key + ".backup", "");
            PlayerSettings.productName = ProductName;
            if (mainExists) PlayerPrefs.SetString(key, main);
            if (backupExists) PlayerPrefs.SetString(key + ".backup", backup);
            if (mainExists || backupExists) PlayerPrefs.Save();

#pragma warning disable CS0618
            PlayerSettings.SetIconsForTargetGroup(BuildTargetGroup.Unknown, new[] { icon }, IconKind.Any);
#pragma warning restore CS0618
            foreach (var kind in new[] { AndroidPlatformIconKind.Legacy, AndroidPlatformIconKind.Round, AndroidPlatformIconKind.Adaptive })
            {
                var slots = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, kind);
                foreach (var slot in slots)
                {
                    // Opaque artwork is intentional: the navy background gives the
                    // same restrained appearance on circle and rounded-square launchers.
                    var layers = new Texture2D[slot.minLayerCount];
                    for (int layer = 0; layer < layers.Length; layer++) layers[layer] = icon;
                    slot.SetTextures(layers);
                }
                PlayerSettings.SetPlatformIcons(NamedBuildTarget.Android, kind, slots);
            }
            AssetDatabase.SaveAssets();
        }
    }
}
