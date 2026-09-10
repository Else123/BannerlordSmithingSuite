namespace SmithingSuite.Advisor
{
    /// <summary>
    /// Which slot of a weapon design a piece fills. Mirrors <c>TaleWorlds.Core.CraftingPiece.PieceTypes</c>
    /// (Blade = 0, Guard = 1, Handle = 2, Pommel = 3) - the same ordinals <see cref="DesignOptimizer"/>
    /// and <c>EngineDesignEvaluator</c> already assume - as an engine-free enum, so the Advisor layer
    /// can name a slot without owning the English word for it.
    /// </summary>
    public enum AdvisorPieceKind
    {
        Blade = 0,
        Guard = 1,
        Handle = 2,
        Pommel = 3,

        /// <summary>
        /// A piece type outside the four vanilla slots. Kept as an explicit member rather than a
        /// thrown exception because this value can come from engine data the advisor only reads.
        /// </summary>
        Unknown = 4
    }
}
