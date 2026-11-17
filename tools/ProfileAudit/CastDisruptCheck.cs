// ★ cast disruption (docs/BURST_AIM_AND_TACTICS_PLAN.md §4, slice B): a hit on an enemy whose spell has not gone off forces a
// concentration check. Native contracts on the real assembly, which real blueprints the rule covers, and the compiled
// routes into the scoring. Actor-free: no Game/Player, context, damage, roll, command or Unity timing.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Blueprints;
using Kingmaker.Controllers;
using Kingmaker.RuleSystem.Rules.Abilities;
using Kingmaker.RuleSystem.Rules.Damage;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Commands;
using Kingmaker.UnitLogic.Commands.Base;

namespace ProfileAudit
{
    internal static class CastDisruptCheck
    {
        private const BindingFlags Hidden = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                                            BindingFlags.DeclaredOnly;

        private static List<MemberInfo> Body(Type type, string name)
        {
            var method = type.GetMethods(Hidden).FirstOrDefault(m => m.Name == name) ?? throw new MissingMethodException(type.FullName, name);
            return SaveBoostCheck.ReferencedMembers(method).ToList();
        }

        private static bool Calls(IEnumerable<MemberInfo> body, Type type, string name) =>
            body.Any(m => m.DeclaringType == type && m.Name == name);

