using System;
using System.Collections.Generic;
using System.Linq;

namespace SmithingSuite.Advisor
{
    /// <summary>
    /// The unlocked universe the optimizer is allowed to search within: the pieces the smith has
    /// actually unlocked for this template, and the scale steps to try. The caller has already
    /// filtered <see cref="AvailablePieces"/> to unlocked parts - the optimizer treats this list as
    /// the ONLY legal universe (see SPEC-SLICE4-5.md TODO 8, AC3) and must never re-derive a wider
    /// one from the seed or anywhere else.
    /// </summary>
    public sealed class PieceCatalogue
    {
        public string TemplateId { get; }
        public IReadOnlyList<DesignPiece> AvailablePieces { get; }
        public IReadOnlyList<int> ScaleSteps { get; }

        public PieceCatalogue(string templateId, IReadOnlyList<DesignPiece> availablePieces, IReadOnlyList<int> scaleSteps)
        {
            TemplateId = templateId;
            AvailablePieces = availablePieces ?? new DesignPiece[0];
            ScaleSteps = scaleSteps ?? new int[0];
        }
    }

    /// <summary>Bounds and context for one <see cref="DesignOptimizer.Optimize"/> call.</summary>
    public sealed class OptimizerOptions
    {
        /// <summary>
        /// Hard cap on <see cref="IDesignEvaluator.Evaluate"/> calls - Evaluate is the expensive
        /// engine call in the real implementation, so this is what keeps the search from ever
        /// blocking the UI thread for a perceptible time (AC7).
        /// </summary>
        public int EvaluationBudget { get; }

        public MountedContext Context { get; }
        public ScoreWeights Weights { get; }

        /// <summary>From <c>SmithingModel.CalculateWeaponDesignDifficulty</c> vs the smith's skill (AC4).</summary>
        public int MaxDifficulty { get; }

        public OptimizerOptions(int evaluationBudget, MountedContext context, ScoreWeights weights, int maxDifficulty)
        {
            EvaluationBudget = evaluationBudget;
            Context = context;
            Weights = weights;
            MaxDifficulty = maxDifficulty;
        }
    }

    /// <summary>What one <see cref="DesignOptimizer.Optimize"/> call produced.</summary>
    public sealed class OptimizationResult
    {
        /// <summary>Always legal (AC4) and always within the catalogue (AC3) - never null (see class doc).</summary>
        public DesignCandidate Best { get; }

        public WeaponScore Score { get; }

        /// <summary>Every <see cref="IDesignEvaluator.Evaluate"/> call made, cache hits excluded (AC7).</summary>
        public int EvaluationsUsed { get; }

        /// <summary>True if the search stopped because the budget ran out, not because it converged.</summary>
        public bool BudgetExhausted { get; }

        public OptimizationResult(DesignCandidate best, WeaponScore score, int evaluationsUsed, bool budgetExhausted)
        {
            Best = best;
            Score = score;
            EvaluationsUsed = evaluationsUsed;
            BudgetExhausted = budgetExhausted;
        }
    }

    /// <summary>
    /// Bounded staged search over a <see cref="PieceCatalogue"/> for the weapon design with the
    /// highest <see cref="WeaponScore.CombatTotal"/> (SPEC-SLICE4-5.md TODO 8). Four slots times
    /// dozens of pieces times scale steps is combinatorially far too large to brute-force between
    /// keystrokes, so this optimises one slot at a time - blade first because it dominates the
    /// design's identity and score, then handle, guard, pommel - then hill-climbs
    /// <c>ScalePercentage</c> per piece, always keeping the rest of the design fixed while one
    /// dimension is searched.
    ///
    /// Determinism is a contract here (AC8), not an accident: slot order is fixed, every stage
    /// walks the catalogue's own order verbatim, acceptance is strict-improvement only, there is no
    /// randomness anywhere, and nothing below reads output from a <see cref="Dictionary{TKey,TValue}"/>
    /// or <see cref="HashSet{T}"/> enumeration order - both are used only for O(1) lookups keyed by
    /// candidate identity. Same inputs must always walk the same path and land on the same design.
    /// </summary>
    public sealed class DesignOptimizer
    {
        // Mirrors vanilla TaleWorlds.Core.PieceTypes ordinals (see DesignPiece.PieceType's doc).
        private const int PieceTypeBlade = 0;
        private const int PieceTypeGuard = 1;
        private const int PieceTypeHandle = 2;
        private const int PieceTypePommel = 3;

