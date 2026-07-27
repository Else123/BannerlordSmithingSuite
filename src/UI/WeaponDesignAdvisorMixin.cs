using System;
using System.Collections.Generic;
using System.Linq;
using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.ViewModels;
using SmithingSuite.Advisor;
using SmithingSuite.Integration;
using TaleWorlds.CampaignSystem.ViewModelCollection.WeaponCrafting.WeaponDesign;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace SmithingSuite.UI
{
    /// <summary>
    /// Inline crafting advisor (SPEC-SLICE4-5.md Slice 5, TODO 11): a UIExtenderEx view-model mixin
    /// on vanilla's <see cref="WeaponDesignVM"/> that scores the design the player is currently
    /// building and, on request, searches for the best design within the same template.
    ///
    /// Two very different cost budgets share this class, per the spec's performance constraint:
    /// <list type="bullet">
    /// <item><see cref="OnRefresh"/> runs on every <c>WeaponDesignVM.RefreshItem()</c> call, which
    /// <c>GauntletCraftingScreen</c> may invoke every frame - it must stay cheap (read already
    /// engine-computed numbers, score them, short-circuit if nothing changed) and must never touch
    /// <c>Crafting.GenerateItem</c> or <see cref="DesignOptimizer"/>.</item>
    /// <item><see cref="ExecuteSuggestBest"/> runs only on the panel's button click and is allowed to
    /// be expensive - it is the only place in this class permitted to run the optimizer.</item>
    /// </list>
    ///
    /// Fail-safe by construction (AC15): every public entry point is wrapped in try/catch. Any
    /// exception hides the panel (<see cref="AdvisorIsVisible"/> goes false) and is logged at most
    /// once, mirroring <see cref="EngineDesignEvaluator"/>'s own once-per-run failure logging - the
    /// vanilla crafting screen must keep working even if the game's <c>WeaponDesignVM</c> shape
    /// changes under this mixin in a future update.
    /// </summary>
    [ViewModelMixin("RefreshItem")]
    public sealed class WeaponDesignAdvisorMixin : BaseViewModelMixin<WeaponDesignVM>
    {
        // The engine exposes WeaponDesignElement.ScalePercentage as an arbitrary int with no built-in
        // min/max (TaleWorlds.Core.WeaponDesignElement) - vanilla's own crafting UI never exposes a
        // scale slider at all, so there is no existing convention to mirror here. This range is a
        // labelled OPINION, exactly like ScoreWeights (SPEC-SLICE4-5.md "Honest limitation"): wide
        // enough that a scaled winner can actually show up (AC6), narrow enough that the suggested
        // pieces still look like the weapon the player chose.
        private static readonly int[] ScaleSteps = { 80, 90, 100, 110, 120 };

        private const int DefaultCatalogueScale = 100;

        // Logged once per run (not once per call) - RefreshItem can fire every frame, so a broken
        // mixin must not spam the log the same way EngineDesignEvaluator's own failures do not.
        private static bool _hasLoggedFailure;

        private string _lastDesignKey;

        private bool _advisorIsVisible;
        private string _advisorVerdictText = string.Empty;
        private MBBindingList<AdvisorAxisItemVM> _advisorAxes;
        private string _advisorSuggestionText = string.Empty;
        private bool _advisorHasSuggestion;
        private bool _advisorIsBusy;
        private bool _advisorIsSuggestButtonEnabled = true;
        private bool _advisorIsMounted;
        private string _advisorContextButtonText;

        public WeaponDesignAdvisorMixin(WeaponDesignVM vm) : base(vm)
        {
            _advisorAxes = new MBBindingList<AdvisorAxisItemVM>();
            _advisorContextButtonText = ContextButtonLabel(mounted: false);
        }

        [DataSourceProperty]
        public bool AdvisorIsVisible
        {
            get { return _advisorIsVisible; }
            set
            {
                if (value != _advisorIsVisible)
                {
                    _advisorIsVisible = value;
                    OnPropertyChanged();
                }
            }
        }

        [DataSourceProperty]
        public string AdvisorVerdictText
        {
            get { return _advisorVerdictText; }
            set
            {
                if (value != _advisorVerdictText)
                {
                    _advisorVerdictText = value;
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
        public string AdvisorSuggestionText
        {
            get { return _advisorSuggestionText; }
            set
            {
                if (value != _advisorSuggestionText)
                {
                    _advisorSuggestionText = value;
                    OnPropertyChanged();
                }
            }
        }

        [DataSourceProperty]
        public bool AdvisorHasSuggestion
        {
            get { return _advisorHasSuggestion; }
            set
            {
                if (value != _advisorHasSuggestion)
                {
                    _advisorHasSuggestion = value;
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
                    // Derived, not independent state - the panel binds IsEnabled to this rather than
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

        public void ExecuteToggleMountedContext()
        {
            try
            {
                AdvisorIsMounted = !AdvisorIsMounted;
                AdvisorContextButtonText = ContextButtonLabel(AdvisorIsMounted);
                // Context flips ScoreWeights.ForContext's axis weights - force a rescore even though
                // the design key itself has not changed, so the toggle takes visible effect
                // immediately instead of waiting for the next incidental RefreshItem tick. Still just
                // the cheap read-and-score path, never the optimizer.
                RefreshFromCurrentItem(force: true);
            }
            catch (Exception ex)
            {
                AdvisorIsVisible = false;
                LogFailureOnce(ex);
            }
        }

        /// <summary>
        /// The one entry point allowed to run <see cref="DesignOptimizer"/> - explicit user action
        /// only, never from <see cref="OnRefresh"/> (SPEC-SLICE4-5.md TODO 11). Builds the search
        /// universe from what <see cref="WeaponDesignVM.PieceLists"/> already exposes rather than
        /// re-querying <c>ICraftingCampaignBehavior.IsOpened</c> itself, and guards against re-entry
        /// so a double-click cannot start a second search while one is already running.
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
                WeaponDesignVM vm = ViewModel;
                if (vm == null)
                {
                    return;
                }

                PieceCatalogue catalogue = BuildCatalogue(vm);
                if (catalogue.AvailablePieces.Count == 0)
                {
                    AdvisorSuggestionText = "No unlocked pieces are available to search for this template.";
                    AdvisorHasSuggestion = true;
                    return;
                }

                DesignCandidate seed = BuildSeed(vm);
                MountedContext context = AdvisorIsMounted ? MountedContext.Mounted : MountedContext.OnFoot;
                ScoreWeights weights = ScoreWeights.ForContext(context);
                var options = new OptimizerOptions(SmithingSuiteConfig.AdvisorEvaluationBudget, context, weights, vm.MaxDifficulty);
                var optimizer = new DesignOptimizer(new EngineDesignEvaluator());
                OptimizationResult result = optimizer.Optimize(seed, catalogue, options);

                AdvisorSuggestionText = DescribeSuggestion(result);
                AdvisorHasSuggestion = true;
            }
            catch (Exception ex)
            {
                AdvisorIsVisible = false;
                LogFailureOnce(ex);
            }
            finally
            {
                AdvisorIsBusy = false;
            }
        }

        /// <summary>
        /// The shared cheap path behind both <see cref="OnRefresh"/> (every incidental tick) and
        /// <see cref="ExecuteToggleMountedContext"/> (an explicit but still-cheap re-score): reads
        /// <see cref="WeaponDesignVM.CraftedItemObject"/>'s already-engine-computed stats and scores
        /// them via <see cref="WeaponScorer"/>. Never calls <c>Crafting.GenerateItem</c> or
        /// <see cref="DesignOptimizer"/> - those are reserved for <see cref="ExecuteSuggestBest"/>.
        ///
        /// <paramref name="force"/> = false is the per-frame path: if the four selected pieces (id +
        /// scale) are identical to last time, this returns immediately without recomputing anything
        /// or raising a single property-changed event - the difference between a smooth crafting
        /// screen and a per-frame stutter.
        /// </summary>
        private void RefreshFromCurrentItem(bool force)
        {
            try
            {
                WeaponDesignVM vm = ViewModel;
                if (vm == null)
                {
                    AdvisorIsVisible = false;
                    return;
                }

                string key = BuildDesignKey(vm);
                if (!force && key == _lastDesignKey)
                {
                    return;
                }
                _lastDesignKey = key;

                WeaponStats stats = ToWeaponStats(vm.CraftedItemObject);
                if (stats == null)
                {
                    AdvisorIsVisible = false;
                    return;
                }

                MountedContext context = AdvisorIsMounted ? MountedContext.Mounted : MountedContext.OnFoot;
                ScoreWeights weights = ScoreWeights.ForContext(context);
                WeaponScore score = WeaponScorer.Score(stats, context, weights);

                UpdateAxes(score);
                AdvisorVerdictText = BuildVerdict(score, context);
                // The design (or context) changed - any previously computed suggestion no longer
                // describes what's on screen, so it is withdrawn rather than left stale.
                AdvisorSuggestionText = string.Empty;
                AdvisorHasSuggestion = false;
                AdvisorIsVisible = true;
            }
            catch (Exception ex)
            {
                AdvisorIsVisible = false;
                LogFailureOnce(ex);
            }
        }

        /// <summary>Reads the item's engine-computed weapon stats without calling into the engine again.</summary>
        private static WeaponStats ToWeaponStats(ItemObject item)
        {
            WeaponComponentData weapon = item?.WeaponComponent?.PrimaryWeapon;
            return weapon == null ? null : EngineDesignEvaluator.ToWeaponStats(weapon, item);
        }

        private void UpdateAxes(WeaponScore score)
        {
            if (AdvisorAxes == null)
            {
                AdvisorAxes = new MBBindingList<AdvisorAxisItemVM>();
            }

            AdvisorAxes.Clear();
            foreach (ScoreAxis axis in score.Axes)
            {
                AdvisorAxes.Add(new AdvisorAxisItemVM(axis.Name, axis.Value, axis.Reason));
            }
        }

        private static string BuildVerdict(WeaponScore score, MountedContext context)
        {
            string contextLabel = context == MountedContext.Mounted ? "mounted" : "on foot";
            return "Combat score (" + contextLabel + "): " + score.CombatTotal.ToString("F0")
                + " - sale value " + score.SaleValue + " gold. Axes below explain why.";
        }

        /// <summary>
        /// A cheap identity for "has the design changed": the four selected pieces' ids and scales.
        /// Deliberately NOT <c>CraftedItemObject</c>'s own identity/StringId - GauntletCraftingScreen
        /// regenerates that item on every refresh regardless of whether the design actually changed,
        /// so keying on it would defeat the short-circuit entirely.
        /// </summary>
        private static string BuildDesignKey(WeaponDesignVM vm)
        {
            return PieceKey(vm.SelectedBladePiece) + "|" + PieceKey(vm.SelectedGuardPiece) + "|"
                + PieceKey(vm.SelectedHandlePiece) + "|" + PieceKey(vm.SelectedPommelPiece);
        }

        private static string PieceKey(CraftingPieceVM pieceVm)
        {
            WeaponDesignElement element = pieceVm?.CraftingPiece;
            CraftingPiece piece = element?.CraftingPiece;
            return piece == null ? "-" : piece.StringId + ":" + element.ScalePercentage;
        }

        /// <summary>
        /// The unlocked universe the optimizer is allowed to search: every piece across
        /// <see cref="WeaponDesignVM.PieceLists"/> whose <c>PlayerHasPiece</c> flag is already true -
        /// that flag was set from <c>ICraftingCampaignBehavior.IsOpened</c> when the VM built the
        /// list, so reading it here is "using what the VM already exposes" rather than re-querying
        /// the engine for unlocked state a second time (SPEC-SLICE4-5.md TODO 11).
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

                        pieces.Add(new DesignPiece(piece.StringId, pieceVm.PieceType, pieceVm.Tier, DefaultCatalogueScale));
                    }
                }
            }

            return new PieceCatalogue(vm.CurrentCraftedWeaponTemplateId, pieces, ScaleSteps);
        }

        /// <summary>The player's current selection, as the optimizer's starting point.</summary>
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

        private static string DescribeSuggestion(OptimizationResult result)
        {
            if (result?.Score == null)
            {
                return "No craftable design was found within the current smithing skill and unlocked parts.";
            }

            string pieceList = string.Join(", ", result.Best.Pieces
                .OrderBy(p => p.PieceType)
                .Select(p => PieceTypeName(p.PieceType) + " " + p.PieceId + " @" + p.ScalePercentage + "%"));

            string budgetNote = result.BudgetExhausted
                ? " (search budget exhausted - best found so far)"
                : string.Empty;

            return "Suggested: " + pieceList + " -> combat score " + result.Score.CombatTotal.ToString("F0")
                + ", sale value " + result.Score.SaleValue + " gold" + budgetNote + ".";
        }

        private static string PieceTypeName(int pieceType)
        {
            // Mirrors TaleWorlds.Core.CraftingPiece.PieceTypes (Blade=0, Guard=1, Handle=2, Pommel=3),
            // the same ordinals DesignOptimizer and EngineDesignEvaluator already assume.
            switch (pieceType)
            {
                case 0: return "Blade";
                case 1: return "Guard";
                case 2: return "Handle";
                case 3: return "Pommel";
                default: return "Piece";
            }
        }

        private static string ContextButtonLabel(bool mounted)
        {
            return mounted ? "Context: Mounted" : "Context: On Foot";
        }

        private static void LogFailureOnce(Exception ex)
        {
            if (_hasLoggedFailure)
            {
                return;
            }

            _hasLoggedFailure = true;
            SmithingLog.Line("WeaponDesignAdvisorMixin: disabling advisor panel after exception - " + ex.Message);
        }
    }

    /// <summary>
    /// One row in the advisor panel's axis list. A plain <see cref="ViewModel"/> (not the mixin
    /// itself) because <see cref="MBBindingList{T}"/> item templates bind against real view-model
    /// instances - one per <see cref="WeaponScore.Axes"/> entry, rebuilt whenever the design changes.
    /// </summary>
    public sealed class AdvisorAxisItemVM : ViewModel
    {
        private string _name;
        private string _valueText;
        private string _reason;

        public AdvisorAxisItemVM(string name, float value, string reason)
        {
            _name = name;
            _valueText = value.ToString("F1");
            _reason = reason;
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
    }
}
