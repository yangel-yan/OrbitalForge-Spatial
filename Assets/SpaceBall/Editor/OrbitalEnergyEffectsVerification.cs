using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using ByteDance.PICO.SpatialAdapter;
using UnityEditor;
using UnityEngine;

namespace SpaceBall.Editor
{
    /// <summary>
    /// Deterministic visual-state contract checks. Synthetic state transitions and
    /// sampled ball poses here are not native input or physical-flight evidence.
    /// </summary>
    [InitializeOnLoad]
    public static partial class OrbitalEnergyEffectsVerification
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static Session previewSession;
        static OrbitalPrototype previewGame;

        static OrbitalEnergyEffectsVerification()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingPlayMode && previewSession != null) EndPreview();
            };
            AssemblyReloadEvents.beforeAssemblyReload += () => { if (previewSession != null) EndPreview(); };
        }

        [Serializable]
        sealed class PreviewReport
        {
            public string status, level, phase;
            public bool launched, protectedPlayerData;
            public string method = "Isolated temporary profile; reference parts are physically arranged, then normal Rigidbody/sensor gameplay runs. No sensor events are injected.";
        }

        public static string BeginPreview(OrbitalLevel level)
        {
            if (!Application.isPlaying || previewSession != null)
                throw new InvalidOperationException("Begin a single protected preview in Play mode.");
            if (!OrbitalCampaign.IsValid(level)) throw new ArgumentOutOfRangeException(nameof(level));
            var game = UnityEngine.Object.FindFirstObjectByType<OrbitalPrototype>();
            RequireIdle(game);
            previewSession = new Session(game); previewGame = game;
            try
            {
                game.Profile.sound = true; game.Profile.reducedMotion = false; game.Profile.trace = false;
                game.SelectLevel(level); game.ResetAttempt();
                if (game.Construction != null)
                {
                    var arrange = typeof(OrbitalPrototype).GetMethod("ApplyConstructionReferenceSolution", BindingFlags.Instance | BindingFlags.Public);
                    if (arrange == null || !(bool)arrange.Invoke(game, new object[] { level }))
                        throw new InvalidOperationException("Could not arrange the reference physical track.");
                }
                else
                {
                    game.Assembly.Place(0); game.Assembly.Place(1);
                    game.ConfigureRoute(OrbitalCampaign.CorrectRoute(level));
                    game.ConfigureReflector(OrbitalCampaign.CorrectReflector(level));
                    game.Assembly.SettlePresentation();
                }
                return JsonUtility.ToJson(new PreviewReport { status = "ready", level = level.ToString(), phase = game.State.Phase.ToString(), protectedPlayerData = true }, true);
            }
            catch { EndPreview(); throw; }
        }

        public static string LaunchPreview()
        {
            if (previewSession == null || previewGame == null)
                throw new InvalidOperationException("BeginPreview must protect the player data before launching.");
            bool launched = previewGame.Launch();
            if (!launched) throw new InvalidOperationException("The arranged preview could not launch.");
            return JsonUtility.ToJson(new PreviewReport { status = "launched", level = previewGame.State.Level.ToString(), phase = previewGame.State.Phase.ToString(), launched = true, protectedPlayerData = true }, true);
        }

        public static string EndPreview()
        {
            if (previewSession == null) return JsonUtility.ToJson(new PreviewReport { status = "idle", protectedPlayerData = true });
            var session = previewSession; var game = previewGame;
            previewSession = null; previewGame = null;
            session.Dispose();
            if (!session.PlayerKeysMatch) throw new InvalidOperationException("Protected preview did not restore both player save keys.");
            return JsonUtility.ToJson(new PreviewReport { status = "restored", level = game != null ? game.State.Level.ToString() : "", phase = game != null ? game.State.Phase.ToString() : "", protectedPlayerData = true }, true);
        }

        [Serializable]
        sealed class Report
        {
            public string status = "passed", failure = "";
            public string method = "Editor VFX contract: synthetic accepted/rejected run-state events and sampled Rigidbody poses; no physical sensor or native input claim";
            public int assertions, levels, acceptedTargetBursts, successSequences;
            public string[] scenarios;
        }

        sealed class Checks
        {
            public int count;
            public readonly List<string> scenarios = new();
            public void Require(bool condition, string message)
            {
                count++;
                if (!condition) throw new InvalidOperationException("Energy VFX verification: " + message);
            }
        }

        readonly struct SavedString
        {
            readonly string key, value;
            readonly bool existed;
            public SavedString(string key) { this.key = key; existed = PlayerPrefs.HasKey(key); value = PlayerPrefs.GetString(key, ""); }
            public void Restore() { if (existed) PlayerPrefs.SetString(key, value); else PlayerPrefs.DeleteKey(key); }
            public bool Matches() => PlayerPrefs.HasKey(key) == existed && PlayerPrefs.GetString(key, "") == value;
            public RecoveryKey Recovery() => new RecoveryKey { key = key, existed = existed, value = value };
        }

        [Serializable]
        sealed class RecoveryKey { public string key, value; public bool existed; }
        [Serializable]
        sealed class RecoverySnapshot { public string savedAtUtc, level, profile; public RecoveryKey main, backup; }

        sealed class Session : IDisposable
        {
            readonly OrbitalPrototype game;
            readonly object profile;
            readonly OrbitalLevel level;
            readonly bool preferences, suspended;
            readonly int selected, route, reflector;
            readonly int[] slots, turns, choices;
            readonly bool[] installed;
            readonly SavedString main, backup;
            public bool PlayerKeysMatch => main.Matches() && backup.Matches();

            public Session(OrbitalPrototype game)
            {
                this.game = game; level = game.State.Level;
                profile = Field("progress").GetValue(game);
                preferences = (bool)Field("preferencesOpen").GetValue(game);
                suspended = (bool)Field("applicationSuspended").GetValue(game);
                route = game.State.Route; reflector = game.State.Reflector;
                main = new SavedString(OrbitalProgress.StorageKey);
                backup = new SavedString(OrbitalProgress.StorageKey + ".backup");
                string folder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Builds", "SpaceBall", "Effects-v042");
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "journey-before-vfx-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff") + ".json"),
                    JsonUtility.ToJson(new RecoverySnapshot { savedAtUtc = DateTime.UtcNow.ToString("O"), level = level.ToString(),
                        profile = JsonUtility.ToJson(game.Profile), main = main.Recovery(), backup = backup.Recovery() }, true));
                if (game.Construction != null)
                {
                    var kit = game.Construction; selected = kit.selected;
                    slots = new int[kit.tiles.Length]; turns = new int[kit.tiles.Length];
                    for (int i = 0; i < slots.Length; i++) { slots[i] = kit.tiles[i].SocketIndex; turns[i] = kit.tiles[i].QuarterTurns; }
                }
                else if (game.Assembly != null)
                {
                    var rig = game.Assembly; selected = rig.selected;
                    choices = new int[rig.pieces.Length]; installed = new bool[rig.pieces.Length];
                    for (int i = 0; i < choices.Length; i++) { choices[i] = rig.pieces[i].Choice; installed[i] = rig.pieces[i].Installed; }
                }
                Field("progress").SetValue(game, new OrbitalProgress { sound = false, reducedMotion = false, trace = false });
                Field("preferencesOpen").SetValue(game, false);
                Call(game, "OnApplicationPause", false);
                game.ResetAttempt();
            }

            public void Dispose()
            {
                try
                {
                    if (game == null) return;
                    Field("replaying").SetValue(game, false);
                    Call(game, "OnApplicationPause", false);
                    for (int i = 0; i < OrbitalCampaign.Count; i++)
                    {
                        game.ResetAttempt(); game.SelectLevel((OrbitalLevel)i);
                        game.Assembly?.Clear(); game.Construction?.Clear();
                    }
                    game.SelectLevel(level); game.ResetAttempt();
                }
                finally
                {
                    try
                    {
                        if (game != null)
                        {
                            Field("progress").SetValue(game, profile);
                            Field("preferencesOpen").SetValue(game, preferences);
                            Field("applicationSuspended").SetValue(game, suspended);
                            if (game.Construction != null && slots != null)
                            {
                                var kit = game.Construction;
                                foreach (var tile in kit.tiles) tile.SetPlacement(-1, tile.QuarterTurns);
                                for (int i = 0; i < slots.Length; i++) kit.tiles[i].SetPlacement(slots[i], turns[i]);
                                kit.selected = selected; kit.RefreshConnections(); kit.UpdateHighlights();
                            }
                            if (game.Assembly != null && choices != null)
                            {
                                for (int i = 0; i < choices.Length; i++)
                                {
                                    var piece = game.Assembly.pieces[i];
                                    piece.SetChoice(choices[i]); piece.SetInstalled(installed[i]); piece.SettlePresentation();
                                }
                                game.Assembly.selected = selected; game.Assembly.UpdateHighlights();
                                game.State.SetRoute(route); game.State.SetReflector(reflector);
                                Call(game, "UpdateConfiguration");
                            }
                            foreach (var effects in game.GetComponentsInChildren<OrbitalEnergyEffects>(true)) effects.ResetEffects();
                        }
                    }
                    finally { main.Restore(); backup.Restore(); PlayerPrefs.Save(); }
                    if (game != null) Call(game, "RefreshExperience");
                }
            }
        }

        struct BodyPose
        {
            public Rigidbody body;
            public Vector3 position, scale, velocity, angularVelocity;
            public Quaternion rotation;
            public bool kinematic;
        }

        struct FixedPose { public Collider collider; public Matrix4x4 matrix; }

        public static string Run()
        {
            if (!Application.isPlaying) throw new InvalidOperationException("Run VFX verification in Play mode.");
            if (previewSession != null) throw new InvalidOperationException("End the protected preview before running VFX contract checks.");
            var game = UnityEngine.Object.FindFirstObjectByType<OrbitalPrototype>();
            var camera = UnityEngine.Object.FindFirstObjectByType<SpatialCamera>();
            if (game == null || camera == null) throw new InvalidOperationException("The chapter and spatial camera must be loaded.");
            RequireIdle(game);

            var report = new Report(); var checks = new Checks();
            var main = new SavedString(OrbitalProgress.StorageKey); var backup = new SavedString(OrbitalProgress.StorageKey + ".backup");
            string originalProfile = JsonUtility.ToJson(game.Profile);
            try
            {
                var all = game.GetComponentsInChildren<OrbitalEnergyEffects>(true);
                checks.Require(all.Length == OrbitalCampaign.Count, "all six boards contain authored VFX pools");
                using (new Session(game))
                {
                    foreach (var effects in all)
                    {
                        checks.Require(effects.GetComponentsInChildren<Collider>(true).Length == 0 && effects.GetComponentsInChildren<Rigidbody>(true).Length == 0,
                            "VFX pools do not contain physical components");
                        RequireBounds(effects.GetMaximumBoundsInWorld(), camera, checks, "complete dynamic VFX envelope");
                    }
                    OrbitalEnergyEffects previous = null;
                    for (int level = 0; level < OrbitalCampaign.Count; level++)
                    {
                        if (level == 0) game.ResetAttempt();
                        game.SelectLevel((OrbitalLevel)level);
                        game.Profile.reducedMotion = false;
                        var active = game.GetComponentsInChildren<OrbitalEnergyEffects>();
                        checks.Require(active.Length == 1, "exactly one level VFX pool is active");
                        var effects = active[0]; effects.Bind(game); effects.ResetEffects();
                        if (previous != null) RequireQuiet(previous, checks, "level switch clears previous effects");
                        VerifyCeremony(game, effects, camera, checks);
                        VerifyLevel(game, effects, camera, checks, report);
                        previous = effects; report.levels++;
                    }
                    game.SelectLevel(OrbitalLevel.Tutorial);
                    RequireQuiet(previous, checks, "leaving the last successful board clears its receiver sequence");
                }
                checks.Require(main.Matches() && backup.Matches(), "both player save keys are restored exactly");
                checks.Require(JsonUtility.ToJson(game.Profile) == originalProfile, "the player's original profile remains unchanged");
                checks.scenarios.Add("All six boards: collisionless pools, complete dynamic envelope, accepted-only hits, duplicate suppression and success-only docking");
                checks.scenarios.Add("Curved sampled ball positions, 12 cm maximum trail, stopped/reduced/replay/reset cleanup, pause progression and fixed mesh pool");
                checks.scenarios.Add("Rigidbody poses/velocities/scale and fixed collider transforms are unaffected; main/backup/profile and current draft restored");
            }
            catch (Exception exception) { report.status = "failed"; report.failure = exception.Message; throw; }
            finally
            {
                report.assertions = checks.count; report.scenarios = checks.scenarios.ToArray();
                string folder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Builds", "SpaceBall", "Effects-v042");
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "editor-vfx-verification.json"), JsonUtility.ToJson(report, true));
            }
            return JsonUtility.ToJson(report, true);
        }

        static void VerifyLevel(OrbitalPrototype game, OrbitalEnergyEffects effects, SpatialCamera camera, Checks checks, Report report)
        {
            int meshCount = effects.GetComponentsInChildren<MeshFilter>(true).Length;
            var fixedPoses = new List<FixedPose>();
            foreach (var collider in game.GetComponentsInChildren<Collider>(true))
                if (collider.attachedRigidbody == null) fixedPoses.Add(new FixedPose { collider = collider, matrix = collider.transform.localToWorldMatrix });
            effects.PlayEvent(OrbitalEvent.TargetA); effects.PlayEvent(OrbitalEvent.Dock);
            RequireQuiet(effects, checks, "configuration cannot celebrate a hit or delivery");
            checks.Require(effects.AcceptedBurstCount == 0 && effects.DockSequenceCount == 0, "configuration does not consume an effect event");

            OrbitalEvent[] sequence = Sequence(game.State.Level);
            checks.Require(game.State.Launch(), "synthetic VFX run starts");
            Tick(game, effects, .02f, checks);
            effects.PlayEvent(sequence[0]);
            checks.Require(effects.AcceptedBurstCount == 0, "missing state flag cannot emit a target burst");
            VerifyTrail(game, effects, camera, checks);

            checks.Require(game.State.Apply(sequence[0]) && game.State.Phase == OrbitalPhase.Running, "first target accepted by the run-state model");
            effects.PlayEvent(sequence[0]);
            checks.Require(effects.AcceptedBurstCount == 1 && effects.ActiveBurstCount > 0, "accepted target starts a visible burst");
            effects.PlayEvent(sequence[0]);
            checks.Require(effects.AcceptedBurstCount == 1, "duplicate target does not create another burst");
            Tick(game, effects, .04f, checks); RequireActiveMeshesInside(effects, camera, checks);
            checks.Require(game.State.TogglePause(), "synthetic run pauses");
            Tick(game, effects, .2f, checks);
            checks.Require(effects.ActiveTrailSegments == 0, "pause hides the moving trail");
            var paused = CaptureDecorations(effects);
            effects.PlayEvent(OrbitalEvent.Dock); Tick(game, effects, .5f, checks);
            RequireDecorations(paused, checks, "paused VFX do not keep advancing");
            checks.Require(effects.AcceptedBurstCount == 1 && effects.DockSequenceCount == 0, "paused events do not celebrate");
            checks.Require(game.State.TogglePause(), "synthetic run resumes");
            Tick(game, effects, .02f, checks);

            int accepted = 1;
            for (int i = 1; i < sequence.Length - 1; i++)
            {
                checks.Require(game.State.Apply(sequence[i]) && game.State.Phase == OrbitalPhase.Running, "next objective accepted by the run-state model");
                effects.PlayEvent(sequence[i]); accepted++;
                checks.Require(effects.AcceptedBurstCount == accepted, "each unique accepted objective emits once");
                effects.PlayEvent(sequence[i]);
                checks.Require(effects.AcceptedBurstCount == accepted, "later duplicate objective is suppressed");
                Tick(game, effects, .02f, checks); RequireActiveMeshesInside(effects, camera, checks);
            }
            effects.PlayEvent(OrbitalEvent.Dock);
            checks.Require(!effects.DockActive && effects.DockSequenceCount == 0, "running phase cannot pre-emptively celebrate docking");
            checks.Require(game.State.Apply(OrbitalEvent.Dock) && game.State.Phase == OrbitalPhase.Success, "complete synthetic objective sequence succeeds");
            effects.PlayEvent(OrbitalEvent.Dock);
            checks.Require(effects.DockActive && effects.DockSequenceCount == 1, "successful docking starts one receiver sequence");
            effects.PlayEvent(OrbitalEvent.Dock);
            checks.Require(effects.DockSequenceCount == 1, "duplicate success is suppressed");
            float progress = effects.DockProgress;
            for (int i = 0; i < 14; i++)
            {
                Tick(game, effects, .1f, checks); RequireActiveMeshesInside(effects, camera, checks);
                if (i == 1) checks.Require(effects.DockProgress > progress, "receiver sequence has visible staged progression");
            }
            checks.Require(!effects.DockActive && effects.ActiveBurstCount == 0, "finite receiver and burst sequences finish");
            var ballVisual = (Transform)typeof(OrbitalEnergyEffects).GetField("ballVisual", Private).GetValue(effects);
            checks.Require(ballVisual != null && !ballVisual.gameObject.activeSelf, "successful receiver sequence consumes only the decorative ball child");
            Field("replaying").SetValue(game, true);
            try
            {
                Tick(game, effects, .02f, checks);
                checks.Require(ballVisual.gameObject.activeSelf, "replay restores the consumed decorative ball so its recorded path stays visible");
                effects.PlayEvent(OrbitalEvent.Dock);
                RequireQuiet(effects, checks, "replaying a delivered flight does not replay the success burst");
            }
            finally { Field("replaying").SetValue(game, false); }
            effects.PlayEvent(OrbitalEvent.Dock);
            checks.Require(effects.DockSequenceCount == 1 && !effects.DockActive, "ending replay cannot restart the previous receiver success");
            report.acceptedTargetBursts += accepted; report.successSequences++;
            RequireFixedPhysics(fixedPoses, checks);

            game.ResetAttempt(); effects.ResetEffects(); game.State.Launch();
            OrbitalEvent invalid = sequence[0] == OrbitalEvent.TargetA ? OrbitalEvent.TargetB : OrbitalEvent.TargetA;
            checks.Require(game.State.Apply(invalid) && game.State.Phase == OrbitalPhase.Failure, "wrong-order synthetic objective is rejected");
            effects.PlayEvent(invalid); effects.PlayEvent(OrbitalEvent.Dock); Tick(game, effects, .02f, checks);
            RequireQuiet(effects, checks, "failure never emits success effects");
            checks.Require(effects.AcceptedBurstCount == 0 && effects.DockSequenceCount == 0, "rejected target does not count as a visual success");

            game.ResetAttempt(); effects.ResetEffects(); game.State.Launch();
            game.State.Apply(sequence[0]); effects.PlayEvent(sequence[0]); Tick(game, effects, .03f, checks);
            var pauseHandler = typeof(OrbitalEnergyEffects).GetMethod("OnApplicationPause", Private);
            checks.Require(pauseHandler != null, "VFX explicitly handle application suspension");
            pauseHandler.Invoke(effects, new object[] { true });
            try
            {
                var suspended = CaptureDecorations(effects);
                Tick(game, effects, .5f, checks);
                RequireDecorations(suspended, checks, "application suspension freezes transient VFX");
                checks.Require(effects.ActiveTrailSegments == 0, "application suspension hides the trail");
                game.State.Apply(sequence[1]); effects.PlayEvent(sequence[1]);
                checks.Require(effects.AcceptedBurstCount == 1, "late accepted state cannot emit while the application is suspended");
            }
            finally { pauseHandler.Invoke(effects, new object[] { false }); }

            game.ResetAttempt(); effects.ResetEffects(); game.State.Launch();
            game.State.Apply(sequence[0]); effects.PlayEvent(sequence[0]); Tick(game, effects, .03f, checks);
            Field("replaying").SetValue(game, true);
            try
            {
                Tick(game, effects, .03f, checks); effects.PlayEvent(sequence[0]); effects.PlayEvent(OrbitalEvent.Dock);
                RequireQuiet(effects, checks, "replay hides transient feedback");
            }
            finally { Field("replaying").SetValue(game, false); }
            int beforeReplayReturn = effects.AcceptedBurstCount;
            effects.PlayEvent(sequence[0]);
            checks.Require(effects.AcceptedBurstCount == beforeReplayReturn, "leaving replay does not celebrate an old target again");

            game.ResetAttempt(); effects.ResetEffects(); game.State.Launch();
            game.State.Apply(sequence[0]); effects.PlayEvent(sequence[0]); Tick(game, effects, .03f, checks);
            game.Profile.reducedMotion = true; Tick(game, effects, .03f, checks);
            RequireQuiet(effects, checks, "enabling reduced motion settles transient feedback immediately");
            effects.PlayEvent(sequence[0]); effects.PlayEvent(OrbitalEvent.Dock);
            RequireQuiet(effects, checks, "reduced motion suppresses new moving feedback");
            game.Profile.reducedMotion = false;
            game.ResetAttempt(); effects.ResetEffects();
            RequireQuiet(effects, checks, "retry resets all transient VFX");
            checks.Require(meshCount == effects.GetComponentsInChildren<MeshFilter>(true).Length, "effect playback reuses the authored mesh pool");
            // Leave a genuine active success animation for the following
            // SelectLevel call, so OnDisable cleanup is actually exercised.
            game.State.Launch();
            foreach (var signal in sequence) game.State.Apply(signal);
            effects.PlayEvent(OrbitalEvent.Dock); Tick(game, effects, .02f, checks);
            checks.Require(effects.DockActive, "arm a receiver animation before switching boards");
        }

        static void VerifyTrail(OrbitalPrototype game, OrbitalEnergyEffects effects, SpatialCamera camera, Checks checks)
        {
            Transform board = game.Ball.transform.parent;
            var sampled = new List<Vector3>();
            for (int i = 0; i < 18; i++)
            {
                Vector3 local = i < 9 ? new Vector3(-.08f + i * .01f, .12f, -.03f) : new Vector3(0, .12f, -.03f + (i - 8) * .01f);
                Vector3 world = board.TransformPoint(local); sampled.Add(world); game.Ball.position = world;
                Tick(game, effects, .02f, checks); RequireActiveMeshesInside(effects, camera, checks);
                checks.Require(effects.TrailLength <= .1202f, "trail arc length remains at most 12 cm");
            }
            checks.Require(effects.ActiveTrailSegments > 0 && effects.TrailPointCount >= 3, "moving ball produces a sampled comet trail");
            bool hasBend = false;
            for (int i = 0; i < effects.TrailPointCount; i++)
            {
                Vector3 point = effects.GetTrailPoint(i); float distance = float.PositiveInfinity;
                for (int segment = 1; segment < sampled.Count; segment++) distance = Mathf.Min(distance, DistanceToSegment(point, sampled[segment - 1], sampled[segment]));
                checks.Require(distance < .001f, "trail sample lies on actual curved ball-position history");
                if (board.InverseTransformPoint(point).x < -.015f) hasBend = true;
            }
            checks.Require(hasBend, "the tail retains the pre-turn path instead of projecting a straight velocity line");
            for (int i = 0; i < 5; i++) Tick(game, effects, .04f, checks);
            checks.Require(effects.ActiveTrailSegments == 0 && effects.TrailLength == 0, "a stationary ball sheds the trail within 0.2 seconds while the attempt remains running");
        }

        static float DistanceToSegment(Vector3 point, Vector3 a, Vector3 b)
        {
            Vector3 delta = b - a;
            return (point - (a + delta * Mathf.Clamp01(Vector3.Dot(point - a, delta) / Mathf.Max(delta.sqrMagnitude, .00000001f)))).magnitude;
        }

        static void Tick(OrbitalPrototype game, OrbitalEnergyEffects effects, float delta, Checks checks)
        {
            string state = StateSignature(game);
            var poses = new List<BodyPose>();
            foreach (var body in game.GetComponentsInChildren<Rigidbody>(true))
                poses.Add(new BodyPose { body = body, position = body.position, rotation = body.rotation, scale = body.transform.lossyScale,
                    velocity = body.linearVelocity, angularVelocity = body.angularVelocity, kinematic = body.isKinematic });
            effects.Tick(delta);
            checks.Require(state == StateSignature(game), "VFX do not mutate puzzle state or objective flags");
            foreach (var pose in poses)
                checks.Require((pose.body.position - pose.position).sqrMagnitude < .0000000001f && Quaternion.Angle(pose.body.rotation, pose.rotation) < .001f &&
                    (pose.body.transform.lossyScale - pose.scale).sqrMagnitude < .0000000001f && pose.body.isKinematic == pose.kinematic &&
                    (pose.body.linearVelocity - pose.velocity).sqrMagnitude < .0000000001f && (pose.body.angularVelocity - pose.angularVelocity).sqrMagnitude < .0000000001f,
                    "VFX leave Rigidbody pose, scale and velocities unchanged");
        }

        static string StateSignature(OrbitalPrototype game) => game.State.Level + ":" + game.State.Phase + ":" + game.State.HasA + ":" + game.State.HasB + ":" +
            game.State.HasC + ":" + game.State.GateOpen + ":" + game.State.GateCrossed + ":" + game.State.ResultCode;

        static void RequireFixedPhysics(List<FixedPose> poses, Checks checks)
        {
            foreach (var pose in poses)
            {
                Matrix4x4 current = pose.collider.transform.localToWorldMatrix; bool same = true;
                for (int element = 0; element < 16; element++) same &= Mathf.Abs(current[element] - pose.matrix[element]) < .000001f;
                checks.Require(same, "VFX do not move fixed physical track or input collider " + pose.collider.name);
            }
        }

        static Dictionary<Transform, Matrix4x4> CaptureDecorations(OrbitalEnergyEffects effects)
        {
            var result = new Dictionary<Transform, Matrix4x4>();
            foreach (var mesh in effects.GetComponentsInChildren<MeshFilter>())
                if (mesh.gameObject.activeInHierarchy) result[mesh.transform] = mesh.transform.localToWorldMatrix;
            return result;
        }

        static void RequireDecorations(Dictionary<Transform, Matrix4x4> poses, Checks checks, string context)
        {
            foreach (var pose in poses)
            {
                Matrix4x4 current = pose.Key.localToWorldMatrix; bool same = true;
                for (int i = 0; i < 16; i++) same &= Mathf.Abs(current[i] - pose.Value[i]) < .000001f;
                checks.Require(same, context);
            }
        }

        static void RequireQuiet(OrbitalEnergyEffects effects, Checks checks, string context) =>
            checks.Require(effects.ActiveTrailSegments == 0 && effects.ActiveBurstCount == 0 && !effects.DockActive, context);

        static void RequireActiveMeshesInside(OrbitalEnergyEffects effects, SpatialCamera camera, Checks checks)
        {
            foreach (var mesh in effects.GetComponentsInChildren<MeshFilter>())
                if (mesh.sharedMesh != null && mesh.gameObject.activeInHierarchy)
                    RequireMeshBounds(mesh, camera, checks);
        }

        static void RequireMeshBounds(MeshFilter mesh, SpatialCamera camera, Checks checks)
        {
            Bounds bounds = mesh.sharedMesh.bounds;
            Matrix4x4 matrix = camera.transform.worldToLocalMatrix * mesh.transform.localToWorldMatrix;
            Vector3 half = camera.Dimensions * .5f + Vector3.one * .0002f;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = matrix.MultiplyPoint3x4(bounds.center + Vector3.Scale(bounds.extents,
                    new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1)));
                checks.Require(Mathf.Abs(point.x) <= half.x && Mathf.Abs(point.y) <= half.y && Mathf.Abs(point.z) <= half.z,
                    "active VFX mesh stays inside the spatial window: " + mesh.name);
            }
        }

        static void RequireBounds(Bounds bounds, SpatialCamera camera, Checks checks, string context)
        {
            Vector3 half = camera.Dimensions * .5f + Vector3.one * .0002f;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = camera.transform.InverseTransformPoint(bounds.center + Vector3.Scale(bounds.extents,
                    new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1)));
                checks.Require(Mathf.Abs(point.x) <= half.x && Mathf.Abs(point.y) <= half.y && Mathf.Abs(point.z) <= half.z, context);
            }
        }

        static OrbitalEvent[] Sequence(OrbitalLevel level) => level switch
        {
            OrbitalLevel.Moon => new[] { OrbitalEvent.TargetA, OrbitalEvent.ReturnGate, OrbitalEvent.TargetB, OrbitalEvent.Dock },
            OrbitalLevel.Reverse => new[] { OrbitalEvent.TargetB, OrbitalEvent.TargetA, OrbitalEvent.Dock },
            OrbitalLevel.Bridge => new[] { OrbitalEvent.TargetB, OrbitalEvent.ReturnGate, OrbitalEvent.TargetA, OrbitalEvent.Dock },
            OrbitalLevel.Finale => new[] { OrbitalEvent.TargetA, OrbitalEvent.TargetB, OrbitalEvent.ReturnGate, OrbitalEvent.CheckpointC, OrbitalEvent.Dock },
            _ => new[] { OrbitalEvent.TargetA, OrbitalEvent.TargetB, OrbitalEvent.Dock }
        };

        static FieldInfo Field(string name) => typeof(OrbitalPrototype).GetField(name, Private) ?? throw new MissingFieldException(name);
        static void RequireIdle(OrbitalPrototype game)
        {
            if (game == null) throw new InvalidOperationException("No chapter is loaded.");
            if (game.IsReplaying || game.State.Phase != OrbitalPhase.Configuring || game.Construction != null && game.Construction.HasHeld)
                throw new InvalidOperationException("Finish the player's capture/flight and return to configuration before VFX work.");
            if (game.Assembly != null)
                foreach (var piece in game.Assembly.pieces)
                    if (piece.IsManipulating) throw new InvalidOperationException("Finish the player's captured piece first.");
        }
        static void Call(OrbitalPrototype game, string name, params object[] args)
        {
            try { (typeof(OrbitalPrototype).GetMethod(name, Private) ?? throw new MissingMethodException(name)).Invoke(game, args); }
            catch (TargetInvocationException exception) { throw exception.InnerException ?? exception; }
        }
    }
}