        private const int DefaultScalePercentage = 100;

        // The SEARCH order (blade dominant, then handle, then guard/pommel - SPEC-SLICE4-5.md
        // "Ansatz + Architektur / Slice 4") is a policy choice independent of the PieceTypes
        // ordinals above; do not "simplify" this to numeric order.
        private static readonly int[] SlotSearchOrder = { PieceTypeBlade, PieceTypeHandle, PieceTypeGuard, PieceTypePommel };

        private readonly IDesignEvaluator _evaluator;

        public DesignOptimizer(IDesignEvaluator evaluator)
        {
            _evaluator = evaluator;
        }

        public OptimizationResult Optimize(DesignCandidate seed, PieceCatalogue catalogue, OptimizerOptions options)
        {
            if (seed == null)
            {
                throw new ArgumentNullException(nameof(seed));
            }
            if (catalogue == null)
            {
                throw new ArgumentNullException(nameof(catalogue));
            }
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            var state = new SearchState(_evaluator, catalogue, options);

            var current = seed;
            var seedEvaluation = state.TryEvaluate(current);
            state.ConsiderForBestLegal(current, seedEvaluation);

            // Only proceed into the staged search if the budget survived even the seed's own
            // evaluation - with a budget that small there is nothing left to search with anyway.
            if (seedEvaluation != null)
            {
                foreach (var slotType in SlotSearchOrder)
                {
                    if (state.BudgetExhausted)
                    {
                        break;
                    }
                    current = OptimizeSlot(current, slotType, catalogue, state);
                }

                if (!state.BudgetExhausted)
                {
                    current = OptimizeScale(current, catalogue, state);
                }
            }

            return state.BuildResult(seed);
        }

        /// <summary>
        /// Tries every catalogue piece of <paramref name="slotType"/>, keeping every other slot
        /// fixed at its current value (rule 2). Deterministic: catalogue order decides both
        /// evaluation order and, on a score tie, which piece wins - the first-seen candidate is
        /// never displaced by a later one that merely matches it (AC8).
        /// </summary>
        private static DesignCandidate OptimizeSlot(DesignCandidate current, int slotType, PieceCatalogue catalogue, SearchState state)
        {
            var slotCandidates = catalogue.AvailablePieces.Where(p => p.PieceType == slotType).ToList();
            if (slotCandidates.Count == 0)
            {
                return current;
            }

            var existingPiece = CurrentPiece(current, slotType);
            var currentScale = existingPiece != null ? existingPiece.ScalePercentage : DefaultScalePercentage;

            // A slot whose current piece is not itself part of the legal catalogue (e.g. a seed
            // carrying a piece the player has not actually unlocked) must be swapped out regardless
            // of score - AC3 forbids ever RETURNING such a piece, not merely deprioritising it.
            var mustReplace = existingPiece == null || slotCandidates.All(p => p.PieceId != existingPiece.PieceId);

            var baselineEvaluation = state.TryEvaluate(current);
            var baselineScore = baselineEvaluation != null ? state.CombatScoreOf(baselineEvaluation) : float.NegativeInfinity;

            DesignCandidate bestTrial = null;
            var bestTrialScore = float.NegativeInfinity;

            foreach (var candidatePiece in slotCandidates)
            {
                if (state.BudgetExhausted)
                {
                    break;
                }

                var trial = WithPiece(current, slotType, candidatePiece.PieceId, candidatePiece.PieceTier, currentScale);
                var evaluation = state.TryEvaluate(trial);
                if (evaluation == null)
                {
                    // Budget ran out mid-stage (rule 5) - stop trying further pieces for this slot.
                    break;
                }

                // Every evaluated candidate is a chance to improve the final answer, whether or not
                // it ends up winning this slot's local comparison below.
                state.ConsiderForBestLegal(trial, evaluation);

                var score = state.CombatScoreOf(evaluation);
                if (bestTrial == null || score > bestTrialScore)
                {
                    bestTrial = trial;
                    bestTrialScore = score;
                }
            }

            if (bestTrial == null)
            {
                return current;
            }

            // Strict improvement only (rule 4) - unless the current piece must be replaced anyway.
            return (bestTrialScore > baselineScore || mustReplace) ? bestTrial : current;
        }

