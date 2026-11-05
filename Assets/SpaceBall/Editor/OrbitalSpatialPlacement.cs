using System;
using ByteDance.PICO.SpatialAdapter;
using UnityEditor;
using UnityEngine;

namespace SpaceBall.Editor
{
    /// <summary>Place the complete chapter near the base of its spatial volume.</summary>
    public static class OrbitalSpatialPlacement
    {
        public const float BottomClearance = .02f;
        const float EdgeClearance = .04f;
        const float SizeStep = .05f;

        /// <summary>Use one metre-scale window for all chapter and interface states.</summary>
        public static Vector3 FitToContent(SpatialCamera camera, Transform content)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Adjust spatial placement in Edit mode.");
            if (camera == null || camera.OutputConfiguration == null)
                throw new InvalidOperationException("Assign a spatial output configuration before fitting the chapter.");

            Bounds bounds = FittingBounds(camera, content);
            Vector3 padded = bounds.size + new Vector3(EdgeClearance * 2, BottomClearance + EdgeClearance, EdgeClearance * 2);
            Vector3 dimensions = new Vector3(RoundUp(padded.x), RoundUp(padded.y), RoundUp(padded.z));
            Vector3 shift = new Vector3(bounds.center.x, bounds.min.y + dimensions.y * .5f - BottomClearance, bounds.center.z);
            Undo.RecordObjects(new UnityEngine.Object[] { camera, camera.transform, camera.OutputConfiguration }, "Fit chapter spatial window");
            camera.transform.position += camera.transform.TransformVector(shift);
            // Equal reference/output sizes preserve the physical size and aspect
            // of every rail, click target and piece in the native window.
            camera.Dimensions = dimensions;
            camera.OutputConfiguration.outputDimensions = dimensions;
            EditorUtility.SetDirty(camera);
            EditorUtility.SetDirty(camera.transform);
            EditorUtility.SetDirty(camera.OutputConfiguration);
            return dimensions;
        }

        static float RoundUp(float value) => Mathf.Ceil((value - .00001f) / SizeStep) * SizeStep;

        public static Bounds FittingBounds(SpatialCamera camera, Transform content)
        {
            Bounds result = ContentBounds(camera, content);
            foreach (var collider in content.GetComponentsInChildren<Collider>(true))
                IncludeCollider(ref result, camera.transform.worldToLocalMatrix, collider);

            // Any construction shape may occupy any socket, including wrong
            // solutions. Include all four orientations, not just the reference route.
            foreach (var tile in content.GetComponentsInChildren<OrbitalConstructionTile>(true))
            {
                Bounds piece = OrbitalManipulable.SpatialBounds(tile.transform);
                Matrix4x4 parent = camera.transform.worldToLocalMatrix * tile.transform.parent.localToWorldMatrix;
                for (int turn = 0; turn < 4; turn++)
                {
                    Quaternion rotation = Quaternion.Euler(0, turn * 90, 0);
                    IncludeBounds(ref result, parent * Matrix4x4.TRS(tile.homePosition + Vector3.up * .022f, rotation, tile.transform.localScale), piece);
                    foreach (var socket in tile.kit.sockets)
                    {
                        Vector3 position = tile.transform.parent.InverseTransformPoint(socket.transform.position);
                        IncludeBounds(ref result, parent * Matrix4x4.TRS(position + Vector3.up * .022f, rotation, tile.transform.localScale), piece);
                    }
                }
            }
            foreach (var piece in content.GetComponentsInChildren<OrbitalAssemblyPiece>(true))
            {
                Bounds local = OrbitalManipulable.SpatialBounds(piece.transform);
                Matrix4x4 parent = camera.transform.worldToLocalMatrix * piece.transform.parent.localToWorldMatrix;
                foreach (Vector3 position in new[] { piece.HomePosition, piece.SocketPosition })
                    for (int turn = 0; turn < 4; turn++)
                        IncludeBounds(ref result, parent * Matrix4x4.TRS(position + Vector3.up * .022f,
                            Quaternion.Euler(0, turn * 90, 0), piece.transform.localScale), local);
            }
            foreach (var effects in content.GetComponentsInChildren<OrbitalEnergyEffects>(true))
                IncludeBounds(ref result, camera.transform.worldToLocalMatrix, effects.GetMaximumBoundsInWorld());
            return result;
        }

