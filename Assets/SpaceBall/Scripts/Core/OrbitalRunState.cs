namespace SpaceBall
{
    public enum OrbitalLevel
    {
        Tutorial,
        Moon,
        Crossing,
        Reverse,
        Bridge,
        Finale
    }

    public enum OrbitalPhase
    {
        Configuring,
        Running,
        Paused,
        Success,
        Failure
    }

    public enum OrbitalEvent
    {
        TargetA,
        ReturnGate,
        TargetB,
        Dock,
        Recovery,
        ClosedGate,
        CheckpointC
    }

    /// <summary>
    /// Owns one puzzle attempt. Motion and input adapters report events; this model
    /// enforces progress order without depending on Unity, physics, or wall time.
    /// </summary>
    public sealed class OrbitalRunState
    {
        public OrbitalLevel Level { get; private set; }
        public OrbitalPhase Phase { get; private set; }
        public int Route { get; private set; }
        public int Reflector { get; private set; }
        public bool HasA { get; private set; }
        public bool HasB { get; private set; }
        public bool HasC { get; private set; }
        public bool GateOpen { get; private set; }
        public bool GateCrossed { get; private set; }

        /// <summary>Empty until an attempt ends; stable machine-readable terminal reason.</summary>
        public string ResultCode { get; private set; }
        public string Feedback { get; private set; }

        public OrbitalRunState(OrbitalLevel level = OrbitalLevel.Tutorial)
        {
            SelectLevel(level);
        }

        public bool SetRoute(int route)
        {
            if (Phase != OrbitalPhase.Configuring || route < 0 || route > 1)
            {
                return false;
            }

            Route = route;
            return true;
        }

        public bool SetReflector(int reflector)
        {
            if (Phase != OrbitalPhase.Configuring || reflector < 0 || reflector > 1)
            {
                return false;
            }

            Reflector = reflector;
            return true;
        }

        public bool Launch()
        {
            if (Phase != OrbitalPhase.Configuring)
            {
                return false;
            }

            ClearProgress();
            Phase = OrbitalPhase.Running;
            Feedback = "能量球已发射。任务顺序：" + OrbitalCampaign.Objective(Level);
            return true;
        }

        /// <summary>
        /// Returns true when a running attempt consumes the event, including an
        /// event that ends in failure. Returns false outside Running.
        /// </summary>
        public bool Apply(OrbitalEvent orbitalEvent)
        {
            if (Phase != OrbitalPhase.Running)
            {
                return false;
            }

            switch (orbitalEvent)
            {
                case OrbitalEvent.TargetA:
                    if (OrbitalCampaign.ReverseTargets(Level) && !HasB)
                        return Fail("target_a_before_b", "本关先点亮 B，再前往 A；请检查目标顺序。");
                    if (Level == OrbitalLevel.Bridge && !GateCrossed)
                        return Fail("target_a_before_gate", "B 已供电，但球还没有穿过返程门。请检查折返方向。");
                    HasA = true;
                    if (Level == OrbitalLevel.Moon)
                    {
                        GateOpen = true;
                        Feedback = "中继 A 已供能，返程门已打开，并会保持到本次尝试结束。";
                    }
                    else
                    {
                        Feedback = OrbitalCampaign.ReverseTargets(Level) ? "目标 A 已点亮，继续前往空间站。" :
                            Level == OrbitalLevel.Finale ? "中继 A 已供电，还需要点亮高轨上的 B。" : "目标 A 已点亮，继续前往目标 B。";
                    }

                    return true;

                case OrbitalEvent.ReturnGate:
                    if (!OrbitalCampaign.RequiresGate(Level))
                    {
                        return Fail("unexpected_gate", "本关没有返程门；检测到不属于本关的轨道事件，请重试。");
                    }

                    if (!GateOpen)
                    {
                        return Fail("closed_gate", ClosedGateFeedback());
                    }

                    GateCrossed = true;
                    Feedback = Level == OrbitalLevel.Bridge ? "已通过返程门，继续点亮目标 A，再进入空间站。" :
                        Level == OrbitalLevel.Finale ? "已通过返程门，沿低层轨道完成 C 校验。" : "已通过返程门，继续点亮目标 B，再进入空间站。";
                    return true;

                case OrbitalEvent.TargetB:
                    if (!HasA && !OrbitalCampaign.ReverseTargets(Level))
                    {
                        return Fail("target_b_before_a", "先到达了目标 B，但目标 A 尚未点亮；请调整去程路线。");
                    }

                    if (Level == OrbitalLevel.Moon && !GateCrossed)
                    {
                        return Fail("target_b_before_gate", "中继 A 已供能，但球尚未通过返程门；请检查返程路线。");
                    }

                    HasB = true;
                    if (Level == OrbitalLevel.Bridge || Level == OrbitalLevel.Finale)
                    {
                        GateOpen = Level == OrbitalLevel.Bridge || HasA;
                        Feedback = Level == OrbitalLevel.Bridge ? "中继 B 已供电，返程门已打开。穿门后前往 A。" :
                            "A、B 双中继已供电，门已打开。穿过 G 后完成 C 校验。";
                    }
                    else Feedback = Level == OrbitalLevel.Reverse ? "目标 B 已点亮，继续前往目标 A。" : "目标 B 已点亮，前往空间站入口。";
                    return true;

                case OrbitalEvent.CheckpointC:
                    if (!OrbitalCampaign.HasCheckpoint(Level)) return Fail("unexpected_checkpoint", "本关没有 C 校验点，请重试。");
                    if (!GateCrossed || !HasA || !HasB)
                        return Fail("checkpoint_before_gate", "C 校验需要先完成 A、B 供电，并穿过返程门。");
                    HasC = true;
                    Feedback = "C 校验完成，能源可以交付空间站。";
                    return true;

                case OrbitalEvent.Dock:
                    if (!HasA)
                    {
                        return Fail("dock_missing_a", "空间站尚不能接收：目标 A 未点亮，请先完成去程供能。");
                    }

                    if (OrbitalCampaign.RequiresGate(Level) && !GateCrossed)
                    {
                        return Fail("dock_before_gate", "空间站尚不能接收：球还没有通过返程门。");
                    }

                    if (!HasB)
                    {
                        return Fail("dock_missing_b", "空间站尚不能接收：目标 B 未点亮，请检查后段轨道。");
                    }

                    if (OrbitalCampaign.HasCheckpoint(Level) && !HasC)
                        return Fail("dock_missing_c", "空间站尚不能接收：C 校验未完成，请检查门后的低层航道。");

                    Phase = OrbitalPhase.Success;
                    ResultCode = "success";
                    Feedback = Level == OrbitalLevel.Moon
                        ? "供能、折返、交付完成！同一颗能量球已通过返程门，抵达空间站。"
                        : Level == OrbitalLevel.Finale ? "双中继供电、返程与 C 校验完成！能源已交付空间站。" :
                            "任务目标已完成，能量球已抵达空间站！";
                    return true;

                case OrbitalEvent.Recovery:
                    return Recover();

                case OrbitalEvent.ClosedGate:
                    return Fail("closed_gate", ClosedGateFeedback());

                default:
                    return Fail("unknown_event", "检测到未知轨道事件，请重试。");
            }
        }

        /// <summary>Restarts configuration, preserving both selected mechanism settings.</summary>
        public void Reset()
        {
            ClearProgress();
            Phase = OrbitalPhase.Configuring;
            Feedback = Level == OrbitalLevel.Moon
                ? "配置已保留。先经过中继 A 打开返程门，再沿低层轨道折返。"
                : Level == OrbitalLevel.Tutorial ? "配置已保留。设置道岔与反射板，依次点亮 A、B 并抵达空间站。" :
                    "配置已保留。任务顺序：" + OrbitalCampaign.Objective(Level);
        }

        /// <summary>Switching levels starts a fresh puzzle with that level's initial settings.</summary>
        public void SelectLevel(OrbitalLevel level)
        {
            if (!OrbitalCampaign.IsValid(level))
            {
                throw new System.ArgumentOutOfRangeException(nameof(level));
            }

            Level = level;
            Route = 0;
            Reflector = OrbitalCampaign.InitialReflector(level);
            Reset();
        }

        public bool TogglePause()
        {
            if (Phase == OrbitalPhase.Running)
            {
                Phase = OrbitalPhase.Paused;
                Feedback = "已暂停。配置和本次目标进度保持不变。";
                return true;
            }

            if (Phase == OrbitalPhase.Paused)
            {
                Phase = OrbitalPhase.Running;
                Feedback = "继续运行。配置和本次目标进度保持不变。";
                return true;
            }

            return false;
        }

        private void ClearProgress()
        {
            HasA = false;
            HasB = false;
            HasC = false;
            GateOpen = false;
            GateCrossed = false;
            ResultCode = string.Empty;
        }

        private bool Recover()
        {
            if (Level == OrbitalLevel.Bridge)
            {
                if (!HasB) return Fail("recovery_before_b", "球已回收；先选择经过上层 B 的去程，为门供电。");
                return !GateCrossed ? Fail("recovery_gate_open", "B 已供电，但球还没有穿过 G。检查供电段出口与门前轨段的方向和高度。") :
                    Fail("recovery_after_gate", "球已穿门。检查下层 A 与空间站之间的交付航道。");
            }
            if (Level == OrbitalLevel.Reverse)
            {
                return !HasB ? Fail("recovery_before_b", "本关先 B 后 A。请检查去程是否经过上层 B。") :
                    Fail("recovery_before_a", "B 已点亮，但还没有到 A。检查降轨的低出口是否接上 A 前的航道。");
            }
            if (Level == OrbitalLevel.Finale)
            {
                if (!HasA || !HasB) return Fail("recovery_dual_power", "球已回收；A、B 两座中继都供电，返程门才会打开。");
                return !GateCrossed ? Fail("recovery_gate_open", "双中继已供电，但球尚未穿过 G。检查接向门口的轨段，留意高低接口。") :
                    Fail("recovery_after_gate", "球已穿门。检查 C 校验点与空间站之间的低层航道。");
            }
            if (Level == OrbitalLevel.Moon)
            {
                if (!HasA)
                {
                    return Fail("recovery_uncharged", "球进入了回收区；中继 A 未供能，返程门仍关闭。请检查去程是否经过 A。");
                }

                if (!GateCrossed)
                {
                    return Fail("recovery_gate_open", "中继 A 已供能、返程门已打开，但球进入了回收区。请检查返程反射板的朝向。");
                }

                return Fail("recovery_after_gate", "球已通过返程门，却进入了回收区。请检查目标 B 和空间站之间的后段轨道。");
            }

            if (!HasA)
            {
                return Fail("recovery_before_a", Level == OrbitalLevel.Crossing ? "目标 A 尚未点亮。沿发射口检查第一处接轨缺口。" : "球进入了回收区，目标 A 尚未点亮。请检查去程道岔。");
            }

            return !HasB
                ? Fail("recovery_before_b", Level == OrbitalLevel.Crossing ? "A 已点亮。检查弯轨的出口是否接向 B，而不是空接口。" : "目标 A 已点亮，但球进入了回收区。请检查反射板是否朝向目标 B。")
                : Fail("recovery_before_dock", "目标 A、B 已点亮，但球进入了回收区。请检查空间站入口方向。");
        }

        private bool Fail(string resultCode, string feedback)
        {
            Phase = OrbitalPhase.Failure;
            ResultCode = resultCode;
            Feedback = feedback;
            return true;
        }

        string ClosedGateFeedback()
        {
            string source = Level == OrbitalLevel.Bridge ? "B" : Level == OrbitalLevel.Finale ? "A、B 两座中继" : "A";
            return "返程门尚未供能。先经过 " + source + " 供电，再沿低层轨道折返。";
        }
    }
}
