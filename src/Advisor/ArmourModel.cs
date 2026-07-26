using System;

namespace SmithingSuite.Advisor
{
    /// <summary>
    /// Engine-verified mirror of <c>TaleWorlds.MountAndBlade.DefaultStrikeMagnitudeModel
    /// .ComputeRawDamage</c> (see SPEC-SLICE4-5.md, "The armour model - [OPEN-critical] CLOSED").
    /// This is engine truth, not a heuristic - the coefficients are hardcoded IL literals in the
    /// binary with no XML override, so they are reproduced verbatim here.
    /// </summary>
    public static class ArmourModel
    {
        /// <summary>
        /// Effective damage after armour, with <c>absorbedDamageRatio</c> defaulted to 1.0 (the
        /// value the engine uses for <c>AttackInformation.VictimAgentAbsorbedDamageRatio</c> in the
        /// normal case).
        /// </summary>
        public static float EffectiveDamage(float magnitude, DamageKind kind, float armour)
        {
            return EffectiveDamage(magnitude, kind, armour, 1f);
        }

        /// <summary>
        /// Effective damage after armour. This is a **blend**, never an either/or: the blunted term
        /// (<c>bluntFactor * mitigated</c>) always contributes, and the penetrating term
        /// (<c>(1 - bluntFactor) * max(0, mitigated - threshold)</c>) adds on top once the mitigated
        /// magnitude clears the threshold. Below the threshold the penetrating term is exactly zero
        /// and the result collapses to the blunted term alone - that collapse is why an older
        /// either/or description of this mechanic happened to look right there. Do not "simplify"
        /// this into a branch; the blend is the engine's actual behaviour.
        ///
        /// The threshold is subtracted from the already-mitigated magnitude, not the raw magnitude -
        /// mirroring the engine's own order of operations.
        /// </summary>
        public static float EffectiveDamage(float magnitude, DamageKind kind, float armour, float absorbedDamageRatio)
        {
            // Guard against negative armour defensively: the engine never passes a negative
            // armorEffectiveness, but a negative value here would push mitigation above 1.0 (via
            // 50/(50+armour) growing beyond 1 as the denominator shrinks below 50) and would flip
            // the threshold negative, so treat anything below 0 as 0.
            if (armour < 0f)
            {
                armour = 0f;
            }

            float bluntFactor;
            float threshold;
            switch (kind)
            {
                case DamageKind.Cut:
                    bluntFactor = 0.1f;
                    threshold = armour * 0.5f;
                    break;
                case DamageKind.Pierce:
                    bluntFactor = 0.25f;
                    threshold = armour * 0.33f;
                    break;
                case DamageKind.Blunt:
                    bluntFactor = 0.6f;
                    threshold = armour * 0.2f;
                    break;
                default:
                    // Mirrors the engine's default branch for an unrecognised damage type.
                    return 0f;
            }

            // Keep every step in float, matching the engine's own float arithmetic, so results do
            // not drift from what the game actually computes via an incidental double promotion.
            float mitigated = magnitude * (50f / (50f + armour));
            float penetrating = Math.Max(0f, mitigated - threshold);

            return (bluntFactor * mitigated + (1f - bluntFactor) * penetrating) * absorbedDamageRatio;
        }
    }
}
