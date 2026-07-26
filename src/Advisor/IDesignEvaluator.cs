namespace SmithingSuite.Advisor
{
    /// <summary>
    /// The seam between the search algorithm and the engine. The real implementation (Slice 5,
    /// <c>Integration/EngineDesignEvaluator.cs</c>) calls <c>TaleWorlds.Core.Crafting.GenerateItem</c>
    /// to actually build the item and read its computed stats - which means it needs a running game.
    /// Without this interface, <c>DesignOptimizer</c> could only be exercised inside Bannerlord, which
    /// would make its search logic (staged piece selection, scale hill-climbing, budget accounting)
    /// untestable on the host .NET SDK. Tests instead substitute a fake with hand-set
    /// <see cref="DesignEvaluation"/> results, so the search can be driven and asserted on
    /// deterministically without a game install.
    ///
    /// <see cref="Evaluate"/> is expected to be the EXPENSIVE call in the search - building an item
    /// via the engine is orders of magnitude slower than comparing scores - which is why
    /// <c>DesignOptimizer</c> works to a bounded evaluation budget (AC7) rather than exhausting the
    /// combinatorial space of pieces and scale steps.
    /// </summary>
    public interface IDesignEvaluator
    {
        DesignEvaluation Evaluate(DesignCandidate candidate);
    }
}
