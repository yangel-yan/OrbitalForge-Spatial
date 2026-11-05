using UnityEngine;
using ByteDance.PICO.SpatialAdapter;

namespace SpaceBall
{
    /// <summary>Shared capture lifecycle for system-selected construction objects.</summary>
    public abstract class OrbitalManipulable : MonoBehaviour
    {
        SpatialCamera spatialCamera;
        Bounds manipulationBounds;
        bool hasManipulationBounds;

        protected void CacheSpatialBounds()
        {
            spatialCamera = SpatialCamera.Current != null ? SpatialCamera.Current : FindFirstObjectByType<SpatialCamera>();
            hasManipulationBounds = false;
            if (spatialCamera == null) return;
            manipulationBounds = SpatialBounds(transform);
            hasManipulationBounds = true;
            // Decorative selection frames can expand by 7% while held.
            manipulationBounds.Expand(.02f);
        }

        /// <summary>Full part envelope, including native selection and physical colliders.</summary>
        public static Bounds SpatialBounds(Transform root)
        {
            Bounds result = default;
            bool found = false;
            Matrix4x4 worldToPart = root.worldToLocalMatrix;
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null || filter.sharedMesh.vertexCount == 0) continue;
                Include(ref result, ref found, filter.sharedMesh.bounds, worldToPart * filter.transform.localToWorldMatrix);
            }
            foreach (var collider in root.GetComponentsInChildren<Collider>(true))
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
                else continue;
                Include(ref result, ref found, local, worldToPart * collider.transform.localToWorldMatrix);
            }
            return result;
        }

        static void Include(ref Bounds result, ref bool found, Bounds part, Matrix4x4 matrix)
        {
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = matrix.MultiplyPoint3x4(part.center + Vector3.Scale(part.extents,
                    new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1)));
                if (!found) { result = new Bounds(point, Vector3.zero); found = true; }
                else result.Encapsulate(point);
            }
        }

        protected Vector3 ConstrainToSpatialWindow(Vector3 proposedLocal)
        {
            if (spatialCamera == null || !hasManipulationBounds) return proposedLocal;
            Vector3 world = transform.parent != null ? transform.parent.TransformPoint(proposedLocal) : proposedLocal;
            Vector3 delta = world - transform.position;
            Matrix4x4 toCamera = spatialCamera.transform.worldToLocalMatrix;
            Bounds candidate = default;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = toCamera.MultiplyPoint3x4(transform.TransformPoint(manipulationBounds.center +
                    Vector3.Scale(manipulationBounds.extents, new Vector3((corner & 1) == 0 ? -1 : 1,
                        (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1))) + delta);
                if (corner == 0) candidate = new Bounds(point, Vector3.zero);
                else candidate.Encapsulate(point);
            }
            Vector3 half = spatialCamera.Dimensions * .5f - Vector3.one * .005f;
            Vector3 correction = Vector3.zero;
            for (int axis = 0; axis < 3; axis++)
            {
                float minimum = -half[axis] - candidate.min[axis];
                float maximum = half[axis] - candidate.max[axis];
                correction[axis] = minimum <= maximum ? Mathf.Clamp(0, minimum, maximum) : -candidate.center[axis];
            }
            world += spatialCamera.transform.TransformVector(correction);
            return transform.parent != null ? transform.parent.InverseTransformPoint(world) : world;
        }

        public abstract Plane ManipulationPlane { get; }
        public abstract bool BeginManipulation(Vector3 worldPosition);
        public abstract void UpdateManipulation(Vector3 worldPosition);
        public abstract void EndManipulation(bool cancelled);
    }
}
