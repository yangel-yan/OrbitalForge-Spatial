using System;
using UnityEngine;

namespace SpaceBall
{
    /// <summary>Short physical-event feedback using only pre-exported native meshes.</summary>
    [DisallowMultipleComponent]
    public sealed partial class OrbitalEnergyEffects : MonoBehaviour
    {
        public const float MaximumTrailLength = .12f;
        public const float BurstDuration = .42f;
        public const float DockDuration = 1.05f;
        const float BurstRadius = .075f;
        const float ScanRadius = .115f;

        [Serializable]
        public sealed class Burst
        {
            public Transform root;
            public Transform[] rings, sparks;
            public Vector3 origin;
            [NonSerialized] public float elapsed = -1;
        }

        [SerializeField] OrbitalPrototype owner;
        [SerializeField] internal Transform board, ballVisual, shellRoot, dockRoot;
        [SerializeField] internal Rigidbody energyBall;
        [SerializeField] internal Renderer coreRenderer;
        [SerializeField] internal Transform[] shellRings, tail, dockFlow, dockWaves;
        [SerializeField] internal Renderer[] shellRenderers, relayBars;
        [SerializeField] internal Burst[] bursts;
        [SerializeField] internal Vector3 dockLocal;
        [SerializeField] internal Material white, cyan, deepCyan, gold, dimGold;
        readonly Vector3[] points = new Vector3[48];
        readonly bool[] seen = new bool[5];
        int pointCount, trailSegments, burstCount, acceptedBursts, dockCount;
        float trailLength, dockElapsed = -1, animationTime, stationaryTime;
        Vector3 dockSource;
        bool applicationPaused, wasRunning, consumed;

        public int ActiveTrailSegments => trailSegments;
        public float TrailLength => trailLength;
        public int TrailPointCount => pointCount;
        public Vector3 GetTrailPoint(int index) => index >= 0 && index < pointCount ? points[index] : throw new ArgumentOutOfRangeException(nameof(index));
        public Vector3 TrailPoint(int index) => GetTrailPoint(index);
        public int ActiveBurstCount => burstCount;
        public int AcceptedBurstCount => acceptedBursts;
        public int EmittedBurstCount => acceptedBursts;
        public int DockSequenceCount => dockCount;
        public bool DockActive => dockElapsed >= 0 && dockElapsed < DockDuration;
        public bool IsDockSequenceActive => DockActive;
        public float DockProgress => dockElapsed < 0 ? 0 : Mathf.Clamp01(dockElapsed / DockDuration);

        public static OrbitalEnergyEffects Create(OrbitalPrototype owner, OrbitalPresentation presentation,
            Rigidbody ball, Transform board, Transform[] targets, Material[] palette) =>
            OrbitalEnergyEffectsArt.Build(owner, presentation, ball, board, targets, palette);

        public void Bind(OrbitalPrototype prototype)
        {
            owner = prototype;
            ResetEffects();
        }

        void OnEnable() => ResetEffects();
        void OnDisable() => ResetEffects();
        void OnApplicationPause(bool paused)
        {
            applicationPaused = paused;
            if (paused) ClearTrail();
        }

        public void ResetEffects()
        {
            ClearTrail(); ClearBursts();
            dockElapsed = -1; animationTime = 0;
            acceptedBursts = dockCount = 0; wasRunning = consumed = false;
            Array.Clear(seen, 0, seen.Length);
            SetActive(ballVisual, true);
            SetActive(shellRoot, true);
            if (shellRoot != null)
            {
                shellRoot.localScale = Vector3.one;
                if (energyBall != null) shellRoot.position = energyBall.position;
            }
            if (shellRings != null)
                for (int i = 0; i < shellRings.Length; i++)
                    shellRings[i].localRotation = Quaternion.Euler(i * 56 + 24, i * 39, i * 47);
            if (shellRenderers != null)
                for (int i = 0; i < shellRenderers.Length; i++) SetMaterial(shellRenderers[i], i == 0 ? white : cyan);
            HideNodes(dockFlow); HideNodes(dockWaves);
            SetRelayCount(0);
            ResetLaunchCeremony();
            ResetGateCeremony();
            ResetDeliveryCeremony();
        }

