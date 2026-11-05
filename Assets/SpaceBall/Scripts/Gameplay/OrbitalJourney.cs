using TMPro;
using UnityEngine;

namespace SpaceBall
{
    public sealed partial class OrbitalPrototype
    {
        [SerializeField] TextMeshPro journeyText;
        [SerializeField] Transform[] memoryDots;
        OrbitalProgress progress;
        int attempts, hintStage, memoryCount;
        bool usedHint;
        bool preferencesOpen;
        bool chapterOverview;
        string hintText = "";
        public OrbitalProgress Profile => progress ?? (progress = OrbitalProgress.Load());
        public bool ReducedMotion => Profile.reducedMotion;
        public int ChallengeAttempts => attempts;
        public bool ChallengeUsedHint => usedHint;
        public int PreviousFlightPointCount => memoryCount;
        public bool IsChapterOverview => chapterOverview;

        void InitializeExperience() { progress = OrbitalProgress.Load(); }
        void SaveExperience() { if (!IsReviewSession) Profile.Save(); interfaceSignature = -1; }

        void StartChallenge()
        {
            attempts = hintStage = memoryCount = 0;
            usedHint = false;
            chapterOverview = false;
            hintText = "";
            Profile.lastLevel = (int)State.Level;
            if (Application.isPlaying) SaveExperience();
        }

        void CompleteFlight()
        {
            LogReviewResult();
            if (memoryDots != null && frames.Count > 1)
            {
                memoryCount = Mathf.Min(memoryDots.Length, frames.Count);
                for (int i = 0; i < memoryCount; i++)
                {
                    int index = Mathf.RoundToInt(i * (frames.Count - 1f) / (memoryCount - 1));
                    memoryDots[i].localPosition = transform.InverseTransformPoint(frames[index].position);
                }
            }
            if (State.Phase != OrbitalPhase.Success) return;
            Profile.For(State.Level).Complete(attempts, usedHint);
            SaveExperience();
        }

        public void ContinueJourney()
        {
            if (replaying || State.Phase != OrbitalPhase.Success) return;
            int next = (int)State.Level + 1;
            if (next < OrbitalCampaign.Count) SelectLevel((OrbitalLevel)next);
            else { chapterOverview = !chapterOverview; preferencesOpen = false; interfaceSignature = -1; }
        }

        public void ShowHint()
        {
            if (!AssemblyEditable || State.Phase == OrbitalPhase.Success) return;
            usedHint = true;
            hintStage = Mathf.Min(hintStage + 1, 2);
            hintText = "线索 " + hintStage + "/2 · " + OrbitalCampaign.Hint(State.Level, hintStage);
            motionFeedback = "";
            interfaceSignature = -1;
        }

        string AssemblyInstruction()
        {
            if (Construction != null) return Construction.Instruction;
            if (Assembly == null) return "将能源送到空间站。";
            if (Assembly.selected >= 0)
                return Assembly.selected == 0 ? "D 已选中 · 点亮起的 D 接口安装" : "R 已选中 · 点亮起的 R 接口安装";
            if (!Assembly.pieces[0].Installed) return "点托盘中的 D 道岔，再点 D 接口";
            if (!Assembly.pieces[1].Installed) return "点托盘中的 R 模块，再点 R 接口";
            if (State.Level == OrbitalLevel.Tutorial && !Profile.tutorial.completed)
                return "点已装零件转向：先经过 A，再到 B；点发射器试跑";
            return "转动已装零件规划路线 · 点发射器试跑";
        }

