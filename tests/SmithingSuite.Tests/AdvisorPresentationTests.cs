using System.Collections.Generic;
using System.Linq;
using SmithingSuite.Advisor;
using Xunit;

namespace SmithingSuite.Tests
{
    /// <summary>
    /// Tests for <see cref="AdvisorPresentation"/> - the pure presentation decisions moved out of
    /// <c>WeaponDesignAdvisorMixin</c> (architecture review structural fix) so they compile and run
    /// on the host .NET SDK. <see cref="DescribeSuggestion_WhenResultHasNoLegalResult_RefusesToPresentIt"/>
    /// covers the review-found guard - refusing to present an unverified/illegal result as a
    /// suggestion - that had NO test before that move.
    ///
    /// Since the advisor was made localisable these functions return structured data rather than
    /// English sentences, so the assertions moved onto <see cref="AdvisorSuggestionKey"/>,
    /// <see cref="AdvisorPieceKind"/> and numbers. That is strictly stronger: the refusal case is now
    /// checked as "the result is flagged NoImprovement AND carries no piece data at all" instead of
    /// "the produced sentence happens to read like a refusal", so no wording change - and no
    /// translation - can make a leaked illegal design pass as refused.
    /// </summary>
    public class AdvisorPresentationTests
    {
        private const int BladeType = 0;
        private const int GuardType = 1;
        private const int HandleType = 2;
        private const int PommelType = 3;

        private static DesignCandidate Candidate(string templateId, params DesignPiece[] pieces)
            => new DesignCandidate(templateId, pieces);

        private static WeaponScore Score(float combatTotal = 42f, float saleValue = 300f)
        {
            var axes = new List<ScoreAxis>
            {
                new ScoreAxis(AdvisorAxisKind.Handling, 30f, AxisReason.Handling(30f)),
            };
            return new WeaponScore(axes, combatTotal, saleValue);
        }

        [Fact]
        public void DescribeSuggestion_WhenResultHasNoLegalResult_RefusesToPresentIt()
        {
            // Mirrors the seed-only outcome DesignOptimizer.BuildResult produces when nothing legal
            // was ever found (FINDING 1) - Best is non-null (hard rule) but HasLegalResult is false,
            // so this must never be shown to the player as a suggestion.
            var seed = Candidate(
                "one-handed-sword",
                new DesignPiece("blade-basic", BladeType, pieceTier: 1, scalePercentage: 100));
            var result = new OptimizationResult(seed, hasLegalResult: false, score: null, evaluationsUsed: 3, budgetExhausted: false);

            var suggestion = AdvisorPresentation.DescribeSuggestion(result);

            Assert.Equal(AdvisorSuggestionKey.NoImprovement, suggestion.Key);
            // Not one piece of the unverified seed may leak into what a renderer can display.
            Assert.Empty(suggestion.Pieces);
            Assert.False(suggestion.BudgetExhausted);
        }

        [Fact]
        public void DescribeSuggestion_WhenResultIsNull_RefusesToPresentIt()
        {
            var suggestion = AdvisorPresentation.DescribeSuggestion(null);

            Assert.Equal(AdvisorSuggestionKey.NoImprovement, suggestion.Key);
            Assert.Empty(suggestion.Pieces);
        }

        [Fact]
        public void DescribeSuggestion_WhenLegalButScoreIsMissing_RefusesToPresentIt()
        {
            // The other half of the guard: a result flagged legal but carrying no score (the
            // zero-budget path in DesignOptimizer.BuildResult) has no numbers to show, so it must be
            // refused too rather than presented with silently invented zeroes.
            var best = Candidate(
                "one-handed-sword",
                new DesignPiece("blade-basic", BladeType, pieceTier: 1, scalePercentage: 100));
            var result = new OptimizationResult(best, hasLegalResult: true, score: null, evaluationsUsed: 0, budgetExhausted: true);

            var suggestion = AdvisorPresentation.DescribeSuggestion(result);

            Assert.Equal(AdvisorSuggestionKey.NoImprovement, suggestion.Key);
            Assert.Empty(suggestion.Pieces);
        }

        [Fact]
        public void DescribeSuggestion_LabelsABudgetExhaustedResultAsIncomplete()
        {
            var best = Candidate(
                "one-handed-sword",
                new DesignPiece("blade-decent", BladeType, pieceTier: 2, scalePercentage: 110));
            var result = new OptimizationResult(best, hasLegalResult: true, score: Score(), evaluationsUsed: 200, budgetExhausted: true);

            var suggestion = AdvisorPresentation.DescribeSuggestion(result);

            Assert.Equal(AdvisorSuggestionKey.Suggestion, suggestion.Key);
            Assert.True(suggestion.BudgetExhausted);
        }

