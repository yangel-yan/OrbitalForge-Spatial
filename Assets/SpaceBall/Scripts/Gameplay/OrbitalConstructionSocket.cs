using UnityEngine;

namespace SpaceBall
{
    [RequireComponent(typeof(OrbitalClickTarget))]
    public sealed class OrbitalConstructionSocket : MonoBehaviour
    {
        public OrbitalConstructionKit kit;
        public int index;
        public Transform selectionHalo;
        Transform rejectCue;
        Transform cachedHalo;
        Vector3 haloScale = Vector3.one;
        Vector3 rejectScale = Vector3.one;
        bool highlighted, near;
        float rejectTime;

        void OnEnable()
        {
            GetComponent<OrbitalClickTarget>().Clicked += Place;
            if (rejectCue == null) rejectCue = transform.Find("RejectCue");
            if (rejectCue != null) rejectScale = rejectCue.localScale;
        }

        void OnDisable()
        {
            GetComponent<OrbitalClickTarget>().Clicked -= Place;
            ResetFeedback();
        }

        void Place() { if (kit != null) kit.PlaceSelected(index); }

        public void SetHint(bool selected, bool close, bool occupied)
        {
            CacheHalo();
            highlighted = selected || close;
            near = close;
            if (selectionHalo != null && selectionHalo.gameObject.activeSelf != highlighted) selectionHalo.gameObject.SetActive(highlighted);
            if (rejectCue != null && rejectCue.gameObject.activeSelf != (occupied || rejectTime > 0)) rejectCue.gameObject.SetActive(occupied || rejectTime > 0);
            if (!highlighted) SettlePresentation();
        }

        public void Reject()
        {
            rejectTime = .45f;
            if (rejectCue == null) rejectCue = transform.Find("RejectCue");
            if (rejectCue != null) rejectCue.gameObject.SetActive(true);
        }

        public void ResetFeedback()
        {
            highlighted = near = false;
            rejectTime = 0;
            if (selectionHalo != null && selectionHalo.gameObject.activeSelf) selectionHalo.gameObject.SetActive(false);
            if (rejectCue != null && rejectCue.gameObject.activeSelf) rejectCue.gameObject.SetActive(false);
            SettlePresentation();
        }

        public void SettlePresentation()
        {
            CacheHalo();
            if (selectionHalo != null && selectionHalo.localScale != haloScale) selectionHalo.localScale = haloScale;
            if (rejectCue != null && rejectCue.localScale != rejectScale) rejectCue.localScale = rejectScale;
        }

        void CacheHalo()
        {
            if (selectionHalo == cachedHalo) return;
            cachedHalo = selectionHalo;
            if (selectionHalo != null) haloScale = selectionHalo.localScale;
        }

        void LateUpdate()
        {
            if (kit == null || !kit.Editable)
            {
                if (highlighted || near || rejectTime > 0 || selectionHalo != null && selectionHalo.gameObject.activeSelf)
                    ResetFeedback();
                return;
            }
            if (rejectTime > 0)
            {
                rejectTime = Mathf.Max(0, rejectTime - Time.unscaledDeltaTime);
                if (rejectTime <= 0 && rejectCue != null) rejectCue.gameObject.SetActive(false);
            }
            if (highlighted && selectionHalo != null)
                selectionHalo.localScale = haloScale * (near ? .93f : kit.owner.ReducedMotion ? 1 : 1 + Mathf.Sin(Time.unscaledTime * 4) * .014f);
        }
    }
}
