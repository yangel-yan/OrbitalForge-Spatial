using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceBall.Editor
{
    /// <summary>
    /// Exercises the live assembly API without changing scenes or project assets.
    /// Invoke Run in Play mode; restores the user's profile and both save slots,
    /// leaving the original level on an empty workbench.
    /// </summary>
    public static class OrbitalAssemblyVerification
    {
        [Serializable]
        sealed class Report
        {
            public string status = "passed";
            public int levels;
            public int assertions;
            public string[] scenarios;
        }

        sealed class Checks
        {
            public int count;
            public readonly List<string> scenarios = new();

            public void Require(bool condition, string message)
            {
                count++;
                if (!condition) throw new InvalidOperationException("Assembly verification failed: " + message);
            }

            public void At(Vector3 actual, Vector3 expected, string message)
            {
                Require((actual - expected).sqrMagnitude < .000001f,
                    message + "; actual=" + actual.ToString("F4") + ", expected=" + expected.ToString("F4"));
            }
        }

        public static string Run()
        {
            if (!Application.isPlaying)
                throw new InvalidOperationException("Assembly verification requires Play mode.");
            var game = UnityEngine.Object.FindFirstObjectByType<OrbitalPrototype>();
            if (game == null) throw new InvalidOperationException("No active OrbitalPrototype was found.");

            var checks = new Checks();
            using (new OrbitalVerificationScope(game))
            {
                VerifyColliderLayers(game, checks);
                for (int index = 0; index < OrbitalCampaign.Count; index++)
                    VerifyLevel(game, (OrbitalLevel)index, checks);
                VerifyReplayCancellation(game, checks);
                return JsonUtility.ToJson(new Report
                {
                    levels = OrbitalCampaign.Count,
                    assertions = checks.count,
                    scenarios = checks.scenarios.ToArray()
                }, true);
            }
        }

        static void VerifyColliderLayers(OrbitalPrototype game, Checks checks)
        {
            // Inspect the initially inactive level before SelectLevel can activate it.
            // Both collection and ownership lookup must explicitly include inactive objects.
            int defaultLayer = LayerMask.NameToLayer("Default");
            int uiLayer = LayerMask.NameToLayer("UI");
            int ignoreRaycast = LayerMask.NameToLayer("Ignore Raycast");
            int interactive = 0, physical = 0, inactivePieces = 0, inactiveSockets = 0, inactiveLaunchers = 0;
            var badInteractive = new List<string>();
            var badPhysical = new List<string>();
            foreach (var collider in game.GetComponentsInChildren<Collider>(true))
            {
                var piece = collider.GetComponentInParent<OrbitalAssemblyPiece>(true);
                var target = collider.GetComponentInParent<OrbitalClickTarget>(true);
                int layer = collider.gameObject.layer;
                if (piece != null || target != null)
                {
                    interactive++;
                    if (layer == ignoreRaycast || (layer != defaultLayer && layer != uiLayer))
                        badInteractive.Add(ColliderPath(collider) + " layer=" + layer);
                    if (!collider.gameObject.activeInHierarchy)
                    {
                        if (piece != null) inactivePieces++;
                        if (collider.GetComponentInParent<OrbitalAssemblySocket>(true) != null) inactiveSockets++;
                        var control = collider.GetComponentInParent<OrbitalControl>(true);
                        if (control != null && control.kind == OrbitalControlKind.Launch) inactiveLaunchers++;
                    }
                }
                else
                {
                    physical++;
                    if (layer != ignoreRaycast) badPhysical.Add(ColliderPath(collider) + " layer=" + layer);
                }
            }
            checks.Require(defaultLayer >= 0 && uiLayer >= 0 && ignoreRaycast >= 0,
                "Layers: required Default, UI and Ignore Raycast layers exist");
            int inactiveLevels = OrbitalCampaign.Count - 1;
            checks.Require(inactivePieces >= inactiveLevels * 2, "Layers: both pieces in every inactive level were inspected");
            checks.Require(inactiveSockets >= inactiveLevels * 2, "Layers: both sockets in every inactive level were inspected");
            checks.Require(inactiveLaunchers >= inactiveLevels, "Layers: launcher in every inactive level was inspected");
            checks.Require(interactive >= OrbitalCampaign.Count * 5, "Layers: every level's pieces, sockets and launcher were inspected");
            checks.Require(physical > 0, "Layers: non-interactive physics colliders were inspected");
            checks.Require(badInteractive.Count == 0,
                "Layers: all active/inactive interaction colliders must use Default or UI; " + string.Join("; ", badInteractive));
            checks.Require(badPhysical.Count == 0,
                "Layers: all non-interactive physics colliders must use Ignore Raycast; " + string.Join("; ", badPhysical));
            checks.scenarios.Add("All campaign levels: inactive pieces, sockets and launcher remain selectable; physics geometry ignores selection rays");
        }

        static string ColliderPath(Collider collider)
        {
            string path = collider.name;
            for (Transform parent = collider.transform.parent; parent != null; parent = parent.parent)
                path = parent.name + "/" + path;
            return path;
        }

        static void VerifyLevel(OrbitalPrototype game, OrbitalLevel level, Checks checks)
        {
            string context = level + ": ";
            game.ResetAttempt();
            game.SelectLevel(level);
            var rig = game.Assembly;
            checks.Require(rig != null, context + "assembly exists");
            checks.Require(rig.pieces != null && rig.pieces.Length == 2 && rig.pieces[0] != null && rig.pieces[1] != null,
                context + "both puzzle pieces exist");
            checks.Require(rig.sockets != null && rig.sockets.Length == 2 && rig.sockets[0] != null && rig.sockets[1] != null,
                context + "both puzzle sockets exist");
            rig.Clear();
            checks.Require(game.AssemblyEditable, context + "empty workbench is editable");
            checks.Require(!rig.Ready && !rig.pieces[0].Installed && !rig.pieces[1].Installed,
                context + "clear leaves both pieces uninstalled");
            checks.Require(!game.Launch() && game.State.Phase == OrbitalPhase.Configuring,
                context + "empty assembly cannot launch");
            VerifyClearHighlights(rig, checks, context);
            VerifySelectionOwnership(rig, checks, context);
            VerifyResetFromTray(game, rig, checks, context);

            for (int index = 0; index < 2; index++)
            {
                rig.Select(index);
                checks.Require(!rig.PlaceSelected(1 - index), context + "piece " + index + " rejects the other socket");
                checks.Require(!rig.pieces[index].Installed && !rig.Ready,
                    context + "wrong socket leaves assembly incomplete");
            }
            checks.scenarios.Add(context + "empty launch and incompatible socket rejection");

            checks.Require(rig.Place(0), context + "route installs through placement API");
            checks.Require(rig.pieces[0].Installed && !rig.Ready, context + "one piece is insufficient");
            checks.Require(!game.Launch() && game.State.Phase == OrbitalPhase.Configuring,
                context + "one-piece assembly cannot launch");
            checks.Require(rig.Place(1) && rig.Ready, context + "two compatible pieces make assembly ready");
            VerifyHeldLaunch(game, rig, checks, context);
            checks.Require(game.Launch() && game.State.Phase == OrbitalPhase.Running,
                context + "complete assembly launches");
            VerifyLock(game, rig, checks, context + "running");
            game.TogglePause();
            checks.Require(game.State.Phase == OrbitalPhase.Paused, context + "launch can pause");
            VerifyLock(game, rig, checks, context + "paused");
            checks.scenarios.Add(context + "assembly gating and running/paused manipulation lock");

            int routeChoice = rig.pieces[0].Choice, reflectorChoice = rig.pieces[1].Choice;
            game.ResetAttempt();
            checks.Require(game.AssemblyEditable && rig.Ready && rig.pieces[0].Installed && rig.pieces[1].Installed,
                context + "reset keeps installed pieces and restores editing");
            checks.Require(rig.pieces[0].Choice == routeChoice && rig.pieces[1].Choice == reflectorChoice,
                context + "reset keeps both orientations");

            for (int index = 0; index < 2; index++)
            {
                OrbitalAssemblyPiece piece = rig.pieces[index];
                string label = context + "piece " + index + ": ";
                int beforeTap = piece.Choice;
                Vector3 tapPoint = piece.transform.position;
                checks.Require(piece.BeginManipulation(tapPoint), label + "installed piece accepts a press");
                piece.EndManipulation(false);
                checks.Require(piece.Installed && piece.Choice != beforeTap, label + "tap rotates installed piece");
                checks.Require(piece.Choice == (index == 0 ? game.State.Route : game.State.Reflector),
                    label + "orientation updates live route configuration");

                Vector3 seated = piece.transform.localPosition;
                int seatedChoice = piece.Choice;
                Drag(piece, rig.transform, seated + new Vector3(2f, 0, 2f), false, checks, label + "invalid drop");
                checks.Require(piece.Installed && piece.Choice == seatedChoice, label + "invalid drop preserves installed state");
                checks.At(piece.transform.localPosition, seated, label + "invalid drop restores original position");

                Drag(piece, rig.transform, piece.HomePosition, true, checks, label + "cancelled unload");
                checks.Require(piece.Installed && piece.Choice == seatedChoice, label + "cancel preserves installed state");
                checks.At(piece.transform.localPosition, seated, label + "cancel restores original position");

                Drag(piece, rig.transform, piece.HomePosition, false, checks, label + "unload");
                checks.Require(!piece.Installed && !rig.Ready, label + "dropping on tray uninstalls piece");
                checks.At(piece.transform.localPosition, piece.HomePosition, label + "unloaded piece seats in tray");
                checks.Require(!game.Launch() && game.State.Phase == OrbitalPhase.Configuring,
                    label + "unloading closes launch gate");

                Drag(piece, rig.transform, piece.SocketPosition, true, checks, label + "cancelled install");
                checks.Require(!piece.Installed, label + "cancelled install leaves piece uninstalled");
                checks.At(piece.transform.localPosition, piece.HomePosition, label + "cancelled install returns to tray");

                Drag(piece, rig.transform, piece.SocketPosition, false, checks, label + "install");
                checks.Require(piece.Installed && rig.Ready, label + "dragging onto matching socket installs piece");
                checks.At(piece.transform.localPosition, piece.SocketPosition, label + "installed piece seats in socket");
                checks.Require(piece.Choice == seatedChoice, label + "dragging preserves orientation");
            }
            checks.scenarios.Add(context + "reset, tap rotation, invalid drop, cancellation, tray unload and socket drag placement");
            game.ResetAttempt();
            rig.Clear();
        }

        static void VerifyClearHighlights(OrbitalAssemblyRig rig, Checks checks, string context)
        {
            for (int selected = 0; selected < 2; selected++)
            {
                rig.Select(selected);
                var pieceHalo = rig.pieces[selected].selectionHalo;
                var socketHalo = rig.sockets[selected].selectionHalo;
                checks.Require(pieceHalo != null && socketHalo != null,
                    context + "piece and socket " + selected + " expose selection halos");
                checks.Require(pieceHalo.gameObject.activeSelf && socketHalo.gameObject.activeSelf,
                    context + "selection lights both matching halos");
                rig.Clear();
                checks.Require(rig.selected == -1, context + "clear removes the selected piece");
                foreach (var piece in rig.pieces)
                    checks.Require(piece.selectionHalo != null && !piece.selectionHalo.gameObject.activeSelf,
                        context + "clear turns every piece halo off");
                foreach (var socket in rig.sockets)
                    checks.Require(socket.selectionHalo != null && !socket.selectionHalo.gameObject.activeSelf,
                        context + "clear turns every socket halo off");
            }
            checks.scenarios.Add(context + "clear removes piece and socket selection highlights");
        }

        static void VerifySelectionOwnership(OrbitalAssemblyRig rig, Checks checks, string context)
        {
            // Cover both index orders: an older input cancelling must not clear a
            // newer input's completed tap selection.
            for (int firstIndex = 0; firstIndex < 2; firstIndex++)
            {
                rig.Clear();
                var first = rig.pieces[firstIndex];
                var second = rig.pieces[1 - firstIndex];
                checks.Require(first.BeginManipulation(first.transform.position), context + "first overlapping press begins");
                checks.Require(second.BeginManipulation(second.transform.position), context + "second overlapping press begins");
                second.EndManipulation(false);
                checks.Require(rig.selected == second.index && !second.Installed,
                    context + "second uninstalled-piece tap owns selection");
                first.EndManipulation(true);
                checks.Require(rig.selected == second.index,
                    context + "older cancelled press preserves newer selection");
                checks.Require(!first.IsManipulating && !second.IsManipulating,
                    context + "both overlapping presses end");
                checks.At(first.transform.localPosition, first.HomePosition, context + "cancelled piece returns home");
                checks.At(second.transform.localPosition, second.HomePosition, context + "tapped piece stays home");
                checks.Require(second.selectionHalo.gameObject.activeSelf && rig.sockets[second.index].selectionHalo.gameObject.activeSelf,
                    context + "newer selection retains its matching halos");
            }
            rig.Clear();
            checks.scenarios.Add(context + "overlapping input cancellation preserves newer selection in either order");
        }

        static void VerifyResetFromTray(OrbitalPrototype game, OrbitalAssemblyRig rig, Checks checks, string context)
        {
            foreach (var piece in rig.pieces)
            {
                Vector3 hit = piece.transform.position;
                checks.Require(piece.BeginManipulation(hit), context + "tray piece begins dragging before reset");
                piece.UpdateManipulation(hit + rig.transform.TransformVector(new Vector3(.04f, 0, .03f)));
            }
            game.ResetAttempt();
            checks.Require(game.State.Phase == OrbitalPhase.Configuring && game.AssemblyEditable,
                context + "reset restores editable configuration");
            foreach (var piece in rig.pieces)
            {
                checks.Require(!piece.IsManipulating && !piece.Installed,
                    context + "reset interrupts held tray piece without installing it");
                checks.At(piece.transform.localPosition, piece.HomePosition,
                    context + "reset removes drag lift and restores initial home position");
            }
            checks.scenarios.Add(context + "reset cancels both held tray pieces without leaving floating models");
        }

        static void VerifyHeldLaunch(OrbitalPrototype game, OrbitalAssemblyRig rig, Checks checks, string context)
        {
            foreach (var piece in rig.pieces)
            {
                int choice = piece.Choice;
                checks.Require(piece.BeginManipulation(piece.transform.position),
                    context + "installed piece " + piece.index + " can be held before launch");
                checks.Require(rig.pieces[0].Installed && rig.pieces[1].Installed && !rig.Ready,
                    context + "holding either installed piece makes assembly unready");
                checks.Require(!game.Launch() && game.State.Phase == OrbitalPhase.Configuring,
                    context + "launch rejects held piece " + piece.index + " despite both installed flags");
                game.ResetAttempt();
                checks.Require(!piece.IsManipulating && piece.Installed && piece.Choice == choice && rig.Ready,
                    context + "reset cancels installed-piece manipulation and retains configuration");
                checks.At(piece.transform.localPosition, piece.SocketPosition,
                    context + "reset returns held installed piece to socket");
            }
            checks.scenarios.Add(context + "either held installed piece blocks launch and reset restores its seated pose");
        }

        static void VerifyReplayCancellation(OrbitalPrototype game, Checks checks)
        {
            string probe = game.RunPhysicsProbe(OrbitalLevel.Tutorial, 1, 1);
            checks.Require(game.RecordedFrameCount > 1 && game.State.Phase != OrbitalPhase.Running && game.State.Phase != OrbitalPhase.Paused,
                "Replay: physics probe produces a completed recorded attempt; " + probe);
            var rig = game.Assembly;
            int frameCount = game.RecordedFrameCount;
            foreach (var piece in rig.pieces)
            {
                Vector3 hit = piece.transform.position;
                checks.Require(piece.BeginManipulation(hit), "Replay: installed piece can begin manipulation after the attempt");
                piece.UpdateManipulation(hit + rig.transform.TransformVector(new Vector3(.035f, 0, -.025f)));
            }
            game.Replay();
            checks.Require(game.IsReplaying && !game.AssemblyEditable && game.RecordedFrameCount == frameCount,
                "Replay: starts the existing recording and locks editing");
            foreach (var piece in rig.pieces)
            {
                checks.Require(!piece.IsManipulating && piece.Installed,
                    "Replay: starting playback cancels each held piece and preserves installation");
                checks.At(piece.transform.localPosition, piece.SocketPosition,
                    "Replay: held piece returns to its seated pose before playback");
            }
            checks.scenarios.Add("Tutorial: real recorded playback cancels held pieces and restores both seated poses");
            game.ResetAttempt();
        }

        static void VerifyLock(OrbitalPrototype game, OrbitalAssemblyRig rig, Checks checks, string context)
        {
            checks.Require(!game.AssemblyEditable, context + ": assembly is locked");
            foreach (var piece in rig.pieces)
            {
                Vector3 before = piece.transform.localPosition;
                int choice = piece.Choice;
                bool began = piece.BeginManipulation(piece.transform.position);
                // Undo an unexpectedly accepted press before surfacing its failure.
                if (began) piece.EndManipulation(true);
                checks.Require(!began, context + ": manipulation cannot begin");
                checks.Require(piece.Installed && piece.Choice == choice, context + ": installed configuration stays unchanged");
                checks.At(piece.transform.localPosition, before, context + ": locked piece stays in place");
            }
        }

        static void Drag(OrbitalAssemblyPiece piece, Transform root, Vector3 destination, bool cancel, Checks checks, string context)
        {
            // Begin lifts the model. Capture the hit and local origin before the lift,
            // then move the original hit by the desired local displacement in world space.
            Vector3 localStart = piece.transform.localPosition;
            Vector3 hit = piece.transform.position;
            checks.Require(piece.BeginManipulation(hit), context + ": manipulation starts");
            try
            {
                Vector3 worldTarget = hit + root.TransformVector(destination - localStart);
                piece.UpdateManipulation(worldTarget);
            }
            catch
            {
                piece.EndManipulation(true);
                throw;
            }
            piece.EndManipulation(cancel);
        }
    }
}
