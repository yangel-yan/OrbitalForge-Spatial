using UnityEngine;

namespace SpaceBall
{
    /// <summary>Consumes system selection positions, without hand joints or controllers.</summary>
    public sealed class OrbitalAssemblyPiece : OrbitalManipulable
    {
        public OrbitalAssemblyRig rig;
        public int index;
        public Vector3 homePosition, socketPosition;
        public int initialChoice;
        public float alternateYaw = -90f;
        public Transform directionRotor, selectionHalo;
        [SerializeField] bool installed;
        [SerializeField] int choice;
        bool manipulating, moved;
        Vector3 grabWorld, originalLocal;
        Plane dragPlane;
        Transform shell, selectionBrackets, feedbackHalo, badge;
        Renderer feedbackRenderer;
        Vector3 shellRest, rotorRest, haloRest, bracketsRest, badgeRest, visualOffset, travelFrom;
        Quaternion turnFrom, turnTarget;
        float travelTime, travelDuration, turnTime, feedbackTime, feedbackDuration;
        bool visualInitialized, travelling, turning, highlighted;
        const float TurnDuration = .18f;
        public bool Installed => installed;
        public int Choice => choice;
        public bool IsManipulating => manipulating;
        public Vector3 HomePosition => homePosition;
        public Vector3 SocketPosition => socketPosition;
        public override Plane ManipulationPlane => dragPlane;
        bool ReducedMotion => rig != null && rig.owner != null && rig.owner.ReducedMotion;

        public override bool BeginManipulation(Vector3 worldPosition)
        {
            if (manipulating || rig == null || !rig.Editable || !isActiveAndEnabled) return false;
            SettlePresentation();
            CacheSpatialBounds();
            manipulating = true;
            moved = false;
            grabWorld = worldPosition;
            originalLocal = transform.localPosition;
            dragPlane = new Plane(transform.parent.up, worldPosition);
            transform.localPosition = ConstrainToSpatialWindow(originalLocal + Vector3.up * .022f);
            rig.selected = index;
            rig.UpdateHighlights();
            return true;
        }

        public override void UpdateManipulation(Vector3 worldPosition)
        {
            if (!manipulating) return;
            if (!rig.Editable) { EndManipulation(true); return; }
            Vector3 delta = transform.parent.InverseTransformVector(worldPosition - grabWorld);
            // Spatial positions describe free 3D motion, not a board-plane ray hit.
            // Only the desktop adapter projects its mouse ray onto a fixed plane.
            if (delta.sqrMagnitude > .012f * .012f) moved = true;
            Vector3 next = originalLocal + delta + Vector3.up * .022f;
            next.x = Mathf.Clamp(next.x, -.54f, .54f);
            next.y = Mathf.Clamp(next.y, -.03f, .50f);
            next.z = Mathf.Clamp(next.z, -.56f, .40f);
            transform.localPosition = ConstrainToSpatialWindow(next);
            rig.UpdateHighlights();
        }

        public override void EndManipulation(bool cancelled)
        {
            if (!manipulating) return;
            manipulating = false;
            if (cancelled || !rig.Editable)
            {
                RestorePose();
                if (rig.selected == index) rig.selected = -1;
                rig.UpdateHighlights();
                return;
            }
            if (moved) rig.FinishDrag(this);
            else
            {
                RestorePose();
                if (installed) rig.Rotate(this);
                else rig.Select(index);
            }
        }

        public void SetInstalled(bool value)
        {
            bool changed = installed != value;
            installed = value;
            if (rig != null && rig.sockets != null && rig.sockets[index] != null)
                rig.sockets[index].gameObject.SetActive(!installed);
            if (index == 1 && directionRotor != null) directionRotor.gameObject.SetActive(!installed);
            MoveToPose(changed && Application.isPlaying);
            if (changed && value) PlayFeedback(false);
        }

        public void SetChoice(int value)
        {
            EnsureVisuals();
            int previous = choice;
            choice = Mathf.Clamp(value, 0, 1);
            if (directionRotor != null)
            {
                turnTarget = Quaternion.Euler(0, index == 0 ? (choice == 0 ? 0 : alternateYaw) : 0, 0);
                turnFrom = directionRotor.localRotation;
                turnTime = 0;
                turning = Application.isPlaying && !ReducedMotion && installed && index == 0 && previous != choice;
                if (!turning) directionRotor.localRotation = turnTarget;
            }
            if (previous != choice && installed) PlayFeedback(false);
        }

        public void RestorePose()
        {
            MoveToPose(false);
            SettlePresentation();
        }

        public void ReturnToSeat()
        {
            MoveToPose(Application.isPlaying);
            PlayFeedback(true);
        }

        public void Highlight(bool active)
        {
            EnsureVisuals();
            highlighted = active;
            if (selectionHalo != null) selectionHalo.gameObject.SetActive(active);
            if (selectionBrackets != null) selectionBrackets.gameObject.SetActive(active);
            if (!active)
            {
                if (selectionHalo != null) selectionHalo.localScale = Vector3.one;
                if (selectionBrackets != null) selectionBrackets.localScale = Vector3.one;
            }
        }

        /// <summary>Brief confirmation uses white; amber rejection never implies puzzle correctness.</summary>
        public void PlayFeedback(bool rejected)
        {
            EnsureVisuals();
            if (!Application.isPlaying || feedbackHalo == null || rig == null || rig.owner == null) return;
            var palette = rig.owner.palette;
            int material = rejected ? 2 : 4;
            if (feedbackRenderer != null && palette != null && palette.Length > material)
                feedbackRenderer.sharedMaterial = palette[material];
            feedbackDuration = rejected ? .38f : .30f;
            feedbackTime = 0;
            feedbackHalo.localScale = Vector3.one;
            feedbackHalo.gameObject.SetActive(true);
        }

