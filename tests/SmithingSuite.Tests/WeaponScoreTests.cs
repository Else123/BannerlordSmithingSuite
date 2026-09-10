using System;
using System.Linq;
using SmithingSuite.Advisor;
using Xunit;

namespace SmithingSuite.Tests
{
    /// <summary>
    /// Tests for <c>SmithingSuite.Advisor.WeaponScorer</c> / <c>WeaponScore</c> / <c>ScoreWeights</c>
    /// (SPEC-SLICE4-5.md TODO 6, covering AC9-AC11): the scorer produces identified, explained axes
    /// that read engine truth (via <see cref="ArmourModel"/>) rather than raw damage, keeps sale
    /// value separate from the combat score, and shifts ranking with <see cref="MountedContext"/> as
    /// the user explicitly asked ("bin ich beritten oder nicht").
    ///
    /// Since the advisor was made localisable, an axis carries an <see cref="AdvisorAxisKind"/> and an
    /// <see cref="AxisReason"/> (key + typed numbers) instead of English prose, and the assertions
    /// below moved with it - onto the key and its arguments. That is strictly stronger than asserting
    /// on sentences: a copy-edit or a translation can no longer break these tests, and "the axis has
    /// SOME non-empty reason" became "the axis has the RIGHT reason, carrying the right numbers".
    /// </summary>
    public class WeaponScoreTests
    {
        // The spec's example representative armour tiers (20 / 40 / 60) - see SPEC-SLICE4-5.md
        // "Ansatz + Architektur / Slice 4", WeaponScore bullet. The middle one anchors the score.
        private const float LightArmour = 20f;
        private const float RepresentativeArmour = 40f;
        private const float HeavyArmour = 60f;

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

        private static WeaponScore ScoreOnFoot(WeaponStats stats)
            => WeaponScorer.Score(stats, MountedContext.OnFoot, ScoreWeights.ForContext(MountedContext.OnFoot));

        private static ScoreAxis AxisOf(WeaponScore score, AdvisorAxisKind kind)
            => Assert.Single(score.Axes, axis => axis.Kind == kind);

        /// <summary>The reason each axis is contractually required to give - see AC10.</summary>
        private static AdvisorReasonKey ExpectedReasonKey(AdvisorAxisKind kind)
        {
            switch (kind)
            {
                case AdvisorAxisKind.EffectiveDamage: return AdvisorReasonKey.EffectiveDamageAcrossArmourTiers;
                case AdvisorAxisKind.AttackSpeed: return AdvisorReasonKey.AttackSpeedOfChosenMode;
                case AdvisorAxisKind.Reach: return AdvisorReasonKey.ReachFromWeaponLength;
                case AdvisorAxisKind.Handling: return AdvisorReasonKey.HandlingBetweenAttacks;
                case AdvisorAxisKind.SaleValue: return AdvisorReasonKey.SaleValueKeptSeparate;
                default: throw new InvalidOperationException("No reason key is defined for axis kind " + kind + ".");
            }
        }

        /// <summary>
        /// A reason key is only useful to a renderer if the arguments that key's sentence needs are
        /// actually filled in - an empty carrier would render a sentence full of zeroes and still
        /// pass a "has a reason" check, which is exactly the hole this closes.
        /// </summary>
        private static void AssertReasonCarriesItsArguments(ScoreAxis axis)
        {
            AxisReason reason = axis.Reason;
            if (reason.Key == AdvisorReasonKey.EffectiveDamageAcrossArmourTiers)
            {
                Assert.True(reason.RawDamage > 0f, "The effective-damage reason must quote the chosen mode's raw damage.");
                Assert.Equal(LightArmour, reason.LightArmour, 3);
                Assert.Equal(RepresentativeArmour, reason.MediumArmour, 3);
                Assert.Equal(HeavyArmour, reason.HeavyArmour, 3);

                // The axis's own value IS the medium-tier figure - the anchor the combat score uses -
                // so the explanation can never quote a different number than the row it explains.
                Assert.Equal(axis.Value, reason.EffectiveAtMedium, 3);

                // More armour must mean less damage gets through, or the reason is not reading the
                // armour model at all.
                Assert.True(reason.EffectiveAtLight > reason.EffectiveAtMedium,
                    $"Light-armour damage {reason.EffectiveAtLight} should exceed medium {reason.EffectiveAtMedium}.");
                Assert.True(reason.EffectiveAtMedium > reason.EffectiveAtHeavy,
                    $"Medium-armour damage {reason.EffectiveAtMedium} should exceed heavy {reason.EffectiveAtHeavy}.");
                return;
            }

            // Every other reason quotes exactly one number, and it must be the axis's own value.
            Assert.Equal(axis.Value, reason.Stat, 3);
        }

