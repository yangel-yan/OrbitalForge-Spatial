using System;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;

namespace SpaceBall.Editor
{
    /// <summary>End-to-end first-chapter checks against live colliders and local progress.</summary>
    public static class OrbitalCampaignVerification
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        // These are the authored puzzle answers, intentionally independent of the
        // runtime's hint/answer metadata. A metadata typo must not bless a bad route.
        static readonly int[] Routes = { 1, 1, 0, 1, 0, 1 };
        static readonly int[] Reflectors = { 1, 0, 1, 1, 0, 0 };

        [Serializable]
        sealed class Flight
        {
            public string level, result, feedback, details;
            public int route, reflector;
            public float elapsed;
            public bool a, b, gateCrossed, c;
        }

        [Serializable]
        sealed class Report
        {
            public string status = "passed";
            public int levels, assertions;
            public string[] scenarios;
            public Flight[] configurations;
        }

        sealed class Checks
        {
            public int count;
            public void Require(bool condition, string message)
            {
                count++;
                if (!condition) throw new InvalidOperationException("Campaign verification failed: " + message);
            }
        }

        public static string Run()
        {
            if (!Application.isPlaying) throw new InvalidOperationException("Campaign verification requires Play mode.");
            var game = UnityEngine.Object.FindFirstObjectByType<OrbitalPrototype>();
            if (game == null) throw new InvalidOperationException("No active OrbitalPrototype was found.");
            var checks = new Checks();
            var flights = new List<Flight>();
            using (new OrbitalVerificationScope(game))
            {
                checks.Require(OrbitalCampaign.Count == Routes.Length && Routes.Length == Reflectors.Length,
                    "the authored six-mission test plan matches the playable chapter size");
                VerifySaveMigration(checks);
                VerifyConfigurationMatrix(game, checks, flights);
                VerifyGateProgressMeaning(game, checks);
                VerifyChapterJourney(game, checks);
                return JsonUtility.ToJson(new Report
                {
                    levels = OrbitalCampaign.Count,
                    assertions = checks.count,
                    configurations = flights.ToArray(),
                    scenarios = new[]
                    {
                        "Legacy two-mission save migration, independent added records and backup recovery",
                        "All 24 D/R configurations use live PhysX and reach an explained terminal result",
                        "Gate progress distinguishes power from crossing in live results and recorded replay",
                        "Fresh chapter, locked-entry guards, six real successful flights and sequential Continue",
                        "All six records and 18 eligible badges persist without cross-mission contamination",
                        "Final Continue opens/closes chapter overview and completed missions remain replayable"
                    }
                }, true);
            }
        }

        static void VerifyConfigurationMatrix(OrbitalPrototype game, Checks checks, List<Flight> flights)
        {
            for (int index = 0; index < OrbitalCampaign.Count; index++)
            {
                var level = (OrbitalLevel)index;
                int successes = 0;
                for (int route = 0; route < 2; route++)
                    for (int reflector = 0; reflector < 2; reflector++)
                    {
                        string details = game.RunPhysicsProbe(level, route, reflector);
                        var state = game.State;
                        string context = level + " D" + route + "/R" + reflector + ": ";
                        bool answer = route == Routes[index] && reflector == Reflectors[index];
                        checks.Require(state.Phase == OrbitalPhase.Success || state.Phase == OrbitalPhase.Failure,
                            context + "live physics reaches a terminal result; " + details);
                        checks.Require(game.RecordedFrameCount > 1 && game.ShotElapsed > 0f && game.ShotElapsed <= 25f,
                            context + "shot records a real bounded flight");
                        checks.Require(!string.IsNullOrEmpty(state.ResultCode) && !string.IsNullOrEmpty(state.Feedback),
                            context + "terminal outcome includes a reason and player-facing explanation");
                        if (answer)
                        {
                            successes++;
                            RequireDelivery(game, checks, context + details);
                        }
                        else
                        {
                            checks.Require(state.Phase == OrbitalPhase.Failure && state.ResultCode != "success",
                                context + "an incorrect spatial route cannot deliver the mission; " + details);
                            VerifyFailureMeaning(state, checks, context);
                            game.ShowHint();
                            string first = (string)OrbitalVerificationScope.Field("hintText").GetValue(game);
                            game.ShowHint();
                            string second = (string)OrbitalVerificationScope.Field("hintText").GetValue(game);
                            checks.Require(game.ChallengeUsedHint && !string.IsNullOrEmpty(first) &&
                                !string.IsNullOrEmpty(second) && first != second,
                                context + "a failed route offers two distinct, accessible hint stages");
                        }
                        flights.Add(new Flight
                        {
                            level = level.ToString(), route = route, reflector = reflector,
                            result = state.ResultCode, feedback = state.Feedback, details = details,
                            elapsed = game.ShotElapsed, a = state.HasA, b = state.HasB,
                            gateCrossed = state.GateCrossed, c = state.HasC
                        });
                    }
                checks.Require(successes == 1, level + ": the authored D/R plan has exactly one successful combination");
            }
        }

