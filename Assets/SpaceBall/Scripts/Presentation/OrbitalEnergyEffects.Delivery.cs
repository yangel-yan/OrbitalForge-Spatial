using System;
using UnityEngine;

namespace SpaceBall
{
    public sealed partial class OrbitalEnergyEffects
    {
        [SerializeField] internal Transform[] deliveryLocks = Array.Empty<Transform>();
        [SerializeField] internal Transform deliveryCore;
        [SerializeField] internal Renderer deliveryCoreRenderer;
        bool deliveryLockPlayed, deliveryConfirmationPlayed;

        void RenderDeliveryCeremony()
        {
            float seconds = dockElapsed;
            if (DockProgress >= .23f && !consumed)
            {
                consumed = true;
                SetActive(ballVisual, false);
                SetActive(shellRoot, false);
            }

            RenderDeliveryBars(seconds);
            float closure = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.55f, .80f, seconds));
            float seat = Mathf.Sin(Mathf.PI * Mathf.InverseLerp(.80f, .85f, seconds)) * .0015f;
            PoseDeliveryLocks(closure, seat);
            SetActive(deliveryCore, true);
            float charge = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.10f, .65f, seconds));
            float echo = Mathf.Sin(Mathf.PI * Mathf.InverseLerp(.80f, DockDuration, seconds));
            deliveryCore.localScale = Vector3.one * (Mathf.Lerp(.006f, .018f, charge) + echo * .003f);
            SetMaterial(deliveryCoreRenderer, seconds < .65f ? white : gold);

            // Start at the capture point, with the tiny decorative streaks lifted
            // inside the authored dock volume rather than below its lower edge.
            Vector3 capture = dockSource - dockLocal;
            capture = new Vector3(Mathf.Clamp(capture.x, -.09f, .09f),
                Mathf.Clamp(capture.y, .010f, .10f), Mathf.Clamp(capture.z, -.09f, .09f));
            for (int i = 0; i < dockFlow.Length; i++)
            {
                float start = .10f + i * .065f;
                float arrival = start + .18f;
                bool flowing = seconds >= start && seconds < arrival;
                SetActive(dockFlow[i], flowing);
                if (!flowing) continue;
                float p = Mathf.InverseLerp(start, arrival, seconds);
                Vector3 end = new Vector3(.064f, .012f + i * .015f, .055f);
                dockFlow[i].localPosition = Vector3.Lerp(capture, end, p) +
                    Vector3.up * (Mathf.Sin(p * Mathf.PI) * .012f);
                Vector3 direction = end - capture + Vector3.up * (Mathf.Cos(p * Mathf.PI) * .012f * Mathf.PI);
                dockFlow[i].localRotation = Facing(direction, Vector3.up, Vector3.forward);
                dockFlow[i].localScale = new Vector3(.008f, .008f, .024f) * (.8f + .2f * Mathf.Sin(p * Mathf.PI));
            }

            RenderDeliveryScans(seconds);
            if (!deliveryLockPlayed && seconds >= .80f)
            {
                deliveryLockPlayed = true;
                owner.PlayPresentationCue(OrbitalFeedbackCue.DockLock);
            }
            if (!deliveryConfirmationPlayed && seconds >= .86f)
            {
                deliveryConfirmationPlayed = true;
                owner.PlayDeliveryConfirmation();
            }
        }

        void RenderDeliveryBars(float seconds)
        {
            // ResetEffects also runs during AddComponent, before the art factory
            // has assigned the existing six relay renderers.
            if (relayBars == null) return;
            for (int i = 0; i < relayBars.Length; i++)
            {
                float arrival = .28f + i * .065f;
                float fill = Mathf.InverseLerp(arrival - .02f, arrival + .045f, seconds);
                float width = fill > 0 ? Mathf.Lerp(.0012f, .024f, fill) : .024f;
                Transform bar = relayBars[i].transform;
                bar.localPosition = new Vector3(.064f - (.024f - width) * .5f, .012f + i * .015f, .055f);
                bar.localScale = new Vector3(width, .006f, .006f);
                SetMaterial(relayBars[i], fill <= 0 ? dimGold : fill < 1 || i == 5 && seconds >= .80f ? white : gold);
            }
        }

        void PoseDeliveryLocks(float closure, float seat)
        {
            float radius = Mathf.Lerp(.055f, .028f, closure);
            float height = Mathf.Lerp(.018f, .011f, closure) - seat;
            for (int i = 0; i < deliveryLocks.Length; i++)
            {
                float angle = (i * 120f + 30f) * Mathf.Deg2Rad;
                Vector3 radial = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                deliveryLocks[i].localPosition = radial * radius + Vector3.up * height;
                deliveryLocks[i].localRotation = Quaternion.LookRotation(-radial, Vector3.up);
                SetActive(deliveryLocks[i], true);
            }
        }

        void RenderDeliveryScans(float seconds)
        {
            for (int i = 0; i < dockWaves.Length; i++)
            {
                float captureStart = i * .055f;
                bool capture = seconds >= captureStart && seconds < .24f;
                float echoStart = .80f + i * .07f;
                bool echo = seconds >= echoStart && seconds < DockDuration;
                SetActive(dockWaves[i], capture || echo);
                if (capture)
                {
                    float p = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(captureStart, .24f, seconds));
                    dockWaves[i].localPosition = Vector3.up * (.006f + i * .004f);
                    dockWaves[i].localScale = Vector3.one * Mathf.Lerp(.052f + i * .01f, .024f + i * .006f, p);
                }
                else if (echo)
                {
                    float p = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(echoStart, DockDuration, seconds));
                    dockWaves[i].localPosition = Vector3.up * (.006f + i * .004f);
                    dockWaves[i].localScale = Vector3.one * Mathf.Lerp(.032f, .108f - i * .016f, p);
                }
            }
        }

        void ResetDeliveryCeremony()
        {
            deliveryLockPlayed = deliveryConfirmationPlayed = false;
            HideDeliveryCeremony();
        }

        void HideDeliveryCeremony()
        {
            HideNodes(deliveryLocks);
            SetActive(deliveryCore, false);
            HideNodes(dockFlow);
            HideNodes(dockWaves);
            RenderDeliveryBars(-1);
        }

        void SetStaticDeliveryCeremony()
        {
            if (owner.State.Phase != OrbitalPhase.Success)
            {
                HideDeliveryCeremony();
                return;
            }
            HideNodes(dockFlow);
            HideNodes(dockWaves);
            PoseDeliveryLocks(1, 0);
            SetActive(deliveryCore, true);
            deliveryCore.localScale = Vector3.one * .018f;
            SetMaterial(deliveryCoreRenderer, gold);
            RenderDeliveryBars(DockDuration);
            // Reduced motion settles the real success state immediately. Its
            // confirmation sound is owned by the caller, not this static pose.
            deliveryLockPlayed = deliveryConfirmationPlayed = true;
        }
    }
}