        [Fact]
        public void DescribeSuggestion_WhenLegalAndBudgetNotExhausted_OmitsTheExhaustionNote()
        {
            var best = Candidate(
                "one-handed-sword",
                new DesignPiece("blade-decent", BladeType, pieceTier: 2, scalePercentage: 110));
            var result = new OptimizationResult(best, hasLegalResult: true, score: Score(), evaluationsUsed: 5, budgetExhausted: false);

            var suggestion = AdvisorPresentation.DescribeSuggestion(result);

            Assert.Equal(AdvisorSuggestionKey.Suggestion, suggestion.Key);
            Assert.False(suggestion.BudgetExhausted);
        }

        [Fact]
        public void DescribeSuggestion_ReportsEveryPieceOrderedBySlotWithItsIdAndScale()
        {
            // Deliberately built in a scrambled slot order: the player reads the design back
            // blade-first, so the presentation - not the optimizer's internal list order - is what
            // must impose Blade / Guard / Handle / Pommel.
            var best = Candidate(
                "one-handed-sword",
                new DesignPiece("pommel-heavy", PommelType, pieceTier: 2, scalePercentage: 90),
                new DesignPiece("blade-decent", BladeType, pieceTier: 2, scalePercentage: 110),
                new DesignPiece("handle-long", HandleType, pieceTier: 3, scalePercentage: 130),
                new DesignPiece("guard-plain", GuardType, pieceTier: 1, scalePercentage: 100));
            var result = new OptimizationResult(best, hasLegalResult: true, score: Score(combatTotal: 61f, saleValue: 480f), evaluationsUsed: 12, budgetExhausted: false);

            var suggestion = AdvisorPresentation.DescribeSuggestion(result);

            Assert.Equal(
                new[] { AdvisorPieceKind.Blade, AdvisorPieceKind.Guard, AdvisorPieceKind.Handle, AdvisorPieceKind.Pommel },
                suggestion.Pieces.Select(p => p.Kind).ToArray());
            Assert.Equal(
                new[] { "blade-decent", "guard-plain", "handle-long", "pommel-heavy" },
                suggestion.Pieces.Select(p => p.PieceId).ToArray());
            Assert.Equal(
                new[] { 110, 100, 130, 90 },
                suggestion.Pieces.Select(p => p.ScalePercentage).ToArray());
            Assert.Equal(61f, suggestion.CombatTotal, 3);
            Assert.Equal(480f, suggestion.SaleValue, 3);
        }

        [Fact]
        public void BuildVerdict_CarriesTheScoresOwnNumbersForTheRequestedContext()
        {
            var score = Score(combatTotal: 55.4f, saleValue: 620f);

            var verdict = AdvisorPresentation.BuildVerdict(score, MountedContext.OnFoot);

            Assert.Equal(MountedContext.OnFoot, verdict.Context);
            Assert.Equal(55.4f, verdict.CombatTotal, 3);
            Assert.Equal(620f, verdict.SaleValue, 3);
        }

        [Fact]
        public void BuildVerdict_ReportsTheMountedContextDistinctlyFromOnFoot()
        {
            var score = Score();

            var mounted = AdvisorPresentation.BuildVerdict(score, MountedContext.Mounted);
            var onFoot = AdvisorPresentation.BuildVerdict(score, MountedContext.OnFoot);

            Assert.Equal(MountedContext.Mounted, mounted.Context);
            Assert.Equal(MountedContext.OnFoot, onFoot.Context);
            Assert.NotEqual(mounted.Context, onFoot.Context);
        }

        [Theory]
        [InlineData(BladeType, AdvisorPieceKind.Blade)]
        [InlineData(GuardType, AdvisorPieceKind.Guard)]
        [InlineData(HandleType, AdvisorPieceKind.Handle)]
        [InlineData(PommelType, AdvisorPieceKind.Pommel)]
        [InlineData(99, AdvisorPieceKind.Unknown)]
        [InlineData(-1, AdvisorPieceKind.Unknown)]
        public void PieceKindFor_ReturnsTheExpectedKind(int pieceType, AdvisorPieceKind expected)
        {
            Assert.Equal(expected, AdvisorPresentation.PieceKindFor(pieceType));
        }
    }
}