        static void VerifyGateProgressMeaning(OrbitalPrototype game, Checks checks)
        {
            string failure = game.RunPhysicsProbe(OrbitalLevel.Moon, 1, 1);
            checks.Require(game.State.Phase == OrbitalPhase.Failure && game.State.HasA && game.State.GateOpen &&
                !game.State.GateCrossed, "Moon outer exit powers the gate without crossing it; " + failure);
            Refresh(game);
            RequireGateNode(game, checks, false, "powered but uncrossed failed flight");

            string success = game.RunPhysicsProbe(OrbitalLevel.Moon, 1, 0);
            checks.Require(game.State.Phase == OrbitalPhase.Success && game.State.GateCrossed,
                "Moon inner return crosses the powered gate and delivers; " + success);
            Refresh(game);
            RequireGateNode(game, checks, true, "successful crossed-gate flight");

            // Seek genuine recorded samples. The current successful state has G
            // crossed throughout playback, so consulting live state or GateOpen
            // instead of the recorded crossing would light the first sample wrong.
            var frames = (System.Collections.IList)OrbitalVerificationScope.Field("frames").GetValue(game);
            int before = -1, after = -1;
            for (int index = 0; index < frames.Count - 1; index++)
            {
                object frame = frames[index];
                bool powered = FrameFlag(frame, "gate"), crossed = FrameFlag(frame, "gateCrossed");
                if (powered && !crossed && before < 0) before = index;
                if (powered && crossed && after < 0) after = index;
            }
            checks.Require(before >= 0 && after > before,
                "successful real flight records separate powered-before-crossing and crossed-gate samples");
            game.Replay();
            checks.Require(game.IsReplaying, "gate semantic check starts the genuine recorded replay");
            VerifyReplayGateFrame(game, checks, before, false, "recorded powered-before-crossing sample");
            VerifyReplayGateFrame(game, checks, after, true, "recorded crossed-gate sample");
            game.ResetAttempt();
        }

        static bool FrameFlag(object frame, string name) => (bool)frame.GetType().GetField(name).GetValue(frame);

        static void VerifyReplayGateFrame(OrbitalPrototype game, Checks checks, int index, bool crossed, string context)
        {
            float frameAdvance = Mathf.Min(Time.unscaledDeltaTime, .1f) * .5f;
            OrbitalVerificationScope.Field("replayTime").SetValue(game, (index + .25f) * Time.fixedDeltaTime - frameAdvance);
            OrbitalVerificationScope.Call(game, "Update");
            Refresh(game);
            checks.Require(game.IsReplaying && game.DisplayGate && game.DisplayGateCrossed == crossed,
                context + ": playback presents the recorded power and crossing independently");
            RequireGateNode(game, checks, crossed, context);
        }

        static void RequireGateNode(OrbitalPrototype game, Checks checks, bool reached, string context)
        {
            var nodes = (Array)OrbitalVerificationScope.Field("missionNodes").GetValue(game);
            Renderer gateCore = null;
            foreach (object node in nodes)
            {
                var symbol = (TMP_Text)node.GetType().GetField("symbol").GetValue(node);
                if (symbol.text == "G") gateCore = (Renderer)node.GetType().GetField("core").GetValue(node);
            }
            checks.Require(gateCore != null && gateCore.sharedMaterial == game.palette[reached ? 1 : 3],
                context + ": mission G progress is " + (reached ? "reached only after crossing" : "unreached while merely powered"));
        }

