namespace SmithingSuite.Logic
{
    /// <summary>
    /// Requests a message's colour from <see cref="ISmithingConnector.Report"/> without this
    /// engine-free layer referencing <c>TaleWorlds.Library.Color</c> directly. The connector maps
    /// <see cref="Positive"/> to vanilla's own "good outcome" green (<c>new Color(0f, 1f, 0f)</c>,
    /// verified by decompiling vanilla's own completion messages); <see cref="Neutral"/> leaves the
    /// default colour, matching how vanilla reports the overwhelming majority of its messages.
    /// </summary>
    public enum ReportTone
    {
        Neutral,
        Positive
    }
}
