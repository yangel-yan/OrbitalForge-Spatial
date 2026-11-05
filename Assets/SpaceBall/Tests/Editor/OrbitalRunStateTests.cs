#if UNITY_INCLUDE_TESTS
using NUnit.Framework;

namespace SpaceBall.Tests
{
    public sealed class OrbitalRunStateTests
    {
        [TestCase(OrbitalLevel.Tutorial, 0)]
        [TestCase(OrbitalLevel.Moon, 1)]
        public void NewLevelStartsWithItsOwnConfiguration(OrbitalLevel level, int reflector)
        {
            var state = new OrbitalRunState(level);

            Assert.That(state.Level, Is.EqualTo(level));
            Assert.That(state.Phase, Is.EqualTo(OrbitalPhase.Configuring));
            Assert.That(state.Route, Is.Zero);
            Assert.That(state.Reflector, Is.EqualTo(reflector));
            AssertFreshProgress(state);
        }

        [Test]
        public void TutorialRequiresBothTargetsBeforeDelivery()
        {
            var state = new OrbitalRunState();
            Assert.That(state.Launch(), Is.True);

            Assert.That(state.Apply(OrbitalEvent.TargetA), Is.True);
            Assert.That(state.Phase, Is.EqualTo(OrbitalPhase.Running));
            Assert.That(state.GateOpen, Is.False, "The tutorial has no gate to open.");
            Assert.That(state.Apply(OrbitalEvent.TargetB), Is.True);
            Assert.That(state.Phase, Is.EqualTo(OrbitalPhase.Running), "Targets alone do not deliver the ball.");
            Assert.That(state.Apply(OrbitalEvent.Dock), Is.True);

            Assert.That(state.Phase, Is.EqualTo(OrbitalPhase.Success));
            Assert.That(state.ResultCode, Is.EqualTo("success"));
            Assert.That(state.HasA && state.HasB, Is.True);
            Assert.That(state.GateCrossed, Is.False);
        }

        [Test]
        public void MoonRequiresOneContinuousChargeGateTargetDeliverySequence()
        {
            var state = new OrbitalRunState(OrbitalLevel.Moon);
            state.Launch();
            state.Apply(OrbitalEvent.TargetA);

            Assert.That(state.GateOpen, Is.True);
            Assert.That(state.GateCrossed, Is.False, "Opening a gate does not count as crossing it.");
            state.Apply(OrbitalEvent.ReturnGate);
            Assert.That(state.GateCrossed, Is.True);
            state.Apply(OrbitalEvent.TargetB);
            Assert.That(state.Phase, Is.EqualTo(OrbitalPhase.Running));
            state.Apply(OrbitalEvent.Dock);

            Assert.That(state.Phase, Is.EqualTo(OrbitalPhase.Success));
            Assert.That(state.ResultCode, Is.EqualTo("success"));
        }

        [TestCase(OrbitalLevel.Tutorial)]
        [TestCase(OrbitalLevel.Moon)]
        public void TargetBReachedBeforeAEndsTheAttempt(OrbitalLevel level)
        {
            var state = new OrbitalRunState(level);
            state.Launch();

            Assert.That(state.Apply(OrbitalEvent.TargetB), Is.True, "The invalid event is consumed as a failure.");

            Assert.That(state.Phase, Is.EqualTo(OrbitalPhase.Failure));
            Assert.That(state.ResultCode, Is.EqualTo("target_b_before_a"));
            Assert.That(state.HasB, Is.False);
        }

        [Test]
        public void MoonTargetBCannotSkipTheOpenedGate()
        {
            var state = new OrbitalRunState(OrbitalLevel.Moon);
            state.Launch();
            state.Apply(OrbitalEvent.TargetA);
            state.Apply(OrbitalEvent.TargetB);

            Assert.That(state.Phase, Is.EqualTo(OrbitalPhase.Failure));
            Assert.That(state.ResultCode, Is.EqualTo("target_b_before_gate"));
            Assert.That(state.GateOpen, Is.True);
            Assert.That(state.HasB, Is.False);
        }