        public void ApplyMotionPreference()
        {
            // A preference change settles this attempt; only ResetEffects starts
            // a new attempt and clears its consumed-event markers.
            ClearMotion();
            wasRunning = false;
            SetActive(ballVisual, true); consumed = false;
            UpdateCore(0, false);
            if (owner.IsReplaying || owner.State.Phase == OrbitalPhase.Failure) return;
            SetStaticLaunchCeremony();
            SetStaticGateCeremony();
            SetStaticDeliveryCeremony();
            if (owner.State.Phase == OrbitalPhase.Success) owner.PlayDeliveryConfirmation();
        }

        public void PlayEvent(OrbitalEvent evt)
        {
            if (!CanPlay()) return;
            int index;
            bool accepted;
            switch (evt)
            {
                case OrbitalEvent.TargetA: index = 0; accepted = owner.State.HasA; break;
                case OrbitalEvent.TargetB: index = 1; accepted = owner.State.HasB; break;
                case OrbitalEvent.ReturnGate: index = 2; accepted = owner.State.GateCrossed; break;
                case OrbitalEvent.CheckpointC: index = 4; accepted = owner.State.HasC; break;
                case OrbitalEvent.Dock: index = 3; accepted = owner.State.Phase == OrbitalPhase.Success; break;
                default: return;
            }
            if (!accepted || seen[index]) return;
            seen[index] = true;
            if (index == 3)
            {
                dockElapsed = 0; dockSource = board.InverseTransformPoint(energyBall.position);
                dockCount++; consumed = false;
                ClearTrail();
                RenderDock();
            }
            else if (bursts != null && index < bursts.Length && bursts[index] != null)
            {
                bursts[index].elapsed = 0;
                acceptedBursts++; burstCount++;
                RenderBurst(bursts[index]);
            }
        }

        bool CanPlay() => owner != null && energyBall != null && owner.Ball == energyBall &&
            isActiveAndEnabled && !applicationPaused && !owner.IsReplaying && !owner.ReducedMotion &&
            (owner.State.Phase == OrbitalPhase.Running || owner.State.Phase == OrbitalPhase.Success);

        public void Tick(float delta)
        {
            if (owner == null || energyBall == null || board == null) return;
            if (!isActiveAndEnabled || owner.Ball != energyBall) return;
            if (applicationPaused)
            {
                ClearTrail(); wasRunning = false;
                return;
            }
            if (owner.IsReplaying || owner.State.Phase == OrbitalPhase.Failure)
            {
                ClearMotion(); SetActive(shellRoot, false);
                // Delivery consumes only the decorative child. Replaying the
                // recorded Rigidbody path must make that child visible again.
                SetActive(ballVisual, true); consumed = false;
                return;
            }
            if (owner.ReducedMotion)
            {
                ClearTrail(); ClearBursts(); wasRunning = false;
                dockElapsed = -1;
                HideNodes(dockFlow); HideNodes(dockWaves);
                SetActive(ballVisual, true); consumed = false;
                UpdateCore(0, false);
                SetRelayCount(owner.State.Phase == OrbitalPhase.Success ? 6 : 0);
                SetStaticLaunchCeremony();
                SetStaticGateCeremony();
                SetStaticDeliveryCeremony();
                if (owner.State.Phase == OrbitalPhase.Success) owner.PlayDeliveryConfirmation();
                return;
            }
            if (owner.State.Phase == OrbitalPhase.Paused)
            {
                ClearTrail(); wasRunning = false;
                return;
            }
            delta = Mathf.Max(0, delta);
            animationTime += delta;
            TickLaunchCeremony(delta);
            TickGateCeremony(delta);
            bool running = owner.State.Phase == OrbitalPhase.Running;
            if (running)
            {
                if (!wasRunning) ClearTrail();
                SampleTrail(delta);
            }
            else ClearTrail();
            wasRunning = running;
            if (bursts != null)
                foreach (var burst in bursts)
                {
                    if (burst == null || burst.elapsed < 0) continue;
                    burst.elapsed += delta;
                    if (burst.elapsed >= BurstDuration)
                    {
                        burst.elapsed = -1; burstCount--;
                        SetActive(burst.root, false);
                    }
                    else RenderBurst(burst);
                }
            if (DockActive)
            {
                dockElapsed = Mathf.Min(DockDuration, dockElapsed + delta);
                RenderDock();
            }
            UpdateCore(delta, true);
        }

