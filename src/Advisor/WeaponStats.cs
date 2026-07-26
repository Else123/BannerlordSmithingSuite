namespace SmithingSuite.Advisor
{
    /// <summary>
    /// Immutable carrier for the numbers the ENGINE produced for one weapon design. The advisor
    /// never models or re-derives these - it only reads them. They come from
    /// <c>Crafting.GenerateItem</c> (see SPEC-SLICE4-5.md, "Verified engine anchors"), so they are
    /// engine truth: inventing or approximating swing/thrust speed, damage, handling, length,
    /// weight, tier, or value would defeat the whole point of scoring against what the game itself
    /// computed for this exact combination of pieces and scale.
    /// </summary>
    public sealed class WeaponStats
    {
        public float SwingSpeed { get; }
        public float SwingDamage { get; }
        public DamageKind SwingDamageType { get; }
        public float ThrustSpeed { get; }
        public float ThrustDamage { get; }
        public DamageKind ThrustDamageType { get; }
        public float Handling { get; }
        public float WeaponLength { get; }
        public float Weight { get; }
        public int Tier { get; }
        public int Value { get; }

        public WeaponStats(
            float swingSpeed,
            float swingDamage,
            DamageKind swingDamageType,
            float thrustSpeed,
            float thrustDamage,
            DamageKind thrustDamageType,
            float handling,
            float weaponLength,
            float weight,
            int tier,
            int value)
        {
            SwingSpeed = swingSpeed;
            SwingDamage = swingDamage;
            SwingDamageType = swingDamageType;
            ThrustSpeed = thrustSpeed;
            ThrustDamage = thrustDamage;
            ThrustDamageType = thrustDamageType;
            Handling = handling;
            WeaponLength = weaponLength;
            Weight = weight;
            Tier = tier;
            Value = value;
        }
    }
}
