using UnityEngine;

namespace SpaceBall
{
    [RequireComponent(typeof(OrbitalClickTarget))]
    public sealed class OrbitalAssemblySocket : MonoBehaviour
    {
        public OrbitalAssemblyRig rig;
        public int index;
        public Transform selectionHalo;
        Transform dockGuide, rejectCue;
        Renderer haloRenderer, guideRenderer;
        Material restingMaterial;
        bool initialized, highlighted, compatibleNear, incompatibleNear;
        float rejectionTime;

        void OnEnable()
        {
            GetComponent<OrbitalClickTarget>().Clicked += Place;
            EnsureVisuals();
        }

        void OnDisable()
        {
            GetComponent<OrbitalClickTarget>().Clicked -= Place;
            ResetFeedback();
        }

        void Place() { if (rig != null) rig.PlaceSelected(index); }

        public void Highlight(bool active) { SetHint(active, false, false); }

        public void SetHint(bool selected, bool matchingPieceNear, bool wrongPieceNear)
        {
            EnsureVisuals();
            highlighted = selected || matchingPieceNear;
            compatibleNear = matchingPieceNear;
            incompatibleNear = wrongPieceNear;
            ApplyState();
        }

        public void Reject()
        {
            EnsureVisuals();
            rejectionTime = .55f;
            ApplyState();
        }

        public void ResetFeedback()
        {
            EnsureVisuals();
            highlighted = compatibleNear = incompatibleNear = false;
            rejectionTime = 0;
            ApplyState();
            SettlePresentation();
        }

        public void SettlePresentation()
        {
            if (selectionHalo != null) selectionHalo.localScale = Vector3.one;
            if (dockGuide != null) dockGuide.localScale = Vector3.one;
            if (rejectCue != null) rejectCue.localScale = Vector3.one;
        }

        void EnsureVisuals()
        {
            if (initialized) return;
            if (selectionHalo == null) selectionHalo = transform.Find("SelectionHalo");
            dockGuide = transform.Find("DockGuide");
            rejectCue = transform.Find("RejectCue");
            if (selectionHalo != null)
            {
                haloRenderer = selectionHalo.GetComponent<Renderer>();
                if (haloRenderer != null) restingMaterial = haloRenderer.sharedMaterial;
            }
            if (dockGuide != null) guideRenderer = dockGuide.GetComponent<Renderer>();
            initialized = true;
        }

        void ApplyState()
        {
            if (selectionHalo != null) selectionHalo.gameObject.SetActive(highlighted);
            if (dockGuide != null) dockGuide.gameObject.SetActive(highlighted);
            if (rejectCue != null) rejectCue.gameObject.SetActive(incompatibleNear || rejectionTime > 0);
            Material material = restingMaterial;
            if (compatibleNear && rig != null && rig.owner != null && rig.owner.palette != null && rig.owner.palette.Length > 4)
                material = rig.owner.palette[4];
            if (haloRenderer != null && material != null && haloRenderer.sharedMaterial != material) haloRenderer.sharedMaterial = material;
            if (guideRenderer != null && material != null && guideRenderer.sharedMaterial != material) guideRenderer.sharedMaterial = material;
        }

        void LateUpdate()
        {
            if (rig == null || !rig.Editable)
            {
                ResetFeedback();
                return;
            }
            bool reduced = rig.owner.ReducedMotion;
            if (rejectionTime > 0)
            {
                rejectionTime = Mathf.Max(0, rejectionTime - Time.unscaledDeltaTime);
                if (rejectionTime <= 0) ApplyState();
            }
            if (highlighted)
            {
                float breath = reduced ? 0 : Mathf.Sin(Time.unscaledTime * 4f) * .015f;
                if (selectionHalo != null) selectionHalo.localScale = Vector3.one * (1f + breath);
                // A smaller, closed frame means "fits here", not "the route is correct".
                if (dockGuide != null)
                {
                    Vector3 target = Vector3.one * (compatibleNear ? .91f : 1f);
                    dockGuide.localScale = reduced ? target : Vector3.Lerp(dockGuide.localScale, target, 1f - Mathf.Exp(-18f * Time.unscaledDeltaTime));
                }
            }
            else SettlePresentation();
        }
    }
}