        public static float AlignToBottom(SpatialCamera camera, Transform content)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Adjust spatial placement in Edit mode.");
            Bounds bounds = ContentBounds(camera, content);
            float shift = bounds.min.y + camera.Dimensions.y * .5f - BottomClearance;
            if (Mathf.Abs(shift) < .00001f) return 0;

            // GetRootTransform contains camera.worldToLocalMatrix: raising the
            // volume's origin lowers the content in the native spatial window.
            // All board-local physics and capture coordinates stay unchanged.
            Undo.RecordObject(camera.transform, "Align chapter to spatial window base");
            camera.transform.position += camera.transform.TransformVector(Vector3.up * shift);
            EditorUtility.SetDirty(camera.transform);
            return shift;
        }

        public static Bounds ContentBounds(SpatialCamera camera, Transform content)
        {
            if (camera == null || content == null)
                throw new ArgumentNullException(camera == null ? nameof(camera) : nameof(content));
            return ContentBoundsInFrame(camera.transform.worldToLocalMatrix, content);
        }

        static Bounds ContentBoundsInFrame(Matrix4x4 worldToFrame, Transform content)
        {
            Bounds result = default;
            bool found = false;
            // Include inactive missions and interface states so changing level
            // cannot lower a tray through the container floor.
            foreach (var filter in content.GetComponentsInChildren<MeshFilter>(true))
            {
                // Pool meshes have arbitrary resting poses. Their authored full
                // motion envelope is merged above instead of fitting idle unit meshes.
                if (filter.GetComponentInParent<OrbitalEnergyEffects>(true) != null) continue;
                var mesh = filter.sharedMesh;
                var renderer = filter.GetComponent<Renderer>();
                if (mesh == null || mesh.vertexCount == 0 || renderer == null) continue;
                Bounds local = mesh.bounds;
                Matrix4x4 matrix = worldToFrame * filter.transform.localToWorldMatrix;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = matrix.MultiplyPoint3x4(local.center + Vector3.Scale(local.extents,
                        new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1)));
                    if (!found) { result = new Bounds(point, Vector3.zero); found = true; }
                    else result.Encapsulate(point);
                }
            }
            if (!found) throw new InvalidOperationException("The chapter has no renderable meshes to align.");
            return result;
        }

        static void IncludeBounds(ref Bounds result, Matrix4x4 matrix, Bounds local)
        {
            for (int corner = 0; corner < 8; corner++)
                result.Encapsulate(matrix.MultiplyPoint3x4(local.center + Vector3.Scale(local.extents,
                    new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1))));
        }

        static void IncludeCollider(ref Bounds result, Matrix4x4 worldToFrame, Collider collider)
        {
            Bounds local;
            if (collider is BoxCollider box) local = new Bounds(box.center, box.size);
            else if (collider is SphereCollider sphere) local = new Bounds(sphere.center, Vector3.one * sphere.radius * 2);
            else if (collider is CapsuleCollider capsule)
            {
                Vector3 size = Vector3.one * capsule.radius * 2;
                size[capsule.direction] = Mathf.Max(size[capsule.direction], capsule.height);
                local = new Bounds(capsule.center, size);
            }
            else if (collider is MeshCollider mesh && mesh.sharedMesh != null) local = mesh.sharedMesh.bounds;
            else throw new InvalidOperationException("Unsupported spatial fitting collider: " + collider.name);
            IncludeBounds(ref result, worldToFrame * collider.transform.localToWorldMatrix, local);
        }
    }
}
