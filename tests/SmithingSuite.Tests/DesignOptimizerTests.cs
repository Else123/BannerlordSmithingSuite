using System;
using System.Collections.Generic;
using System.Linq;
using SmithingSuite.Advisor;
using Xunit;

namespace SmithingSuite.Tests
{
    /// <summary>
    /// Red-state tests for the not-yet-implemented <c>SmithingSuite.Advisor.DesignOptimizer</c>
    /// (SPEC-SLICE4-5.md TODO 6, covering AC3-AC8). Written first per TDD: these must fail to
    /// compile until the optimizer types exist, and then pass once the staged search respects the
    /// catalogue (unlocked parts only), the difficulty gate, stays within the seed's template,
    /// actually varies scale, and respects its evaluation budget deterministically.
    ///
    /// The fake <see cref="IDesignEvaluator"/> below never calls the real engine: it wraps a pure,
    /// deterministic function of the candidate's pieces (id / type / tier / scale) that each test
    /// supplies, plus an invocation counter, so the search can be driven and asserted on without a
    /// game install.
    /// </summary>
    public class DesignOptimizerTests
    {
        private const int BladeType = 0;
        private const int GuardType = 1;
        private const int HandleType = 2;
        private const int PommelType = 3;

        private sealed class FakeDesignEvaluator : IDesignEvaluator
        {
            private readonly Func<DesignCandidate, DesignEvaluation> _evaluate;

            public FakeDesignEvaluator(Func<DesignCandidate, DesignEvaluation> evaluate)
            {
                _evaluate = evaluate;
            }

            public int InvocationCount { get; private set; }

            public DesignEvaluation Evaluate(DesignCandidate candidate)
            {
                InvocationCount++;
                return _evaluate(candidate);
            }
        }

        private static DesignPiece Piece(string id, int pieceType, int tier, int scale = 100)
            => new DesignPiece(id, pieceType, tier, scale);

        private static DesignCandidate Candidate(string templateId, params DesignPiece[] pieces)
            => new DesignCandidate(templateId, pieces);

        private static WeaponStats Stats(float damage, DamageKind damageType = DamageKind.Cut, float speed = 100f, float handling = 80f, float length = 90f)
            => new WeaponStats(speed, damage, damageType, speed, damage, damageType, handling, length, 1.2f, 3, 300);

        private static void AssertSameDesign(DesignCandidate a, DesignCandidate b)
        {
            Assert.Equal(a.TemplateId, b.TemplateId);

            var aSorted = a.Pieces.OrderBy(p => p.PieceType).ToList();
            var bSorted = b.Pieces.OrderBy(p => p.PieceType).ToList();
            Assert.Equal(aSorted.Count, bSorted.Count);
            for (var i = 0; i < aSorted.Count; i++)
            {
                Assert.Equal(aSorted[i].PieceId, bSorted[i].PieceId);
                Assert.Equal(aSorted[i].ScalePercentage, bSorted[i].ScalePercentage);
            }
        }

        [Fact]
        public void Optimize_NeverProposesAPieceThatIsNotInTheCatalogue() // AC3
        {
            // The seed currently carries a legendary blade that scores enormously well in this
            // fake, but is NOT among the smith's unlocked parts (catalogue.AvailablePieces). If the
            // optimizer ever trusted the seed's own pieces instead of re-deriving everything from
            // the catalogue, it would keep this "temptation" - it must not.
            var seed = Candidate(
                "one-handed-sword",
                Piece("blade-forbidden-legendary", BladeType, tier: 6),
                Piece("guard-basic", GuardType, tier: 1),
                Piece("handle-basic", HandleType, tier: 1),
                Piece("pommel-basic", PommelType, tier: 1));

            var catalogue = new PieceCatalogue(
                "one-handed-sword",
                new[]
                {
                    Piece("blade-basic", BladeType, tier: 1),
                    Piece("blade-decent", BladeType, tier: 2),
                    Piece("guard-basic", GuardType, tier: 1),
                    Piece("handle-basic", HandleType, tier: 1),
                    Piece("pommel-basic", PommelType, tier: 1),
                },
                new[] { 100 });

            DesignEvaluation Evaluate(DesignCandidate candidate)
            {
                var blade = candidate.Pieces.First(p => p.PieceType == BladeType);
                var damage = blade.PieceId == "blade-forbidden-legendary" ? 500f : 20f + blade.PieceTier * 10f;
                return new DesignEvaluation(Stats(damage), difficulty: 10);
            }

            var evaluator = new FakeDesignEvaluator(Evaluate);
            var optimizer = new DesignOptimizer(evaluator);
            var options = new OptimizerOptions(
                evaluationBudget: 200,
                context: MountedContext.OnFoot,
                weights: ScoreWeights.ForContext(MountedContext.OnFoot),
                maxDifficulty: 100);

            var result = optimizer.Optimize(seed, catalogue, options);

            var availableIds = catalogue.AvailablePieces.Select(p => p.PieceId).ToHashSet();
            Assert.All(result.Best.Pieces, piece => Assert.Contains(piece.PieceId, availableIds));
            Assert.DoesNotContain(result.Best.Pieces, piece => piece.PieceId == "blade-forbidden-legendary");
        }

