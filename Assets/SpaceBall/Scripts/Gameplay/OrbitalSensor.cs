using UnityEngine;

namespace SpaceBall
{
    public sealed class OrbitalSensor : MonoBehaviour
    {
        public OrbitalPrototype owner;
        public OrbitalEvent signal;
        void OnTriggerEnter(Collider other)
        {
            if (owner != null && other.attachedRigidbody == owner.Ball)
                owner.Receive(signal);
        }
    }
}
