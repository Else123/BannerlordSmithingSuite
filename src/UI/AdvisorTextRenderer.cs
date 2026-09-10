using System.Collections.Generic;
using System.Linq;
using SmithingSuite.Advisor;
using TaleWorlds.Localization;

namespace SmithingSuite.UI
{
    /// <summary>
    /// The one place the crafting advisor's player-visible English lives, and the only place it is
    /// allowed to live.
    ///
    /// WHY the split exists: the advisor's judgement (which axes there are, why each has the value it
    /// has, whether a search result may be presented at all) is deliberately implemented in
    /// <c>SmithingSuite.Advisor</c>, which is engine-free precisely so the test project can compile
    /// and exercise it on the host .NET SDK without a game install - that is what closed the
    /// <c>HasLegalResult</c> refusal bug class, among others. But engine-free also means no
    /// <see cref="TextObject"/>, and vanilla resolves every player-visible string through a
    /// <c>{=id}</c> TextObject. Building the sentences down in the Advisor layer would therefore have
    /// left the advisor permanently English-only in its most visible feature; building them back up
    /// in <c>WeaponDesignAdvisorMixin</c> would have made them untestable again. So the Advisor layer
    /// returns DATA (keys + typed numbers) and this class - which lives on the engine side of the
    /// boundary and is never compiled by the test project - renders that data into localisable text.
    ///
    /// Consequences worth keeping: a translator can change every word below without touching a single
    /// line of tested logic, and conversely the tests assert on keys and numbers, so a copy-edit here
    /// can no longer break them. The English inside each <c>{=id}</c> literal is the FALLBACK used
    /// until a module_strings entry for that id exists.
    /// </summary>
    internal static class AdvisorTextRenderer
    {
        // ---- Popup chrome -------------------------------------------------------------------

        public static string Title
        {
            get { return Localized("{=SmithingSuite_Advisor_Title}Crafting Advisor"); }
        }

        public static string SuggestButton
        {
            get { return Localized("{=SmithingSuite_Advisor_SuggestBest}Suggest Best"); }
        }

        public static string NoDesign
        {
            get
            {
                return Localized("{=SmithingSuite_Advisor_NoDesign}No design to score yet - pick "
                    + "pieces for this weapon and the advisor will rate what you are building.");
            }
        }

        public static string NoUnlockedPieces
        {
            get
            {
                return Localized("{=SmithingSuite_Advisor_NoPieces}No unlocked pieces are available "
                    + "to search for this template.");
            }
        }

        public static string TriggerHint
        {
            get
            {
                return Localized("{=SmithingSuite_Advisor_TriggerHint}Open the crafting advisor: "
                    + "scored axes with a plain-language reason for each, for the design you are "
                    + "building right now.");
            }
        }

        public static string SuggestHint
        {
            get
            {
                return Localized("{=SmithingSuite_Advisor_SuggestHint}Search this template's "
                    + "unlocked pieces for the best-scoring design your smith can actually craft. "
                    + "Runs under a time budget and reports the best design found.");
            }
        }

        public static string ContextHint
        {
            get
            {
                return Localized("{=SmithingSuite_Advisor_ContextHint}Switch the scoring weights "
                    + "between fighting on foot and fighting from horseback.");
            }
        }

        public static string CloseHint
        {
            get
            {
                return Localized("{=SmithingSuite_Advisor_CloseHint}Close the advisor and go back "
                    + "to the design.");
            }
        }

        /// <summary>The context toggle button's own caption - distinct from <see cref="ContextWord"/>, which reads mid-sentence.</summary>
        public static string ContextButtonLabel(MountedContext context)
        {
            return context == MountedContext.Mounted
                ? Localized("{=SmithingSuite_Advisor_ContextButton_Mounted}Context: Mounted")
                : Localized("{=SmithingSuite_Advisor_ContextButton_OnFoot}Context: On Foot");
        }

        // ---- Axes ---------------------------------------------------------------------------

        public static string AxisName(AdvisorAxisKind kind)
        {
            switch (kind)
            {
                case AdvisorAxisKind.EffectiveDamage:
                    return Localized("{=SmithingSuite_Advisor_Axis_EffectiveDamage}Effective Damage");
                case AdvisorAxisKind.AttackSpeed:
                    return Localized("{=SmithingSuite_Advisor_Axis_AttackSpeed}Attack Speed");
                case AdvisorAxisKind.Reach:
                    return Localized("{=SmithingSuite_Advisor_Axis_Reach}Reach");
                case AdvisorAxisKind.Handling:
                    return Localized("{=SmithingSuite_Advisor_Axis_Handling}Handling");
                case AdvisorAxisKind.SaleValue:
                    return Localized("{=SmithingSuite_Advisor_Axis_SaleValue}Sale Value");
                default:
                    // An axis kind added to the enum but not yet given a caption here degrades to an
                    // unlabelled row next to a still-correct number, rather than throwing inside the
                    // mixin's fail-safe and taking the whole popup down with it.
                    return string.Empty;
            }
        }

