using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SmithingSuite.Integration;
using SmithingSuite.Logic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameMenus;
using TestRig.Harness.Logic;

namespace SmithingSuite.Scenarios
{
    /// <summary>
    /// Health scenario (time stopped, no Days steps): the mod's campaign behaviors are registered
    /// and its town-menu option is present exactly once, directly after vanilla's "town_smithy".
    /// The mod registers no GameModels, so there is no model check.
    /// </summary>
    public sealed class HealthScenario : IScenario
    {
        private const string MenuId = "town";
        private const string AnchorOptionId = "town_smithy";
        private const string OwnOptionId = "smithingsuite_town";

        // Same path as SmithingLog.LogFilePath (private there).
        private static readonly string LogFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Mount and Blade II Bannerlord", "SmithingSuite", "smithingsuite.log");

        // Error paths (SmithingBehavior.SmithingTownConsequence, WeaponDesignAdvisorMixin,
        // EngineDesignEvaluator) call SmithingLog.Error, which writes a line carrying this tag
        // regardless of VerboseLogging, so the check is meaningful with default settings. Info and
        // trace lines (e.g. the "UIExtenderEx unavailable" optional-dependency note) are written
        // only while VerboseLogging is on and are not errors. The check covers error lines only.
        private static readonly string[] ErrorMarkers = { LogMarkers.ErrorTag };

        // The harness calls Run twice on the same instance: "@0" before the soak, "@N" after it.
        private long? _logLengthAtStart;

        public string Id => "smithingsuite.health";

        public IEnumerable<ScenarioStep> Run(IScenarioContext ctx)
        {
            // GetCampaignBehavior<T> matches by "is T", so the typed reference proves registration.
            SmithingBehavior smithing = Campaign.Current.GetCampaignBehavior<SmithingBehavior>();
            ctx.Check("behavior.SmithingBehavior", smithing != null,
                "SmithingBehavior registered", smithing != null ? "ok" : "null");

            TravelStaminaBehavior travel = Campaign.Current.GetCampaignBehavior<TravelStaminaBehavior>();
            ctx.Check("behavior.TravelStaminaBehavior", travel != null,
                "TravelStaminaBehavior registered", travel != null ? "ok" : "null");

            CheckTownMenu(ctx);

            CheckLogGrowth(ctx);
            yield break;
        }

        private void CheckLogGrowth(IScenarioContext ctx)
        {
            if (_logLengthAtStart == null)
            {
                // First call (@0): a missing log counts as length 0.
                _logLengthAtStart = GetLogLength();
                ctx.Log("mod log length at start: " + _logLengthAtStart + " bytes (" + LogFilePath + ")");
                return;
            }

            long start = _logLengthAtStart.Value;
            long end = GetLogLength();
            if (end < start)
            {
                // SmithingLog rotated the file (5 MB) during the soak; the rotated content is in
                // smithingsuite.log.old and the new file holds only newer lines, so read it whole.
                ctx.Log("mod log shrank (" + start + " -> " + end + " bytes), rotated; scanning the new file from 0");
                start = 0;
            }

            string appended = ReadFrom(start);
            string found = null;
            if (appended == null)
            {
                found = "log unreadable";
            }
            else
            {
                foreach (string marker in ErrorMarkers)
                {
                    if (appended.IndexOf(marker, StringComparison.Ordinal) >= 0)
                    {
                        found = marker;
                        break;
                    }
                }
            }

            ctx.Check("log.no-error-markers", found == null,
                "no error marker in the " + (end - start) + " bytes appended since @0 (" + string.Join(" | ", ErrorMarkers) + ")",
                found ?? "none");
        }

        private static long GetLogLength()
        {
            try
            {
                return File.Exists(LogFilePath) ? new FileInfo(LogFilePath).Length : 0;
            }
            catch (IOException)
            {
                return 0;
            }
        }

        // Returns null when the file cannot be read.
        private static string ReadFrom(long offset)
        {
            try
            {
                if (!File.Exists(LogFilePath))
                {
                    return string.Empty;
                }

                using (FileStream stream = new FileStream(LogFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    stream.Seek(offset, SeekOrigin.Begin);
                    using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        return reader.ReadToEnd();
                    }
                }
            }
            catch (IOException)
            {
                return null;
            }
        }

        private static void CheckTownMenu(IScenarioContext ctx)
        {
            GameMenu menu = Campaign.Current.GameMenuManager.GetGameMenu(MenuId);
            ctx.Check("menu.town", menu != null, "game menu '" + MenuId + "' exists", menu != null ? "ok" : "null");
            if (menu == null)
            {
                return;
            }

            int ownCount = 0;
            int ownIndex = -1;
            int anchorIndex = -1;
            int position = 0;
            foreach (GameMenuOption option in menu.MenuOptions)
            {
                if (option.IdString == OwnOptionId)
                {
                    ownCount++;
                    if (ownIndex < 0)
                    {
                        ownIndex = position;
                    }
                }
                else if (option.IdString == AnchorOptionId && anchorIndex < 0)
                {
                    anchorIndex = position;
                }

                position++;
            }

            ctx.Check("menu.option-once", ownCount == 1, OwnOptionId + " occurs exactly once", ownCount.ToString());

            bool anchored = anchorIndex >= 0 && ownIndex == anchorIndex + 1;
            ctx.Check("menu.option-after-anchor", anchored,
                OwnOptionId + " directly after " + AnchorOptionId,
                OwnOptionId + " at " + ownIndex + ", " + AnchorOptionId + " at " + anchorIndex);
        }
    }
}
