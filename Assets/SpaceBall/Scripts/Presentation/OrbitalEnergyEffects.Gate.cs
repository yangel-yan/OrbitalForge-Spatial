using System;
using UnityEngine;

namespace SpaceBall
{
    public sealed partial class OrbitalEnergyEffects
    {
        const float GatePowerDuration = .48f;
        const float GateConfirmationDuration = .30f;

        [SerializeField] internal Transform gateCeremonyRoot;
        [SerializeField] internal Transform[] gateGuideLamps = Array.Empty<Transform>();
        [SerializeField] internal Renderer[] gateGuideRenderers = Array.Empty<Renderer>();
        [SerializeField] internal Transform[] gateLockPlates = Array.Empty<Transform>();
        [SerializeField] internal Transform[] gateScanLoops = Array.Empty<Transform>();
        bool gateObservedOpen, gateObservedCrossed;
        float gatePowerElapsed = -1, gateConfirmationElapsed = -1;

        public void ResetGateCeremony()
        {
            HideGateCeremony();
            gateObservedOpen = gateObservedCrossed = false;
        }

        /// <summary>
        /// Called only by the active board's eligible, unpaused Tick branch.
        /// The two real state transitions are consumed once per attempt.
        /// </summary>
        public void TickGateCeremony(float delta)
        {
            if (gateCeremonyRoot == null) return;
            bool open = owner.State.GateOpen;
            bool crossed = owner.State.GateCrossed;
            if (!open)
            {
                HideGateCeremony();
                return;
            }
            if (!gateObservedOpen)
            {
                gatePowerElapsed = 0;
                owner.PlayPresentationCue(OrbitalFeedbackCue.GatePower);
            }
            if (crossed && !gateObservedCrossed)
            {
                gateConfirmationElapsed = 0;
                // The existing accepted ReturnGate shockwave is the main beat.
                // Finish unlocking before the short mechanical confirmation.
                gatePowerElapsed = -1;
            }
            gateObservedOpen = open;
            gateObservedCrossed = crossed;
            if (gatePowerElapsed >= 0)
                gatePowerElapsed = Mathf.Min(GatePowerDuration, gatePowerElapsed + delta);
            if (gateConfirmationElapsed >= 0)
                gateConfirmationElapsed = Mathf.Min(GateConfirmationDuration, gateConfirmationElapsed + delta);

            float opening = gatePowerElapsed < 0 ? 1 : gatePowerElapsed / GatePowerDuration;
            float confirmation = gateConfirmationElapsed < 0 ? -1 : gateConfirmationElapsed / GateConfirmationDuration;
            RenderGateCeremony(opening, confirmation);
            if (gatePowerElapsed >= GatePowerDuration) gatePowerElapsed = -1;
            if (gateConfirmationElapsed >= GateConfirmationDuration) gateConfirmationElapsed = -1;
        }

        /// <summary>Cancel motion and hide immediately on replay, failure or board deactivation.</summary>
        public void HideGateCeremony()
        {
            gatePowerElapsed = gateConfirmationElapsed = -1;
            SetActive(gateCeremonyRoot, false);
            HideNodes(gateScanLoops);
            for (int i = 0; i < gateLockPlates.Length; i++)
                gateLockPlates[i].localPosition = new Vector3((i == 0 ? -1 : 1) * .037f, .001f, -.009f);
            // Hide can be called every replay frame. Remember the actual run's
            // flags so leaving playback cannot manufacture a new power event.
            if (owner != null)
            {
                gateObservedOpen = owner.State.GateOpen;
                gateObservedCrossed = owner.State.GateCrossed;
            }
        }

        /// <summary>Reduced Motion displays actual power/through state without an animation or new cue.</summary>
        public void SetStaticGateCeremony()
        {
            if (gateCeremonyRoot == null) return;
            gatePowerElapsed = gateConfirmationElapsed = -1;
            gateObservedOpen = owner.State.GateOpen;
            gateObservedCrossed = owner.State.GateCrossed;
            if (!gateObservedOpen)
            {
                HideGateCeremony();
                return;
            }
            RenderGateCeremony(1, -1);
        }

        void RenderGateCeremony(float opening, float confirmation)
        {
            SetActive(gateCeremonyRoot, true);
            int energized = Mathf.FloorToInt(opening * gateGuideLamps.Length);
            bool confirming = confirmation >= 0 && confirmation < 1;
            float unlock = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.20f, .88f, opening));
            float contraction = confirming ? Mathf.Sin(confirmation * Mathf.PI) : 0;
            for (int i = 0; i < gateGuideLamps.Length; i++)
            {
                gateGuideLamps[i].localPosition = new Vector3((i - 2.5f) * .015f * (1 - .10f * contraction), .0085f, -.008f);
                Material material = gateObservedCrossed ? confirming && confirmation < .45f ? white : gold :
                    i < energized ? cyan : i == energized ? white : deepCyan;
                SetMaterial(gateGuideRenderers[i], material);
            }
            for (int i = 0; i < gateLockPlates.Length; i++)
            {
                float side = i == 0 ? -1 : 1;
                float x = Mathf.Lerp(.037f, .054f, unlock);
                gateLockPlates[i].localPosition = new Vector3(side * x, .001f, -.009f);
                // These are two tiny axle scans beside the header, never another
                // large ring across the ball's opening or the existing shockwave.
                bool powerScan = gatePowerElapsed >= 0 && opening > .08f && opening < .90f;
                SetActive(gateScanLoops[i], powerScan || confirming);
                if (powerScan || confirming)
                {
                    float radius = confirming ? Mathf.Lerp(.0085f, .003f, confirmation) : Mathf.Lerp(.0075f, .0042f, opening);
                    gateScanLoops[i].localPosition = new Vector3(side * x, .009f, -.012f);
                    gateScanLoops[i].localScale = new Vector3(radius, .010f, radius);
                }
            }
        }
    }
}
