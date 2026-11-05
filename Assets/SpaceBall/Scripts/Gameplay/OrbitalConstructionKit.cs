using UnityEngine;

namespace SpaceBall
{
    /// <summary>
    /// A shared set of sockets accepts any track tile. Only the assembled physical
    /// track and the ball's sensors determine a flight's result.
    /// </summary>
    public sealed class OrbitalConstructionKit : MonoBehaviour
    {
        public OrbitalPrototype owner;
        public OrbitalConstructionTile[] tiles = new OrbitalConstructionTile[0];
        public OrbitalConstructionSocket[] sockets = new OrbitalConstructionSocket[0];
        public OrbitalClickTarget trayTarget;
        [Tooltip("Board-local endpoints of the fixed track, used only for connection collars.")]
        public Vector3[] fixedPorts = new Vector3[0];
        public int selected = -1;
        const float SnapDistance = .075f;
        const float PortDistance = .019f;
        OrbitalClickTarget boundTrayTarget;

        public bool Editable => owner != null && owner.AssemblyEditable && owner.Construction == this;
        public bool HasHeld
        {
            get { foreach (var tile in tiles) if (tile != null && tile.IsManipulating) return true; return false; }
        }
        public int PlacedCount
        {
            get { int count = 0; foreach (var tile in tiles) if (tile != null && tile.Installed) count++; return count; }
        }
        public int OpenEndCount
        {
            get
            {
                int count = 0;
                foreach (var tile in tiles)
                    if (tile != null && tile.Installed && !tile.IsManipulating)
                    { if (!tile.InputConnected) count++; if (!tile.OutputConnected) count++; }
                return count;
            }
        }
        public string Instruction
        {
            get
            {
                if (HasHeld) return "未接合青入金出，绿环为接合；移到空接口上方松开。";
                if (ValidTile(selected)) return tiles[selected].DisplayName + "已选中 · 点空接口安装，再点选中轨段旋转";
                if (PlacedCount == 0) return "点轨段再点空接口；未接合青入金出，绿环为接合。";
                return "未接合青入金出，绿环为接合；再点选中轨段旋转。";
            }
        }

        public void Clear()
        {
            if (!Editable) return;
            CancelManipulations();
            selected = -1;
            foreach (var tile in tiles) if (tile != null) tile.ResetToTray();
            foreach (var socket in sockets) if (socket != null) socket.ResetFeedback();
            RefreshConnections();
            UpdateHighlights();
            Notify("空接口可接任意轨段。观察端口方向和高度，拼出经过目标的路线。");
        }

        public void CancelManipulations()
        {
            foreach (var tile in tiles) if (tile != null) tile.EndManipulation(true);
        }

        public void SettlePresentation()
        {
            foreach (var tile in tiles) if (tile != null) tile.SettlePresentation();
            foreach (var socket in sockets) if (socket != null) socket.SettlePresentation();
        }

        internal void PrepareEdit()
        {
            if (Editable) owner.PrepareAssemblyEdit();
        }

        public void Select(int index)
        {
            if (!Editable || !ValidTile(index) || tiles[index].IsManipulating) return;
            PrepareEdit();
            selected = index;
            UpdateHighlights();
            Notify(tiles[index].DisplayName + "已选中。点空接口放置，再点这件零件旋转；点托盘可归还。");
        }

        public void Tap(OrbitalConstructionTile tile)
        {
            if (!Editable || !Owns(tile) || tile.IsManipulating) return;
            if (selected == tile.index) Rotate(tile);
            else Select(tile.index);
        }

        public bool PlaceSelected(int socket)
        {
            if (!Editable || !ValidSocket(socket)) return false;
            if (!ValidTile(selected))
            {
                sockets[socket].Reject();
                Notify("先点选一件轨段，再点空接口。", OrbitalFeedbackCue.Reject);
                return false;
            }
            return Place(selected, socket, tiles[selected].QuarterTurns);
        }

