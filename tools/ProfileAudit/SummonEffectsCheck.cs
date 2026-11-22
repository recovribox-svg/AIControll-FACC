using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Blueprints;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Mechanics;

namespace ProfileAudit
{
    internal static class SummonEffectsCheck
    {
        internal static object Property(AbilityProfile profile, string name) =>
            profile == null ? null : typeof(AbilityProfile).GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(profile);

        private static readonly MethodInfo CarrierPlan = typeof(CombatMath).GetMethod("CarrierPlanOf", BindingFlags.Static | BindingFlags.NonPublic);
        internal static bool IsBenefitCarrier(BlueprintBuff buff)
        {
            var plan = CarrierPlan?.Invoke(null, new object[] { buff });
            return plan?.GetType().GetField("Kind", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(plan)?.ToString() == "Benefit";
        }

        internal static List<string> Run(IEnumerable<SimpleBlueprint> blueprints, out int failures)
        {
            var lines = new List<string>();
            var abilities = blueprints.OfType<BlueprintAbility>().ToLookup(a => a.name, StringComparer.Ordinal);
            int failed = 0;

            void Check(string name, Func<AbilityProfile, bool> accepts)
            {
                var ability = abilities[name].FirstOrDefault();
                try
                {
                    if (ability != null && accepts(AbilityProfile.Get(ability)))
                    {
                        lines.Add("SUMMON-EFFECTS " + name + " passed");
                        return;
                    }
                }
                catch (Exception e)
                {
                    lines.Add("SUMMON-EFFECTS " + name + " evidence exception " + e.GetType().Name);
                }
                failed++;
                lines.Add("SUMMON-EFFECTS FAILED: " + name + (ability == null ? " (fixture unavailable)" : ""));
            }

            bool AreaControl(AbilityProfile p) => p.IsAoE && p.SelectsAround && p.AppliesHarmfulBuffs && p.ControlStrength == ControlStrength.Hard;
            Check("VrockStunningScreechAbility", AreaControl);
            Check("DerakniDroneAbility", AreaControl);
            Check("Nalfeshnee_Ability_UnholyNimbusBlast", AreaControl);
            Check("Nalfeshnee_Ability_UnholyNimbusCast", p => Property(p, "DelayedControlBurst") is BlueprintAbility child &&
                child.name == "Nalfeshnee_Ability_UnholyNimbusBlast");
            Check("Nalfeshnee_Ability_TouchOfChaos", p => p.AppliesHarmfulBuffs && !p.MarksForOwnAttacks && Property(p, "WorstOfTwoAttacks") != null);
            Check("SongOfDiscord", p => p.IsAoE && p.Save == SavingThrowType.Will && p.ControlStrength == ControlStrength.None &&
                Property(p, "RedirectChance") is float chance && Math.Abs(chance - 0.5f) < 0.0001f);
            Check("Enervation", p => p.DealsDamage && p.TargetBuffs.Any(IsBenefitCarrier));
            foreach (string suffix in new[] { "BlindingSickness", "CackleFever", "MindFire", "Shakes" })
                Check("Contagion" + suffix, p => p.TargetBuffs.Any(b => b.Frequency == DurationRate.Days && CombatMath.HasPoisonStatDamage(b)));
            Check("SlayerStudyTargetAbility", p => p.MarksForOwnAttacks && Property(p, "AttackMark") != null);
            Check("SlayerSwiftStudyTargetAbility", p => p.MarksForOwnAttacks && Property(p, "AttackMark") != null);

            lines.Add($"SUMMON-EFFECTS {lines.Count} stock blueprint fixtures, {failed} failures");
            failures = failed;
            return lines;
        }
    }
}
