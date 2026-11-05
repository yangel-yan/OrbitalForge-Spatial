using System;
using UnityEngine;

namespace SpaceBall
{
    [DisallowMultipleComponent]
    public sealed class OrbitalClickTarget : MonoBehaviour
    {
        public bool interactable = true;

        public event Action Clicked;

        int m_LastActivationFrame = -1;

        public void Activate()
        {
            if (!isActiveAndEnabled || !interactable || m_LastActivationFrame == Time.frameCount)
                return;

            m_LastActivationFrame = Time.frameCount;
            Clicked?.Invoke();
        }
    }
}
