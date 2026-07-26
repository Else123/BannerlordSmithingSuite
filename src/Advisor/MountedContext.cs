namespace SmithingSuite.Advisor
{
    /// <summary>
    /// Which situation the player wants a weapon design scored for. Reach matters more when mounted
    /// (leaning out of the saddle to hit before being hit), while raw swing speed matters more on
    /// foot (footwork closes the gap that a horse otherwise closes for you) - so the axis weights the
    /// advisor applies must differ per context rather than using one fixed blend. The user asked for
    /// this split explicitly: "bin ich beritten oder nicht" is one of the named factors.
    /// </summary>
    public enum MountedContext
    {
        OnFoot = 0,
        Mounted = 1
    }
}
