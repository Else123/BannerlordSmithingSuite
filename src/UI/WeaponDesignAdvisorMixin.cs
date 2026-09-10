using System;
using System.Collections.Generic;
using System.Diagnostics;
using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.ViewModels;
using SmithingSuite.Advisor;
using SmithingSuite.Integration;
using static SmithingSuite.Advisor.AdvisorPresentation;
using TaleWorlds.CampaignSystem.ViewModelCollection.WeaponCrafting.WeaponDesign;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;

namespace SmithingSuite.UI
{
    /// <summary>
    /// Crafting advisor (SPEC-SLICE4-5.md Slice 5, TODO 11): a UIExtenderEx view-model mixin on
    /// vanilla's <see cref="WeaponDesignVM"/> that scores the design the player is currently building
    /// and, on request, searches for the best design within the same template. The scored result is
    /// presented in a popup opened from the crafting screen's own control cluster - see
    /// <c>Prefabs\AdvisorPopup.xml</c> - rather than as a panel floating over the 3D scene.
    ///
    /// Two very different cost budgets share this class, per the spec's performance constraint:
    /// <list type="bullet">
    /// <item><see cref="OnRefresh"/> runs on every <c>WeaponDesignVM.RefreshItem()</c> call, which
    /// <c>GauntletCraftingScreen</c> may invoke every frame - it must stay cheap (read already
    /// engine-computed numbers, score them, short-circuit if nothing changed) and must never touch
    /// <c>Crafting.GenerateItem</c> or <see cref="DesignOptimizer"/>.</item>
    /// <item><see cref="ExecuteSuggestBest"/> runs only on the popup's button click and is allowed to
    /// be expensive - it is the only place in this class permitted to run the optimizer.</item>
    /// </list>
    ///
    /// Fail-safe by construction (AC15): every public entry point is wrapped in try/catch. Any
    /// exception closes the popup (<see cref="AdvisorIsPopupOpen"/> goes false) and is logged at most
    /// once, mirroring <see cref="EngineDesignEvaluator"/>'s own once-per-run failure logging - the
    /// vanilla crafting screen must keep working even if the game's <c>WeaponDesignVM</c> shape
    /// changes under this mixin in a future update.
    /// </summary>
    [ViewModelMixin("RefreshItem")]
    public sealed class WeaponDesignAdvisorMixin : BaseViewModelMixin<WeaponDesignVM>
    {
        /// <summary>
        /// Usage index the advisor reads. Vanilla <c>RefreshStats</c> reads
        /// <c>SecondaryUsageSelector.SelectedIndex</c> to support dual-usage weapons (e.g. a spear
        /// that can also be thrown), but that member is not decompile-verified as public and
        /// reliably populated, so this stays on a weapon's primary usage - correct for the
        /// overwhelming majority of craftable designs and wrong only for designs with a selectable
        /// secondary usage. Revisit once the member is verified.
        /// </summary>
        private const int PrimaryUsageIndex = 0;

        // Display-only normalisation ceilings for the two axes the crafting template has no MaxValue
        // for (see FillRatioFor). These are chosen presentation constants, NOT game-derived limits:
        // above them the bar simply reads full while the number printed next to it stays exact.
        private const float EffectiveDamageDisplayCeiling = 100f;
        private const float SaleValueDisplayCeiling = 50000f;

        // Logged once per run (not once per call) - RefreshItem can fire every frame, so a broken
        // mixin must not spam the log the same way EngineDesignEvaluator's own failures do not.
        // Deliberately an INSTANCE field, not static (code review FINDING 2, mirroring
        // EngineDesignEvaluator._hasLoggedFailure's own reasoning): a static flag would persist for
        // the whole game session across every crafting-screen open and every mixin instance, so one
        // benign first-frame exception would burn it and a genuinely different failure an hour later
        // would then log nothing while the popup just silently refused to open.
        private bool _hasLoggedFailure;

        // Separate one-shot flags per message (not a single shared bool): OnRefresh can fire every
        // frame, and the "not yet ready" case legitimately happens on the very first tick or two
        // before Crafting.GetCurrentCraftedItemObject() has a real design to return - it must log
        // once so that state is visible, but must never fire again once the advisor has started
        // succeeding, and must not prevent the success line from ever logging.
        private bool _hasLoggedFirstSuccess;
        private bool _hasLoggedNotYetReady;
        private bool _hasLoggedPopupOpened;

        // Root-cause fix (SPEC-SLICE4-5.md "Kontext"): WeaponDesignVM.CraftedItemObject is only ever
        // assigned in CraftingVM.ExecuteMainAction() - i.e. only after the player clicks the final
        // Craft button - so during design it was always null and the advisor never had data. The fix
        // reads the same private Crafting field vanilla's own WeaponDesignVM.RefreshStats() reads,
        // via reflection (WeaponDesignVM exposes no public accessor for it). Cached once resolved
        // (the field's presence cannot change at runtime for a given game version); if resolution
        // fails, _cachedCrafting stays null and the NEXT call retries rather than latching a
        // transient miss as permanent - a future game patch renaming the field must not permanently
        // disable the advisor any more than a genuine one-frame timing miss should.
        private Crafting _cachedCrafting;

        // One-shot, separate from _hasLoggedFailure (which covers the generic try/catch fail-safe):
        // this is a specific, actionable diagnosis - "the _crafting field is gone" - that must name
        // the field so a future maintainer does not have to rediscover this reflection dependency
        // from a generic exception message.
        private bool _hasLoggedCraftingFieldMissing;

        // Cached identity of the last-scored design (FINDING 5) - the four selected pieces' engine
        // object references and scale percentages, compared field-by-field in DesignUnchanged rather
        // than via a per-frame string concatenation. null piece + 0 scale is a valid "no piece
        // selected" identity, matching PieceKey's old "-" sentinel.
        private CraftingPiece _lastBladePiece;
        private int _lastBladeScale;
        private CraftingPiece _lastGuardPiece;
        private int _lastGuardScale;
        private CraftingPiece _lastHandlePiece;
        private int _lastHandleScale;
        private CraftingPiece _lastPommelPiece;
        private int _lastPommelScale;

