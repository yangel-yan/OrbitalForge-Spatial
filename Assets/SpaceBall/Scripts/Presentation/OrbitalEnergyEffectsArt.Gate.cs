using UnityEngine;

namespace SpaceBall
{
    public static partial class OrbitalEnergyEffectsArt
    {
        /// <summary>
        /// Ten shared-mesh nodes on the existing gate power header. No gate target
        /// means this board has no gate and receives no ceremony pool.
        /// </summary>
        public static void BuildGateCeremony(OrbitalEnergyEffects fx, Transform target, Mesh wave, Mesh bar)
        {
            if (target == null) return;
            fx.gateCeremonyRoot = Group(fx.transform, "Gate power ceremony", fx.bursts[2].origin);
            fx.gateGuideLamps = new Transform[6];
            fx.gateGuideRenderers = new Renderer[6];
            for (int i = 0; i < fx.gateGuideLamps.Length; i++)
            {
                Transform lamp = MeshNode(fx.gateCeremonyRoot, "Gate charge guide " + i, bar, fx.deepCyan);
                lamp.localPosition = new Vector3((i - 2.5f) * .015f, .0085f, -.008f);
                lamp.localScale = new Vector3(.010f, .0024f, .002f);
                fx.gateGuideLamps[i] = lamp;
                fx.gateGuideRenderers[i] = lamp.GetComponent<Renderer>();
            }
            fx.gateLockPlates = new Transform[2];
            fx.gateScanLoops = new Transform[2];
            for (int i = 0; i < fx.gateLockPlates.Length; i++)
            {
                float side = i == 0 ? -1 : 1;
                Transform plate = MeshNode(fx.gateCeremonyRoot, "Gate header lock " + i, bar, fx.dimGold);
                plate.localPosition = new Vector3(side * .037f, .001f, -.009f);
                plate.localScale = new Vector3(.012f, .007f, .004f);
                fx.gateLockPlates[i] = plate;
                Transform loop = MeshNode(fx.gateCeremonyRoot, "Gate axle scan " + i, wave, fx.white);
                loop.localPosition = new Vector3(side * .054f, .009f, -.012f);
                loop.localRotation = Quaternion.Euler(90, 0, 0);
                loop.localScale = new Vector3(.0085f, .010f, .0085f);
                loop.gameObject.SetActive(false);
                fx.gateScanLoops[i] = loop;
            }
            fx.gateCeremonyRoot.gameObject.SetActive(false);
        }
    }
}
