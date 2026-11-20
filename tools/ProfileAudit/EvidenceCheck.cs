// ★ evidence for the next playtest (docs/R_ITEMS_PLAN.md, R1/R4): the native events and fields TimelineObserver reads, and
// that it only reads - it issues, interrupts and triggers nothing. Actor-free: no Game/Player, Rulebook, command or timing.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Blueprints;
using Kingmaker.PubSubSystem;
using Kingmaker.RuleSystem.Rules;
using Kingmaker.RuleSystem.Rules.Abilities;
using Kingmaker.RuleSystem.Rules.Damage;
using Kingmaker.UnitLogic.Commands;
using Kingmaker.UnitLogic.Commands.Base;

namespace ProfileAudit
{
    internal static class EvidenceCheck
    {
        private const BindingFlags Hidden = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static List<MemberInfo> Body(Type type, string name) => BuffSuppressCheck.BodyWithLambdas(type, name);
        private static bool Calls(IEnumerable<MemberInfo> body, Type type, string name) =>
            body.Any(m => m.DeclaringType == type && m.Name == name);

        internal static List<string> Run(IEnumerable<SimpleBlueprint> loaded, out int failures)
        {
            int checks = 0, bad = 0; var lines = new List<string>();
            void Check(bool ok, string why) { checks++; if (!ok) { bad++; lines.Add("EVIDENCE FAILED: " + why); } }
            try
            {
                // --- native contracts ---
                var fail = Body(typeof(UnitUseAbility), "FailIfConcentrationCheckFailed");
                Check(fail.Any(m => m.Name == "get_ConcentrationCheckFailed") && fail.Any(m => m.Name == "ForceFinish"),
                    "native: a failed concentration check finishes the cast as failed, without acting");
                Check(typeof(RuleCheckConcentration).GetProperty("Success")?.PropertyType == typeof(bool),
                    "native: RuleCheckConcentration carries its result (Success)");
                Check(typeof(RuleAttackWithWeapon).GetProperty("IsAttackOfOpportunity")?.PropertyType == typeof(bool) &&
                      typeof(RuleAttackRoll).GetProperty("RuleAttackWithWeapon") != null,
                    "native: an attack roll names its attack of opportunity");
                Check(typeof(RuleAttackRoll).GetProperty("TargetIsFlanked")?.PropertyType == typeof(bool),
                    "native: an attack roll carries the target's flanked state");
                Check(typeof(UnitAttack).GetMethod("GetAttackIndex") != null && typeof(UnitAttack).GetProperty("AllAttacks") != null &&
                      typeof(UnitCommand).GetProperty("TimeSinceStart") != null,
                    "native: a weapon attack exposes its attack index, its routine and its time since start");

                // --- routes ---
                var observer = typeof(TimelineObserver);
                Check(typeof(IGlobalRulebookHandler<RuleAttackRoll>).IsAssignableFrom(observer) &&
                      typeof(IGlobalRulebookHandler<RuleDealDamage>).IsAssignableFrom(observer) &&
                      typeof(IGlobalRulebookHandler<RuleCheckConcentration>).IsAssignableFrom(observer) &&
                      typeof(IUnitCommandActHandler).IsAssignableFrom(observer),
                    "the observer listens to attack rolls, damage, concentration checks and command acts");
                var engine = typeof(TacticalEngine);
                Check(Calls(Body(engine, "Execute"), observer, "NoteIssued"), "an issued command is noted for the evidence");
                var combatEnd = engine.Assembly.GetTypes().SelectMany(t => t.GetMethods(Hidden)).FirstOrDefault(m => m.Name == "OnPartyCombatEnd");
                Check(combatEnd != null && SaveBoostCheck.ReferencedMembers(combatEnd).Any(m => m.DeclaringType == observer && m.Name == "ReportFight") &&
                      Calls(Body(observer, "Clear"), observer, "ReportFight"),
                    "each fight's evidence is logged when it ends (or when the next one starts without an end)");
                Check(Calls(Body(observer, "NoteFlank"), typeof(CombatMath), "ForecastFlank") &&
                      Calls(Body(typeof(CombatMath), "ForecastFlank"), typeof(CombatMath), "PredictFlank"),
                    "R1: the flank compared at the roll is the engine's own forecast");
                var noteBreak = Body(observer, "NoteBreak");
                Check(Calls(noteBreak, typeof(DisruptRules), "WeaponHitSeconds") && Calls(noteBreak, typeof(DisruptRules), "AbilityHitSeconds") &&
                      Calls(noteBreak, typeof(BurstWatch), "NativeCastTime"),
                    "R4: the break forecast uses the engine's own timing rules and the native cast time");
                // Read only: nothing in the observer issues, interrupts or triggers.
                var all = observer.GetMethods(Hidden | BindingFlags.DeclaredOnly).SelectMany(m => SaveBoostCheck.ReferencedMembers(m)).ToList();
                Check(!all.Any(m => m.Name == "Interrupt" || m.Name == "TriggerEvent" || m.Name == "Run" || m.Name == "Start" ||
                                    m.Name == "ForceFinish" || m.Name == "Spend"),
                    "the observer only reads: it issues, interrupts, triggers and spends nothing");
                Check((int)observer.GetField("MaxRecords", Hidden).GetValue(null) == 32, "break records are bounded per fight");
                TimelineObserver.ReportFight();   // an empty fight logs nothing and does not throw
                Check(true, "an empty fight's report runs");
            }
            catch (Exception ex) { Check(false, "exception " + ex.GetType().Name + ": " + ex.Message); }
            failures = bad;
            lines.Insert(0, $"EVIDENCE {checks} checks, {bad} failures");
            return lines;
        }
    }
}
