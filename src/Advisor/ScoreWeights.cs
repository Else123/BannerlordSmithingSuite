namespace SmithingSuite.Advisor
{
    /// <summary>
    /// Weights that turn <see cref="WeaponScore"/>'s named combat axes into a single
    /// <see cref="WeaponScore.CombatTotal"/> for ranking. These are an OPINION, not truth: no
    /// public source quantifies a speed-vs-damage (or speed-vs-reach) trade-off for Bannerlord's
    /// combat (see SPEC-SLICE4-5.md, "Honest limitation, carried from research"). Effective damage
    /// itself is engine-verified via <see cref="ArmourModel"/>; only the BLEND between it, speed,
    /// reach, and handling below is a tunable default the player is free to disagree with.
    ///
    /// Deliberately plain public fields rather than immutable properties: Slice 6 bridges these
    /// straight from MCM sliders, which write into an existing instance rather than replace it.
    /// </summary>
    public sealed class ScoreWeights
    {
        public float EffectiveDamageWeight;
        public float AttackSpeedWeight;
        public float ReachWeight;
        public float HandlingWeight;

        public ScoreWeights(float effectiveDamageWeight, float attackSpeedWeight, float reachWeight, float handlingWeight)
        {
            EffectiveDamageWeight = effectiveDamageWeight;
            AttackSpeedWeight = attackSpeedWeight;
            ReachWeight = reachWeight;
            HandlingWeight = handlingWeight;
        }

        // Mutable defaults rather than literals baked into ForContext, so Slice 6's MCM bridge
        // (SmithingSuiteMcmSettings.Apply()) can overwrite the player's chosen blend on each use,
        // the same way it bridges every other setting into a plain static holder - without
        // ForContext's callers needing to know MCM exists. Values below are the original
        // opinionated defaults (see ForContext's doc) and are exactly what every existing test
        // still gets when MCM has never run.
        public static float OnFootEffectiveDamageWeight = 1.0f;
        public static float OnFootAttackSpeedWeight = 1.0f;
        public static float OnFootReachWeight = 0.2f;
        public static float OnFootHandlingWeight = 0.5f;

        public static float MountedEffectiveDamageWeight = 1.0f;
        public static float MountedAttackSpeedWeight = 0.4f;
        public static float MountedReachWeight = 1.2f;
        public static float MountedHandlingWeight = 0.3f;

        /// <summary>
        /// Opinionated defaults, per the user's explicit split ("bin ich beritten oder nicht"):
        /// on foot, footwork closes the distance, so swing speed and handling matter more than
        /// reach; mounted, the horse closes the distance instead, so reach - hitting before being
        /// hit - is weighted above speed/handling. Effective damage is weighted identically in both
        /// contexts: it is engine truth either way, only the speed/reach trade-off is situational.
        /// </summary>
        public static ScoreWeights ForContext(MountedContext context)
        {
            switch (context)
            {
                case MountedContext.Mounted:
                    return new ScoreWeights(
                        effectiveDamageWeight: MountedEffectiveDamageWeight,
                        attackSpeedWeight: MountedAttackSpeedWeight,
                        reachWeight: MountedReachWeight,
                        handlingWeight: MountedHandlingWeight);
                case MountedContext.OnFoot:
                default:
                    return new ScoreWeights(
                        effectiveDamageWeight: OnFootEffectiveDamageWeight,
                        attackSpeedWeight: OnFootAttackSpeedWeight,
                        reachWeight: OnFootReachWeight,
                        handlingWeight: OnFootHandlingWeight);
            }
        }
    }
}
