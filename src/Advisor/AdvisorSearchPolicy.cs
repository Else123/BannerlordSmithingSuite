namespace SmithingSuite.Advisor
{
    /// <summary>
    /// The search space the shipped advisor actually uses: the scale steps
    /// <c>WeaponDesignAdvisorMixin.ExecuteSuggestBest</c> hands to <see cref="DesignOptimizer"/> via
    /// its <see cref="PieceCatalogue"/>, and the default scale a freshly-catalogued piece (or a
    /// piece with no current scale) starts at. Single source of truth for both the UI layer and
    /// <see cref="DesignOptimizer"/>'s own internal default - previously these were two separate
    /// literals (one in the mixin, one in the optimizer) that happened to agree by coincidence
    /// rather than by construction.
    ///
    /// The engine exposes <c>WeaponDesignElement.ScalePercentage</c> as an arbitrary int with no
    /// built-in min/max (<c>TaleWorlds.Core.WeaponDesignElement</c>) - vanilla's own crafting UI
    /// never exposes a scale slider at all, so there is no existing convention to mirror here. This
    /// is a labelled OPINION, exactly like <see cref="ScoreWeights"/> (SPEC-SLICE4-5.md "Honest
    /// limitation"): <see cref="ScaleSteps"/> is wide enough that a scaled winner can actually show
    /// up (AC6), narrow enough that the suggested pieces still look like the weapon the player
    /// chose. This type is deliberately engine-free (no <c>TaleWorlds.*</c>) so it stays part of the
    /// unit-tested Advisor layer, not the UI layer that only the game can compile.
    /// </summary>
    public static class AdvisorSearchPolicy
    {
        public static readonly int[] ScaleSteps = { 80, 90, 100, 110, 120 };

        public const int DefaultScalePercentage = 100;
    }
}