        [TestCase(OrbitalLevel.Tutorial, false, false, "dock_missing_a")]
        [TestCase(OrbitalLevel.Tutorial, true, false, "dock_missing_b")]
        [TestCase(OrbitalLevel.Moon, false, false, "dock_missing_a")]
        [TestCase(OrbitalLevel.Moon, true, false, "dock_before_gate")]
        [TestCase(OrbitalLevel.Moon, true, true, "dock_missing_b")]
        public void DockExplainsTheFirstMissingRequirement(
            OrbitalLevel level, bool chargeA, bool crossGate, string expectedCode)
        {
            var state = new OrbitalRunState(level);
            state.Launch();
            if (chargeA)
            {
                state.Apply(OrbitalEvent.TargetA);
            }

            if (crossGate)
            {
                state.Apply(OrbitalEvent.ReturnGate);
            }

            state.Apply(OrbitalEvent.Dock);

            Assert.That(state.Phase, Is.EqualTo(OrbitalPhase.Failure));
            Assert.That(state.ResultCode, Is.EqualTo(expectedCode));
            Assert.That(state.Feedback, Is.Not.Empty);
        }

        [Test]
        public void UnchargedReturnGateStopsTheBallWithoutGrantingProgress()
        {
            var state = new OrbitalRunState(OrbitalLevel.Moon);
            state.Launch();
            state.Apply(OrbitalEvent.ReturnGate);

            Assert.That(state.Phase, Is.EqualTo(OrbitalPhase.Failure));
            Assert.That(state.ResultCode, Is.EqualTo("closed_gate"));
            Assert.That(state.GateOpen || state.GateCrossed, Is.False);
        }

        [Test]
        public void DuplicateChargeKeepsGateLatchedForTheWholeAttempt()
        {
            var state = new OrbitalRunState(OrbitalLevel.Moon);
            state.Launch();
            state.Apply(OrbitalEvent.TargetA);
            state.Apply(OrbitalEvent.ReturnGate);
            state.Apply(OrbitalEvent.TargetA);
            state.Apply(OrbitalEvent.ReturnGate);

            Assert.That(state.Phase, Is.EqualTo(OrbitalPhase.Running));
            Assert.That(state.HasA && state.GateOpen && state.GateCrossed, Is.True);
            Assert.That(state.ResultCode, Is.Empty);
        }

        [Test]
        public void TutorialCannotGainGateProgressFromAMoonEvent()
        {
            var state = new OrbitalRunState();
            state.Launch();
            state.Apply(OrbitalEvent.TargetA);
            state.Apply(OrbitalEvent.ReturnGate);

            Assert.That(state.Phase, Is.EqualTo(OrbitalPhase.Failure));
            Assert.That(state.ResultCode, Is.EqualTo("unexpected_gate"));
            Assert.That(state.GateOpen || state.GateCrossed, Is.False);
        }

        [Test]
        public void ActualClosedGateCollisionIsTerminalEvenAfterACharge()
        {
            var state = new OrbitalRunState(OrbitalLevel.Moon);
            state.Launch();
            state.Apply(OrbitalEvent.TargetA);
            state.Apply(OrbitalEvent.ClosedGate);

            Assert.That(state.Phase, Is.EqualTo(OrbitalPhase.Failure));
            Assert.That(state.ResultCode, Is.EqualTo("closed_gate"));
            Assert.That(state.GateCrossed, Is.False);
        }

        [TestCase(false, false, "recovery_uncharged")]
        [TestCase(true, false, "recovery_gate_open")]
        [TestCase(true, true, "recovery_after_gate")]
        public void MoonRecoveryDistinguishesChargingAndReturnFailures(
            bool chargeA, bool crossGate, string expectedCode)
        {
            var state = new OrbitalRunState(OrbitalLevel.Moon);
            state.Launch();
            if (chargeA)
            {
                state.Apply(OrbitalEvent.TargetA);
            }

            if (crossGate)
            {
                state.Apply(OrbitalEvent.ReturnGate);
            }

            state.Apply(OrbitalEvent.Recovery);

            Assert.That(state.Phase, Is.EqualTo(OrbitalPhase.Failure));
            Assert.That(state.ResultCode, Is.EqualTo(expectedCode));
            Assert.That(state.Feedback, Does.Contain("回收区"));
        }

