// ★ v0.3.28 test 10: the wiring TacticalEngine.SoulPower relies on, on the loaded blueprints (no unit): the Dark Elementalist's
// Soul Power profile heals burn over an area, its success buff adds a ranked feature, a buff that success buff switches on
// carries an Elemental Overflow reading that feature's rank, and that Overflow ignores burn.
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Class.Kineticist;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.UnitLogic.Mechanics.Components;

namespace ProfileAudit
{
    internal static class SoulPowerCheck
    {
        private const string SoulPower = "31a1e5b27cdb78f4094630610519981c";

        internal static List<string> Run(IEnumerable<SimpleBlueprint> loaded, out int failures)
        {
            var lines = new List<string>();
            failures = 0;
            int bad = 0;
            var ability = loaded.FirstOrDefault(b => b.AssetGuid.ToString() == SoulPower) as BlueprintAbility;
            if (ability == null) { lines.Add("SOUL-POWER SKIPPED: Soul Power not loaded"); return lines; }
            var reads = typeof(TacticalEngine).GetMethod("ReadsRankOf", BindingFlags.NonPublic | BindingFlags.Static);
            var profile = AbilityProfile.Get(ability);
            int checks = 0;
            void Check(bool ok, string what) { checks++; if (!ok) { bad++; lines.Add($"SOUL-POWER FAILED: {what}"); } }
            Check(profile != null && profile.HealsBurn, "the profile does not heal burn");
            Check(profile != null && profile.IsAoE && profile.AoERadius > 8f, $"area radius {profile?.AoERadius:0.0} m (30 ft expected)");
            bool wired = false;
            foreach (var buff in profile?.CasterBuffs.Concat(profile.TargetBuffs).Distinct() ?? Enumerable.Empty<BlueprintBuff>())
            {
                foreach (var add in buff.GetComponents<AddFacts>())
                foreach (var feature in add.Facts.OfType<BlueprintFeature>().Where(f => f.Ranks > 1))
                foreach (var actions in buff.GetComponents<AddFactContextActions>())
                foreach (var apply in actions.Activated.Actions.OfType<ContextActionApplyBuff>())
                {
                    var on = apply.Buff;
                    if (on == null || reads == null) continue;
                    bool readsRank = on.GetComponents<ContextRankConfig>().Any(c => (bool)reads.Invoke(null, new object[] { c, feature }));
                    var overflow = on.GetComponents<AddKineticistElementalOverflow>().FirstOrDefault();
                    if (readsRank && overflow != null)
                    {
                        wired = true;
                        Check(overflow.IgnoreBurn, $"{on.name}'s Overflow does not ignore burn");
                    }
                }
            }
            Check(wired, "no success buff adds a ranked feature read by an Overflow buff it switches on");
            failures = bad;
            if (failures == 0) lines.Add($"SOUL-POWER ok: {checks} checks (burn relief, 30 ft area, Overflow rank wiring)");
            return lines;
        }
    }
}
