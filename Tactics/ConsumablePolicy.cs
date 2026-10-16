// ★ v0.3.0: Single source of truth for whether the player allows an item ability to be used.
// Called by every place that collects abilities (turn-based engine and the real-time SituationAnalyzer), so a toggle
// cannot be bypassed by one of the two AI paths.
using Kingmaker.Blueprints.Items.Equipment;
using Kingmaker.Items;
using Kingmaker.UnitLogic.Abilities;
using CompanionAI_Pathfinder.Settings;

namespace CompanionAI_Pathfinder.Tactics
{
    public static class ConsumablePolicy
    {
        /// <summary>True for abilities granted by a usable item (potion, scroll, wand, alchemical/thrown item).</summary>
        public static bool IsConsumable(AbilityData ability) => ability?.SourceItem is ItemEntityUsable;

        /// <summary>False when the settings forbid using this item ability; always true for non-consumables.</summary>
        public static bool IsAllowed(AbilityData ability)
        {
            if (!(ability?.SourceItem is ItemEntityUsable item)) return true;
            var g = ModSettings.Instance;
            if (g == null || !g.UseConsumables) return false;
            switch (item.Blueprint.Type)
            {
                case UsableItemType.Potion: return g.UsePotions;
                case UsableItemType.Scroll:
                case UsableItemType.Wand: return g.UseScrollsAndWands;
                default: return g.UseThrownItems;
            }
        }
    }
}
