using System;
using UnityEditor;
using UnityEngine;

namespace SpaceBall.Editor
{
    /// <summary>Persistent PBR resources shared by the generated chapter and its export.</summary>
    public static class OrbitalSurfaceMaterials
    {
        const string Root = "Assets/SpaceBall/";
        const string Survey = Root + "Art/LunarSurvey/";
        enum SurfaceMap { Color, Normal, Height, Occlusion }

        public static void Configure(Material[] palette)
        {
            var ceramic = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "Art/Generated/AerospaceCeramic_Generated Maps/BaseMap.png");
            // The running surface and ceramic guards have continuous, metre-scaled
            // planar UVs; control faces and the energy core retain their clean colour.
            // Painted running channels and deck do not share the warm ceramic
            // image or the broad metallic reflection of the structural trim.
            SetSurface(palette[0], null, "#31495C", .06f, .22f);
            SetSurface(palette[8], ceramic, "#E6EEF3", .05f, .31f);
            SetSurface(palette[9], null, "#8C9CA8", .85f, .62f);
            SetSurface(palette[3], null, "#101D2D", .02f, .22f);
            SetSurface(palette[2], null, "#D2AA66", .78f, .45f);
            SetSurface(palette[13], null, "#152C43", .05f, .22f);
            SetSurface(palette[14], null, "#819AA8", .08f, .32f);

        }

        public static void ConfigureLunarMaterials()
        {
            var color = AssetDatabase.LoadAssetAtPath<Texture2D>(Survey + "Moon_Albedo_2k.jpg");
            var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(Survey + "Moon_Normal_LOLA.png");
            var height = AssetDatabase.LoadAssetAtPath<Texture2D>(Survey + "Moon_Height_RG16.png");
            var occlusion = AssetDatabase.LoadAssetAtPath<Texture2D>(Survey + "Moon_AO_LOLA.png");
            if (color == null || normal == null || height == null || occlusion == null)
                throw new InvalidOperationException("Import the credited LunarSurvey albedo, height, normal and terrain AO maps before rebuilding.");
            ConfigureTexture(color, SurfaceMap.Color);
            ConfigureTexture(normal, SurfaceMap.Normal);
            ConfigureTexture(height, SurfaceMap.Height);
            ConfigureTexture(occlusion, SurfaceMap.Occlusion);
            foreach (string name in new[] { "MoonSurface", "MoonCrater" })
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(Root + "Materials/" + name + ".mat");
                if (material == null) throw new InvalidOperationException("Missing lunar material: " + name);
                material.SetTexture("_BaseMap", color);
                material.SetTexture("_BumpMap", normal);
                // Height is sampled while constructing the decorative mesh. The
                // material does not enable parallax or require a custom XR shader.
                material.SetTexture("_ParallaxMap", height);
                material.SetTexture("_OcclusionMap", occlusion);
                material.SetFloat("_OcclusionStrength", .85f);
                material.SetColor("_BaseColor", Color.white);
                material.SetFloat("_Metallic", 0);
                material.SetFloat("_Smoothness", .16f);
                material.SetFloat("_BumpScale", .8f);
                material.EnableKeyword("_NORMALMAP");
                material.EnableKeyword("_OCCLUSIONMAP");
                material.DisableKeyword("_PARALLAXMAP");
                material.DisableKeyword("_EMISSION");
                EditorUtility.SetDirty(material);
            }
        }

        static void SetSurface(Material material, Texture2D texture, string color, float metallic, float smoothness)
        {
            material.SetTexture("_BaseMap", texture);
            ColorUtility.TryParseHtmlString(color, out Color tint);
            material.SetColor("_BaseColor", tint);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(material);
        }

        static void ConfigureTexture(Texture2D texture, SurfaceMap map)
        {
            bool normal = map == SurfaceMap.Normal;
            bool height = map == SurfaceMap.Height;
            bool color = map == SurfaceMap.Color;
            var importer = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture));
            var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            bool changed = importer.textureType != type || importer.sRGBTexture != color
                || importer.isReadable != height || importer.wrapModeU != TextureWrapMode.Repeat
                || importer.wrapModeV != TextureWrapMode.Clamp || importer.maxTextureSize != 2048
                || importer.npotScale != TextureImporterNPOTScale.None
                || (height && importer.textureCompression != TextureImporterCompression.Uncompressed);
            if (!changed) return;
            importer.textureType = type;
            importer.sRGBTexture = color;
            importer.isReadable = height;
            importer.wrapModeU = TextureWrapMode.Repeat;
            importer.wrapModeV = TextureWrapMode.Clamp;
            importer.maxTextureSize = 2048;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.mipmapEnabled = true;
            if (height) importer.textureCompression = TextureImporterCompression.Uncompressed;
            var android = importer.GetPlatformTextureSettings("Android");
            android.overridden = true;
            android.maxTextureSize = 2048;
            android.format = height ? TextureImporterFormat.RGBA32 : TextureImporterFormat.ASTC_6x6;
            importer.SetPlatformTextureSettings(android);
            importer.SaveAndReimport();
        }
    }
}