        public static string Reason(AxisReason reason)
        {
            if (reason == null)
            {
                return string.Empty;
            }

            switch (reason.Key)
            {
                case AdvisorReasonKey.EffectiveDamageAcrossArmourTiers:
                    return EffectiveDamageReason(reason);
                case AdvisorReasonKey.AttackSpeedOfChosenMode:
                    return AttackSpeedReason(reason);
                case AdvisorReasonKey.ReachFromWeaponLength:
                    return ReachReason(reason);
                case AdvisorReasonKey.HandlingBetweenAttacks:
                    return HandlingReason(reason);
                case AdvisorReasonKey.SaleValueKeptSeparate:
                    return SaleValueReason(reason);
                default:
                    // Same degradation policy as AxisName: no explanation beats no popup.
                    return string.Empty;
            }
        }

        private static string EffectiveDamageReason(AxisReason reason)
        {
            var text = new TextObject(
                "{=SmithingSuite_Advisor_Reason_EffectiveDamage}Best mode is {MODE} ({RAW_DAMAGE} "
                + "{DAMAGE_TYPE} dmg): {EFFECTIVE_LIGHT} effective dmg vs light armour "
                + "({ARMOUR_LIGHT}), {EFFECTIVE_MEDIUM} vs medium armour ({ARMOUR_MEDIUM}, the "
                + "score's baseline), {EFFECTIVE_HEAVY} vs heavy armour ({ARMOUR_HEAVY}) - computed "
                + "via the engine's verified armour formula, never the raw damage number.", null);
            text.SetTextVariable("MODE", ModeWord(reason.Mode));
            text.SetTextVariable("RAW_DAMAGE", Whole(reason.RawDamage));
            text.SetTextVariable("DAMAGE_TYPE", DamageTypeWord(reason.DamageType));
            text.SetTextVariable("EFFECTIVE_LIGHT", Precise(reason.EffectiveAtLight));
            text.SetTextVariable("EFFECTIVE_MEDIUM", Precise(reason.EffectiveAtMedium));
            text.SetTextVariable("EFFECTIVE_HEAVY", Precise(reason.EffectiveAtHeavy));
            text.SetTextVariable("ARMOUR_LIGHT", Whole(reason.LightArmour));
            text.SetTextVariable("ARMOUR_MEDIUM", Whole(reason.MediumArmour));
            text.SetTextVariable("ARMOUR_HEAVY", Whole(reason.HeavyArmour));
            return text.ToString();
        }

        private static string AttackSpeedReason(AxisReason reason)
        {
            var text = new TextObject(
                "{=SmithingSuite_Advisor_Reason_AttackSpeed}{MODE} speed is {SPEED} - the mode "
                + "reported here is the one chosen above because it penetrates better at medium "
                + "armour.", null);
            text.SetTextVariable("MODE", ModeWord(reason.Mode));
            text.SetTextVariable("SPEED", Whole(reason.Stat));
            return text.ToString();
        }

        private static string ReachReason(AxisReason reason)
        {
            var text = new TextObject(
                "{=SmithingSuite_Advisor_Reason_Reach}Weapon length is {LENGTH} - more reach means "
                + "hitting the opponent before they can hit back.", null);
            text.SetTextVariable("LENGTH", Whole(reason.Stat));
            return text.ToString();
        }

        private static string HandlingReason(AxisReason reason)
        {
            var text = new TextObject(
                "{=SmithingSuite_Advisor_Reason_Handling}Handling is {HANDLING} - how quickly the "
                + "weapon can be redirected between attacks.", null);
            text.SetTextVariable("HANDLING", Whole(reason.Stat));
            return text.ToString();
        }

        private static string SaleValueReason(AxisReason reason)
        {
            var text = new TextObject(
                "{=SmithingSuite_Advisor_Reason_SaleValue}Sells for {GOLD} gold - kept as its own "
                + "axis, never folded into the combat score, because a good fighter and a good "
                + "earner are not always the same weapon.", null);
            text.SetTextVariable("GOLD", Gold(reason.Stat));
            return text.ToString();
        }

        // ---- Verdict and suggestion ---------------------------------------------------------

        public static string Verdict(VerdictData verdict)
        {
            var text = new TextObject(
                "{=SmithingSuite_Advisor_Verdict}Combat score ({CONTEXT}): {COMBAT_TOTAL} - sale "
                + "value {GOLD} gold. Axes below explain why.", null);
            text.SetTextVariable("CONTEXT", ContextWord(verdict.Context));
            text.SetTextVariable("COMBAT_TOTAL", Whole(verdict.CombatTotal));
            text.SetTextVariable("GOLD", Gold(verdict.SaleValue));
            return text.ToString();
        }

