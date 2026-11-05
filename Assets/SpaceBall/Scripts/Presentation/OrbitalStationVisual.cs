using UnityEngine;

namespace SpaceBall
{
    /// <summary>Fits the imported hangar into the station's existing decorative bay.</summary>
    public static class OrbitalStationVisual
    {
        static readonly Vector3 Envelope = new Vector3(.136f, .16f, .122f);
        static readonly Vector3 FloorCentre = new Vector3(.12f, -.028f, .025f);

        // The scene builder validates this art asset before creating the chapter.
        public static void Attach(Transform station, GameObject prefab)
        {
            var instance = Object.Instantiate(prefab, station, false);
            instance.name = "Station_ImportedHabitat";
            Transform visual = instance.transform;
            visual.localPosition = Vector3.zero;
            // A reversed receiving dock still presents its habitat entrance to
            // the board's operating side. Only this decorative child turns.
            visual.localRotation = Quaternion.Inverse(station.localRotation) *
                Quaternion.Euler(0, 180, 0) * visual.localRotation;

            Bounds bounds = MeshBounds(station, visual);
            float fit = Mathf.Min(Envelope.x / bounds.size.x,
                Mathf.Min(Envelope.y / bounds.size.y, Envelope.z / bounds.size.z));
            visual.localScale *= fit;
            visual.localPosition = new Vector3(FloorCentre.x - bounds.center.x * fit,
                FloorCentre.y - bounds.min.y * fit, FloorCentre.z - bounds.center.z * fit);
            foreach (Transform child in visual.GetComponentsInChildren<Transform>(true))
                child.gameObject.layer = 2; // Ignore Raycast; functional dock retains input.
        }

        static Bounds MeshBounds(Transform reference, Transform visual)
        {
            var filters = visual.GetComponentsInChildren<MeshFilter>(true);
            var bounds = new Bounds(reference.InverseTransformPoint(filters[0].transform.TransformPoint(
                filters[0].sharedMesh.bounds.center)), Vector3.zero);
            foreach (var filter in filters)
            {
                Bounds local = filter.sharedMesh.bounds;
                Matrix4x4 matrix = reference.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                    bounds.Encapsulate(matrix.MultiplyPoint3x4(local.center + Vector3.Scale(local.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
            }
            return bounds;
        }
    }
}