        void RefreshExperience()
        {
            if (journeyText == null) return;
            var record = Profile.For(State.Level);
            bool success = State.Phase == OrbitalPhase.Success && !replaying;
            bool receiving = DeliveryPresentationPending;
            bool flight = State.Phase == OrbitalPhase.Running || State.Phase == OrbitalPhase.Paused || replaying;
            RefreshMissionCard(record, success && !receiving, flight);
            if (receiving) SetCopy(statusText, "接收中");
            if (chapterOverview)
                SetCopy(feedbackText, "已完成 " + Profile.CompletedCount + "/6 航程 · 获得 " + Profile.TotalBadges + "/18 奖章 · 选择航程继续精进");
            else if (receiving)
                SetCopy(feedbackText, "能源已捕获 · 正在供能与锁定");
            else if (success)
                SetCopy(feedbackText, (int)State.Level + 1 < OrbitalCampaign.Count ? "进度已保存 · 下一航程：" + OrbitalCampaign.Title((OrbitalLevel)((int)State.Level + 1)) :
                    "归航完成 · 查看航程总览，收集其余奖章");
            else if (!flight)
                SetCopy(feedbackText, !string.IsNullOrEmpty(hintText) ? hintText :
                    !string.IsNullOrEmpty(motionFeedback) && (State.Phase != OrbitalPhase.Configuring || Time.unscaledTime < assemblyFeedbackUntil) ? motionFeedback :
                    State.Phase == OrbitalPhase.Failure ? State.Feedback : AssemblyInstruction());
            else SetCopy(feedbackText, replaying ? "回看上一段航迹 · 不改变成绩" :
                State.Phase == OrbitalPhase.Paused ? "航行已暂停 · 继续时保留速度与目标" : "能源球航行中 · 观察沿途亮起的目标");
            if (preferencesOpen) SetCopy(feedbackText, "偏好保存在本机 · 收起后继续规划航路");

            if (memoryDots != null)
                for (int i = 0; i < memoryDots.Length; i++)
                {
                    bool visible = Profile.trace && !flight && i < memoryCount;
                    if (memoryDots[i].gameObject.activeSelf != visible) memoryDots[i].gameObject.SetActive(visible);
                }

            foreach (var control in controls)
            {
                bool enabled = !flight;
                bool visible = true;
                string caption = null;
                switch (control.kind)
                {
                    case OrbitalControlKind.Level:
                        bool unlocked = Profile.IsUnlocked((OrbitalLevel)control.value);
                        enabled &= unlocked;
                        caption = (control.value + 1).ToString("00") + " " + (unlocked ? OrbitalCampaign.ShortTitle((OrbitalLevel)control.value) : "待解锁");
                        break;
                    case OrbitalControlKind.Reset: enabled = true; caption = flight ? "收回球" : "再试一次"; break;
                    case OrbitalControlKind.Pause:
                        visible = !replaying && (State.Phase == OrbitalPhase.Running || State.Phase == OrbitalPhase.Paused);
                        enabled = !replaying && (State.Phase == OrbitalPhase.Running || State.Phase == OrbitalPhase.Paused);
                        caption = State.Phase == OrbitalPhase.Paused ? "继续航行" : "暂停"; break;
                    case OrbitalControlKind.Replay: visible = !flight && frames.Count > 1; enabled &= frames.Count > 1; caption = "回看航迹"; break;
                    case OrbitalControlKind.Continue:
                        visible = success && !receiving; enabled = visible; caption = (int)State.Level + 1 < OrbitalCampaign.Count ? "下一航程 →" : chapterOverview ? "回到航路" : "航程总览"; break;
                    case OrbitalControlKind.Hint: visible = !flight && !success; enabled &= !success; caption = hintStage == 1 ? "再给线索" : "路线提示"; break;
                    case OrbitalControlKind.Trace: visible = preferencesOpen; enabled = true; caption = Profile.trace ? "航迹 开" : "航迹 关"; break;
                    case OrbitalControlKind.Audio: visible = preferencesOpen; enabled = true; caption = Profile.sound ? "声音 开" : "声音 关"; break;
                    case OrbitalControlKind.Comfort: visible = preferencesOpen; enabled = true; caption = Profile.reducedMotion ? "舒适 静态" : "舒适 动态"; break;
                    case OrbitalControlKind.Settings: enabled = true; caption = preferencesOpen ? "收起偏好" : "偏好"; break;
                    case OrbitalControlKind.Launch:
                        enabled &= LaunchConfigurationReady;
                        break;
                }
                // Native Spatial selection follows the active GameObject, not Collider.enabled.
                if (control.gameObject.activeSelf != visible) control.gameObject.SetActive(visible);
                control.GetComponent<OrbitalClickTarget>().interactable = enabled;
                var link = control.GetComponent<OrbitalControlLabel>();
                if (link != null)
                {
                    if (link.text.gameObject.activeSelf != visible) link.text.gameObject.SetActive(visible);
                    if (caption != null) SetCopy(link.text, caption);
                    bool primary = control.kind == OrbitalControlKind.Continue || control.kind == OrbitalControlKind.Pause ||
                        control.kind == OrbitalControlKind.Level && control.value == (int)State.Level;
                    Color textColor = enabled ? primary ? new Color(.035f, .09f, .13f) : new Color(.88f, .94f, .96f) : new Color(.43f, .55f, .61f);
                    if (link.text.color != textColor) link.text.color = textColor;
                }
                var surface = control.GetComponent<Renderer>();
                if (surface != null)
                {
                    var material = control.kind == OrbitalControlKind.Continue || control.kind == OrbitalControlKind.Pause ? palette[1] :
                        control.kind == OrbitalControlKind.Level && control.value == (int)State.Level ? palette[4] : palette[3];
                    if (surface.sharedMaterial != material) surface.sharedMaterial = material;
                }
            }
        }

        static void SetCopy(TextMeshPro label, string text) { if (label.text != text) label.text = text; }

        void BuildJourneyInterface()
        {
            BuildMissionCard();
            controls = buildControls.ToArray();
            memoryDots = new Transform[64];
            for (int i = 0; i < memoryDots.Length; i++)
            {
                var dot = Primitive("Previous flight " + i, PrimitiveType.Sphere, transform, Vector3.zero, Vector3.one * .005f, palette[2], false);
                dot.SetActive(false);
                memoryDots[i] = dot.transform;
            }
        }
    }
}