        /// <summary>
        /// Hill-climbs <c>ScalePercentage</c> per piece over <see cref="PieceCatalogue.ScaleSteps"/>
        /// (rule 3), one slot at a time, in the same fixed slot order as <see cref="OptimizeSlot"/>.
        /// </summary>
        private static DesignCandidate OptimizeScale(DesignCandidate current, PieceCatalogue catalogue, SearchState state)
        {
            if (catalogue.ScaleSteps.Count == 0)
            {
                return current;
            }

            foreach (var slotType in SlotSearchOrder)
            {
                if (state.BudgetExhausted)
                {
                    break;
                }

                var piece = CurrentPiece(current, slotType);
                if (piece == null)
                {
                    continue;
                }

                var baselineEvaluation = state.TryEvaluate(current);
                var baselineScore = baselineEvaluation != null ? state.CombatScoreOf(baselineEvaluation) : float.NegativeInfinity;

                DesignCandidate bestTrial = null;
                var bestTrialScore = float.NegativeInfinity;

                foreach (var scale in catalogue.ScaleSteps)
                {
                    if (state.BudgetExhausted)
                    {
                        break;
                    }

                    var trial = WithPiece(current, slotType, piece.PieceId, piece.PieceTier, scale);
                    var evaluation = state.TryEvaluate(trial);
                    if (evaluation == null)
                    {
                        break;
                    }

                    state.ConsiderForBestLegal(trial, evaluation);

                    var score = state.CombatScoreOf(evaluation);
                    if (bestTrial == null || score > bestTrialScore)
                    {
                        bestTrial = trial;
                        bestTrialScore = score;
                    }
                }

                // Strict improvement only (rule 4) - scale never "wins" a tie against the seed's own
                // value, so AC6 is a genuine search result, not decoration.
                if (bestTrial != null && bestTrialScore > baselineScore)
                {
                    current = bestTrial;
                }
            }

            return current;
        }

        private static DesignPiece CurrentPiece(DesignCandidate candidate, int slotType)
        {
            return candidate.Pieces.FirstOrDefault(p => p.PieceType == slotType);
        }

        /// <summary>Returns a new candidate with the given slot's piece replaced (or added, if absent).</summary>
        private static DesignCandidate WithPiece(DesignCandidate source, int slotType, string pieceId, int pieceTier, int scalePercentage)
        {
            var pieces = new List<DesignPiece>();
            var replaced = false;
            foreach (var piece in source.Pieces)
            {
                if (piece.PieceType == slotType)
                {
                    pieces.Add(new DesignPiece(pieceId, slotType, pieceTier, scalePercentage));
                    replaced = true;
                }
                else
                {
                    pieces.Add(piece);
                }
            }
            if (!replaced)
            {
                pieces.Add(new DesignPiece(pieceId, slotType, pieceTier, scalePercentage));
            }
            return new DesignCandidate(source.TemplateId, pieces);
        }

        private static string DesignKey(DesignCandidate candidate)
        {
            // Order by PieceType (rather than list order) so two candidates that describe the same
            // design - just with pieces built up in a different sequence - hit the same cache entry.
            var parts = candidate.Pieces
                .OrderBy(p => p.PieceType)
                .Select(p => $"{p.PieceType}:{p.PieceId}:{p.PieceTier}:{p.ScalePercentage}");
            return candidate.TemplateId + "|" + string.Join(";", parts);
        }

