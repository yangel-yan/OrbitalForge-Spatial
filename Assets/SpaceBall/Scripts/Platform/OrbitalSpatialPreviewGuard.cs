using UnityEngine;

namespace SpaceBall
{
    /// <summary>
    /// SpatialAdapter 6.1's window-mode synchronizer touches Android JNI in Editor.
    /// Play-to-PICO already owns the preview window. The Android player retains
    /// the vendor component and this guard is entirely excluded from that build.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-1000000)]
    public sealed class OrbitalSpatialPreviewGuard : MonoBehaviour
    {
#if UNITY_EDITOR && PICO_MS_SDK
        void Awake()
        {
            var sync=GetComponent<ByteDance.PICO.XR.PXR_SpatialCameraSpatialModeSynchronizer>();
            if(sync!=null) sync.enabled=false;
        }
#endif
    }
}
