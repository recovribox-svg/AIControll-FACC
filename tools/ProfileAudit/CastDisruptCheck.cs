// ★ cast disruption (docs/BURST_AIM_AND_TACTICS_PLAN.md §4, slice B): a hit on an enemy whose spell has not gone off forces a
// concentration check. Native contracts on the real assembly, which real blueprints the rule covers, and the compiled
// routes into the scoring. Inert command/routine layouts are executed; no live Game/Player, actor setup, native command
// execution, damage, dice roll or Unity timing.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
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

                // Pending commands may have delivered their first two attacks already, or reordered their hands for an
                // animation sequence. Exercise the production cache reader on that layout, not another selector copy.
                var math = typeof(CombatMath);
                var routineType = math.GetNestedType("AttackRoutine", Hidden);
                var lineType = math.GetNestedType("AttackLine", Hidden);
                var routine = Activator.CreateInstance(routineType, true);
                routineType.GetField("Frame", Hidden).SetValue(routine, UnityEngine.Time.frameCount);
                var linesInRoutine = (IList)routineType.GetField("Lines", Hidden).GetValue(routine);
                var weaponA = (Kingmaker.Items.ItemEntityWeapon)RuntimeHelpers.GetUninitializedObject(typeof(Kingmaker.Items.ItemEntityWeapon));
                var weaponB = (Kingmaker.Items.ItemEntityWeapon)RuntimeHelpers.GetUninitializedObject(typeof(Kingmaker.Items.ItemEntityWeapon));
                void Line(Kingmaker.Items.ItemEntityWeapon weapon, int penalty, int ab, float damage)
                {
                    var line = Activator.CreateInstance(lineType, true);
                    void SetLine(string field, object value) => lineType.GetField(field, Hidden).SetValue(line, value);
                    SetLine("Weapon", weapon); SetLine("AttackBonusPenalty", penalty); SetLine("AttackBonus", ab);
                    SetLine("ArmorClass", 20); SetLine("RawPerHit", damage); SetLine("GameHit", damage);
                    linesInRoutine.Add(line);
                }
                Line(weaponA, 0, 17, 12f); Line(weaponA, -5, 12, 12f); Line(weaponA, -10, 7, 12f);
                Line(weaponB, 0, 15, 30f);
                var estimate = new WeaponAttackEstimate();
                typeof(WeaponAttackEstimate).GetField("RoutineIdentity", Hidden).SetValue(estimate, routine);
                typeof(WeaponAttackEstimate).GetField("RoutineLands", Hidden).SetValue(estimate, 1f);
                var orderedHit = math.GetMethod("TryOrderedHit", Hidden);
                object[] hitArgs;
                bool Ordered(Kingmaker.Items.ItemEntityWeapon weapon, int penalty)
                {
                    hitArgs = new object[] { estimate, weapon, penalty, 0f, 0f };
                    return (bool)orderedHit.Invoke(null, hitArgs);
                }
                hitArgs = null;
                Check(Ordered(weaponA, -10) && Math.Abs((float)hitArgs[3] - 0.4f) < 1e-4 && (float)hitArgs[4] == 12f,
                      "pending third iterative uses its own penalty/hit chance, not the cached second attack");
                Check(Ordered(weaponB, 0) && Math.Abs((float)hitArgs[3] - 0.8f) < 1e-4 && (float)hitArgs[4] == 30f,
                      "a reordered pending off-hand uses its own weapon's damage and chance");
                Check(!Ordered(weaponA, -15) && (float)hitArgs[3] == 0f && (float)hitArgs[4] == 0f,
                      "an unquoted pending line is unknown; no earlier attack's damage or chance is substituted");
                typeof(WeaponAttackEstimate).GetField("RoutineLands", Hidden).SetValue(estimate, 0.5f);
                Check(Ordered(weaponA, -10) && Math.Abs((float)hitArgs[3] - 0.2f) < 1e-4,
                      "pending-hit lookup preserves the routine's concealment/image pass");
                routineType.GetField("Frame", Hidden).SetValue(routine, UnityEngine.Time.frameCount + 1);
                Check(!Ordered(weaponA, -10), "a stale-frame routine cannot supply a pending hit");
                var orderedRoute = Body(engine, "OthersStill");
                Check(Calls(orderedRoute, math, "TryOrderedHit") && orderedRoute.Any(m => m.Name.Contains("AttackBonusPenalty")),
                      "the pending-attack production route matches each native command line by its iterative penalty");
                Check(orderedRoute.Any(m => m.Name == "get_ApproachRadius") && orderedRoute.Any(m => m.Name == "get_IsRanged") &&
                      !orderedRoute.Any(m => m.DeclaringType == typeof(WeaponAttackEstimate) && (m.Name == "IsRanged" || m.Name == "Reach")),
                      "ordered delivery uses the pending hand's range type and the issued command's approach radius");
                Check(Body(math, "EstimateWeaponAttackCore").Any(m => m.Name == "SecondWeapon") &&
                      Body(engine, "WeaponDisruptValue").Any(m => m.Name == "SecondWeapon"),
                      "a prospective mixed routine carries its second weapon into that hit's timing, instead of borrowing the first hand");

                // ★ review follow-up F2: the native attack schedule and allied hits already ordered.
                var tick = Body(typeof(UnitAttack), "OnTick");
                Check(tick.Any(m => m.Name == "m_AnimationsDuration") && tick.Any(m => m.Name == "m_AttackIndex") &&
                      tick.Any(m => m.Name == "GetAnimationStyle") && tick.Any(m => m.Name == "get_IsSequential") &&
                      Calls(tick, typeof(UnitCommand), "get_TimeSinceStart"),
                      "F2: real time, attack k starts k x window / N into the command; bow-style and sequenced attacks chain");
                Check(Body(typeof(UnitAttack), "Init").Any(m => m.Name == "m_AnimationsDuration"),
                      "F2: the attack window is set when the command is initialised (6 s)");
                Check(Calls(Body(typeof(UnitCommand), "Tick"), typeof(UnitCommand), "set_TimeSinceStart") &&
                      Calls(Body(typeof(UnitCommand), "Start"), typeof(UnitCommand), "set_IsStarted"),
                      "F2: a command's time counts from its start, after any approach");
                var value = Body(engine, "WeaponDisruptValue");
                Check(Calls(value, typeof(DisruptRules), "SwingSpacing") && Calls(value, engine, "ProspectiveChainedAttacks") &&
                      Calls(value, engine, "AfterOrderedHits") && Calls(Body(engine, "AbilityDisruptBonus"), engine, "AfterOrderedHits"),
                      "F2: weapon and ability disruption use the native spacing and the allied hits already ordered");
                var others = Body(engine, "OthersStill");
                Check(others.Any(m => m.Name == "get_Standard") && Calls(others, typeof(UnitAttack), "get_IsAttackFull") &&
                      Calls(others, typeof(UnitAttack), "GetAttackIndex") && Calls(others, typeof(UnitCommand), "get_TimeSinceStart") &&
                      Calls(others, typeof(UnitCommand), "get_IsStarted") && Calls(others, typeof(DisruptRules), "RunningHitSeconds") &&
                      Calls(others, typeof(DisruptRules), "InTime") && Calls(others, typeof(DisruptRules), "FailChance"),
                      "F2: allies' running or approaching attacks on the caster are read from their commands, on the same rules");
                var chainedBody = Body(engine, "OrderedChainedAttacks");
                Check(chainedBody.Any(m => m.Name == "get_IsSequential") && chainedBody.Any(m => m.Name == "get_IsReleased") &&
                      Calls(chainedBody, engine, "ProspectiveChainedAttacks") &&
                      !Body(engine, "ProspectiveChainedAttacks").Any(m => m.Name == "get_Sequences"),
                      "F2: issued sequences use selected adjacent attacks and release state; future sequence tables prove nothing");
                var issued = (UnitAttack)RuntimeHelpers.GetUninitializedObject(typeof(UnitAttack));
                var preceding = (AttackHandInfo)RuntimeHelpers.GetUninitializedObject(typeof(AttackHandInfo));
                var following = (AttackHandInfo)RuntimeHelpers.GetUninitializedObject(typeof(AttackHandInfo));
                typeof(UnitAttack).GetField("m_AllAttacks", Hidden).SetValue(issued, new List<AttackHandInfo> { preceding, following });
                var chainReader = engine.GetMethod("OrderedChainedAttacks", Hidden);
                bool Chain() => (bool)chainReader.Invoke(null, new object[] { issued, 1 });
                Check(!Chain(), "an issued attack without a selected sequence does not get sequence timing");
                var selectedField = typeof(AttackHandInfo).GetField("<IsSequential>k__BackingField", Hidden);
                selectedField.SetValue(preceding, true); selectedField.SetValue(following, true);
                var handle = (Kingmaker.Visual.Animation.Kingmaker.UnitAnimationActionHandle)RuntimeHelpers.GetUninitializedObject(
                    typeof(Kingmaker.Visual.Animation.Kingmaker.UnitAnimationActionHandle));
                typeof(AttackHandInfo).GetField("<AnimationHandle>k__BackingField", Hidden).SetValue(preceding, handle);
                Check(!Chain(), "selected adjacent attacks alone cannot bypass the native previous-animation release gate");
                typeof(Kingmaker.Visual.Animation.Actions.AnimationActionHandle).GetField("<IsReleased>k__BackingField", Hidden)
                    .SetValue(handle, true);
                Check(Chain(), "selected adjacent attacks with the previous animation released use the native shortcut");
                selectedField.SetValue(following, false);
                Check(!Chain(), "a sequence boundary does not chain a following unselected attack");
                var windowReader = engine.GetMethod("TryAttackWindow", Hidden);
                var windowField = typeof(UnitAttack).GetField("m_AnimationsDuration", Hidden);
                windowField.SetValue(issued, 3f);
                object[] windowArgs = { issued, 0f };
                Check((bool)windowReader.Invoke(null, windowArgs) && (float)windowArgs[1] == 3f,
                      "issued IgnoreCooldown timing reads the command's configured three-second window, not a copied six");
                windowField.SetValue(issued, 0f);
                Check(!(bool)windowReader.Invoke(null, windowArgs), "an uninitialised window proves no allied hit timing");
                var reorder = Body(typeof(UnitAttack), "TryToReorder");
                Check(reorder.Any(m => m.Name == "Probability") && Calls(reorder, typeof(UnityEngine.Random), "Range"),
                      "native animation sequence selection is probabilistic; forecasting must not roll it during scoring");
                Check(Calls(others, engine, "TryAttackWindow") && Calls(others, engine, "OrderedChainedAttacks"),
                      "pending-hit quotes use their actual configured window and per-transition native shortcut");
                var readiness = Body(engine, "TryOrderedDelay");
                var nativeStart = Body(typeof(UnitCommandController), "CanStart");
                Check(nativeStart.Any(m => m.Name == "HasCooldownForCommand") && nativeStart.Any(m => m.Name == "get_CanActInCombat") &&
                      readiness.Any(m => m.Name == "HasCooldownForCommand") && readiness.Any(m => m.Name == "get_CanStart") &&
                      readiness.Any(m => m.Name == "get_StandardAction" || m.Name == "StandardAction"),
                      "ordered-hit readiness follows native command gates and the actual remaining standard cooldown");
                Check(nativeStart.Any(m => m.Name == "IsUpdateScheduledFor") && readiness.Any(m => m.Name == "IsUpdateScheduledFor"),
                      "an issued but unstarted attack cannot claim a timely hit while native equipment updating blocks its start");
                Check(Calls(others, engine, "TryOrderedDelay") && Calls(others, typeof(DisruptRules), "OrderedStartSeconds") &&
                      DisruptRules.OrderedStartSeconds(2f, 1f) == 2f && DisruptRules.OrderedStartSeconds(1f, 2f) == 2f,
                      "a queued approach overlaps cooldown; the issued-hit quote cannot treat a cooling ally as ready now");
                Check(Body(engine, "ReadCasting").Any(m => m.Name == "OthersRead"),
                      "F2: the allied hits are read again at every decision");
                var after = engine.GetMethod("AfterOrderedHits", Hidden);
                float After(float othersStill, float own, float chance0, out float chance1)
                {
                    Set("OthersRead", true); Set("OthersStill", othersStill);
                    object[] args = { null, null, cast, own, chance0 };
                    float v = (float)after.Invoke(null, args);
                    chance1 = (float)args[4];
                    return v;
                }
                float soloValue = Quote(0.4f, 11f, 0.7f);
                float shared = After(0.5f, soloValue, 0.04f, out float sharedChance);
                float alone = After(1f, soloValue, 0.04f, out float aloneChance);
                Check(Math.Abs(shared - 2f) < 1e-4 && Math.Abs(sharedChance - 0.02f) < 1e-5 &&
                      Math.Abs(alone - soloValue) < 1e-4 && Math.Abs(aloneChance - 0.04f) < 1e-5,
                      "F2: production aggregator: an ally's ordered hit surviving half the time halves this hit's break value");
                Check(DisruptRules.SwingSpacing(3, false) == 2f && DisruptRules.SwingSpacing(3, true) == DisruptRules.NextSwingSeconds &&
                      !DisruptRules.InTime(DisruptRules.WeaponHitSeconds(1, 0f, false, 2f, DisruptRules.SwingSpacing(3, false)), 2f),
                      "F2: a 3-attack routine's second hit lands too late for a 2 s cast");
            }
            catch (Exception ex)
            {
                Check(false, $"{ex.GetType().Name}: {ex.Message}");
            }
            lines.Insert(0, $"CAST-DISRUPT {checks} checks, failures {bad}; native contracts, real blueprints, production/layout fixtures and compiled routes; " +
                            "no live actor, native command execution, damage, dice roll or Unity timing");
            failures = bad;
            return lines;
        }
    }
}
