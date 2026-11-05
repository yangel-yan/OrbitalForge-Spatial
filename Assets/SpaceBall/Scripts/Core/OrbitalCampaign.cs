namespace SpaceBall
{
    /// <summary>The first six-mission expedition. Enum values 0/1 remain save compatible.</summary>
    public static class OrbitalCampaign
    {
        public const int Count = 6;
        public const string ChapterTitle = "第一章 · 归航计划";
        public static bool IsValid(OrbitalLevel level) => (int)level >= 0 && (int)level < Count;
        public static bool RequiresGate(OrbitalLevel level) => level == OrbitalLevel.Moon || level == OrbitalLevel.Bridge || level == OrbitalLevel.Finale;
        public static bool ReverseTargets(OrbitalLevel level) => level == OrbitalLevel.Reverse || level == OrbitalLevel.Bridge;
        public static bool HasCheckpoint(OrbitalLevel level) => level == OrbitalLevel.Finale;
        public static int InitialReflector(OrbitalLevel level) => RequiresGate(level) ? 1 : 0;
        public static int CorrectRoute(OrbitalLevel level) => level == OrbitalLevel.Crossing || level == OrbitalLevel.Bridge ? 0 : 1;
        public static int CorrectReflector(OrbitalLevel level) => RequiresGate(level) ? 0 : 1;
        public static string Title(OrbitalLevel level)
        {
            switch (level)
            {
                case OrbitalLevel.Tutorial: return "点亮中继";
                case OrbitalLevel.Moon: return "绕月折返";
                case OrbitalLevel.Crossing: return "接轨初航";
                case OrbitalLevel.Reverse: return "高低接驳";
                case OrbitalLevel.Bridge: return "供能回路";
                case OrbitalLevel.Finale: return "归航建造";
                default: return "归航计划";
            }
        }
        public static string ShortTitle(OrbitalLevel level)
        {
            switch (level)
            {
                case OrbitalLevel.Tutorial: return "中继";
                case OrbitalLevel.Moon: return "绕月";
                case OrbitalLevel.Crossing: return "接轨";
                case OrbitalLevel.Reverse: return "高低";
                case OrbitalLevel.Bridge: return "供能";
                case OrbitalLevel.Finale: return "建造";
                default: return "航程";
            }
        }
        public static string Objective(OrbitalLevel level)
        {
            switch (level)
            {
                case OrbitalLevel.Moon: return "A 供电 → G 折返 → B → 空间站";
                case OrbitalLevel.Crossing: return "接通 A → B → 空间站";
                case OrbitalLevel.Reverse: return "B → A → 空间站";
                case OrbitalLevel.Bridge: return "B 供电 → G → A → 空间站";
                case OrbitalLevel.Finale: return "A + B 供电 → G → C → 空间站";
                default: return "A → B → 空间站";
            }
        }
        public static string[] Nodes(OrbitalLevel level)
        {
            switch (level)
            {
                case OrbitalLevel.Moon: return new[] { "A", "G", "B", "S" };
                case OrbitalLevel.Bridge: return new[] { "B", "G", "A", "S" };
                case OrbitalLevel.Finale: return new[] { "A", "B", "G", "C", "S" };
                case OrbitalLevel.Reverse: return new[] { "B", "A", "S" };
                default: return new[] { "A", "B", "S" };
            }
        }
        public static string Hint(OrbitalLevel level, int stage)
        {
            if (stage <= 1)
            {
                switch (level)
                {
                    case OrbitalLevel.Moon: return "先经过 A，为下层返程门 G 供能。";
                    case OrbitalLevel.Crossing: return "沿发射口找下一个接收口。直轨延伸，弯轨改变方向；接口还要同高。";
                    case OrbitalLevel.Reverse: return "先到 B，再到低层 A。比较两个接口的高度，平轨接不住明显的落差。";
                    case OrbitalLevel.Bridge: return "先连通 B 的供电线，再把 G 门后的低层路接到 A。";
                    case OrbitalLevel.Finale: return "先让 A、B 供电。门后还要亲自接一条经过 C 的交付路，直达 S 不算完成。";
                    default: return "直达空间站会漏掉 A，检查道岔出口。";
                }
            }
            switch (level)
            {
                case OrbitalLevel.Moon: return "D 走绕月线；R 向内折返，穿 G 到 B。";
                case OrbitalLevel.Crossing: return "先接发射口前的缺口，再让弯轨的出口对准 B。点选同一轨段可转四个方向。";
                case OrbitalLevel.Reverse: return "先完成 B 前的高轨；落差处使用降轨，让低出口和 A 前的接收口对齐。";
                case OrbitalLevel.Bridge: return "B 要在门前供电。检查 G 两端的高度，再选择能把球带向 A 的弯轨。";
                case OrbitalLevel.Finale: return "先连 A、B 和 G；门后给 C 留出转弯空间，再接向空间站，别把交付捷径当成目标全完成。";
                default: return "D 经过 A；R 转向 B，再送往空间站。";
            }
        }
    }
}
