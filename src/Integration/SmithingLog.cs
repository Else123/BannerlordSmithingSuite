using System;
using System.IO;
using SmithingSuite.Logic;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace SmithingSuite.Integration
{
    /// <summary>
    /// Diagnostics logger for in-game testing. Appends a plain-text trace to a log file under
    /// the user's Documents folder and, optionally, posts short phase messages to the in-game
    /// message log. Gated by <see cref="SmithingSuiteConfig.VerboseLogging"/> /
    /// <see cref="SmithingSuiteConfig.LogToScreen"/>.
    ///
    /// MUST NEVER THROW: every file operation is wrapped in try/catch so a logging failure can
    /// never break the campaign - at worst logging silently degrades (this run's lines are
    /// dropped, or logging falls back to screen-only). No retries.
    /// </summary>
    public static class SmithingLog
    {
        private const long MaxLogSizeBytes = 5L * 1024 * 1024;

        private static readonly string LogDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Mount and Blade II Bannerlord", "SmithingSuite");

        private static readonly string LogFilePath = Path.Combine(LogDirectory, "smithingsuite.log");
        private static readonly string RotatedLogFilePath = Path.Combine(LogDirectory, "smithingsuite.log.old");

        /// <summary>Appends one timestamped line when VerboseLogging is on.</summary>
        public static void Line(string message)
        {
            if (!SmithingSuiteConfig.VerboseLogging)
            {
                return;
            }

            AppendLine("[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " + message);
        }

        /// <summary>Appends a visually separated header so one run's phases are easy to scan.</summary>
        public static void Section(string title)
        {
            if (!SmithingSuiteConfig.VerboseLogging)
            {
                return;
            }

            AppendLine("---- " + title + " ----");
        }

        /// <summary>
        /// Starts a run block: a blank line, a rule, and a UTC + local timestamp with the
        /// settlement name, so one run is easy to find when the file has been appended to many
        /// times across a play session.
        /// </summary>
        public static void BeginRun(string settlementName)
        {
            if (!SmithingSuiteConfig.VerboseLogging)
            {
                return;
            }

            AppendLine(string.Empty);
            AppendLine("========================================================================");
            AppendLine("Run started " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") + " UTC / "
                + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " local - settlement: " + settlementName);
            AppendLine("========================================================================");
        }

        /// <summary>Short user-facing message, shown when LogToScreen is on.</summary>
        public static void Screen(string message)
        {
            if (!SmithingSuiteConfig.LogToScreen)
            {
                return;
            }

            try
            {
                InformationManager.DisplayMessage(new InformationMessage(message));
            }
            catch
            {
                // A diagnostics message must never break the campaign.
            }
        }

        /// <summary>
        /// Human-readable name for a vanilla CraftingMaterials ordinal (IronOre=0 .. Charcoal=8),
        /// used to make material and formula log output readable without a debugger.
        /// </summary>
        public static string MaterialName(int ordinal)
        {
            switch (ordinal)
            {
                case 0: return "IronOre";
                case 1: return "Iron1";
                case 2: return "Iron2";
                case 3: return "Iron3";
                case 4: return "Iron4";
                case 5: return "Iron5";
                case 6: return "Iron6";
                case 7: return "Wood";
                case 8: return "Charcoal";
                default: return "?";
            }
        }

        /// <summary>
        /// Renders a formula like "Wood 2 -&gt; Charcoal 3" or "Iron2 2 + Charcoal 1 -&gt; Iron3 1
        /// + Iron1 1", honouring <see cref="RefineFormulaSnapshot.HasSecondInput"/> and a zero
        /// Output2Count - i.e. never printing vanilla's dummy second-input/second-output slots.
        /// </summary>
        public static string Describe(RefineFormulaSnapshot formula)
        {
            if (formula == null)
            {
                return "?";
            }

            string inputs = MaterialName(formula.Input1) + " " + formula.Input1Count;
            if (formula.HasSecondInput)
            {
                inputs += " + " + MaterialName(formula.Input2) + " " + formula.Input2Count;
            }

            string outputs = MaterialName(formula.Output) + " " + formula.OutputCount;
            if (formula.Output2Count > 0)
            {
                outputs += " + " + MaterialName(formula.Output2) + " " + formula.Output2Count;
            }

            return inputs + " -> " + outputs;
        }

        private static void AppendLine(string line)
        {
            try
            {
                EnsureDirectory();
                RotateIfTooLarge();
                File.AppendAllText(LogFilePath, line + Environment.NewLine);
            }
            catch
            {
                // File I/O must never throw into campaign code - silently drop this line rather
                // than retry in a loop.
            }
        }

        private static void EnsureDirectory()
        {
            if (!Directory.Exists(LogDirectory))
            {
                Directory.CreateDirectory(LogDirectory);
            }
        }

        // Single rotation, no fancy scheme: once the live file crosses ~5 MB, the whole thing is
        // moved to smithingsuite.log.old (overwriting any previous .old) and a fresh file starts.
        // Runs are otherwise appended, never truncated, so separate play sessions can be diffed.
        private static void RotateIfTooLarge()
        {
            if (!File.Exists(LogFilePath))
            {
                return;
            }

            var info = new FileInfo(LogFilePath);
            if (info.Length < MaxLogSizeBytes)
            {
                return;
            }

            if (File.Exists(RotatedLogFilePath))
            {
                File.Delete(RotatedLogFilePath);
            }

            File.Move(LogFilePath, RotatedLogFilePath);
        }
    }
}
