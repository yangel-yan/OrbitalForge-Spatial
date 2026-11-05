using System.Collections.Generic;
using ByteDance.PICO.SpatialAdapter;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace SpaceBall
{
    /// <summary>Routes system selections and captured piece drags to the assembly.</summary>
    [DisallowMultipleComponent]
    public sealed class OrbitalSpatialInput : MonoBehaviour
    {
        [Tooltip("Optional camera for mouse testing in the Editor or a desktop player.")]
        public Camera fallbackCamera;

        public int SpatialBeganCount { get; private set; }
        public int SpatialMovedCount { get; private set; }
        public int SpatialEndedCount { get; private set; }
        public int SpatialActionMovedCount { get; private set; }
        public int SpatialRecoveredMoveCount { get; private set; }
        public Vector3 LastSpatialPosition { get; private set; }
        public string LastSpatialTarget { get; private set; } = "";

        readonly HashSet<int> m_ActiveInteractions = new();
        readonly HashSet<int> m_SeenInteractions = new();
        readonly List<int> m_ExpiredInteractions = new();
        readonly Dictionary<int, OrbitalManipulable> m_CapturedPieces = new();
        readonly BufferedMove[] m_BufferedMoves = new BufferedMove[2];
        readonly Vector3[] m_CaptureAnchors = new Vector3[2];
        readonly Vector3[] m_CapturePositions = new Vector3[2];
        readonly double[] m_CaptureStartTimes = new double[2];
        readonly GameObject[] m_InteractionTargets = new GameObject[2];
        readonly SpatialInputState[] m_LastObservedStates = new SpatialInputState[2];
        readonly TouchPhase[] m_LastObservedPhases = new TouchPhase[2];
        readonly int[] m_LastObservedScreenIds = new int[2];
        readonly string[] m_LastObservedScreenNames = new string[2];
        InputAction m_PrimarySpatialAction, m_SecondarySpatialAction;

        struct BufferedMove
        {
            public bool valid;
            public Vector3 anchor, position;
            public double time;
            public int frame;
        }

#if UNITY_EDITOR || UNITY_STANDALONE
        OrbitalManipulable m_MousePiece;
        Plane m_MousePlane;
        bool m_MouseWaitForNeutral = true;
#endif

        bool m_EnhancedTouchEnabled;
        bool m_HasFocus;
        bool m_ApplicationPaused;
        bool m_ExternallySuspended;
        bool m_WaitForNeutral = true;
        int m_LastSpatialDispatchFrame = -1;
        GameObject m_LastSpatialDiagnosticTarget;

        void OnEnable()
        {
            EnhancedTouchSupport.Enable();
            m_EnhancedTouchEnabled = true;
            m_HasFocus = Application.isEditor || Application.isFocused;
            ResetInteractions("enable");
            m_PrimarySpatialAction = CreateSpatialAction("SpaceBall Primary", "<SpatialInputDevice>/primary");
            m_SecondarySpatialAction = CreateSpatialAction("SpaceBall Secondary", "<SpatialInputDevice>/secondary");
        }

        void OnDisable()
        {
            ResetInteractions("disable");
            DisposeSpatialAction(ref m_PrimarySpatialAction);
            DisposeSpatialAction(ref m_SecondarySpatialAction);
            if (m_EnhancedTouchEnabled)
            {
                EnhancedTouchSupport.Disable();
                m_EnhancedTouchEnabled = false;
            }
        }

        InputAction CreateSpatialAction(string actionName, string binding)
        {
            var action = new InputAction(actionName, InputActionType.PassThrough, binding);
            action.performed += OnSpatialActionPerformed;
            action.canceled += OnSpatialActionCancelled;
            action.Enable();
            return action;
        }

        void DisposeSpatialAction(ref InputAction action)
        {
            if (action == null) return;
            action.performed -= OnSpatialActionPerformed;
            action.canceled -= OnSpatialActionCancelled;
            action.Disable();
            action.Dispose();
            action = null;
        }

        void OnSpatialActionPerformed(InputAction.CallbackContext context)
        {
            var inputState = context.ReadValue<SpatialInputState>();
            int id = inputState.interactionId;
            if (id < 0 || id > 1 || inputState.phase != TouchPhase.Moved || inputState.targetObject == null ||
                !m_HasFocus || m_ApplicationPaused || m_ExternallySuspended || m_WaitForNeutral)
                return;

            var previous = m_BufferedMoves[id];
            if (previous.valid && previous.frame == Time.frameCount &&
                previous.anchor.Equals(inputState.startPosition) && previous.position.Equals(inputState.currentPosition))
                return;

            // Capture 3D data during performed, before activeTouches can fold its phase.
            m_BufferedMoves[id] = new BufferedMove
            {
                valid = true, anchor = inputState.startPosition, position = inputState.currentPosition,
                time = context.time, frame = Time.frameCount
            };
            SpatialActionMovedCount++;
            RecordSpatialPosition(inputState);
        }

        void OnSpatialActionCancelled(InputAction.CallbackContext context)
        {
            // PassThrough normal releases arrive as performed states with phase Ended.
            // canceled here means an action/device was reset or disabled externally.
            ResetInteractions("action-cancelled");
        }

        /// <summary>Allows the application's own pause flow to block controls too.</summary>
        public void SetSuspended(bool suspended)
        {
            if (m_ExternallySuspended == suspended)
                return;

            m_ExternallySuspended = suspended;
            LogLifecycle("suspended", suspended);
            ResetInteractions("suspend-change");
        }

        void OnApplicationFocus(bool hasFocus)
        {
            bool changed = m_HasFocus != hasFocus;
#if UNITY_EDITOR
            // Native preview input belongs to the PICO window. Focusing the
            // Emulator must not suspend that path in the unfocused Editor.
            m_HasFocus = true;
            if (!hasFocus)
            {
                EndMouseCapture(true);
                m_MouseWaitForNeutral = true;
            }
#else
            m_HasFocus = hasFocus;
            if (changed) LogLifecycle("focus", hasFocus);
            ResetInteractions("focus-callback");
#endif
        }

        void OnApplicationPause(bool paused)
        {
            bool changed = m_ApplicationPaused != paused;
            m_ApplicationPaused = paused;
            if (changed) LogLifecycle("pause", paused);
            ResetInteractions("pause-callback");
        }

        void Update()
        {
            if (!m_HasFocus || m_ApplicationPaused || m_ExternallySuspended)
                return;

            // A held pinch or the mouse press that restored focus must not fire a control.
            if (m_WaitForNeutral)
            {
                if (!InputsAreNeutral())
                    return;

                m_WaitForNeutral = false;
            }

            PollSpatialSelections();

#if UNITY_EDITOR || UNITY_STANDALONE
            PollMouse();
#endif
        }

        void PollSpatialSelections()
        {
            // GetInputState indexes the SDK's two-slot buffer directly. Ignore ordinary
            // desktop touch devices when the Spatial Adapter has not created its device.
            if (InputSystem.GetDevice<SpatialInputDevice>() == null)
            {
                CancelSpatialCaptures("spatial-device-missing");
                m_ActiveInteractions.Clear();
                return;
            }

            m_SeenInteractions.Clear();
            foreach (var touch in Touch.activeTouches)
            {
                if (touch.touchId < 1 || touch.touchId > 2)
                    continue;

                // EnhancedTouch preserves a short press as Began even when the SDK's
                // latest spatial state is already Ended. Use its lifecycle consistently.
                int interactionId = touch.touchId - 1;
                var phase = touch.phase;
                var inputState = SpatialInputSupport.GetInputState(touch);
                m_LastObservedStates[interactionId] = inputState;
                m_LastObservedPhases[interactionId] = phase;
                var screen = touch.screen;
                if (screen != null && m_LastObservedScreenIds[interactionId] != screen.deviceId)
                {
                    m_LastObservedScreenIds[interactionId] = screen.deviceId;
                    m_LastObservedScreenNames[interactionId] = screen.name;
                }
                RecordSpatialDiagnostics(interactionId, phase, inputState);

                if (phase == TouchPhase.Ended ||
                    phase == TouchPhase.Canceled ||
                    phase == TouchPhase.None)
                {
                    if (phase == TouchPhase.Ended)
                    {
                        ApplyBufferedMove(interactionId);
                        // A whole short drag may surface only Began then Ended. The SDK
                        // retains its last valid position on Ended; it does not zero it.
                        if (HasMatchingState(interactionId, inputState) && inputState.phase == TouchPhase.Ended)
                            UpdateCapturedPiece(interactionId, inputState.startPosition, inputState.currentPosition, true);
                    }
                    EndSpatialInteraction(interactionId, phase != TouchPhase.Ended,
                        phase == TouchPhase.Ended ? "touch-ended" : phase == TouchPhase.Canceled ? "touch-cancelled" : "touch-none");
                    continue;
                }

                m_SeenInteractions.Add(interactionId);
                if (phase == TouchPhase.Moved)
                {
                    ApplyBufferedMove(interactionId);
                    if (m_CapturedPieces.TryGetValue(interactionId, out var captured))
                    {
                        if (captured == null || !captured.isActiveAndEnabled)
                            EndSpatialInteraction(interactionId, true, "piece-inactive");
                        else
                        {
                            // Touch history may outlive the SDK's latest slot state.
                            // A cleared slot has no usable position, including at the origin.
                            if (HasMatchingState(interactionId, inputState))
                                UpdateCapturedPiece(interactionId, inputState.startPosition, inputState.currentPosition, false);
                        }
                    }

                    continue;
                }

                if (phase != TouchPhase.Began || !m_ActiveInteractions.Add(interactionId))
                    continue;

                var targetObject = inputState.targetObject;
                m_InteractionTargets[interactionId] = targetObject;
                if (targetObject == null)
                {
                    LogSpatialGesture("begin", interactionId, false, false);
                    continue;
                }

                var piece = targetObject.GetComponentInParent<OrbitalManipulable>();
                if (piece != null)
                {
                    m_LastSpatialDispatchFrame = Time.frameCount;
                    if (piece.isActiveAndEnabled && !PieceAlreadyCaptured(piece) &&
                        HasMatchingState(interactionId, inputState) && piece.BeginManipulation(inputState.startPosition))
                    {
                        m_CapturedPieces[interactionId] = piece;
                        m_CaptureAnchors[interactionId] = inputState.startPosition;
                        m_CapturePositions[interactionId] = inputState.startPosition;
                        m_CaptureStartTimes[interactionId] = touch.startTime;
                    }
                    LogSpatialGesture("begin", interactionId, m_CapturedPieces.ContainsKey(interactionId), false);
                    ApplyBufferedMove(interactionId);

                    // The piece owns both its tap and drag behavior.
                    continue;
                }

                var target = targetObject.GetComponentInParent<OrbitalClickTarget>();
                LogSpatialGesture("begin", interactionId, false, false);
                if (target == null)
                    continue;

                m_LastSpatialDispatchFrame = Time.frameCount;
                target.Activate();
            }

            // A valid custom-device Moved can coexist with Touch.Stationary.
            for (int id = 0; id < 2; id++)
                ApplyBufferedMove(id);

            // End/cancel may disappear while a window is being hidden.
            m_ExpiredInteractions.Clear();
            foreach (int interactionId in m_ActiveInteractions)
            {
                if (!m_SeenInteractions.Contains(interactionId))
                    m_ExpiredInteractions.Add(interactionId);
            }

            while (m_ExpiredInteractions.Count > 0)
            {
                int last = m_ExpiredInteractions.Count - 1;
                int interactionId = m_ExpiredInteractions[last];
                m_ExpiredInteractions.RemoveAt(last);
                EndSpatialInteraction(interactionId, true, "touch-disappeared");
            }
        }

        static bool HasMatchingState(int id, SpatialInputState inputState)
        {
            return inputState.interactionId == id && inputState.phase != TouchPhase.None && inputState.targetObject != null;
        }

        void ApplyBufferedMove(int id)
        {
            var sample = m_BufferedMoves[id];
            if (!sample.valid || !m_CapturedPieces.ContainsKey(id)) return;
            m_BufferedMoves[id] = default;
            if (Time.frameCount - sample.frame > 2 || sample.time + .000001 < m_CaptureStartTimes[id]) return;
            UpdateCapturedPiece(id, sample.anchor, sample.position, true);
        }

        void UpdateCapturedPiece(int id, Vector3 anchor, Vector3 position, bool recovered)
        {
            if (!m_CapturedPieces.TryGetValue(id, out var piece)) return;
            if (piece == null || !piece.isActiveAndEnabled)
            {
                EndSpatialInteraction(id, true, "piece-inactive");
                return;
            }
            // The slots are reused. An old gesture's movement must not move a new capture.
            if (!anchor.Equals(m_CaptureAnchors[id]) || position.Equals(m_CapturePositions[id])) return;
            m_CapturePositions[id] = position;
            if (recovered) SpatialRecoveredMoveCount++;
            piece.UpdateManipulation(position);
        }

        void RecordSpatialDiagnostics(int interactionId, TouchPhase phase, SpatialInputState inputState)
        {
            if (inputState.interactionId != interactionId || inputState.phase == TouchPhase.None)
                return;

            switch (phase)
            {
                case TouchPhase.Began: SpatialBeganCount++; break;
                case TouchPhase.Moved: SpatialMovedCount++; break;
                case TouchPhase.Ended: SpatialEndedCount++; break;
                default: return;
            }

            RecordSpatialPosition(inputState);
        }

        void RecordSpatialPosition(SpatialInputState inputState)
        {
            LastSpatialPosition = inputState.currentPosition;
            var targetObject = inputState.targetObject;
            if (targetObject != m_LastSpatialDiagnosticTarget)
            {
                m_LastSpatialDiagnosticTarget = targetObject;
                LastSpatialTarget = targetObject != null ? targetObject.name : "";
            }
        }

        bool PieceAlreadyCaptured(OrbitalManipulable piece)
        {
#if UNITY_EDITOR || UNITY_STANDALONE
            if (m_MousePiece == piece)
                return true;
#endif
            return m_CapturedPieces.ContainsValue(piece);
        }

        void EndSpatialInteraction(int interactionId, bool cancelled, string reason)
        {
            bool active = m_ActiveInteractions.Remove(interactionId);
            bool captured = m_CapturedPieces.TryGetValue(interactionId, out var piece);
            m_CapturedPieces.Remove(interactionId);
            m_BufferedMoves[interactionId] = default;
            cancelled |= piece != null && !piece.isActiveAndEnabled;
            if (active || captured) LogSpatialGesture("end", interactionId, captured, cancelled, reason);
            m_InteractionTargets[interactionId] = null;
            if (piece != null)
                piece.EndManipulation(cancelled);
        }

        void CancelSpatialCaptures(string reason)
        {
            // End clears ownership before callbacks, which can rebuild the assembly.
            for (int id = 0; id < 2; id++) EndSpatialInteraction(id, true, reason);
        }

        void LogSpatialGesture(string action, int id, bool captured, bool cancelled, string reason = "target")
        {
            if (!Debug.isDebugBuild || Application.isEditor) return;
            var state = m_LastObservedStates[id];
            var target = m_InteractionTargets[id];
            Debug.Log($"[OrbitalInput] {action} id={id} target={(target != null ? target.name : "<none>")} " +
                $"touch={m_LastObservedPhases[id]} sdk={state.phase} capture={captured} cancelled={cancelled} reason={reason} " +
                $"screen={m_LastObservedScreenIds[id]}:{m_LastObservedScreenNames[id]} " +
                $"start={state.startPosition:F3} position={state.currentPosition:F3}");
        }

        void LogLifecycle(string action, bool value)
        {
            if (!Debug.isDebugBuild || Application.isEditor) return;
            Debug.Log($"[OrbitalInput] {action} value={value} focus={m_HasFocus} paused={m_ApplicationPaused} " +
                $"suspended={m_ExternallySuspended} neutral={m_WaitForNeutral} captures={m_CapturedPieces.Count}");
        }

        static bool InputsAreNeutral()
        {
            foreach (var touch in Touch.activeTouches)
            {
                if (touch.phase == TouchPhase.Began || touch.phase == TouchPhase.Moved ||
                    touch.phase == TouchPhase.Stationary)
                    return false;
            }

#if UNITY_EDITOR || UNITY_STANDALONE
            if (Application.isFocused && Mouse.current != null && Mouse.current.leftButton.isPressed)
                return false;
#endif

            return true;
        }

#if UNITY_EDITOR || UNITY_STANDALONE
        void PollMouse()
        {
            if (!Application.isFocused)
            {
                EndMouseCapture(true);
                m_MouseWaitForNeutral = true;
                return;
            }

            var mouse = Mouse.current;
            if (mouse == null)
            {
                EndMouseCapture(true);
                m_MouseWaitForNeutral = true;
                return;
            }

            if (m_MouseWaitForNeutral)
            {
                if (mouse.leftButton.isPressed)
                    return;

                m_MouseWaitForNeutral = false;
            }

            var camera = fallbackCamera != null ? fallbackCamera : Camera.main;
            if (m_MousePiece != null)
            {
                if (!m_MousePiece.isActiveAndEnabled || camera == null)
                    EndMouseCapture(true);
                else if (mouse.leftButton.wasReleasedThisFrame || !mouse.leftButton.isPressed)
                    EndMouseCapture(false);
                else
                {
                    var dragRay = camera.ScreenPointToRay(mouse.position.ReadValue());
                    if (m_MousePlane.Raycast(dragRay, out float distance))
                        m_MousePiece.UpdateManipulation(dragRay.GetPoint(distance));
                }

                return;
            }

            if (!mouse.leftButton.wasPressedThisFrame || m_LastSpatialDispatchFrame == Time.frameCount)
                return;

            if (camera == null || !camera.pixelRect.Contains(mouse.position.ReadValue()))
                return;

            var ray = camera.ScreenPointToRay(mouse.position.ReadValue());
            var hits = Physics.RaycastAll(ray, Mathf.Infinity, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Collide);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var hit in hits)
            {
                var piece = hit.collider.GetComponentInParent<OrbitalManipulable>();
                if (piece != null)
                {
                    if (!piece.isActiveAndEnabled)
                        continue;

                    if (!PieceAlreadyCaptured(piece) && piece.BeginManipulation(hit.point))
                    {
                        m_MousePiece = piece;
                        m_MousePlane = piece.ManipulationPlane;
                    }

                    return;
                }

                var target = hit.collider.GetComponentInParent<OrbitalClickTarget>();
                if (target == null || !target.isActiveAndEnabled || !target.interactable)
                    continue;

                target.Activate();
                return;
            }
        }

        void EndMouseCapture(bool cancelled)
        {
            var piece = m_MousePiece;
            m_MousePiece = null;
            if (piece != null)
                piece.EndManipulation(cancelled || !piece.isActiveAndEnabled);
        }
#endif

        void ResetInteractions(string reason)
        {
            if (Debug.isDebugBuild && !Application.isEditor &&
                (m_ActiveInteractions.Count > 0 || m_CapturedPieces.Count > 0 || !m_WaitForNeutral ||
                 reason == "enable" || reason == "disable"))
                Debug.Log($"[OrbitalInput] reset reason={reason} focus={m_HasFocus} paused={m_ApplicationPaused} " +
                    $"suspended={m_ExternallySuspended} active={m_ActiveInteractions.Count} captures={m_CapturedPieces.Count}");
            CancelSpatialCaptures(reason);
#if UNITY_EDITOR || UNITY_STANDALONE
            EndMouseCapture(true);
            m_MouseWaitForNeutral = true;
#endif
            m_ActiveInteractions.Clear();
            m_SeenInteractions.Clear();
            m_ExpiredInteractions.Clear();
            System.Array.Clear(m_BufferedMoves, 0, m_BufferedMoves.Length);
            m_LastSpatialDispatchFrame = -1;
            m_WaitForNeutral = true;
        }
    }
}