        // What the single explanation block falls back to when the pointer is not over any axis row.
        // Held separately from AdvisorExplanationText so that un-hovering an axis restores whatever
        // was last shown - the verdict, or the suggestion if a search has since produced one -
        // instead of silently discarding it.
        private string _baseExplanation;

        private bool _advisorIsPopupOpen;
        private string _advisorExplanationText = string.Empty;
        private MBBindingList<AdvisorAxisItemVM> _advisorAxes;
        private bool _advisorIsBusy;
        private bool _advisorIsSuggestButtonEnabled = true;
        private bool _advisorIsMounted;
        private string _advisorContextButtonText;
        // Set once in the constructor and never again, but written through the same full get/set
        // property shape every vanilla view model uses for bound members (WeaponDesignVM.RandomizeHint
        // and friends) rather than exposed read-only - a binding form vanilla nowhere relies on.
        private string _advisorTitleText;
        private string _advisorSuggestButtonText;
        private BasicTooltipViewModel _advisorTriggerHint;
        private BasicTooltipViewModel _advisorSuggestHint;
        private BasicTooltipViewModel _advisorContextHint;
        private BasicTooltipViewModel _advisorCloseHint;

        public WeaponDesignAdvisorMixin(WeaponDesignVM vm) : base(vm)
        {
            // Bridged once when the crafting screen (and this mixin) is constructed (code review
            // Slice 6 finding A): the player can reach this screen directly, without ever
            // clicking the town-menu option that was the only other Apply() call site, so
            // ScoreWeights.ForContext(...) below and in ExecuteSuggestBest would otherwise keep
            // scoring against compiled defaults for the whole session. Deliberately NOT in
            // OnRefresh - that can run every frame (see class doc). Safe no-op if MCM has not
            // registered the page yet.
            SmithingSuiteMcmSettings.Apply();

            _advisorAxes = new MBBindingList<AdvisorAxisItemVM>();
            AdvisorTitleText = AdvisorTextRenderer.Title;
            AdvisorSuggestButtonText = AdvisorTextRenderer.SuggestButton;
            _advisorContextButtonText = AdvisorTextRenderer.ContextButtonLabel(MountedContext.OnFoot);
            _baseExplanation = AdvisorTextRenderer.NoDesign;
            _advisorExplanationText = _baseExplanation;

            // Hints are built once and reused: BasicTooltipViewModel takes a delegate that the
            // tooltip system calls at hover time, so a single instance per button stays correct even
            // though the mixin's state changes underneath it.
            AdvisorTriggerHint = Hint(AdvisorTextRenderer.TriggerHint);
            AdvisorSuggestHint = Hint(AdvisorTextRenderer.SuggestHint);
            AdvisorContextHint = Hint(AdvisorTextRenderer.ContextHint);
            AdvisorCloseHint = Hint(AdvisorTextRenderer.CloseHint);

            // One-shot, unconditional (the constructor itself only ever runs once per mixin
            // instance): confirms UIExtenderEx actually constructed this mixin, distinct from the
            // "mixins registered" line logged at UIExtenderEx.Apply() time, which only proves the
            // registration call happened, not that the game ever built an instance for a real
            // crafting screen.
            SmithingLog.Line("WeaponDesignAdvisorMixin: advisor mixin attached to WeaponDesignVM");
            // The crafting screen is opened independently of any town-menu run - the batching
            // optimisation does not apply here, so flush immediately (see SmithingLog.Flush()).
            SmithingLog.Flush();
        }

        [DataSourceProperty]
        public bool AdvisorIsPopupOpen
        {
            get { return _advisorIsPopupOpen; }
            set
            {
                if (value != _advisorIsPopupOpen)
                {
                    _advisorIsPopupOpen = value;
                    OnPropertyChanged();
                }
            }
        }

        [DataSourceProperty]
        public string AdvisorTitleText
        {
            get { return _advisorTitleText; }
            set
            {
                if (value != _advisorTitleText)
                {
                    _advisorTitleText = value;
                    OnPropertyChanged();
                }
            }
        }

