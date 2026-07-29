using System;
using System.Collections.Generic;
using System.IO;
using SmithingSuite.Logic;
using TaleWorlds.CampaignSystem;
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

        // Safety net for callers outside a BeginRun/EndRun block (see _runInProgress below): if
        // one of those callers forgets an explicit Flush(), the buffer must still never grow
        // unbounded across a whole play session - flush automatically once it reaches this size.
        private const int MaxBufferedLinesOutsideRun = 500;

        private static readonly string LogDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Mount and Blade II Bannerlord", "SmithingSuite");

        private static readonly string LogFilePath = Path.Combine(LogDirectory, "smithingsuite.log");
        private static readonly string RotatedLogFilePath = Path.Combine(LogDirectory, "smithingsuite.log.old");

        // Lines are accumulated here during a run instead of hitting the filesystem per line -
        // with verbose logging on and a large party, one town-menu click can emit hundreds of
        // lines, and hundreds of file opens per click is a real stutter risk. EndRun() flushes
        // this in one operation.
        private static readonly List<string> _bufferedLines = new List<string>();

        // True only between BeginRun() and EndRun(). The batching optimisation above is a
        // hot-path win for the town-menu run only (hundreds of lines per click); every other
        // caller (travel stamina's hourly tick, the crafting-screen advisor, the design
        // evaluator, UI bridge startup) is not a hot path and must reach disk promptly, or a
        // session that never triggers a run loses its diagnostics silently. AppendLine consults
        // this flag to decide whether the safety-net auto-flush below applies.
        private static bool _runInProgress;

        /// <summary>
        /// Mirrors <see cref="SmithingSuiteConfig.VerboseLogging"/> so callers building an
        /// expensive diagnostic string (e.g. formatting a material stock or a plan) can guard
        /// the construction itself, not just the call into this class.
        /// </summary>
        public static bool IsVerbose => SmithingSuiteConfig.VerboseLogging;

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
        /// Starts a run block: a blank line, a rule, and a UTC + local timestamp plus the
        /// in-game campaign date/time, with the settlement name, so one run is easy to find when
        /// the file has been appended to many times across a play session.
        /// </summary>
        public static void BeginRun(string settlementName)
        {
            if (!SmithingSuiteConfig.VerboseLogging)
            {
                return;
            }

            // Marks the run-scoped batching window open - see _runInProgress's doc. Set before
            // the header lines below so they too are treated as part of the batched run.
            _runInProgress = true;

            AppendLine(string.Empty);
            AppendLine("========================================================================");
            AppendLine("Run started " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") + " UTC / "
                + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " local / campaign time "
                + DescribeCampaignTime() + " - settlement: " + settlementName);
            AppendLine("========================================================================");
        }

        // Wall-clock timestamps alone cannot tell a reader whether two runs a few seconds apart
        // also span hours of in-game time (stamina legitimately regenerated) or none at all
        // (stamina was simply never consumed) - see SPEC-SLICE6.md finding 2. CampaignTime.Now
        // reads Campaign.Current.MapTimeTracker.Now, so it would throw a NullReferenceException
        // outside an active campaign (e.g. from the main menu); guard explicitly rather than let
        // that happen, per this class's never-throw contract. CampaignTime's own ToString() is
        // the simplest readable rendering that actually exists (day/season/year), so it is used
        // as-is instead of reinventing a format from GetYear/GetSeasonOfYear/GetDayOfSeason - but
        // it renders day-only resolution, so two runs on the same in-game day are otherwise
        // indistinguishable (code review Slice 6 finding B). CampaignTime.GetHourOfDay
        // (verified by decompiling TaleWorlds.CampaignSystem.CampaignTime, v1.4.7:
        // "public int GetHourOfDay => (int)(_numTicks / TimeTicksPerHour % HoursInDay);") is
        // appended to restore that resolution.
        private static string DescribeCampaignTime()
        {
            try
            {
                if (Campaign.Current == null)
                {
                    return "?";
                }

                CampaignTime now = CampaignTime.Now;
                return now.ToString() + ", hour " + now.GetHourOfDay;
            }
            catch
            {
                // Diagnostics must never break the campaign - fall back to "unknown" rather than
                // let a formatting/localization edge case propagate.
                return "?";
            }
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

        /// <summary>
        /// Ends the run-scoped batching window and flushes whatever this run buffered. MUST be
        /// called exactly once at the end of every run - including the early "nothing to do"
        /// return - via try/finally, otherwise this run's lines are silently lost. Trade-off
        /// accepted for a diagnostics log: if the process dies mid-run, the buffer (and this
        /// run's trace) is lost with it rather than being flushed line-by-line.
        /// </summary>
        public static void EndRun()
        {
            Flush();
            _runInProgress = false;
        }

        /// <summary>
        /// Flushes any buffered lines to disk in a single file operation, then clears the buffer.
        /// Callers outside a BeginRun/EndRun block (travel stamina's hourly tick, the
        /// crafting-screen advisor, the design evaluator, UI bridge startup) are not the hot path
        /// the run-scoped batching exists for, so they must call this after every line they log
        /// to reach disk promptly instead of waiting for a town-menu run that may never happen.
        /// </summary>
        public static void Flush()
        {
            if (_bufferedLines.Count == 0)
            {
                return;
            }

            try
            {
                EnsureDirectory();
                RotateIfTooLarge();
                File.AppendAllText(LogFilePath, string.Join(Environment.NewLine, _bufferedLines) + Environment.NewLine);
            }
            catch
            {
                // File I/O must never throw into campaign code - silently drop these lines
                // rather than retry in a loop.
            }
            finally
            {
                _bufferedLines.Clear();
            }
        }

        private static void AppendLine(string line)
        {
            // Buffered in memory only - see Flush()/EndRun() for where the buffer actually
            // reaches disk (one file open/close per run instead of per line).
            _bufferedLines.Add(line);

            // Safety net (see _runInProgress's doc): outside a run-scoped block nothing else
            // guarantees these lines ever reach disk - if a caller forgets its explicit Flush()
            // and the buffer keeps growing, flush anyway rather than let it grow unbounded for
            // the rest of the session.
            if (!_runInProgress && _bufferedLines.Count >= MaxBufferedLinesOutsideRun)
            {
                Flush();
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
