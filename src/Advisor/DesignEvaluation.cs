namespace SmithingSuite.Advisor
{
    /// <summary>
    /// What evaluating one <see cref="DesignCandidate"/> yields: the engine-computed stats plus the
    /// crafting difficulty, so the optimizer can score a design and gate it against the smith's
    /// skill in a single round trip through <see cref="IDesignEvaluator"/>. Immutable, same rationale
    /// as <see cref="WeaponStats"/> - it is a read-only carrier of numbers the engine produced.
    /// </summary>
    public sealed class DesignEvaluation
    {
        public WeaponStats Stats { get; }

        /// <summary>
        /// From vanilla <c>SmithingModel.CalculateWeaponDesignDifficulty</c>. The optimizer needs
        /// this to reject designs the smith cannot actually craft (AC4) - a design that scores well
        /// but exceeds the hero's smithing skill is not a usable suggestion.
        /// </summary>
        public int Difficulty { get; }

        public DesignEvaluation(WeaponStats stats, int difficulty)
        {
            Stats = stats;
            Difficulty = difficulty;
        }
    }
}
