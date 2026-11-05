using UnityEngine;

namespace SpaceBall
{
    public enum OrbitalTileShape { Straight, CornerLeft, CornerRight, Drop }

    /// <summary>A movable piece of real track, with four physical orientations.</summary>
    public sealed class OrbitalConstructionTile : OrbitalManipulable
    {
        public OrbitalConstructionKit kit;
        public int index;
        public OrbitalTileShape shape;
        public Vector3 homePosition;
        public Vector3 inputPort, outputPort;
        public Transform selectionHalo;
        public Transform labelRoot;
        public Collider[] trackColliders = new Collider[0];
        public Renderer inputIndicator, outputIndicator;
        public Material connectedMaterial, disconnectedMaterial;
        public Material inputIdleMaterial, outputIdleMaterial;
        [SerializeField] int socketIndex = -1, quarterTurns;
        bool manipulating, moved, highlighted;
        Vector3 grabWorld, originalLocal;
        Plane dragPlane;
        Transform cachedHalo;
        Vector3 haloScale = Vector3.one;
        float feedbackTime;
        const float Lift = .022f;
        public bool Installed => socketIndex >= 0;
        public bool IsManipulating => manipulating;
        public int SocketIndex => socketIndex;
        public int QuarterTurns => quarterTurns;
        public bool InputConnected { get; private set; }
        public bool OutputConnected { get; private set; }
        public Vector3 WorldIn => transform.TransformPoint(inputPort);
        public Vector3 WorldOut => transform.TransformPoint(outputPort);
        public override Plane ManipulationPlane => dragPlane;
        public string DisplayName => shape == OrbitalTileShape.CornerLeft ? "左弯轨" :
            shape == OrbitalTileShape.CornerRight ? "右弯轨" : shape == OrbitalTileShape.Drop ? "降层轨" : "直轨";

        public override bool BeginManipulation(Vector3 worldPosition)
        {
            if (manipulating || kit == null || !kit.Editable || !isActiveAndEnabled) return false;
            // Reset a completed/failed flight before capture owns this piece.
            kit.PrepareEdit();
            SettlePresentation();
            CacheSpatialBounds();
            manipulating = true;
            moved = false;
            grabWorld = worldPosition;
            originalLocal = transform.localPosition;
            dragPlane = new Plane(transform.parent != null ? transform.parent.up : Vector3.up, worldPosition);
            transform.localPosition = ConstrainToSpatialWindow(originalLocal + Vector3.up * Lift);
            ApplyTrackColliders();
            kit.RefreshConnections();
            kit.UpdateHighlights();
            return true;
        }

        public override void UpdateManipulation(Vector3 worldPosition)
        {
            if (!manipulating) return;
            if (kit == null || !kit.Editable) { EndManipulation(true); return; }
            Vector3 delta = transform.parent != null ? transform.parent.InverseTransformVector(worldPosition - grabWorld) : worldPosition - grabWorld;
            if (delta.sqrMagnitude > .012f * .012f) moved = true;
            Vector3 position = originalLocal + delta + Vector3.up * Lift;
            position.x = Mathf.Clamp(position.x, -.54f, .54f);
            position.y = Mathf.Clamp(position.y, -.03f, .48f);
            position.z = Mathf.Clamp(position.z, -.53f, .40f);
            transform.localPosition = ConstrainToSpatialWindow(position);
            kit.UpdateHighlights();
        }

        public override void EndManipulation(bool cancelled)
        {
            if (!manipulating) return;
            manipulating = false;
            if (cancelled || kit == null || !kit.Editable)
            {
                RestorePose();
                if (kit != null)
                {
                    if (kit.selected == index) kit.selected = -1;
                    kit.RefreshConnections();
                    kit.UpdateHighlights();
                }
                return;
            }
            if (moved) kit.FinishDrag(this);
            else
            {
                RestorePose();
                kit.Tap(this);
            }
        }

        public void SetPlacement(int socket, int turns)
        {
            if (Application.isPlaying && (kit == null || !kit.Editable || manipulating)) return;
            if (socket >= 0 && (kit == null || kit.sockets == null || socket >= kit.sockets.Length || kit.sockets[socket] == null)) return;
            if (socket >= 0 && kit.IsOccupied(socket, index)) return;
            socketIndex = socket < 0 ? -1 : socket;
            quarterTurns = ((turns % 4) + 4) % 4;
            RestorePose();
        }

