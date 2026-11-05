using UnityEngine;

namespace SpaceBall
{
    /// <summary>
    /// Decorative feedback for the physical puzzle. Uses ordinary mesh transforms
    /// and shared materials so feedback can also be represented in Shared Space.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OrbitalPresentation : MonoBehaviour
    {
        [SerializeField] OrbitalPrototype owner;
        public OrbitalEnergyEffects energyEffects;

        [Header("Decorative transforms only")]
        public Transform moon;
        public Transform ballVisual;
        [Tooltip("Optional gate decoration reference. Gate geometry is never moved here.")]
        public Transform gateVisual;
        [Tooltip("Order: A, B, return gate, station, optional C. Additional rings join the success pulse.")]
        public Transform[] pulseRings = new Transform[0];

        [Header("Progress lamps")]
        public Renderer[] targetA = new Renderer[0];
        public Renderer[] targetB = new Renderer[0];
        public Renderer[] gateLamps = new Renderer[0];
        public Renderer[] stationLamps = new Renderer[0];
        public Material offMaterial;
        public Material onMaterial;
        public Material amberMaterial;

        [Header("Restrained motion")]
        [Range(5f, 30f)] public float animationRate = 30f;
        [Range(0f, 8f)] public float moonDegreesPerSecond = 5f;
        [Range(0f, 45f)] public float ballDegreesPerSecond = 16f;
        [Range(0f, .04f)] public float ballBreath = .012f;

        struct DecorativePose
        {
            public Transform transform;
            public Vector3 scale;
            public Quaternion rotation;
            public bool canAnimate;
        }

        struct RingPulse
        {
            public DecorativePose pose;
            public float elapsed;
            public float duration;
            public float strength;
            public bool active;
        }

        DecorativePose moonPose, ballPose;
        RingPulse[] rings = new RingPulse[0];
        bool initialized, applicationPaused, previousReplaying, reducedMotionApplied;
        bool previousA, previousB, previousC, previousGateOpen, previousGateCrossed;
        OrbitalLevel previousLevel;
        OrbitalPhase previousPhase;
        float accumulatedDelta, animationTime, moonAngle, ballAngle;
        float ballPulseElapsed, ballPulseDuration, ballPulseStrength;
        int lampSignature = -1;

        public void Bind(OrbitalPrototype prototype)
        {
            RestoreDecorations();
            owner = prototype;
            energyEffects?.Bind(prototype);
            CaptureDecorations();
            initialized = true;
            ResetPresentation();
        }

        void OnEnable()
        {
            if (!initialized)
            {
                CaptureDecorations();
                initialized = true;
            }

            ResetPresentation();
        }

        void OnDisable()
        {
            energyEffects?.ResetEffects();
            RestoreDecorations();
            accumulatedDelta = 0f;
        }

        void OnApplicationPause(bool paused)
        {
            applicationPaused = paused;
            accumulatedDelta = 0f;
        }

        void Update()
        {
            if (owner == null || applicationPaused)
                return;

            OrbitalRunState state = owner.State;
            if (state.Level != previousLevel ||
                (state.Phase == OrbitalPhase.Configuring && previousPhase != OrbitalPhase.Configuring))
            {
                ResetPresentation();
            }
            if (owner.IsReplaying && !previousReplaying)
                ClearPulses();

            // A replay has its own recorded lamp state. It never creates new shot events.
            if (!owner.IsReplaying && state.Phase != OrbitalPhase.Paused)
            {
                if (state.Phase == OrbitalPhase.Running && previousPhase == OrbitalPhase.Configuring)
                    PulseBall(.11f, .22f);
                if (!previousA && state.HasA) PlayEvent(OrbitalEvent.TargetA);
                if (!previousB && state.HasB) PlayEvent(OrbitalEvent.TargetB);
                if (!previousC && state.HasC) PlayEvent(OrbitalEvent.CheckpointC);
                if (!previousGateOpen && state.GateOpen) Pulse(2, .12f, .32f);
                if (!previousGateCrossed && state.GateCrossed) PlayEvent(OrbitalEvent.ReturnGate);
                if (previousPhase != OrbitalPhase.Success && state.Phase == OrbitalPhase.Success)
                    PlayEvent(OrbitalEvent.Dock);
                if (previousPhase != OrbitalPhase.Failure && state.Phase == OrbitalPhase.Failure)
                {
                    // A short contraction belongs to the failed ball only. Target rings
                    // never celebrate a rejected delivery or an uncharged return gate.
                    PulseBall(-.10f, .26f);
                    if (state.ResultCode == "closed_gate") Pulse(2, -.09f, .30f);
                }
            }

            // Accessibility changes can be made while a shot is paused. Settle
            // once immediately instead of retaining an enlarged paused target.
            if (owner.ReducedMotion && !reducedMotionApplied)
            {
                RestoreDecorations();
                reducedMotionApplied = true;
            }
            ApplyLamps();
            RememberState();
            energyEffects?.Tick(Time.deltaTime);
            if (state.Phase == OrbitalPhase.Paused)
                return;

            accumulatedDelta += Time.deltaTime;
            float interval = 1f / Mathf.Clamp(animationRate, 5f, 30f);
            if (accumulatedDelta < interval)
                return;

            float delta = accumulatedDelta;
            accumulatedDelta = 0f;
            AnimateDecorations(delta);
        }

        /// <summary>Optional immediate event hook; state polling also starts these pulses.</summary>
        public void PlayEvent(OrbitalEvent evt)
        {
            if (owner == null || owner.IsReplaying || applicationPaused ||
                owner.State.Phase == OrbitalPhase.Paused)
                return;

            energyEffects?.PlayEvent(evt);
            switch (evt)
            {
                case OrbitalEvent.TargetA:
                    Pulse(0, .15f, .34f);
                    break;
                case OrbitalEvent.TargetB:
                    Pulse(1, .18f, .40f);
                    break;
                case OrbitalEvent.ReturnGate:
                    Pulse(2, .12f, .30f);
                    break;
                case OrbitalEvent.CheckpointC:
                    Pulse(4, .18f, .40f);
                    break;
                case OrbitalEvent.Dock:
                    PulseBall(.16f, .42f);
                    Pulse(3, .23f, .62f);
                    for (int i = 4; i < rings.Length; i++)
                        Pulse(i, .18f, .62f, (i - 3) * .055f);
                    break;
            }
        }

        public void ApplyMotionPreference()
        {
            RestoreDecorations();
            accumulatedDelta = 0f;
            reducedMotionApplied = owner.ReducedMotion;
            energyEffects?.ApplyMotionPreference();
        }

        /// <summary>Clears decorative motion without changing the ball, gate, or run state.</summary>
        public void ResetPresentation()
        {
            if (!initialized)
            {
                CaptureDecorations();
                initialized = true;
            }

            RestoreDecorations();
            energyEffects?.ResetEffects();
            accumulatedDelta = animationTime = moonAngle = ballAngle = 0f;
            reducedMotionApplied = owner != null && owner.ReducedMotion;
            lampSignature = -1;
            if (owner != null)
            {
                RememberState();
                ApplyLamps();
            }
        }

        void CaptureDecorations()
        {
            moonPose = CapturePose(moon);
            ballPose = CapturePose(ballVisual);
            int count = pulseRings == null ? 0 : pulseRings.Length;
            rings = new RingPulse[count];
            for (int i = 0; i < count; i++)
                rings[i].pose = CapturePose(pulseRings[i]);
        }

        DecorativePose CapturePose(Transform target)
        {
            var pose = new DecorativePose { transform = target };
            if (target == null)
                return pose;

            pose.scale = target.localScale;
            pose.rotation = target.localRotation;
            // A mesh-only child of a Rigidbody is safe; its physical parent is untouched.
            pose.canAnimate = target.GetComponent<Rigidbody>() == null &&
                target.GetComponentInChildren<Collider>(true) == null &&
                target.GetComponentInChildren<Rigidbody>(true) == null;
            if (owner != null && owner.Ball != null && target == owner.Ball.transform)
                pose.canAnimate = false;
            return pose;
        }

        void RememberState()
        {
            previousLevel = owner.State.Level;
            previousPhase = owner.State.Phase;
            previousA = owner.State.HasA;
            previousB = owner.State.HasB;
            previousC = owner.State.HasC;
            previousGateOpen = owner.State.GateOpen;
            previousGateCrossed = owner.State.GateCrossed;
            previousReplaying = owner.IsReplaying;
        }

        void ApplyLamps()
        {
            if (owner == null)
                return;

            bool closedGateFailure = !owner.IsReplaying &&
                owner.State.Phase == OrbitalPhase.Failure &&
                owner.State.ResultCode == "closed_gate";
            int signature = (owner.DisplayA ? 1 : 0) | (owner.DisplayB ? 2 : 0) |
                (owner.DisplayGate ? 4 : 0) | (owner.DisplayDelivered ? 8 : 0) |
                (closedGateFailure ? 16 : 0);
            if (signature == lampSignature) return;
            lampSignature = signature;
            SetMaterial(targetA, owner.DisplayA ? onMaterial : offMaterial);
            SetMaterial(targetB, owner.DisplayB ? onMaterial : offMaterial);
            Material gateMaterial = owner.DisplayGate ? onMaterial :
                closedGateFailure && amberMaterial != null ? amberMaterial : offMaterial;
            SetMaterial(gateLamps, gateMaterial);
            SetMaterial(stationLamps, owner.DisplayDelivered ? onMaterial : offMaterial);
        }

        static void SetMaterial(Renderer[] renderers, Material material)
        {
            if (renderers == null || material == null)
                return;

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer != null && renderer.sharedMaterial != material)
                    renderer.sharedMaterial = material;
            }
        }

        void AnimateDecorations(float delta)
        {
            if (owner.ReducedMotion)
            {
                if (!reducedMotionApplied) RestoreDecorations();
                reducedMotionApplied = true;
                return;
            }
            reducedMotionApplied = false;
            animationTime += delta;
            moonAngle = Mathf.Repeat(moonAngle + moonDegreesPerSecond * delta, 360f);
            ballAngle = Mathf.Repeat(ballAngle + ballDegreesPerSecond * delta, 360f);
            if (moonPose.canAnimate && moonPose.transform != null)
                moonPose.transform.localRotation = moonPose.rotation * Quaternion.AngleAxis(moonAngle, Vector3.up);

            if (ballPose.canAnimate && ballPose.transform != null)
            {
                bool running = owner.State.Phase == OrbitalPhase.Running && !owner.IsReplaying;
                ballPose.transform.localRotation = running
                    ? ballPose.rotation * Quaternion.AngleAxis(ballAngle, Vector3.up)
                    : ballPose.rotation;
                ballPose.transform.localScale = ballPose.scale *
                    ((running ? 1f + Mathf.Sin(animationTime * 3f) * ballBreath : 1f) * BallPulse(delta));
            }

            for (int i = 0; i < rings.Length; i++)
            {
                RingPulse ring = rings[i];
                if (!ring.active || !ring.pose.canAnimate || ring.pose.transform == null)
                    continue;

                ring.elapsed += delta;
                if (ring.elapsed >= ring.duration)
                {
                    RestorePose(ring.pose);
                    ring.active = false;
                }
                else if (ring.elapsed >= 0f)
                {
                    float wave = Mathf.Sin(Mathf.PI * ring.elapsed / ring.duration);
                    ring.pose.transform.localScale = ring.pose.scale * (1f + wave * ring.strength);
                    // Keep the ring aligned to its real opening. Rotation during a hit
                    // makes a fixed physical target look as though it has changed pose.
                }

                rings[i] = ring;
            }
        }

        void Pulse(int index, float strength, float duration, float delay = 0f)
        {
            if (owner == null || owner.ReducedMotion || index < 0 || index >= rings.Length)
                return;

            RingPulse ring = rings[index];
            // An explicit event and the next state poll may report the same hit.
            if (ring.active || !ring.pose.canAnimate || ring.pose.transform == null)
                return;

            ring.elapsed = -delay;
            ring.duration = duration;
            ring.strength = strength;
            ring.active = true;
            rings[index] = ring;
        }

        void PulseBall(float strength, float duration)
        {
            if (owner == null || owner.ReducedMotion || !ballPose.canAnimate) return;
            ballPulseElapsed = 0;
            ballPulseDuration = duration;
            ballPulseStrength = strength;
        }

        float BallPulse(float delta)
        {
            if (ballPulseDuration <= 0) return 1f;
            ballPulseElapsed += delta;
            float t = Mathf.Clamp01(ballPulseElapsed / ballPulseDuration);
            if (t >= 1f) ballPulseDuration = 0;
            return 1f + Mathf.Sin(t * Mathf.PI) * ballPulseStrength;
        }

        void RestoreDecorations()
        {
            RestorePose(moonPose);
            RestorePose(ballPose);
            ballPulseDuration = 0;
            ClearPulses();
        }

        void ClearPulses()
        {
            ballPulseDuration = 0;
            RestorePose(ballPose);
            for (int i = 0; i < rings.Length; i++)
            {
                RestorePose(rings[i].pose);
                rings[i].active = false;
            }
        }

        static void RestorePose(DecorativePose pose)
        {
            if (!pose.canAnimate || pose.transform == null)
                return;

            pose.transform.localScale = pose.scale;
            pose.transform.localRotation = pose.rotation;
        }
    }
}