        [Fact]
        public void Score_EveryAxisCarriesTheReasonKeyItsKindPromisesWithThatKeysArguments() // AC10
        {
            var score = ScoreOnFoot(Stats());

            Assert.NotEmpty(score.Axes);

            // All five facets, each reported exactly once - no silent duplicate or missing axis.
            Assert.Equal(
                new[]
                {
                    AdvisorAxisKind.EffectiveDamage,
                    AdvisorAxisKind.AttackSpeed,
                    AdvisorAxisKind.Reach,
                    AdvisorAxisKind.Handling,
                    AdvisorAxisKind.SaleValue,
                },
                score.Axes.Select(axis => axis.Kind).ToArray());

            Assert.All(score.Axes, axis =>
            {
                Assert.NotNull(axis.Reason);
                Assert.Equal(ExpectedReasonKey(axis.Kind), axis.Reason.Key);
                AssertReasonCarriesItsArguments(axis);
            });
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

            // And it must be reported as its own identified axis too, not just a bare field - with
            // the gold amount carried in the axis value AND in its explanation.
            var saleValueAxis = AxisOf(expensiveScore, AdvisorAxisKind.SaleValue);
            Assert.Equal(9000f, saleValueAxis.Value, 3);
            Assert.Equal(AdvisorReasonKey.SaleValueKeptSeparate, saleValueAxis.Reason.Key);
            Assert.Equal(9000f, saleValueAxis.Reason.Stat, 3);
        }

        [Fact]
        public void Score_ExposesAnEffectiveDamageAxisDerivedFromArmourModel()
        {
            // Swing and thrust are identical on purpose so this assertion does not depend on which
            // of the two the scorer happens to read for this axis.
            var stats = Stats(swingDamage: 60f, swingDamageType: DamageKind.Pierce, thrustDamage: 60f, thrustDamageType: DamageKind.Pierce);

            var score = ScoreOnFoot(stats);

            // Pins that the scorer reads engine truth via ArmourModel rather than raw damage -
            // computed independently here, not via a mirroring helper.
            var expected = ArmourModel.EffectiveDamage(stats.SwingDamage, stats.SwingDamageType, RepresentativeArmour);

            var effectiveDamageAxis = AxisOf(score, AdvisorAxisKind.EffectiveDamage);
            Assert.Equal(expected, effectiveDamageAxis.Value, 2);
        }

        [Fact]
        public void Score_EffectiveDamageAxis_UsesTheEffectiveDamageReasonKeyWithAllThreeArmourTiers()
        {
            var stats = Stats(swingDamage: 55f, swingDamageType: DamageKind.Blunt, thrustDamage: 10f, thrustDamageType: DamageKind.Blunt);

            var reason = AxisOf(ScoreOnFoot(stats), AdvisorAxisKind.EffectiveDamage).Reason;

            Assert.Equal(AdvisorReasonKey.EffectiveDamageAcrossArmourTiers, reason.Key);
            Assert.Equal(DamageKind.Blunt, reason.DamageType);
            Assert.Equal(55f, reason.RawDamage, 3);
            Assert.Equal(ArmourModel.EffectiveDamage(55f, DamageKind.Blunt, LightArmour), reason.EffectiveAtLight, 2);
            Assert.Equal(ArmourModel.EffectiveDamage(55f, DamageKind.Blunt, RepresentativeArmour), reason.EffectiveAtMedium, 2);
            Assert.Equal(ArmourModel.EffectiveDamage(55f, DamageKind.Blunt, HeavyArmour), reason.EffectiveAtHeavy, 2);
        }

