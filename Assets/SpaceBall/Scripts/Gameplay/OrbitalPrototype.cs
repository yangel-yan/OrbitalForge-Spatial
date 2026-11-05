using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace SpaceBall
{
    /// <summary>A physical tabletop expedition. No route playback drives a live shot.</summary>
    public sealed partial class OrbitalPrototype : MonoBehaviour
    {
        public TMP_FontAsset uiFont;
        public Material[] palette;
        public Material moonSurface;
        public Material moonCrater;
        public GameObject stationVisualPrefab;
        public Camera previewCamera;
        public AudioClip[] eventSounds;
        public float localGravity = 3.6f;
        public float launchSpeed = 0.56f;

        [Serializable]
        public sealed class LevelRig
        {
            public GameObject root;
            public Rigidbody ball;
            public Vector3 launchPosition;
            public Vector3 launchDirection;
            public GameObject[] routes = new GameObject[2];
            public GameObject[] exits = new GameObject[2];
            public GameObject[] reflectors = new GameObject[2];
            public Renderer aLight, bLight, cLight, stationLight;
            public GameObject gateBlock, gateVisual;
            public OrbitalPresentation presentation;
            public Renderer[] aLamps,bLamps,cLamps = new Renderer[0];
            public OrbitalAssemblyRig assembly;
            public OrbitalConstructionKit construction;
            [NonSerialized] public int lastLampState=-1;
        }

        [SerializeField] LevelRig[] rigs;
        [SerializeField] TextMeshPro titleText, goalText, statusText, feedbackText;
        [SerializeField] OrbitalControl[] controls;
        [SerializeField] Transform[] trailDots;
        [SerializeField] PhysicsMaterial rollingSurface, bounceSurface;
        OrbitalRunState state;
        readonly List<Frame> frames = new List<Frame>();
        readonly Queue<Vector3> recentPositions = new Queue<Vector3>();
        float shotElapsed, stillTime, replayTime;
        Vector3 pausedVelocity, pausedAngularVelocity;
        bool replaying;
        bool applicationSuspended;
        float assemblyFeedbackUntil;
        Frame replayFrame;
        float previousBounceThreshold;
        string motionFeedback = "";
        AudioSource audioSource;
        OrbitalFeedbackAudio feedbackAudio;
        AudioClip tickSound, aSound, bSound, winSound;
        bool deliveryConfirmed;
        public bool LaunchConfigurationReady => Construction != null
            ? !Construction.HasHeld && Construction.PlacedCount > 0 : Assembly == null || Assembly.Ready;
        public bool DeliveryPresentationPending => State.Phase == OrbitalPhase.Success &&
            !replaying && !ReducedMotion && !deliveryConfirmed;

        public void PlayPresentationCue(OrbitalFeedbackCue cue) => feedbackAudio?.Play(cue);

        public void PlayDeliveryConfirmation()
        {
            if (deliveryConfirmed) return;
            deliveryConfirmed = true;
            interfaceSignature = -1;
            Play(winSound);
        }
        int interfaceSignature=-1;
        string interfaceFeedback,interfaceMotion;
        readonly List<OrbitalControl> buildControls = new List<OrbitalControl>();

        struct Frame
        {
            public Vector3 position;
            public Quaternion rotation;
            public bool a, b, c, gate, gateCrossed, delivered;
        }

        public OrbitalRunState State => state ?? (state = new OrbitalRunState());
        LevelRig Rig => rigs[(int)State.Level];
        public Rigidbody Ball => rigs != null && OrbitalCampaign.IsValid(State.Level) && (int)State.Level < rigs.Length ? Rig.ball : null;
        public bool IsReplaying => replaying;
        public bool DisplayA => replaying ? replayFrame.a : State.HasA;
        public bool DisplayB => replaying ? replayFrame.b : State.HasB;
        public bool DisplayC => replaying ? replayFrame.c : State.HasC;
        public bool DisplayGate => replaying ? replayFrame.gate : State.GateOpen;
        public bool DisplayGateCrossed => replaying ? replayFrame.gateCrossed : State.GateCrossed;
        public bool DisplayDelivered => replaying ? replayFrame.delivered : State.Phase == OrbitalPhase.Success;
        public float ShotElapsed => shotElapsed;
        public string StatusDescription => State.Phase + " / " + State.ResultCode + " / " + motionFeedback;
        public int RecordedFrameCount => frames.Count;
        public OrbitalAssemblyRig Assembly => rigs != null && OrbitalCampaign.IsValid(State.Level) && (int)State.Level < rigs.Length ? Rig.assembly : null;
        public OrbitalConstructionKit Construction => rigs != null && OrbitalCampaign.IsValid(State.Level) && (int)State.Level < rigs.Length ? Rig.construction : null;
        public bool AssemblyEditable => !replaying && State.Phase != OrbitalPhase.Running && State.Phase != OrbitalPhase.Paused;

        public void PrepareAssemblyEdit()
        {
            if (AssemblyEditable && State.Phase != OrbitalPhase.Configuring) ResetAttempt();
        }

        public void AssemblyChanged(string message, OrbitalFeedbackCue cue = OrbitalFeedbackCue.Select)
        {
            if (Assembly == null) return;
            State.SetRoute(Assembly.pieces[0].Choice);
            State.SetReflector(Assembly.pieces[1].Choice);
            motionFeedback = message;
            hintText = "";
            assemblyFeedbackUntil = Time.unscaledTime + 4f;
            interfaceSignature = -1;
            UpdateConfiguration();
            feedbackAudio?.Play(cue);
        }

        public void ConstructionChanged(string message, OrbitalFeedbackCue cue = OrbitalFeedbackCue.Select)
        {
            if (Construction == null) return;
            if (restoringConstruction) { interfaceSignature = -1; return; }
            SaveConstructionBlueprint();
            motionFeedback = message;
            hintText = "";
            assemblyFeedbackUntil = Time.unscaledTime + 5f;
            interfaceSignature = -1;
            feedbackAudio?.Play(cue);
        }

        void Awake()
        {
            if (rigs == null || rigs.Length != OrbitalCampaign.Count) return;
            previousBounceThreshold = Physics.bounceThreshold;
            Physics.bounceThreshold = .01f;
            var input = GetComponent<OrbitalSpatialInput>();
            if (input != null) input.fallbackCamera = previewCamera;
            audioSource = GetComponent<AudioSource>();
            feedbackAudio = GetComponent<OrbitalFeedbackAudio>();
            if (feedbackAudio == null) feedbackAudio = gameObject.AddComponent<OrbitalFeedbackAudio>();
            feedbackAudio.Bind(this,audioSource);
            if (audioSource != null)
            {
                tickSound = Sound(0, 210, .035f);
                aSound = Sound(1, 520, .10f);
                bSound = Sound(2, 780, .12f);
                winSound = Sound(3, 1040, .3f);
            }
            InitializeExperience();
            InitializeReviewSession();
            SelectLevel((OrbitalLevel)(IsReviewSession ? reviewLevel : Profile.ResumeLevel));
            foreach (var rig in rigs)
            {
                var contact = rig.ball.GetComponent<OrbitalBallContactFeedback>();
                if (contact == null) contact = rig.ball.gameObject.AddComponent<OrbitalBallContactFeedback>();
                contact.Bind(this,feedbackAudio);
            }
            foreach (var rig in rigs) if (rig.presentation != null) rig.presentation.Bind(this);
        }

        void FixedUpdate()
        {
            if (!replaying && !applicationSuspended) TickPhysics(Time.fixedDeltaTime);
        }

        void TickPhysics(float delta)
        {
            if (State.Phase != OrbitalPhase.Running || Ball == null) return;
            shotElapsed += delta;
            Ball.AddForce(Rig.root.transform.TransformDirection(Vector3.down) * localGravity, ForceMode.Acceleration);
            Vector3 local = Rig.root.transform.InverseTransformPoint(Ball.position);
            if (Mathf.Abs(local.x) > .55f || Mathf.Abs(local.z) > .43f || local.y < -.08f)
                EndMotion("球离开了轨道。回看最后一段，检查接收口和反射方向。");
            if (Ball.linearVelocity.sqrMagnitude < .000225f) stillTime += delta;
            else stillTime = 0;
            if (stillTime > 2.5f || shotElapsed > 24f)
                EndMotion("球停在了轨道上。当前几何或速度需要调整，可回收后重试。");
            if (frames.Count < 2400) frames.Add(CaptureFrame());
        }

        void Update()
        {
            if (Ball == null || applicationSuspended) return;
            TickReviewLaunch();
            if (replaying)
            {
                replayTime += Mathf.Min(Time.unscaledDeltaTime, .1f) * .5f;
                int index = Mathf.Min((int)(replayTime / Time.fixedDeltaTime), frames.Count - 1);
                Frame frame = frames[index];
                replayFrame = frame;
                Ball.position = frame.position;
                Ball.rotation = frame.rotation;
                UpdateLights(frame.a, frame.b, frame.gate);
                if (index == frames.Count - 1) replaying = false;
            }
            else UpdateLights(State.HasA, State.HasB, State.GateOpen);
            bool legacyTrail = Rig.presentation == null || Rig.presentation.energyEffects == null;
            if (legacyTrail && (State.Phase == OrbitalPhase.Running || replaying))
            {
                recentPositions.Enqueue(Ball.position);
                while (recentPositions.Count > trailDots.Length) recentPositions.Dequeue();
            }
            else if (!legacyTrail) recentPositions.Clear();
            int n = 0;
            foreach (Vector3 point in recentPositions)
            {
                trailDots[n].gameObject.SetActive(true);
                trailDots[n++].position = point;
            }
            for (; n < trailDots.Length; n++) trailDots[n].gameObject.SetActive(false);
            RefreshInterface();
            RefreshExperience();
        }

        Frame CaptureFrame()
        {
            return new Frame { position = Ball.position, rotation = Ball.rotation,
                a = State.HasA, b = State.HasB, c = State.HasC, gate = State.GateOpen, gateCrossed = State.GateCrossed,
                delivered = State.Phase == OrbitalPhase.Success };
        }

        public void Receive(OrbitalEvent signal)
        {
            if (replaying || State.Phase != OrbitalPhase.Running) return;
            if (signal == OrbitalEvent.ClosedGate && State.GateOpen) return;
            bool hadA = State.HasA, hadB = State.HasB, hadGate = State.GateCrossed, hadC = State.HasC;
            if (!State.Apply(signal)) return;
            UpdateLights(State.HasA, State.HasB, State.GateOpen);
            if (!hadA && State.HasA) Play(aSound);
            if (!hadB && State.HasB) Play(bSound);
            if (!hadC && State.HasC) Play(bSound);
            if (!hadGate && State.GateCrossed) feedbackAudio?.Play(OrbitalFeedbackCue.Gate);
            if (State.Phase == OrbitalPhase.Success || State.Phase == OrbitalPhase.Failure)
            {
                frames.Add(CaptureFrame());
                FreezeBall();
                CompleteFlight();
                if (State.Phase == OrbitalPhase.Success)
                {
                    if (ReducedMotion) PlayDeliveryConfirmation();
                }
                else feedbackAudio?.Play(OrbitalFeedbackCue.Failure);
            }
        }

        void EndMotion(string feedback)
        {
            if (State.Phase != OrbitalPhase.Running) return;
            State.Apply(OrbitalEvent.Recovery);
            feedbackAudio?.Play(OrbitalFeedbackCue.Failure);
            motionFeedback = Construction != null
                ? Construction.ExplainFailure(Rig.root.transform.InverseTransformPoint(Ball.position)) : feedback;
            FreezeBall();
            frames.Add(CaptureFrame());
            CompleteFlight();
        }

        public void SelectLevel(OrbitalLevel level)
        {
            if (!OrbitalCampaign.IsValid(level) || rigs == null || (int)level >= rigs.Length) return;
            if (State.Phase == OrbitalPhase.Running || State.Phase == OrbitalPhase.Paused || replaying) return;
            Construction?.CancelManipulations();
            SaveConstructionBlueprint();
            foreach (LevelRig rig in rigs) { rig.ball.isKinematic = true; rig.root.SetActive(false); }
            State.SelectLevel(level);
            StartChallenge();
            Rig.root.SetActive(true);
            if (Assembly != null) Assembly.Clear();
            if (Construction != null) RestoreConstructionBlueprint();
            ResetAttempt();
        }

        public void ResetAttempt()
        {
            deliveryConfirmed = false;
            audioSource?.Stop();
            if (Assembly != null) foreach (var piece in Assembly.pieces) piece.EndManipulation(true);
            Construction?.CancelManipulations();
            Assembly?.SettlePresentation();
            Construction?.SettlePresentation();
            replaying = false;
            State.Reset();
            motionFeedback = "";
            shotElapsed = stillTime = 0;
            frames.Clear();
            recentPositions.Clear();
            FreezeBall();
            Ball.position = Rig.root.transform.TransformPoint(Rig.launchPosition);
            Ball.rotation = Quaternion.identity;
            UpdateConfiguration();
            UpdateLights(false, false, false);
            if (Rig.presentation != null) Rig.presentation.ResetPresentation();
            Physics.SyncTransforms();
        }

        public bool Launch()
        {
            if (replaying || State.Phase == OrbitalPhase.Paused || State.Phase == OrbitalPhase.Running) return false;
            if (!LaunchConfigurationReady)
            {
                motionFeedback = Construction != null
                    ? Construction.HasHeld ? "先放下手中的轨段，再点发射器。" : "先把一块轨段接上接口，再试跑观察球路。"
                    : "先装好 D 道岔和 R 反射模块，再点发射器。";
                assemblyFeedbackUntil = Time.unscaledTime + 4f;
                feedbackAudio?.Play(OrbitalFeedbackCue.Reject);
                return false;
            }
            if (State.Phase != OrbitalPhase.Configuring) ResetAttempt();
            if (!State.Launch()) return false;
            Assembly?.SettlePresentation();
            Construction?.SettlePresentation();
            attempts++;
            hintText = "";
            shotElapsed = stillTime = 0;
            motionFeedback = "";
            frames.Clear();
            recentPositions.Clear();
            Ball.isKinematic = false;
            Ball.WakeUp();
            Ball.linearVelocity = Rig.root.transform.TransformDirection(Rig.launchDirection) * launchSpeed;
            Ball.angularVelocity = Vector3.zero;
            frames.Add(CaptureFrame());
            feedbackAudio?.Play(OrbitalFeedbackCue.Launch);
            Rig.presentation.energyEffects.PlayLaunch();
            return true;
        }

        public void ConfigureRoute(int choice)
        {
            if (replaying || State.Phase == OrbitalPhase.Running || State.Phase == OrbitalPhase.Paused) return;
            if (State.Phase != OrbitalPhase.Configuring) ResetAttempt();
            State.SetRoute(choice);
            if (Assembly != null) Assembly.pieces[0].SetChoice(State.Route);
            UpdateConfiguration();
        }

        public void ConfigureReflector(int choice)
        {
            if (replaying || State.Phase == OrbitalPhase.Running || State.Phase == OrbitalPhase.Paused) return;
            if (State.Phase != OrbitalPhase.Configuring) ResetAttempt();
            State.SetReflector(choice);
            if (Assembly != null) Assembly.pieces[1].SetChoice(State.Reflector);
            UpdateConfiguration();
        }

        public void TogglePause()
        {
            if (replaying || !State.TogglePause()) return;
            if (State.Phase == OrbitalPhase.Paused)
            {
                pausedVelocity = Ball.linearVelocity;
                pausedAngularVelocity = Ball.angularVelocity;
                FreezeBall();
            }
            else
            {
                Ball.isKinematic = false;
                Ball.linearVelocity = pausedVelocity;
                Ball.angularVelocity = pausedAngularVelocity;
            }
        }

        public void Replay()
        {
            if (State.Phase == OrbitalPhase.Running || State.Phase == OrbitalPhase.Paused || frames.Count < 2) return;
            audioSource?.Stop();
            deliveryConfirmed = true;
            if (Assembly != null) foreach (var piece in Assembly.pieces) piece.EndManipulation(true);
            Construction?.CancelManipulations();
            Assembly?.SettlePresentation();
            Construction?.SettlePresentation();
            FreezeBall();
            recentPositions.Clear();
            replaying = true;
            replayTime = 0;
        }

        void OnApplicationPause(bool pause)
        {
            applicationSuspended = pause;
            if (pause && State.Phase == OrbitalPhase.Running) TogglePause();
            if (pause) SaveExperience();
        }

        void OnDestroy()
        {
            if (Application.isPlaying) Physics.bounceThreshold = previousBounceThreshold;
        }

        void FreezeBall()
        {
            if (Ball == null) return;
            if (!Ball.isKinematic) { Ball.linearVelocity = Vector3.zero; Ball.angularVelocity = Vector3.zero; }
            Ball.isKinematic = true;
        }

        public void ActivateControl(OrbitalControlKind kind, int value)
        {
            switch (kind)
            {
                case OrbitalControlKind.Level:
                    if (!OrbitalCampaign.IsValid((OrbitalLevel)value) || !Profile.IsUnlocked((OrbitalLevel)value)) { motionFeedback = "先完成上一航程，解锁下一个任务。"; break; }
                    SelectLevel((OrbitalLevel)value); break;
                case OrbitalControlKind.Route: ConfigureRoute(value); break;
                case OrbitalControlKind.Reflector: ConfigureReflector(value); break;
                case OrbitalControlKind.Launch: Launch(); break;
                case OrbitalControlKind.Reset: ResetAttempt(); break;
                case OrbitalControlKind.Pause: TogglePause(); break;
                case OrbitalControlKind.Replay: Replay(); break;
                case OrbitalControlKind.Continue: ContinueJourney(); break;
                case OrbitalControlKind.Hint: ShowHint(); break;
                case OrbitalControlKind.Trace: Profile.trace = !Profile.trace; SaveExperience(); break;
                case OrbitalControlKind.Audio: Profile.sound = !Profile.sound; SaveExperience(); break;
                case OrbitalControlKind.Settings: preferencesOpen = !preferencesOpen; break;
                case OrbitalControlKind.Comfort:
                    Profile.reducedMotion = !Profile.reducedMotion;
                    foreach (var rig in rigs) rig.presentation?.ApplyMotionPreference();
                    SaveExperience(); break;
            }
        }

        void UpdateConfiguration()
        {
            if (Construction != null) { Construction.RefreshConnections(); return; }
            for (int i = 0; i < 2; i++)
            {
                SetTrack(Rig.routes[i], i == State.Route && (Assembly == null || Assembly.pieces[0].Installed));
                SetTrack(Rig.exits[i], i == State.Reflector && (Assembly == null || Assembly.pieces[1].Installed));
                Rig.reflectors[i].SetActive(i == State.Reflector && (Assembly == null || Assembly.pieces[1].Installed));
            }
        }

        void SetTrack(GameObject track, bool selected)
        {
            if (track == null) return;
            foreach (Collider c in track.GetComponentsInChildren<Collider>()) c.enabled = selected;
            foreach (Renderer r in track.GetComponentsInChildren<Renderer>(true))
            {
                bool plan = r.name == "Plan path";
                r.gameObject.SetActive(plan ? !selected : selected);
                // The visual builder owns each surface material. Selection only
                // changes visibility, preserving trim, joints and inset layers.
            }
        }

        void UpdateLights(bool a, bool b, bool gate)
        {
            bool delivered=replaying ? replayFrame.delivered : State.Phase == OrbitalPhase.Success;
            bool c = DisplayC;
            int signature=(a?1:0)|(b?2:0)|(gate?4:0)|(delivered?8:0)|(c?16:0);
            if(Rig.lastLampState==signature)return;
            Rig.lastLampState=signature;
            foreach (Renderer r in Rig.aLamps)
                r.sharedMaterial = a ? palette[7] : palette[6];
            foreach (Renderer r in Rig.bLamps)
                r.sharedMaterial = b ? palette[7] : palette[6];
            if (Rig.cLamps != null) foreach (Renderer r in Rig.cLamps)
                if (r != null) r.sharedMaterial = c ? palette[7] : palette[6];
            Rig.stationLight.sharedMaterial = delivered ? palette[7] : palette[6];
            if (Rig.gateBlock != null) Rig.gateBlock.SetActive(!gate);
            if (Rig.gateVisual != null)
                Rig.gateVisual.GetComponent<Renderer>().sharedMaterial = gate ? palette[7] : palette[2];
        }

        void RefreshInterface()
        {
            int signature=(int)State.Level | ((int)State.Phase<<3) | (State.Route<<6) | (State.Reflector<<7)
                | (replaying?256:0) | (DisplayA?512:0) | (DisplayB?1024:0) | (DisplayGate?2048:0) | (frames.Count>1?4096:0)
                | (Assembly != null && Assembly.Ready ? 8192 : 0) | (DisplayC?16384:0) | (DisplayGateCrossed?32768:0);
            if(signature==interfaceSignature && interfaceFeedback==State.Feedback && interfaceMotion==motionFeedback)return;
            interfaceSignature=signature;interfaceFeedback=State.Feedback;interfaceMotion=motionFeedback;
            bool moon = OrbitalCampaign.RequiresGate(State.Level);
            titleText.text = ((int)State.Level + 1).ToString("00") + "  " + OrbitalCampaign.Title(State.Level);
            goalText.text = OrbitalCampaign.Objective(State.Level);
            statusText.text = (replaying ? "半速回放" : State.Phase == OrbitalPhase.Running ? "运行中 · 机关已锁定" :
                State.Phase == OrbitalPhase.Paused ? "已暂停 · 点击继续" :
                State.Phase == OrbitalPhase.Success ? "交付完成" :
                State.Phase == OrbitalPhase.Failure ? "已回收 · 可调整零件" : Assembly != null && !Assembly.Ready ? "拼装中 · D / R 接口待完成" : "拼装就绪 · 点发射器试跑")
                + "    A " + ((replaying ? replayFrame.a : State.HasA) ? "●" : "○")
                + "    B " + ((replaying ? replayFrame.b : State.HasB) ? "●" : "○")
                + (moon ? "    G " + ((replaying ? replayFrame.gate : State.GateOpen) ? "已打开" : "未供能") : "");
            feedbackText.text = string.IsNullOrEmpty(motionFeedback) ? (State.Phase == OrbitalPhase.Configuring ?
                "点选零件 → 点同标记接口 → 点已装件转向" : State.Feedback) : motionFeedback;
            bool locked = replaying || State.Phase == OrbitalPhase.Running || State.Phase == OrbitalPhase.Paused;
            foreach (OrbitalControl c in controls)
            {
                bool selected = c.kind == OrbitalControlKind.Route && c.value == State.Route
                    || c.kind == OrbitalControlKind.Reflector && c.value == State.Reflector
                    || c.kind == OrbitalControlKind.Level && c.value == (int)State.Level;
                var controlRenderer = c.GetComponent<Renderer>();
                if (controlRenderer != null) controlRenderer.sharedMaterial = selected ? palette[1] : c.kind == OrbitalControlKind.Launch ? palette[2] : palette[12];
                bool enabled = !locked || c.kind == OrbitalControlKind.Reset || c.kind == OrbitalControlKind.Pause;
                if (c.kind == OrbitalControlKind.Replay) enabled &= frames.Count > 1;
                if (c.kind == OrbitalControlKind.Pause) enabled = !replaying && (State.Phase == OrbitalPhase.Running || State.Phase == OrbitalPhase.Paused);
                c.GetComponent<OrbitalClickTarget>().interactable = enabled;
                var labelLink = c.GetComponent<OrbitalControlLabel>();
                if (labelLink == null) continue;
                var text = labelLink.text;
                text.color = enabled ? (selected || c.kind == OrbitalControlKind.Launch ? new Color(.03f,.08f,.12f) : Color.white) : new Color(.42f,.50f,.56f);
                if (c.kind == OrbitalControlKind.Route) text.text = c.value == 0 ? (moon ? "D  内侧捷径" : "D  短接到站") : (moon ? "D  绕月线" : "D  经过 A");
                if (c.kind == OrbitalControlKind.Reflector) text.text = moon ? (c.value == 0 ? "R  Ⅰ 内折" : "R  Ⅱ 外送") : (c.value == 0 ? "R  Ⅰ" : "R  Ⅱ");
                if (c.kind == OrbitalControlKind.Pause) text.text = State.Phase == OrbitalPhase.Paused ? "继续" : "暂停";
            }
        }

        public void BuildPrototype()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Build in edit mode.");
            if (transform.childCount != 0) throw new InvalidOperationException("Use a new prototype root.");
            rollingSurface = new PhysicsMaterial("Orbital rolling") { dynamicFriction = .08f, staticFriction = .1f,
                bounciness = 0, frictionCombine = PhysicsMaterialCombine.Minimum, bounceCombine = PhysicsMaterialCombine.Minimum };
            bounceSurface = new PhysicsMaterial("Orbital bounce") { dynamicFriction = 0, staticFriction = 0,
                bounciness = .95f, frictionCombine = PhysicsMaterialCombine.Minimum, bounceCombine = PhysicsMaterialCombine.Maximum };
#if UNITY_EDITOR
            rollingSurface = PersistSurface(rollingSurface, "OrbitalRolling");
            bounceSurface = PersistSurface(bounceSurface, "OrbitalBounce");
#endif
            rigs = new LevelRig[OrbitalCampaign.Count];
            for (int i = 0; i < rigs.Length; i++)
            {
                rigs[i] = BuildLevel((OrbitalLevel)i);
                rigs[i].root.SetActive(i == 0);
            }
            BuildInterface();
            // The Spatial Adapter's colliderLayerMask includes Default and UI.
            // Keep physics geometry on Ignore Raycast so native selection reaches
            // the actual pieces, sockets and launcher instead of the track surface.
            foreach (var collider in GetComponentsInChildren<Collider>(true))
                if (collider.GetComponentInParent<OrbitalClickTarget>(true) == null &&
                    collider.GetComponentInParent<OrbitalManipulable>(true) == null)
                    collider.gameObject.layer = 2;
            trailDots = new Transform[24];
            for (int i = 0; i < trailDots.Length; i++)
            {
                GameObject dot = Primitive("Recorded trail", PrimitiveType.Sphere, transform, Vector3.zero,
                    Vector3.one * .004f, palette[2], false);
                dot.SetActive(false);
                trailDots[i] = dot.transform;
            }
            if (GetComponent<OrbitalSpatialInput>() == null) gameObject.AddComponent<OrbitalSpatialInput>();
            if (GetComponent<AudioSource>() == null) gameObject.AddComponent<AudioSource>();
            Ball.position = Rig.root.transform.TransformPoint(Rig.launchPosition);
        }

        LevelRig BuildLevel(OrbitalLevel level)
        {
            if (UsesConstruction(level)) return BuildConstructionLevel(level);
            bool hasGate = OrbitalCampaign.RequiresGate(level);
            bool moon = hasGate || level == OrbitalLevel.Crossing;
            bool reverse = OrbitalCampaign.ReverseTargets(level);
            bool checkpoint = OrbitalCampaign.HasCheckpoint(level);
            bool swapRoutes = level == OrbitalLevel.Crossing || level == OrbitalLevel.Bridge;
            Vector3 P(float x, float y, float z) => CampaignPoint(level,V(x,y,z));
            var rig = new LevelRig();
            rig.root = new GameObject(((int)level+1).ToString("00") + " " + level);
            rig.root.transform.SetParent(transform, false);
            Transform t = rig.root.transform;
            // Present the tabletop toward the viewer; gravity stays in board space.
            t.localRotation=Quaternion.Euler(-22f,0,0);
            t.localPosition=new Vector3(0,-.055f,-.025f);
            OrbitalArtGeometry.BuildChassis(t,palette[3],palette[9],palette[10],palette[13]);
            rig.presentation=rig.root.AddComponent<OrbitalPresentation>();
            rig.presentation.offMaterial=palette[6];rig.presentation.onMaterial=palette[7];rig.presentation.amberMaterial=palette[11];
            if (level == OrbitalLevel.Moon)
            {
                rig.presentation.moon=OrbitalArtGeometry.BuildMoon(t,new Vector3(0,.14f,0),.09f,
                    moonSurface!=null?moonSurface:palette[5],moonCrater!=null?moonCrater:palette[12],palette[10]);
                var moonCollision=new GameObject("Moon collision");moonCollision.transform.SetParent(t,false);
                moonCollision.transform.localPosition=new Vector3(0,.14f,0);
                var sphere=moonCollision.AddComponent<SphereCollider>();sphere.radius=.09f;sphere.contactOffset=.001f;
            }
            Vector3 l, d, a, r, b, s, e, incoming;
            Vector3[] direct, via;
            if (!moon)
            {
                l=P(-34,28,-22); d=P(-18,26,-22); a=P(-18,23,6); r=P(6,20,6);
                b=P(26,14,-8);s=P(26,9,-22);e=P(6,8,-22);
                if (level == OrbitalLevel.Reverse)
                {
                    // A monotonic S approach feeds a long straight into R.
                    // Preserve the final 1:8 pitch of the calibrated contact lane.
                    l=P(-46,31,-22);d=P(-30,29,-22);a=P(-12,22.25f,6);
                }
                incoming=(r-a).normalized;
                Vector3 start=d-(d-l).normalized*.14f;
                Track(t,"Launch",new[]{l,start});
                direct=new[]{start,d,s};
                via=new[]{start,d,a,r};
                if (level == OrbitalLevel.Reverse)
                {
                    // Gentle S bends replace a lossy hairpin. After the final
                    // fillet, about 26 cm of straight physical track removes
                    // lateral approach variation before the reflection.
                    via=new[]{start,d,P(-26,27,-14),P(-26,24.5f,2),P(-22,23.5f,6),a,r};
                }
            }
            else
            {
                l=P(-34,30,-22);d=P(-20,28,-22);a=P(-12,24,18);r=P(32,18,6);
                b=P(-24,8,-4);s=P(8,5,-24);e=P(30,3,-22);
                Vector3 p=P(-32,26,12),j=P(18,24,-16),c=P(18,21,22),q=P(32,19.5f,22);
                incoming=(r-q).normalized;
                Vector3 start=d-(d-l).normalized*.14f;
                Track(t,"Launch",new[]{l,start});
                direct=new[]{start,d,j,c,q,r};
                via=new[]{start,d,p,a,c,q,r};
                if (level == OrbitalLevel.Bridge)
                {
                    // The powered branch leaves the interchange, crosses the
                    // long outer bridge and returns to B before the reflector.
                    via=new[]{start,d,P(-34,26,-6),P(-34,25,20),a,c,q,r};
                }
            }
            rig.routes[swapRoutes?1:0]=Track(t,"Route Shortcut",direct,0,moon?.06f:0);
            rig.routes[swapRoutes?0:1]=Track(t,"Route Via Relay",via,0,.06f);
            Vector3 goal = moon ? P(-12,10,18) : b;
            Vector3 firstOut = ((moon ? goal : e)-r).normalized;
            Vector3 secondOut = ((moon ? e : b)-r).normalized;
            // Vertical faces reflect horizontal travel; gravity handles the downhill exit.
            incoming.y=0; firstOut.y=0; secondOut.y=0;
            int firstIndex = level == OrbitalLevel.Crossing ? 1 : 0;
            int secondIndex = 1-firstIndex;
            rig.reflectors[firstIndex]=OrbitalTrackGeometry.BuildReflector(t,r,incoming,firstOut,palette[2],bounceSurface);
            rig.reflectors[secondIndex]=OrbitalTrackGeometry.BuildReflector(t,r,incoming,secondOut,palette[2],bounceSurface);
            float handedness = CampaignHandedness(level);
            if (!moon) CalibratePlate(rig.reflectors[1],r,7.5f*handedness);
            else
            {
                CalibratePlate(rig.reflectors[firstIndex],r,5f*handedness);
                // Outer setting parks the plate beside the through lane. The ball
                // rolls physically into recovery, without a shallow grazing hit.
                rig.reflectors[secondIndex].transform.localPosition=r+CampaignPoint(level,Vector3.right*.085f);
            }
            foreach (var reflector in rig.reflectors)
                OrbitalArtGeometry.BuildReflectorHousing(reflector.transform,palette[3],palette[8],palette[2]);
            if (moon)
            {
                rig.exits[firstIndex]=Track(t,"Return Inner",new[]{r,goal,b,s},.06f,0,.08f);
                rig.exits[secondIndex]=Track(t,"Return Outer",new[]{r,e},.06f);
            }
            else
            {
                rig.exits[0]=Track(t,"Return Recovery",new[]{r,e},.06f);
                // The longer reverse-order approach has more contact variation.
                // A visibly wider receiver and earlier guards catch that real
                // reflected motion instead of assuming a single ideal ray.
                rig.exits[1]=Track(t,"Return Via Relay",new[]{r,b,s},
                    level == OrbitalLevel.Reverse ? .035f : .06f,0,
                    level == OrbitalLevel.Reverse ? .095f : .055f);
            }
            Vector3 targetA = reverse ? b : a;
            Vector3 targetB = reverse ? a : checkpoint ? P(25,20.25f,22) : b;
            if (hasGate)
            {
                Vector3 tangent=(b-goal).normalized;
                rig.gateBlock=Primitive("Gate G blocking bar",PrimitiveType.Cube,t,goal+Vector3.up*.015f,
                    new Vector3(.08f,.05f,.009f),palette[2],true);
                rig.gateBlock.transform.localRotation=Quaternion.LookRotation(tangent,Vector3.up);
                rig.gateVisual=Primitive("Gate G power indicator",PrimitiveType.Cube,t,goal+Vector3.up*.065f,
                    new Vector3(.085f,.012f,.012f),palette[2],false);
                Sensor(t,"Gate closed sensor",goal-tangent*.035f,OrbitalEvent.ClosedGate,.026f);
                Sensor(t,"Gate crossed",goal+tangent*.035f,OrbitalEvent.ReturnGate,.025f);
                BuildGateHousing(t,goal,tangent);
                if(checkpoint)
                    LowerRouteMarker(t,"G",goal+Vector3.up*.060f,goal+new Vector3(0,.060f,-.160f),palette[2]);
                else
                    SceneMarker(t,"G",goal+CampaignPoint(level,new Vector3(-.180f,.200f,-.230f)),palette[2]);
                Cable(t,(reverse?targetB:targetA)+Vector3.up*.025f,goal+Vector3.up*.06f);
                if(checkpoint) Cable(t,targetB+Vector3.up*.025f,goal+Vector3.up*.06f);
            }
            if (checkpoint)
            {
                rig.cLight=Ring(t,"C",b,palette[6]);
                var cRenderers=new List<Renderer>(rig.cLight.transform.parent.GetComponentsInChildren<Renderer>());
                var cTarget=OrbitalArtGeometry.BuildTarget(t,b,palette[9],palette[8],palette[6]);
                cRenderers.AddRange(cTarget.Find("Illumination").GetComponentsInChildren<Renderer>());
                rig.cLamps=cRenderers.ToArray();
                Sensor(t,"Checkpoint C",b,OrbitalEvent.CheckpointC,.041f);
                LowerRouteMarker(t,"C",b+new Vector3(.028f,.038f,-.006f),b+new Vector3(.190f,.060f,0),palette[2]);
            }
            rig.aLight=Ring(t,"A",targetA,palette[6]);
            rig.bLight=Ring(t,"B",targetB,palette[6]);
            rig.aLamps=rig.aLight.transform.parent.GetComponentsInChildren<Renderer>();
            rig.bLamps=rig.bLight.transform.parent.GetComponentsInChildren<Renderer>();
            var aTarget=OrbitalArtGeometry.BuildTarget(t,targetA,palette[9],palette[8],palette[6]);
            var bTarget=OrbitalArtGeometry.BuildTarget(t,targetB,palette[9],palette[8],palette[6]);
            var aRenderers=new List<Renderer>(rig.aLamps);aRenderers.AddRange(aTarget.Find("Illumination").GetComponentsInChildren<Renderer>());rig.aLamps=aRenderers.ToArray();
            var bRenderers=new List<Renderer>(rig.bLamps);bRenderers.AddRange(bTarget.Find("Illumination").GetComponentsInChildren<Renderer>());rig.bLamps=bRenderers.ToArray();
            var launcher = OrbitalArtGeometry.BuildLauncher(t,l,palette[3],palette[8],palette[11]);
            Vector3 launchFacing=d-l;launchFacing.y=0;
            launcher.localRotation=Quaternion.LookRotation(launchFacing,Vector3.up);
            var launchCollider = launcher.gameObject.AddComponent<BoxCollider>();
            launchCollider.center = new Vector3(-.012f,.025f,0); launchCollider.size = new Vector3(.12f,.10f,.12f); launchCollider.isTrigger = true;
            launcher.gameObject.AddComponent<OrbitalClickTarget>();
            var launchControl = launcher.gameObject.AddComponent<OrbitalControl>();
            launchControl.owner = this; launchControl.kind = OrbitalControlKind.Launch;
            buildControls.Add(launchControl);
            SceneMarker(t,"发射",l+new Vector3(-.070f,.048f,-.025f),palette[2],.074f);
            var station=OrbitalArtGeometry.BuildStation(t,s,palette[3],palette[8],palette[6]);
            if (level == OrbitalLevel.Crossing || level == OrbitalLevel.Bridge || level == OrbitalLevel.Finale)
                station.localRotation=Quaternion.Euler(0,180,0);
            if(stationVisualPrefab!=null) OrbitalStationVisual.Attach(station,stationVisualPrefab);
            rig.stationLight=Primitive("Station core",PrimitiveType.Sphere,t,s+Vector3.up*.03f,
                Vector3.one*.018f,palette[6],false).GetComponent<Renderer>();
            var arrivalRing=Ring(t,"Dock pulse",s+Vector3.up*.012f,palette[10]);
            arrivalRing.transform.parent.localRotation=Quaternion.Euler(90,0,0);
            rig.presentation.pulseRings=new[]{rig.aLight.transform.parent,rig.bLight.transform.parent,
                rig.gateVisual!=null?rig.gateVisual.transform:null,arrivalRing.transform.parent};
            if (rig.cLight != null)
                rig.presentation.pulseRings=new[]{rig.aLight.transform.parent,rig.bLight.transform.parent,
                    rig.gateVisual!=null?rig.gateVisual.transform:null,arrivalRing.transform.parent,rig.cLight.transform.parent};
            var stationRenderers=new List<Renderer>(arrivalRing.transform.parent.GetComponentsInChildren<Renderer>());
            stationRenderers.AddRange(station.Find("Illumination").GetComponentsInChildren<Renderer>());
            rig.presentation.stationLamps=stationRenderers.ToArray();
            Primitive("Station dock",PrimitiveType.Cylinder,t,s-Vector3.up*.018f,new Vector3(.09f,.01f,.09f),palette[9],false);
            Primitive("Recovery cup",PrimitiveType.Cylinder,t,e-Vector3.up*.021f,new Vector3(.081f,.006f,.081f),palette[3],false);
            OrbitalArtGeometry.Ring(t,"Recovery rim",e-Vector3.up*.014f,.037f,.0025f,palette[9],Quaternion.Euler(90,0,0),32,6);
            Sensor(t,"Target A",targetA,OrbitalEvent.TargetA,.041f);
            Sensor(t,"Target B",targetB,OrbitalEvent.TargetB,.041f);
            Sensor(t,"Dock S",s,OrbitalEvent.Dock,.038f);
            Sensor(t,"Recovery E",e,OrbitalEvent.Recovery,.04f);
            SceneMarker(t,"A",targetA+Vector3.up*.066f,palette[1]);
            SceneMarker(t,"B",targetB+Vector3.up*.066f,palette[1]);
            SceneMarker(t,"S",s+new Vector3(.062f,.074f,0),palette[1]);
            SceneMarker(t,"回收",e+new Vector3(0,.024f,-.024f),palette[9],.074f);
            rig.assembly = BuildAssembly(t, d, r, level);
            rig.assembly.pieces[1].initialChoice=OrbitalCampaign.InitialReflector(level);
            GameObject ball=Primitive("Energy ball",PrimitiveType.Sphere,t,l,Vector3.one*.022f,palette[11],true);
            rig.ball=ball.AddComponent<Rigidbody>();
            rig.ball.mass=.045f;rig.ball.useGravity=false;rig.ball.isKinematic=true;
            rig.ball.linearDamping=.025f;rig.ball.angularDamping=.015f;
            rig.ball.interpolation=RigidbodyInterpolation.Interpolate;
            rig.ball.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
            ball.GetComponent<Collider>().sharedMaterial=rollingSurface;
            var shell=new GameObject("Energy shell");shell.transform.SetParent(ball.transform,false);
            shell.AddComponent<MeshFilter>().sharedMesh=ball.GetComponent<MeshFilter>().sharedMesh;
            shell.AddComponent<MeshRenderer>().sharedMaterial=ball.GetComponent<Renderer>().sharedMaterial;
            DestroyImmediate(ball.GetComponent<MeshFilter>());DestroyImmediate(ball.GetComponent<MeshRenderer>());
            rig.presentation.ballVisual=shell.transform;
            rig.presentation.targetA=rig.aLamps;rig.presentation.targetB=rig.bLamps;
            BuildCampaignLandmarks(t,level);
            rig.launchPosition=l;rig.launchDirection=(d-l).normalized;
            return rig;
        }

        GameObject Track(Transform parent,string name,Vector3[] points,float unguardedStart=0,float unguardedEnd=0,float width=.055f)
        {
            var centerline=OrbitalTrackGeometry.SmoothPolyline(points,.055f,8);
            var track=OrbitalTrackGeometry.BuildTrack(parent,name,centerline,
                palette[0],palette[1],rollingSurface,width,unguardedStart:unguardedStart,unguardedEnd:unguardedEnd);
            OrbitalTrackVisuals.Build(track,centerline,width,unguardedStart,unguardedEnd,palette[0],palette[14],palette[10],palette[9]);
            return track;
        }

        static Vector3 V(float x, float y, float z) => new Vector3(x, y, z) * .01f;

        static void CalibratePlate(GameObject plate, Vector3 point, float yaw)
        {
            Vector3 normal=Quaternion.AngleAxis(yaw,Vector3.up)*plate.transform.localRotation*Vector3.forward;
            plate.transform.localRotation=Quaternion.LookRotation(normal,Vector3.up);
            plate.transform.localPosition=point+normal*.015f;
        }

        Renderer Ring(Transform parent,string name,Vector3 pos,Material mat)
        {
            GameObject hub=new GameObject("Ring "+name);hub.transform.SetParent(parent,false);
            hub.transform.localPosition=pos+Vector3.up*.012f;
            var ring=OrbitalArtGeometry.Ring(hub.transform,"Signal bezel",Vector3.zero,
                .027f,.0018f,mat,Quaternion.identity,32,6);
            return ring.GetComponent<Renderer>();
        }

        void Sensor(Transform parent,string name,Vector3 point,OrbitalEvent signal,float radius)
        {
            var go=new GameObject(name);go.transform.SetParent(parent,false);go.transform.localPosition=point;
            var trigger=go.AddComponent<SphereCollider>();trigger.radius=radius;trigger.isTrigger=true;
            var sensor=go.AddComponent<OrbitalSensor>();sensor.owner=this;sensor.signal=signal;
        }

        void Cable(Transform parent,Vector3 a,Vector3 b, bool subdued=false)
        {
            var go=Primitive("A to G power cable",PrimitiveType.Cylinder,parent,(a+b)*.5f,
                new Vector3(subdued?.002f:.004f,(a-b).magnitude*.5f,subdued?.002f:.004f),palette[subdued?9:2],false);
            go.transform.localRotation=Quaternion.FromToRotation(Vector3.up,b-a);
        }

        OrbitalAssemblyRig BuildAssembly(Transform parent, Vector3 routePoint, Vector3 reflectorPoint, OrbitalLevel level)
        {
            bool moon = OrbitalCampaign.RequiresGate(level) || level == OrbitalLevel.Crossing;
            var kit = parent.gameObject.AddComponent<OrbitalAssemblyRig>();
            kit.owner = this;
            kit.pieces = new OrbitalAssemblyPiece[2];
            kit.sockets = new OrbitalAssemblySocket[2];
            var tray = OrbitalAssemblyArt.BuildTray(parent, palette);
            tray.localPosition = new Vector3(-.02f, .07f, -.43f);
            for (int i = 0; i < 2; i++)
            {
                Vector3 slot = (i == 0 ? routePoint : reflectorPoint) - Vector3.up * .0325f;
                var socketPalette = (Material[])palette.Clone();
                if (i == 1) socketPalette[10] = palette[2];
                var socketArt = OrbitalAssemblyArt.BuildSocket(parent, socketPalette);
                socketArt.localPosition = slot;
                float routeYaw = level == OrbitalLevel.Crossing || level == OrbitalLevel.Bridge ? 0f :
                    level == OrbitalLevel.Finale ? 270f : 90f;
                socketArt.localRotation = Quaternion.Euler(0, i == 0 ? routeYaw : moon ? 180 : 90, 0);
                var socketCollider = socketArt.gameObject.AddComponent<BoxCollider>();
                socketCollider.size = new Vector3(.126f, .10f, .126f);
                socketCollider.center = new Vector3(0, .04f, 0); socketCollider.isTrigger = true;
                socketArt.gameObject.AddComponent<OrbitalClickTarget>();
                var socket = socketArt.gameObject.AddComponent<OrbitalAssemblySocket>();
                socket.rig = kit; socket.index = i; socket.selectionHalo = socketArt.Find("SelectionHalo");
                kit.sockets[i] = socket;
                var slotMarker = SceneMarker(socketArt,i == 0 ? "D" : "R",new Vector3(0,.058f,-.047f),i == 0 ? palette[1] : palette[2]);
                slotMarker.localRotation=Quaternion.Inverse(socketArt.localRotation)*Quaternion.Euler(22,0,0);
                var pieceArt = OrbitalAssemblyArt.BuildPiece(parent, i == 1, palette);
                pieceArt.localRotation = socketArt.localRotation;
                var piece = pieceArt.gameObject.AddComponent<OrbitalAssemblyPiece>();
                piece.rig = kit; piece.index = i;
                piece.alternateYaw = i == 0 && level == OrbitalLevel.Reverse ? 90f : -90f;
                piece.homePosition = tray.localPosition + new Vector3(i == 0 ? -.075f : .075f, .004f, 0);
                piece.socketPosition = slot; piece.initialChoice = i == 1 && moon ? 1 : 0;
                piece.directionRotor = pieceArt.Find("DirectionRotor"); piece.selectionHalo = pieceArt.Find("SelectionHalo");
                piece.ResetToTray();
                var pieceCollider = pieceArt.gameObject.AddComponent<BoxCollider>();
                pieceCollider.center = new Vector3(0, .035f, 0); pieceCollider.size = new Vector3(.112f, .08f, .112f);
                pieceCollider.isTrigger = true;
                kit.pieces[i] = piece;
                // The badge travels with the physical part, including during dragging.
                var badge = Label(pieceArt, i == 0 ? "D" : "R", new Vector3(0, .058f, -.035f), .05f, .026f);
                badge.transform.localRotation = Quaternion.Inverse(pieceArt.localRotation);
            }
            return kit;
        }

        void BuildInterface()
        {
            BuildJourneyInterface();
        }

        void SmallControl(Transform parent, string text, Vector3 point, OrbitalControlKind kind, int value)
        {
            GameObject go = Primitive(text, PrimitiveType.Cube, parent, point, new Vector3(.13f, .038f, .015f), palette[12], true);
            go.GetComponent<Collider>().isTrigger = true;
            go.AddComponent<OrbitalClickTarget>();
            var c = go.AddComponent<OrbitalControl>(); c.owner = this; c.kind = kind; c.value = value; buildControls.Add(c);
            var label = Label(parent, text, point - Vector3.forward * .010f, .12f, .029f);
            go.AddComponent<OrbitalControlLabel>().text = label;
        }

        TextMeshPro Label(Transform parent,string text,Vector3 point,float width,float height)
        {
            GameObject go=new GameObject(text);go.transform.SetParent(parent,false);go.transform.localPosition=point;
            var tmp=go.AddComponent<TextMeshPro>();tmp.font=uiFont;tmp.text=text;
            tmp.rectTransform.sizeDelta=new Vector2(width,height);
            tmp.enableAutoSizing=true;tmp.fontSizeMin=.1f;tmp.fontSizeMax=5f;
            tmp.alignment=TextAlignmentOptions.Center;tmp.color=new Color(.94f,.97f,1);
            tmp.textWrappingMode=TextWrappingModes.Normal;
            return tmp;
        }

        GameObject Primitive(string name,PrimitiveType type,Transform parent,Vector3 point,Vector3 size,Material mat,bool collider)
        {
            var go=GameObject.CreatePrimitive(type);go.name=name;go.transform.SetParent(parent,false);
            go.transform.localPosition=point;go.transform.localScale=size;
            go.GetComponent<Renderer>().sharedMaterial=mat;
            go.GetComponent<Collider>().contactOffset=.001f;
            if(!collider)
            {
                var c=go.GetComponent<Collider>();
                if(Application.isPlaying) Destroy(c);else DestroyImmediate(c);
            }
            return go;
        }

        AudioClip Tone(float hz,float seconds)
        {
            int count=(int)(24000*seconds);var data=new float[count];
            for(int i=0;i<count;i++)data[i]=Mathf.Sin(2*Mathf.PI*hz*i/24000)*(.12f*(1-i/(float)count));
            var clip=AudioClip.Create("Orbital tone",count,1,24000,false);clip.SetData(data,0);return clip;
        }
        void Play(AudioClip clip) { if(Profile.sound&&audioSource!=null&&clip!=null)audioSource.PlayOneShot(clip); }
        AudioClip Sound(int index, float hz, float seconds) => eventSounds != null && index < eventSounds.Length && eventSounds[index] != null
            ? eventSounds[index] : Tone(hz, seconds);

#if UNITY_EDITOR
        public string RunConstructionProbe()
        {
            if (!Application.isPlaying || Construction == null) return "Construction Play mode required";
            ResetAttempt();
            var previous = Physics.simulationMode;
            var interpolation = Ball.interpolation;
            Physics.simulationMode = SimulationMode.Script;
            Ball.interpolation = RigidbodyInterpolation.None;
            try
            {
                if (!Launch()) return State.Level + " launch_rejected " + motionFeedback;
                for (int i = 0; i < 1500 && State.Phase == OrbitalPhase.Running; i++)
                { TickPhysics(.02f); Physics.Simulate(.02f); }
                return State.Level + " result=" + State.ResultCode + " pos=" + Ball.position.ToString("F3") +
                    " elapsed=" + shotElapsed.ToString("F2") + " A=" + State.HasA + " B=" + State.HasB +
                    " G=" + State.GateCrossed + " C=" + State.HasC + " " + motionFeedback;
            }
            finally { Physics.simulationMode = previous; Ball.interpolation = interpolation; }
        }

        static PhysicsMaterial PersistSurface(PhysicsMaterial surface, string name)
        {
            string path = "Assets/SpaceBall/Materials/" + name + ".physicMaterial";
            var existing = UnityEditor.AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
            if (existing != null)
            {
                UnityEditor.EditorUtility.CopySerialized(surface, existing);
                DestroyImmediate(surface);
                UnityEditor.EditorUtility.SetDirty(existing);
                return existing;
            }
            UnityEditor.AssetDatabase.CreateAsset(surface, path);
            return surface;
        }

        public string RunPhysicsProbe(OrbitalLevel level,int route,int reflector)
        {
            if(!Application.isPlaying) return "Play mode required";
            ResetAttempt();SelectLevel(level);
            Assembly.Place(0);Assembly.Place(1);ConfigureRoute(route);ConfigureReflector(reflector);
            SimulationMode previous=Physics.simulationMode;Physics.simulationMode=SimulationMode.Script;
            bool interpolation=Ball.interpolation!=RigidbodyInterpolation.None;
            Ball.interpolation=RigidbodyInterpolation.None;
            try
            {
                Launch();
                for(int i=0;i<1500&&State.Phase==OrbitalPhase.Running;i++)
                { TickPhysics(.02f);Physics.Simulate(.02f); }
                Vector3 pos=Ball.position;
                return level+" route="+route+" R="+reflector+" result="+State.ResultCode+" "+motionFeedback+
                    " pos="+pos.ToString("F3")+" elapsed="+shotElapsed.ToString("F2")+" A="+State.HasA+" B="+State.HasB+" G="+State.GateCrossed+" C="+State.HasC;
            }
            finally { Physics.simulationMode=previous;Ball.interpolation=interpolation?RigidbodyInterpolation.Interpolate:RigidbodyInterpolation.None; }
        }
#endif
    }
}