        [DataSourceProperty]
        public string AdvisorSuggestButtonText
        {
            get { return _advisorSuggestButtonText; }
            set
            {
                if (value != _advisorSuggestButtonText)
                {
                    _advisorSuggestButtonText = value;
                    OnPropertyChanged();
                }
            }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel AdvisorTriggerHint
        {
            get { return _advisorTriggerHint; }
            set
            {
                if (value != _advisorTriggerHint)
                {
                    _advisorTriggerHint = value;
                    OnPropertyChanged();
                }
            }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel AdvisorSuggestHint
        {
            get { return _advisorSuggestHint; }
            set
            {
                if (value != _advisorSuggestHint)
                {
                    _advisorSuggestHint = value;
                    OnPropertyChanged();
                }
            }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel AdvisorContextHint
        {
            get { return _advisorContextHint; }
            set
            {
                if (value != _advisorContextHint)
                {
                    _advisorContextHint = value;
                    OnPropertyChanged();
                }
            }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel AdvisorCloseHint
        {
            get { return _advisorCloseHint; }
            set
            {
                if (value != _advisorCloseHint)
                {
                    _advisorCloseHint = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// The single explanation block under the axis rows: the verdict (or the latest suggestion)
        /// by default, and the hovered axis's reason for as long as the pointer stays on that row.
        /// </summary>
        [DataSourceProperty]
        public string AdvisorExplanationText
        {
            get { return _advisorExplanationText; }
            set
            {
                if (value != _advisorExplanationText)
                {
                    _advisorExplanationText = value;
                    OnPropertyChanged();
                }
            }
        }

        [DataSourceProperty]
        public MBBindingList<AdvisorAxisItemVM> AdvisorAxes
        {
            get { return _advisorAxes; }
            set
            {
                if (value != _advisorAxes)
                {
                    _advisorAxes = value;
                    OnPropertyChanged();
                }
            }
        }

        [DataSourceProperty]
        public bool AdvisorIsBusy
        {
            get { return _advisorIsBusy; }
            set
            {
                if (value != _advisorIsBusy)
                {
                    _advisorIsBusy = value;
                    OnPropertyChanged();
                    // Derived, not independent state - the popup binds IsEnabled to this rather than
                    // to "!@AdvisorIsBusy" because vanilla's own prefabs never use "!@" negation
                    // syntax anywhere in the shipped GUI, so it is not a binding form worth risking.
                    AdvisorIsSuggestButtonEnabled = !value;
                }
            }
        }

        [DataSourceProperty]
        public bool AdvisorIsSuggestButtonEnabled
        {
            get { return _advisorIsSuggestButtonEnabled; }
            set
            {
                if (value != _advisorIsSuggestButtonEnabled)
                {
                    _advisorIsSuggestButtonEnabled = value;
                    OnPropertyChanged();
                }
            }
        }

        [DataSourceProperty]
        public bool AdvisorIsMounted
        {
            get { return _advisorIsMounted; }
            set
            {
                if (value != _advisorIsMounted)
                {
                    _advisorIsMounted = value;
                    OnPropertyChanged();
                }
            }
        }

        [DataSourceProperty]
        public string AdvisorContextButtonText
        {
            get { return _advisorContextButtonText; }
            set
            {
                if (value != _advisorContextButtonText)
                {
                    _advisorContextButtonText = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// UIExtenderEx patches <c>WeaponDesignVM.RefreshItem()</c> to call this afterwards - and
        /// <c>GauntletCraftingScreen</c> calls <c>RefreshItem()</c> from its update loop, i.e.
        /// potentially every frame (SPEC-SLICE4-5.md TODO 11's performance constraint). This method
        /// must therefore do only cheap work and must short-circuit the instant nothing about the
        /// design actually changed - see <see cref="RefreshFromCurrentItem"/>.
        /// </summary>
        public override void OnRefresh()
        {
            RefreshFromCurrentItem(force: false);
        }

        public void ExecuteOpenAdvisorPopup()
        {
            try
            {
                AdvisorIsPopupOpen = true;

                if (!_hasLoggedPopupOpened)
                {
                    _hasLoggedPopupOpened = true;
                    SmithingLog.Line("WeaponDesignAdvisorMixin: advisor popup opened with "
                        + AdvisorAxes.Count + " axes");
                    // The crafting screen has no town-menu run around it - the batching
                    // optimisation does not apply here (see SmithingLog.Flush()).
                    SmithingLog.Flush();
                }
            }
            catch (Exception ex)
            {
                AdvisorIsPopupOpen = false;
                LogFailureOnce(ex);
            }
        }

        public void ExecuteCloseAdvisorPopup()
        {
            AdvisorIsPopupOpen = false;
        }

        public void ExecuteToggleMountedContext()
        {
            try
            {
                AdvisorIsMounted = !AdvisorIsMounted;
                AdvisorContextButtonText = AdvisorTextRenderer.ContextButtonLabel(CurrentContext());
                // Context flips ScoreWeights.ForContext's axis weights - force a rescore even though
                // the design key itself has not changed, so the toggle takes visible effect
                // immediately instead of waiting for the next incidental RefreshItem tick. Still just
                // the cheap read-and-score path, never the optimizer.
                RefreshFromCurrentItem(force: true);
            }
            catch (Exception ex)
            {
                AdvisorIsPopupOpen = false;
                LogFailureOnce(ex);
            }
        }

        /// <summary>
        /// The one entry point allowed to run <see cref="DesignOptimizer"/> - explicit user action
        /// only, never from <see cref="OnRefresh"/> (SPEC-SLICE4-5.md TODO 11). Builds the search
        /// universe from what <see cref="WeaponDesignVM.PieceLists"/> already exposes rather than
        /// re-querying <c>ICraftingCampaignBehavior.IsOpened</c> itself, and guards against re-entry
        /// so a double-click cannot start a second search while one is already running.
        ///
        /// Runs synchronously on this button-click handler - deliberately NOT on a background
        /// thread. <c>Crafting.GenerateItem</c> (what <see cref="DesignOptimizer"/> calls through
        /// <see cref="IDesignEvaluator"/>) builds real <c>ItemObject</c>s and touches
        /// <c>MBObjectManager</c>; Bannerlord's engine types are not documented as thread-safe, and
        /// a concurrency fault there would be far worse than the stutter this is meant to prevent.
        /// The bound against a freeze is instead <see cref="Stopwatch"/>-based wall-clock time
        /// (<see cref="SmithingSuiteConfig.AdvisorTimeBudgetMs"/>), bridged into
        /// <see cref="OptimizerOptions.ShouldAbort"/> - <see cref="DesignOptimizer"/> itself stays
        /// engine-free and deterministic (AC8) and owns no clock of its own; this is the only place
        /// in the mixin permitted to own one, because it is the UI-thread caller that actually needs
        /// the wall-clock guarantee. <c>AdvisorEvaluationBudget</c> alone cannot provide that
        /// guarantee: each evaluation's real cost is unknown and machine-dependent, so a count-only
        /// budget can still block the render thread for a perceptible time on a slow machine.
        /// </summary>
        public void ExecuteSuggestBest()
        {
            if (AdvisorIsBusy)
            {
                return;
            }

            AdvisorIsBusy = true;
            try
            {
                // Bridged again at the start of the one entry point allowed to run the optimizer
                // (code review Slice 6 finding A): a value the player changes in MCM between
                // opening the crafting screen and clicking "Suggest best" - the evaluation budget
                // or any axis weight - must take effect on this search, not the next one. Inside
                // the try so a bridging exception is caught by this method's existing fail-safe
                // (AC15) instead of escaping. Never in OnRefresh, which can run every frame.
                SmithingSuiteMcmSettings.Apply();

                WeaponDesignVM vm = ViewModel;
                if (vm == null)
                {
                    return;
                }

                PieceCatalogue catalogue = BuildCatalogue(vm);
                if (catalogue.AvailablePieces.Count == 0)
                {
                    SetBaseExplanation(AdvisorTextRenderer.NoUnlockedPieces);
                    return;
                }

                DesignCandidate seed = BuildSeed(vm);
                MountedContext context = CurrentContext();
                ScoreWeights weights = ScoreWeights.ForContext(context);

                // The wall-clock guard (see this method's class doc): owned here, not by
                // DesignOptimizer, so the optimizer stays a deterministic, engine-free, unit-tested
                // pure search and this UI-thread caller alone carries the timing dependency. Clamped
                // to >= 0 at this read site, mirroring SmithingSuiteConfig.SmeltStaminaSharePercent's
                // own clamp-at-the-read-site pattern - a corrupted or future bad config/save value
                // must not go negative, which would make ElapsedMilliseconds > timeBudgetMs trivially
                // true forever and silently disable the search on every call.
                var searchClock = Stopwatch.StartNew();
                int timeBudgetMs = Math.Max(0, SmithingSuiteConfig.AdvisorTimeBudgetMs);
                int evaluationBudget = SmithingSuiteConfig.AdvisorEvaluationBudget;
                var options = new OptimizerOptions(evaluationBudget, context, weights, vm.MaxDifficulty,
                    shouldAbort: () => searchClock.ElapsedMilliseconds > timeBudgetMs);
                var optimizer = new DesignOptimizer(new EngineDesignEvaluator());
                OptimizationResult result = optimizer.Optimize(seed, catalogue, options);

                LogIfAbortedOnTime(result, searchClock.ElapsedMilliseconds, timeBudgetMs, evaluationBudget);

                SetBaseExplanation(AdvisorTextRenderer.Suggestion(DescribeSuggestion(result)));
            }
            catch (Exception ex)
            {
                AdvisorIsPopupOpen = false;
                LogFailureOnce(ex);
            }
            finally
            {
                AdvisorIsBusy = false;
            }
        }

        /// <summary>
        /// Called from an axis row's hover catcher (via <see cref="AdvisorAxisItemVM"/>) to swap the
        /// single explanation block to that axis's reason. Hover is a per-frame-ish event on the
        /// render thread, so this does nothing but assign a string.
        /// </summary>
        private void ShowAxisReason(AdvisorAxisItemVM axis)
        {
            try
            {
                if (axis != null)
                {
                    AdvisorExplanationText = axis.Reason;
                }
            }
            catch (Exception ex)
            {
                AdvisorIsPopupOpen = false;
                LogFailureOnce(ex);
            }
        }

        private void RestoreBaseExplanation()
        {
            try
            {
                AdvisorExplanationText = _baseExplanation;
            }
            catch (Exception ex)
            {
                AdvisorIsPopupOpen = false;
                LogFailureOnce(ex);
            }
        }

        /// <summary>
        /// One-line diagnostic for the case a player is most likely to report as "the suggestion
        /// looks poor": the search aborted on the wall-clock guard rather than exhausting the count
        /// budget or converging. Distinguishes "aborted on time" from "aborted on count" by
        /// comparing <see cref="OptimizationResult.EvaluationsUsed"/> against the configured count
        /// budget rather than adding a second bool to <see cref="OptimizationResult"/> for it: if
        /// the search stopped early (<see cref="OptimizationResult.BudgetExhausted"/>) but used
        /// fewer evaluations than the count budget allowed, the count budget cannot be what stopped
        /// it - only <see cref="OptimizerOptions.ShouldAbort"/> could have. Logged via
        /// <see cref="SmithingLog"/> exactly like every other call site in this class - <c>Line</c>
        /// already no-ops when <see cref="SmithingSuiteConfig.VerboseLogging"/> is off, and
        /// <c>Flush</c> is called unconditionally because this call site has no town-menu run around
        /// it to batch into (see <see cref="SmithingLog.Flush"/>).
        /// </summary>
        private static void LogIfAbortedOnTime(OptimizationResult result, long elapsedMs, int timeBudgetMs, int evaluationBudget)
        {
            if (!result.BudgetExhausted || result.EvaluationsUsed >= evaluationBudget)
            {
                return;
            }

            SmithingLog.Line("WeaponDesignAdvisorMixin: suggest-best search aborted on time budget ("
                + timeBudgetMs + "ms) after " + elapsedMs + "ms elapsed, " + result.EvaluationsUsed
                + " evaluations used - returning best design found so far");
            SmithingLog.Flush();
        }

        /// <summary>
        /// The shared cheap path behind both <see cref="OnRefresh"/> (every incidental tick) and
        /// <see cref="ExecuteToggleMountedContext"/> (an explicit but still-cheap re-score): reads
        /// the engine's already-computed stats for the live design and scores them via
        /// <see cref="WeaponScorer"/>. Never calls <c>Crafting.GenerateItem</c> or
        /// <see cref="DesignOptimizer"/> - those are reserved for <see cref="ExecuteSuggestBest"/>.
        ///
        /// <paramref name="force"/> = false is the per-frame path: if the four selected pieces (engine
        /// object identity + scale) are identical to last time, this returns immediately without
        /// recomputing anything, allocating anything, or raising a single property-changed event -
        /// the difference between a smooth crafting screen and a per-frame stutter (FINDING 5: this
        /// check is allocation-free, unlike the string-concatenation key it replaced).
        /// </summary>
        private void RefreshFromCurrentItem(bool force)
        {
            try
            {
                WeaponDesignVM vm = ViewModel;
                if (vm == null)
                {
                    return;
                }

                CraftingPiece bladePiece;
                int bladeScale;
                ReadPieceIdentity(vm.SelectedBladePiece, out bladePiece, out bladeScale);
                CraftingPiece guardPiece;
                int guardScale;
                ReadPieceIdentity(vm.SelectedGuardPiece, out guardPiece, out guardScale);
                CraftingPiece handlePiece;
                int handleScale;
                ReadPieceIdentity(vm.SelectedHandlePiece, out handlePiece, out handleScale);
                CraftingPiece pommelPiece;
                int pommelScale;
                ReadPieceIdentity(vm.SelectedPommelPiece, out pommelPiece, out pommelScale);

                bool unchanged = DesignUnchanged(bladePiece, bladeScale, guardPiece, guardScale,
                    handlePiece, handleScale, pommelPiece, pommelScale);
                if (!force && unchanged)
                {
                    return;
                }

                Crafting crafting = ResolveCrafting();
                WeaponStats stats = ReadCurrentWeaponStats(crafting);
                if (stats == null)
                {
                    // Do NOT commit the cache here. RefreshFromCurrentItem can run before
                    // Crafting.GetCurrentCraftedItemObject() has a real design to return (e.g. the
                    // very first tick, or an incomplete design mid-selection) - stats is null on
                    // that call even though the player has (or is about to have) a real selection.
                    // Committing the cache before this null check would latch that failed attempt as
                    // "the current design", every subsequent frame would then read as unchanged, and
                    // OnRefresh would short-circuit before ever reaching the scoring code below - the
                    // advisor would stay empty for the rest of the session no matter what the player
                    // crafted. Leaving the cache untouched here means the next frame is not
                    // "unchanged" yet and retries.
                    if (!_hasLoggedNotYetReady)
                    {
                        _hasLoggedNotYetReady = true;
                        SmithingLog.Line("WeaponDesignAdvisorMixin: crafted item not yet available, advisor waiting for a design");
                        // The crafting screen has no town-menu run around it - the batching
                        // optimisation does not apply here (see SmithingLog.Flush()).
                        SmithingLog.Flush();
                    }
                    return;
                }

                MountedContext context = CurrentContext();
                ScoreWeights weights = ScoreWeights.ForContext(context);
                WeaponScore score = WeaponScorer.Score(stats, context, weights);

                // Commit only now that a score was actually computed - see the comment on the
                // stats == null branch above for why an early-out must never reach this line.
                CommitLastDesign(bladePiece, bladeScale, guardPiece, guardScale,
                    handlePiece, handleScale, pommelPiece, pommelScale);

                UpdateAxes(score, crafting);
                // The design (or context) changed - any previously computed suggestion no longer
                // describes what is on screen, so the explanation falls back to the fresh verdict.
                SetBaseExplanation(AdvisorTextRenderer.Verdict(BuildVerdict(score, context)));

                if (!_hasLoggedFirstSuccess)
                {
                    _hasLoggedFirstSuccess = true;
                    SmithingLog.Line("WeaponDesignAdvisorMixin: advisor data ready, scored "
                        + score.Axes.Count + " axes");
                    // The crafting screen has no town-menu run around it - the batching
                    // optimisation does not apply here (see SmithingLog.Flush()).
                    SmithingLog.Flush();
                }
            }
            catch (Exception ex)
            {
                AdvisorIsPopupOpen = false;
                LogFailureOnce(ex);
            }
        }

        /// <summary>
        /// Reads the engine's LIVE design preview - not <see cref="WeaponDesignVM.CraftedItemObject"/>,
        /// which is only ever assigned on the final Craft button click (see the class doc and the
        /// <c>_cachedCrafting</c> field comment for the full root-cause story). Mirrors vanilla's own
        /// <c>WeaponDesignVM.RefreshStats()</c>: <c>Crafting.GetCurrentCraftedItemObject()</c> returns
        /// a cached item that <c>Crafting.ReIndex()</c> regenerates on every piece switch / rescale,
        /// so it always matches what is currently on screen, and is cheap to call (no
        /// <c>Crafting.GenerateItem</c>, honouring this class's per-frame budget). The result is
        /// projected through <see cref="EngineDesignEvaluator.ToWeaponStats"/> - the exact same
        /// mapping <see cref="EngineDesignEvaluator"/>'s own (expensive) evaluator uses - so this
        /// cheap path can never silently drift from it.
        /// </summary>
        private static WeaponStats ReadCurrentWeaponStats(Crafting crafting)
        {
            if (crafting == null)
            {
                return null;
            }

            ItemObject item = crafting.GetCurrentCraftedItemObject();
            if (item == null)
            {
                return null;
            }

            WeaponComponentData weapon = item.GetWeaponWithUsageIndex(PrimaryUsageIndex);
            return weapon == null ? null : EngineDesignEvaluator.ToWeaponStats(weapon, item);
        }

        /// <summary>
        /// Resolves <c>WeaponDesignVM</c>'s private <c>_crafting</c> field - there is no public
        /// accessor (see the class doc). Uses <see cref="BaseViewModelMixin{TViewModel}.GetPrivate{TValue}"/>,
        /// the mixin base class's own reflection helper (already used and tested by every other
        /// UIExtenderEx mixin), rather than hand-rolled <c>System.Reflection</c> - one fewer
        /// reflection-shaped dependency for this class to own. Cached in <see cref="_cachedCrafting"/>
        /// once resolved; a null result is deliberately NOT cached, so a transient miss (e.g. called
        /// before the VM has finished constructing) retries on the next call instead of permanently
        /// disabling the advisor - only a genuine, permanent failure (e.g. a future game patch renames
        /// the field) keeps failing, and that case is logged loudly exactly once via
        /// <see cref="_hasLoggedCraftingFieldMissing"/> so the cause is obvious in the log instead of
        /// yet another silently-empty panel.
        /// </summary>
        private Crafting ResolveCrafting()
        {
            if (_cachedCrafting != null)
            {
                return _cachedCrafting;
            }

            Crafting crafting = GetPrivate<Crafting>("_crafting");
            if (crafting == null)
            {
                if (!_hasLoggedCraftingFieldMissing)
                {
                    _hasLoggedCraftingFieldMissing = true;
                    SmithingLog.Line("WeaponDesignAdvisorMixin: could not resolve WeaponDesignVM's "
                        + "private field '_crafting' via GetPrivate<Crafting> - advisor disabled "
                        + "until this is fixed for the current game version");
                    // The crafting screen has no town-menu run around it - the batching
                    // optimisation does not apply here (see SmithingLog.Flush()).
                    SmithingLog.Flush();
                }
                return null;
            }

            _cachedCrafting = crafting;
            return _cachedCrafting;
        }

        private void UpdateAxes(WeaponScore score, Crafting crafting)
        {
            float reachMax;
            float speedMax;
            float handlingMax;
            ReadTemplateStatMaxima(crafting, out reachMax, out speedMax, out handlingMax);

            if (AdvisorAxes == null)
            {
                AdvisorAxes = new MBBindingList<AdvisorAxisItemVM>();
            }

            AdvisorAxes.Clear();
            foreach (ScoreAxis axis in score.Axes)
            {
                float fillRatio = FillRatioFor(axis.Kind, axis.Value, reachMax, speedMax, handlingMax);
                AdvisorAxes.Add(new AdvisorAxisItemVM(
                    AdvisorTextRenderer.AxisName(axis.Kind),
                    axis.Value,
                    AdvisorTextRenderer.Reason(axis.Reason),
                    fillRatio,
                    ShowAxisReason,
                    RestoreBaseExplanation));
            }
        }

        /// <summary>
        /// The bar denominators the game itself uses on this screen. <c>Crafting.GetStatDatas</c> is
        /// what feeds vanilla's own stat rows: for every stat the current template exposes it yields
        /// the live value plus the template's <c>MaxValue</c> for that stat, so reusing those maxima
        /// makes the advisor's reach / speed / handling bars fill to exactly the same fraction as the
        /// vanilla bars a few centimetres to the left. Only called when the design actually changed,
        /// never on an idle frame.
        ///
        /// Attack speed takes the larger of the template's thrust and swing maxima: the scorer picks
        /// whichever attack mode penetrates better and reports that mode's speed under one axis name,
        /// so a single shared denominator is the only one under which the bar stays comparable when
        /// the chosen mode flips. A stat the template does not expose leaves its maximum at 0, which
        /// <see cref="FillRatioFor"/> reads as "no bar" rather than dividing by zero.
        /// </summary>
        private static void ReadTemplateStatMaxima(Crafting crafting, out float reachMax, out float speedMax, out float handlingMax)
        {
            reachMax = 0f;
            speedMax = 0f;
            handlingMax = 0f;
            if (crafting == null)
            {
                return;
            }

            foreach (CraftingStatData stat in crafting.GetStatDatas(PrimaryUsageIndex))
            {
                if (!stat.IsValid)
                {
                    continue;
                }

                switch (stat.Type)
                {
                    case CraftingTemplate.CraftingStatTypes.WeaponReach:
                        reachMax = Math.Max(reachMax, stat.MaxValue);
                        break;
                    case CraftingTemplate.CraftingStatTypes.ThrustSpeed:
                    case CraftingTemplate.CraftingStatTypes.SwingSpeed:
                        speedMax = Math.Max(speedMax, stat.MaxValue);
                        break;
                    case CraftingTemplate.CraftingStatTypes.Handling:
                        handlingMax = Math.Max(handlingMax, stat.MaxValue);
                        break;
                }
            }
        }

        /// <summary>
        /// How full an axis's bar is drawn, always in 0..1. Reach, attack speed and handling divide
        /// by the template's own maximum for the matching crafting stat (see
        /// <see cref="ReadTemplateStatMaxima"/>). Effective damage and sale value have no template
        /// maximum to borrow - effective damage is this mod's own armour-model figure and sale value
        /// is unbounded gold - so they divide by a documented display constant instead. A
        /// non-positive denominator yields an empty bar rather than a division by zero, and the
        /// result is clamped so a value above the ceiling reads as full while the number printed next
        /// to the bar stays exact.
        ///
        /// Keyed on <see cref="AdvisorAxisKind"/>, not on the axis's display name: the axis's
        /// identity is now part of the Advisor layer's public API, so this lookup no longer depends
        /// on English wording that a translation - or a copy-edit - could silently invalidate. An
        /// axis kind with no ceiling defined here still falls through to an empty bar next to a
        /// still-correct number.
        /// </summary>
        private static float FillRatioFor(AdvisorAxisKind kind, float value, float reachMax, float speedMax, float handlingMax)
        {
            float ceiling;
            switch (kind)
            {
                case AdvisorAxisKind.Reach:
                    ceiling = reachMax;
                    break;
                case AdvisorAxisKind.AttackSpeed:
                    ceiling = speedMax;
                    break;
                case AdvisorAxisKind.Handling:
                    ceiling = handlingMax;
                    break;
                case AdvisorAxisKind.EffectiveDamage:
                    ceiling = EffectiveDamageDisplayCeiling;
                    break;
                case AdvisorAxisKind.SaleValue:
                    ceiling = SaleValueDisplayCeiling;
                    break;
                default:
                    ceiling = 0f;
                    break;
            }

            if (ceiling <= 0f)
            {
                return 0f;
            }

            float ratio = value / ceiling;
            if (ratio < 0f)
            {
                return 0f;
            }

            return ratio > 1f ? 1f : ratio;
        }

        private void SetBaseExplanation(string text)
        {
            _baseExplanation = text;
            AdvisorExplanationText = text;
        }

        /// <summary>
        /// A cheap, allocation-free identity check for "has the design changed" (FINDING 5): compares
        /// the four selected pieces' <see cref="CraftingPiece"/> engine object identity - these are
        /// singleton instances from <c>MBObjectManager</c>, and the decompiled vanilla
        /// <c>WeaponDesignVM</c> compares them the same way when matching the current selection
        /// (plain <c>==</c>: <c>_crafting.SelectedPieces[pieceType].CraftingPiece == craftingPieceVM.CraftingPiece.CraftingPiece</c>)
        /// - and scale percentage against what was cached on the previous successful commit.
        /// Deliberately NOT <c>CraftedItemObject</c>'s own identity/StringId - GauntletCraftingScreen
        /// regenerates that item on every refresh regardless of whether the design actually changed,
        /// so keying on it would defeat the short-circuit entirely. Deliberately NOT a string key
        /// (the previous implementation) - that built 4-8 short-lived strings on every call, i.e.
        /// potentially every frame, purely to detect "nothing changed".
        ///
        /// Deliberately a PURE comparison with no side effect on the cache (bug fix - previously this
        /// check and the cache commit were one method, so it committed even on a call that later
        /// bailed out with no score, latching a failed first frame as "the current design" forever;
        /// see <see cref="RefreshFromCurrentItem"/>). The cache is only ever written by
        /// <see cref="CommitLastDesign"/>, which the caller invokes exclusively after a score has
        /// actually been computed.
        /// </summary>
        private bool DesignUnchanged(
            CraftingPiece bladePiece, int bladeScale,
            CraftingPiece guardPiece, int guardScale,
            CraftingPiece handlePiece, int handleScale,
            CraftingPiece pommelPiece, int pommelScale)
        {
            return bladePiece == _lastBladePiece && bladeScale == _lastBladeScale
                && guardPiece == _lastGuardPiece && guardScale == _lastGuardScale
                && handlePiece == _lastHandlePiece && handleScale == _lastHandleScale
                && pommelPiece == _lastPommelPiece && pommelScale == _lastPommelScale;
        }

        /// <summary>
        /// Writes the just-scored design into the cache that <see cref="DesignUnchanged"/> compares
        /// against next time. Must only be called after <see cref="RefreshFromCurrentItem"/> has
        /// successfully computed a score for these exact piece/scale values - never on an early-out
        /// (null VM, unavailable stats, exception), or a failed attempt would incorrectly latch as
        /// "the current design" and mask itself as unchanged on every later frame (the bug this
        /// split fixes).
        /// </summary>
        private void CommitLastDesign(
            CraftingPiece bladePiece, int bladeScale,
            CraftingPiece guardPiece, int guardScale,
            CraftingPiece handlePiece, int handleScale,
            CraftingPiece pommelPiece, int pommelScale)
        {
            _lastBladePiece = bladePiece;
            _lastBladeScale = bladeScale;
            _lastGuardPiece = guardPiece;
            _lastGuardScale = guardScale;
            _lastHandlePiece = handlePiece;
            _lastHandleScale = handleScale;
            _lastPommelPiece = pommelPiece;
            _lastPommelScale = pommelScale;
        }

        private static void ReadPieceIdentity(CraftingPieceVM pieceVm, out CraftingPiece piece, out int scale)
        {
            WeaponDesignElement element = pieceVm?.CraftingPiece;
            piece = element?.CraftingPiece;
            scale = element != null ? element.ScalePercentage : 0;
        }

        /// <summary>
        /// The unlocked universe the optimizer is allowed to search: every piece across
        /// <see cref="WeaponDesignVM.PieceLists"/> whose <c>PlayerHasPiece</c> flag is already true -
        /// that flag was set from <c>ICraftingCampaignBehavior.IsOpened</c> when the VM built the
        /// list, so reading it here is "using what the VM already exposes" rather than re-querying
        /// the engine for unlocked state a second time (SPEC-SLICE4-5.md TODO 11).
        ///
        /// [V-BIN] Verified by decompiling TaleWorlds.CampaignSystem.ViewModelCollection.WeaponCrafting.
        /// WeaponDesign.CraftingPieceVM and TaleWorlds.CampaignSystem.CampaignBehaviors.
        /// CraftingCampaignBehavior (ilspycmd against the game's v1.4.7 assemblies, code review
        /// FINDING 4): <c>CraftingPieceVM.PlayerHasPiece</c> is assigned verbatim from the
        /// <c>isOpened</c> constructor parameter, and <c>WeaponDesignVM</c> populates every
        /// <c>CraftingPieceVM</c> with
        /// <c>bool flag2 = _craftingBehavior.IsOpened(weaponDesignElement.CraftingPiece, _crafting.CurrentCraftingTemplate);</c>
        /// - i.e. <c>PlayerHasPiece</c> IS <c>ICraftingCampaignBehavior.IsOpened</c>, not an
        /// approximation of it. <c>CraftingCampaignBehavior.IsOpened</c> reads:
        /// <code>
        /// public bool IsOpened(CraftingPiece craftingPiece, CraftingTemplate craftingTemplate)
        /// {
        ///     if (!craftingPiece.IsGivenByDefault)
        ///     {
        ///         return _openedPartsDictionary[craftingTemplate].Contains(craftingPiece);
        ///     }
        ///     return true;
        /// }
        /// </code>
        /// so it unconditionally counts every <c>IsGivenByDefault</c> piece as unlocked in addition to
        /// player-researched ones - exactly "unlocked" per AC13, no narrower and no wider. The filter
        /// below is correct as written; no change was needed.
        /// </summary>
        private static PieceCatalogue BuildCatalogue(WeaponDesignVM vm)
        {
            var pieces = new List<DesignPiece>();
            if (vm.PieceLists != null)
            {
                foreach (CraftingPieceListVM pieceList in vm.PieceLists)
                {
                    if (pieceList?.Pieces == null)
                    {
                        continue;
                    }

                    foreach (CraftingPieceVM pieceVm in pieceList.Pieces)
                    {
                        if (pieceVm == null || pieceVm.IsEmpty || !pieceVm.PlayerHasPiece)
                        {
                            continue;
                        }

                        CraftingPiece piece = pieceVm.CraftingPiece?.CraftingPiece;
                        if (piece == null || string.IsNullOrEmpty(piece.StringId))
                        {
                            continue;
                        }

                        pieces.Add(new DesignPiece(piece.StringId, pieceVm.PieceType, pieceVm.Tier, AdvisorSearchPolicy.DefaultScalePercentage));
                    }
                }
            }

            return new PieceCatalogue(vm.CurrentCraftedWeaponTemplateId, pieces, AdvisorSearchPolicy.ScaleSteps);
        }

        /// <summary>
        /// The player's current selection, as the optimizer's starting point.
        ///
        /// Deliberately still reads the four public <c>Selected*Piece</c> VM properties rather than
        /// <c>crafting.CurrentWeaponDesign.UsedPieces</c> (considered while fixing the
        /// <c>CraftedItemObject</c> bug above): unlike <c>CraftedItemObject</c>, these properties are
        /// NOT the "only populated on Craft click" case - they update live as the player picks each
        /// piece, which is exactly why <see cref="ReadPieceIdentity"/> already reads them every frame
        /// for change detection with correct results. Both sources are ultimately backed by the same
        /// engine <c>_crafting</c> state, so switching would add another reflection-shaped dependency
        /// to this optimizer-seeding path for no correctness gain - not worth the risk on a future
        /// game update. Keep this the way it is unless a concrete case shows the two sources diverge.
        /// </summary>
        private static DesignCandidate BuildSeed(WeaponDesignVM vm)
        {
            var pieces = new List<DesignPiece>();
            AddSelected(pieces, vm.SelectedBladePiece);
            AddSelected(pieces, vm.SelectedGuardPiece);
            AddSelected(pieces, vm.SelectedHandlePiece);
            AddSelected(pieces, vm.SelectedPommelPiece);
            return new DesignCandidate(vm.CurrentCraftedWeaponTemplateId, pieces);
        }

        private static void AddSelected(List<DesignPiece> pieces, CraftingPieceVM pieceVm)
        {
            WeaponDesignElement element = pieceVm?.CraftingPiece;
            CraftingPiece piece = element?.CraftingPiece;
            if (piece == null || string.IsNullOrEmpty(piece.StringId))
            {
                return;
            }

            pieces.Add(new DesignPiece(piece.StringId, pieceVm.PieceType, pieceVm.Tier, element.ScalePercentage));
        }

        /// <summary>
        /// The scoring context the popup's toggle currently selects. The bound
        /// <see cref="AdvisorIsMounted"/> flag is the single source of truth for it, so every reader
        /// - the two scoring paths and the toggle's own caption - goes through here rather than
        /// repeating the ternary and risking one of them drifting.
        /// </summary>
        private MountedContext CurrentContext()
        {
            return AdvisorIsMounted ? MountedContext.Mounted : MountedContext.OnFoot;
        }

        private static BasicTooltipViewModel Hint(string text)
        {
            return new BasicTooltipViewModel(() => text);
        }

        private void LogFailureOnce(Exception ex)
        {
            if (_hasLoggedFailure)
            {
                return;
            }

            _hasLoggedFailure = true;
            SmithingLog.Line("WeaponDesignAdvisorMixin: disabling advisor after exception - " + ex.Message);
            // The crafting screen has no town-menu run around it - the batching optimisation does
            // not apply here, so flush immediately (see SmithingLog.Flush()).
            SmithingLog.Flush();
        }
    }

    /// <summary>
    /// One scored axis row in the advisor popup. A plain <see cref="ViewModel"/> (not the mixin
    /// itself) because <see cref="MBBindingList{T}"/> item templates bind against real view-model
    /// instances - one per <see cref="WeaponScore.Axes"/> entry, rebuilt whenever the design changes.
    ///
    /// The two hover commands are handled here rather than through a <c>..\..\</c> parent path in the
    /// prefab (which vanilla does use, but which silently breaks the moment the row's nesting depth
    /// changes) and are forwarded to the mixin through callbacks handed in at construction.
    /// </summary>
    public sealed class AdvisorAxisItemVM : ViewModel
    {
        private readonly Action<AdvisorAxisItemVM> _onHoverBegin;
        private readonly Action _onHoverEnd;
        private string _name;
        private string _valueText;
        private string _reason;
        private float _fillRatio;

        public AdvisorAxisItemVM(string name, float value, string reason, float fillRatio,
            Action<AdvisorAxisItemVM> onHoverBegin, Action onHoverEnd)
        {
            _name = name;
            _valueText = value.ToString("F1");
            _reason = reason;
            _fillRatio = fillRatio;
            _onHoverBegin = onHoverBegin;
            _onHoverEnd = onHoverEnd;
        }

        [DataSourceProperty]
        public string Name
        {
            get { return _name; }
            set
            {
                if (value != _name)
                {
                    _name = value;
                    OnPropertyChangedWithValue(value, "Name");
                }
            }
        }

        [DataSourceProperty]
        public string ValueText
        {
            get { return _valueText; }
            set
            {
                if (value != _valueText)
                {
                    _valueText = value;
                    OnPropertyChangedWithValue(value, "ValueText");
                }
            }
        }

        [DataSourceProperty]
        public string Reason
        {
            get { return _reason; }
            set
            {
                if (value != _reason)
                {
                    _reason = value;
                    OnPropertyChangedWithValue(value, "Reason");
                }
            }
        }

        /// <summary>How full this axis's bar is drawn, 0..1 - see the mixin's FillRatioFor.</summary>
        [DataSourceProperty]
        public float FillRatio
        {
            get { return _fillRatio; }
            set
            {
                if (value != _fillRatio)
                {
                    _fillRatio = value;
                    OnPropertyChangedWithValue(value, "FillRatio");
                }
            }
        }

        public void ExecuteHoverAxis()
        {
            if (_onHoverBegin != null)
            {
                _onHoverBegin(this);
            }
        }

        public void ExecuteUnhoverAxis()
        {
            if (_onHoverEnd != null)
            {
                _onHoverEnd();
            }
        }
    }
}