        [TestCase(OrbitalPhase.Configuring)]
        [TestCase(OrbitalPhase.Paused)]
        [TestCase(OrbitalPhase.Success)]
        [TestCase(OrbitalPhase.Failure)]
        public void SensorEventsCannotChangeAnAttemptOutsideRunning(OrbitalPhase phase)
        {
            var state = StateAt(phase);
            var hadA = state.HasA;
            var hadB = state.HasB;
            var gateWasOpen = state.GateOpen;
            var gateWasCrossed = state.GateCrossed;
            var code = state.ResultCode;
            var feedback = state.Feedback;

            foreach (OrbitalEvent orbitalEvent in System.Enum.GetValues(typeof(OrbitalEvent)))
            {
                Assert.That(state.Apply(orbitalEvent), Is.False);
            }

            Assert.That(state.Phase, Is.EqualTo(phase));
            Assert.That(state.HasA, Is.EqualTo(hadA));
            Assert.That(state.HasB, Is.EqualTo(hadB));
            Assert.That(state.GateOpen, Is.EqualTo(gateWasOpen));
            Assert.That(state.GateCrossed, Is.EqualTo(gateWasCrossed));
            Assert.That(state.ResultCode, Is.EqualTo(code));
            Assert.That(state.Feedback, Is.EqualTo(feedback));
        }

        [TestCase(OrbitalPhase.Running)]
        [TestCase(OrbitalPhase.Paused)]
        [TestCase(OrbitalPhase.Success)]
        [TestCase(OrbitalPhase.Failure)]
        public void ConfigurationAndLaunchAreLockedUntilReset(OrbitalPhase phase)
        {
            var state = StateAt(phase);
            var route = state.Route;
            var reflector = state.Reflector;

            Assert.That(state.SetRoute(1 - route), Is.False);
            Assert.That(state.SetReflector(1 - reflector), Is.False);
            Assert.That(state.Launch(), Is.False);
            Assert.That(state.Route, Is.EqualTo(route));
            Assert.That(state.Reflector, Is.EqualTo(reflector));
            Assert.That(state.Phase, Is.EqualTo(phase));
        }

        [TestCase(OrbitalPhase.Paused)]
        [TestCase(OrbitalPhase.Success)]
        [TestCase(OrbitalPhase.Failure)]
        public void ResetPreservesConfigurationButNeverCarriesChargeIntoTheNextShot(OrbitalPhase phase)
        {
            var state = StateAt(phase);
            var route = state.Route;
            var reflector = state.Reflector;

            state.Reset();

            Assert.That(state.Phase, Is.EqualTo(OrbitalPhase.Configuring));
            Assert.That(state.Route, Is.EqualTo(route));
            Assert.That(state.Reflector, Is.EqualTo(reflector));
            AssertFreshProgress(state);
            Assert.That(state.Launch(), Is.True);
            AssertFreshProgress(state);
            state.Apply(OrbitalEvent.ReturnGate);
            Assert.That(state.ResultCode, Is.EqualTo("closed_gate"));
        }

        [Test]
        public void PauseAndResumePreserveProgressAndDoNotConsumeSensorEvents()
        {
            var state = new OrbitalRunState(OrbitalLevel.Moon);
            state.SetRoute(1);
            state.SetReflector(0);
            state.Launch();
            state.Apply(OrbitalEvent.TargetA);
            state.Apply(OrbitalEvent.ReturnGate);

            Assert.That(state.TogglePause(), Is.True);
            Assert.That(state.Phase, Is.EqualTo(OrbitalPhase.Paused));
            Assert.That(state.Apply(OrbitalEvent.TargetB), Is.False);
            Assert.That(state.HasB, Is.False);
            Assert.That(state.HasA && state.GateOpen && state.GateCrossed, Is.True);
            Assert.That(state.Route, Is.EqualTo(1));
            Assert.That(state.Reflector, Is.Zero);
            Assert.That(state.TogglePause(), Is.True);

            state.Apply(OrbitalEvent.TargetB);
            state.Apply(OrbitalEvent.Dock);
            Assert.That(state.Phase, Is.EqualTo(OrbitalPhase.Success));
        }