        public static string Suggestion(SuggestionData suggestion)
        {
            if (suggestion.Key == AdvisorSuggestionKey.NoImprovement)
            {
                return Localized("{=SmithingSuite_Advisor_NoImprovement}No craftable improvement "
                    + "was found within the current smithing skill and unlocked parts.");
            }

            var text = new TextObject(
                "{=SmithingSuite_Advisor_Suggestion}Suggested: {PIECES} -> combat score "
                + "{COMBAT_TOTAL}, sale value {GOLD} gold{BUDGET_NOTE}.", null);
            text.SetTextVariable("PIECES", PieceList(suggestion.Pieces));
            text.SetTextVariable("COMBAT_TOTAL", Whole(suggestion.CombatTotal));
            text.SetTextVariable("GOLD", Gold(suggestion.SaleValue));
            // The separating space is concatenated OUTSIDE the localised literal: a "{=id}" value
            // that begins with whitespace is a needlessly fragile thing to rely on, and a translator
            // should not have to preserve a leading space to keep the sentence spaced correctly.
            text.SetTextVariable("BUDGET_NOTE", suggestion.BudgetExhausted
                ? " " + Localized("{=SmithingSuite_Advisor_BudgetExhausted}(search budget exhausted - best found so far)")
                : string.Empty);
            return text.ToString();
        }

        private static string PieceList(IReadOnlyList<SuggestionPiece> pieces)
        {
            return string.Join(", ", pieces.Select(RenderPiece));
        }

        private static string RenderPiece(SuggestionPiece piece)
        {
            var text = new TextObject(
                "{=SmithingSuite_Advisor_SuggestionPiece}{PIECE_KIND} {PIECE_ID} @{SCALE}%", null);
            text.SetTextVariable("PIECE_KIND", PieceKindWord(piece.Kind));
            text.SetTextVariable("PIECE_ID", piece.PieceId);
            text.SetTextVariable("SCALE", piece.ScalePercentage.ToString());
            return text.ToString();
        }

        // ---- Words for enum values ----------------------------------------------------------

        private static string PieceKindWord(AdvisorPieceKind kind)
        {
            switch (kind)
            {
                case AdvisorPieceKind.Blade:
                    return Localized("{=SmithingSuite_Advisor_Piece_Blade}Blade");
                case AdvisorPieceKind.Guard:
                    return Localized("{=SmithingSuite_Advisor_Piece_Guard}Guard");
                case AdvisorPieceKind.Handle:
                    return Localized("{=SmithingSuite_Advisor_Piece_Handle}Handle");
                case AdvisorPieceKind.Pommel:
                    return Localized("{=SmithingSuite_Advisor_Piece_Pommel}Pommel");
                case AdvisorPieceKind.Unknown:
                default:
                    return Localized("{=SmithingSuite_Advisor_Piece_Unknown}Piece");
            }
        }

        private static string ModeWord(AttackMode mode)
        {
            return mode == AttackMode.Thrust
                ? Localized("{=SmithingSuite_Advisor_Mode_Thrust}thrust")
                : Localized("{=SmithingSuite_Advisor_Mode_Swing}swing");
        }

        private static string ContextWord(MountedContext context)
        {
            return context == MountedContext.Mounted
                ? Localized("{=SmithingSuite_Advisor_Context_Mounted}mounted")
                : Localized("{=SmithingSuite_Advisor_Context_OnFoot}on foot");
        }

        private static string DamageTypeWord(DamageKind kind)
        {
            switch (kind)
            {
                case DamageKind.Cut:
                    return Localized("{=SmithingSuite_Advisor_DamageType_Cut}Cut");
                case DamageKind.Pierce:
                    return Localized("{=SmithingSuite_Advisor_DamageType_Pierce}Pierce");
                case DamageKind.Blunt:
                    return Localized("{=SmithingSuite_Advisor_DamageType_Blunt}Blunt");
                default:
                    // An engine damage type outside the three the armour model covers (e.g. vanilla's
                    // Invalid = -1): name nothing rather than print a raw ordinal at the player.
                    return string.Empty;
            }
        }

        // ---- Number formatting --------------------------------------------------------------
        //
        // The format specifiers below are the ones the advisor already used before these strings
        // became TextObjects, so the rendered text does not change. Numbers are pre-formatted into
        // string variables rather than passed as numeric ones because no advisor sentence carries a
        // COUNT: every number here is a measurement (damage, speed, length, armour value, gold,
        // percentage), so none of them drives a plural form or any other numeric condition.

        private static string Whole(float value)
        {
            return value.ToString("F0");
        }

        private static string Precise(float value)
        {
            return value.ToString("F1");
        }

        /// <summary>
        /// Gold is always a whole number of denars from the engine, and was previously printed by
        /// plain concatenation - keep that exact formatting so the line does not gain or lose digits.
        /// </summary>
        private static string Gold(float value)
        {
            return value.ToString();
        }

        /// <summary>
        /// Every string this renderer produces goes through here: the advisor's prefabs carry no
        /// literal display text (vanilla prefabs carry none either), so the "{=id}Fallback" form is
        /// what makes these translatable without a module_strings entry having to exist yet.
        /// </summary>
        private static string Localized(string keyedText)
        {
            return new TextObject(keyedText, null).ToString();
        }
    }
}
