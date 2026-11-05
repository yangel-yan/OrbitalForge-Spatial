using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceBall.Editor
{
    /// <summary>Read-only source-mesh audit; does not repair assets or inspect exported GLB.</summary>
    public static class OrbitalMeshValidation
    {
        [Serializable]
        public sealed class Report
        {
            public int meshFilters, readableMeshes, unreadableMeshes, invalidMeshes;
            public string[] issues;
        }

        public static string InspectJson() => JsonUtility.ToJson(Inspect(), true);

        public static Report Inspect()
        {
            var result = new Report();
            var issues = new List<string>();
            var seen = new HashSet<int>();
            foreach (var filter in UnityEngine.Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!filter.gameObject.scene.IsValid() || !filter.gameObject.scene.isLoaded || filter.sharedMesh == null) continue;
                result.meshFilters++;
                Mesh mesh = filter.sharedMesh;
                if (!seen.Add(mesh.GetInstanceID())) continue;
                if (!mesh.isReadable) { result.unreadableMeshes++; continue; }
                result.readableMeshes++;
                Vector3[] vertices = mesh.vertices, normals = mesh.normals;
                int zeroNormals = 0, invalidPositions = 0, degenerateTriangles = 0;
                bool distinct = false;
                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector3 vertex = vertices[i];
                    if (!Finite(vertex)) invalidPositions++;
                    if (i > 0 && vertex != vertices[0]) distinct = true;
                }
                for (int i = 0; i < normals.Length; i++)
                    if (!Finite(normals[i]) || normals[i].sqrMagnitude < 1e-12f) zeroNormals++;
                int[] triangles = mesh.triangles;
                for (int i = 0; i + 2 < triangles.Length; i += 3)
                {
                    int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                    if (a < 0 || b < 0 || c < 0 || a >= vertices.Length || b >= vertices.Length || c >= vertices.Length)
                    { degenerateTriangles++; continue; }
                    Vector3 cross = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
                    if (!Finite(cross) || cross.sqrMagnitude < 1e-24f) degenerateTriangles++;
                }
                bool bad = vertices.Length == 0 || !distinct || invalidPositions > 0 || normals.Length != vertices.Length || zeroNormals > 0 || degenerateTriangles > 0;
                if (!bad) continue;
                result.invalidMeshes++;
                issues.Add(filter.name + " / " + mesh.name + ": vertices=" + vertices.Length +
                    ", allIdentical=" + !distinct + ", invalidPositions=" + invalidPositions +
                    ", normals=" + normals.Length + ", zeroOrInvalidNormals=" + zeroNormals +
                    ", degenerateOrInvalidTriangles=" + degenerateTriangles);
            }
            result.issues = issues.ToArray();
            return result;
        }

        static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