        public void ResetToTray()
        {
            if (Application.isPlaying && (kit == null || !kit.Editable)) return;
            EndManipulation(true);
            socketIndex = -1;
            quarterTurns = 0;
            RestorePose();
            SetConnections(false, false);
            Highlight(false);
        }

        public void RestorePose()
        {
            bool valid = Installed && kit != null && kit.sockets != null && socketIndex < kit.sockets.Length && kit.sockets[socketIndex] != null;
            if (!valid) socketIndex = -1;
            if (valid)
            {
                Vector3 world = kit.sockets[socketIndex].transform.position;
                transform.localPosition = transform.parent != null ? transform.parent.InverseTransformPoint(world) : world;
            }
            else transform.localPosition = homePosition;
            transform.localRotation = Quaternion.Euler(0, quarterTurns * 90f, 0);
            if (labelRoot != null) labelRoot.localRotation = Quaternion.Inverse(transform.localRotation) * Quaternion.Euler(22f, 0, 0);
            ApplyTrackColliders();
            SettlePresentation();
            Physics.SyncTransforms();
        }

        void ApplyTrackColliders()
        {
            bool enabled = Installed && !manipulating;
            foreach (var collider in trackColliders)
                if (collider != null && collider.enabled != enabled) collider.enabled = enabled;
        }

        public Vector3 WorldPortDirection(bool input)
        {
            Vector3 port = input ? inputPort : outputPort;
            port.y = 0;
            return transform.TransformDirection(port.normalized);
        }

        public void SetConnections(bool input, bool output)
        {
            InputConnected = input;
            OutputConnected = output;
            SetIndicator(inputIndicator, input, inputIdleMaterial);
            SetIndicator(outputIndicator, output, outputIdleMaterial);
            UpdatePortScale();
        }

        void SetIndicator(Renderer renderer, bool connected, Material idleMaterial)
        {
            if (renderer == null) return;
            Material material = connected ? connectedMaterial : idleMaterial != null ? idleMaterial : disconnectedMaterial;
            if (material == null && kit != null && kit.owner != null && kit.owner.palette != null)
            {
                int paletteIndex = connected ? 10 : 2;
                if (kit.owner.palette.Length > paletteIndex) material = kit.owner.palette[paletteIndex];
            }
            if (material != null && renderer.sharedMaterial != material) renderer.sharedMaterial = material;
        }

        public void Highlight(bool value)
        {
            CacheHalo();
            highlighted = value;
            UpdatePortScale();
            if (labelRoot != null && labelRoot.gameObject.activeSelf != (!Installed || value))
                labelRoot.gameObject.SetActive(!Installed || value);
            if (selectionHalo != null && selectionHalo.gameObject.activeSelf != value) selectionHalo.gameObject.SetActive(value);
            if (!value && selectionHalo != null && selectionHalo.localScale != haloScale) selectionHalo.localScale = haloScale;
        }

        void UpdatePortScale()
        {
            // The selected module explains its two ports. Seated connections
            // become small seam indicators so they do not compete with goals.
            if (inputIndicator != null)
                inputIndicator.transform.localScale = Vector3.one * (!Installed || highlighted ? 1f : InputConnected ? .55f : .8f);
            if (outputIndicator != null)
                outputIndicator.transform.localScale = Vector3.one * (!Installed || highlighted ? 1f : OutputConnected ? .55f : .8f);
        }

        public void PlayFeedback(bool rejected)
        {
            // Keep root pose and collider geometry exact. Only the decorative
            // selection frame briefly expands; audio reports the actual action.
            feedbackTime = rejected ? .30f : .20f;
        }

        public void SettlePresentation()
        {
            CacheHalo();
            feedbackTime = 0;
            if (selectionHalo != null && selectionHalo.localScale != haloScale) selectionHalo.localScale = haloScale;
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
                if (highlighted || feedbackTime > 0 || selectionHalo != null && selectionHalo.gameObject.activeSelf)
                { SettlePresentation(); Highlight(false); }
                return;
            }
            bool reduced = kit.owner.ReducedMotion;
            if (feedbackTime > 0) feedbackTime = Mathf.Max(0, feedbackTime - Time.unscaledDeltaTime);
            if (selectionHalo != null && highlighted)
            {
                Vector3 scale = haloScale * (reduced ? 1 : feedbackTime > 0 ? 1.07f : 1 + Mathf.Sin(Time.unscaledTime * 4) * .014f);
                if (selectionHalo.localScale != scale) selectionHalo.localScale = scale;
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