        void UpdateCore(float delta, bool moving)
        {
            if (shellRoot == null || energyBall == null) return;
            Vector3 local = board.InverseTransformPoint(energyBall.position);
            bool visible = !consumed && InFlightArea(local);
            SetActive(shellRoot, visible);
            if (!visible) return;
            if ((shellRoot.position - energyBall.position).sqrMagnitude > .00000001f) shellRoot.position = energyBall.position;
            Vector3 scale = Vector3.one * (DockActive ? Mathf.Lerp(1, .30f, Mathf.Clamp01(dockElapsed / .24f)) : 1);
            if (shellRoot.localScale != scale) shellRoot.localScale = scale;
            bool charged = owner.State.HasA && owner.State.HasB;
            if (shellRenderers != null)
                for (int i = 0; i < shellRenderers.Length; i++)
                    SetMaterial(shellRenderers[i], i == 0 ? white : charged ? gold : cyan);
            if (shellRings != null)
                for (int i = 0; i < shellRings.Length; i++)
                {
                    Quaternion angle = Quaternion.Euler(i * 56 + 24, i * 39 + (moving ? animationTime * (i == 1 ? -32 : 24) : 0), i * 47);
                    if (shellRings[i].localRotation != angle) shellRings[i].localRotation = angle;
                }
        }

        static bool InFlightArea(Vector3 p) => Mathf.Abs(p.x) <= .55f && Mathf.Abs(p.z) <= .43f && p.y >= -.035f && p.y <= .40f;

        void SampleTrail(float delta)
        {
            Vector3 position = energyBall.position;
            if (!InFlightArea(board.InverseTransformPoint(position))) { ClearTrail(); return; }
            if (pointCount > 0 && (position - points[0]).sqrMagnitude > .18f * .18f) ClearTrail();
            if (pointCount == 0) { points[0] = position; pointCount = 1; return; }
            if ((position - points[0]).sqrMagnitude > .0004f * .0004f)
            {
                stationaryTime = 0;
                int count = Mathf.Min(pointCount, points.Length - 1);
                for (int i = count; i > 0; i--) points[i] = points[i - 1];
                points[0] = position; pointCount = Mathf.Min(pointCount + 1, points.Length);
            }
            else
            {
                stationaryTime += delta;
                if (stationaryTime >= .12f) { ClearTrail(); return; }
            }
            trailLength = 0;
            for (int i = 1; i < pointCount; i++)
            {
                float segment = Vector3.Distance(points[i - 1], points[i]);
                if (trailLength + segment > MaximumTrailLength)
                {
                    points[i] = Vector3.Lerp(points[i - 1], points[i], (MaximumTrailLength - trailLength) / segment);
                    pointCount = i + 1; trailLength = MaximumTrailLength;
                    break;
                }
                trailLength += segment;
            }
            trailSegments = 0;
            if (tail == null || trailLength < .006f) { HideNodes(tail); return; }
            for (int i = 0; i < tail.Length; i++)
            {
                Vector3 a = PathPoint(trailLength * i / tail.Length);
                Vector3 b = PathPoint(trailLength * (i + 1) / tail.Length);
                Vector3 direction = b - a;
                if (direction.sqrMagnitude < .00000001f) { SetActive(tail[i], false); continue; }
                Transform node = tail[i];
                node.position = (a + b) * .5f;
                node.rotation = Facing(direction, board.up, board.forward);
                float width = Mathf.Lerp(.009f, .0014f, i / (float)(tail.Length - 1));
                node.localScale = new Vector3(width, width, direction.magnitude * 1.05f);
                SetActive(node, true); trailSegments++;
            }
        }

        Vector3 PathPoint(float distance)
        {
            for (int i = 1; i < pointCount; i++)
            {
                float length = Vector3.Distance(points[i - 1], points[i]);
                if (distance <= length) return Vector3.Lerp(points[i - 1], points[i], length <= .000001f ? 0 : distance / length);
                distance -= length;
            }
            return points[pointCount - 1];
        }

