using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace SpaceBall.Editor
{
    /// <summary>One-time preparation of generated art; the scene builder only loads the results.</summary>
    public static partial class OrbitalGeneratedArt
    {
        const string Root = "Assets/SpaceBall";
        const string Generated = Root + "/Art/Generated";
        const string ModelPath = Generated + "/OrbitalDock_Assets/selected.glb";
        const string PrefabPath = Generated + "/OrbitalDock.prefab";
        const string TextureFolder = Generated + "/OrbitalDock_Assets/PreparedTextures";
        const string Materials = Root + "/Materials";
        const int TextureLimit = 1024;

        [MenuItem("SpaceBall/Prepare Generated Art")]
        public static void PrepareFromMenu() => Debug.Log(Prepare());

        public static string RefreshDockMasks()
        {
            string metallicPath = TextureFolder + "/MetallicSmoothness.png";
            string occlusionPath = TextureFolder + "/Occlusion.png";
            ConvertOrm(TextureFolder + "/SourceORM.jpg", metallicPath, occlusionPath);
            ImportTexture(metallicPath, false);
            ImportTexture(occlusionPath, false);
            AssetDatabase.SaveAssets();
            return metallicPath;
        }

        public static string Prepare()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play mode before preparing generated art.");

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("URP/Lit is required.");
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
                throw new InvalidOperationException("Import selected.glb with glTFast before preparing generated art.");

            var payload = ReadGlb(ModelPath);
            if (payload.document.materials == null || payload.document.materials.Length != 1)
                throw new InvalidOperationException("This preparation expects the reviewed single-material docking station.");
            var sourceMaterial = payload.document.materials[0];
            if (sourceMaterial.pbrMetallicRoughness == null || sourceMaterial.normalTexture == null ||
                sourceMaterial.pbrMetallicRoughness.baseColorTexture == null ||
                sourceMaterial.pbrMetallicRoughness.metallicRoughnessTexture == null)
                throw new InvalidDataException("The generated model is missing one of its reviewed PBR textures.");

            // Keep the original GLB and its embedded images untouched.
            EnsureFolder(TextureFolder);
            EnsureFolder(Materials);
            string basePath = ExtractImage(payload, sourceMaterial.pbrMetallicRoughness.baseColorTexture.index, "BaseColor");
            string normalPath = ExtractImage(payload, sourceMaterial.normalTexture.index, "Normal");
            string ormPath = ExtractImage(payload, sourceMaterial.pbrMetallicRoughness.metallicRoughnessTexture.index, "SourceORM");
            string metallicPath = TextureFolder + "/MetallicSmoothness.png";
            string occlusionPath = TextureFolder + "/Occlusion.png";
            ConvertOrm(ormPath, metallicPath, occlusionPath);

            var baseMap = ImportTexture(basePath, true);
            var normalMap = ImportTexture(normalPath, false, true);
            ImportTexture(ormPath, false);
            var metallicMap = ImportTexture(metallicPath, false);
            var occlusionMap = ImportTexture(occlusionPath, false);

            var dockMaterial = LoadOrCreateMaterial(Materials + "/OrbitalDock_Lit.mat", shader);
            ConfigureOpaqueLit(dockMaterial);
            dockMaterial.SetColor("_BaseColor", Color.white);
            dockMaterial.SetTexture("_BaseMap", baseMap);
            dockMaterial.SetTexture("_BumpMap", normalMap);
            dockMaterial.SetFloat("_BumpScale", 1f);
            dockMaterial.SetTexture("_MetallicGlossMap", metallicMap);
            dockMaterial.SetFloat("_Metallic", 1f);
            dockMaterial.SetFloat("_Smoothness", 1f);
            dockMaterial.SetFloat("_SmoothnessTextureChannel", 0f);
            dockMaterial.SetTexture("_OcclusionMap", occlusionMap);
            dockMaterial.SetFloat("_OcclusionStrength", 1f);
            dockMaterial.SetFloat("_Cull", sourceMaterial.doubleSided ? (float)CullMode.Off : (float)CullMode.Back);
            dockMaterial.EnableKeyword("_NORMALMAP");
            dockMaterial.EnableKeyword("_METALLICSPECGLOSSMAP");
            dockMaterial.EnableKeyword("_OCCLUSIONMAP");
            EditorUtility.SetDirty(dockMaterial);

            // Save at the existing path so all scene/prefab references keep the original GUID.
            string originalGuid = AssetDatabase.AssetPathToGUID(PrefabPath);
            GameObject instance = null;
            int vertexCount = 0, triangleCount = 0;
            try
            {
                instance = Object.Instantiate(model);
                instance.name = "OrbitalDock";
                if (PrefabUtility.IsPartOfPrefabInstance(instance))
                    PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                    renderer.sharedMaterials = renderer.sharedMaterials.Select(_ => dockMaterial).ToArray();
                foreach (var filter in instance.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (filter.sharedMesh == null) continue;
                    vertexCount += filter.sharedMesh.vertexCount;
                    for (int sub = 0; sub < filter.sharedMesh.subMeshCount; sub++)
                        triangleCount += (int)filter.sharedMesh.GetIndexCount(sub) / 3;
                }
                if (triangleCount < 100)
                    throw new InvalidOperationException("The imported station is still a placeholder, so the prefab was not overwritten.");
                var prefab = PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath);
                if (prefab == null) throw new IOException("Unity could not save the prepared station prefab.");
                if (!string.IsNullOrEmpty(originalGuid) && AssetDatabase.AssetPathToGUID(PrefabPath) != originalGuid)
                    throw new InvalidOperationException("The station prefab GUID unexpectedly changed.");
            }
            finally
            {
                if (instance != null) Object.DestroyImmediate(instance);
            }

            OrbitalSurfaceMaterials.ConfigureLunarMaterials();
            var ceramicMap = ImportTexture(Generated + "/AerospaceCeramic_Generated Maps/BaseMap.png", true);
            PrepareSurface(Materials + "/Ceramic.mat", shader, ceramicMap, Color.white, .48f);
            PrepareStationReadability();
            AssetDatabase.SaveAssets();
            return $"Prepared OrbitalDock: {vertexCount} vertices, {triangleCount} triangles; persistent URP/Lit PBR textures, MoonSurface, MoonCrater and Ceramic. Texture import limit: {TextureLimit}.";
        }

        static void PrepareSurface(string path, Shader shader, Texture2D texture, Color tint, float smoothness)
        {
            var material = LoadOrCreateMaterial(path, shader);
            ConfigureOpaqueLit(material);
            material.SetTexture("_BaseMap", texture);
            material.SetColor("_BaseColor", tint);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", smoothness);
            material.SetTexture("_BumpMap", null);
            material.SetTexture("_MetallicGlossMap", null);
            material.SetTexture("_OcclusionMap", null);
            material.DisableKeyword("_NORMALMAP");
            material.DisableKeyword("_METALLICSPECGLOSSMAP");
            material.DisableKeyword("_OCCLUSIONMAP");
            EditorUtility.SetDirty(material);
        }

        static Material LoadOrCreateMaterial(string path, Shader shader)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            return material;
        }

        static void ConfigureOpaqueLit(Material material)
        {
            material.SetFloat("_WorkflowMode", 1f);
            material.SetFloat("_Surface", 0f);
            material.SetFloat("_AlphaClip", 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.One);
            material.SetFloat("_DstBlend", (float)BlendMode.Zero);
            material.SetFloat("_ZWrite", 1f);
            material.SetFloat("_Cull", (float)CullMode.Back);
            material.SetTextureScale("_BaseMap", Vector2.one);
            material.SetTextureOffset("_BaseMap", Vector2.zero);
            material.SetOverrideTag("RenderType", "Opaque");
            material.renderQueue = -1;
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.DisableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
            material.DisableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", Color.black);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
        }

        static Texture2D ImportTexture(string path, bool srgb, bool normal = false)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("Generated texture is missing.", path);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("No TextureImporter for " + path);
            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.convertToNormalmap = false;
            importer.sRGBTexture = srgb;
            importer.maxTextureSize = TextureLimit;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 2;
            importer.isReadable = false;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static void ConvertOrm(string sourcePath, string metallicPath, string occlusionPath)
        {
            Texture2D source = null, metallic = null, occlusion = null;
            try
            {
                source = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
                if (!ImageConversion.LoadImage(source, File.ReadAllBytes(sourcePath), false))
                    throw new InvalidDataException("The embedded ORM image could not be decoded.");
                float scale = Mathf.Min(1f, (float)TextureLimit / Mathf.Max(source.width, source.height));
                int width = Mathf.Max(1, Mathf.RoundToInt(source.width * scale));
                int height = Mathf.Max(1, Mathf.RoundToInt(source.height * scale));
                var metalPixels = new Color32[width * height];
                var aoPixels = new Color32[width * height];
                for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    Color32 orm = source.GetPixelBilinear((x + .5f) / width, (y + .5f) / height);
                    int index = y * width + x;
                    // URP reads R=metalness and A=smoothness. The installed glTF
                    // exporter inverts A but leaves RGB unchanged, so also retain
                    // G=roughness and B=metalness for its native glTF consumer.
                    metalPixels[index] = new Color32(orm.b, orm.g, orm.b, (byte)(255 - orm.g));
                    // URP's occlusion shader samples green; preserve glTF's red channel there.
                    aoPixels[index] = new Color32(orm.r, orm.r, orm.r, 255);
                }
                metallic = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
                metallic.SetPixels32(metalPixels); metallic.Apply(false, false);
                occlusion = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
                occlusion.SetPixels32(aoPixels); occlusion.Apply(false, false);
                File.WriteAllBytes(metallicPath, ImageConversion.EncodeToPNG(metallic));
                File.WriteAllBytes(occlusionPath, ImageConversion.EncodeToPNG(occlusion));
            }
            finally
            {
                if (source != null) Object.DestroyImmediate(source);
                if (metallic != null) Object.DestroyImmediate(metallic);
                if (occlusion != null) Object.DestroyImmediate(occlusion);
            }
        }

        static string ExtractImage(GlbPayload payload, int textureIndex, string name)
        {
            var document = payload.document;
            if (textureIndex < 0 || textureIndex >= document.textures.Length)
                throw new InvalidDataException("Invalid glTF texture index.");
            var image = document.images[document.textures[textureIndex].source];
            if (image.bufferView < 0 || image.bufferView >= document.bufferViews.Length)
                throw new InvalidDataException("Expected an embedded glTF image.");
            string extension = image.mimeType == "image/png" ? ".png" : image.mimeType == "image/jpeg" ? ".jpg" : null;
            if (extension == null) throw new InvalidDataException("Unsupported embedded image format: " + image.mimeType);
            var view = document.bufferViews[image.bufferView];
            if (view.buffer != 0 || view.byteOffset < 0 || view.byteLength <= 0 ||
                (long)view.byteOffset + view.byteLength > payload.binaryLength)
                throw new InvalidDataException("The embedded image is outside the GLB binary chunk.");
            var bytes = new byte[view.byteLength];
            Buffer.BlockCopy(payload.bytes, payload.binaryOffset + view.byteOffset, bytes, 0, bytes.Length);
            string path = TextureFolder + "/" + name + extension;
            File.WriteAllBytes(path, bytes);
            return path;
        }

        static GlbPayload ReadGlb(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            using var stream = new MemoryStream(bytes, false);
            using var reader = new BinaryReader(stream, Encoding.UTF8);
            if (bytes.Length < 20 || reader.ReadUInt32() != 0x46546C67 || reader.ReadUInt32() != 2 || reader.ReadUInt32() != bytes.Length)
                throw new InvalidDataException("Expected a complete glTF 2.0 binary file.");
            var payload = new GlbPayload { bytes = bytes };
            while (stream.Position + 8 <= stream.Length)
            {
                uint length = reader.ReadUInt32();
                uint type = reader.ReadUInt32();
                if (length > int.MaxValue || stream.Position + length > stream.Length)
                    throw new InvalidDataException("Invalid GLB chunk size.");
                if (type == 0x4E4F534A)
                    payload.document = JsonUtility.FromJson<GlbDocument>(Encoding.UTF8.GetString(reader.ReadBytes((int)length)));
                else
                {
                    if (type == 0x004E4942)
                    {
                        payload.binaryOffset = (int)stream.Position;
                        payload.binaryLength = (int)length;
                    }
                    stream.Position += length;
                }
            }
            if (payload.document == null || payload.binaryLength == 0)
                throw new InvalidDataException("GLB JSON or binary chunk is missing.");
            return payload;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(parent)) throw new ArgumentException("Invalid asset folder: " + path);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        sealed class GlbPayload { public byte[] bytes; public GlbDocument document; public int binaryOffset, binaryLength; }
        [Serializable] sealed class GlbDocument { public GlbMaterial[] materials; public GlbTexture[] textures; public GlbImage[] images; public GlbBufferView[] bufferViews; }
        [Serializable] sealed class GlbMaterial { public bool doubleSided; public GlbPbr pbrMetallicRoughness; public GlbTextureInfo normalTexture; }
        [Serializable] sealed class GlbPbr { public GlbTextureInfo baseColorTexture, metallicRoughnessTexture; }
        [Serializable] sealed class GlbTextureInfo { public int index; }
        [Serializable] sealed class GlbTexture { public int source; }
        [Serializable] sealed class GlbImage { public int bufferView; public string mimeType; }
        [Serializable] sealed class GlbBufferView { public int buffer, byteOffset, byteLength; }
    }
}
