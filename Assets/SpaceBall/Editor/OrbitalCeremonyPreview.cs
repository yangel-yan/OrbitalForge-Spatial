using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace SpaceBall.Editor
{
    /// <summary>Pauses real P2P playback at a named beat for a native spatial screenshot.</summary>
    [InitializeOnLoad]
    public static class OrbitalCeremonyPreview
    {
        static OrbitalEnergyEffects effects;
        static FieldInfo elapsed;
        static float threshold;
        static bool armed, ownsPause;
        public static string LastBeat { get; private set; } = "idle";

        static OrbitalCeremonyPreview()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingPlayMode) Release();
            };
            AssemblyReloadEvents.beforeAssemblyReload += Release;
        }

        public static string Arm(string beat)
        {
            if (!Application.isPlaying || EditorApplication.isPaused || armed)
                throw new InvalidOperationException("Arm one inspection during unpaused Play mode.");
            var game = UnityEngine.Object.FindFirstObjectByType<OrbitalPrototype>();
            effects = game.GetComponentInChildren<OrbitalEnergyEffects>();
            string field;
            switch (beat)
            {
                case "charge": field = "chargeElapsed"; threshold = .20f; break;
                case "launch": field = "launchElapsed"; threshold = 0; break;
                case "gate": field = "gatePowerElapsed"; threshold = .20f; break;
                case "capture": field = "dockElapsed"; threshold = .43f; break;
                case "confirmation": field = "dockElapsed"; threshold = .90f; break;
                default: throw new ArgumentException("Unknown ceremony inspection beat: " + beat, nameof(beat));
            }
            elapsed = typeof(OrbitalEnergyEffects).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
            LastBeat = "waiting:" + beat;
            armed = true;
            EditorApplication.update += Inspect;
            return LastBeat;
        }

        static void Inspect()
        {
            if ((float)elapsed.GetValue(effects) < threshold) return;
            EditorApplication.update -= Inspect;
            armed = false; ownsPause = true;
            LastBeat = LastBeat.Replace("waiting:", "paused:");
            EditorApplication.isPaused = true;
        }

        public static void Release()
        {
            EditorApplication.update -= Inspect;
            armed = false;
            if (ownsPause) EditorApplication.isPaused = false;
            ownsPause = false;
            LastBeat = "idle";
        }
    }
}
