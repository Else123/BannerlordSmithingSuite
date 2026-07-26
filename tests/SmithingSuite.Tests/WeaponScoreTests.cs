using System;
using SmithingSuite.Advisor;
using Xunit;

namespace SmithingSuite.Tests
{
    /// <summary>
    /// Red-state tests for the not-yet-implemented <c>SmithingSuite.Advisor.WeaponScorer</c> /
    /// <c>WeaponScore</c> / <c>ScoreWeights</c> (SPEC-SLICE4-5.md TODO 6, covering AC9-AC11).
    /// Written first per TDD: these must fail to compile until the scoring types exist, and then
    /// pass once the scorer produces named, explained axes that read engine truth (via
    /// <see cref="ArmourModel"/>) rather than raw damage, keep sale value separate from the combat
    /// score, and shift ranking with <see cref="MountedContext"/> as the user explicitly asked
    /// ("bin ich beritten oder nicht").
    /// </summary>
    public class WeaponScoreTests
    {
        // Middle of the spec's example representative armour tiers (20 / 40 / 60) - see
        // SPEC-SLICE4-5.md "Ansatz + Architektur / Slice 4", WeaponScore bullet.
        private const float RepresentativeArmour = 40f;

        private static WeaponStats Stats(
            float swingSpeed = 100f,
            float swingDamage = 40f,
            DamageKind swingDamageType = DamageKind.Cut,
            float thrustSpeed = 100f,
            float thrustDamage = 40f,
            DamageKind thrustDamageType = DamageKind.Cut,
            float handling = 80f,
            float weaponLength = 90f,
            float weight = 1.2f,
            int tier = 3,
            int value = 500)
            => new WeaponStats(
                swingSpeed, swingDamage, swingDamageType,
                thrustSpeed, thrustDamage, thrustDamageType,
                handling, weaponLength, weight, tier, value);

        [Fact]
        public void Score_EveryAxisHasANonEmptyHumanReadableReason() // AC10
        {
            var score = WeaponScorer.Score(Stats(), MountedContext.OnFoot, ScoreWeights.ForContext(MountedContext.OnFoot));

            Assert.NotEmpty(score.Axes);
            Assert.All(score.Axes, axis => Assert.False(string.IsNullOrWhiteSpace(axis.Reason)));
        }

        [Fact]
        public void Score_SaleValueIsItsOwnAxisAndIsNeverFoldedIntoCombatTotal() // AC11
        {
            var cheap = Stats(value: 100);
            var expensive = Stats(value: 9000);
            var weights = ScoreWeights.ForContext(MountedContext.OnFoot);

            var cheapScore = WeaponScorer.Score(cheap, MountedContext.OnFoot, weights);
            var expensiveScore = WeaponScorer.Score(expensive, MountedContext.OnFoot, weights);

            // Same combat stats, only Value differs - CombatTotal must not move at all.
            Assert.Equal(cheapScore.CombatTotal, expensiveScore.CombatTotal, 3);

            // But SaleValue must reflect the actual Value and differ between the two.
            Assert.Equal(100f, cheapScore.SaleValue, 3);
            Assert.Equal(9000f, expensiveScore.SaleValue, 3);
            Assert.NotEqual(cheapScore.SaleValue, expensiveScore.SaleValue);

            // And it must be reported as its own named axis too, not just a bare field.
            Assert.Contains(expensiveScore.Axes, axis => axis.Name.IndexOf("Sale", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        [Fact]
        public void Score_ExposesAnEffectiveDamageAxisDerivedFromArmourModel()
        {
            // Swing and thrust are identical on purpose so this assertion does not depend on which
            // of the two the scorer happens to read for this axis.
            var stats = Stats(swingDamage: 60f, swingDamageType: DamageKind.Pierce, thrustDamage: 60f, thrustDamageType: DamageKind.Pierce);

            var score = WeaponScorer.Score(stats, MountedContext.OnFoot, ScoreWeights.ForContext(MountedContext.OnFoot));

            // Pins that the scorer reads engine truth via ArmourModel rather than raw damage -
            // computed independently here, not via a mirroring helper.
            var expected = ArmourModel.EffectiveDamage(stats.SwingDamage, stats.SwingDamageType, RepresentativeArmour);

            var effectiveDamageAxis = Assert.Single(
                score.Axes,
                axis => axis.Name.IndexOf("Effective Damage", StringComparison.OrdinalIgnoreCase) >= 0);
            Assert.Equal(expected, effectiveDamageAxis.Value, 2);
        }

        [Fact]
        public void Score_MountedContextFlipsRankingBetweenAFastShortWeaponAndASlowLongWeapon() // AC9
        {
            // A: fast and short - should win on foot, where footwork closes the distance and swing
            // speed is what "getting through" depends on.
            var fastShort = Stats(swingSpeed: 140f, thrustSpeed: 140f, handling: 100f, weaponLength: 60f, swingDamage: 30f, thrustDamage: 30f);
            // B: slow and long - should win mounted, where reach lets the rider hit before being
            // hit and the horse - not footwork - closes the gap.
            var slowLong = Stats(swingSpeed: 60f, thrustSpeed: 60f, handling: 40f, weaponLength: 160f, swingDamage: 30f, thrustDamage: 30f);

            var footWeights = ScoreWeights.ForContext(MountedContext.OnFoot);
            var mountedWeights = ScoreWeights.ForContext(MountedContext.Mounted);

            var fastShortOnFoot = WeaponScorer.Score(fastShort, MountedContext.OnFoot, footWeights).CombatTotal;
            var slowLongOnFoot = WeaponScorer.Score(slowLong, MountedContext.OnFoot, footWeights).CombatTotal;
            var fastShortMounted = WeaponScorer.Score(fastShort, MountedContext.Mounted, mountedWeights).CombatTotal;
            var slowLongMounted = WeaponScorer.Score(slowLong, MountedContext.Mounted, mountedWeights).CombatTotal;

            Assert.True(fastShortOnFoot > slowLongOnFoot,
                $"fast+short ({fastShortOnFoot}) should outrank slow+long ({slowLongOnFoot}) on foot.");
            Assert.True(slowLongMounted > fastShortMounted,
                $"slow+long ({slowLongMounted}) should outrank fast+short ({fastShortMounted}) mounted.");
        }
    }
}
