using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.Prefabs2;

namespace SmithingSuite.UI
{
    /// <summary>
    /// Adds the advisor's trigger button to the vanilla crafting screen's bottom-right control
    /// cluster (SPEC-SLICE4-5.md Slice 5).
    ///
    /// XPath anchor: <c>//ListPanel[@Id='RightBottomButtons']/Children</c> in <c>Crafting.xml</c>.
    /// That <c>Id</c> is load-bearing rather than decorative - the <c>NavigationScopeTargeter</c> one
    /// line above it addresses the panel by name (<c>ScopeParent="..\RightBottomButtons"</c>), so
    /// TaleWorlds cannot rename it without editing the same file in the same breath, and the widget
    /// is a real <c>ListPanel</c>: appending a child adds a stacked row instead of landing on top of
    /// something, which is exactly why this cluster was chosen over the Randomize/History/Undo/Redo
    /// one. That cluster's four buttons are absolutely positioned into a full 2x2 grid (columns at
    /// x=22 and x=81, rows at MarginBottom 85 and 35) inside a 140x470 panel whose remaining vertical
    /// space is 139px, of which the cluster already uses 125 - a fifth 54x40 button there would have
    /// to overlap either the piece-type selector above it or the panel edge below it.
    ///
    /// If the XPath ever stops matching, UIExtenderEx logs an error and skips this patch while the
    /// vanilla screen keeps loading (AC15), which is why this class carries no fallback of its own.
    /// </summary>
    [PrefabExtension("Crafting", "//ListPanel[@Id='RightBottomButtons']/Children")]
    public sealed class AdvisorTriggerButtonPrefab : PrefabExtensionInsertPatch
    {
        // Child, not Append: Append inserts as the matched node's NEXT SIBLING (verified against
        // Bannerlord.UIExtenderEx.Components.PrefabComponent), which would place the button next to
        // the <Children> element rather than inside it. See AdvisorPopupPrefab for the same note.
        public override InsertType Type => InsertType.Child;

        // Beyond any realistic child count, so the button always lands after vanilla's two buttons
        // (InsertAsChild clamps to "after the last existing child"). The list stacks bottom-to-top,
        // so last means topmost.
        public override int Index => int.MaxValue;

        [PrefabExtensionFileName]
        public string TriggerButtonFileName => "AdvisorTriggerButton";
    }

    /// <summary>
    /// Adds the advisor popup to the vanilla crafting screen (SPEC-SLICE4-5.md Slice 5).
    ///
    /// XPath anchor: <c>//CraftingScreenWidget/Children</c> in <c>Crafting.xml</c>. This is the
    /// movie's root widget and its element name is the C# widget class the screen instantiates, so
    /// unlike any <c>Id</c> in the file it cannot be renamed without a matching engine change - the
    /// most stable node the document has. Appending last puts this popup immediately after vanilla's
    /// own popup block (NewCraftedWeapon / CraftingOrder / TemplateSelection / History / Hero), which
    /// is both where popups belong on this screen and what makes it draw above everything else.
    ///
    /// Same fallback story as <see cref="AdvisorTriggerButtonPrefab"/>: a missing XPath is logged and
    /// skipped by UIExtenderEx, and the vanilla screen still loads.
    /// </summary>
    [PrefabExtension("Crafting", "//CraftingScreenWidget/Children")]
    public sealed class AdvisorPopupPrefab : PrefabExtensionInsertPatch
    {
        public override InsertType Type => InsertType.Child;

        public override int Index => int.MaxValue;

        [PrefabExtensionFileName]
        public string PopupFileName => "AdvisorPopup";
    }
}
