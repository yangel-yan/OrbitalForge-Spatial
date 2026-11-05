using System;
using System.Collections.Generic;
using ByteDance.PICO.SpatialAdapter;
using UnityEngine;

namespace SpaceBall.Editor
{
    public static partial class OrbitalEnergyEffectsVerification
    {
        // Runs inside Run's existing protected Session. Objective signals and the
        // sampled docking pose below are synthetic contract inputs, not evidence
        // that a physical or native-input flight reached those objectives.
        static void VerifyCeremony(OrbitalPrototype game, OrbitalEnergyEffects effects, SpatialCamera camera, Checks checks)
        {
            var originalMeshes = new Dictionary<MeshFilter, Mesh>();
            foreach (var filter in effects.GetComponentsInChildren<MeshFilter>(true)) originalMeshes.Add(filter, filter.sharedMesh);
            var locks = CeremonyField<Transform[]>(effects, "gateLockPlates");
            var gate = CeremonyField<Transform>(effects, "gateCeremonyRoot");
            var ballVisual = CeremonyField<Transform>(effects, "ballVisual");

            void Step(float seconds)
            {
                Tick(game, effects, seconds, checks);
                RequireActiveMeshesInside(effects, camera, checks);
                var filters = effects.GetComponentsInChildren<MeshFilter>(true);
                checks.Require(filters.Length == originalMeshes.Count, "ceremony playback never adds mesh nodes");
                foreach (var filter in filters)
                    checks.Require(originalMeshes[filter] == filter.sharedMesh, "ceremonies reuse their authored shared meshes");
            }

            void Reset()
            {
                game.Profile.reducedMotion = false;
                Field("replaying").SetValue(game, false);
                game.ResetAttempt(); effects.ResetEffects();
            }

            try
            {
                game.Assembly?.Clear(); game.Construction?.Clear(); Reset();
                checks.Require(!game.LaunchConfigurationReady, "an empty assembly is not ready for launch charging");
                Step(.15f);
                checks.Require(effects.ChargeCycleCount == 0, "unready configuration never starts a charge cycle");
                if (game.Construction != null)
                    checks.Require(game.ApplyConstructionReferenceSolution(game.State.Level), "reference construction parts are installed before ceremony checks");
                else
                {
                    game.Assembly.Place(0); game.Assembly.Place(1);
                    game.ConfigureRoute(OrbitalCampaign.CorrectRoute(game.State.Level));
                    game.ConfigureReflector(OrbitalCampaign.CorrectReflector(game.State.Level));
                    game.Assembly.SettlePresentation();
                }
                checks.Require(game.LaunchConfigurationReady, "reference assembly becomes ready");
                Step(.02f);
                checks.Require(effects.ChargeCycleCount == 1, "becoming ready starts exactly one charge cycle");
                for (int i = 0; i < 6; i++) Step(.10f);
                checks.Require(effects.ChargeCycleCount == 1, "remaining ready does not repeat charge cycles");
                checks.Require(game.Launch(), "normal launch accepts the prepared physical assembly");
                checks.Require(game.State.Phase == OrbitalPhase.Running && !game.Ball.isKinematic &&
                    Mathf.Abs(game.Ball.linearVelocity.magnitude - game.launchSpeed) < .00001f,
                    "Launch releases the real Rigidbody at launchSpeed immediately, without waiting for discharge animation");
                checks.Require(effects.LaunchSequenceCount == 1, "the actual launch starts one discharge");
                effects.PlayLaunch(); effects.PlayLaunch();
                checks.Require(effects.LaunchSequenceCount == 1, "duplicate PlayLaunch calls cannot emit a second discharge");
                Step(.06f);
                checks.Require(CeremonyField<Transform>(effects, "muzzleWave").gameObject.activeSelf, "accepted launch exposes its finite muzzle wave");
                checks.Require(game.State.TogglePause(), "launch ceremony test pauses the run-state model");
                var launchPaused = CaptureDecorations(effects);
                float launchClock = CeremonyField<float>(effects, "launchElapsed");
                Step(.40f);
                RequireDecorations(launchPaused, checks, "paused launch decoration poses stay frozen");
                checks.Require(CeremonyField<float>(effects, "launchElapsed") == launchClock, "pause does not advance discharge time");
                checks.Require(game.State.TogglePause(), "launch ceremony test resumes");
                Step(.25f);
                RequireCeremonyNodesHidden(CeremonyField<Transform[]>(effects, "muzzleStreaks"), checks, "discharge streaks finish");
                checks.Require(!CeremonyField<Transform>(effects, "muzzleWave").gameObject.activeSelf, "discharge wave finishes");

                Reset();
                if (OrbitalCampaign.RequiresGate(game.State.Level))
                {
                    checks.Require(gate != null && locks.Length == 2, "gate levels own the two-lock ceremony pool");
                    checks.Require(!gate.gameObject.activeSelf && Mathf.Abs(Mathf.Abs(locks[0].localPosition.x) - .037f) < .000001f,
                        "gate reset hides the ceremony and returns lock plates to their closed positions");
                    ArmCeremonyGate(game, effects, checks);
                    Step(.02f);
                    checks.Require(gate.gameObject.activeSelf && game.State.GateOpen, "real GateOpen flag exposes the power ceremony");
                    Step(.18f);
                    checks.Require(Mathf.Abs(locks[0].localPosition.x) > .0375f && Mathf.Abs(locks[0].localPosition.x) < .054f,
                        "opening power visibly retracts a lock before settling");
                    checks.Require(game.State.TogglePause(), "gate ceremony test pauses");
                    var gatePaused = CaptureDecorations(effects);
                    float powerClock = CeremonyField<float>(effects, "gatePowerElapsed");
                    Step(.40f);
                    RequireDecorations(gatePaused, checks, "pause freezes the gate ceremony");
                    checks.Require(CeremonyField<float>(effects, "gatePowerElapsed") == powerClock, "pause freezes gate power time");
                    int launchesBeforeComfort = effects.LaunchSequenceCount;
                    game.ActivateControl(OrbitalControlKind.Comfort, 0);
                    checks.Require(game.ReducedMotion, "the actual Comfort action enables Reduced Motion while paused");
                    Step(.01f);
                    checks.Require(game.State.Phase == OrbitalPhase.Paused && CeremonyField<float>(effects, "gatePowerElapsed") < 0 &&
                        Mathf.Abs(Mathf.Abs(locks[0].localPosition.x) - .054f) < .000001f,
                        "Reduced Motion settles an opening gate immediately even while the run is paused");
                    checks.Require(effects.LaunchSequenceCount == launchesBeforeComfort && CeremonyField<float>(effects, "launchElapsed") < 0,
                        "the actual Comfort action cancels discharge motion without resetting or repeating the launch count");
                    checks.Require(CeremonyField<bool>(effects, "gateObservedOpen") && !CeremonyField<bool>(effects, "gateObservedCrossed"),
                        "the actual Comfort action consumes the existing open flag without inventing a crossing");
                    RequireCeremonyNodesHidden(CeremonyField<Transform[]>(effects, "gateScanLoops"), checks, "Reduced Motion removes moving gate scans");
                    game.ActivateControl(OrbitalControlKind.Comfort, 0);
                    checks.Require(!game.ReducedMotion, "the actual Comfort action returns to dynamic presentation");
                    Step(.20f);
                    checks.Require(game.State.TogglePause(), "gate ceremony resumes after its static pose");
                    Step(.02f);
                    checks.Require(CeremonyField<float>(effects, "gatePowerElapsed") < 0 &&
                        Mathf.Abs(Mathf.Abs(locks[0].localPosition.x) - .054f) < .000001f,
                        "leaving Reduced Motion cannot replay the already-observed gate opening");
                    checks.Require(effects.LaunchSequenceCount == launchesBeforeComfort && CeremonyField<float>(effects, "launchElapsed") < 0 &&
                        CeremonyField<bool>(effects, "gateObservedOpen") && !CeremonyField<bool>(effects, "gateObservedCrossed"),
                        "returning through the actual Comfort action restarts neither launch nor consumed gate power");
                    checks.Require(game.State.Apply(OrbitalEvent.ReturnGate) && game.State.GateCrossed,
                        "synthetic accepted crossing sets the real GateCrossed flag");
                    effects.PlayEvent(OrbitalEvent.ReturnGate); Step(.03f);
                    checks.Require(CeremonyField<float>(effects, "gateConfirmationElapsed") > 0,
                        "actual crossing starts the short supplementary confirmation");
                    float crossingClock = CeremonyField<float>(effects, "gateConfirmationElapsed");
                    effects.PlayEvent(OrbitalEvent.ReturnGate); Step(.02f);
                    checks.Require(CeremonyField<float>(effects, "gateConfirmationElapsed") > crossingClock,
                        "duplicate crossing signals do not restart gate confirmation");
                    Step(.31f);
                    checks.Require(CeremonyField<float>(effects, "gateConfirmationElapsed") < 0 && gate.gameObject.activeSelf,
                        "gate crossing motion ends while its actual open state remains visible");
                    RequireCeremonyNodesHidden(CeremonyField<Transform[]>(effects, "gateScanLoops"), checks, "crossing scans end after 0.3 seconds");

                    Reset(); ArmCeremonyGate(game, effects, checks); Step(.08f);
                    Field("replaying").SetValue(game, true); Step(.02f);
                    checks.Require(!gate.gameObject.activeSelf, "replay immediately hides active gate power motion");
                    Field("replaying").SetValue(game, false); Step(.02f);
                    checks.Require(CeremonyField<float>(effects, "gatePowerElapsed") < 0,
                        "leaving replay does not manufacture another gate-open transition");
                    Reset(); ArmCeremonyGate(game, effects, checks); Step(.08f);
                    checks.Require(game.State.Apply(OrbitalEvent.Recovery) && game.State.Phase == OrbitalPhase.Failure,
                        "synthetic recovery ends the gate test in genuine failure state");
                    Step(.02f);
                    checks.Require(!gate.gameObject.activeSelf && Mathf.Abs(Mathf.Abs(locks[0].localPosition.x) - .037f) < .000001f,
                        "failure hides the gate ceremony and returns its locks immediately");
                }
                else checks.Require(gate == null && locks.Length == 0, "boards without a gate allocate no gate ceremony");

                Reset();
                checks.Require(game.Launch(), "delivery contract begins with an actual immediate launch");
                ReceiveCeremonySuccess(game, effects, checks);
                checks.Require(game.Profile.For(game.State.Level).completed, "Receive Dock records completion in the protected temporary profile");
                checks.Require(game.DeliveryPresentationPending && !(bool)Field("deliveryConfirmed").GetValue(game),
                    "real success waits for delivery presentation confirmation");
                RequireCeremonyContinue(game, false, checks);
                Step(OrbitalEnergyEffects.DockDuration * .229f);
                checks.Require(ballVisual.gameObject.activeSelf, "decorative ball remains visible before normalized delivery progress 0.23");
                Step(OrbitalEnergyEffects.DockDuration * .003f);
                checks.Require(!ballVisual.gameObject.activeSelf, "normalized delivery progress 0.23 consumes only the decorative ball");
                Step(.79f - CeremonyField<float>(effects, "dockElapsed"));
                checks.Require(game.DeliveryPresentationPending && !(bool)Field("deliveryConfirmed").GetValue(game),
                    "before 0.8 seconds the success UI remains in receiving state");
                RequireCeremonyContinue(game, false, checks);
                Step(.02f);
                checks.Require(CeremonyField<bool>(effects, "deliveryLockPlayed") && game.DeliveryPresentationPending,
                    "lock closure at 0.8 seconds precedes UI confirmation");
                Step(.06f);
                checks.Require(!game.DeliveryPresentationPending && (bool)Field("deliveryConfirmed").GetValue(game),
                    "after 0.86 seconds confirmed delivery releases the next-level action");
                RequireCeremonyContinue(game, true, checks);
                Step(.23f);
                checks.Require(!effects.DockActive && effects.DockSequenceCount == 1, "the receiver ends after 1.05 seconds without duplicating delivery");
                RequireCeremonyNodesHidden(CeremonyField<Transform[]>(effects, "dockFlow"), checks, "finished delivery has no flowing sparks");
                RequireCeremonyNodesHidden(CeremonyField<Transform[]>(effects, "dockWaves"), checks, "finished delivery has no transient scan waves");
                var deliveryLocks = CeremonyField<Transform[]>(effects, "deliveryLocks");
                foreach (var leaf in deliveryLocks)
                    checks.Require(leaf.gameObject.activeSelf && Mathf.Abs(new Vector2(leaf.localPosition.x, leaf.localPosition.z).magnitude - .028f) < .000001f &&
                        Mathf.Abs(leaf.localPosition.y - .011f) < .000001f, "delivery leaves remain closed and seated after capture");
                var delivered = CaptureDecorations(effects); Step(.30f);
                RequireDecorations(delivered, checks, "completed receiver decorations remain settled");
                effects.PlayEvent(OrbitalEvent.Dock);
                checks.Require(effects.DockSequenceCount == 1 && !effects.DockActive, "repeated completed delivery cannot restart its ceremony");

                game.Replay();
                checks.Require(game.IsReplaying, "actual Replay starts from the recorded synthetic contract endpoints");
                Step(.02f);
                RequireCeremonyCleanup(effects, checks, "replay clears delivery and launch motions");
                checks.Require(ballVisual.gameObject.activeSelf, "replay restores the decorative energy ball");
                Reset(); RequireCeremonyCleanup(effects, checks, "retry clears all new ceremony motion");
                checks.Require(!game.DeliveryPresentationPending && !(bool)Field("deliveryConfirmed").GetValue(game), "retry resets delivery UI confirmation");

                checks.Require(game.Launch(), "Comfort-during-receiving regression begins with a normal launch");
                ReceiveCeremonySuccess(game, effects, checks); Step(.30f);
                checks.Require(game.DeliveryPresentationPending && !(bool)Field("deliveryConfirmed").GetValue(game),
                    "the separate Comfort regression is still receiving before confirmation");
                int deliveriesBeforeComfort = effects.DockSequenceCount;
                int deliveryLaunchesBeforeComfort = effects.LaunchSequenceCount;
                game.ActivateControl(OrbitalControlKind.Comfort, 0);
                checks.Require(game.ReducedMotion && !game.DeliveryPresentationPending && (bool)Field("deliveryConfirmed").GetValue(game),
                    "the actual Comfort action immediately confirms an accepted delivery that was still receiving");
                RequireCeremonyContinue(game, true, checks); Step(.01f);
                checks.Require(!effects.DockActive && effects.DockSequenceCount == deliveriesBeforeComfort &&
                    effects.LaunchSequenceCount == deliveryLaunchesBeforeComfort && CeremonyField<float>(effects, "launchElapsed") < 0 &&
                    CeremonyField<bool>(effects, "deliveryConfirmationPlayed"),
                    "settling the receiver through Comfort preserves event counts and consumes its confirmation");
                RequireCeremonyNodesHidden(CeremonyField<Transform[]>(effects, "dockFlow"), checks, "Comfort immediately settles receiver flow");
                RequireCeremonyNodesHidden(CeremonyField<Transform[]>(effects, "dockWaves"), checks, "Comfort immediately settles receiver scans");
                game.ActivateControl(OrbitalControlKind.Comfort, 0);
                checks.Require(!game.ReducedMotion && (bool)Field("deliveryConfirmed").GetValue(game) && !game.DeliveryPresentationPending,
                    "returning through Comfort preserves the existing delivery confirmation");
                effects.PlayEvent(OrbitalEvent.Dock); Step(.02f);
                checks.Require(!effects.DockActive && effects.DockSequenceCount == deliveriesBeforeComfort &&
                    effects.LaunchSequenceCount == deliveryLaunchesBeforeComfort && CeremonyField<float>(effects, "launchElapsed") < 0 &&
                    CeremonyField<bool>(effects, "deliveryConfirmationPlayed"),
                    "returning to dynamic presentation rejects an old Dock event rather than repeating receiver motion or confirmation");
                RequireCeremonyContinue(game, true, checks); Step(OrbitalEnergyEffects.DockDuration + .05f);
                checks.Require(!effects.DockActive && effects.DockSequenceCount == deliveriesBeforeComfort &&
                    (bool)Field("deliveryConfirmed").GetValue(game) && !game.DeliveryPresentationPending,
                    "confirmed delivery remains settled after enough time for an accidental second ceremony to finish");
                RequireCeremonyNodesHidden(CeremonyField<Transform[]>(effects, "dockFlow"), checks, "Comfort round trip leaves no receiver flow");
                RequireCeremonyNodesHidden(CeremonyField<Transform[]>(effects, "dockWaves"), checks, "Comfort round trip leaves no receiver waves");
                Reset();

                checks.Require(game.Launch(), "failure cleanup contract begins running");
                game.Receive(OrbitalEvent.Recovery); Step(.02f);
                checks.Require(game.State.Phase == OrbitalPhase.Failure, "Receive Recovery reaches the real failure state");
                RequireCeremonyCleanup(effects, checks, "failure clears all ceremony motion");

                Reset(); game.Profile.reducedMotion = true;
                checks.Require(game.Launch() && !game.Ball.isKinematic && Mathf.Abs(game.Ball.linearVelocity.magnitude - game.launchSpeed) < .00001f,
                    "Reduced Motion also launches the real ball immediately at launchSpeed");
                ReceiveCeremonySuccess(game, effects, checks);
                checks.Require(!game.DeliveryPresentationPending && (bool)Field("deliveryConfirmed").GetValue(game),
                    "Reduced Motion confirms accepted success immediately without waiting for animation");
                RequireCeremonyContinue(game, true, checks);
                Step(.02f);
                checks.Require(!effects.DockActive && effects.DockSequenceCount == 0 && effects.LaunchSequenceCount == 0,
                    "Reduced Motion never starts launch or receiver motion sequences");
                RequireCeremonyNodesHidden(CeremonyField<Transform[]>(effects, "dockFlow"), checks, "Reduced Motion hides delivery flow");
                RequireCeremonyNodesHidden(CeremonyField<Transform[]>(effects, "dockWaves"), checks, "Reduced Motion hides delivery waves");
                RequireCeremonyNodesHidden(CeremonyField<Transform[]>(effects, "gateScanLoops"), checks, "Reduced Motion hides gate scans");
                checks.scenarios.Add(game.State.Level + ": readiness/instant real launch; synthetic flag-driven gate, actual Comfort control round trips while paused/receiving, replay/failure cleanup; Receive-driven delivery timing and protected completion/UI contract (not physical-flight evidence)");
            }
            finally
            {
                game.Profile.reducedMotion = false;
                Field("replaying").SetValue(game, false);
                game.ResetAttempt(); effects.ResetEffects();
                Call(game, "RefreshExperience");
            }
            checks.Require(game.State.Phase == OrbitalPhase.Configuring && !game.ReducedMotion && !game.IsReplaying,
                "ceremony contract leaves the original verifier in non-reduced configuration state");
        }

