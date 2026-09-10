namespace SmithingSuite.Advisor
{
    /// <summary>
    /// The one way of attacking that <see cref="WeaponScorer"/> committed to when it scored a design.
    /// A weapon swings and thrusts differently, so every axis of a single score must describe the
    /// same mode - reporting swing's speed next to thrust's damage would describe an attack the
    /// weapon cannot actually make. Carried as an enum rather than the word "swing"/"thrust" so the
    /// Advisor layer stays engine-free and the UI can translate it.
    /// </summary>
    public enum AttackMode
    {
        Swing = 0,
        Thrust = 1
    }
}
