using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace SpaceBall.Editor
{
    /// <summary>
    /// Synchronous Play-mode checks of the journey, UI dispatch and real physics.
    /// Restores the two journey preferences and the original profile reference.
    /// Leaves the original level on an empty, editable workbench.
    /// </summary>
    public static class OrbitalJourneyVerification
    {
        const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        [Serializable]
        sealed class Report
        {
            public string status = "passed";
            public int assertions;
            public string[] scenarios;
            public string tutorialPhysics;
        }

        sealed class Checks
        {
            public int count;
            public readonly List<string> scenarios = new();
            public void Require(bool condition, string message)
            {
                count++;
                if (!condition) throw new InvalidOperationException("Journey verification failed: " + message);
            }
            public void At(Vector3 actual, Vector3 expected, string message)
            {
                Require((actual - expected).sqrMagnitude < .00000001f, message + "; actual=" + actual + ", expected=" + expected);
            }
        }

        readonly struct Preference
        {
            readonly string key, value;
            readonly bool existed;
            public Preference(string key)
            {
                this.key = key;
                existed = PlayerPrefs.HasKey(key);
                value = PlayerPrefs.GetString(key, "");
            }
            public void Restore()
            {
                if (existed) PlayerPrefs.SetString(key, value);
                else PlayerPrefs.DeleteKey(key);
            }
        }

        public static string Run()
        {
            if (!Application.isPlaying) throw new InvalidOperationException("Journey verification requires Play mode.");
            var game = UnityEngine.Object.FindFirstObjectByType<OrbitalPrototype>();
            if (game == null) throw new InvalidOperationException("No active OrbitalPrototype was found.");
            var checks = new Checks();
            var main = new Preference(OrbitalProgress.StorageKey);
            var backup = new Preference(OrbitalProgress.StorageKey + ".backup");
            object originalProfile = Field("progress").GetValue(game);
            bool originalPreferencesOpen = Read<bool>(game, "preferencesOpen");
            OrbitalLevel originalLevel = game.State.Level;
            string tutorialPhysics = "";

            try
            {
                // Delete only our isolated save slots, never the PlayerPrefs database.
                PlayerPrefs.DeleteKey(OrbitalProgress.StorageKey);
                PlayerPrefs.DeleteKey(OrbitalProgress.StorageKey + ".backup");
                PlayerPrefs.Save();
                Field("progress").SetValue(game, OrbitalProgress.Load());
                Field("preferencesOpen").SetValue(game, false);
                Call(game, "OnApplicationPause", false);
                game.ResetAttempt();
                game.SelectLevel(OrbitalLevel.Tutorial);
                checks.Require(Read<UnityEngine.Object>(game, "journeyText") != null &&
                    Read<Transform[]>(game, "memoryDots")?.Length > 1, "rebuild the journey UI and previous-flight dot pool before verification");
                VerifyFreshUi(game, checks);

                tutorialPhysics = game.RunPhysicsProbe(OrbitalLevel.Tutorial, 1, 1);
                checks.Require(game.State.Phase == OrbitalPhase.Success, "real Tutorial physics succeeds; " + tutorialPhysics);
                checks.Require(game.ChallengeAttempts == 1, "Tutorial success counts one accepted launch");
                checks.Require(game.Profile.tutorial.completed && game.Profile.tutorial.independent && game.Profile.tutorial.efficient,
                    "real Tutorial success earns all three eligible badges");
                checks.Require(game.Profile.tutorial.bestAttempts == 1, "Tutorial records best attempts");
                VerifyContextUi(game, checks, false, false, true, true, false, true, "Success");
                Click(game, OrbitalControlKind.Settings, 0, checks);
                VerifyMissionGroups(game, checks, false, false, "success preferences overlay");
                Click(game, OrbitalControlKind.Settings, 0, checks);
                VerifyMissionGroups(game, checks, false, true, "closing preferences restores success medals");
                Refresh(game);
                checks.Require(Control(game, OrbitalControlKind.Level, 1).GetComponent<OrbitalClickTarget>().interactable,
                    "Tutorial success unlocks the Moon UI control");
                Click(game, OrbitalControlKind.Continue, 0, checks);
                checks.Require(game.State.Level == OrbitalLevel.Moon && game.State.Phase == OrbitalPhase.Configuring,
                    "visible Continue control starts the Moon challenge");
                checks.Require(game.ChallengeAttempts == 0 && !game.ChallengeUsedHint && !game.Assembly.Ready,
                    "Continue starts a fresh unassembled challenge");
                checks.scenarios.Add("Fresh profile, locked Moon UI, missing-part launch, real Tutorial success and Continue");

                VerifyAccumulatedBadges(game, checks);
                VerifyReplayPause(game, checks);
                VerifySettingsAndRecovery(game, checks);
                VerifyRunningPause(game, checks);
                checks.scenarios.Add("Contextual target/label visibility, hidden activation guards and mission-overlay restoration");
                return JsonUtility.ToJson(new Report
                {
                    assertions = checks.count, scenarios = checks.scenarios.ToArray(), tutorialPhysics = tutorialPhysics
                }, true);
            }
            finally
            {
                try
                {
                    if (game != null)
                    {
                        // Cleanup still uses the temporary profile, so it cannot mutate
                        // the player's in-memory progress through StartChallenge.
                        Call(game, "OnApplicationPause", false);
                        game.ResetAttempt();
                        game.SelectLevel(originalLevel);
                        game.Assembly?.Clear();
                        game.ResetAttempt();
                    }
                }
                finally
                {
                    if (game != null)
                    {
                        Field("progress").SetValue(game, originalProfile);
                        Field("preferencesOpen").SetValue(game, originalPreferencesOpen);
                    }
                    main.Restore();
                    backup.Restore();
                    PlayerPrefs.Save();
                    if (game != null) Refresh(game);
                }
            }
        }

        static void VerifyFreshUi(OrbitalPrototype game, Checks checks)
        {
            checks.Require(!game.Profile.tutorial.completed && !game.Profile.moon.completed, "fresh profile has no completion");
            checks.Require(game.Profile.tutorial.BadgeCount == 0 && game.Profile.moon.BadgeCount == 0, "fresh profile has no badges");
            Click(game, OrbitalControlKind.Level, 1, checks, false);
            checks.Require(game.State.Level == OrbitalLevel.Tutorial, "locked Moon click cannot switch levels");
            game.ActivateControl(OrbitalControlKind.Level, 1);
            checks.Require(game.State.Level == OrbitalLevel.Tutorial, "UI command also enforces the Moon lock");
            Click(game, OrbitalControlKind.Launch, 0, checks, false);
            checks.Require(!game.Launch() && game.ChallengeAttempts == 0, "missing-part launch rejection does not count an attempt");
            checks.Require(game.State.Phase == OrbitalPhase.Configuring && !game.Profile.tutorial.completed,
                "rejected launch neither starts physics nor awards progress");
            VerifyContextUi(game, checks, true, false, false, false, true, false, "Configuring");
        }

        static void VerifyAccumulatedBadges(OrbitalPrototype game, Checks checks)
        {
            Configure(game, 0, 0, checks);
            Fly(game, checks, "first Moon failure");
            checks.Require(game.State.Phase == OrbitalPhase.Failure && game.ChallengeAttempts == 1, "real wrong route fails after one launch");
            checks.Require(!game.Profile.moon.completed && game.Profile.moon.BadgeCount == 0, "failure does not award a badge");
            int traceCount = game.PreviousFlightPointCount;
            checks.Require(traceCount > 1, "failed real flight records a previous trajectory");
            Click(game, OrbitalControlKind.Reset, 0, checks);
            checks.Require(game.ChallengeAttempts == 1 && game.PreviousFlightPointCount == traceCount,
                "Reset preserves challenge attempts and the prior trajectory");
            checks.Require(game.RecordedFrameCount == 0 && game.Assembly.Ready, "Reset clears replay frames and retains assembly");
            checks.Require(VisibleMemoryDots(game) > 1, "prior trajectory remains visibly available after Reset");

            Click(game, OrbitalControlKind.Hint, 0, checks);
            checks.Require(game.ChallengeUsedHint && game.ChallengeAttempts == 1, "Hint marks assistance without counting a launch");
            checks.Require(game.Profile.moon.BadgeCount == 0, "Hint itself does not grant progress");
            Configure(game, 1, 0, checks);
            Fly(game, checks, "hinted Moon success");
            checks.Require(game.State.Phase == OrbitalPhase.Success && game.ChallengeAttempts == 2, "a hinted challenge can succeed on its second launch");
            var record = game.Profile.moon;
            checks.Require(record.completed && record.efficient && !record.independent && record.BadgeCount == 2,
                "hinted success grants delivery and efficiency, not independent solving");
            checks.Require(record.bestAttempts == 2, "hinted challenge saves two-attempt best");

            game.SelectLevel(OrbitalLevel.Moon);
            Refresh(game);
            checks.Require(game.ChallengeAttempts == 0 && !game.ChallengeUsedHint, "Moon rechallenge resets session counters and hint use");
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                Configure(game, 0, 0, checks);
                Fly(game, checks, "independent failure " + attempt);
                checks.Require(game.State.Phase == OrbitalPhase.Failure && game.ChallengeAttempts == attempt,
                    "rechallenge failure retains cumulative launch count " + attempt);
                checks.Require(!game.Profile.moon.independent, "failure cannot award independent-solving badge");
                Click(game, OrbitalControlKind.Reset, 0, checks);
            }
            Configure(game, 1, 0, checks);
            Fly(game, checks, "independent third-attempt success");
            checks.Require(game.State.Phase == OrbitalPhase.Success && game.ChallengeAttempts == 3, "independent challenge succeeds on launch three");
            checks.Require(record.completed && record.independent && record.efficient && record.BadgeCount == 3,
                "independent and two-attempt badges accumulate across different successful challenges");
            checks.Require(record.bestAttempts == 2, "slower independent success does not replace the two-attempt best");
            checks.scenarios.Add("Real Moon failure, preserved attempts/trace, hinted success and independently accumulated badges");
        }

        static void VerifyReplayPause(OrbitalPrototype game, Checks checks)
        {
            string badges = JsonUtility.ToJson(game.Profile.moon);
            Click(game, OrbitalControlKind.Replay, 0, checks);
            checks.Require(game.IsReplaying, "Replay UI starts recorded playback");
            VerifyContextUi(game, checks, false, false, false, false, true, false, "Replay");
            Call(game, "Update");
            float time = Read<float>(game, "replayTime");
            Vector3 position = game.Ball.position;
            Quaternion rotation = game.Ball.rotation;
            Call(game, "OnApplicationPause", true);
            for (int i = 0; i < 4; i++) Call(game, "Update");
            checks.Require(game.IsReplaying && Read<float>(game, "replayTime") == time, "paused replay does not advance or finish");
            checks.At(game.Ball.position, position, "paused replay preserves ball position");
            checks.Require(Quaternion.Angle(game.Ball.rotation, rotation) < .001f, "paused replay preserves ball rotation");
            Call(game, "OnApplicationPause", false);
            Call(game, "Update");
            float advance = Read<float>(game, "replayTime") - time;
            checks.Require(advance >= 0f && advance <= .05001f, "resumed replay cannot jump by an unbounded frame delta");
            checks.Require(JsonUtility.ToJson(game.Profile.moon) == badges, "read-only replay does not award badges again");
            Click(game, OrbitalControlKind.Reset, 0, checks);
            checks.Require(!game.IsReplaying && game.AssemblyEditable, "Reset exits playback and restores editing");
            VerifyContextUi(game, checks, true, false, false, false, true, false, "Reset after replay");
            checks.scenarios.Add("Replay pauses without pose/time changes and resumes with bounded advancement");
        }

        static void VerifyRunningPause(OrbitalPrototype game, Checks checks)
        {
            game.SelectLevel(OrbitalLevel.Moon);
            Configure(game, 1, 0, checks);
            checks.Require(game.Launch(), "pause test launches a real ball");
            VerifyContextUi(game, checks, false, true, false, false, true, false, "Running");
            Vector3 position = game.Ball.position, velocity = game.Ball.linearVelocity;
            float elapsed = game.ShotElapsed;
            Call(game, "OnApplicationPause", true);
            checks.Require(game.State.Phase == OrbitalPhase.Paused && game.Ball.isKinematic, "system pause freezes a running shot");
            Call(game, "Update");
            checks.At(game.Ball.position, position, "paused shot preserves position");
            checks.Require(game.ShotElapsed == elapsed, "paused shot preserves simulation time");
            Call(game, "OnApplicationPause", false);
            checks.Require(game.State.Phase == OrbitalPhase.Paused, "returning to the app requires explicit shot resume");
            Click(game, OrbitalControlKind.Pause, 0, checks);
            checks.Require(game.State.Phase == OrbitalPhase.Running && !game.Ball.isKinematic, "Continue UI resumes the shot");
            checks.At(game.Ball.linearVelocity, velocity, "resuming restores saved velocity");
            Click(game, OrbitalControlKind.Reset, 0, checks);
            checks.scenarios.Add("Running shot system-pause, explicit continue and velocity restoration");
        }

        static void VerifySettingsAndRecovery(OrbitalPrototype game, Checks checks)
        {
            VerifyHiddenPreferences(game, checks);
            Click(game, OrbitalControlKind.Settings, 0, checks);
            VerifyMissionGroups(game, checks, false, false, "configuration preferences overlay");
            Click(game, OrbitalControlKind.Audio, 0, checks);
            Click(game, OrbitalControlKind.Comfort, 0, checks);
            Click(game, OrbitalControlKind.Trace, 0, checks);
            checks.Require(!game.Profile.sound && game.Profile.reducedMotion && !game.Profile.trace, "UI toggles all three preferences");
            checks.Require(VisibleMemoryDots(game) == 0, "trace-off hides all prior-flight dots");
            Click(game, OrbitalControlKind.Settings, 0, checks);
            VerifyHiddenPreferences(game, checks);
            VerifyMissionGroups(game, checks, true, false, "closing preferences restores route diagram");
            // Save twice so the known-good backup has this exact settings snapshot.
            game.Profile.Save(); game.Profile.Save();
            var loaded = OrbitalProgress.Load();
            RequireSavedProfile(loaded, checks, "normal reload");
            Call(game, "InitializeExperience");
            RequireSavedProfile(game.Profile, checks, "game profile reload");
            Refresh(game);
            checks.Require(game.ReducedMotion && VisibleMemoryDots(game) == 0, "reloaded comfort and trace settings reach the live UI");
            PlayerPrefs.SetString(OrbitalProgress.StorageKey, "{ definitely broken json");
            PlayerPrefs.Save();
            RequireSavedProfile(OrbitalProgress.Load(), checks, "corrupt primary uses backup");
            PlayerPrefs.SetString(OrbitalProgress.StorageKey, "{\"version\":999}");
            RequireSavedProfile(OrbitalProgress.Load(), checks, "unsupported primary version uses backup");
            PlayerPrefs.SetString(OrbitalProgress.StorageKey + ".backup", "broken backup");
            var fresh = OrbitalProgress.Load();
            checks.Require(!fresh.tutorial.completed && !fresh.moon.completed && fresh.lastLevel == 0,
                "two invalid saves fall back to a fresh locked profile");
            checks.Require(fresh.sound && fresh.trace && !fresh.reducedMotion, "corrupt-save fallback restores preference defaults");
            checks.scenarios.Add("Settings UI, profile round-trip, backup recovery and fully corrupt fallback");
        }

        static void VerifyHiddenPreferences(OrbitalPrototype game, Checks checks)
        {
            Refresh(game);
            bool sound = game.Profile.sound, reducedMotion = game.Profile.reducedMotion, trace = game.Profile.trace;
            foreach (var kind in new[] { OrbitalControlKind.Audio, OrbitalControlKind.Comfort, OrbitalControlKind.Trace })
            {
                var control = Control(game, kind, 0);
                var target = control.GetComponent<OrbitalClickTarget>();
                var label = control.GetComponent<OrbitalControlLabel>();
                checks.Require(!control.gameObject.activeSelf && !control.gameObject.activeInHierarchy,
                    kind + ": collapsed preference target is inactive");
                checks.Require(label != null && label.text != null && !label.text.gameObject.activeSelf,
                    kind + ": collapsed preference label is inactive");
                checks.Require(target != null, kind + ": hidden target exists for activation guard check");
                typeof(OrbitalClickTarget).GetField("m_LastActivationFrame", PrivateInstance).SetValue(target, -1);
                target.Activate();
                checks.Require(game.Profile.sound == sound && game.Profile.reducedMotion == reducedMotion && game.Profile.trace == trace,
                    kind + ": hidden preference cannot be activated");
            }
        }

        static void RequireSavedProfile(OrbitalProgress profile, Checks checks, string label)
        {
            checks.Require(!profile.sound && profile.reducedMotion && !profile.trace && profile.lastLevel == 1, label + ": preferences and last level");
            checks.Require(profile.tutorial.BadgeCount == 3 && profile.moon.BadgeCount == 3 && profile.moon.bestAttempts == 2,
                label + ": unlock, accumulated badges and best attempts");
        }

        static void VerifyContextUi(OrbitalPrototype game, Checks checks, bool hint, bool pause, bool replay, bool next,
            bool route, bool medals, string context)
        {
            Refresh(game);
            var kinds = new[] { OrbitalControlKind.Hint, OrbitalControlKind.Pause, OrbitalControlKind.Replay, OrbitalControlKind.Continue };
            var visibility = new[] { hint, pause, replay, next };
            for (int i = 0; i < kinds.Length; i++)
            {
                var control = Control(game, kinds[i], 0);
                var label = control.GetComponent<OrbitalControlLabel>();
                var target = control.GetComponent<OrbitalClickTarget>();
                checks.Require(control.gameObject.activeSelf == visibility[i] && control.gameObject.activeInHierarchy == visibility[i] &&
                    label != null && label.text != null && label.text.gameObject.activeSelf == visibility[i],
                    context + ": " + kinds[i] + " target and label follow contextual visibility");
                if (visibility[i]) continue;
                OrbitalLevel level = game.State.Level;
                OrbitalPhase phase = game.State.Phase;
                int attempts = game.ChallengeAttempts, frames = game.RecordedFrameCount;
                bool usedHint = game.ChallengeUsedHint, playingReplay = game.IsReplaying;
                float replayTime = Read<float>(game, "replayTime");
                string profile = JsonUtility.ToJson(game.Profile);
                typeof(OrbitalClickTarget).GetField("m_LastActivationFrame", PrivateInstance).SetValue(target, -1);
                target.Activate();
                checks.Require(game.State.Level == level && game.State.Phase == phase && game.ChallengeAttempts == attempts &&
                    game.RecordedFrameCount == frames && game.ChallengeUsedHint == usedHint && game.IsReplaying == playingReplay &&
                    Read<float>(game, "replayTime") == replayTime && JsonUtility.ToJson(game.Profile) == profile,
                    context + ": hidden " + kinds[i] + " cannot change the challenge or progress");
            }
            checks.Require(Control(game, OrbitalControlKind.Reset, 0).gameObject.activeInHierarchy &&
                Control(game, OrbitalControlKind.Settings, 0).gameObject.activeInHierarchy,
                context + ": recovery and preferences remain accessible");
            VerifyMissionGroups(game, checks, route, medals, context);
        }

        static void VerifyMissionGroups(OrbitalPrototype game, Checks checks, bool route, bool medals, string context)
        {
            Refresh(game);
            var routeRoot = Read<Transform>(game, "missionRoute");
            var medalRoot = Read<Transform>(game, "missionMedals");
            checks.Require(routeRoot != null && routeRoot.gameObject.activeSelf == route && routeRoot.gameObject.activeInHierarchy == route &&
                medalRoot != null && medalRoot.gameObject.activeSelf == medals && medalRoot.gameObject.activeInHierarchy == medals,
                context + ": route and achievement groups have the expected visibility");
        }

        static void Configure(OrbitalPrototype game, int route, int reflector, Checks checks)
        {
            if (game.State.Phase != OrbitalPhase.Configuring) game.ResetAttempt();
            checks.Require(game.Assembly.Place(0) && game.Assembly.Place(1), "configure installs both compatible pieces");
            game.ConfigureRoute(route); game.ConfigureReflector(reflector);
            checks.Require(game.Assembly.Ready && game.State.Route == route && game.State.Reflector == reflector,
                "configured physical routes match piece choices");
        }

        static void Fly(OrbitalPrototype game, Checks checks, string label)
        {
            checks.Require(game.Launch(), label + ": accepted launch");
            SimulationMode simulation = Physics.simulationMode;
            var ball = game.Ball;
            RigidbodyInterpolation interpolation = ball.interpolation;
            try
            {
                Physics.simulationMode = SimulationMode.Script;
                ball.interpolation = RigidbodyInterpolation.None;
                Physics.SyncTransforms();
                for (int i = 0; i < 1500 && game.State.Phase == OrbitalPhase.Running; i++)
                {
                    Call(game, "TickPhysics", .02f);
                    Physics.Simulate(.02f);
                }
                checks.Require(game.State.Phase == OrbitalPhase.Success || game.State.Phase == OrbitalPhase.Failure,
                    label + ": real simulation reaches a terminal outcome; " + game.StatusDescription);
            }
            finally
            {
                Physics.simulationMode = simulation;
                if (ball != null) ball.interpolation = interpolation;
            }
        }

        static void Click(OrbitalPrototype game, OrbitalControlKind kind, int value, Checks checks, bool enabled = true)
        {
            Refresh(game);
            var control = Control(game, kind, value);
            var target = control.GetComponent<OrbitalClickTarget>();
            checks.Require(control.gameObject.activeInHierarchy && target != null, kind + ": visible UI target exists");
            checks.Require(target.interactable == enabled, kind + ": expected UI enabled state");
            // Run is synchronous, so all synthetic presses share a Unity frame. Clear
            // only the frame debounce, preserving actual active/interactable checks.
            typeof(OrbitalClickTarget).GetField("m_LastActivationFrame", PrivateInstance).SetValue(target, -1);
            target.Activate();
            Refresh(game);
        }

        static OrbitalControl Control(OrbitalPrototype game, OrbitalControlKind kind, int value)
        {
            foreach (var control in game.GetComponentsInChildren<OrbitalControl>(true))
                if (control.kind == kind && control.value == value && control.owner == game) return control;
            throw new InvalidOperationException("Rebuild the journey interface before verification; missing control: " + kind);
        }

        static int VisibleMemoryDots(OrbitalPrototype game)
        {
            Refresh(game);
            var dots = Read<Transform[]>(game, "memoryDots");
            int visible = 0;
            if (dots != null) foreach (var dot in dots) if (dot != null && dot.gameObject.activeInHierarchy) visible++;
            return visible;
        }

        static FieldInfo Field(string name) => typeof(OrbitalPrototype).GetField(name, PrivateInstance) ??
            throw new MissingFieldException(typeof(OrbitalPrototype).FullName, name);
        static T Read<T>(OrbitalPrototype game, string name) => (T)Field(name).GetValue(game);
        static void Refresh(OrbitalPrototype game) { Call(game, "RefreshInterface"); Call(game, "RefreshExperience"); }
        static void Call(OrbitalPrototype game, string name, params object[] arguments)
        {
            var method = typeof(OrbitalPrototype).GetMethod(name, PrivateInstance) ??
                throw new MissingMethodException(typeof(OrbitalPrototype).FullName, name);
            try { method.Invoke(game, arguments); }
            catch (TargetInvocationException exception) { throw exception.InnerException ?? exception; }
        }
    }
}