        static T CeremonyField<T>(OrbitalEnergyEffects effects, string name) =>
            (T)typeof(OrbitalEnergyEffects).GetField(name, Private).GetValue(effects);

        static void ArmCeremonyGate(OrbitalPrototype game, OrbitalEnergyEffects effects, Checks checks)
        {
            checks.Require(game.State.Launch(), "synthetic gate contract starts its run-state sequence");
            foreach (var signal in Sequence(game.State.Level))
            {
                if (signal == OrbitalEvent.ReturnGate) break;
                checks.Require(game.State.Apply(signal) && game.State.Phase == OrbitalPhase.Running,
                    "synthetic prerequisite is accepted before gate power");
                effects.PlayEvent(signal);
            }
            checks.Require(game.State.GateOpen && !game.State.GateCrossed, "prerequisite flags genuinely open this level's gate");
        }

        static void ReceiveCeremonySuccess(OrbitalPrototype game, OrbitalEnergyEffects effects, Checks checks)
        {
            foreach (var signal in Sequence(game.State.Level))
            {
                if (signal == OrbitalEvent.Dock)
                {
                    // This is an explicit sampled pose, not a simulated arrival.
                    var board = CeremonyField<Transform>(effects, "board");
                    game.Ball.position = board.TransformPoint(CeremonyField<Vector3>(effects, "dockLocal") + Vector3.up * .025f);
                }
                game.Receive(signal);
                checks.Require(game.State.Phase == (signal == OrbitalEvent.Dock ? OrbitalPhase.Success : OrbitalPhase.Running),
                    "Receive accepts the synthetic ceremony objective in the required order");
                effects.PlayEvent(signal);
            }
        }