        /// <summary>
        /// Mutable bookkeeping threaded through one <see cref="Optimize"/> call: the evaluation
        /// cache/budget and the single best LEGAL candidate seen so far. Kept as its own type
        /// (rather than ref parameters or ad-hoc closures) so <see cref="OptimizeSlot"/> and
        /// <see cref="OptimizeScale"/> read as the staged policy, not as budget plumbing.
        /// </summary>
        private sealed class SearchState
        {
            private readonly IDesignEvaluator _evaluator;
            private readonly OptimizerOptions _options;
            private readonly HashSet<string> _availablePieceIds;
            private readonly Dictionary<string, DesignEvaluation> _cache = new Dictionary<string, DesignEvaluation>();

            private DesignCandidate _bestLegalCandidate;
            private DesignEvaluation _bestLegalEvaluation;
            private float _bestLegalScore = float.NegativeInfinity;

            public int EvaluationsUsed { get; private set; }
            public bool BudgetExhausted { get; private set; }

            public SearchState(IDesignEvaluator evaluator, PieceCatalogue catalogue, OptimizerOptions options)
            {
                _evaluator = evaluator;
                _options = options;
                _availablePieceIds = new HashSet<string>(catalogue.AvailablePieces.Select(p => p.PieceId));
            }

            /// <summary>
            /// Evaluates a candidate, or returns the cached result for an identical one so the fixed
            /// budget is spent only on genuinely new designs (never twice on the same candidate
            /// identity). Returns null once the budget is exhausted - callers must stop immediately
            /// (rule 5), not substitute a fallback value.
            /// </summary>
            public DesignEvaluation TryEvaluate(DesignCandidate candidate)
            {
                var key = DesignKey(candidate);
                DesignEvaluation cached;
                if (_cache.TryGetValue(key, out cached))
                {
                    return cached;
                }

                if (EvaluationsUsed >= _options.EvaluationBudget)
                {
                    BudgetExhausted = true;
                    return null;
                }

                var evaluation = _evaluator.Evaluate(candidate);
                EvaluationsUsed++;
                _cache[key] = evaluation;
                return evaluation;
            }

            public float CombatScoreOf(DesignEvaluation evaluation)
            {
                return WeaponScorer.Score(evaluation.Stats, _options.Context, _options.Weights).CombatTotal;
            }

            /// <summary>
            /// A candidate is legal only if BOTH the smith can craft it (difficulty within skill,
            /// AC4) AND every one of its pieces is drawn from the catalogue's unlocked universe
            /// (AC3) - a design that reuses a piece id the catalogue never offered is not a real
            /// option, even if the evaluator happens to report a low difficulty for it.
            /// </summary>
            public bool IsLegal(DesignCandidate candidate, DesignEvaluation evaluation)
            {
                return evaluation.Difficulty <= _options.MaxDifficulty
                    && candidate.Pieces.All(p => _availablePieceIds.Contains(p.PieceId));
            }

            public void ConsiderForBestLegal(DesignCandidate candidate, DesignEvaluation evaluation)
            {
                if (evaluation == null || !IsLegal(candidate, evaluation))
                {
                    return;
                }

                var score = CombatScoreOf(evaluation);
                if (_bestLegalCandidate == null || score > _bestLegalScore)
                {
                    _bestLegalCandidate = candidate;
                    _bestLegalEvaluation = evaluation;
                    _bestLegalScore = score;
                }
            }

            /// <summary>
            /// Never null, never throws (hard rule): the best legal design found anywhere during the
            /// search, or - if none was ever legal, e.g. even the seed is illegal and no catalogue
            /// swap fixed that - the seed itself, scored honestly if it could be evaluated at all.
            /// </summary>
            public OptimizationResult BuildResult(DesignCandidate seed)
            {
                if (_bestLegalCandidate != null)
                {
                    var score = WeaponScorer.Score(_bestLegalEvaluation.Stats, _options.Context, _options.Weights);
                    return new OptimizationResult(_bestLegalCandidate, score, EvaluationsUsed, BudgetExhausted);
                }

                var seedEvaluation = TryEvaluate(seed);
                var seedScore = seedEvaluation != null
                    ? WeaponScorer.Score(seedEvaluation.Stats, _options.Context, _options.Weights)
                    : null;
                return new OptimizationResult(seed, seedScore, EvaluationsUsed, BudgetExhausted);
            }
        }
    }
}
