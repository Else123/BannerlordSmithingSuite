using SmithingSuite.Advisor;
using Xunit;

namespace SmithingSuite.Tests
{
    /// <summary>
    /// Red-state tests for the not-yet-implemented <c>SmithingSuite.Advisor.ArmourModel</c>
    /// (SPEC-SLICE4-5.md TODO 3, AC1-AC2). Written first per TDD: these must fail to compile
    /// until <c>ArmourModel</c> exists, and then must pass once it implements the verified
    /// engine formula from <c>TaleWorlds.MountAndBlade.DefaultStrikeMagnitudeModel</c>:
    ///
    ///   bluntFactor : Cut 0.1, Pierce 0.25, Blunt 0.6
    ///   mitigated   = magnitude * (50 / (50 + armour))
    ///   threshold   : Cut armour*0.5, Pierce armour*0.33, Blunt armour*0.2
    ///   penetrating = max(0, mitigated - threshold)
    ///   result      = (bluntFactor * mitigated + (1 - bluntFactor) * penetrating) * absorbedDamageRatio
    ///
    /// Both terms are ALWAYS blended - this is not an either/or. Below the threshold the
    /// penetrating term is zero and the result collapses to bluntFactor * mitigated, which is
    /// exactly what the "just below" cases below pin.
    ///
    /// All expected values are computed by hand from the formula above, not from a helper that
    /// mirrors the production code - a mirroring helper would prove nothing.
    /// </summary>
    public class ArmourModelTests
    {
        // Assert.Equal(expected, actual, precision) rounds both sides to this many decimal
        // digits before comparing - the tolerance float maths needs against exact hand-computed
        // expectations.
        private const int Precision = 3;

        // ---- Cut: armour = 50 -> mitigation = 50/(50+50) = 0.5, threshold = 50*0.5 = 25 ----

        [Fact]
        public void EffectiveDamage_Cut_JustBelowThreshold_CollapsesToBluntedTermOnly()
        {
            // magnitude 48 -> mitigated = 48*0.5 = 24, which is below the threshold of 25.
            // penetrating = max(0, 24-25) = 0.
            // result = (0.1*24 + 0.9*0) * 1 = 2.4
            var actual = ArmourModel.EffectiveDamage(48f, DamageKind.Cut, 50f);

            Assert.Equal(2.4f, actual, Precision);
        }

        [Fact]
        public void EffectiveDamage_Cut_ExactlyAtThreshold_PenetratingTermIsStillZero()
        {
            // magnitude 50 -> mitigated = 50*0.5 = 25, exactly the threshold of 25.
            // penetrating = max(0, 25-25) = 0.
            // result = (0.1*25 + 0.9*0) * 1 = 2.5
            var actual = ArmourModel.EffectiveDamage(50f, DamageKind.Cut, 50f);

            Assert.Equal(2.5f, actual, Precision);
        }

        [Fact]
        public void EffectiveDamage_Cut_JustAboveThreshold_BlendsInThePenetratingTerm()
        {
            // magnitude 52 -> mitigated = 52*0.5 = 26, one above the threshold of 25.
            // penetrating = max(0, 26-25) = 1.
            // result = (0.1*26 + 0.9*1) * 1 = 2.6 + 0.9 = 3.5
            var actual = ArmourModel.EffectiveDamage(52f, DamageKind.Cut, 50f);

            Assert.Equal(3.5f, actual, Precision);
        }

        // ---- Pierce: armour = 50 -> mitigation = 0.5, threshold = 50*0.33 = 16.5 ----

        [Fact]
        public void EffectiveDamage_Pierce_JustBelowThreshold_CollapsesToBluntedTermOnly()
        {
            // magnitude 32 -> mitigated = 32*0.5 = 16, which is below the threshold of 16.5.
            // penetrating = max(0, 16-16.5) = 0.
            // result = (0.25*16 + 0.75*0) * 1 = 4.0
            var actual = ArmourModel.EffectiveDamage(32f, DamageKind.Pierce, 50f);

            Assert.Equal(4.0f, actual, Precision);
        }

        [Fact]
        public void EffectiveDamage_Pierce_ExactlyAtThreshold_PenetratingTermIsStillZero()
        {
            // magnitude 33 -> mitigated = 33*0.5 = 16.5, exactly the threshold of 16.5.
            // penetrating = max(0, 16.5-16.5) = 0.
            // result = (0.25*16.5 + 0.75*0) * 1 = 4.125
            var actual = ArmourModel.EffectiveDamage(33f, DamageKind.Pierce, 50f);

            Assert.Equal(4.125f, actual, Precision);
        }

        [Fact]
        public void EffectiveDamage_Pierce_JustAboveThreshold_BlendsInThePenetratingTerm()
        {
            // magnitude 34 -> mitigated = 34*0.5 = 17, half above the threshold of 16.5.
            // penetrating = max(0, 17-16.5) = 0.5.
            // result = (0.25*17 + 0.75*0.5) * 1 = 4.25 + 0.375 = 4.625
            var actual = ArmourModel.EffectiveDamage(34f, DamageKind.Pierce, 50f);

            Assert.Equal(4.625f, actual, Precision);
        }

        // ---- Blunt: armour = 50 -> mitigation = 0.5, threshold = 50*0.2 = 10 ----

        [Fact]
        public void EffectiveDamage_Blunt_JustBelowThreshold_CollapsesToBluntedTermOnly()
        {
            // magnitude 18 -> mitigated = 18*0.5 = 9, which is below the threshold of 10.
            // penetrating = max(0, 9-10) = 0.
            // result = (0.6*9 + 0.4*0) * 1 = 5.4
            var actual = ArmourModel.EffectiveDamage(18f, DamageKind.Blunt, 50f);

            Assert.Equal(5.4f, actual, Precision);
        }

