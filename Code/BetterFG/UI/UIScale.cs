namespace BetterFG.UI
{
    /// <summary>
    /// Change S to scale the entire UI. 1.0 = original, 1.3 = 30% larger, etc.
    /// Everything in BetterFGTab, BetterFGTabManager, and CustomizationTab derives from this.
    /// </summary>
    public static class UIScale
    {
        public const float S = 1.3f;

        // ── Tab shell ─────────────────────────────────────────────────────────
        public const float TAB_W = 336f * S;
        public const float TAB_CONTENT_H = 312f * S;
        public const float TITLE_H = 28f * S;
        public const float TAB_GAP = 6f * S;
        public const float TAB_MARGIN_X = 35f * S;

        // ── Skin UI layout ────────────────────────────────────────────────────
        public const float PAD = 4f * S;
        public const float VPAD = 1f * S;
        public const float LH = 16f * S;   // line height
        public const float SH = 1f * S;   // separator height
        public const float BTN_H = 22f * S;
        public const float BTN_W = 78f * S;
        public const float ROW_H = 62f * S;

        public const float COVER_W = 84f * S;
        public const float COVER_H = 60f * S;
        public const float SEL_W = 46f * S;

        // ── Font sizes (rounded int) ───────────────────────────────────────────
        public static int FS => (int)(11f * S);
        public static int FS_SM => (int)(10f * S);
        public static int FS_TITLE => (int)(13f * S);
    }
}