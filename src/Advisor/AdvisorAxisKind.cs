namespace SmithingSuite.Advisor
{
    /// <summary>
    /// Which facet of a weapon a <see cref="ScoreAxis"/> measures. The Advisor layer must stay free
    /// of any TaleWorlds reference (it is compiled by the test project on the host SDK), so it cannot
    /// build a localised axis title itself - it names the axis with this enum instead and lets the UI
    /// layer resolve it through a <c>TextObject</c>. That also makes the identity of an axis a value
    /// the compiler checks, rather than an English display string a copy-edit could silently break.
    /// </summary>
    public enum AdvisorAxisKind
    {
        EffectiveDamage = 0,
        AttackSpeed = 1,
        Reach = 2,
        Handling = 3,
        SaleValue = 4
    }
}
