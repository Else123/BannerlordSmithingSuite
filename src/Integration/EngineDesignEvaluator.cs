using System;
using SmithingSuite.Advisor;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace SmithingSuite.Integration
{
    /// <summary>
    /// The real <see cref="IDesignEvaluator"/> (Slice 5, SPEC-SLICE4-5.md TODO 9): builds the actual
    /// crafted item via <c>Crafting.GenerateItem</c> - the same call the vanilla crafting screen uses
    /// for its live preview - and reads the engine-computed stats back into the engine-free
    /// <see cref="WeaponStats"/> the Advisor scores. This is the ONLY class that lets the search seam
    /// (<see cref="IDesignEvaluator"/>) touch a TaleWorlds API; <c>Advisor/*.cs</c> stays engine-free.
    ///
    /// CONTRACT (this runs inside a live campaign, inside <see cref="DesignOptimizer"/>'s search
    /// loop): <see cref="Evaluate"/> must never throw. Any resolution failure - missing template,
    /// unknown piece id, an incomplete design the engine could not fill in, or any unexpected
    /// exception - degrades to <see cref="UnusableEvaluation"/>: zeroed stats with
    /// <see cref="int.MaxValue"/> difficulty, which every legality check in
    /// <see cref="DesignOptimizer"/> rejects. Failures are logged at most once per run (not once per
    /// call - the optimizer may retry the same bad candidate many times in one search) via
    /// <see cref="SmithingLog"/>, so a broken search does not spam the log.
    /// </summary>
    public sealed class EngineDesignEvaluator : IDesignEvaluator
    {
        // Mirrors TaleWorlds.Core.CraftingPiece.PieceTypes.NumberOfPieceTypes (Blade/Guard/Handle/
        // Pommel) - see DesignPiece.PieceType's doc for why the Advisor layer keeps this as a plain
        // int rather than referencing the engine enum.
        private const int PieceTypeCount = 4;

        private static readonly WeaponStats ZeroStats = new WeaponStats(
            0f, 0f, DamageKind.Cut, 0f, 0f, DamageKind.Cut, 0f, 0f, 0f, 0, 0);

        // A design the optimizer can never select (every AC4 / SearchState.IsLegal check compares
        // Difficulty against a finite smithing skill) - the defensive return value for every
        // resolution failure below.
        private static readonly DesignEvaluation UnusableEvaluation = new DesignEvaluation(ZeroStats, int.MaxValue);

        // Logged once per RUN (not once per call, see class doc) - the optimizer's search loop can
        // hit the same failure on every remaining evaluation once something is wrong. Deliberately
        // an INSTANCE field, not static (FINDING 2): a fresh EngineDesignEvaluator is constructed
        // per "Suggest Best" click, and a static flag would stay tripped for the rest of the game
        // session, silently swallowing a genuinely different failure an hour later.
        private bool _hasLoggedFailure;

        public DesignEvaluation Evaluate(DesignCandidate candidate)
        {
            try
            {
                return EvaluateCore(candidate);
            }
            catch (Exception ex)
            {
                LogFailureOnce("unexpected exception - " + ex.Message);
                return UnusableEvaluation;
            }
        }

        private DesignEvaluation EvaluateCore(DesignCandidate candidate)
        {
            if (candidate == null)
            {
                LogFailureOnce("candidate was null");
                return UnusableEvaluation;
            }

            // Live campaign only - the whole point of the fake-evaluator seam (see IDesignEvaluator)
            // is that DesignOptimizer's own tests never reach this class, but a defensive guard costs
            // nothing and protects any caller that constructs this evaluator too early (e.g. from
            // SubModule initialization before a campaign exists).
            if (Campaign.Current == null)
            {
                LogFailureOnce("Campaign.Current is null");
                return UnusableEvaluation;
            }

            CraftingTemplate template = CraftingTemplate.GetTemplateFromId(candidate.TemplateId);
            if (template == null)
            {
                LogFailureOnce("unknown template id '" + candidate.TemplateId + "'");
                return UnusableEvaluation;
            }

            WeaponDesignElement[] elements;
            if (!TryBuildElements(candidate, out elements))
            {
                LogFailureOnce("could not resolve one or more piece ids for template '" + candidate.TemplateId + "'");
                return UnusableEvaluation;
            }

            var name = new TextObject(string.Empty);
            var design = new WeaponDesign(template, name, elements);

            ItemObject item = null;
            Crafting.GenerateItem(design, name, null, null, ref item);

            // GenerateItem degrades quietly on an incomplete design (e.g. a slot the template
            // requires was left invalid): it returns an item with no WeaponComponent rather than
            // throwing. That is exactly the "not craftable as given" case the spec calls out.
            WeaponComponentData weapon = item?.WeaponComponent?.PrimaryWeapon;
            if (weapon == null)
            {
                LogFailureOnce("engine produced no weapon component for template '" + candidate.TemplateId + "'");
                return UnusableEvaluation;
            }

            var stats = ToWeaponStats(weapon, item);

            int difficulty = Campaign.Current.Models.SmithingModel.CalculateWeaponDesignDifficulty(design);

            return new DesignEvaluation(stats, difficulty);
        }

        /// <summary>
        /// Maps engine-computed weapon/item numbers into the engine-free <see cref="WeaponStats"/>
        /// carrier. Internal (not private) so <c>UI.WeaponDesignAdvisorMixin</c>'s cheap per-frame
        /// refresh - which reads <c>WeaponDesignVM.CraftedItemObject</c> directly instead of calling
        /// <c>Crafting.GenerateItem</c> - can reuse the exact same mapping this evaluator uses for its
        /// own (expensive) generate-and-evaluate path, rather than a second copy that could silently
        /// drift out of sync (e.g. the <see cref="DamageTypes.Invalid"/> handling in
        /// <see cref="MapDamageType"/>).
        /// </summary>
        internal static WeaponStats ToWeaponStats(WeaponComponentData weapon, ItemObject item)
        {
            float swingDamage = weapon.SwingDamage;
            float thrustDamage = weapon.ThrustDamage;

            // MapDamageType falls through Invalid to Cut as a "least-wrong placeholder" (see its own
            // doc) on the ASSUMPTION that the engine always pairs Invalid with a zero damage value.
            // That pairing is not enforced anywhere (FINDING 3) - if the engine ever reported Invalid
            // with a nonzero magnitude, the scorer would treat it as real Cut damage and quietly
            // corrupt the "engine truth" claim. Defensively zero the paired magnitude here so the
            // placeholder can never influence a score, regardless of what the engine actually sends.
            if (weapon.SwingDamageType == DamageTypes.Invalid)
            {
                swingDamage = 0f;
            }
            if (weapon.ThrustDamageType == DamageTypes.Invalid)
            {
                thrustDamage = 0f;
            }

            return new WeaponStats(
                weapon.SwingSpeed,
                swingDamage,
                MapDamageType(weapon.SwingDamageType),
                weapon.ThrustSpeed,
                thrustDamage,
                MapDamageType(weapon.ThrustDamageType),
                weapon.Handling,
                weapon.WeaponLength,
                item.Weight,
                (int)item.Tier,
                item.Value);
        }

        /// <summary>
        /// Resolves every <see cref="DesignPiece"/> in the candidate to a real, valid
        /// <see cref="CraftingPiece"/> and places it at its <see cref="DesignPiece.PieceType"/> slot,
        /// scaled via <see cref="WeaponDesignElement.CreateUsablePiece"/> - the engine's own lever for
        /// "the length of the individual pieces" the user asked the advisor to optimise. Slots the
        /// candidate does not mention are filled with <see cref="WeaponDesignElement.GetInvalidPieceForType"/>,
        /// matching how <c>Crafting.Init()</c> itself builds an incomplete design. Returns false - and
        /// resolves nothing further - the moment any piece id or slot index cannot be trusted, so the
        /// caller never builds a <see cref="WeaponDesign"/> out of half-resolved pieces.
        /// </summary>
        private static bool TryBuildElements(DesignCandidate candidate, out WeaponDesignElement[] elements)
        {
            var built = new WeaponDesignElement[PieceTypeCount];
            for (int i = 0; i < PieceTypeCount; i++)
            {
                built[i] = WeaponDesignElement.GetInvalidPieceForType((CraftingPiece.PieceTypes)i);
            }

            foreach (DesignPiece designPiece in candidate.Pieces)
            {
                if (designPiece == null || string.IsNullOrEmpty(designPiece.PieceId))
                {
                    elements = null;
                    return false;
                }

                if (designPiece.PieceType < 0 || designPiece.PieceType >= PieceTypeCount)
                {
                    elements = null;
                    return false;
                }

                CraftingPiece piece = MBObjectManager.Instance.GetObject<CraftingPiece>(designPiece.PieceId);
                if (piece == null || !piece.IsValid)
                {
                    elements = null;
                    return false;
                }

                built[designPiece.PieceType] = WeaponDesignElement.CreateUsablePiece(piece, designPiece.ScalePercentage);
            }

            elements = built;
            return true;
        }

        /// <summary>
        /// TaleWorlds.Core.DamageTypes and <see cref="DamageKind"/> share ordinals by design (Cut=0,
        /// Pierce=1, Blunt=2) so this is normally a plain cast - EXCEPT <c>DamageTypes.Invalid</c>
        /// (-1), which a real weapon design can legitimately produce (e.g. a blade with no thrust
        /// component reports <c>ThrustDamageType == Invalid</c>, see CraftingTemplate.GetStatDatas).
        /// DamageKind has no Invalid member, so that case must be handled explicitly rather than cast
        /// - Cut is the least-wrong placeholder. <see cref="ToWeaponStats"/> defensively zeroes the
        /// paired damage magnitude whenever this placeholder is used, so this mapping alone is never
        /// enough to make the axis it feeds overstate anything (see ToWeaponStats for why).
        /// </summary>
        private static DamageKind MapDamageType(DamageTypes damageType)
        {
            switch (damageType)
            {
                case DamageTypes.Cut:
                    return DamageKind.Cut;
                case DamageTypes.Pierce:
                    return DamageKind.Pierce;
                case DamageTypes.Blunt:
                    return DamageKind.Blunt;
                default:
                    return DamageKind.Cut;
            }
        }

        private void LogFailureOnce(string reason)
        {
            if (_hasLoggedFailure)
            {
                return;
            }

            _hasLoggedFailure = true;
            SmithingLog.Line("EngineDesignEvaluator: " + reason);
        }
    }
}
