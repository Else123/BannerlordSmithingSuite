using System.Collections.Generic;
using SmithingSuite.Advisor;
using Xunit;

namespace SmithingSuite.Tests
{
    /// <summary>
    /// Tests for <see cref="AdvisorPresentation"/> - pure presentation functions moved out of
    /// <c>WeaponDesignAdvisorMixin</c> (architecture review structural fix) so they compile and run
    /// on the host .NET SDK. <see cref="DescribeSuggestion_WhenResultHasNoLegalResult_RefusesToPresentIt"/>
    /// covers the review-found guard - refusing to present an unverified/illegal result as a
    /// suggestion - that had NO test before this move.
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
                new ScoreAxis("Effective Damage", 30f, "reason"),
            };
            return new WeaponScore(axes, combatTotal, saleValue);
        }

        [Fact]
        public void DescribeSuggestion_WhenResultHasNoLegalResult_RefusesToPresentIt()
        {
            // Mirrors the seed-only outcome DesignOptimizer.BuildResult produces when nothing legal
            // was ever found (FINDING 1) - Best is non-null (hard rule) but HasLegalResult is false,
            // so this must never be shown to the player as "Suggested: ...".
            var seed = Candidate(
                "one-handed-sword",
                new DesignPiece("blade-basic", BladeType, pieceTier: 1, scalePercentage: 100));
            var result = new OptimizationResult(seed, hasLegalResult: false, score: null, evaluationsUsed: 3, budgetExhausted: false);

            var text = AdvisorPresentation.DescribeSuggestion(result);

            Assert.Equal("No craftable improvement was found within the current smithing skill and unlocked parts.", text);
            Assert.DoesNotContain("Suggested", text);
        }

        [Fact]
        public void DescribeSuggestion_WhenResultIsNull_RefusesToPresentIt()
        {
            var text = AdvisorPresentation.DescribeSuggestion(null);

            Assert.Equal("No craftable improvement was found within the current smithing skill and unlocked parts.", text);
        }

        [Fact]
        public void DescribeSuggestion_LabelsABudgetExhaustedResultAsIncomplete()
        {
            var best = Candidate(
                "one-handed-sword",
                new DesignPiece("blade-decent", BladeType, pieceTier: 2, scalePercentage: 110));
            var result = new OptimizationResult(best, hasLegalResult: true, score: Score(), evaluationsUsed: 200, budgetExhausted: true);

            var text = AdvisorPresentation.DescribeSuggestion(result);

            Assert.StartsWith("Suggested:", text);
            Assert.Contains("search budget exhausted", text);
        }

        [Fact]
        public void DescribeSuggestion_WhenLegalAndBudgetNotExhausted_OmitsTheExhaustionNote()
        {
            var best = Candidate(
                "one-handed-sword",
                new DesignPiece("blade-decent", BladeType, pieceTier: 2, scalePercentage: 110));
            var result = new OptimizationResult(best, hasLegalResult: true, score: Score(), evaluationsUsed: 5, budgetExhausted: false);

            var text = AdvisorPresentation.DescribeSuggestion(result);

            Assert.StartsWith("Suggested:", text);
            Assert.DoesNotContain("budget exhausted", text);
        }

        [Fact]
        public void BuildVerdict_ProducesANonEmptyHumanReadableLineForANormalScore()
        {
            var score = Score(combatTotal: 55.4f, saleValue: 620f);

            var text = AdvisorPresentation.BuildVerdict(score, MountedContext.OnFoot);

            Assert.False(string.IsNullOrWhiteSpace(text));
            Assert.Contains("Combat score", text);
            Assert.Contains("on foot", text);
            Assert.Contains("620", text);
        }

        [Fact]
        public void BuildVerdict_LabelsTheMountedContextDifferentlyFromOnFoot()
        {
            var score = Score();

            var mountedText = AdvisorPresentation.BuildVerdict(score, MountedContext.Mounted);
            var onFootText = AdvisorPresentation.BuildVerdict(score, MountedContext.OnFoot);

            Assert.Contains("mounted", mountedText);
            Assert.Contains("on foot", onFootText);
            Assert.NotEqual(mountedText, onFootText);
        }

        [Theory]
        [InlineData(true, "Context: Mounted")]
        [InlineData(false, "Context: On Foot")]
        public void ContextButtonLabel_ReturnsTheExpectedLabel(bool mounted, string expected)
        {
            Assert.Equal(expected, AdvisorPresentation.ContextButtonLabel(mounted));
        }

        [Theory]
        [InlineData(BladeType, "Blade")]
        [InlineData(GuardType, "Guard")]
        [InlineData(HandleType, "Handle")]
        [InlineData(PommelType, "Pommel")]
        [InlineData(99, "Piece")]
        [InlineData(-1, "Piece")]
        public void PieceTypeName_ReturnsTheExpectedName(int pieceType, string expected)
        {
            Assert.Equal(expected, AdvisorPresentation.PieceTypeName(pieceType));
        }
    }
}
