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

        /// <summary>
        /// ★ v0.3.23 (bombs task): the usable item the game takes from the inventory each time this ability is used
        /// (AbilityData.Spend → SpendMaterialComponent, while RequireMaterialComponent: a material component and no source
        /// item) - Grenadier Alchemical Weapon's Alchemist's Fire, Acid Flask or Holy Water. Null for everything else; a
        /// spell's material component (diamond dust) is not a usable item and stays with the spell policy.
        /// </summary>
        public static BlueprintItemEquipmentUsable SpentMaterial(AbilityData ability)
        {
            try
            {
                if (ability?.Blueprint == null || !ability.RequireMaterialComponent) return null;
                return ability.Blueprint.MaterialComponent?.Item as BlueprintItemEquipmentUsable;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>False when the settings forbid using this item ability; always true for non-consumables.</summary>
        /// <remarks>★ v0.3.23 (bombs task): also for an ability that uses a usable item up as its material component
        /// (<see cref="SpentMaterial"/>), by that item's type: Alchemical Weapon spends flasks with "use thrown items" off.
        /// Class bombs spend their own daily resource, not an item, and are not affected.</remarks>
        public static bool IsAllowed(AbilityData ability)
        {
            UsableItemType type;
            if (ability?.SourceItem is ItemEntityUsable item) type = item.Blueprint.Type;
            else
            {
                var material = SpentMaterial(ability);
                if (material == null) return true;
                type = material.Type;
            }
            var g = ModSettings.Instance;
            if (g == null || !g.UseConsumables) return false;
            switch (type)
            {
                case UsableItemType.Potion: return g.UsePotions;
                case UsableItemType.Scroll:
                case UsableItemType.Wand: return g.UseScrollsAndWands;
                default: return g.UseThrownItems;
            }
        }
    }
}