        /// <summary>Only decorative children move. Root pose and colliders are already stable.</summary>
        public void SettlePresentation()
        {
            EnsureVisuals();
            travelling = turning = false;
            SetVisualOffset(Vector3.zero);
            if (directionRotor != null) directionRotor.localRotation = turnTarget;
            if (feedbackHalo != null)
            {
                feedbackHalo.gameObject.SetActive(false);
                feedbackHalo.localScale = Vector3.one;
            }
            feedbackTime = feedbackDuration = 0;
        }

        public void ResetToTray()
        {
            EndManipulation(true);
            SetInstalled(false);
            SetChoice(initialChoice);
            RestorePose();
            Highlight(false);
        }

        void EnsureVisuals()
        {
            if (visualInitialized)
            {
                // The scene builder adds its D/R badge after ResetToTray. Refresh that
                // late reference as well when Enter Play Mode skips the domain reload.
                if (badge == null)
                {
                    badge = transform.Find(index == 0 ? "D" : "R");
                    if (badge != null) badgeRest = badge.localPosition;
                }
                return;
            }
            shell = transform.Find("BrickShell");
            if (directionRotor == null) directionRotor = transform.Find("DirectionRotor");
            if (selectionHalo == null) selectionHalo = transform.Find("SelectionHalo");
            selectionBrackets = transform.Find("SelectionBrackets");
            feedbackHalo = transform.Find("FeedbackHalo");
            badge = transform.Find(index == 0 ? "D" : "R");
            if (feedbackHalo != null) feedbackRenderer = feedbackHalo.GetComponent<Renderer>();
            if (shell != null) shellRest = shell.localPosition;
            if (selectionHalo != null) haloRest = selectionHalo.localPosition;
            if (selectionBrackets != null) bracketsRest = selectionBrackets.localPosition;
            if (badge != null) badgeRest = badge.localPosition;
            if (directionRotor != null)
            {
                rotorRest = directionRotor.localPosition;
                turnTarget = directionRotor.localRotation;
            }
            visualInitialized = true;
        }

        void MoveToPose(bool animate)
        {
            EnsureVisuals();
            Vector3 previousVisualWorld = transform.TransformPoint(visualOffset);
            transform.localPosition = installed ? socketPosition : homePosition;
            travelFrom = transform.InverseTransformPoint(previousVisualWorld);
            travelTime = 0;
            travelDuration = Mathf.Lerp(.14f, .24f, Mathf.Clamp01(travelFrom.magnitude / .45f));
            travelling = animate && !ReducedMotion && travelFrom.sqrMagnitude > .000001f;
            SetVisualOffset(travelling ? travelFrom : Vector3.zero);
        }

        void SetVisualOffset(Vector3 offset)
        {
            visualOffset = offset;
            if (shell != null) shell.localPosition = shellRest + offset;
            if (directionRotor != null) directionRotor.localPosition = rotorRest + offset;
            if (selectionHalo != null) selectionHalo.localPosition = haloRest + offset;
            if (selectionBrackets != null) selectionBrackets.localPosition = bracketsRest + offset;
            if (badge != null) badge.localPosition = badgeRest + offset;
        }

        void LateUpdate()
        {
            if (rig == null || !rig.Editable)
            {
                SettlePresentation();
                Highlight(false);
                return;
            }
            if (ReducedMotion)
            {
                travelling = turning = false;
                SetVisualOffset(Vector3.zero);
                if (directionRotor != null) directionRotor.localRotation = turnTarget;
            }
            float delta = Mathf.Min(Time.unscaledDeltaTime, .05f);
            if (travelling)
            {
                travelTime += delta;
                float t = Mathf.Clamp01(travelTime / travelDuration);
                float eased = 1f - Mathf.Pow(1f - t, 3f);
                SetVisualOffset(travelFrom * (1f - eased) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * .008f));
                if (t >= 1f) { travelling = false; SetVisualOffset(Vector3.zero); }
            }
            if (turning && directionRotor != null)
            {
                turnTime += delta;
                float t = Mathf.Clamp01(turnTime / TurnDuration);
                directionRotor.localRotation = Quaternion.Slerp(turnFrom, turnTarget, t * t * (3f - 2f * t));
                if (t >= 1f) turning = false;
            }
            if (highlighted)
            {
                float scale = ReducedMotion ? 1f : 1f + Mathf.Sin(Time.unscaledTime * 4f) * .018f;
                if (selectionHalo != null) selectionHalo.localScale = Vector3.one * scale;
                if (selectionBrackets != null) selectionBrackets.localScale = Vector3.one * (manipulating ? 1.035f : 1f);
            }
            if (feedbackHalo != null && feedbackHalo.gameObject.activeSelf)
            {
                feedbackTime += delta;
                float t = Mathf.Clamp01(feedbackTime / feedbackDuration);
                feedbackHalo.localScale = ReducedMotion ? Vector3.one : Vector3.one * Mathf.Lerp(.92f, 1.16f, t);
                if (t >= 1f) feedbackHalo.gameObject.SetActive(false);
            }
        }

        void OnDisable()
        {
            EndManipulation(true);
            SettlePresentation();
            Highlight(false);
        }
    }
}
