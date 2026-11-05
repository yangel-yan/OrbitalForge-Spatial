using UnityEngine;

namespace SpaceBall
{
    public enum OrbitalFeedbackCue
    {
        Select, Install, Rotate, Reject, Launch, Gate, Failure, Impact,
        ChargeReady, GatePower, DockLock
    }

    /// <summary>
    /// Distinct interaction sounds complement the existing A/B/delivery cues.
    /// Callers report actual outcomes; this component never guesses correctness.
    /// Clips are persistent imported assets so Spatial export can include them.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OrbitalFeedbackAudio : MonoBehaviour
    {
        [Tooltip("Interaction and ceremony cues, in OrbitalFeedbackCue enum order.")]
        public AudioClip[] cues = new AudioClip[11];
        [Range(0f, 1f)] public float volume = .65f;
        OrbitalPrototype owner;
        AudioSource source;
        float nextImpactTime;
        float lastCueTime = -1;
        OrbitalFeedbackCue lastCue;

        public void Bind(OrbitalPrototype prototype, AudioSource audioSource)
        {
            owner = prototype;
            source = audioSource;
            nextImpactTime = 0;
            lastCueTime = -1;
        }

        public void Play(OrbitalFeedbackCue cue, float strength = 1f)
        {
            if (owner == null || source == null || !owner.Profile.sound || owner.IsReplaying ||
                !isActiveAndEnabled || !source.isActiveAndEnabled) return;
            int index = (int)cue;
            if (cues == null || index < 0 || index >= cues.Length || cues[index] == null) return;
            // One rejected attempt can report at both piece and socket boundaries.
            // Coalesce that event without masking a later intentional selection.
            if (cue == lastCue && Time.unscaledTime - lastCueTime < .045f) return;
            lastCue = cue;
            lastCueTime = Time.unscaledTime;
            source.PlayOneShot(cues[index], volume * Mathf.Clamp01(strength));
        }

        public void Contact(float relativeSpeed)
        {
            if (owner == null || owner.State.Phase != OrbitalPhase.Running || owner.IsReplaying ||
                relativeSpeed < .12f || Time.unscaledTime < nextImpactTime) return;
            nextImpactTime = Time.unscaledTime + .12f;
            Play(OrbitalFeedbackCue.Impact, Mathf.Lerp(.24f, .72f, Mathf.InverseLerp(.12f, 1.2f, relativeSpeed)));
        }

        void OnDisable()
        {
            nextImpactTime = 0;
            lastCueTime = -1;
        }
    }
}
