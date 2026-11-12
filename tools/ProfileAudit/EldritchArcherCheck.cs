// Native exported identities, correlated first-arrow quotes and compiled delivery routes. No live actor/command.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Commands;
using Kingmaker.UnitLogic.Parts;

namespace ProfileAudit
{
    internal static class EldritchArcherCheck
    {
        internal static List<string> Run(IEnumerable<SimpleBlueprint> loaded, out int failures)
        {
            int checks = 0, bad = 0; var lines = new List<string>();
            void Check(bool ok, string why) { checks++; if (!ok) { bad++; lines.Add("ELDRITCH-ARCHER FAILED: " + why); } }
            try
            {
                const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
                var blueprints = loaded.ToArray();
                foreach (var pair in new[] { ("EldritchArcherRangedSpellCombat", "SpellCombat"), ("EldritchArcherRangedSpellStrike", "Spellstrike") })
                {
                    var feature = blueprints.OfType<BlueprintFeature>().FirstOrDefault(b => b.name == pair.Item1);
                    Check(feature != null && feature.ComponentsArray.Any(c => c.GetType().Name == "AddMagusMechanicPart" &&
                        c.GetType().GetField("m_Feature", all).GetValue(c).ToString() == pair.Item2), "actual ranged feature unlocks native " + pair.Item2);
                }
                var plain = typeof(TacticalEngine).GetMethod("PlainRangedPayload", all);
                foreach (var pair in new[] { ("ScorchingRay", true), ("Snowball", true), ("ShockingGraspCast", false), ("Fireball", false), ("Haste", false) })
                {
                    var bp = blueprints.OfType<BlueprintAbility>().FirstOrDefault(b => b.name == pair.Item1);
                    Check(bp != null && (bool)plain.Invoke(null, new object[] { CasterPolicyCheck.Data(bp), AbilityProfile.Get(bp) }) == pair.Item2,
                        "actual supported ranged payload boundary: " + pair.Item1);
                }
                var ray = blueprints.OfType<BlueprintAbility>().First(b => b.name == "ScorchingRay");
                var ability = CasterPolicyCheck.Data(ray);
                var target = (UnitEntityData)RuntimeHelpers.GetUninitializedObject(typeof(UnitEntityData));
                var scope = typeof(CombatMath).GetNestedType("MagusRollScope", all);
                var first = scope.GetMethod("FirstArrow", all);
                var critical = typeof(CombatMath).GetMethod("AbilityCritFactor", all);
                var quote = new WeaponAttackEstimate { Attacks = 5, FirstHitChance = 0.2f, FirstCritChance = 0.01f,
                    AnyHitChance = 0.999f, TouchHitChance = 0.95f, TouchCritChance = 0.4f, CritMultiplier = 4 };
                IDisposable Use(bool crit) => (IDisposable)first.Invoke(null, new object[] { ability, target, quote, crit });
                using (Use(true))
                {
                    Check(Math.Abs(CombatMath.SpellAttackHitChance(null, target, ability) - 0.2f) < 0.00001f,
                        "payload uses first arrow, not full routine's hit or melee first-success chance");
                    Check(Math.Abs((float)critical.Invoke(null, new object[] { ability, AbilityProfile.Get(ray), target }) - 1.05f) < 0.00001f,
                        "spell uses x2 confirmation, not x4 weapon critical");
                    using (Use(false)) Check((float)critical.Invoke(null, new object[] { ability, AbilityProfile.Get(ray), target }) == 1f,
                        "payload ignoring criticals retains that rule");
                    Check(Math.Abs(CombatMath.SpellAttackHitChance(null, target, ability) - 0.2f) < 0.00001f, "nested quotes restore outer probability");
                }
                quote.FirstHitChance = quote.FirstCritChance = 0f;
                using (Use(true))
                {
                    Check(CombatMath.SpellAttackHitChance(null, target, ability) == 0f, "first-arrow miss is not retried by later arrows or Manyshot");
                    Check((float)critical.Invoke(null, new object[] { ability, AbilityProfile.Get(ray), target }) == 1f, "zero first-hit quote has no critical division");
                }
                Check(scope.GetField("s_MagusRoll", all) == null && typeof(CombatMath).GetField("s_MagusRoll", all).GetValue(null) == null,
                    "no first-arrow quote escapes its scope");

                bool Calls(MethodInfo m, string name) => SaveBoostCheck.ReferencedMembers(m).Any(x => x.Name == name);
                Check(Calls(typeof(UnitPartMagus).GetMethod("IsSuitableForEldritchArcherSpellStrike", all), "get_IsRay"), "native eligibility is a ray predicate");
                Check(Calls(typeof(UnitPartMagus).GetMethod("IsSuitableForEldritchArcherSpellStrike", all), "IsSpellFromMagusSpellList"), "native eligibility retains spellbook/list rule");
                Check(Calls(typeof(TacticalEngine).GetMethod("NativeRangedStrike", all), "IsSuitableForEldritchArcherSpellStrike"), "production delegates eligibility to native Magus");
                var delivery = typeof(CombatMath).GetMethods(all).Single(m => m.Name == "DeliveryWeapon" && m.GetParameters().Length == 5);
                Check(Calls(delivery, "NativeRangedStrike") && Calls(delivery, "GetFirstWeapon"), "ordinary active-strike forecasts use the actual ranged weapon");
                Check(Calls(typeof(TacticalEngine).GetMethod("PriceEldritchCast", all), "FirstArrow"), "combined cast reuses correlated first-arrow quote");
                Check(Calls(typeof(TacticalEngine).GetMethod("ValidateMagusIssue", all), "ValidateEldritchIssue"), "native ranged guards reach final issue");
                Check(Calls(typeof(TacticalEngine).GetMethod("PriceHeldCharge", all), "PriceEldritchReservation"), "pending ranged payload opportunity cost reaches selection");
                var native = typeof(UnitEntityData).Assembly;
                var launch = native.GetType("Kingmaker.RuleSystem.Rules.RuleAttackWithWeapon").GetMethod("LaunchProjectile", all);
                Check(Calls(launch, "get_EldritchArcherSpell") && Calls(launch, "set_AttackRoll"), "native arrow launches spell with its own attack roll");
                var tick = native.GetType("Kingmaker.Controllers.MagusController").GetMethod("Tick", all);
                Check(Calls(tick, "get_HasMotionThisTick") && Calls(tick, "SetEldritchArcherSpell"), "native movement clears pending payload");
                Check(Calls(tick, "get_EldritchArcherSpellCastTime"), "native payload retention is time bounded");
                var guard = typeof(TacticalEngine).GetMethod("ArmedRangedSpell", all);
                Check(Calls(guard, "get_HasMotionThisTick") && Calls(guard, "get_EldritchArcherSpellCastTime"), "forecast preserves native motion/expiry gates");
            }
            catch (Exception ex) { bad++; lines.Add("ELDRITCH-ARCHER FAILED: " + ex.GetBaseException()); }
            failures = bad;
            lines.Insert(0, $"ELDRITCH-ARCHER {checks} checks, failures {bad}; exported features/spells, scalar quotes, compiled native routes; no actor, cast, attack, clock or Unity execution");
            return lines;
        }
    }
}
