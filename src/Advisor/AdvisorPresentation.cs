using System.Linq;

namespace SmithingSuite.Advisor
{
    /// <summary>
    /// Pure presentation functions over engine-free Advisor types: turning a <see cref="WeaponScore"/>
    /// or <see cref="OptimizationResult"/> into the strings the crafting-advisor panel displays.
    /// Moved out of <c>WeaponDesignAdvisorMixin</c> (a structural fix from architecture review) so
    /// these functions compile and are unit-tested on the host .NET SDK - the mixin itself only
    /// compiles inside the game, so nothing that lives there can be exercised by the test project.
    /// No behaviour or wording changed in the move.
    /// </summary>
    public static class AdvisorPresentation
    {
        public static string BuildVerdict(WeaponScore score, MountedContext context)
        {
            string contextLabel = context == MountedContext.Mounted ? "mounted" : "on foot";
            return "Combat score (" + contextLabel + "): " + score.CombatTotal.ToString("F0")
                + " - sale value " + score.SaleValue + " gold. Axes below explain why.";
        }

        public static string DescribeSuggestion(OptimizationResult result)
        {
            // HasLegalResult is the ONLY thing that makes result.Best trustworthy (FINDING 1) - an
            // unverified seed must never be shown as "Suggested", so this checks it before Score at
            // all, rather than trusting a non-null Score to imply legality.
            if (result == null || !result.HasLegalResult || result.Score == null)
            {
                return "No craftable improvement was found within the current smithing skill and unlocked parts.";
            }

            string pieceList = string.Join(", ", result.Best.Pieces
                .OrderBy(p => p.PieceType)
                .Select(p => PieceTypeName(p.PieceType) + " " + p.PieceId + " @" + p.ScalePercentage + "%"));

            string budgetNote = result.BudgetExhausted
                ? " (search budget exhausted - best found so far)"
                : string.Empty;

            return "Suggested: " + pieceList + " -> combat score " + result.Score.CombatTotal.ToString("F0")
                + ", sale value " + result.Score.SaleValue + " gold" + budgetNote + ".";
        }

        public static string PieceTypeName(int pieceType)
        {
            // Mirrors TaleWorlds.Core.CraftingPiece.PieceTypes (Blade=0, Guard=1, Handle=2, Pommel=3),
            // the same ordinals DesignOptimizer and EngineDesignEvaluator already assume.
            switch (pieceType)
            {
                case 0: return "Blade";
                case 1: return "Guard";
                case 2: return "Handle";
                case 3: return "Pommel";
                default: return "Piece";
            }
        }

        public static string ContextButtonLabel(bool mounted)
        {
            return mounted ? "Context: Mounted" : "Context: On Foot";
        }
    }
}
