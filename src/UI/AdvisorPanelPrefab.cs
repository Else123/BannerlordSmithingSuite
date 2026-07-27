using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.Prefabs2;

namespace SmithingSuite.UI
{
    /// <summary>
    /// Inserts <c>Prefabs\AdvisorPanel.xml</c> into the vanilla crafting screen's "CraftingCategory"
    /// movie, as an additional child inside <c>InnerPanel</c>'s own children list (SPEC-SLICE4-5.md
    /// Slice 5, TODO 12). If the XPath below ever stops matching - e.g. a future game update reshapes
    /// <c>CraftingCategory.xml</c> - UIExtenderEx logs an error and skips this patch; the rest of the
    /// crafting screen keeps loading either way (AC15), which is why this class carries no fallback
    /// logic of its own.
    ///
    /// DEVIATION FROM THE ORIGINAL PLAN, recorded here because it is easy to get subtly wrong:
    /// <see cref="InsertType.Append"/> at this XPath was the first idea, but
    /// <c>Bannerlord.UIExtenderEx.Components.PrefabComponent</c> (decompiled to verify) implements
    /// Append as "insert as the NEXT SIBLING of the XPath-matched node" - i.e. matching the
    /// <c>&lt;Children&gt;</c> element itself and using Append would place this panel as a sibling of
    /// <c>&lt;Children&gt;</c>, OUTSIDE it, under <c>&lt;Widget Id="InnerPanel"&gt;</c>. Every vanilla
    /// prefab nests a widget's children inside a <c>&lt;Children&gt;</c> element with no exceptions,
    /// so a node placed next to it rather than inside it is very unlikely to render at all - a silent
    /// AC12 failure, not a loud one. <see cref="InsertType.Child"/> at the same XPath instead inserts
    /// INTO the matched node's own child list (confirmed via <c>PrefabComponent.InsertAsChild</c>),
    /// which is what "append this panel among InnerPanel's children" actually requires.
    /// <see cref="Index"/> is set beyond the current child count so the panel always lands at the end
    /// of that list regardless of how many children vanilla or other mods add before it.
    /// </summary>
    [PrefabExtension("CraftingCategory", "//Widget[@Id='InnerPanel']/Children")]
    public sealed class AdvisorPanelPrefab : PrefabExtensionInsertPatch
    {
        public override InsertType Type => InsertType.Child;

        // Larger than InnerPanel's children could ever realistically number - InsertAsChild clamps
        // this to "insert after the last existing child" once it is >= the child count.
        public override int Index => int.MaxValue;

        // The XML fragment lives at src/UI/Prefabs/AdvisorPanel.xml and is deployed to
        // <Module>/GUI/Prefabs/AdvisorPanel.xml (see SmithingSuite.csproj's CopyToBannerlord target).
        // PrefabExtensionFileName searches the module's GUI folder recursively for a file matching
        // this name (extension-less, case-insensitive) - see PrefabComponent.GetNodes(...,
        // PrefabExtensionFileNameAttribute, ...).
        [PrefabExtensionFileName]
        public string PanelFileName => "AdvisorPanel";
    }
}