        [Fact]
        public void Optimize_NeverProposesADesignWhoseDifficultyExceedsMaxDifficulty() // AC4
        {
            // blade-expensive scores far higher but pushes difficulty past the smith's skill;
            // blade-cheap is the only blade that keeps the design legal.
            var seed = Candidate(
                "dagger",
                Piece("blade-cheap", BladeType, tier: 1),
                Piece("guard-basic", GuardType, tier: 1),
                Piece("handle-basic", HandleType, tier: 1),
                Piece("pommel-basic", PommelType, tier: 1));

            var catalogue = new PieceCatalogue(
                "dagger",
                new[]
                {
                    Piece("blade-cheap", BladeType, tier: 1),
                    Piece("blade-expensive", BladeType, tier: 9),
                    Piece("guard-basic", GuardType, tier: 1),
                    Piece("handle-basic", HandleType, tier: 1),
                    Piece("pommel-basic", PommelType, tier: 1),
                },
                new[] { 100 });

            DesignEvaluation Evaluate(DesignCandidate candidate)
            {
                var blade = candidate.Pieces.First(p => p.PieceType == BladeType);
                var damage = 20f + blade.PieceTier * 10f;
                var difficulty = blade.PieceTier * 10; // tier 9 -> 90 (illegal), tier 1 -> 10 (legal)
                return new DesignEvaluation(Stats(damage), difficulty);
            }

            var evaluator = new FakeDesignEvaluator(Evaluate);
            var optimizer = new DesignOptimizer(evaluator);
            var options = new OptimizerOptions(
                evaluationBudget: 200,
                context: MountedContext.OnFoot,
                weights: ScoreWeights.ForContext(MountedContext.OnFoot),
                maxDifficulty: 50);

            var result = optimizer.Optimize(seed, catalogue, options);

            var bladeUsed = result.Best.Pieces.First(p => p.PieceType == BladeType);
            Assert.NotEqual("blade-expensive", bladeUsed.PieceId);

            var evaluationOfBest = Evaluate(result.Best);
            Assert.True(evaluationOfBest.Difficulty <= options.MaxDifficulty,
                $"Best design's difficulty {evaluationOfBest.Difficulty} exceeds MaxDifficulty {options.MaxDifficulty}.");
        }

        [Fact]
        public void Optimize_OnlyEverProposesDesignsWithinTheSeedsTemplate() // AC5
        {
            var seed = Candidate(
                "two-handed-sword",
                Piece("blade-a", BladeType, tier: 1),
                Piece("guard-a", GuardType, tier: 1),
                Piece("handle-a", HandleType, tier: 1),
                Piece("pommel-a", PommelType, tier: 1));

            var catalogue = new PieceCatalogue(
                "two-handed-sword",
                new[]
                {
                    Piece("blade-a", BladeType, tier: 1),
                    Piece("blade-b", BladeType, tier: 2),
                    Piece("guard-a", GuardType, tier: 1),
                    Piece("handle-a", HandleType, tier: 1),
                    Piece("pommel-a", PommelType, tier: 1),
                },
                new[] { 100, 120 });

            DesignEvaluation Evaluate(DesignCandidate candidate)
            {
                var tierSum = candidate.Pieces.Sum(p => p.PieceTier);
                return new DesignEvaluation(Stats(20f + tierSum * 5f), difficulty: 10);
            }

            var evaluator = new FakeDesignEvaluator(Evaluate);
            var optimizer = new DesignOptimizer(evaluator);
            var options = new OptimizerOptions(
                evaluationBudget: 100,
                context: MountedContext.OnFoot,
                weights: ScoreWeights.ForContext(MountedContext.OnFoot),
                maxDifficulty: 100);

            var result = optimizer.Optimize(seed, catalogue, options);

            // The optimizer only ever searches within the template it was handed - never a
            // different weapon form.
            Assert.Equal("two-handed-sword", result.Best.TemplateId);
        }

        [Fact]
        public void Optimize_ActuallyVariesScalePercentageWhenItImprovesTheScore() // AC6
        {
            var seed = Candidate(
                "polearm",
                Piece("blade-a", BladeType, tier: 1, scale: 100),
                Piece("guard-a", GuardType, tier: 1, scale: 100),
                Piece("handle-a", HandleType, tier: 1, scale: 100),
                Piece("pommel-a", PommelType, tier: 1, scale: 100));

            var catalogue = new PieceCatalogue(
                "polearm",
                new[]
                {
                    Piece("blade-a", BladeType, tier: 1),
                    Piece("guard-a", GuardType, tier: 1),
                    Piece("handle-a", HandleType, tier: 1),
                    Piece("pommel-a", PommelType, tier: 1),
                },
                new[] { 80, 100, 120, 140 });

            DesignEvaluation Evaluate(DesignCandidate candidate)
            {
                // Score increases monotonically with the blade's scale, so a genuine search - not
                // decoration - is required to find the improvement over the seed's 100%. Difficulty
                // stays flat and legal at every scale step so this AC is isolated from AC4.
                var blade = candidate.Pieces.First(p => p.PieceType == BladeType);
                var damage = 20f + (blade.ScalePercentage - 100) * 0.5f;
                return new DesignEvaluation(Stats(damage), difficulty: 10);
            }

            var evaluator = new FakeDesignEvaluator(Evaluate);
            var optimizer = new DesignOptimizer(evaluator);
            var options = new OptimizerOptions(
                evaluationBudget: 200,
                context: MountedContext.OnFoot,
                weights: ScoreWeights.ForContext(MountedContext.OnFoot),
                maxDifficulty: 100);

            var result = optimizer.Optimize(seed, catalogue, options);

            var bladeUsed = result.Best.Pieces.First(p => p.PieceType == BladeType);
            Assert.True(bladeUsed.ScalePercentage > 100,
                $"Winning scale {bladeUsed.ScalePercentage} should exceed the seed's 100% - scale must be searched, not decoration.");
        }

