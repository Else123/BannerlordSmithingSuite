namespace SmithingSuite.Logic
{
    /// <summary>
    /// Engine-free formatting of error lines in the mod log. Error lines are written regardless of
    /// VerboseLogging and always carry <see cref="ErrorTag"/>, so a script or an in-game health
    /// check can detect failures by searching for that one tag (see HealthScenario).
    /// </summary>
    public static class LogMarkers
    {
        public const string ErrorTag = "[ERROR]";

        /// <summary>"[HH:mm:ss.fff] [ERROR] message" - same timestamp prefix as an ordinary line.</summary>
        public static string FormatError(string timestamp, string message)
        {
            return "[" + timestamp + "] " + ErrorTag + " " + message;
        }
    }
}
