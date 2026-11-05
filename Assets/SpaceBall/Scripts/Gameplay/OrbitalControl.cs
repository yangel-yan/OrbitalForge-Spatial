using UnityEngine;

namespace SpaceBall
{
    public enum OrbitalControlKind { Level, Route, Reflector, Launch, Reset, Pause, Replay, Continue, Hint, Trace, Audio, Comfort, Settings }

    [RequireComponent(typeof(OrbitalClickTarget))]
    public sealed class OrbitalControl : MonoBehaviour
    {
        public OrbitalPrototype owner;
        public OrbitalControlKind kind;
        public int value;
        OrbitalClickTarget target;
        void OnEnable() { target = GetComponent<OrbitalClickTarget>(); target.Clicked += Click; }
        void OnDisable() { if (target != null) target.Clicked -= Click; }
        void Click() { if (owner != null) owner.ActivateControl(kind, value); }
    }
}
