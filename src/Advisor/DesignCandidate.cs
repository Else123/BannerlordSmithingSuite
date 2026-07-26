using System.Collections.Generic;

namespace SmithingSuite.Advisor
{
    /// <summary>
    /// One piece placed into a design, at a given scale. Immutable: the optimizer explores many
    /// candidates and must never have one mutate under it mid-search.
    /// </summary>
    public sealed class DesignPiece
    {
        public string PieceId { get; }

        /// <summary>
        /// Mirrors vanilla <c>TaleWorlds.Core.PieceTypes</c> (Blade / Guard / Handle / Pommel) as a
        /// plain int so this type stays engine-free - see SPEC-SLICE4-5.md "Verified engine anchors".
        /// </summary>
        public int PieceType { get; }

        public int PieceTier { get; }

        /// <summary>
        /// The lever the user explicitly asked for ("the length of the individual pieces"). Maps
        /// 1:1 to <c>WeaponDesignElement.CreateUsablePiece(piece, scalePercentage)</c> - this is the
        /// value the Integration layer passes straight through when it builds the real design.
        /// </summary>
        public int ScalePercentage { get; }

        public DesignPiece(string pieceId, int pieceType, int pieceTier, int scalePercentage)
        {
            PieceId = pieceId;
            PieceType = pieceType;
            PieceTier = pieceTier;
            ScalePercentage = scalePercentage;
        }
    }

    /// <summary>
    /// An engine-free description of one candidate weapon design: the template the player already
    /// chose, plus the pieces (and their scale) the optimizer wants evaluated. Never carries engine
    /// objects - only ids and numbers - so it can be built and compared entirely off the game.
    /// </summary>
    public sealed class DesignCandidate
    {
        public string TemplateId { get; }

        public IReadOnlyList<DesignPiece> Pieces { get; }

        public DesignCandidate(string templateId, IReadOnlyList<DesignPiece> pieces)
        {
            TemplateId = templateId;
            // A candidate with a null piece list would otherwise push a null check onto every
            // consumer (evaluator, optimizer, tests); an empty list is the honest default instead.
            Pieces = pieces ?? new DesignPiece[0];
        }
    }
}
