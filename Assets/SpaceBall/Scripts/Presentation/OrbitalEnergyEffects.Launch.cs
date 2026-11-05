using System;
using UnityEngine;

namespace SpaceBall
{
    public sealed partial class OrbitalEnergyEffects
    {
        const float ChargeDuration = .42f, LaunchDuration = .22f;
        [SerializeField] internal Transform launchRoot, muzzleWave;
        [SerializeField] internal Transform[] chargeCoils = Array.Empty<Transform>();
        [SerializeField] internal Renderer[] chargeCoilRenderers = Array.Empty<Renderer>();
        [SerializeField] internal Transform[] muzzleStreaks = Array.Empty<Transform>();
        bool launchReady, launchFired;
        float chargeElapsed, launchElapsed = -1;
        int launchSequences, chargeCycles;
        public int LaunchSequenceCount => launchSequences;
        public int ChargeCycleCount => chargeCycles;

        void ResetLaunchCeremony()
        {
            launchReady = launchFired = false;
            chargeElapsed = 0; launchElapsed = -1;
            launchSequences = chargeCycles = 0;
            HideLaunchCeremony();
        }

        public void PlayLaunch()
        {
            if (owner.State.Phase != OrbitalPhase.Running || launchFired) return;
            launchFired = true;
            if (!CanPlay()) return;
            launchElapsed = 0; launchSequences++;
            SetActive(launchRoot, true);
            RenderLaunchDischarge();
        }

        void TickLaunchCeremony(float delta)
        {
            if (owner.State.Phase == OrbitalPhase.Configuring)
            {
                bool ready = owner.LaunchConfigurationReady;
                if (ready && !launchReady)
                {
                    chargeElapsed = 0; chargeCycles++;
                    owner.PlayPresentationCue(OrbitalFeedbackCue.ChargeReady);
                }
                launchReady = ready;
                chargeElapsed = ready ? Mathf.Min(ChargeDuration, chargeElapsed + delta) : 0;
                SetActive(launchRoot, true);
                HideNodes(muzzleStreaks); SetActive(muzzleWave, false);
                for (int i = 0; i < chargeCoils.Length; i++)
                {
                    chargeCoils[i].localPosition = new Vector3(0, -.001f, -.073f + i * .028f);
                    SetMaterial(chargeCoilRenderers[i], !ready || chargeElapsed < (i + 1) * ChargeDuration / 3 ? deepCyan : cyan);
                }
                return;
            }
            if (owner.State.Phase == OrbitalPhase.Running && !launchFired) PlayLaunch();
            if (launchElapsed < 0) return;
            launchElapsed = Mathf.Min(LaunchDuration, launchElapsed + delta);
            RenderLaunchDischarge();
        }

        void RenderLaunchDischarge()
        {
            float t = launchElapsed / LaunchDuration;
            float kick = Mathf.Sin(Mathf.PI * t) * .005f;
            for (int i = 0; i < chargeCoils.Length; i++)
            {
                chargeCoils[i].localPosition = new Vector3(0, -.001f, -.073f + i * .028f - kick);
                SetMaterial(chargeCoilRenderers[i], t < .25f ? white : deepCyan);
            }
            SetActive(muzzleWave, t < .86f);
            muzzleWave.localPosition = new Vector3(0, -.001f, .002f + t * .025f);
            muzzleWave.localScale = Vector3.one * Mathf.Lerp(.020f, .038f, t);
            for (int i = 0; i < muzzleStreaks.Length; i++)
            {
                float p = Mathf.InverseLerp(i * .075f, .87f, t);
                SetActive(muzzleStreaks[i], t >= i * .075f && t < .87f);
                float angle = i * Mathf.PI * .5f + Mathf.PI * .25f;
                float radius = .015f + p * .010f;
                muzzleStreaks[i].localPosition = new Vector3(Mathf.Cos(angle) * radius,
                    -.001f + Mathf.Sin(angle) * radius, .005f + p * .060f);
                muzzleStreaks[i].localScale = new Vector3(.0035f, .0035f, .018f) * (1 - p * .75f);
            }
            if (t >= 1) { launchElapsed = -1; HideNodes(muzzleStreaks); SetActive(muzzleWave, false); }
        }

        void HideLaunchCeremony()
        {
            SetActive(launchRoot, false);
            HideNodes(muzzleStreaks); SetActive(muzzleWave, false);
            launchElapsed = -1;
        }

        void SetStaticLaunchCeremony()
        {
            launchElapsed = -1;
            launchReady = owner.State.Phase == OrbitalPhase.Configuring && owner.LaunchConfigurationReady;
            chargeElapsed = launchReady ? ChargeDuration : 0;
            if (owner.State.Phase != OrbitalPhase.Configuring) launchFired = true;
            SetActive(launchRoot, owner.State.Phase == OrbitalPhase.Configuring);
            for (int i = 0; i < chargeCoils.Length; i++)
            {
                chargeCoils[i].localPosition = new Vector3(0, -.001f, -.073f + i * .028f);
                SetMaterial(chargeCoilRenderers[i], launchReady ? cyan : deepCyan);
            }
            HideNodes(muzzleStreaks); SetActive(muzzleWave, false);
        }

        void IncludeLaunchBounds(ref Bounds bounds)
        {
            if (launchRoot == null) return; // Component OnEnable precedes factory assignment.
            for (int i = 0; i < 8; i++)
                bounds.Encapsulate(launchRoot.localPosition + launchRoot.localRotation * new Vector3(
                    (i & 1) == 0 ? -.042f : .042f, (i & 2) == 0 ? -.043f : .043f,
                    (i & 4) == 0 ? -.09f : .09f));
        }
    }
}