        [Fact]
        public void Score_AttackSpeedAxis_UsesTheAttackSpeedReasonKeyWithTheChosenModesSpeed()
        {
            var stats = Stats(swingSpeed: 111f, thrustSpeed: 55f);

            var axis = AxisOf(ScoreOnFoot(stats), AdvisorAxisKind.AttackSpeed);

            Assert.Equal(AdvisorReasonKey.AttackSpeedOfChosenMode, axis.Reason.Key);
            Assert.Equal(111f, axis.Reason.Stat, 3);
        }

        [Fact]
        public void Score_ReachAxis_UsesTheReachReasonKeyWithTheWeaponLength()
        {
            var axis = AxisOf(ScoreOnFoot(Stats(weaponLength: 137f)), AdvisorAxisKind.Reach);

            Assert.Equal(AdvisorReasonKey.ReachFromWeaponLength, axis.Reason.Key);
            Assert.Equal(137f, axis.Value, 3);
            Assert.Equal(137f, axis.Reason.Stat, 3);
        }

        [Fact]
        public void Score_HandlingAxis_UsesTheHandlingReasonKeyWithTheHandlingStat()
        {
            var axis = AxisOf(ScoreOnFoot(Stats(handling: 64f)), AdvisorAxisKind.Handling);

            Assert.Equal(AdvisorReasonKey.HandlingBetweenAttacks, axis.Reason.Key);
            Assert.Equal(64f, axis.Value, 3);
            Assert.Equal(64f, axis.Reason.Stat, 3);
        }

        [Fact]
        public void Score_SaleValueAxis_UsesTheSaleValueReasonKeyWithTheGoldValue()
        {
            var axis = AxisOf(ScoreOnFoot(Stats(value: 1234)), AdvisorAxisKind.SaleValue);

            Assert.Equal(AdvisorReasonKey.SaleValueKeptSeparate, axis.Reason.Key);
            Assert.Equal(1234f, axis.Reason.Stat, 3);
        }

        [Theory]
        // Thrust (Pierce 60) gets far more through 40 armour than swing (Cut 30) does.
        [InlineData(30f, DamageKind.Cut, 60f, DamageKind.Pierce, AttackMode.Thrust, 55f)]
        // ... and the mirror image: swing (Cut 90) beats thrust (Pierce 10).
        [InlineData(90f, DamageKind.Cut, 10f, DamageKind.Pierce, AttackMode.Swing, 120f)]
        public void Score_ReportsOneCoherentAttackMode_AcrossTheDamageAndSpeedAxes(
            float swingDamage, DamageKind swingType, float thrustDamage, DamageKind thrustType,
            AttackMode expectedMode, float expectedSpeed)
        {
            // Speeds are distinct per mode so "which speed got reported" is observable, not a
            // coincidence: swing 120, thrust 55.
            var stats = Stats(
                swingSpeed: 120f, swingDamage: swingDamage, swingDamageType: swingType,
                thrustSpeed: 55f, thrustDamage: thrustDamage, thrustDamageType: thrustType);

            var score = ScoreOnFoot(stats);
            var damageReason = AxisOf(score, AdvisorAxisKind.EffectiveDamage).Reason;
            var speedAxis = AxisOf(score, AdvisorAxisKind.AttackSpeed);

            // Both mode-bearing axes must name the SAME mode - reporting swing's speed next to
            // thrust's damage would describe an attack the weapon cannot make.
            Assert.Equal(expectedMode, damageReason.Mode);
            Assert.Equal(expectedMode, speedAxis.Reason.Mode);

            // ... and the numbers must follow that mode, not just its name.
            Assert.Equal(expectedMode == AttackMode.Thrust ? thrustDamage : swingDamage, damageReason.RawDamage, 3);
            Assert.Equal(expectedMode == AttackMode.Thrust ? thrustType : swingType, damageReason.DamageType);
            Assert.Equal(expectedSpeed, speedAxis.Value, 3);
            Assert.Equal(expectedSpeed, speedAxis.Reason.Stat, 3);
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
