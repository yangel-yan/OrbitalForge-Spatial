using UnityEngine;

namespace SpaceBall
{
    /// <summary>Observes real PhysX contact; never changes force, pose or velocity.</summary>
    [DisallowMultipleComponent]
    public sealed class OrbitalBallContactFeedback : MonoBehaviour
    {
        OrbitalPrototype owner;
        OrbitalFeedbackAudio audioFeedback;

        public void Bind(OrbitalPrototype prototype, OrbitalFeedbackAudio feedback)
        {
            owner = prototype;
            audioFeedback = feedback;
        }

        void OnCollisionEnter(Collision contact)
        {
            if (owner == null || audioFeedback == null || owner.Ball == null ||
                owner.Ball.gameObject != gameObject || owner.IsReplaying ||
                owner.State.Phase != OrbitalPhase.Running) return;
            // Ignore the first floor contact when launching. Later contacts use
            // normal approach speed, keeping harmless rolling chatter quiet.
            if (owner.ShotElapsed < .10f || contact.contactCount == 0) return;
            float approachSpeed = Mathf.Abs(Vector3.Dot(contact.relativeVelocity, contact.GetContact(0).normal));
            audioFeedback.Contact(approachSpeed);
        }
    }
}
