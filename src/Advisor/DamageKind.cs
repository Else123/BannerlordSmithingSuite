namespace SmithingSuite.Advisor
{
    /// <summary>
    /// Engine-free mirror of <c>TaleWorlds.Core.DamageTypes</c> (Invalid = -1, Cut, Pierce, Blunt,
    /// NumberOfDamageTypes). The Advisor layer must stay free of any TaleWorlds reference so it can
    /// be compiled and unit-tested on the host .NET SDK without a game install - so the real enum
    /// cannot be reused directly. The ordinals below are kept deliberately aligned with the engine's
    /// (Cut = 0, Pierce = 1, Blunt = 2) so the Integration layer can cast between the two rather than
    /// needing a translation table at the boundary.
    /// </summary>
    public enum DamageKind
    {
        Cut = 0,
        Pierce = 1,
        Blunt = 2
    }
}
