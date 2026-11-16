// ★ v0.3.52: diagnostic-only production guards, without actors or a logger.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CompanionAI_Pathfinder.Analysis;
using CompanionAI_Pathfinder.GameInterface;
using CompanionAI_Pathfinder.Tactics;

namespace ProfileAudit
{
    internal static class DiagnosticLogCheck
    {
        internal static List<string> Run(out int failures)
        {
            int checks = 0, bad = 0; var lines = new List<string>();
            void Check(bool ok, string why) { checks++; if (!ok) { bad++; lines.Add("DIAGNOSTIC-LOG FAILED: " + why); } }
            try
            {
                const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
                var assembly = typeof(CombatMath).Assembly; var gate = assembly.GetType("CompanionAI_Pathfinder.Core.DiagnosticChangeLog`1").MakeGenericType(typeof(int));
                var observe = gate.GetMethod("Observe", all); var memo = Activator.CreateInstance(gate, all, null, new object[] { 2 }, null);
                bool See(int epoch, string key, int value) => (bool)observe.Invoke(memo, new object[] { epoch, key, value });
                Check(See(1, "caster", 4) && !See(1, "caster", 4) && See(1, "caster", 6), "actual mod gate reports first and changed scalar, suppresses duplicate");
                Check(See(1, "other", 4) && !See(1, "overflow", 4) && !See(1, "caster", 6), "capacity does not clear and replay old messages");
                Check(See(2, "caster", 6) && !See(2, null, 4), "new fight resets diagnostic entries; null identity remains quiet");
                var refs = SaveBoostCheck.ReferencedMembers(observe).ToArray();
                Check(!refs.Any(m => m.DeclaringType?.Namespace?.StartsWith("Kingmaker") == true || m.DeclaringType?.Namespace?.StartsWith("UnityEngine") == true),
                    "bounded memo has no game/Unity dependency or actor query");
                var policy = assembly.GetType("CompanionAI_Pathfinder.Core.DiagnosticLogPolicy");
                var roundNote = policy.GetMethod("RoundNoteKey", all);
                string Key(int fight) => (string)roundNote.Invoke(null, new object[] { "buffwhy", fight, "Lann", 1, "Dexterity2:Lann" });
                Check(Key(1) == Key(1) && Key(1) != Key(7),
                    "the actual round-note identity repeats within a fight and is fresh for the next fight's same cast");
                var engine = typeof(TacticalEngine);
                var choose = engine.GetMethods(all).Single(m => m.Name == "Decide" && m.GetParameters().Any(p => p.ParameterType == typeof(bool)));
                var chooseRefs = SaveBoostCheck.ReferencedMembers(choose).ToArray();
                Check(chooseRefs.Count(m => m.DeclaringType == policy && m.Name == "RoundNoteKey") == 2 &&
                      chooseRefs.Any(m => m.DeclaringType == typeof(CombatMemory) && m.Name == "get_Fight"),
                    "both selected-buff and restraint diagnostics use the fight-scoped round identity in production");
                var decision = SaveBoostCheck.ReferencedMembers(engine.GetMethod("LogDecision", all)).ToArray();
                Check(Array.FindIndex(decision, m => m.Name == "WriteDecision") < Array.FindIndex(decision, m => m.Name == ".ctor" && m.DeclaringType == typeof(System.Text.StringBuilder)),
                    "production quiet-decision policy runs before StringBuilder construction");
                Check(decision.Any(m => m.Name == "get_VerboseOn"), "production summary policy reads actual current verbosity");
                var pen = SaveBoostCheck.ReferencedMembers(typeof(HitChanceCalculator).GetMethod("GetSpellPenetrationBonus", all)).ToArray();
                Check(Array.FindIndex(pen, m => m.Name == "get_VerboseOn") < Array.FindIndex(pen, m => m.Name == "Observe") && pen.Any(m => m.Name == "get_Fight"),
                    "spell-penetration diagnostics are gated by verbosity and fight epoch after calculation");
                var action = SaveBoostCheck.ReferencedMembers(typeof(RealTimeController).GetMethod("NoteActionState", all)).ToArray();
                var enemies = SaveBoostCheck.ReferencedMembers(engine.GetMethod("NoteEnemyAvailability", all)).ToArray();
                Check(action.Any(m => m.Name == "Observe") && enemies.Any(m => m.Name == "Observe") &&
                    action.Any(m => m.Name == "get_VerboseOn") && enemies.Any(m => m.Name == "get_VerboseOn"),
                    "blocked/empty-enemy states use diagnostic transition gates only in verbose mode");
                // The public weapon/ability entry points forward to these internal formatting bodies.
                foreach (var name in new[] { "CalculateFullAttackHitChance", "CalculateSaveDC", "CalculateEnergyResistance", "CalculateWeaponHitChanceInternal", "CalculateAbilityHitChanceInternal" })
                {
                    var methods = typeof(HitChanceCalculator).GetMethods(all).Where(m => m.Name == name).ToArray();
                    Check(methods.Any(m => SaveBoostCheck.ReferencedMembers(m).Any(x => x.Name == "get_VerboseOn")), "native math logging guards formatting in " + name);
                }
                var idle = SaveBoostCheck.ReferencedMembers(engine.GetMethod("IdleAdvanceNote", all)).ToArray();
                Check(idle.Any(m => m.Name == "get_VerboseOn") && idle.Any(m => m.Name == "ReportOnce") &&
                    !idle.Any(m => m.Name == "CanPrepare" || m.Name == "ReadRoutineCore" || m.Name == "EstimateWeaponAttack" || m.Name == "FindPath"),
                    "new idle witness uses bounded lazy reporting, without path/weapon queries");
                var pathLog = assembly.GetType("CompanionAI_Pathfinder.Core.PathEndDiagnosticLog", true);
                var snapshot = pathLog.GetNestedType("Snapshot", all); var pathMemo = Activator.CreateInstance(pathLog, true);
                var observePath = pathLog.GetMethod("Observe", all); object state = Activator.CreateInstance(snapshot);
                void Set(string field, object value) => snapshot.GetField(field, all).SetValue(state, value);
                Set("GoalX", 93.6f); Set("GoalY", 45.1f); Set("Radius", 16.24f); Set("NeedSight", true);
                bool Path(int fight, string unit) => (bool)observePath.Invoke(pathMemo, new object[] { fight, unit, state });
                Check(Path(1, "caster") && !Path(1, "caster") && Path(1, "other"), "actual path witness memo suppresses duplicates without sharing actors");
                Set("EndX", 1f); Check(Path(1, "caster") && !Path(1, "caster") && Path(2, "caster"),
                    "actual path memo preserves material change and first witness of the next fight");
                Check((int)pathLog.GetField("MaxUnits", all).GetRawConstantValue() == 128 &&
                      (int)pathLog.GetField("MaxQueriesPerUnit", all).GetRawConstantValue() == 16 &&
                      (int)pathLog.GetField("MaxReportsPerQuery", all).GetRawConstantValue() == 5,
                    "path witness identities/repeats have hard per-fight bounds");
                var reachRefs = SaveBoostCheck.ReferencedMembers(typeof(CommandDispatcher).GetMethod("PathEndVerdict", all)).ToArray();
                Check(reachRefs.Any(m => m.Name == "IsUnitCloseEnough") && reachRefs.Any(m => m.Name == "MechanicsDistance") &&
                      reachRefs.Any(m => m.Name == "JudgePathEnd") && !reachRefs.Any(m => m.DeclaringType == typeof(string)),
                    "native path verdict remains authoritative and formats no success/quiet witness strings");
                var pathNote = SaveBoostCheck.ReferencedMembers(typeof(CommandDispatcher).GetMethod("LogPathEndRefusal", all)).ToArray();
                Check(Array.FindIndex(pathNote, m => m.Name == "get_VerboseOn") < Array.FindIndex(pathNote, m => m.Name == "Observe") &&
                      Array.FindIndex(pathNote, m => m.Name == "Observe") < Array.FindIndex(pathNote, m => m.Name == "FormatPathEnd"),
                    "production verbosity and bounded change gate precede all path witness formatting");
                Check(!pathNote.Any(m => m.Name == "FindPath" || m.Name == "CanPrepare" || m.Name == "Trigger" ||
                      m.Name == "IsUnitCloseEnough" || m.Name == "set_ForcedPath") &&
                      !SaveBoostCheck.ReferencedMembers(observePath).Any(m => m.DeclaringType?.Namespace?.StartsWith("Kingmaker") == true ||
                        m.DeclaringType?.Namespace?.StartsWith("UnityEngine") == true),
                    "diagnostic suppression has no path/rule/command mutation or game dependency");
            }
            catch (Exception ex) { bad++; lines.Add("DIAGNOSTIC-LOG FAILED: " + ex.GetBaseException()); }
            failures = bad; lines.Insert(0, $"DIAGNOSTIC-LOG {checks} checks, failures {bad}; actual bounded memo and compiled guards; no actor, logger, command or Unity timing execution");
            return lines;
        }
    }
}