        internal static List<string> Run(IEnumerable<SimpleBlueprint> loaded, out int failures)
        {
            int checks = 0, bad = 0;
            var lines = new List<string>();
            void Check(bool ok, string why) { checks++; if (!ok) { bad++; lines.Add("CAST-DISRUPT FAILED: " + why); } }
            try
            {
                // --- native contracts ---
                var about = Body(typeof(UnitConcentrationController), "OnEventAboutToTrigger");
                Check(about.Any(m => m.Name == "get_Standard") && Calls(about, typeof(UnitCommand), "get_IsRunning") &&
                      Calls(about, typeof(UnitCommand), "get_IsActed") && Calls(about, typeof(AbilityData), "get_CanBeInterruptedWithDamage"),
                      "damage to a caster whose Standard cast runs and has not gone off queues a check, if the spell can be broken");
                Check(Calls(Body(typeof(UnitConcentrationController), "Tick"), typeof(UnitUseAbility), "MakeConcentrationCheck"),
                      "the queued damage makes the cast's concentration check");
                var trigger = Body(typeof(RuleCheckConcentration), "OnTrigger");
                Check(Calls(trigger, typeof(RuleDealDamage), "get_Result") && trigger.Any(m => m.Name == "get_SpellLevel") &&
                      trigger.Any(m => m.Name == "get_Concentration"),
                      "the DC reads the spell level and the damage dealt; the roll adds the caster's concentration");
                Check(Calls(Body(typeof(UnitUseAbility), "FailIfConcentrationCheckFailed"), typeof(AbilityData), "Spend"),
                      "a failed check spends the spell");
                Check(typeof(UnitUseAbility).GetField("m_CastTime", Hidden)?.FieldType == typeof(float),
                      "the running cast's native cast time is readable (time left until it goes off)");

                // --- which casts the rule covers, on real blueprints ---
                var all = loaded.OfType<BlueprintAbility>().ToList();
                bool Breakable(BlueprintAbility b) => CasterPolicyCheck.Data(b).CanBeInterruptedWithDamage;
                var fireball = all.FirstOrDefault(b => b.name == "Fireball");
                Check(fireball != null && fireball.IsSpell && Breakable(fireball), "a spell (Fireball) can be broken by damage");
                var supernatural = all.FirstOrDefault(b => !b.IsSpell && b.Type == AbilityType.Supernatural);
                Check(supernatural != null && !Breakable(supernatural), $"a supernatural ability ({supernatural?.name}) cannot");
                int spells = all.Count(b => b.IsSpell), breakable = all.Count(b => b.IsSpell && Breakable(b));
                lines.Add($"CAST-DISRUPT exports: {breakable} of {spells} spell blueprints can be broken by damage when cast from a spellbook");
                Check(breakable == spells, "every spell cast from a spellbook (no held touch, no item) is breakable by damage");

                // --- compiled routes ---
                var engine = typeof(TacticalEngine);
                var read = Body(engine, "ReadCasting");
                Check(Calls(read, typeof(AbilityData), "get_CanBeInterruptedWithDamage") && Calls(read, typeof(UnitCommand), "get_IsActed") &&
                      Calls(read, typeof(UnitCommand), "get_IsRunning") && Calls(read, typeof(BurstWatch), "NativeCastTime"),
                      "ReadCasting finds the casts by the native condition and their time left");
                Check(Calls(Body(engine, "CastingFor"), typeof(CombatMemory), "get_IsRealTime"), "only real time looks for casts to break");
                var assess = Body(engine, "AssessCast");
                Check(Calls(assess, typeof(EnemyThreat), "CastHarm") && Calls(assess, typeof(EnemyThreat), "RoundWorth") &&
                      Calls(assess, typeof(CombatMath), "ParamsOf"),
                      "a cast's stakes reuse the enemy threat reading; its level and concentration come from the game's parameters");
                Check(Calls(Body(typeof(EnemyThreat), "BestAbility"), typeof(EnemyThreat), "AbilityHarm") &&
                      Calls(Body(typeof(EnemyThreat), "CastHarm"), typeof(EnemyThreat), "AbilityHarm"),
                      "the threat assessment and the cast stakes share one harm reading");
                Check(Calls(Body(engine, "AddWeaponCandidates"), engine, "WeaponDisruptBonus"),
                      "weapon attacks (full, single, walk and attack) gain the disruption bonus");
                Check(Calls(Body(engine, "ScoreAbility"), engine, "AbilityDisruptBonus"), "damaging abilities gain it per enemy they hit");
                var modeBodies = engine.GetNestedTypes(Hidden).SelectMany(t => t.GetMethods(Hidden))
                    .Where(m => m.Name.Contains("<ChooseAttackModes>")).SelectMany(SaveBoostCheck.ReferencedMembers).ToList();
                Check(Calls(modeBodies, engine, "WeaponDisruptValue") &&
                      modeBodies.Any(m => m.DeclaringType == typeof(Candidate) && m.Name == "DisruptWalkSeconds") &&
                      modeBodies.Any(m => m.DeclaringType == typeof(Candidate) && m.Name == "DisruptFactor"),
                      "weapon mode sets reprice disruption with their own routine and the candidate's timing and survival weight");
                var bonus = Body(engine, "DisruptBonus");
                Check(Calls(bonus, typeof(DisruptRules), "FailChance") && Calls(bonus, typeof(DisruptRules), "InTime") &&
                      Calls(bonus, typeof(DisruptRules), "StillCasting"),
                      "the bonus counts only hits in time, each by the concentration rule");

                // Execute the production aggregator, not another copy of the pure formula or just its IL route.
                var castType = engine.GetNestedType("CastInfo", Hidden);
                var cast = Activator.CreateInstance(castType, true);
                void Set(string name, object value) => castType.GetField(name, Hidden).SetValue(cast, value);
                Set("SpellLevel", 2); Set("Concentration", 14); Set("Stakes", 100f); Set("SecondsLeft", 2f);
                var quote = engine.GetMethod("DisruptBonus", Hidden);
                float Quote(float hit, float damage, float at, float second = 0f)
                {
                    object[] args = { cast, hit, damage, at, second, damage, at + 0.7f, 0f };
                    return (float)quote.Invoke(null, args);
                }
                Check(Math.Abs(Quote(0.9f, 5f, 0.7f)) < 1e-4 && Math.Abs(Quote(0.4f, 11f, 0.7f) - 4f) < 1e-4,
                      "production disruption changes when a mode trades hit chance for damage across a native DC threshold");
                Check(Math.Abs(Quote(0.4f, 11f, 1.8f)) < 1e-4,
                      "production disruption rejects a hit inside the cast's final timing margin");
                Check(Math.Abs(Quote(0.4f, 11f, 0.7f, 0.4f) - 7.84f) < 1e-4,
                      "production disruption combines two timely hits without counting a cast break twice");
            }
            catch (Exception ex)
            {
                Check(false, $"{ex.GetType().Name}: {ex.Message}");
            }
            lines.Insert(0, $"CAST-DISRUPT {checks} checks, failures {bad}; native contracts, real blueprints and compiled routes; " +
                            "no actor, damage, roll, command or Unity timing");
            failures = bad;
            return lines;
        }
    }
}
