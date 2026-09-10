namespace SmithingSuite.Advisor
{
    /// <summary>
    /// Which explanation a <see cref="ScoreAxis"/> gives for its value. One member per sentence the
    /// advisor can say about an axis; the sentence itself lives in the UI layer's text renderer, so a
    /// translator can change the words without any engine-free code changing at all.
    /// </summary>
    public enum AdvisorReasonKey
    {
        /// <summary>Effective damage of the chosen mode across the light/medium/heavy armour tiers.</summary>
        EffectiveDamageAcrossArmourTiers = 0,

        /// <summary>The chosen attack mode's speed, and why that mode is the one being reported.</summary>
        AttackSpeedOfChosenMode = 1,

        /// <summary>Weapon length, and what reach buys in a fight.</summary>
        ReachFromWeaponLength = 2,

        /// <summary>Handling, and what it buys between attacks.</summary>
        HandlingBetweenAttacks = 3,

        /// <summary>Gold value, and why it is never folded into the combat score.</summary>
        SaleValueKeptSeparate = 4
    }

    /// <summary>
    /// Why one <see cref="ScoreAxis"/> has the value it has: a <see cref="AdvisorReasonKey"/> plus the
    /// numbers that key's sentence needs, never a rendered sentence. The user's whole complaint about
    /// vanilla's crafting screen is that it shows numbers with no judgement (SPEC-SLICE4-5.md
    /// "Problem"), so every axis must still be able to explain itself (AC10) - but the Advisor layer
    /// is engine-free and cannot reach <c>TextObject</c>, so it hands the UI the facts and the UI
    /// alone owns the words.
    ///
    /// Deliberately ONE carrier with per-key static factories rather than a type per key: every
    /// consumer switches on <see cref="Key"/> anyway, and a small class hierarchy would buy nothing
    /// but casts at the single rendering site. Fields a key does not use stay at their zero default;
    /// the factory that builds a reason is the definition of which fields that key means.
    /// </summary>
    public sealed class AxisReason
    {
        public AdvisorReasonKey Key { get; }

        /// <summary>The attack mode this reason talks about. Meaningful only for the two combat-mode keys.</summary>
        public AttackMode Mode { get; }

        /// <summary>The chosen mode's damage type. Meaningful only for <see cref="AdvisorReasonKey.EffectiveDamageAcrossArmourTiers"/>.</summary>
        public DamageKind DamageType { get; }

        /// <summary>The chosen mode's RAW damage - quoted for context, never used as the axis value.</summary>
        public float RawDamage { get; }

        public float LightArmour { get; }
        public float MediumArmour { get; }
        public float HeavyArmour { get; }

        public float EffectiveAtLight { get; }
        public float EffectiveAtMedium { get; }
        public float EffectiveAtHeavy { get; }

        /// <summary>
        /// The single number a one-number reason quotes: attack speed, weapon length, handling, or
        /// gold value. Always equal to the owning axis's <see cref="ScoreAxis.Value"/> for those keys,
        /// so an axis and its explanation can never drift apart.
        /// </summary>
        public float Stat { get; }

        private AxisReason(
            AdvisorReasonKey key,
            AttackMode mode,
            DamageKind damageType,
            float rawDamage,
            float lightArmour,
            float mediumArmour,
            float heavyArmour,
            float effectiveAtLight,
            float effectiveAtMedium,
            float effectiveAtHeavy,
            float stat)
        {
            Key = key;
            Mode = mode;
            DamageType = damageType;
            RawDamage = rawDamage;
            LightArmour = lightArmour;
            MediumArmour = mediumArmour;
            HeavyArmour = heavyArmour;
            EffectiveAtLight = effectiveAtLight;
            EffectiveAtMedium = effectiveAtMedium;
            EffectiveAtHeavy = effectiveAtHeavy;
            Stat = stat;
        }

        public static AxisReason EffectiveDamage(
            AttackMode mode,
            DamageKind damageType,
            float rawDamage,
            float lightArmour,
            float mediumArmour,
            float heavyArmour,
            float effectiveAtLight,
            float effectiveAtMedium,
            float effectiveAtHeavy)
        {
            return new AxisReason(
                AdvisorReasonKey.EffectiveDamageAcrossArmourTiers,
                mode,
                damageType,
                rawDamage,
                lightArmour,
                mediumArmour,
                heavyArmour,
                effectiveAtLight,
                effectiveAtMedium,
                effectiveAtHeavy,
                stat: 0f);
        }

        public static AxisReason AttackSpeed(AttackMode mode, float speed)
        {
            return new AxisReason(
                AdvisorReasonKey.AttackSpeedOfChosenMode,
                mode,
                DamageKind.Cut, 0f, 0f, 0f, 0f, 0f, 0f, 0f,
                stat: speed);
        }

        public static AxisReason Reach(float weaponLength)
        {
            return new AxisReason(
                AdvisorReasonKey.ReachFromWeaponLength,
                AttackMode.Swing,
                DamageKind.Cut, 0f, 0f, 0f, 0f, 0f, 0f, 0f,
                stat: weaponLength);
        }

        public static AxisReason Handling(float handling)
        {
            return new AxisReason(
                AdvisorReasonKey.HandlingBetweenAttacks,
                AttackMode.Swing,
                DamageKind.Cut, 0f, 0f, 0f, 0f, 0f, 0f, 0f,
                stat: handling);
        }

        public static AxisReason SaleValue(float goldValue)
        {
            return new AxisReason(
                AdvisorReasonKey.SaleValueKeptSeparate,
                AttackMode.Swing,
                DamageKind.Cut, 0f, 0f, 0f, 0f, 0f, 0f, 0f,
                stat: goldValue);
        }
    }
}
