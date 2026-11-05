using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace SpaceBall.Editor
{
    /// <summary>
    /// Tests the live construction workbench and real PhysX flights. The reference
    /// helper only places authored track parts; delivery still requires the ball
    /// to trigger every mission sensor. This suite does not inject native input.
    /// </summary>
    public static class OrbitalConstructionVerification
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [Serializable]
        sealed class Report
        {
            public string status = "passed", failure = "";
            public string mode, nativeInput = "Not exercised: Editor manipulation/click APIs and PhysX only";
            public string retention = "Not inferred from automated verification";
            public int assertions, constructionLevels, correctFlightRepeats;
            public string[] scenarios;
            public Flight[] flights;
        }

        [Serializable]
        sealed class Flight
        {
            public string level, scenario, expected, result, feedback, details, placements;
            public float elapsed;
            public bool a, b, gateCrossed, c;
        }

        sealed class Checks
        {
            public int count;
            public readonly List<string> scenarios = new();
            public void Require(bool condition, string message)
            {
                count++;
                if (!condition) throw new InvalidOperationException("Construction verification failed: " + message);
            }
            public void At(Vector3 actual, Vector3 expected, string message)
            {
                Require((actual - expected).sqrMagnitude < .000001f,
                    message + "; actual=" + actual.ToString("F4") + ", expected=" + expected.ToString("F4"));
            }
        }

        struct Pose
        {
            public int tile, socket, turns;
            public Vector3 position;
            public Quaternion rotation;
        }

        readonly struct SavedString
        {
            public readonly string key, value;
            public readonly bool existed;
            public SavedString(string key)
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
            public bool Matches() => PlayerPrefs.HasKey(key) == existed && PlayerPrefs.GetString(key, "") == value;
        }

        sealed class Session : IDisposable
        {
            readonly OrbitalPrototype game;
            readonly object originalProfile;
            readonly OrbitalLevel originalLevel;
            readonly bool originalPreferences, originalSuspension;
            readonly SavedString main, backup;
            public Session(OrbitalPrototype game)
            {
                this.game = game;
                originalProfile = Field("progress").GetValue(game);
                originalLevel = game.State.Level;
                originalPreferences = (bool)Field("preferencesOpen").GetValue(game);
                originalSuspension = (bool)Field("applicationSuspended").GetValue(game);
                main = new SavedString(OrbitalProgress.StorageKey);
                backup = new SavedString(OrbitalProgress.StorageKey + ".backup");
                Field("progress").SetValue(game, new OrbitalProgress());
                Field("preferencesOpen").SetValue(game, false);
                Call(game, "OnApplicationPause", false);
                game.ResetAttempt();
            }
            public void Dispose()
            {
                try
                {
                    if (game == null) return;
                    Call(game, "OnApplicationPause", false);
                    for (int index = 0; index < OrbitalCampaign.Count; index++)
                    {
                        game.ResetAttempt();
                        game.SelectLevel((OrbitalLevel)index);
                        game.Assembly?.Clear();
                        game.Construction?.Clear();
                    }
                    game.SelectLevel(originalLevel);
                    game.ResetAttempt();
                }
                finally
                {
                    try
                    {
                        if (game != null)
                        {
                            Field("progress").SetValue(game, originalProfile);
                            Field("preferencesOpen").SetValue(game, originalPreferences);
                            Field("applicationSuspended").SetValue(game, originalSuspension);
                            // Re-seat the player's original draft after test-board
                            // cleanup. Restore suppresses notifications/saves; the
                            // exact original preference strings are restored last.
                            if (game.Construction != null) Call(game, "RestoreConstructionBlueprint");
                        }
                    }
                    finally { main.Restore(); backup.Restore(); PlayerPrefs.Save(); }
                    if (game != null) Call(game, "RefreshExperience");
                }
            }
        }

        public static string Run(int repetitions = 5) => RunInternal(true, true, repetitions, "construction-verification.json");
        public static string RunInteractions() => RunInternal(true, false, 0, "construction-interaction-verification.json");
        public static string RunPhysics(int repetitions = 5) => RunInternal(false, true, repetitions, "construction-physics-verification.json");

        public static string RunSpatialVolume()
        {
            if (!Application.isPlaying) throw new InvalidOperationException("Spatial verification requires Play mode.");
            var game = UnityEngine.Object.FindFirstObjectByType<OrbitalPrototype>();
            var camera = UnityEngine.Object.FindFirstObjectByType<ByteDance.PICO.SpatialAdapter.SpatialCamera>();
            if (game == null || camera == null) throw new InvalidOperationException("Spatial verification needs the chapter and its spatial camera.");
            if (game.IsReplaying || game.State.Phase == OrbitalPhase.Running || game.State.Phase == OrbitalPhase.Paused)
                throw new InvalidOperationException("Finish the player's current flight before spatial verification.");
            var checks = new Checks();
            var report = new Report { mode = "Spatial volume, full-piece edge constraints and real socket drops" };
            var main = new SavedString(OrbitalProgress.StorageKey);
            var backup = new SavedString(OrbitalProgress.StorageKey + ".backup");
            string originalProfile = JsonUtility.ToJson(game.Profile);
            int placements = 0, extremeDrags = 0;
            try
            {
                checks.Require(camera.Dimensions == camera.OutputDimensions, "reference and output dimensions preserve 1:1 scale");
                using (new Session(game))
                {
                    for (int level = 0; level < OrbitalCampaign.Count; level++)
                    {
                        Enter(game, (OrbitalLevel)level);
                        if (game.Construction != null)
                        {
                            var kit = game.Construction;
                            foreach (var tile in kit.tiles)
                                for (int turn = 0; turn < 4; turn++)
                                {
                                    tile.SetPlacement(-1, turn);
                                    VerifyWindowEdges(tile, camera, checks); extremeDrags += 8;
                                    for (int socket = 0; socket < kit.sockets.Length; socket++)
                                    {
                                        tile.SetPlacement(-1, turn);
                                        Vector3 anchor = tile.transform.position;
                                        checks.Require(tile.BeginManipulation(anchor), "capture tile before valid drop");
                                        tile.UpdateManipulation(kit.sockets[socket].transform.position);
                                        RequireInsideWindow(tile, camera, checks);
                                        tile.EndManipulation(false);
                                        checks.Require(tile.Installed && tile.SocketIndex == socket && tile.QuarterTurns == turn,
                                            "every tile/socket/orientation remains reachable by dragging");
                                        RequireInsideWindow(tile, camera, checks);
                                        placements++;
                                        tile.ResetToTray();
                                    }
                                }
                        }
                        else
                            foreach (var piece in game.Assembly.pieces)
                                for (int choice = 0; choice < 2; choice++)
                                {
                                    piece.SetInstalled(false); piece.SetChoice(choice); piece.SettlePresentation();
                                    VerifyWindowEdges(piece, camera, checks); extremeDrags += 8;
                                    Vector3 anchor = piece.transform.position;
                                    checks.Require(piece.BeginManipulation(anchor), "capture tutorial part before valid drop");
                                    piece.UpdateManipulation(piece.transform.parent.TransformPoint(piece.SocketPosition));
                                    RequireInsideWindow(piece, camera, checks);
                                    piece.EndManipulation(false);
                                    checks.Require(piece.Installed && piece.Choice == choice, "tutorial socket remains reachable without changing choice");
                                    piece.SettlePresentation();
                                    RequireInsideWindow(piece, camera, checks);
                                    placements++;
                                }
                    }
                }
                checks.Require(main.Matches() && backup.Matches(), "spatial verification restores both player save keys");
                checks.Require(JsonUtility.ToJson(game.Profile) == originalProfile, "spatial verification restores the player profile");
                checks.scenarios.Add("Six levels: " + placements + " actual socket drops and " + extremeDrags + " extreme diagonal drag positions");
                checks.scenarios.Add("Whole mesh/collider bounds remain inside " + camera.Dimensions.ToString("F3") + "; captured motion cancels back to the original pose");
            }
            catch (Exception exception) { report.status = "failed"; report.failure = exception.Message; throw; }
            finally
            {
                report.assertions = checks.count; report.scenarios = checks.scenarios.ToArray();
                string folder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Builds", "SpaceBall");
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "spatial-volume-interaction-verification.json"), JsonUtility.ToJson(report, true));
            }
            return JsonUtility.ToJson(report, true);
        }

        static void VerifyWindowEdges(OrbitalManipulable part, ByteDance.PICO.SpatialAdapter.SpatialCamera camera, Checks checks)
        {
            Vector3 anchor = part.transform.position;
            checks.Require(part.BeginManipulation(anchor), "capture full part for edge check");
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 delta = new Vector3((corner & 1) == 0 ? -10 : 10, (corner & 2) == 0 ? -10 : 10, (corner & 4) == 0 ? -10 : 10);
                part.UpdateManipulation(anchor + part.transform.parent.TransformVector(delta));
                RequireInsideWindow(part, camera, checks);
            }
            part.EndManipulation(true);
            checks.At(part.transform.position, anchor, "cancel returns the part to its pre-capture pose");
        }

        static void RequireInsideWindow(OrbitalManipulable part, ByteDance.PICO.SpatialAdapter.SpatialCamera camera, Checks checks)
        {
            Bounds bounds = OrbitalManipulable.SpatialBounds(part.transform);
            Matrix4x4 matrix = camera.transform.worldToLocalMatrix * part.transform.localToWorldMatrix;
            Vector3 half = camera.Dimensions * .5f + Vector3.one * .0001f;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = matrix.MultiplyPoint3x4(bounds.center + Vector3.Scale(bounds.extents,
                    new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1)));
                checks.Require(Mathf.Abs(point.x) <= half.x && Mathf.Abs(point.y) <= half.y && Mathf.Abs(point.z) <= half.z,
                    part.name + " mesh/collider is inside the spatial window at " + point.ToString("F4"));
            }
        }

        static string RunInternal(bool interactions, bool physics, int repetitions, string fileName)
        {
            if (!Application.isPlaying) throw new InvalidOperationException("Construction verification requires Play mode.");
            var game = UnityEngine.Object.FindFirstObjectByType<OrbitalPrototype>();
            if (game == null) throw new InvalidOperationException("No active OrbitalPrototype exists.");
            if (game.IsReplaying || game.State.Phase == OrbitalPhase.Running || game.State.Phase == OrbitalPhase.Paused)
                throw new InvalidOperationException("Finish the player's current flight before verification.");
            repetitions = Mathf.Clamp(repetitions, 1, 20);
            var report = new Report { mode = interactions && physics ? "Interaction + physical flight regression" : interactions ? "Interaction regression" : "Physical flight regression" };
            var checks = new Checks();
            var flights = new List<Flight>();
            var main = new SavedString(OrbitalProgress.StorageKey);
            var backup = new SavedString(OrbitalProgress.StorageKey + ".backup");
            string originalProfile = JsonUtility.ToJson(game.Profile);
            try
            {
                using (new Session(game))
                {
                    VerifySaveMigration(checks);
                    VerifyBlueprintPersistence(game, checks);
                    if (interactions)
                        for (int index = 2; index < OrbitalCampaign.Count; index++)
                            VerifyWorkbench(game, (OrbitalLevel)index, checks);
                    if (physics)
                    {
                        VerifyLegacyPhysics(game, checks, flights);
                        for (int index = 2; index < OrbitalCampaign.Count; index++)
                        {
                            VerifyPhysicalLevel(game, (OrbitalLevel)index, repetitions, checks, flights);
                            report.correctFlightRepeats += repetitions;
                        }
                        VerifyFinaleBranch(game, checks, flights);
                        VerifyEquivalentPieces(game, checks, flights);
                        VerifyChapter(game, checks, flights);
                    }
                    report.constructionLevels = OrbitalCampaign.Count - 2;
                }
                checks.Require(main.Matches() && backup.Matches(), "verification restores both player save slots exactly");
                checks.Require(JsonUtility.ToJson(game.Profile) == originalProfile, "verification restores the player's original in-memory profile");
                if (game.Construction != null)
                    RequireDraftMatchesRecord(game.Construction, game.Profile.For(game.State.Level), checks, "restored player blueprint");
                checks.scenarios.Add("Player main/backup keys, original profile and active saved construction draft restored; no QA completion is left in the real journey");
            }
            catch (Exception exception)
            {
                report.status = "failed";
                report.failure = exception.Message;
                throw;
            }
            finally
            {
                report.assertions = checks.count;
                report.scenarios = checks.scenarios.ToArray();
                report.flights = flights.ToArray();
                string folder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Builds", "SpaceBall");
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, fileName), JsonUtility.ToJson(report, true));
            }
            return JsonUtility.ToJson(report, true);
        }

        static void VerifySaveMigration(Checks checks)
        {
            const string complete = "{\"completed\":true,\"independent\":true,\"efficient\":true,\"bestAttempts\":1}";
            string oldSix = "{\"version\":1,\"lastLevel\":5,\"sound\":false,\"reducedMotion\":true,\"trace\":false," +
                "\"tutorial\":" + complete + ",\"moon\":" + complete + ",\"expedition\":[" + complete + "," + complete + "," + complete + "," + complete + "]}";
            PlayerPrefs.SetString(OrbitalProgress.StorageKey, oldSix);
            PlayerPrefs.DeleteKey(OrbitalProgress.StorageKey + ".backup");
            var migrated = OrbitalProgress.Load();
            checks.Require(migrated.constructionRevision == OrbitalProgress.CurrentConstructionRevision,
                "old switch-only save receives the current construction revision");
            checks.Require(migrated.tutorial.completed && migrated.moon.completed && migrated.CompletedCount == 2 && migrated.TotalBadges == 6,
                "migration retains both unchanged missions but does not credit solutions of four replacement puzzles");
            checks.Require(!migrated.sound && migrated.reducedMotion && !migrated.trace && migrated.ResumeLevel == 2,
                "migration preserves preferences and resumes at the first newly constructed puzzle");
            for (int index = 2; index < OrbitalCampaign.Count; index++)
                checks.Require(!migrated.For((OrbitalLevel)index).completed && migrated.For((OrbitalLevel)index).BadgeCount == 0,
                    "replacement mission " + index + " begins without switch-only awards");
            checks.Require(migrated.IsUnlocked(OrbitalLevel.Crossing) && !migrated.IsUnlocked(OrbitalLevel.Reverse),
                "migration unlocks only the first replacement puzzle");
            migrated.For(OrbitalLevel.Crossing).Complete(2, false);
            migrated.lastLevel = 2;
            migrated.Save(); migrated.Save();
            var loaded = OrbitalProgress.Load();
            checks.Require(loaded.CompletedCount == 3 && loaded.For(OrbitalLevel.Crossing).bestAttempts == 2 && loaded.TotalBadges == 9,
                "current-revision construction scores survive a normal save/load without being cleared again");
            PlayerPrefs.SetString(OrbitalProgress.StorageKey, "broken primary");
            checks.Require(OrbitalProgress.Load().CompletedCount == 3,
                "current-revision construction scores recover from the valid backup");
            checks.scenarios.Add("Old six-switch-mission saves retain Tutorial/Moon/preferences, invalidate only replaced puzzles, and current construction scores round-trip");
        }

        static void VerifyBlueprintPersistence(OrbitalPrototype game, Checks checks)
        {
            var profile = new OrbitalProgress { sound = false, reducedMotion = true, trace = false };
            profile.tutorial.Complete(1, false); profile.moon.Complete(1, false);
            game.ResetAttempt(); game.Construction?.Clear();
            Field("progress").SetValue(game, profile);
            Enter(game, OrbitalLevel.Crossing);
            var kit = game.Construction;
            kit.Clear();
            var record = game.Profile.For(OrbitalLevel.Crossing);
            // Enter saves the journey selection, but an untouched tray has no
            // construction edit to persist and must not allocate draft arrays.
            checks.Require(record.tileSockets == null && record.tileTurns == null,
                "untouched construction tray does not create an empty draft on first entry");
            checks.Require(kit.Place(2, 0, 2), "an intentionally unsolved right-turn track may occupy the entry socket");
            kit.Select(1);
            for (int turn = 0; turn < 3; turn++) kit.Rotate(kit.tiles[1]);
            checks.Require(!record.completed && record.BadgeCount == 0 && game.ChallengeAttempts == 0,
                "saving an unsolved draft grants no completion, badge or launch attempt");
            checks.Require(record.tileSockets != null && record.tileSockets.Length == kit.tiles.Length &&
                record.tileSockets[2] == 0 && record.tileTurns[2] == 2 && record.tileSockets[1] == -1 && record.tileTurns[1] == 3,
                "draft includes wrong placement and pre-rotated tray pieces without filtering by the authored answer");
            Pose[] expected = Capture(kit);
            int[] savedSockets = record.tileSockets, savedTurns = record.tileTurns;
            var main = new SavedString(OrbitalProgress.StorageKey);
            var backup = new SavedString(OrbitalProgress.StorageKey + ".backup");
            Call(game, "SaveConstructionBlueprint"); Call(game, "SaveConstructionBlueprint");
            checks.Require(ReferenceEquals(savedSockets, record.tileSockets) && ReferenceEquals(savedTurns, record.tileTurns) &&
                main.Matches() && backup.Matches(), "saving an unchanged draft neither reallocates its arrays nor rewrites either preference slot");

            game.SelectLevel(OrbitalLevel.Moon); game.SelectLevel(OrbitalLevel.Crossing);
            RequireSame(game.Construction, expected, checks, "switch away and back restores unsolved layout");
            checks.Require(game.Construction.selected == -1, "restoring a draft starts with no stale selection");
            record = game.Profile.For(OrbitalLevel.Crossing);
            main = new SavedString(OrbitalProgress.StorageKey); backup = new SavedString(OrbitalProgress.StorageKey + ".backup");
            string beforeRestore = JsonUtility.ToJson(game.Profile);
            Call(game, "RestoreConstructionBlueprint");
            checks.Require(main.Matches() && backup.Matches() && JsonUtility.ToJson(game.Profile) == beforeRestore,
                "restore is read-only for the profile and both save slots despite Clear/Place notifications");
            RequireSame(game.Construction, expected, checks, "explicit restore uses saved player placements");

            var reloaded = OrbitalProgress.Load();
            Field("progress").SetValue(game, reloaded);
            game.SelectLevel(OrbitalLevel.Moon); game.SelectLevel(OrbitalLevel.Crossing);
            kit = game.Construction; record = game.Profile.For(OrbitalLevel.Crossing);
            RequireSame(kit, expected, checks, "new profile instance restores the persisted layout");
            checks.Require(game.Profile.CompletedCount == 2 && game.Profile.TotalBadges == 6 && !game.Profile.sound &&
                game.Profile.reducedMotion && !game.Profile.trace, "blueprint round-trip preserves unchanged mission awards and preferences");

            string heldProfile = JsonUtility.ToJson(game.Profile);
            main = new SavedString(OrbitalProgress.StorageKey); backup = new SavedString(OrbitalProgress.StorageKey + ".backup");
            var held = kit.tiles[2];
            Vector3 anchor = held.transform.position;
            checks.Require(held.BeginManipulation(anchor), "saved installed track begins a captured movement");
            held.UpdateManipulation(anchor + kit.transform.TransformVector(new Vector3(.11f, .04f, .08f)));
            Call(game, "SaveConstructionBlueprint");
            checks.Require(main.Matches() && backup.Matches() && JsonUtility.ToJson(game.Profile) == heldProfile,
                "a held intermediate pose never overwrites the seated draft");
            held.EndManipulation(true);
            RequireSame(kit, expected, checks, "capture cancellation restores saved socket and orientation");
            checks.Require(main.Matches() && backup.Matches() && JsonUtility.ToJson(game.Profile) == heldProfile,
                "cancelling manipulation leaves the saved draft untouched");

            anchor = held.transform.position;
            checks.Require(held.BeginManipulation(anchor), "switch-during-capture setup");
            held.UpdateManipulation(anchor + kit.transform.TransformVector(new Vector3(-.12f, .02f, .06f)));
            game.SelectLevel(OrbitalLevel.Moon); game.SelectLevel(OrbitalLevel.Crossing);
            kit = game.Construction;
            RequireSame(kit, expected, checks, "switching boards during a capture saves only its cancelled seated pose");
            checks.Require(!kit.HasHeld, "switching boards leaves no captured track in the restored draft");
            game.ResetAttempt();
            RequireSame(kit, expected, checks, "retry preserves the saved physical blueprint");

            // Review mode is intentionally read-only. Exercise its save guard
            // without launching an Android intent or changing any mission sensor.
            var reviewSetter = typeof(OrbitalPrototype).GetProperty("IsReviewSession").GetSetMethod(true);
            bool wasReview = game.IsReviewSession;
            string reviewProfile = JsonUtility.ToJson(game.Profile);
            main = new SavedString(OrbitalProgress.StorageKey); backup = new SavedString(OrbitalProgress.StorageKey + ".backup");
            try
            {
                reviewSetter.Invoke(game, new object[] { true });
                kit.tiles[1].SetPlacement(-1, 1);
                Call(game, "SaveConstructionBlueprint");
                checks.Require(main.Matches() && backup.Matches() && JsonUtility.ToJson(game.Profile) == reviewProfile,
                    "isolated review mode cannot write a changed physical arrangement into player drafts");
            }
            finally { reviewSetter.Invoke(game, new object[] { wasReview }); Call(game, "RestoreConstructionBlueprint"); }
            RequireSame(kit, expected, checks, "review save guard leaves the saved draft available to restore");

            kit.Rotate(kit.tiles[2]);
            int editedTurn = kit.tiles[2].QuarterTurns;
            Call(game, "OnApplicationPause", true);
            try
            {
                var pausedLoad = OrbitalProgress.Load();
                checks.Require(pausedLoad.For(OrbitalLevel.Crossing).tileTurns[2] == editedTurn &&
                    pausedLoad.For(OrbitalLevel.Crossing).BadgeCount == 0,
                    "pause preserves the latest unsolved edit without earning a result");
            }
            finally { Call(game, "OnApplicationPause", false); }

            kit.Clear();
            record = game.Profile.For(OrbitalLevel.Crossing);
            checks.Require(record.tileSockets != null && record.tileTurns != null, "clearing an existing draft explicitly persists its empty replacement");
            for (int index = 0; index < kit.tiles.Length; index++)
                checks.Require(record.tileSockets[index] == -1 && record.tileTurns[index] == 0,
                    "cleared draft resets every socket and tray orientation");
            game.SelectLevel(OrbitalLevel.Moon); game.SelectLevel(OrbitalLevel.Crossing);
            checks.Require(game.Construction.PlacedCount == 0, "cleared workbench does not resurrect the older draft after switching");
            RequireDraftMatchesRecord(game.Construction, game.Profile.For(OrbitalLevel.Crossing), checks, "persisted empty replacement");
            VerifyCorruptBlueprints(checks);
            checks.scenarios.Add("Unsolved player blueprints, tray rotations, switch/load/retry/pause restoration, unchanged-save deduplication, capture cancellation, review isolation and explicit clearing");
        }

        static void VerifyCorruptBlueprints(Checks checks)
        {
            var profile = new OrbitalProgress { sound = false, reducedMotion = true, trace = false };
            profile.tutorial.Complete(4, true); profile.moon.Complete(2, false);
            var crossing = profile.For(OrbitalLevel.Crossing);
            crossing.Complete(2, false); crossing.tileSockets = new[] { 0, 0, 99 }; crossing.tileTurns = new[] { -1, 5, 10 };
            var reverse = profile.For(OrbitalLevel.Reverse);
            reverse.Complete(3, true); reverse.tileSockets = new[] { 0 }; reverse.tileTurns = new[] { 0 };
            var bridge = profile.For(OrbitalLevel.Bridge);
            bridge.tileSockets = null; bridge.tileTurns = new[] { 0, 0, 0, 0, 0 };
            var finale = profile.For(OrbitalLevel.Finale);
            finale.tileSockets = new[] { -2, 1, 1, 99, 0, -1 }; finale.tileTurns = new[] { -1, 4, 5, 6, 7, 8 };
            int completed = profile.CompletedCount, badges = profile.TotalBadges;
            PlayerPrefs.SetString(OrbitalProgress.StorageKey, JsonUtility.ToJson(profile));
            var loaded = OrbitalProgress.Load();
            checks.Require(loaded.CompletedCount == completed && loaded.TotalBadges == badges &&
                loaded.tutorial.bestAttempts == 4 && loaded.moon.bestAttempts == 2 && !loaded.sound && loaded.reducedMotion && !loaded.trace,
                "sanitizing corrupt construction arrays preserves mission records, badges and preferences");
            crossing = loaded.For(OrbitalLevel.Crossing);
            checks.Require(crossing.tileSockets[0] == 0 && crossing.tileSockets[1] == -1 && crossing.tileSockets[2] == -1 &&
                crossing.tileTurns[0] == 3 && crossing.tileTurns[1] == 1 && crossing.tileTurns[2] == 2,
                "duplicate/out-of-range socket IDs return only invalid tiles to tray and turns normalize modulo four");
            checks.Require(loaded.For(OrbitalLevel.Reverse).tileSockets == null && loaded.For(OrbitalLevel.Reverse).tileTurns == null &&
                loaded.For(OrbitalLevel.Reverse).completed && loaded.For(OrbitalLevel.Reverse).bestAttempts == 3,
                "wrong array lengths clear only the draft, retaining that mission's valid score");
            checks.Require(loaded.For(OrbitalLevel.Bridge).tileSockets == null && loaded.For(OrbitalLevel.Bridge).tileTurns == null,
                "one missing parallel array clears the incomplete blueprint pair");
            finale = loaded.For(OrbitalLevel.Finale);
            int[] sockets = { -1, 1, -1, -1, 0, -1 }, turns = { 3, 0, 1, 2, 3, 0 };
            for (int index = 0; index < sockets.Length; index++)
                checks.Require(finale.tileSockets[index] == sockets[index] && finale.tileTurns[index] == turns[index],
                    "finale blueprint sanitizes invalid/duplicate entry " + index + " independently");
            string oldRevision = JsonUtility.ToJson(profile).Replace("\"constructionRevision\":" + OrbitalProgress.CurrentConstructionRevision,
                "\"constructionRevision\":0");
            PlayerPrefs.SetString(OrbitalProgress.StorageKey, oldRevision);
            loaded = OrbitalProgress.Load();
            checks.Require(loaded.CompletedCount == 2 && loaded.tutorial.bestAttempts == 4 && loaded.moon.bestAttempts == 2,
                "old construction revision still preserves unchanged missions while invalidating replaced puzzles");
            for (int index = 2; index < OrbitalCampaign.Count; index++)
            {
                var record = loaded.For((OrbitalLevel)index);
                checks.Require(!record.completed && record.tileSockets == null && record.tileTurns == null,
                    "old-revision mission " + index + " retains neither stale puzzle awards nor stale physical placements");
            }
        }

        static void VerifyWorkbench(OrbitalPrototype game, OrbitalLevel level, Checks checks)
        {
            Enter(game, level);
            var kit = game.Construction;
            string context = level + ": ";
            checks.Require(kit != null && game.Assembly == null, context + "uses actual movable track construction instead of the legacy two-switch rig");
            checks.Require(kit.tiles.Length == (int)level + 1 && kit.sockets.Length > kit.tiles.Length,
                context + "piece progression is 3/4/5/6 and each board offers an unfilled spatial socket");
            kit.Clear();
            checks.Require(kit.Editable && kit.PlacedCount == 0 && !kit.HasHeld && kit.selected == -1,
                context + "clear restores an empty editable workbench");
            checks.Require(!game.Launch(), context + "empty workbench does not launch");
            for (int index = 0; index < kit.tiles.Length; index++)
            {
                var tile = kit.tiles[index];
                checks.Require(tile != null && tile.kit == kit && tile.index == index && tile.trackColliders.Length > 0,
                    context + "tile " + index + " owns real track colliders");
                checks.Require(HasSelectionCollider(tile.gameObject), context + "tile " + index + " has a selectable trigger on Default/UI");
                foreach (var collider in tile.trackColliders)
                    checks.Require(collider != null && collider.transform.IsChildOf(tile.transform) && !collider.enabled &&
                        collider.gameObject.layer == LayerMask.NameToLayer("Ignore Raycast"),
                        context + "tray track colliders are disabled, owned by their movable tile and ignore selection rays");
                checks.At(tile.transform.localPosition, tile.homePosition, context + "tile " + index + " is seated in its tray");
            }
            foreach (var socket in kit.sockets)
                checks.Require(socket != null && HasSelectionCollider(socket.gameObject) && socket.GetComponent<OrbitalClickTarget>() != null,
                    context + "every spatial socket is targetable before any track is installed");

            // Test each shape on the same socket. Acceptance never looks up the answer.
            for (int index = 0; index < kit.tiles.Length; index++)
            {
                checks.Require(kit.Place(index, 0, index % 4), context + "socket accepts tile " + index + " regardless of shape");
                checks.Require(kit.tiles[index].SocketIndex == 0, context + "accepted tile really moves to the chosen socket");
                kit.Select(index); kit.ReturnSelectedToTray();
                checks.Require(!kit.tiles[index].Installed, context + "selected tile can return to tray");
            }
            kit.Clear();
            var first = kit.tiles[0];
            Press(first, checks, context + "first tap");
            checks.Require(kit.selected == 0 && first.QuarterTurns == 0, context + "first tap selects instead of unexpectedly rotating");
            Press(first, checks, context + "second tap");
            checks.Require(kit.selected == 0 && first.QuarterTurns == 1, context + "second tap turns the selected physical tile by 90 degrees");
            Click(kit.sockets[0].GetComponent<OrbitalClickTarget>());
            checks.Require(first.Installed && first.SocketIndex == 0 && first.QuarterTurns == 1,
                context + "system-click fallback installs the selected tile through the socket target");
            Click(kit.sockets[1].GetComponent<OrbitalClickTarget>());
            checks.Require(first.SocketIndex == 1 && !kit.IsOccupied(0), context + "click fallback relocates an installed tile to another empty socket");
            checks.Require(kit.Place(1, 0, 0), context + "second tile installs in the released socket");
            checks.Require(!kit.Place(1, 1, 2) && kit.tiles[1].SocketIndex == 0 && first.SocketIndex == 1,
                context + "occupied socket rejects another tile without moving either one");
            kit.Clear();
            checks.Require(kit.Place(0, 0, 2), context + "drag setup installs a rotated tile");
            var seated = first.transform.position;
            Drag(first, kit.sockets[1].transform.position, true, checks, context + "cancelled relocation");
            checks.Require(first.SocketIndex == 0 && first.QuarterTurns == 2, context + "cancel retains original socket and orientation");
            checks.At(first.transform.position, seated, context + "cancel restores exact seated pose");
            Drag(first, kit.sockets[1].transform.position, false, checks, context + "valid relocation");
            checks.Require(first.SocketIndex == 1 && first.QuarterTurns == 2,
                context + "drag to a real free socket relocates without rotating");
            checks.At(first.transform.position, kit.sockets[1].transform.position, context + "relocated track is exactly seated");
            Vector3 verticalMiss = VerticalMiss(kit, kit.sockets[0].transform.position, checks, context);
            Drag(first, verticalMiss, false, checks, context + "wrong height");
            checks.Require(first.SocketIndex == 1 && first.QuarterTurns == 2,
                context + "same-XZ drop outside the height tolerance is not accepted as a planar snap");
            checks.At(first.transform.position, kit.sockets[1].transform.position, context + "wrong-height drop restores its seat");
            checks.Require(kit.Place(1, 0, 0), context + "occupied drag setup");
            Drag(first, kit.sockets[0].transform.position, false, checks, context + "occupied drop");
            checks.Require(first.SocketIndex == 1 && kit.tiles[1].SocketIndex == 0,
                context + "occupied drag preserves both placements");
            int turns = first.QuarterTurns;
            kit.Select(0); kit.ReturnSelectedToTray();
            checks.Require(!first.Installed && first.QuarterTurns == turns, context + "returning to tray preserves the planned orientation");
            kit.Clear();
            checks.Require(first.QuarterTurns == 0 && kit.selected == -1, context + "full Clear resets orientations and selection");
            checks.Require(kit.Place(0, 0, 0), context + "held launch setup");
            checks.Require(first.BeginManipulation(first.transform.position) && kit.HasHeld,
                context + "installed track can be captured for manipulation");
            foreach (var collider in first.trackColliders) checks.Require(!collider.enabled, context + "held track cannot collide with a future ball");
            checks.Require(!game.Launch() && game.State.Phase == OrbitalPhase.Configuring,
                context + "holding any track blocks launch");
            game.ResetAttempt();
            checks.Require(!kit.HasHeld && first.Installed && first.SocketIndex == 0,
                context + "reset cancels capture and restores the installed track");
            foreach (var collider in first.trackColliders) checks.Require(collider.enabled, context + "seated track collider is restored after capture cancellation");

            kit.Clear();
            var second = kit.tiles[1];
            checks.Require(first.BeginManipulation(first.transform.position), context + "older overlapping capture begins");
            Press(second, checks, context + "newer overlapping tap");
            checks.Require(kit.selected == 1, context + "newer tap owns the selection");
            first.EndManipulation(true);
            checks.Require(kit.selected == 1 && !first.IsManipulating && !second.IsManipulating,
                context + "older cancellation cannot erase the newer selection");

            Reference(game, level, checks);
            checks.Require(game.Launch(), context + "placed route launches before manipulation-lock checks");
            VerifyLocked(game, checks, context + "running");
            game.TogglePause();
            checks.Require(game.State.Phase == OrbitalPhase.Paused, context + "flight can pause");
            VerifyLocked(game, checks, context + "paused");
            game.ResetAttempt();
            checks.Require(kit.Editable && !kit.HasHeld, context + "reset restores construction editing");
            kit.Clear();
            checks.scenarios.Add(context + "shape-independent placement, click relocation, four-way orientation, height-aware drag/cancel/occupancy, held launch and phase locks");
        }

        static void VerifyLocked(OrbitalPrototype game, Checks checks, string context)
        {
            var kit = game.Construction;
            Pose[] before = Capture(kit);
            int selected = kit.selected;
            checks.Require(!kit.Editable && !kit.tiles[0].BeginManipulation(kit.tiles[0].transform.position), context + ": cannot capture a track");
            kit.Select(0); kit.Rotate(kit.tiles[0]); kit.ReturnSelectedToTray(); kit.Clear();
            checks.Require(!kit.Place(0, 0, 3), context + ": cannot place while locked");
            kit.tiles[0].SetPlacement(-1, 2);
            RequireSame(kit, before, checks, context + ": direct APIs cannot mutate the physical layout");
            checks.Require(kit.selected == selected && !kit.HasHeld, context + ": rejected operations preserve capture/selection state");
            OrbitalLevel level = game.State.Level;
            game.SelectLevel(level == OrbitalLevel.Finale ? OrbitalLevel.Tutorial : OrbitalLevel.Finale);
            checks.Require(game.State.Level == level, context + ": cannot switch the active board during a flight or replay");
        }

        static void VerifyLegacyPhysics(OrbitalPrototype game, Checks checks, List<Flight> flights)
        {
            for (int index = 0; index < 2; index++)
                for (int route = 0; route < 2; route++)
                    for (int reflector = 0; reflector < 2; reflector++)
                    {
                        var level = (OrbitalLevel)index;
                        string details = game.RunPhysicsProbe(level, route, reflector);
                        bool correct = route == 1 && reflector == (index == 0 ? 1 : 0);
                        Record(game, "legacy D" + route + "/R" + reflector, correct ? "success" : "failure", details, flights);
                        RequireTerminal(game, checks, level + " legacy physical regression");
                        checks.Require((game.State.Phase == OrbitalPhase.Success) == correct,
                            level + " legacy route outcome remains unchanged; " + details);
                    }
            checks.scenarios.Add("Unchanged Tutorial and Moon: all eight original D/R configurations still use real PhysX and retain their outcomes");
        }

        static void VerifyPhysicalLevel(OrbitalPrototype game, OrbitalLevel level, int repeats, Checks checks, List<Flight> flights)
        {
            for (int repeat = 0; repeat < repeats; repeat++)
            {
                Enter(game, level); Reference(game, level, checks);
                Probe(game, "reference " + (repeat + 1), "success", checks, flights);
                RequireDelivery(game, checks, level + " reference " + (repeat + 1));
            }
            game.Replay();
            checks.Require(game.IsReplaying, level + ": replay starts from a genuine successful physical flight");
            VerifyLocked(game, checks, level + " replay");
            game.ResetAttempt();

            Enter(game, level); Reference(game, level, checks);
            var poses = Capture(game.Construction);
            Pose first = FirstPlaced(poses), last = LastPlaced(poses);
            Remove(game.Construction, first.tile);
            Probe(game, "missing entry track", "failure", checks, flights);
            RequireFailure(game, checks, level + " missing entry track");

            Enter(game, level); Reference(game, level, checks);
            checks.Require(game.Construction.Place(last.tile, last.socket, last.turns + 1), level + ": wrong-orientation track is physically placeable");
            Probe(game, "wrong exit orientation", "failure", checks, flights);
            RequireFailure(game, checks, level + " wrong exit orientation");

            Enter(game, level); Reference(game, level, checks);
            poses = Capture(game.Construction);
            int other = DifferentShape(game.Construction, first.tile);
            checks.Require(other >= 0, level + ": construction contains physically different track shapes");
            Swap(game.Construction, poses, first.tile, other, checks, level + " wrong-shape swap");
            Probe(game, "wrong shapes in valid sockets", "failure", checks, flights);
            RequireFailure(game, checks, level + " wrong shapes");

            Enter(game, level); Reference(game, level, checks);
            poses = Capture(game.Construction);
            int drop = -1, laterStraight = -1;
            for (int index = 0; index < game.Construction.tiles.Length; index++)
                if (game.Construction.tiles[index].Installed && game.Construction.tiles[index].shape == OrbitalTileShape.Drop) drop = index;
            if (drop >= 0)
                for (int index = drop + 1; index < game.Construction.tiles.Length; index++)
                    if (game.Construction.tiles[index].Installed && game.Construction.tiles[index].shape == OrbitalTileShape.Straight)
                    { laterStraight = index; break; }
            if (laterStraight >= 0)
            {
                // The replacement flat track preserves the inlet direction but
                // loses the Drop's extra 6 cm descent. The donated straight came
                // from downstream, so the ball still reaches the height fault.
                Swap(game.Construction, poses, drop, laterStraight, checks, level + " drop-height substitution");
                Probe(game, "flat track cannot replace the descending connection", "failure after upstream relay", checks, flights);
                RequireFailure(game, checks, level + " wrong connection height");
                checks.Require(game.State.HasB, level + ": the ball physically reaches the upstream power relay before the height-sensitive failure");
            }
            checks.scenarios.Add(level + ": repeated correct physical deliveries, replay locks, missing entry, rotated exit, wrong-shape placement and applicable descending-track substitution");
        }

        static void VerifyFinaleBranch(OrbitalPrototype game, Checks checks, List<Flight> flights)
        {
            Enter(game, OrbitalLevel.Finale); Reference(game, OrbitalLevel.Finale, checks);
            var kit = game.Construction;
            checks.Require(kit.tiles.Length >= 6 && kit.tiles[3].shape == OrbitalTileShape.CornerRight && kit.tiles[5].shape == OrbitalTileShape.CornerLeft,
                "Finale branch fixture has independently manipulable right and left turns");
            var branch = kit.tiles[3];
            checks.Require(kit.Place(3, branch.SocketIndex, branch.QuarterTurns + 1), "Finale post-gate track accepts a deliberately wrong orientation");
            Probe(game, "post-gate orientation changes the real flight", "failure after gate", checks, flights);
            RequireFailure(game, checks, "Finale post-gate changed track");
            checks.Require(game.State.HasA && game.State.HasB && game.State.GateCrossed,
                "changing the post-gate track preserves real upstream power/crossing, then fails downstream");

            Enter(game, OrbitalLevel.Finale); Reference(game, OrbitalLevel.Finale, checks);
            kit = game.Construction;
            Remove(kit, 3); Remove(kit, 5);
            checks.Require(kit.Place(5, 3, 3), "Finale left-turn shortcut is physically assembled in the post-gate socket");
            Probe(game, "actual shortcut reaches dock without C", "dock_missing_c", checks, flights);
            checks.Require(game.State.Phase == OrbitalPhase.Failure && game.State.ResultCode == "dock_missing_c" &&
                game.State.HasA && game.State.HasB && game.State.GateCrossed && !game.State.HasC,
                "Finale shortcut physically reaches S after power/gate but is rejected for the independently omitted C checkpoint");
            checks.scenarios.Add("Finale C is a genuine downstream route choice: changing only the post-gate construction changes the outcome, and an assembled shortcut docks without C");
        }

        static void VerifyEquivalentPieces(OrbitalPrototype game, Checks checks, List<Flight> flights)
        {
            bool tested = false;
            for (int levelIndex = 2; levelIndex < OrbitalCampaign.Count; levelIndex++)
            {
                var level = (OrbitalLevel)levelIndex;
                Enter(game, level); Reference(game, level, checks);
                var kit = game.Construction;
                Pose[] poses = Capture(kit);
                for (int first = 0; first < kit.tiles.Length && !tested; first++)
                    for (int second = first + 1; second < kit.tiles.Length && !tested; second++)
                    {
                        if (!kit.tiles[first].Installed || !kit.tiles[second].Installed || kit.tiles[first].shape != kit.tiles[second].shape) continue;
                        Swap(kit, poses, first, second, checks, level + " equivalent pieces");
                        Probe(game, "interchange identical shapes", "success", checks, flights);
                        RequireDelivery(game, checks, level + " interchangeable shapes");
                        tested = true;
                    }
                if (tested) break;
            }
            checks.Require(tested, "at least one level proves equal track shapes may exchange sockets without answer-ID gating");
            checks.scenarios.Add("Geometrically identical track pieces are interchangeable: the real ball still delivers after their identities swap");
        }

        static void VerifyChapter(OrbitalPrototype game, Checks checks, List<Flight> flights)
        {
            game.ResetAttempt(); game.Construction?.Clear();
            Field("progress").SetValue(game, new OrbitalProgress());
            Enter(game, OrbitalLevel.Tutorial);
            for (int index = 0; index < OrbitalCampaign.Count; index++)
            {
                var level = (OrbitalLevel)index;
                checks.Require(game.State.Level == level && game.ChallengeAttempts == 0, level + ": sequential journey begins with fresh attempts");
                for (int locked = index + 1; locked < OrbitalCampaign.Count; locked++)
                {
                    game.ActivateControl(OrbitalControlKind.Level, locked);
                    checks.Require(game.State.Level == level, level + ": locked mission " + locked + " rejects activation");
                }
                string details;
                if (index < 2)
                    details = game.RunPhysicsProbe(level, 1, index == 0 ? 1 : 0);
                else
                {
                    checks.Require(game.Construction.PlacedCount == 0, level + ": Continue starts a genuinely empty construction workbench");
                    Reference(game, level, checks);
                    details = ConstructionProbe(game);
                }
                Record(game, "fresh sequential chapter", "success", details, flights);
                RequireDelivery(game, checks, level + " sequential chapter");
                checks.Require(game.Profile.CompletedCount == index + 1 && game.Profile.TotalBadges == (index + 1) * 3,
                    level + ": only actually completed flights contribute their three eligible badges");
                if (index + 1 < OrbitalCampaign.Count) game.ContinueJourney();
            }
            checks.Require(game.Profile.AllCompleted && game.Profile.TotalBadges == 18,
                "six genuine physical deliveries finish the chapter with 18 eligible badges");
            string before = JsonUtility.ToJson(game.Profile);
            game.ContinueJourney();
            checks.Require(game.IsChapterOverview && game.State.Level == OrbitalLevel.Finale && game.State.Phase == OrbitalPhase.Success,
                "final Continue opens chapter overview without inventing a seventh level");
            checks.Require(JsonUtility.ToJson(game.Profile) == before, "overview grants no duplicate reward");
            game.ContinueJourney();
            checks.Require(!game.IsChapterOverview && game.State.Phase == OrbitalPhase.Success, "overview can close without losing the final delivery");
            var loaded = OrbitalProgress.Load();
            checks.Require(loaded.AllCompleted && loaded.TotalBadges == 18 && loaded.constructionRevision == OrbitalProgress.CurrentConstructionRevision,
                "current-revision chapter completion survives loading");
            checks.scenarios.Add("Fresh continuous chapter: original two missions plus four constructed routes, ordered unlocking, actual awards, persistence and final overview");
        }

        // Test cases explicitly request an empty fixture. Player-facing level
        // switches intentionally preserve drafts and are exercised separately.
        static void Enter(OrbitalPrototype game, OrbitalLevel level)
        {
            game.ResetAttempt(); game.SelectLevel(level); game.Construction?.Clear();
        }
        static void Reference(OrbitalPrototype game, OrbitalLevel level, Checks checks)
        {
            var method = typeof(OrbitalPrototype).GetMethod("ApplyConstructionReferenceSolution", BindingFlags.Instance | BindingFlags.Public);
            checks.Require(method != null, "geometry exposes the Editor reference placement helper");
            bool success = (bool)Invoke(method, game, new object[] { level });
            checks.Require(success && game.State.Level == level && game.Construction.PlacedCount > 0,
                level + ": reference helper places real tracks in the current board");
            checks.Require(game.State.Phase == OrbitalPhase.Configuring && !game.State.HasA && !game.State.HasB && !game.State.GateCrossed && !game.State.HasC,
                level + ": placing a reference route never marks sensors, power, gate or delivery as completed");
            game.Construction.SettlePresentation();
            Physics.SyncTransforms();
        }
        static string ConstructionProbe(OrbitalPrototype game)
        {
            var method = typeof(OrbitalPrototype).GetMethod("RunConstructionProbe", BindingFlags.Instance | BindingFlags.Public);
            if (method == null) throw new MissingMethodException("OrbitalPrototype.RunConstructionProbe() is required for real flight verification.");
            return (string)Invoke(method, game, null);
        }
        static void Probe(OrbitalPrototype game, string scenario, string expected, Checks checks, List<Flight> flights)
        {
            string placements = Describe(game.Construction);
            string details = ConstructionProbe(game);
            Record(game, scenario, expected, details, flights, placements);
            RequireTerminal(game, checks, game.State.Level + " " + scenario);
        }
        static void RequireTerminal(OrbitalPrototype game, Checks checks, string context)
        {
            checks.Require(game.State.Phase == OrbitalPhase.Success || game.State.Phase == OrbitalPhase.Failure,
                context + ": actual simulation reaches a terminal phase; " + game.StatusDescription);
            checks.Require(game.RecordedFrameCount > 1 && game.ShotElapsed > 0 && game.ShotElapsed <= 25,
                context + ": records an actual bounded physical flight");
            checks.Require(!string.IsNullOrEmpty(game.State.ResultCode) && !string.IsNullOrEmpty(game.State.Feedback),
                context + ": terminal outcome explains a real mission condition");
        }
        static void RequireDelivery(OrbitalPrototype game, Checks checks, string context)
        {
            checks.Require(game.State.Phase == OrbitalPhase.Success && game.State.ResultCode == "success" && game.State.HasA && game.State.HasB,
                context + ": real ball reaches both relays and the station; " + game.StatusDescription);
            checks.Require(game.State.GateCrossed == OrbitalCampaign.RequiresGate(game.State.Level), context + ": gate crossing matches the mission");
            checks.Require(game.State.HasC == OrbitalCampaign.HasCheckpoint(game.State.Level), context + ": C is required only for the final mission");
        }
        static void RequireFailure(OrbitalPrototype game, Checks checks, string context)
        {
            checks.Require(game.State.Phase == OrbitalPhase.Failure && game.State.ResultCode != "success", context + ": incorrect physical assembly does not deliver; " + game.StatusDescription);
        }
        static void Record(OrbitalPrototype game, string scenario, string expected, string details, List<Flight> flights, string placements = "")
        {
            flights.Add(new Flight { level = game.State.Level.ToString(), scenario = scenario, expected = expected,
                result = game.State.ResultCode, feedback = game.State.Feedback, details = details, placements = placements,
                elapsed = game.ShotElapsed, a = game.State.HasA, b = game.State.HasB, gateCrossed = game.State.GateCrossed, c = game.State.HasC });
        }
        static bool HasSelectionCollider(GameObject root)
        {
            foreach (var collider in root.GetComponents<Collider>())
                if (collider.enabled && collider.isTrigger && (collider.gameObject.layer == 0 || collider.gameObject.layer == LayerMask.NameToLayer("UI"))) return true;
            return false;
        }
        static void Press(OrbitalConstructionTile tile, Checks checks, string context)
        {
            checks.Require(tile.BeginManipulation(tile.transform.position), context + ": begins capture");
            tile.EndManipulation(false);
            checks.Require(!tile.IsManipulating, context + ": ends capture");
        }
        static void Drag(OrbitalConstructionTile tile, Vector3 target, bool cancelled, Checks checks, string context)
        {
            Vector3 anchor = tile.transform.position;
            checks.Require(tile.BeginManipulation(anchor), context + ": begins capture");
            // Account for the visual lift without assuming its private constant.
            tile.UpdateManipulation(anchor + target - tile.transform.position);
            tile.EndManipulation(cancelled);
            checks.Require(!tile.IsManipulating, context + ": releases capture");
        }
        static Vector3 VerticalMiss(OrbitalConstructionKit kit, Vector3 socket, Checks checks, string context)
        {
            Vector3 local = kit.transform.InverseTransformPoint(socket);
            foreach (float offset in new[] { .14f, -.14f, .22f, -.22f })
            {
                Vector3 candidate = local + Vector3.up * offset;
                if (candidate.y < -.025f || candidate.y > .475f) continue;
                Vector3 world = kit.transform.TransformPoint(candidate);
                bool near = false;
                foreach (var other in kit.sockets) if (Vector3.Distance(world, other.transform.position) < .09f) near = true;
                if (!near) return world;
            }
            checks.Require(false, context + "has a same-XZ position outside every socket's height tolerance");
            return socket;
        }
        static void Click(OrbitalClickTarget target)
        {
            if (target == null) throw new InvalidOperationException("Missing socket click target.");
            typeof(OrbitalClickTarget).GetField("m_LastActivationFrame", Private).SetValue(target, -1);
            target.Activate();
        }
        static Pose[] Capture(OrbitalConstructionKit kit)
        {
            var poses = new Pose[kit.tiles.Length];
            for (int index = 0; index < poses.Length; index++)
            {
                var tile = kit.tiles[index];
                poses[index] = new Pose { tile = index, socket = tile.SocketIndex, turns = tile.QuarterTurns,
                    position = tile.transform.localPosition, rotation = tile.transform.localRotation };
            }
            return poses;
        }
        static void RequireSame(OrbitalConstructionKit kit, Pose[] poses, Checks checks, string context)
        {
            foreach (Pose pose in poses)
            {
                var tile = kit.tiles[pose.tile];
                checks.Require(tile.SocketIndex == pose.socket && tile.QuarterTurns == pose.turns, context + ": tile " + pose.tile + " placement unchanged");
                checks.At(tile.transform.localPosition, pose.position, context + ": tile " + pose.tile + " position unchanged");
                checks.Require(Quaternion.Angle(tile.transform.localRotation, pose.rotation) < .01f, context + ": tile " + pose.tile + " collider rotation unchanged");
            }
        }
        static void RequireDraftMatchesRecord(OrbitalConstructionKit kit, OrbitalProgress.Record record, Checks checks, string context)
        {
            bool saved = record.tileSockets != null && record.tileTurns != null &&
                record.tileSockets.Length == kit.tiles.Length && record.tileTurns.Length == kit.tiles.Length;
            for (int index = 0; index < kit.tiles.Length; index++)
            {
                int socket = saved ? record.tileSockets[index] : -1;
                int turns = saved ? record.tileTurns[index] : 0;
                var tile = kit.tiles[index];
                checks.Require(tile.SocketIndex == socket && tile.QuarterTurns == turns && !tile.IsManipulating,
                    context + ": tile " + index + " matches the player's persisted seat and orientation");
                Vector3 expected = socket >= 0 ? kit.sockets[socket].transform.position : kit.transform.TransformPoint(tile.homePosition);
                checks.At(tile.transform.position, expected, context + ": tile " + index + " is physically seated");
            }
        }
        static Pose FirstPlaced(Pose[] poses) { foreach (var pose in poses) if (pose.socket >= 0) return pose; throw new InvalidOperationException("No placed reference tile."); }
        static Pose LastPlaced(Pose[] poses) { for (int index = poses.Length - 1; index >= 0; index--) if (poses[index].socket >= 0) return poses[index]; throw new InvalidOperationException("No placed reference tile."); }
        static int DifferentShape(OrbitalConstructionKit kit, int reference)
        {
            for (int index = 0; index < kit.tiles.Length; index++)
                if (kit.tiles[index].Installed && kit.tiles[index].shape != kit.tiles[reference].shape) return index;
            return -1;
        }
        static void Remove(OrbitalConstructionKit kit, int tile) { kit.Select(tile); kit.ReturnSelectedToTray(); }
        static void Swap(OrbitalConstructionKit kit, Pose[] poses, int first, int second, Checks checks, string context)
        {
            kit.Clear();
            foreach (Pose pose in poses)
            {
                if (pose.socket < 0) continue;
                int tile = pose.tile == first ? second : pose.tile == second ? first : pose.tile;
                checks.Require(kit.Place(tile, pose.socket, pose.turns), context + ": a shape may occupy the other piece's socket");
            }
        }
        static string Describe(OrbitalConstructionKit kit)
        {
            var parts = new List<string>();
            foreach (var tile in kit.tiles) if (tile.Installed) parts.Add(tile.index + "@" + tile.SocketIndex + "/" + tile.QuarterTurns);
            return string.Join(",", parts);
        }
        static FieldInfo Field(string name) => typeof(OrbitalPrototype).GetField(name, Private) ?? throw new MissingFieldException(name);
        static void Call(OrbitalPrototype game, string name, params object[] args)
        {
            var method = typeof(OrbitalPrototype).GetMethod(name, Private) ?? throw new MissingMethodException(name);
            Invoke(method, game, args);
        }
        static object Invoke(MethodInfo method, object target, object[] args)
        {
            try { return method.Invoke(target, args); }
            catch (TargetInvocationException exception) { throw exception.InnerException ?? exception; }
        }
    }
}