        static void VerifyFailureMeaning(OrbitalRunState state, Checks checks, string context)
        {
            switch (state.ResultCode)
            {
                case "closed_gate":
                    checks.Require(!state.GateCrossed && state.Feedback.Contains("门"), context + "closed-gate explanation matches the uncrossed gate"); break;
                case "target_a_before_b":
                    checks.Require(!state.HasA && !state.HasB && state.Feedback.Contains("B"), context + "reverse-order failure identifies B before A"); break;
                case "target_b_before_a":
                    checks.Require(!state.HasA && !state.HasB && state.Feedback.Contains("A"), context + "normal-order failure identifies missing A"); break;
                case "target_a_before_gate":
                case "target_b_before_gate":
                case "checkpoint_before_gate":
                case "dock_before_gate":
                    checks.Require(!state.GateCrossed && state.Feedback.Contains("门"), context + "gate-order failure identifies the uncrossed gate"); break;
                case "dock_missing_a": checks.Require(!state.HasA && state.Feedback.Contains("A"), context + "dock rejection identifies missing A"); break;
                case "dock_missing_b": checks.Require(!state.HasB && state.Feedback.Contains("B"), context + "dock rejection identifies missing B"); break;
                case "dock_missing_c": checks.Require(!state.HasC && state.Feedback.Contains("C"), context + "dock rejection identifies missing final calibration"); break;
                case "recovery_gate_open":
                    checks.Require(state.GateOpen && !state.GateCrossed && state.Feedback.Contains("回收"), context + "powered-gate recovery is not described as crossing the gate"); break;
                case "recovery_after_gate":
                    checks.Require(state.GateCrossed, context + "after-gate recovery actually crossed the gate"); break;
                case "recovery_dual_power":
                    checks.Require((!state.HasA || !state.HasB) && state.Feedback.Contains("A") && state.Feedback.Contains("B"), context + "dual-power failure identifies the missing pair"); break;
                case "recovery_before_a": checks.Require(!state.HasA && state.Feedback.Contains("A"), context + "recovery identifies unvisited A"); break;
                case "recovery_before_b": checks.Require(!state.HasB && state.Feedback.Contains("B"), context + "recovery identifies unvisited B"); break;
                case "recovery_uncharged": checks.Require(!state.GateOpen && state.Feedback.Contains("A"), context + "uncharged recovery matches a closed gate"); break;
                case "recovery_before_dock": checks.Require(state.HasA && state.HasB, context + "post-target recovery retains both reached targets"); break;
                default:
                    checks.Require(false, context + "unexpected failure needs an explicit, reviewed explanation: " + state.ResultCode); break;
            }
        }

        static void VerifyChapterJourney(OrbitalPrototype game, Checks checks)
        {
            OrbitalVerificationScope.Field("progress").SetValue(game, new OrbitalProgress());
            game.ResetAttempt();
            game.SelectLevel(OrbitalLevel.Tutorial);
            for (int index = 0; index < OrbitalCampaign.Count; index++)
            {
                var level = (OrbitalLevel)index;
                checks.Require(game.State.Level == level && game.State.Phase == OrbitalPhase.Configuring,
                    level + ": journey enters the expected fresh mission");
                checks.Require(!game.Assembly.Ready && game.ChallengeAttempts == 0 && !game.ChallengeUsedHint,
                    level + ": next mission starts with empty assembly and fresh attempt counters");
                VerifyLockedEntries(game, index, checks);
                checks.Require(game.Assembly.Place(0) && game.Assembly.Place(1), level + ": both pieces can be installed");
                game.ConfigureRoute(Routes[index]); game.ConfigureReflector(Reflectors[index]);
                Fly(game, checks, level.ToString());
                RequireDelivery(game, checks, level + ": chapter playthrough");
                var record = game.Profile.For(level);
                checks.Require(record.completed && record.independent && record.efficient && record.bestAttempts == 1,
                    level + ": unassisted first delivery earns all eligible badges");
                checks.Require(game.Profile.CompletedCount == index + 1 && game.Profile.TotalBadges == 3 * (index + 1),
                    level + ": only completed missions contribute progress and badges");
                if (index + 1 < OrbitalCampaign.Count)
                {
                    Click(game, OrbitalControlKind.Continue, 0, checks);
                    checks.Require(!game.IsChapterOverview, level + ": ordinary Continue advances without opening final overview");
                }
            }

            checks.Require(game.Profile.AllCompleted && game.Profile.CompletedCount == 6 && game.Profile.TotalBadges == 18,
                "six physical deliveries complete the chapter with 18 earned badges");
            string earned = JsonUtility.ToJson(game.Profile);
            Click(game, OrbitalControlKind.Continue, 0, checks);
            checks.Require(game.IsChapterOverview && game.State.Level == OrbitalLevel.Finale && game.State.Phase == OrbitalPhase.Success,
                "final Continue opens chapter overview while retaining the final success");
            checks.Require(JsonUtility.ToJson(game.Profile) == earned, "opening overview grants no extra awards or attempts");
            Click(game, OrbitalControlKind.Continue, 0, checks);
            checks.Require(!game.IsChapterOverview && game.State.Phase == OrbitalPhase.Success,
                "final Continue closes overview without restarting the challenge");
            var loaded = OrbitalProgress.Load();
            checks.Require(loaded.AllCompleted && loaded.TotalBadges == 18 && loaded.ResumeLevel == 5,
                "chapter completion, all badges and final mission persist to a fresh profile load");
            Click(game, OrbitalControlKind.Level, 0, checks);
            checks.Require(game.State.Level == OrbitalLevel.Tutorial && !game.IsChapterOverview && !game.Assembly.Ready,
                "completed missions remain replayable from the chapter selector");
            checks.Require(game.Profile.AllCompleted && game.Profile.TotalBadges == 18,
                "replaying an earlier mission preserves all chapter awards");
        }