        [Fact]
        public void EffectiveDamage_Blunt_ExactlyAtThreshold_PenetratingTermIsStillZero()
        {
            // magnitude 20 -> mitigated = 20*0.5 = 10, exactly the threshold of 10.
            // penetrating = max(0, 10-10) = 0.
            // result = (0.6*10 + 0.4*0) * 1 = 6.0
            var actual = ArmourModel.EffectiveDamage(20f, DamageKind.Blunt, 50f);

            Assert.Equal(6.0f, actual, Precision);
        }

        [Fact]
        public void EffectiveDamage_Blunt_JustAboveThreshold_BlendsInThePenetratingTerm()
        {
            // magnitude 22 -> mitigated = 22*0.5 = 11, one above the threshold of 10.
            // penetrating = max(0, 11-10) = 1.
            // result = (0.6*11 + 0.4*1) * 1 = 6.6 + 0.4 = 7.0
            var actual = ArmourModel.EffectiveDamage(22f, DamageKind.Blunt, 50f);

            Assert.Equal(7.0f, actual, Precision);
        }

        // ---- Zero armour: mitigation = 50/50 = 1.0, threshold = 0*factor = 0 for every kind,
        //      so penetrating = mitigated = magnitude and the blend collapses to plain magnitude
        //      regardless of bluntFactor (bluntFactor*x + (1-bluntFactor)*x == x). ----

        [Fact]
        public void EffectiveDamage_ZeroArmourCut_CollapsesToRawMagnitude()
        {
            var actual = ArmourModel.EffectiveDamage(40f, DamageKind.Cut, 0f);

            Assert.Equal(40f, actual, Precision);
        }

        [Fact]
        public void EffectiveDamage_ZeroArmourPierce_CollapsesToRawMagnitude()
        {
            var actual = ArmourModel.EffectiveDamage(40f, DamageKind.Pierce, 0f);

            Assert.Equal(40f, actual, Precision);
        }

        [Fact]
        public void EffectiveDamage_ZeroArmourBlunt_CollapsesToRawMagnitude()
        {
            var actual = ArmourModel.EffectiveDamage(40f, DamageKind.Blunt, 0f);

            Assert.Equal(40f, actual, Precision);
        }

        // ---- absorbedDamageRatio: final multiplier, defaults to 1.0 in the 3-arg overload ----

        [Fact]
        public void EffectiveDamage_AbsorbedDamageRatioHalf_HalvesTheFullRatioResult()
        {
            // Reuses the Cut "just above threshold" case (magnitude 52, armour 50), whose
            // full-ratio result is 3.5 (see EffectiveDamage_Cut_JustAboveThreshold_...).
            // At absorbedDamageRatio 0.5 the whole result is halved: 3.5 * 0.5 = 1.75
            var actual = ArmourModel.EffectiveDamage(52f, DamageKind.Cut, 50f, 0.5f);

            Assert.Equal(1.75f, actual, Precision);
        }

        [Fact]
        public void EffectiveDamage_ThreeArgOverload_DefaultsAbsorbedDamageRatioToOne()
        {
            // The 3-arg overload must be equivalent to calling the 4-arg overload with
            // absorbedDamageRatio = 1.0 - both must reproduce the same 3.5 as the
            // "just above threshold" Cut case.
            var viaDefaultOverload = ArmourModel.EffectiveDamage(52f, DamageKind.Cut, 50f);
            var viaExplicitFullRatio = ArmourModel.EffectiveDamage(52f, DamageKind.Cut, 50f, 1.0f);

            Assert.Equal(3.5f, viaDefaultOverload, Precision);
            Assert.Equal(viaExplicitFullRatio, viaDefaultOverload, Precision);
        }

        // ---- AC2: the whole point of modelling armour - a lower raw-magnitude Pierce hit that
        //      penetrates must outscore a higher raw-magnitude Cut hit that does not, against the
        //      same realistic armour value of 50. ----

        [Fact]
        public void EffectiveDamage_AC2_LowerMagnitudePierceThatPenetratesOutscoresHigherMagnitudeCutThatDoesNot()
        {
            // Cut, raw magnitude 48 (the HIGHER of the two): mitigated = 48*0.5 = 24, below the
            // Cut threshold of 25 -> no penetration.
            // result = (0.1*24 + 0.9*0) * 1 = 2.4
            var cutEffectiveDamage = ArmourModel.EffectiveDamage(48f, DamageKind.Cut, 50f);

            // Pierce, raw magnitude 40 (the LOWER of the two): mitigated = 40*0.5 = 20, past the
            // Pierce threshold of 50*0.33 = 16.5 -> penetrating = 20 - 16.5 = 3.5.
            // result = (0.25*20 + 0.75*3.5) * 1 = 5.0 + 2.625 = 7.625
            var pierceEffectiveDamage = ArmourModel.EffectiveDamage(40f, DamageKind.Pierce, 50f);

            Assert.Equal(2.4f, cutEffectiveDamage, Precision);
            Assert.Equal(7.625f, pierceEffectiveDamage, Precision);
            Assert.True(
                pierceEffectiveDamage > cutEffectiveDamage,
                $"Pierce (raw magnitude 40, effective {pierceEffectiveDamage}) should outscore " +
                $"Cut (raw magnitude 48, effective {cutEffectiveDamage}) because it penetrates the armour.");
        }
    }
}
