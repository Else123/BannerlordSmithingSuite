using MCM.Abstractions.Attributes;
using MCM.Abstractions.Attributes.v2;
using MCM.Abstractions.Base.Global;
using SmithingSuite.Advisor;

namespace SmithingSuite
{
    /// <summary>
    /// Mod Configuration Menu (MCM) settings page for Smithing Suite - the single configuration
    /// surface (workspace convention: no bespoke settings UI). Mirrors the AutoTrader pattern
    /// (<c>AutoTraderMcmSettings</c>): this is the user-facing model, <see cref="SmithingSuiteConfig"/>
    /// and <see cref="ScoreWeights"/>'s static defaults stay the internal effective configuration
    /// the mod's logic reads, and <see cref="Apply"/> translates between them - bridged on each use
    /// rather than cached, exactly like the AutoTrader does, so a value changed mid-session takes
    /// effect on the next run without a restart (SPEC-SLICE6.md AC10).
    /// </summary>
    public sealed class SmithingSuiteMcmSettings : AttributeGlobalSettings<SmithingSuiteMcmSettings>
    {
        public override string Id => "SmithingSuite_v1";

        public override string DisplayName => "Smithing Suite";

        public override string FolderName => "SmithingSuite";

        public override string FormatType => "json2";

        private const string GeneralGroup = "1. General";
        private const string ProductionGroup = "2. Production";
        private const string TravelGroup = "3. Travel";
        private const string AdvisorGroup = "4. Advisor";

        // Shared by every one of the eight weight sliders below: no public source quantifies the
        // damage/speed/reach/handling trade-off for Bannerlord's combat (see ScoreWeights' own
        // doc and SPEC-SLICE4-5.md, "Honest limitation, carried from research"). Effective damage
        // itself IS engine truth either way; only the blend between the four axes is opinion, and
        // every hint below says so explicitly rather than presenting a slider as settled fact.
        private const string WeightOpinionNote = " This weight is OPINION, not engine truth - no public " +
            "source quantifies this trade-off for Bannerlord's combat. The effective-damage numbers " +
            "themselves are engine-verified; only the BLEND between axes is a tunable default you are " +
            "free to disagree with.";

        // --- 1. General --------------------------------------------------------

        [SettingPropertyBool("Verbose logging", Order = 0, RequireRestart = false,
            HintText = "Writes a detailed run log to Documents\\Mount and Blade II Bannerlord\\SmithingSuite\\smithingsuite.log.")]
        [SettingPropertyGroup(GeneralGroup, GroupOrder = 0)]
        public bool VerboseLogging { get; set; } = true;

        [SettingPropertyBool("Log to screen", Order = 1, RequireRestart = false,
            HintText = "Also post short phase messages to the in-game message log, in addition to the run summary shown after every use.")]
        [SettingPropertyGroup(GeneralGroup, GroupOrder = 0)]
        public bool LogToScreen { get; set; } = true;

        // --- 2. Production -------------------------------------------------------

        [SettingPropertyInteger("Smelt stamina share (%)", 0, 100, "0", Order = 0, RequireRestart = false,
            HintText = "The share of each smith's crafting stamina spent on smelting; the remainder is left for " +
                "refining. Set to 50 after an in-game run showed smelting consuming everything and refining never " +
                "running - see SPEC-SLICE6.md finding 1.")]
        [SettingPropertyGroup(ProductionGroup, GroupOrder = 1)]
        public int SmeltStaminaSharePercent { get; set; } = 50;

        // --- 3. Travel -------------------------------------------------------

        [SettingPropertyBool("Recover stamina while travelling", Order = 0, RequireRestart = false,
            HintText = "BALANCE CHANGE: vanilla only recovers a hero's crafting stamina while standing inside a " +
                "settlement. When on, heroes in the player's own party also recover it while travelling between " +
                "settlements, at the rate configured below. Never affects AI lords.")]
        [SettingPropertyGroup(TravelGroup, GroupOrder = 2)]
        public bool TravelStaminaEnabled { get; set; } = true;

        [SettingPropertyFloatingInteger("Travel recovery rate", 0.0f, 2.0f, "0.00", Order = 1, RequireRestart = false,
            HintText = "Multiplier on the travelling recovery rate. 1.0 means travelling recovers crafting stamina " +
                "at exactly the same rate as resting in a settlement.")]
        [SettingPropertyGroup(TravelGroup, GroupOrder = 2)]
        public float TravelStaminaMultiplier { get; set; } = 1.0f;

        // --- 4. Advisor -------------------------------------------------------

        [SettingPropertyInteger("Evaluation budget", 100, 2000, "0", Order = 0, RequireRestart = false,
            HintText = "Soft cap on how many candidate weapon designs the advisor evaluates per search. Higher " +
                "can find a marginally better design at the cost of a longer search. The time budget below is " +
                "the real safety net against a freeze - this only shapes search quality.")]
        [SettingPropertyGroup(AdvisorGroup, GroupOrder = 3)]
        public int AdvisorEvaluationBudget { get; set; } = 500;

        [SettingPropertyInteger("Time budget (ms)", 10, 500, "0", Order = 1, RequireRestart = false,
            HintText = "Hard wall-clock cap on one 'Suggest best' search. This is the real safety net against a " +
                "freeze: when it trips, the search stops immediately and returns the best design found so far, " +
                "labelled as such, regardless of how much of the evaluation budget above was left. Roughly 3 " +
                "frames at 60fps by default - a hitch, not a freeze.")]
        [SettingPropertyGroup(AdvisorGroup, GroupOrder = 3)]
        public int AdvisorTimeBudgetMs { get; set; } = 50;

