using UnityEngine;

namespace SpaceBall
{
    public static partial class OrbitalEnergyEffectsArt
    {
        static void BuildLaunchCeremony(OrbitalEnergyEffects fx, Transform launcher, Mesh wave, Mesh shard)
        {
            fx.launchRoot = Group(fx.transform, "Launcher energy sleeves", launcher.localPosition);
            fx.launchRoot.localRotation = launcher.localRotation;
            fx.chargeCoils = new Transform[3];
            fx.chargeCoilRenderers = new Renderer[3];
            for (int i = 0; i < 3; i++)
            {
                var coil = MeshNode(fx.launchRoot, "Induction sleeve " + i, wave, fx.deepCyan);
                coil.localPosition = new Vector3(0, -.001f, -.073f + i * .028f);
                coil.localRotation = Quaternion.Euler(90, 0, 0);
                coil.localScale = Vector3.one * .028f;
                fx.chargeCoils[i] = coil; fx.chargeCoilRenderers[i] = coil.GetComponent<Renderer>();
            }
            fx.muzzleWave = MeshNode(fx.launchRoot, "Muzzle pressure ring", wave, fx.cyan);
            fx.muzzleWave.localRotation = Quaternion.Euler(90, 0, 0);
            fx.muzzleWave.localScale = Vector3.one * .020f;
            fx.muzzleStreaks = new Transform[4];
            for (int i = 0; i < 4; i++)
                fx.muzzleStreaks[i] = MeshNode(fx.launchRoot, "Launch discharge " + i, shard, i == 0 ? fx.white : fx.cyan);
        }
    }
}