        static void VerifyLockedEntries(OrbitalPrototype game, int current, Checks checks)
        {
            Refresh(game);
            for (int index = 0; index < OrbitalCampaign.Count; index++)
            {
                var level = (OrbitalLevel)index;
                var control = Control(game, OrbitalControlKind.Level, index);
                var target = control.GetComponent<OrbitalClickTarget>();
                bool unlocked = index <= current;
                checks.Require(control.gameObject.activeInHierarchy && target.interactable == unlocked && game.Profile.IsUnlocked(level) == unlocked,
                    "mission " + index + ": visible selector and progress agree about sequential unlocking");
                if (unlocked) continue;
                typeof(OrbitalClickTarget).GetField("m_LastActivationFrame", Private).SetValue(target, -1);
                target.Activate();
                checks.Require(game.State.Level == (OrbitalLevel)current, "locked target cannot change the current mission");
                game.ActivateControl(OrbitalControlKind.Level, index);
                checks.Require(game.State.Level == (OrbitalLevel)current, "locked level command cannot bypass progression");
            }
        }

        static void VerifySaveMigration(Checks checks)
        {
            const string oldSave = "{\"version\":1,\"lastLevel\":1,\"sound\":false,\"reducedMotion\":true,\"trace\":false," +
                "\"tutorial\":{\"completed\":true,\"independent\":true,\"efficient\":false,\"bestAttempts\":3}," +
                "\"moon\":{\"completed\":true,\"independent\":false,\"efficient\":true,\"bestAttempts\":2}}";
            PlayerPrefs.SetString(OrbitalProgress.StorageKey, oldSave);
            PlayerPrefs.DeleteKey(OrbitalProgress.StorageKey + ".backup");
            var migrated = OrbitalProgress.Load();
            checks.Require(!migrated.sound && migrated.reducedMotion && !migrated.trace && migrated.ResumeLevel == 1,
                "legacy save retains preferences and selected Moon mission");
            checks.Require(migrated.tutorial.completed && migrated.tutorial.independent && !migrated.tutorial.efficient && migrated.tutorial.bestAttempts == 3 &&
                migrated.moon.completed && !migrated.moon.independent && migrated.moon.efficient && migrated.moon.bestAttempts == 2,
                "legacy delivery, independently earned badges and best attempts survive migration");
            checks.Require(migrated.CompletedCount == 2 && migrated.TotalBadges == 4 && !migrated.AllCompleted,
                "legacy two-mission completion does not auto-complete the new chapter");
            checks.Require(migrated.IsUnlocked(OrbitalLevel.Crossing) && !migrated.IsUnlocked(OrbitalLevel.Reverse),
                "legacy completion unlocks exactly the next added mission");
            for (int index = 2; index < OrbitalCampaign.Count; index++)
                checks.Require(!migrated.For((OrbitalLevel)index).completed && migrated.For((OrbitalLevel)index).BadgeCount == 0,
                    "new mission " + index + " begins without invented awards");
            migrated.For(OrbitalLevel.Crossing).Complete(1, false);
            checks.Require(migrated.CompletedCount == 3 && migrated.TotalBadges == 7 &&
                !migrated.For(OrbitalLevel.Reverse).completed && migrated.tutorial.bestAttempts == 3 && migrated.moon.bestAttempts == 2,
                "new mission awards do not alias another new record or either legacy record");
            migrated.lastLevel = 2;
            migrated.Save(); migrated.Save();
            PlayerPrefs.SetString(OrbitalProgress.StorageKey, "{ broken json");
            var recovered = OrbitalProgress.Load();
            checks.Require(recovered.CompletedCount == 3 && recovered.TotalBadges == 7 && recovered.ResumeLevel == 2 && !recovered.sound,
                "expanded progress and preferences recover from the valid backup");
            PlayerPrefs.SetString(OrbitalProgress.StorageKey, "{\"version\":999}");
            checks.Require(OrbitalProgress.Load().CompletedCount == 3, "unsupported primary version still uses the valid expanded backup");
            PlayerPrefs.SetString(OrbitalProgress.StorageKey + ".backup", "broken backup");
            var fresh = OrbitalProgress.Load();
            checks.Require(fresh.CompletedCount == 0 && fresh.TotalBadges == 0 && fresh.IsUnlocked(OrbitalLevel.Tutorial) &&
                !fresh.IsUnlocked(OrbitalLevel.Moon) && fresh.ResumeLevel == 0,
                "two corrupt save slots create a consistent fresh, locked chapter");
        }