        public bool Place(int tileIndex, int socketIndex, int quarterTurns)
        {
            if (!Editable || !ValidTile(tileIndex) || !ValidSocket(socketIndex)) return false;
            var tile = tiles[tileIndex];
            if (tile.IsManipulating) return false;
            if (IsOccupied(socketIndex, tileIndex))
            {
                sockets[socketIndex].Reject();
                tile.PlayFeedback(true);
                Notify("这个接口已有轨段。先移走它，或把选中的轨段放到空接口。", OrbitalFeedbackCue.Reject);
                return false;
            }
            PrepareEdit();
            tile.SetPlacement(socketIndex, quarterTurns);
            selected = tileIndex;
            RefreshConnections();
            UpdateHighlights();
            Physics.SyncTransforms();
            tile.PlayFeedback(false);
            Notify("轨段已落位。绿环仅表示端口接合；仍需检查方向、高度和目标顺序。", OrbitalFeedbackCue.Install);
            return true;
        }

        public bool IsOccupied(int socketIndex, int exceptTile = -1)
        {
            if (!ValidSocket(socketIndex)) return false;
            foreach (var tile in tiles)
                if (tile != null && tile.index != exceptTile && tile.SocketIndex == socketIndex) return true;
            return false;
        }

        public void Rotate(OrbitalConstructionTile tile)
        {
            if (!Editable || !Owns(tile) || tile.IsManipulating) return;
            PrepareEdit();
            tile.SetPlacement(tile.SocketIndex, tile.QuarterTurns + 1);
            selected = tile.index;
            RefreshConnections();
            UpdateHighlights();
            Physics.SyncTransforms();
            tile.PlayFeedback(false);
            Notify("轨段已旋转四分之一圈。比较两端方向，注意高低差。", OrbitalFeedbackCue.Rotate);
        }

        public void ReturnSelectedToTray()
        {
            if (!Editable || !ValidTile(selected)) return;
            var tile = tiles[selected];
            if (tile.IsManipulating) return;
            PrepareEdit();
            tile.SetPlacement(-1, tile.QuarterTurns);
            selected = -1;
            RefreshConnections();
            UpdateHighlights();
            Physics.SyncTransforms();
            Notify("轨段已归还托盘，方向保留。可选择另一件继续拼装。", OrbitalFeedbackCue.Install);
        }

        internal void FinishDrag(OrbitalConstructionTile tile)
        {
            if (!Owns(tile)) return;
            if (!Editable) { tile.RestorePose(); return; }
            Vector3 position = tile.transform.position;
            int nearest = -1;
            float distance = SnapDistance;
            for (int i = 0; i < sockets.Length; i++)
            {
                if (!ValidSocket(i)) continue;
                float candidate = Vector3.Distance(position, sockets[i].transform.position);
                if (candidate <= distance) { distance = candidate; nearest = i; }
            }
            if (nearest >= 0 && !IsOccupied(nearest, tile.index))
            {
                Place(tile.index, nearest, tile.QuarterTurns);
                return;
            }
            Vector3 homeWorld = tile.transform.parent != null ? tile.transform.parent.TransformPoint(tile.homePosition) : tile.homePosition;
            if (Vector3.Distance(position, homeWorld) <= .10f)
            {
                selected = tile.index;
                ReturnSelectedToTray();
                return;
            }
            tile.RestorePose();
            selected = tile.index;
            if (nearest >= 0) sockets[nearest].Reject();
            tile.PlayFeedback(true);
            RefreshConnections();
            UpdateHighlights();
            Notify(nearest >= 0 ? "接口已占用，轨段已回到原位。" : "这里没有接住轨段，已回到原位。请靠近接口并对齐高度。", OrbitalFeedbackCue.Reject);
        }

        public void UpdateHighlights()
        {
            bool editable = Editable;
            bool held = HasHeld;
            foreach (var tile in tiles)
                if (tile != null) tile.Highlight(editable && (tile.IsManipulating || !held && selected == tile.index));
            for (int i = 0; i < sockets.Length; i++)
            {
                var socket = sockets[i];
                if (socket == null) continue;
                bool near = false, blocked = false;
                foreach (var tile in tiles)
                    if (tile != null && tile.IsManipulating && Vector3.Distance(tile.transform.position, socket.transform.position) <= SnapDistance)
                    { if (IsOccupied(i, tile.index)) blocked = true; else near = true; }
                bool available = !IsOccupied(i, selected);
                socket.SetHint(editable && (held || ValidTile(selected)) && available, editable && near, editable && blocked);
            }
        }