        [TestCase(OrbitalPhase.Configuring)]
        [TestCase(OrbitalPhase.Success)]
        [TestCase(OrbitalPhase.Failure)]
        public void PauseCannotReviveAnAttempt(OrbitalPhase phase)
        {
            var state = StateAt(phase);

            Assert.That(state.TogglePause(), Is.False);
            Assert.That(state.Phase, Is.EqualTo(phase));
        }

        [Test]
        public void SelectingAnotherLevelResetsConfigurationAndAllAttemptProgress()
        {
            var state = StateAt(OrbitalPhase.Success);

            state.SelectLevel(OrbitalLevel.Tutorial);

            Assert.That(state.Level, Is.EqualTo(OrbitalLevel.Tutorial));
            Assert.That(state.Route, Is.Zero);
            Assert.That(state.Reflector, Is.Zero);
            Assert.That(state.Phase, Is.EqualTo(OrbitalPhase.Configuring));
            AssertFreshProgress(state);

            state.SetRoute(1);
            state.SelectLevel(OrbitalLevel.Moon);
            Assert.That(state.Route, Is.Zero);
            Assert.That(state.Reflector, Is.EqualTo(1));
            AssertFreshProgress(state);
        }

        [Test]
        public void SeparatePuzzleInstancesNeverShareConfigurationOrCharge()
        {
            var moon = new OrbitalRunState(OrbitalLevel.Moon);
            var tutorial = new OrbitalRunState();
            moon.SetRoute(1);
            moon.Launch();
            moon.Apply(OrbitalEvent.TargetA);

            Assert.That(tutorial.Level, Is.EqualTo(OrbitalLevel.Tutorial));
            Assert.That(tutorial.Route, Is.Zero);
            Assert.That(tutorial.Phase, Is.EqualTo(OrbitalPhase.Configuring));
            AssertFreshProgress(tutorial);
        }

        [TestCase(-1)]
        [TestCase(2)]
        public void UnsupportedMechanismSettingsAreRejectedWithoutMutation(int invalidSetting)
        {
            var state = new OrbitalRunState(OrbitalLevel.Moon);

            Assert.That(state.SetRoute(invalidSetting), Is.False);
            Assert.That(state.SetReflector(invalidSetting), Is.False);
            Assert.That(state.Route, Is.Zero);
            Assert.That(state.Reflector, Is.EqualTo(1));
        }

        private static OrbitalRunState StateAt(OrbitalPhase phase)
        {
            var state = new OrbitalRunState(OrbitalLevel.Moon);
            state.SetRoute(1);
            state.SetReflector(0);
            if (phase == OrbitalPhase.Configuring)
            {
                return state;
            }

            state.Launch();
            state.Apply(OrbitalEvent.TargetA);
            if (phase == OrbitalPhase.Paused)
            {
                state.Apply(OrbitalEvent.ReturnGate);
                state.TogglePause();
            }
            else if (phase == OrbitalPhase.Success)
            {
                state.Apply(OrbitalEvent.ReturnGate);
                state.Apply(OrbitalEvent.TargetB);
                state.Apply(OrbitalEvent.Dock);
            }
            else if (phase == OrbitalPhase.Failure)
            {
                state.Apply(OrbitalEvent.Recovery);
            }

            return state;
        }

        private static void AssertFreshProgress(OrbitalRunState state)
        {
            Assert.That(state.HasA || state.HasB || state.GateOpen || state.GateCrossed, Is.False);
            Assert.That(state.ResultCode, Is.Empty);
            Assert.That(state.Feedback, Is.Not.Empty);
        }
    }
}
#endif
