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
    }
}