        static void RequireCeremonyContinue(OrbitalPrototype game, bool visible, Checks checks)
        {
            Call(game, "RefreshExperience");
            int count = 0;
            foreach (var control in (OrbitalControl[])Field("controls").GetValue(game))
                if (control.kind == OrbitalControlKind.Continue)
                {
                    count++;
                    checks.Require(control.gameObject.activeSelf == visible && control.GetComponent<OrbitalClickTarget>().interactable == visible,
                        visible ? "confirmed delivery exposes an actionable Continue control" : "receiving keeps Continue hidden and non-interactable");
                }
            checks.Require(count > 0, "the complete game interface contains a Continue control");
        }

        static void RequireCeremonyNodesHidden(Transform[] nodes, Checks checks, string context)
        {
            foreach (var node in nodes) checks.Require(!node.gameObject.activeSelf, context);
        }

        static void RequireCeremonyCleanup(OrbitalEnergyEffects effects, Checks checks, string context)
        {
            RequireCeremonyNodesHidden(CeremonyField<Transform[]>(effects, "muzzleStreaks"), checks, context);
            RequireCeremonyNodesHidden(CeremonyField<Transform[]>(effects, "gateScanLoops"), checks, context);
            RequireCeremonyNodesHidden(CeremonyField<Transform[]>(effects, "deliveryLocks"), checks, context);
            RequireCeremonyNodesHidden(CeremonyField<Transform[]>(effects, "dockFlow"), checks, context);
            RequireCeremonyNodesHidden(CeremonyField<Transform[]>(effects, "dockWaves"), checks, context);
            checks.Require(!CeremonyField<Transform>(effects, "muzzleWave").gameObject.activeSelf &&
                !CeremonyField<Transform>(effects, "deliveryCore").gameObject.activeSelf && !effects.DockActive, context);
        }
    }
}