        [SettingPropertyFloatingInteger("On foot: effective damage weight", 0.0f, 3.0f, "0.00", Order = 2, RequireRestart = false,
            HintText = "How much a design's effective damage counts towards its on-foot score." + WeightOpinionNote)]
        [SettingPropertyGroup(AdvisorGroup, GroupOrder = 3)]
        public float OnFootEffectiveDamageWeight { get; set; } = 1.0f;

        [SettingPropertyFloatingInteger("On foot: attack speed weight", 0.0f, 3.0f, "0.00", Order = 3, RequireRestart = false,
            HintText = "How much attack speed counts towards the on-foot score. On foot, footwork closes the " +
                "distance, so speed is weighted higher than reach by default." + WeightOpinionNote)]
        [SettingPropertyGroup(AdvisorGroup, GroupOrder = 3)]
        public float OnFootAttackSpeedWeight { get; set; } = 1.0f;

        [SettingPropertyFloatingInteger("On foot: reach weight", 0.0f, 3.0f, "0.00", Order = 4, RequireRestart = false,
            HintText = "How much reach counts towards the on-foot score." + WeightOpinionNote)]
        [SettingPropertyGroup(AdvisorGroup, GroupOrder = 3)]
        public float OnFootReachWeight { get; set; } = 0.2f;

        [SettingPropertyFloatingInteger("On foot: handling weight", 0.0f, 3.0f, "0.00", Order = 5, RequireRestart = false,
            HintText = "How much handling counts towards the on-foot score." + WeightOpinionNote)]
        [SettingPropertyGroup(AdvisorGroup, GroupOrder = 3)]
        public float OnFootHandlingWeight { get; set; } = 0.5f;

        [SettingPropertyFloatingInteger("Mounted: effective damage weight", 0.0f, 3.0f, "0.00", Order = 6, RequireRestart = false,
            HintText = "How much a design's effective damage counts towards its mounted score." + WeightOpinionNote)]
        [SettingPropertyGroup(AdvisorGroup, GroupOrder = 3)]
        public float MountedEffectiveDamageWeight { get; set; } = 1.0f;

        [SettingPropertyFloatingInteger("Mounted: attack speed weight", 0.0f, 3.0f, "0.00", Order = 7, RequireRestart = false,
            HintText = "How much attack speed counts towards the mounted score." + WeightOpinionNote)]
        [SettingPropertyGroup(AdvisorGroup, GroupOrder = 3)]
        public float MountedAttackSpeedWeight { get; set; } = 0.4f;

        [SettingPropertyFloatingInteger("Mounted: reach weight", 0.0f, 3.0f, "0.00", Order = 8, RequireRestart = false,
            HintText = "How much reach counts towards the mounted score. Mounted, the horse closes the distance " +
                "instead of footwork, so reach - hitting before being hit - is weighted higher than speed by default." +
                WeightOpinionNote)]
        [SettingPropertyGroup(AdvisorGroup, GroupOrder = 3)]
        public float MountedReachWeight { get; set; } = 1.2f;

        [SettingPropertyFloatingInteger("Mounted: handling weight", 0.0f, 3.0f, "0.00", Order = 9, RequireRestart = false,
            HintText = "How much handling counts towards the mounted score." + WeightOpinionNote)]
        [SettingPropertyGroup(AdvisorGroup, GroupOrder = 3)]
        public float MountedHandlingWeight { get; set; } = 0.3f;

        /// <summary>
        /// Translates this page into the effective configuration the mod's logic reads
        /// (<see cref="SmithingSuiteConfig"/> and <see cref="ScoreWeights"/>' static defaults).
        /// Safe no-op when MCM has not registered the settings yet, so the mod still works with
        /// MCM absent - same contract as <c>AutoTraderMcmSettings.Apply()</c>.
        /// </summary>
        public static void Apply()
        {
            SmithingSuiteMcmSettings s = Instance;
            if (s == null)
            {
                return;
            }

            SmithingSuiteConfig.VerboseLogging = s.VerboseLogging;
            SmithingSuiteConfig.LogToScreen = s.LogToScreen;

            SmithingSuiteConfig.SmeltStaminaSharePercent = s.SmeltStaminaSharePercent;

            SmithingSuiteConfig.TravelStaminaEnabled = s.TravelStaminaEnabled;
            SmithingSuiteConfig.TravelStaminaMultiplier = s.TravelStaminaMultiplier;

            SmithingSuiteConfig.AdvisorEvaluationBudget = s.AdvisorEvaluationBudget;
            SmithingSuiteConfig.AdvisorTimeBudgetMs = s.AdvisorTimeBudgetMs;

            ScoreWeights.OnFootEffectiveDamageWeight = s.OnFootEffectiveDamageWeight;
            ScoreWeights.OnFootAttackSpeedWeight = s.OnFootAttackSpeedWeight;
            ScoreWeights.OnFootReachWeight = s.OnFootReachWeight;
            ScoreWeights.OnFootHandlingWeight = s.OnFootHandlingWeight;

            ScoreWeights.MountedEffectiveDamageWeight = s.MountedEffectiveDamageWeight;
            ScoreWeights.MountedAttackSpeedWeight = s.MountedAttackSpeedWeight;
            ScoreWeights.MountedReachWeight = s.MountedReachWeight;
            ScoreWeights.MountedHandlingWeight = s.MountedHandlingWeight;
        }
    }
}
