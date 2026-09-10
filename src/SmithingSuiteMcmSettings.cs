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

        private const string GeneralGroup = "General";
        private const string ProductionGroup = "Production";
        private const string TravelGroup = "Travel";
        private const string AdvisorGroup = "Advisor";

        // Shared by every one of the eight weight sliders below: no public source quantifies the
        // damage/speed/reach/handling trade-off for Bannerlord's combat (see ScoreWeights' own
        // doc and SPEC-SLICE4-5.md, "Honest limitation, carried from research"). Effective damage
        // itself IS engine truth either way; only the blend between the four axes is opinion.
        private const string WeightOpinionNote = " A tuning value, not a game rule.";

        // --- General --------------------------------------------------------

        [SettingPropertyBool("Verbose Logging", Order = 0, RequireRestart = false,
            HintText = "Writes a detailed log to Documents. Enable before reporting a bug.")]
        [SettingPropertyGroup(GeneralGroup, GroupOrder = 0)]
        public bool VerboseLogging { get; set; } = false;

        [SettingPropertyBool("Log to Screen", Order = 1, RequireRestart = false,
            HintText = "Also posts short phase messages to the in-game message log, in addition to the summary shown after every use.")]
        [SettingPropertyGroup(GeneralGroup, GroupOrder = 0)]
        public bool LogToScreen { get; set; } = false;

        // --- Production -------------------------------------------------------

        [SettingPropertyInteger("Smelt Stamina Share", 0, 100, "0", Order = 0, RequireRestart = false,
            HintText = "The share of each smith's crafting stamina spent on smelting; the remainder is left for refining.")]
        [SettingPropertyGroup(ProductionGroup, GroupOrder = 1)]
        public int SmeltStaminaSharePercent { get; set; } = 50;

        // --- Travel -------------------------------------------------------

        [SettingPropertyBool("Recover Stamina While Travelling", Order = 0, RequireRestart = false,
            HintText = "When enabled, heroes in your own party also recover crafting stamina while travelling " +
                "between settlements, not just inside one. Never affects AI-controlled parties.")]
        [SettingPropertyGroup(TravelGroup, GroupOrder = 2)]
        public bool TravelStaminaEnabled { get; set; } = true;

        [SettingPropertyFloatingInteger("Travel Recovery Rate", 0.0f, 2.0f, "0.00", Order = 1, RequireRestart = false,
            HintText = "Multiplier on the travelling recovery rate. 1.0 means travelling recovers crafting stamina " +
                "at exactly the same rate as resting in a settlement.")]
        [SettingPropertyGroup(TravelGroup, GroupOrder = 2)]
        public float TravelStaminaMultiplier { get; set; } = 1.0f;

        // --- Advisor -------------------------------------------------------

        [SettingPropertyInteger("Evaluation Budget", 100, 2000, "0", Order = 0, RequireRestart = false,
            HintText = "Soft cap on how many candidate weapon designs the advisor evaluates per search. Higher " +
                "values can find a marginally better design at the cost of a longer search.")]
        [SettingPropertyGroup(AdvisorGroup, GroupOrder = 3)]
        public int AdvisorEvaluationBudget { get; set; } = 500;

        [SettingPropertyInteger("Search Time Budget", 10, 500, "0", Order = 1, RequireRestart = false,
            HintText = "Hard wall-clock cap, in milliseconds, on one 'Suggest best' search. When it trips, the " +
                "search stops immediately and returns the best design found so far.")]
        [SettingPropertyGroup(AdvisorGroup, GroupOrder = 3)]
        public int AdvisorTimeBudgetMs { get; set; } = 50;

        [SettingPropertyFloatingInteger("On Foot Damage Weight", 0.0f, 3.0f, "0.00", Order = 2, RequireRestart = false,
            HintText = "How much a design's effective damage counts towards its on-foot score." + WeightOpinionNote)]
        [SettingPropertyGroup(AdvisorGroup, GroupOrder = 3)]
        public float OnFootEffectiveDamageWeight { get; set; } = 1.0f;

        [SettingPropertyFloatingInteger("On Foot Attack Speed Weight", 0.0f, 3.0f, "0.00", Order = 3, RequireRestart = false,
            HintText = "How much attack speed counts towards the on-foot score." + WeightOpinionNote)]
        [SettingPropertyGroup(AdvisorGroup, GroupOrder = 3)]
        public float OnFootAttackSpeedWeight { get; set; } = 1.0f;

        [SettingPropertyFloatingInteger("On Foot Reach Weight", 0.0f, 3.0f, "0.00", Order = 4, RequireRestart = false,
            HintText = "How much reach counts towards the on-foot score." + WeightOpinionNote)]
        [SettingPropertyGroup(AdvisorGroup, GroupOrder = 3)]
        public float OnFootReachWeight { get; set; } = 0.2f;

        [SettingPropertyFloatingInteger("On Foot Handling Weight", 0.0f, 3.0f, "0.00", Order = 5, RequireRestart = false,
            HintText = "How much handling counts towards the on-foot score." + WeightOpinionNote)]
        [SettingPropertyGroup(AdvisorGroup, GroupOrder = 3)]
        public float OnFootHandlingWeight { get; set; } = 0.5f;

        [SettingPropertyFloatingInteger("Mounted Damage Weight", 0.0f, 3.0f, "0.00", Order = 6, RequireRestart = false,
            HintText = "How much a design's effective damage counts towards its mounted score." + WeightOpinionNote)]
        [SettingPropertyGroup(AdvisorGroup, GroupOrder = 3)]
        public float MountedEffectiveDamageWeight { get; set; } = 1.0f;

        [SettingPropertyFloatingInteger("Mounted Attack Speed Weight", 0.0f, 3.0f, "0.00", Order = 7, RequireRestart = false,
            HintText = "How much attack speed counts towards the mounted score." + WeightOpinionNote)]
        [SettingPropertyGroup(AdvisorGroup, GroupOrder = 3)]
        public float MountedAttackSpeedWeight { get; set; } = 0.4f;

        [SettingPropertyFloatingInteger("Mounted Reach Weight", 0.0f, 3.0f, "0.00", Order = 8, RequireRestart = false,
            HintText = "How much reach counts towards the mounted score." + WeightOpinionNote)]
        [SettingPropertyGroup(AdvisorGroup, GroupOrder = 3)]
        public float MountedReachWeight { get; set; } = 1.2f;

        [SettingPropertyFloatingInteger("Mounted Handling Weight", 0.0f, 3.0f, "0.00", Order = 9, RequireRestart = false,
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
