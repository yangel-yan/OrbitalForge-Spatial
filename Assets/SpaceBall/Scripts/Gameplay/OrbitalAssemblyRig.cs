using UnityEngine;

namespace SpaceBall
{
    /// <summary>A two-part construction kit. Placement and orientation drive the real track colliders.</summary>
    public sealed class OrbitalAssemblyRig : MonoBehaviour
    {
        public OrbitalPrototype owner;
        public OrbitalAssemblyPiece[] pieces;
        public OrbitalAssemblySocket[] sockets;
        public int selected = -1;
        public bool Ready => pieces != null && pieces.Length == 2 && pieces[0].Installed && pieces[1].Installed
            && !pieces[0].IsManipulating && !pieces[1].IsManipulating;
        public bool Editable => owner != null && owner.AssemblyEditable && owner.Assembly == this;

        public void Clear()
        {
            if (!Editable) return;
            selected = -1;
            foreach (var piece in pieces) piece.ResetToTray();
            foreach (var socket in sockets) socket.ResetFeedback();
            UpdateHighlights();
            Notify("轻点零件，再点同标记接口安装；轻点已装零件转向。");
        }

        public void Select(int index)
        {
            if (!Editable || index < 0 || index >= pieces.Length) return;
            selected = index;
            UpdateHighlights();
            Notify(index == 0 ? "道岔已选中：点亮起的 D 接口安装。" : "反射模块已选中：点亮起的 R 接口安装。");
        }

        public bool PlaceSelected(int socketIndex)
        {
            if (!Editable || socketIndex < 0 || socketIndex >= sockets.Length) return false;
            if (selected != socketIndex)
            {
                if (selected >= 0 && selected < pieces.Length)
                {
                    sockets[socketIndex].Reject();
                    pieces[selected].PlayFeedback(true);
                    Notify(selected == 0 ? "这块是 D 道岔，请放到亮起的 D 接口。" : "这块是 R 反射模块，请放到亮起的 R 接口。", OrbitalFeedbackCue.Reject);
                }
                else Notify("先轻点托盘里的零件，再点同标记接口。", OrbitalFeedbackCue.Reject);
                return false;
            }
            return Place(socketIndex);
        }

        public bool Place(int index)
        {
            if (!Editable || index < 0 || index >= pieces.Length || pieces[index].IsManipulating) return false;
            owner.PrepareAssemblyEdit();
            pieces[index].SetInstalled(true);
            selected = -1;
            UpdateHighlights();
            Notify(Ready ? "拼装完成。轻点零件选择出路，点发射器试跑。" : "安装完成。轻点另一块零件，再点它的接口。", OrbitalFeedbackCue.Install);
            return true;
        }

        public void FinishDrag(OrbitalAssemblyPiece piece)
        {
            if (!Editable) { piece.RestorePose(); return; }
            owner.PrepareAssemblyEdit();
            Vector3 position = piece.transform.localPosition;
            if (PlanarDistance(position, piece.SocketPosition) < .09f)
            {
                Place(piece.index);
                return;
            }
            if (PlanarDistance(position, piece.HomePosition) < .10f)
            {
                piece.SetInstalled(false);
                selected = -1;
                UpdateHighlights();
                Notify("零件已放回托盘。轻点零件和同标记接口可重新安装。");
                return;
            }
            int other = 1 - piece.index;
            bool wrongSocket = PlanarDistance(position, pieces[other].SocketPosition) < .09f;
            if (wrongSocket) sockets[other].Reject();
            piece.ReturnToSeat();
            UpdateHighlights();
            Notify(wrongSocket ? (piece.index == 0 ? "D 零件对应 D 接口，已送回原位。" : "R 零件对应 R 接口，已送回原位。")
                : "这里没有匹配接口，零件已送回原位。", OrbitalFeedbackCue.Reject);
        }

        public void Rotate(OrbitalAssemblyPiece piece)
        {
            if (!Editable || !piece.Installed) return;
            owner.PrepareAssemblyEdit();
            piece.SetChoice(1 - piece.Choice);
            selected = -1;
            UpdateHighlights();
            Notify(piece.index == 0 ? "道岔已转向。沿亮起的轨道检查去程。" : "反射模块已转向。沿亮起的轨道检查返程。", OrbitalFeedbackCue.Rotate);
        }

        public void UpdateHighlights()
        {
            for (int i = 0; i < pieces.Length; i++)
            {
                bool near = pieces[i].IsManipulating && PlanarDistance(pieces[i].transform.localPosition, pieces[i].SocketPosition) < .09f;
                int other = 1 - i;
                bool wrongNear = pieces[other].IsManipulating && PlanarDistance(pieces[other].transform.localPosition, pieces[i].SocketPosition) < .09f;
                pieces[i].Highlight(selected == i || pieces[i].IsManipulating);
                sockets[i].SetHint(selected == i, near, wrongNear);
            }
        }

        /// <summary>Finishes decorative motion without changing placement, selection or physics.</summary>
        public void SettlePresentation()
        {
            if (pieces != null) foreach (var piece in pieces) if (piece != null) piece.SettlePresentation();
            if (sockets != null) foreach (var socket in sockets) if (socket != null) socket.SettlePresentation();
        }

        public void Notify(string message, OrbitalFeedbackCue cue = OrbitalFeedbackCue.Select)
        {
            if (owner != null && owner.Assembly == this) owner.AssemblyChanged(message, cue);
        }

        static float PlanarDistance(Vector3 a, Vector3 b)
        {
            a.y = b.y = 0;
            return Vector3.Distance(a, b);
        }
    }
}
