using System.Collections.Generic;
using System.Linq;

namespace SmithingSuite.Advisor
{
    /// <summary>Which of the two things the advisor can say about a finished search.</summary>
    public enum AdvisorSuggestionKey
    {
        /// <summary>A verified, craftable design the player can act on.</summary>
        Suggestion = 0,

        /// <summary>Nothing legal was found - the refusal case (FINDING 1), never a design.</summary>
        NoImprovement = 1
    }

    /// <summary>One piece of a suggested design, as the player needs to read it back on the screen.</summary>
    public sealed class SuggestionPiece
    {
        public AdvisorPieceKind Kind { get; }
        public string PieceId { get; }
        public int ScalePercentage { get; }

        public SuggestionPiece(AdvisorPieceKind kind, string pieceId, int scalePercentage)
        {
            Kind = kind;
            PieceId = pieceId;
            ScalePercentage = scalePercentage;
        }
    }

    /// <summary>
    /// Everything the advisor's headline needs about the design currently on screen. Numbers and a
    /// context enum only - never a sentence, see <see cref="AdvisorPresentation"/>.
    /// </summary>
    public sealed class VerdictData
    {
        public MountedContext Context { get; }
        public float CombatTotal { get; }
        public float SaleValue { get; }

        public VerdictData(MountedContext context, float combatTotal, float saleValue)
        {
            Context = context;
            CombatTotal = combatTotal;
            SaleValue = saleValue;
        }
    }

    /// <summary>
    /// Everything the advisor's suggestion line needs about a finished search - including the
    /// refusal case, which is a <see cref="AdvisorSuggestionKey"/> value rather than a magic empty
    /// result, so a renderer cannot accidentally present an unverified design as a suggestion.
    /// </summary>
    public sealed class SuggestionData
    {
        public AdvisorSuggestionKey Key { get; }

        /// <summary>The suggested design's pieces, ordered by slot. Always empty for <see cref="AdvisorSuggestionKey.NoImprovement"/>.</summary>
        public IReadOnlyList<SuggestionPiece> Pieces { get; }

        public float CombatTotal { get; }
        public float SaleValue { get; }

        /// <summary>
        /// The search stopped early and this is only the best design found so far (AC7). Always
        /// false for <see cref="AdvisorSuggestionKey.NoImprovement"/>: that refusal is unconditional
        /// and carries no "best so far" qualifier, because there is no design to qualify.
        /// </summary>
        public bool BudgetExhausted { get; }

        private SuggestionData(AdvisorSuggestionKey key, IReadOnlyList<SuggestionPiece> pieces, float combatTotal, float saleValue, bool budgetExhausted)
        {
            Key = key;
            Pieces = pieces;
            CombatTotal = combatTotal;
            SaleValue = saleValue;
            BudgetExhausted = budgetExhausted;
        }

        public static SuggestionData ForDesign(IReadOnlyList<SuggestionPiece> pieces, float combatTotal, float saleValue, bool budgetExhausted)
        {
            return new SuggestionData(AdvisorSuggestionKey.Suggestion, pieces, combatTotal, saleValue, budgetExhausted);
        }

        public static SuggestionData NoImprovement()
        {
            return new SuggestionData(AdvisorSuggestionKey.NoImprovement, new SuggestionPiece[0], 0f, 0f, false);
        }
    }

    /// <summary>
    /// Pure presentation functions over engine-free Advisor types: deciding WHAT the crafting-advisor
    /// panel says about a <see cref="WeaponScore"/> or <see cref="OptimizationResult"/>. Moved out of
    /// <c>WeaponDesignAdvisorMixin</c> (a structural fix from architecture review) so these functions
    /// compile and are unit-tested on the host .NET SDK - the mixin itself only compiles inside the
    /// game, so nothing that lives there can be exercised by the test project.
    ///
    /// The decisions live here; the WORDS live in <c>SmithingSuite.UI.AdvisorTextRenderer</c>. That
    /// split is what makes the advisor localisable without giving up the tests: this layer may not
    /// reference <c>TextObject</c>, so it returns keys plus typed numbers and the UI boundary renders
    /// them. The refusal guard below is therefore also strictly stronger than it was as prose - a
    /// caller cannot mistake <see cref="AdvisorSuggestionKey.NoImprovement"/> for a design.
    /// </summary>
    public static class AdvisorPresentation
    {
        public static VerdictData BuildVerdict(WeaponScore score, MountedContext context)
        {
            return new VerdictData(context, score.CombatTotal, score.SaleValue);
        }

        public static SuggestionData DescribeSuggestion(OptimizationResult result)
        {
            // HasLegalResult is the ONLY thing that makes result.Best trustworthy (FINDING 1) - an
            // unverified seed must never be shown as "Suggested", so this checks it before Score at
            // all, rather than trusting a non-null Score to imply legality.
            if (result == null || !result.HasLegalResult || result.Score == null)
            {
                return SuggestionData.NoImprovement();
            }

            var pieces = result.Best.Pieces
                .OrderBy(p => p.PieceType)
                .Select(p => new SuggestionPiece(PieceKindFor(p.PieceType), p.PieceId, p.ScalePercentage))
                .ToList();

            return SuggestionData.ForDesign(pieces, result.Score.CombatTotal, result.Score.SaleValue, result.BudgetExhausted);
        }

        /// <summary>
        /// Maps a vanilla <c>PieceTypes</c> ordinal onto <see cref="AdvisorPieceKind"/>. Kept as an
        /// explicit mapping rather than an unchecked cast so an ordinal outside the four vanilla
        /// slots lands on <see cref="AdvisorPieceKind.Unknown"/> instead of an undefined enum value.
        /// </summary>
        public static AdvisorPieceKind PieceKindFor(int pieceType)
        {
            switch (pieceType)
            {
                case 0: return AdvisorPieceKind.Blade;
                case 1: return AdvisorPieceKind.Guard;
                case 2: return AdvisorPieceKind.Handle;
                case 3: return AdvisorPieceKind.Pommel;
                default: return AdvisorPieceKind.Unknown;
            }
        }
    }
}
