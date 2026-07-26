using System;
using System.Collections.Generic;

namespace SmithingSuite.Advisor
{
    /// <summary>
    /// One named, explained facet of a weapon's score. The user's whole complaint about vanilla's
    /// crafting screen is that it shows numbers with no judgement (SPEC-SLICE4-5.md "Problem") - so
    /// every axis must be able to say, in plain language, why its value is what it is (AC10).
    /// </summary>
    public sealed class ScoreAxis
    {
        public string Name { get; }
        public float Value { get; }
        public string Reason { get; }

        public ScoreAxis(string name, float value, string reason)
        {
            Name = name;
            Value = value;
            Reason = reason;
        }
    }

    /// <summary>
    /// The scored result for one <see cref="WeaponStats"/>: named axes for display, a combat total
    /// for ranking, and sale value kept strictly separate (AC11) - the user sells crafted weapons as
    /// income, so "best fighter" and "best earner" must stay two different, comparable numbers
    /// rather than one blended figure that quietly favours whichever the weights happen to prefer.
    /// </summary>
    public sealed class WeaponScore
    {
        public IReadOnlyList<ScoreAxis> Axes { get; }

        /// <summary>Sum of the weighted combat axes only - never includes <see cref="SaleValue"/>.</summary>
        public float CombatTotal { get; }

        public float SaleValue { get; }

        public WeaponScore(IReadOnlyList<ScoreAxis> axes, float combatTotal, float saleValue)
        {
            Axes = axes;
            CombatTotal = combatTotal;
            SaleValue = saleValue;
        }
    }

    /// <summary>
    /// Turns engine-produced <see cref="WeaponStats"/> into a <see cref="WeaponScore"/>. Reads
    /// effective damage through <see cref="ArmourModel"/> rather than raw damage - that is the whole
    /// reason this scorer exists instead of just displaying the numbers the crafting screen already
    /// shows (see SPEC-SLICE4-5.md "The armour model").
    /// </summary>
    public static class WeaponScorer
    {
        // Representative armour tiers from SPEC-SLICE4-5.md ("WeaponScore bullet"). The composite
        // score is anchored to the middle tier; light/heavy are reported in the reason text only, so
        // the player sees how the weapon's standing shifts across armour rather than a single point
        // sample presented as the whole truth.
        private const float LightArmourTier = 20f;
        private const float MediumArmourTier = 40f;
        private const float HeavyArmourTier = 60f;

        public static WeaponScore Score(WeaponStats stats, MountedContext context, ScoreWeights weights)
        {
            if (stats == null)
            {
                throw new ArgumentNullException(nameof(stats));
            }
            if (weights == null)
            {
                throw new ArgumentNullException(nameof(weights));
            }

            // A weapon swings and thrusts differently - reporting swing's speed next to thrust's
            // damage (or vice versa) would describe an attack the weapon cannot actually make.
            // Evaluate both modes once, at the anchor tier, and commit to whichever one actually
            // gets through better there; every other axis below follows that same chosen mode so
            // the whole row of numbers describes one coherent way of fighting with this weapon.
            var swingEffectiveAtMedium = ArmourModel.EffectiveDamage(stats.SwingDamage, stats.SwingDamageType, MediumArmourTier);
            var thrustEffectiveAtMedium = ArmourModel.EffectiveDamage(stats.ThrustDamage, stats.ThrustDamageType, MediumArmourTier);
            var useThrust = thrustEffectiveAtMedium > swingEffectiveAtMedium;

            float chosenSpeed;
            float chosenDamage;
            DamageKind chosenDamageType;
            float chosenEffectiveAtMedium;
            string modeName;
            if (useThrust)
            {
                chosenSpeed = stats.ThrustSpeed;
                chosenDamage = stats.ThrustDamage;
                chosenDamageType = stats.ThrustDamageType;
                chosenEffectiveAtMedium = thrustEffectiveAtMedium;
                modeName = "thrust";
            }
            else
            {
                chosenSpeed = stats.SwingSpeed;
                chosenDamage = stats.SwingDamage;
                chosenDamageType = stats.SwingDamageType;
                chosenEffectiveAtMedium = swingEffectiveAtMedium;
                modeName = "swing";
            }

            var effectiveAtLight = ArmourModel.EffectiveDamage(chosenDamage, chosenDamageType, LightArmourTier);
            var effectiveAtHeavy = ArmourModel.EffectiveDamage(chosenDamage, chosenDamageType, HeavyArmourTier);

            var effectiveDamageAxis = new ScoreAxis(
                "Effective Damage",
                chosenEffectiveAtMedium,
                $"Best mode is {modeName} ({chosenDamage:F0} {chosenDamageType} dmg): {effectiveAtLight:F1} effective dmg vs light armour (20), " +
                $"{chosenEffectiveAtMedium:F1} vs medium armour (40, the score's baseline), {effectiveAtHeavy:F1} vs heavy armour (60) - " +
                "computed via the engine's verified armour formula, never the raw damage number.");

            var attackSpeedAxis = new ScoreAxis(
                "Attack Speed",
                chosenSpeed,
                $"{modeName} speed is {chosenSpeed:F0} - the mode reported here is the one chosen above because it penetrates better at medium armour.");

            var reachAxis = new ScoreAxis(
                "Reach",
                stats.WeaponLength,
                $"Weapon length is {stats.WeaponLength:F0} - more reach means hitting the opponent before they can hit back.");

            var handlingAxis = new ScoreAxis(
                "Handling",
                stats.Handling,
                $"Handling is {stats.Handling:F0} - how quickly the weapon can be redirected between attacks.");

            var saleValueAxis = new ScoreAxis(
                "Sale Value",
                stats.Value,
                $"Sells for {stats.Value} gold - kept as its own axis, never folded into the combat score, because a good fighter and a good earner are not always the same weapon.");

            var axes = new List<ScoreAxis>
            {
                effectiveDamageAxis,
                attackSpeedAxis,
                reachAxis,
                handlingAxis,
                saleValueAxis,
            };

            // Sale value is deliberately excluded here (AC11) - it is reported above as its own
            // axis and read from WeaponScore.SaleValue, never added into the ranking total.
            var combatTotal =
                weights.EffectiveDamageWeight * chosenEffectiveAtMedium +
                weights.AttackSpeedWeight * chosenSpeed +
                weights.ReachWeight * stats.WeaponLength +
                weights.HandlingWeight * stats.Handling;

            return new WeaponScore(axes, combatTotal, stats.Value);
        }
    }
}