        [Fact]
        public void Optimize_NeverExceedsTheEvaluationBudgetAndFlagsExhaustion() // AC7
        {
            var seed = Candidate(
                "great-axe",
                Piece("blade-0", BladeType, tier: 1),
                Piece("guard-a", GuardType, tier: 1),
                Piece("handle-a", HandleType, tier: 1),
                Piece("pommel-a", PommelType, tier: 1));

            // A blade catalogue far larger than the tiny budget below, so exhaustion inside the
            // blade stage of the staged search is unavoidable for any reasonable implementation.
            var blades = Enumerable.Range(0, 40)
                .Select(i => Piece($"blade-{i}", BladeType, tier: 1 + i % 6))
                .ToList();
            var pieces = new List<DesignPiece>(blades)
            {
                Piece("guard-a", GuardType, tier: 1),
                Piece("handle-a", HandleType, tier: 1),
                Piece("pommel-a", PommelType, tier: 1),
            };
            var catalogue = new PieceCatalogue("great-axe", pieces, new[] { 80, 100, 120 });

            DesignEvaluation Evaluate(DesignCandidate candidate)
            {
                var tierSum = candidate.Pieces.Sum(p => p.PieceTier);
                return new DesignEvaluation(Stats(20f + tierSum * 5f), difficulty: 10);
            }

            var evaluator = new FakeDesignEvaluator(Evaluate);
            var optimizer = new DesignOptimizer(evaluator);
            var options = new OptimizerOptions(
                evaluationBudget: 5,
                context: MountedContext.OnFoot,
                weights: ScoreWeights.ForContext(MountedContext.OnFoot),
                maxDifficulty: 100);

            var result = optimizer.Optimize(seed, catalogue, options);

            // Never null, never throws, always a best-so-far - even when the budget cannot cover
            // the search space.
            Assert.NotNull(result);
            Assert.NotNull(result.Best);
            Assert.True(result.EvaluationsUsed <= options.EvaluationBudget,
                $"EvaluationsUsed {result.EvaluationsUsed} exceeds budget {options.EvaluationBudget}.");
            Assert.True(result.BudgetExhausted, "A search space this large against a budget of 5 must exhaust the budget.");
        }

        [Fact]
        public void Optimize_IsDeterministicAcrossRepeatedRunsWithIdenticalInputs() // AC8
        {
            var seed = Candidate(
                "one-handed-sword",
                Piece("blade-a", BladeType, tier: 1),
                Piece("guard-a", GuardType, tier: 1),
                Piece("handle-a", HandleType, tier: 1),
                Piece("pommel-a", PommelType, tier: 1));

            var catalogue = new PieceCatalogue(
                "one-handed-sword",
                new[]
                {
                    Piece("blade-a", BladeType, tier: 1),
                    Piece("blade-b", BladeType, tier: 3),
                    Piece("blade-c", BladeType, tier: 2),
                    Piece("guard-a", GuardType, tier: 1),
                    Piece("handle-a", HandleType, tier: 1),
                    Piece("pommel-a", PommelType, tier: 1),
                },
                new[] { 90, 100, 110 });

            DesignEvaluation Evaluate(DesignCandidate candidate)
            {
                var blade = candidate.Pieces.First(p => p.PieceType == BladeType);
                var damage = 20f + blade.PieceTier * 7f + (blade.ScalePercentage - 100) * 0.3f;
                return new DesignEvaluation(Stats(damage), difficulty: 10);
            }

            var options = new OptimizerOptions(
                evaluationBudget: 100,
                context: MountedContext.OnFoot,
                weights: ScoreWeights.ForContext(MountedContext.OnFoot),
                maxDifficulty: 100);

            var firstEvaluator = new FakeDesignEvaluator(Evaluate);
            var firstResult = new DesignOptimizer(firstEvaluator).Optimize(seed, catalogue, options);

            var secondEvaluator = new FakeDesignEvaluator(Evaluate);
            var secondResult = new DesignOptimizer(secondEvaluator).Optimize(seed, catalogue, options);

            AssertSameDesign(firstResult.Best, secondResult.Best);
            Assert.Equal(firstResult.EvaluationsUsed, secondResult.EvaluationsUsed);
        }
    }
}