        void RenderBurst(Burst burst)
        {
            SetActive(burst.root, true);
            float t = Mathf.Clamp01(burst.elapsed / BurstDuration);
            for (int i = 0; i < burst.rings.Length; i++)
            {
                float phase = Mathf.Clamp01((t - i * .13f) / (1 - i * .13f));
                SetActive(burst.rings[i], t >= i * .13f && phase < .96f);
                burst.rings[i].localPosition = Vector3.up * (.004f + phase * .009f + i * .004f);
                float radius = Mathf.Lerp(.026f, BurstRadius - i * .010f, 1 - (1 - phase) * (1 - phase));
                burst.rings[i].localScale = Vector3.one * radius;
            }
            for (int i = 0; i < burst.sparks.Length; i++)
            {
                float angle = (i * 45f + 11) * Mathf.Deg2Rad;
                Vector3 direction = new Vector3(Mathf.Cos(angle), .25f + (i % 3) * .12f, Mathf.Sin(angle));
                burst.sparks[i].localPosition = direction * (.022f + t * .042f) + Vector3.up * (.004f - t * t * .012f);
                burst.sparks[i].localRotation = Quaternion.LookRotation(direction, Vector3.up);
                float fade = 1 - t;
                burst.sparks[i].localScale = new Vector3(.005f * fade, .005f * fade, .019f * Mathf.Sqrt(fade));
                SetActive(burst.sparks[i], t < .90f);
            }
        }

        void RenderDock() => RenderDeliveryCeremony();

        void SetRelayCount(int count)
        {
            if (relayBars == null) return;
            for (int i = 0; i < relayBars.Length; i++) SetMaterial(relayBars[i], i < count ? i == count - 1 ? white : gold : dimGold);
        }

        void ClearTrail() { pointCount = trailSegments = 0; trailLength = stationaryTime = 0; HideNodes(tail); }
        void ClearBursts()
        {
            if (bursts != null) foreach (var burst in bursts) if (burst != null) { burst.elapsed = -1; SetActive(burst.root, false); }
            burstCount = 0;
        }
        void ClearMotion()
        {
            ClearTrail(); ClearBursts(); wasRunning = false;
            HideLaunchCeremony();
            HideGateCeremony();
            HideDeliveryCeremony();
            if (dockElapsed >= 0) { dockElapsed = -1; HideNodes(dockFlow); HideNodes(dockWaves); }
        }

        static void SetMaterial(Renderer renderer, Material material)
        {
            if (renderer != null && material != null && renderer.sharedMaterial != material) renderer.sharedMaterial = material;
        }
        static void SetActive(Transform node, bool value)
        {
            if (node != null && node.gameObject.activeSelf != value) node.gameObject.SetActive(value);
        }
        static void HideNodes(Transform[] nodes) { if (nodes != null) foreach (var node in nodes) SetActive(node, false); }

        static Quaternion Facing(Vector3 direction, Vector3 up, Vector3 alternativeUp)
        {
            if (direction.sqrMagnitude < .00000001f) return Quaternion.identity;
            return Quaternion.LookRotation(direction, Mathf.Abs(Vector3.Dot(direction.normalized, up)) > .98f ? alternativeUp : up);
        }

        public Bounds GetMaximumBoundsInWorld()
        {
            if (board == null) return new Bounds(transform.position, Vector3.zero);
            // Corona and tail are hidden once the physical ball leaves this
            // below-deck/escaped-flight area; all sampled chords stay inside it.
            Bounds local = new Bounds(new Vector3(0, .1825f, 0), new Vector3(1.132f, .467f, .892f));
            if (bursts != null)
                foreach (var burst in bursts)
                    if (burst != null)
                    {
                        local.Encapsulate(burst.origin + new Vector3(-.078f, -.004f, -.078f));
                        local.Encapsulate(burst.origin + new Vector3(.078f, .043f, .078f));
                    }
            local.Encapsulate(dockLocal + new Vector3(-.119f, -.004f, -.119f));
            local.Encapsulate(dockLocal + new Vector3(.119f, .138f, .119f));
            IncludeLaunchBounds(ref local);
            Bounds world = default;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = board.TransformPoint(local.center + Vector3.Scale(local.extents,
                    new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1)));
                if (corner == 0) world = new Bounds(point, Vector3.zero); else world.Encapsulate(point);
            }
            return world;
        }
    }
}
