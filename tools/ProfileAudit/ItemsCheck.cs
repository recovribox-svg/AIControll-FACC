using System;
using System.Collections.Generic;
using System.Linq;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Items.Equipment;

namespace ProfileAudit
{
    internal static class ItemsCheck
    {
        internal static List<string> Run(IEnumerable<SimpleBlueprint> blueprints, out int failures)
        {
            var lines = new List<string>();
            int bad = 0;
            try
            {
                var items = blueprints.OfType<BlueprintItemEquipmentUsable>()
                    .OrderBy(item => item.name, StringComparer.Ordinal).ToList();
                var verdicts = new Dictionary<Kingmaker.UnitLogic.Abilities.Blueprints.BlueprintAbility, string>();
                foreach (var group in items.GroupBy(item => item.Type))
                {
                    int read = 0, guessed = 0, unread = 0;
                    foreach (var item in group)
                    {
                        var ability = item.Ability;
                        if (ability == null) { unread++; continue; }
                        if (!verdicts.TryGetValue(ability, out string verdict))
                            verdicts[ability] = verdict = Audit.VerdictOf(ability).Verdict;
                        if (verdict != null && verdict.StartsWith("valued:", StringComparison.Ordinal)) read++;
                        else if (verdict != null && verdict.StartsWith("GUESSED:", StringComparison.Ordinal)) guessed++;
                        else unread++;
                    }
                    string price = group.Key == UsableItemType.Potion ? "buff restraint only; native harm removal and heal appraisal"
                        : group.Key == UsableItemType.Scroll || group.Key == UsableItemType.Wand ? "0.7 x native UMD chance"
                        : "0.85 x native UMD chance";
                    lines.Add($"ITEMS {group.Key}: {read} read, {guessed} guessed, {unread} unread of {read + guessed + unread}; {price}");
                }

                void Check(string name, UsableItemType type, Func<AbilityProfile, bool> predicate)
                {
                    var item = items.FirstOrDefault(i => i.name == name && i.Type == type);
                    var profile = item?.Ability == null ? null : AbilityProfile.Get(item.Ability);
                    if (profile == null || !predicate(profile))
                    {
                        bad++;
                        lines.Add("ITEMS FAILED: " + name);
                    }
                    else lines.Add($"ITEMS {name}: {item.Ability.name}; {verdicts[item.Ability]}");
                }
                Check("PotionOfCureLightWounds", UsableItemType.Potion, p => p.Heals.Count > 0);
                Check("PotionOfNeutralizePoison", UsableItemType.Potion, p => p.DispelActions.Any(d => (d.Descriptor.Value & Kingmaker.Blueprints.Classes.Spells.SpellDescriptor.Poison) != 0));
                Check("PotionOfShieldOfFaith", UsableItemType.Potion, p => p.TargetBuffs.Count + p.CasterBuffs.Count > 0);
                Check("ScrollOfMagicMissile", UsableItemType.Scroll, p => p.DealsDamage);
            }
            catch (Exception ex)
            {
                bad++;
                lines.Add("ITEMS FAILED: " + ex.GetBaseException().Message);
            }
            lines.Insert(0, $"ITEMS: item-to-ability joins, 4 blueprint fixtures, {bad} failures");
            failures = bad;
            return lines;
        }
    }
}
