using ByteDance.PICO.SpatialAdapter.Component;
using UnityEditor;
using UnityEngine;

namespace SpaceBall.Editor
{
    /// <summary>Registers selected solid models as native grounding-shadow participants.</summary>
    public static class OrbitalNativeShadows
    {
        public static void Apply(OrbitalPrototype game)
        {
            // Include the saved inactive missions; their renderer entities register
            // when the corresponding board becomes active in Shared Space.
            foreach (var renderer in game.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (renderer.name == "Art_Chassis" || renderer.name == "Crater_Hull" ||
                    renderer.name == "Support machined housing")
                    Add(renderer);
            }

            foreach (var presentation in game.GetComponentsInChildren<OrbitalPresentation>(true))
            {
                var hangar = presentation.transform.Find("Art_DockingStation/Station_ImportedHabitat");
                foreach (var renderer in hangar.GetComponentsInChildren<MeshRenderer>(true))
                    Add(renderer);

                // This is the physical ball's visible model, referenced explicitly
                // so auxiliary energy shells, trails and VFX never receive a marker.
                Add(presentation.ballVisual.GetComponent<MeshRenderer>());
            }
        }

        static void Add(MeshRenderer renderer)
        {
            // Apply can be used again on the same saved scene without duplicating
            // the SDK marker. It belongs on the model entity, not its container.
            if (!renderer.TryGetComponent<SpatialAdapterGroundingShadow>(out _))
                Undo.AddComponent<SpatialAdapterGroundingShadow>(renderer.gameObject);
        }
    }
}