        static void RequireDelivery(OrbitalPrototype game, Checks checks, string context)
        {
            checks.Require(game.State.Phase == OrbitalPhase.Success && game.State.ResultCode == "success" && game.State.HasA && game.State.HasB,
                context + ": both required relays and the physical destination were reached");
            bool gate = game.State.Level == OrbitalLevel.Moon || game.State.Level == OrbitalLevel.Bridge || game.State.Level == OrbitalLevel.Finale;
            checks.Require(game.State.GateCrossed == gate, context + ": gate crossing matches this mission's authored requirement");
            checks.Require(game.State.HasC == (game.State.Level == OrbitalLevel.Finale), context + ": only final delivery completes calibration C");
        }

        static void Fly(OrbitalPrototype game, Checks checks, string label)
        {
            checks.Require(game.Launch(), label + ": fully assembled route accepts launch");
            var simulation = Physics.simulationMode;
            var ball = game.Ball;
            var interpolation = ball.interpolation;
            try
            {
                Physics.simulationMode = SimulationMode.Script;
                ball.interpolation = RigidbodyInterpolation.None;
                Physics.SyncTransforms();
                for (int step = 0; step < 1500 && game.State.Phase == OrbitalPhase.Running; step++)
                {
                    OrbitalVerificationScope.Call(game, "TickPhysics", .02f);
                    Physics.Simulate(.02f);
                }
                checks.Require(game.State.Phase == OrbitalPhase.Success || game.State.Phase == OrbitalPhase.Failure,
                    label + ": live simulation reaches a terminal outcome; " + game.StatusDescription);
            }
            finally
            {
                Physics.simulationMode = simulation;
                if (ball != null) ball.interpolation = interpolation;
            }
        }

        static void Click(OrbitalPrototype game, OrbitalControlKind kind, int value, Checks checks)
        {
            Refresh(game);
            var control = Control(game, kind, value);
            var target = control.GetComponent<OrbitalClickTarget>();
            checks.Require(control.gameObject.activeInHierarchy && target.interactable, kind + ": visible enabled action exists");
            typeof(OrbitalClickTarget).GetField("m_LastActivationFrame", Private).SetValue(target, -1);
            target.Activate();
            Refresh(game);
        }

        static OrbitalControl Control(OrbitalPrototype game, OrbitalControlKind kind, int value)
        {
            foreach (var control in game.GetComponentsInChildren<OrbitalControl>(true))
                if (control.owner == game && control.kind == kind && control.value == value) return control;
            throw new InvalidOperationException("Rebuild chapter UI; missing " + kind + " " + value);
        }

        static void Refresh(OrbitalPrototype game)
        {
            OrbitalVerificationScope.Call(game, "RefreshInterface");
            OrbitalVerificationScope.Call(game, "RefreshExperience");
        }
    }
}
