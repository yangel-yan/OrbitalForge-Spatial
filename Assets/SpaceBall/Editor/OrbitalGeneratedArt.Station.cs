using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SpaceBall.Editor
{
    public static partial class OrbitalGeneratedArt
    {
        const string StationReadabilityMeshPath = Generated + "/OrbitalDock_Assets/HangarReadabilityMesh.asset";
        const string StationArmorPath = Materials + "/OrbitalDock_Armor.mat";

        [MenuItem("SpaceBall/Prepare Station Readability")]
        public static void PrepareStationReadabilityFromMenu() => Debug.Log(PrepareStationReadability());

        public static string PrepareStationReadability()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play mode before preparing the station asset.");
            var sourceModel = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var sourceMaterial = AssetDatabase.LoadAssetAtPath<Material>(Materials + "/OrbitalDock_Lit.mat");
            string originalGuid = AssetDatabase.AssetPathToGUID(PrefabPath);
            if (sourceModel == null || sourceMaterial == null || string.IsNullOrEmpty(originalGuid))
                throw new InvalidOperationException("Prepare the original station model, PBR material and prefab first.");

            string sourceGlbHash = StationSha256(File.ReadAllBytes(ModelPath));
            var previewScene = EditorSceneManager.NewPreviewScene();
            GameObject instance = null;
            Mesh working = null;
            try
            {
                // Always start with the imported GLB, never the already partitioned prefab.
                instance = Object.Instantiate(sourceModel);
                SceneManager.MoveGameObjectToScene(instance, previewScene);
                instance.name = "OrbitalDock";
                if (PrefabUtility.IsPartOfPrefabInstance(instance))
                    PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                if (instance.GetComponentsInChildren<MeshFilter>(true).Length != 1 ||
                    instance.GetComponentsInChildren<MeshRenderer>(true).Length != 1 ||
                    instance.GetComponentsInChildren<Collider>(true).Length != 0 ||
                    instance.GetComponentsInChildren<Rigidbody>(true).Length != 0)
                    throw new InvalidDataException("The reviewed station must have one static model and no physics components.");

                var filter = instance.GetComponentInChildren<MeshFilter>(true);
                var renderer = filter.GetComponent<MeshRenderer>();
                Mesh sourceMesh = filter.sharedMesh;
                if (sourceMesh.subMeshCount != 1)
                    throw new InvalidDataException("The selected GLB must retain its original single-submesh source.");
                string originalVertexHash = StationVertexHash(sourceMesh);
                string originalTriangleHash = StationTriangleHash(sourceMesh);

                // Reproduce the approved 644-triangle probe in its exact baseline pose.
                instance.transform.position = Vector3.zero;
                instance.transform.rotation = Quaternion.Euler(0, 180, 0) * sourceModel.transform.localRotation;
                Bounds bounds = renderer.bounds;
                var vertices = sourceMesh.vertices;
                var normals = sourceMesh.normals;
                var triangles = sourceMesh.triangles;
                var structural = new List<int>();
                var painted = new List<int>();
                Matrix4x4 matrix = filter.transform.localToWorldMatrix;
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                    Vector3 p = matrix.MultiplyPoint3x4((vertices[a] + vertices[b] + vertices[c]) / 3);
                    p = new Vector3((p.x - bounds.center.x) / bounds.extents.x,
                        (p.y - bounds.center.y) / bounds.extents.y, (p.z - bounds.center.z) / bounds.extents.z);
                    Vector3 n = matrix.MultiplyVector((normals[a] + normals[b] + normals[c]) / 3).normalized;
                    bool frame = n.z < -.25f && p.z < -.15f && p.y > -.65f && p.y < .45f &&
                        Mathf.Abs(p.x) < .8f && (Mathf.Abs(p.x) > .45f || p.y > .12f);
                    bool roof = n.y > .8f && Mathf.Abs(n.x) < .25f && p.y > -.05f && p.y < .45f &&
                        Mathf.Abs(p.x) < .78f && p.z < .35f;
                    var group = frame || roof ? painted : structural;
                    group.Add(a); group.Add(b); group.Add(c);
                }

                working = Object.Instantiate(sourceMesh);
                working.name = "OrbitalDock_HangarReadability";
                working.subMeshCount = 2;
                working.SetTriangles(structural, 0, false);
                working.SetTriangles(painted, 1, false);
                var preparedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(StationReadabilityMeshPath);
                if (preparedMesh == null)
                {
                    preparedMesh = working;
                    AssetDatabase.CreateAsset(preparedMesh, StationReadabilityMeshPath);
                    working = null;
                }
                else
                {
                    EditorUtility.CopySerialized(working, preparedMesh);
                    preparedMesh.UploadMeshData(false);
                    EditorUtility.SetDirty(preparedMesh);
                }
                string preparedVertexHash = StationVertexHash(preparedMesh);
                if (preparedVertexHash != originalVertexHash || StationTriangleHash(preparedMesh) != originalTriangleHash ||
                    !preparedMesh.bounds.Equals(sourceMesh.bounds))
                    throw new InvalidDataException("Station partition changed vertex data, triangle content/winding or bounds.");

                var armor = LoadOrCreateMaterial(StationArmorPath, sourceMaterial.shader);
                EditorUtility.CopySerialized(sourceMaterial, armor);
                armor.name = "OrbitalDock_Armor";
                ColorUtility.TryParseHtmlString("#CFDFE8", out Color coat);
                armor.SetColor("_BaseColor", coat);
                armor.SetTexture("_BaseMap", null);
                armor.SetTexture("_MetallicGlossMap", null);
                armor.DisableKeyword("_METALLICSPECGLOSSMAP");
                armor.SetFloat("_Metallic", .08f);
                armor.SetFloat("_Smoothness", .32f);
                armor.SetColor("_EmissionColor", Color.black);
                armor.DisableKeyword("_EMISSION");
                armor.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                EditorUtility.SetDirty(armor);

                filter.sharedMesh = preparedMesh;
                renderer.sharedMaterials = new[] { sourceMaterial, armor };
                // The classification pose must not be saved into the asset.
                instance.transform.localPosition = sourceModel.transform.localPosition;
                instance.transform.localRotation = sourceModel.transform.localRotation;
                var prefab = PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath);
                if (prefab == null || AssetDatabase.AssetPathToGUID(PrefabPath) != originalGuid)
                    throw new InvalidOperationException("The prepared station prefab failed to save with its original GUID.");
                if (StationSha256(File.ReadAllBytes(ModelPath)) != sourceGlbHash)
                    throw new InvalidDataException("The original station GLB changed during preparation.");
                AssetDatabase.SaveAssets();
                return JsonConvert.SerializeObject(new
                {
                    status = "prepared", sourceGlb = ModelPath, sourceGlbSha256 = sourceGlbHash,
                    prefab = PrefabPath, prefabGuid = originalGuid, mesh = StationReadabilityMeshPath,
                    armorMaterial = StationArmorPath, vertices = sourceMesh.vertexCount,
                    triangles = triangles.Length / 3, structuralTriangles = structural.Count / 3,
                    coatedTriangles = painted.Count / 3, subMeshes = preparedMesh.subMeshCount,
                    renderers = 1, colliders = 0, rigidbodies = 0,
                    originalVertexDataSha256 = originalVertexHash, preparedVertexDataSha256 = preparedVertexHash,
                    triangleContentSha256 = originalTriangleHash, vertexDataUnchanged = true,
                    trianglePartitionVerified = true, meshBoundsUnchanged = true, sourceGlbUnchanged = true,
                    boundsCenter = preparedMesh.bounds.center.ToString("F6"), boundsSize = preparedMesh.bounds.size.ToString("F6"),
                    prefabRootEuler = prefab.transform.localEulerAngles.ToString("F4"),
                    normalTexturePreserved = armor.GetTexture("_BumpMap") == sourceMaterial.GetTexture("_BumpMap"),
                    occlusionTexturePreserved = armor.GetTexture("_OcclusionMap") == sourceMaterial.GetTexture("_OcclusionMap")
                }, Formatting.Indented);
            }
            finally
            {
                if (working != null) Object.DestroyImmediate(working);
                if (instance != null) Object.DestroyImmediate(instance);
                EditorSceneManager.ClosePreviewScene(previewScene);
            }
        }

        static string StationVertexHash(Mesh mesh)
        {
            using var data = Mesh.AcquireReadOnlyMeshData(mesh);
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write(mesh.vertexCount);
            foreach (var attribute in mesh.GetVertexAttributes())
            {
                writer.Write((int)attribute.attribute); writer.Write((int)attribute.format);
                writer.Write(attribute.dimension); writer.Write(attribute.stream);
            }
            for (int i = 0; i < mesh.vertexBufferCount; i++)
            {
                var bytes = data[0].GetVertexData<byte>(i).ToArray();
                writer.Write(bytes.Length); writer.Write(bytes);
            }
            writer.Flush();
            return StationSha256(stream.ToArray());
        }

        static string StationTriangleHash(Mesh mesh)
        {
            var triangles = new List<string>();
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                var indices = mesh.GetTriangles(sub);
                for (int i = 0; i < indices.Length; i += 3)
                    triangles.Add(indices[i] + "," + indices[i + 1] + "," + indices[i + 2]);
            }
            triangles.Sort(StringComparer.Ordinal);
            return StationSha256(Encoding.UTF8.GetBytes(string.Join(";", triangles)));
        }

        static string StationSha256(byte[] bytes)
        {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
    }
}
