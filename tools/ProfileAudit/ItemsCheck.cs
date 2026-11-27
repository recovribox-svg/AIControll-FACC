using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Commands;
using Kingmaker.UI.UnitSettings;
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
                    const string price = "shared spend restraint; native UMD when required";
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
                var missile = items.FirstOrDefault(i => i.name == "ScrollOfMagicMissile")?.Ability;
                if (missile == null || !blueprints.Any(b => b == missile) || missile.name != "MagicMissile")
                {
                    bad++;
                    lines.Add("ITEMS FAILED: Magic Missile scroll must resolve to the canonical spellbook blueprint");
                }
                else lines.Add("ITEMS Magic Missile scroll: shares its cast blueprint with the spellbook");

                const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
                var canOffer = typeof(AbilityDiscovery).GetMethod("CanOfferPotionHandoff", all);
                var cure = items.Single(i => i.name == "PotionOfCureLightWounds").Ability;
                bool Handoff(AbilityData ability, bool slot) => (bool)canOffer.Invoke(null, new object[] { ability, slot });
                var drink = CasterPolicyCheck.Data(cure);
                var give = CasterPolicyCheck.Data(cure);
                give.PotionForOther = true;
                var personal = items.Where(i => i.Type == UsableItemType.Potion && i.Ability != null)
                    .Select(i => i.Ability).FirstOrDefault(a => a.Range == AbilityRange.Personal);
                if (!Handoff(drink, true) || Handoff(drink, false) || AbilityDiscovery.Key(drink) == AbilityDiscovery.Key(give) ||
                    personal == null || Handoff(CasterPolicyCheck.Data(personal), true))
                {
                    bad++;
                    lines.Add("ITEMS FAILED: potion handoff must distinguish native identity, quick slot and Personal range");
                }
                else lines.Add("ITEMS potion handoff: actual cure and Personal potion, quick slot and separate option identity");

                bool Calls(Type owner, string method, string member) =>
                    SaveBoostCheck.ReferencedMembers(owner.GetMethod(method, all)).Any(m => m.Name == member);
                if (!(Calls(typeof(MechanicActionBarSlotItem), "GetConvertedAbilityData", "set_PotionForOther") &&
                      Calls(typeof(AbilityData), "GetDefaultActionType", "get_PotionForOther") &&
                      Calls(typeof(AbilityData), "GetApproachDistance", "GetWeaponRange") &&
                      Calls(typeof(UnitUseAbility), "OnTick", "get_SourceItemUsableBlueprint") &&
                      Calls(typeof(TacticalEngine), "TouchApproachFirst", "get_PotionForOther")))
                {
                    bad++;
                    lines.Add("ITEMS FAILED: native potion handoff conversion, range, action, or shared approach route");
                }
                else lines.Add("ITEMS potion handoff: native conversion, action, range and combat command routes");
            }
            catch (Exception ex)
            {
                bad++;
                lines.Add("ITEMS FAILED: " + ex.GetBaseException().Message);
            }
            lines.Insert(0, $"ITEMS: item-to-ability joins, 7 blueprint fixtures, {bad} failures");
            failures = bad;
            return lines;
        }
    }
}
