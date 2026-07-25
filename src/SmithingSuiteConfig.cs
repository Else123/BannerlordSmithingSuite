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
    }
}