        public void RefreshConnections()
        {
            foreach (var tile in tiles)
            {
                if (tile == null) continue;
                bool seated = tile.Installed && !tile.IsManipulating;
                tile.SetConnections(seated && PortConnected(tile, true), seated && PortConnected(tile, false));
            }
        }

        bool PortConnected(OrbitalConstructionTile tile, bool input)
        {
            Vector3 point = input ? tile.WorldIn : tile.WorldOut;
            Vector3 outward = tile.WorldPortDirection(input);
            float threshold = PortDistance * PortDistance;
            if (fixedPorts != null)
                foreach (Vector3 fixedPort in fixedPorts)
                    if ((point - transform.TransformPoint(fixedPort)).sqrMagnitude <= threshold) return true;
            foreach (var other in tiles)
            {
                if (other == null || other == tile || !other.Installed || other.IsManipulating) continue;
                if ((point - other.WorldIn).sqrMagnitude <= threshold && Vector3.Dot(outward, other.WorldPortDirection(true)) < -.75f) return true;
                if ((point - other.WorldOut).sqrMagnitude <= threshold && Vector3.Dot(outward, other.WorldPortDirection(false)) < -.75f) return true;
            }
            return false;
        }

        /// <summary>A location-based inspection suggestion, never a claimed diagnosis or a solution lookup.</summary>
        public string ExplainFailure(Vector3 boardLocalPosition)
        {
            RefreshConnections();
            Vector3 point = transform.TransformPoint(boardLocalPosition);
            OrbitalConstructionTile nearest = null;
            float distance = float.PositiveInfinity;
            foreach (var tile in tiles)
            {
                if (tile == null || !tile.Installed || tile.IsManipulating) continue;
                if (!tile.InputConnected)
                {
                    float candidate = (point - tile.WorldIn).sqrMagnitude;
                    if (candidate < distance) { distance = candidate; nearest = tile; }
                }
                if (!tile.OutputConnected)
                {
                    float candidate = (point - tile.WorldOut).sqrMagnitude;
                    if (candidate < distance) { distance = candidate; nearest = tile; }
                }
            }
            if (nearest == null) return "回看球离开路线的最后一段，检查接收口、轨段方向和目标顺序。";
            string socket = (nearest.SocketIndex + 1).ToString("00");
            return "回看" + socket + "号接口" + nearest.DisplayName + "的悬空端；检查方向和接收高度，也可调整相邻轨段。";
        }

        bool ValidTile(int index) => tiles != null && index >= 0 && index < tiles.Length && tiles[index] != null;
        bool ValidSocket(int index) => sockets != null && index >= 0 && index < sockets.Length && sockets[index] != null;
        bool Owns(OrbitalConstructionTile tile) => tile != null && ValidTile(tile.index) && tiles[tile.index] == tile && tile.kit == this;
        void Notify(string message, OrbitalFeedbackCue cue = OrbitalFeedbackCue.Select)
        {
            if (owner != null && owner.Construction == this) owner.ConstructionChanged(message, cue);
        }

        void OnEnable() { BindTrayTarget(); }
        void Start() { BindTrayTarget(); }

        void BindTrayTarget()
        {
            if (boundTrayTarget == trayTarget) return;
            if (boundTrayTarget != null) boundTrayTarget.Clicked -= ReturnSelectedToTray;
            boundTrayTarget = trayTarget;
            if (boundTrayTarget != null) boundTrayTarget.Clicked += ReturnSelectedToTray;
        }

        void OnDisable()
        {
            if (boundTrayTarget != null) boundTrayTarget.Clicked -= ReturnSelectedToTray;
            boundTrayTarget = null;
            CancelManipulations();
            SettlePresentation();
            foreach (var tile in tiles) if (tile != null) tile.Highlight(false);
            foreach (var socket in sockets) if (socket != null) socket.ResetFeedback();
        }
    }
}
