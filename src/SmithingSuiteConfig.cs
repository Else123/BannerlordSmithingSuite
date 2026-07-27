namespace SmithingSuite
{
    /// <summary>
    /// Diagnostics configuration for in-game testing, mirroring AutoTraderConfig's shape as a
    /// plain static holder. Slice 6 will bridge these into MCM v5 (Options -> Mod Options);
    /// until then they are code defaults only - there is no XML persistence yet.
    /// </summary>
    public static class SmithingSuiteConfig
    {
        /// <summary>
        /// When true, <see cref="Integration.SmithingLog"/> writes a detailed per-phase trace to
        /// the log file (smith pools, candidates, plans, execution results). Default ON while
        /// this mod is in testing - see Integration.SmithingLog for the file location.
        /// </summary>
        public static bool VerboseLogging { get; set; } = true;

        /// <summary>
        /// When true, <see cref="Integration.SmithingLog"/> also posts short phase messages to
        /// the in-game message log, in addition to the existing run-summary Report(...) message.
        /// </summary>
        public static bool LogToScreen { get; set; } = true;

        /// <summary>
        /// Percentage of a smith's crafting stamina the SMELT phase is allowed to plan against;
        /// the remainder is left untouched for the REFINE phase's fresh snapshot. Fixes the
        /// Slice 6 finding that smelting ran first against an essentially unlimited candidate
        /// list and consumed every point of stamina, leaving refining only the integer-division
        /// remainder (0 capacity in 60 of 70 observed smith-phases in the first in-game run).
        /// Set to 50 (the user's "50/50 is good" decision, 2026-07-26) after that run. Read
        /// through <see cref="Integration.SmithingBehavior"/>, clamped to [0, 100] at the read
        /// site so an out-of-range value (e.g. from a bad save or future MCM input) cannot break
        /// planning. Slice 6's MCM will expose this as "Production: SmeltStaminaSharePercent".
        /// </summary>
        public static int SmeltStaminaSharePercent { get; set; } = 50;

        /// <summary>
        /// When true, <see cref="Integration.TravelStaminaBehavior"/> tops up the player party's
        /// heroes' crafting stamina every in-game hour while they are travelling (outside any
        /// settlement) - vanilla only regenerates this stamina for heroes standing in a
        /// settlement. This is the mod's FIRST deliberate departure from vanilla economics (every
        /// earlier slice deliberately avoided changing them); the user found the vanilla
        /// restriction unmotivated ("Stamina sollte auch generiert werden, wenn man von A nach B
        /// reist") and asked for it, on the explicit condition that it stays switchable - see
        /// SPEC-SLICE6.md section C. Default ON per the user's request (2026-07-26). Slice 6's
        /// MCM will expose this as "Travel: TravelStaminaEnabled".
        /// </summary>
        public static bool TravelStaminaEnabled { get; set; } = true;

        /// <summary>
        /// Multiplier applied to the vanilla-mirrored hourly recovery rate granted while
        /// travelling (see <see cref="TravelStaminaEnabled"/>). 1.0 (default) means travelling
        /// recovers stamina at exactly the same rate resting in a settlement does; values above
        /// or below that scale the balance change up or down without touching the underlying
        /// formula. Read through <see cref="Integration.TravelStaminaBehavior"/>. Slice 6's MCM
        /// will expose this as "Travel: TravelStaminaMultiplier" (0.0-2.0).
        /// </summary>
        public static float TravelStaminaMultiplier { get; set; } = 1.0f;
    }
}
