// ★ v0.3.14: the parts of driving the tactical engine that both combat modes share.
//
// Turn-based (Core/TurnBased/TurnBasedController) and real time (RealTimeTactics below) both decide one action at a
// time with TacticalEngine.Decide and issue it with TacticalEngine.Execute. What they share is learning from the
// previous command (an option that did nothing is not picked again this round) and the attempt loop when the
// dispatcher refuses an option. The waiting differs: turn-based waits inside the game's turn, real time waits for the
// unit's command slots to empty and its action cooldowns to run out (RealTimeController).
using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.EntitySystem.Entities;
using UnityEngine;

namespace CompanionAI_Pathfinder.Tactics
{
    public static class TacticalDriver
    {
        /// <summary>Candidates tried per decision when the dispatcher rejects one (e.g. no path).</summary>
        public const int MaxAttemptsPerDecision = 3;

        /// <summary>
        /// Learn from the previous command: if it ended without acting (interrupted, no path, target gone), do not pick
        /// the same option again this round. Moved unchanged from TurnBasedController (v0.3.1-v0.3.10 rules).
        /// </summary>
        public static void LearnFromLastCommand(UnitEntityData unit, TurnMemory memory, string logTag)
        {
            if (memory.LastCommand == null) return;
            var last = memory.LastCommand;
            bool isMove = memory.LastKind == CandidateKind.Move;
            float moved = Vector3.Distance(unit.Position, memory.LastIssuePosition);
            string notActed = null;
            // ★ v0.3.10: a move that went nowhere failed, whatever the command reports (an interrupted UnitMoveTo
            // can count as acted): Albedo issued 'Advance->Dretch' fifteen times without moving.
            if (isMove && moved < 0.1f)
                notActed = $"moved nowhere ({last.Result})";
            else if (!last.IsActed)
            {
                // ★ v0.3.2: a move that was cut short (its move action ran out) still made progress; the next
                // decision may continue with the standard action. v0.3.1 ended Albedo's turn after 8.9 m.
                if (!(isMove && moved >= 1f))
                    notActed = $"ended without acting ({last.Result})";
            }
            else if (!last.IsStarted && !isMove)
                // ★ v0.3.1: UnitCommandController.TickCommandTurnBased force-finishes an approaching command whose
                // unit stopped moving (or that has no action left) as "Success" - IsActed, but never started.
                // Albedo repeated 'Move+Attack->Dretch' (0.7 m, no attack) four times. Moves are finished that
                // way when they arrive (5-foot step), so this only applies to attacks and abilities.
                notActed = $"was force-finished before it started ({last.Result})";
            else if (!last.IsIgnoreCooldown && !ActionWasSpent(unit, memory.LastSlot))
                // Commands with IsIgnoreCooldown legitimately spend no action (sticky touch, spell combat...).
                notActed = $"ended ({last.Result}) but its {memory.LastSlot} action is still unused";

            if (notActed != null && memory.LastCandidateKey != null)
            {
                memory.Failed.Add(memory.LastCandidateKey);
                Main.Log($"{logTag} {unit.CharacterName}: '{memory.LastCandidateKey}' {notActed} - not retrying this turn");
            }
            memory.LastCommand = null;
            memory.LastCandidateKey = null;
        }

        /// <summary>Remember what was issued, so the next decision can learn from how it ended.</summary>
        public static void RecordIssued(UnitEntityData unit, TurnMemory memory, Candidate candidate,
                                        Kingmaker.UnitLogic.Commands.Base.UnitCommand command)
        {
            memory.LastCommand = command;
            memory.LastCandidateKey = candidate.Key;
            memory.LastSlot = candidate.Slot;
            memory.LastKind = candidate.Kind;
            memory.LastIssuePosition = unit.Position;
        }

        /// <summary>A rejected option: not tried again this round, and its enemy is unreachable when no path got there.</summary>
        public static void RecordRejected(TurnMemory memory, Candidate candidate, string failure)
        {
            // ★ v0.3.20 (test8): the game's walk provokes more attacks of opportunity than the straight line it was priced
            // on - priced again with them on the next decision, not dropped (TacticalEngine.RepriceRealPaths). Refused a
            // second time, it is dropped for the turn like any other.
            if (failure != null && failure.StartsWith(GameInterface.CommandDispatcher.MoreAttacksOfOpportunityFailure) &&
                candidate.SeenPathAttackers != null && !memory.PathAttackers.ContainsKey(candidate.Key))
            {
                memory.PathAttackers[candidate.Key] = (candidate.SeenPathFrom, candidate.SeenPathAttackers);
                return;
            }
            memory.Failed.Add(candidate.Key);
            // ★ v0.3.18 (test11): another direction for the next 5-foot step (TacticalEngine.AddMovementCandidates).
            if (TacticalEngine.IsFiveFootStepKey(candidate.Key) && candidate.Point.HasValue) memory.RefusedSteps.Add(candidate.Point.Value);
            // ★ v0.3.11: no path gets within reach of this enemy: an advance towards it this turn is wasted
            // (Albedo spent his 5-foot step walking 1.6 m towards an archer he could not reach, three rounds running).
            // ★ v0.3.20: also when the path ends in range without sight (it read "0.0m short" until 0.3.20-test2).
            if (candidate.Target != null && failure != null &&
                (failure.StartsWith(GameInterface.CommandDispatcher.OutOfReachFailure) ||
                 failure.StartsWith(GameInterface.CommandDispatcher.NoSightAtPathEndFailure)))
                memory.Unreachable.Add(candidate.Target.UniqueId);
            // ★ v0.3.20 (test3): the target is farther than this turn's walk-then-act allows - an advance towards it is
            // offered next to whatever can be done from here (TacticalEngine.AddMovementCandidates).
            if (candidate.Target != null && failure != null && failure.Contains(GameInterface.CommandDispatcher.LongerThanOneMoveFailure))
            {
                string id = candidate.Target.UniqueId;
                memory.TooFar[id] = memory.TooFar.TryGetValue(id, out float before) ? Math.Max(before, candidate.Value) : candidate.Value;
            }
        }

        /// <summary>Did the command that just ended use the action it was chosen for?</summary>
        private static bool ActionWasSpent(UnitEntityData unit, ActionSlot slot)
        {
            try
            {
                switch (slot)
                {
                    case ActionSlot.Standard:
                    case ActionSlot.FullRound:
                        return !unit.HasStandardAction();
                    case ActionSlot.Swift:
                        return !unit.HasSwiftAction();
                    default:
                        return true; // free actions, and moves (a move may legitimately leave movement unused)
                }
            }
            catch
            {
                return true;
            }
        }
    }

    /// <summary>
    /// ★ v0.3.14: real-time combat driven by the turn-based tactical engine.
    ///
    /// The real-time engine (UnifiedDecisionEngine) scored options as a geometric mean of a dozen 0..1 factors, where an
    /// option with no effect but no downside won: in the 0.3.14 test every real-time action of Lann, Aeiluvetar and
    /// Orvath was "Aid Another" (a melee-range action), so the ranged characters ran into melee, and not one weapon
    /// attack was chosen. The tactical engine values every option in damage-equivalent points with the game's rules
    /// and has been tuned on many turn-based fights. In real time its "actions left" are the game's real-time action
    /// cooldowns (UnitEntityData.HasStandardAction etc. read UnitCombatState.Cooldown), there is no 5-foot step, and a
    /// "round" is six seconds of combat (CombatMemory.Round).
    /// </summary>
    public static class RealTimeTactics
    {
        private sealed class IssueState
        {
            public int Hp;
            /// <summary>★ v0.3.18: the harmful areas the unit stood in when the command was chosen (unique ids).</summary>
            public HashSet<string> Areas;
            /// <summary>
            /// ★ v0.3.18 (test11): the option already counted on walking through harmful areas - a walk out of one ("Leave
            /// Stinking Cloud"), an advance or withdrawal priced with the areas on its way, an attack priced "[into ...]".
            /// Re-deciding when it enters them threw away walks it had chosen with that cost in mind: two leave-walks
            /// dropped part-way (after 1.6 m and 6 m), Seelah then took a Move+Attack and was nauseated.
            /// </summary>
            public bool AreasPriced;
            /// <summary>★ v0.3.19 (test2): the option the command came from (a stuck command is learnt from by it).</summary>
            public string Key;
        }

        private static HashSet<string> AreasAround(UnitEntityData unit)
        {
            var ids = new HashSet<string>();
            try
            {
                foreach (var area in Analysis.AoeDangerAnalyzer.InsideDanger(unit).Areas)
                    if (area != null) ids.Add(area.UniqueId);
            }
            catch { }
            return ids;
        }

        /// <summary>
        /// ★ v0.3.18 (docs/AI_TUNING_AUDIT.md B8): one of the unit's commands that the engine issued has not started, and
        /// the unit now stands in a harmful area it was not in when that command was chosen (a Stinking Cloud cast on it
        /// while it waited for its action cooldown). The engine decides again - whether to leave (its "Leave area" option
        /// keeps a target in reach and prices attacks of opportunity) or to act from where it stands. Once per area and
        /// command. The old pre-engine escape (RealTimeController) ran whenever the summed danger was 0.5 or more, with no
        /// target kept and no attacks of opportunity priced; it is kept for the old engine only.
        /// </summary>
        public static bool ShouldReplanInArea(UnitEntityData unit, out string why)
        {
            why = null;
            try
            {
                HashSet<string> now = null;
                foreach (var command in unit.Commands.Raw)
                {
                    if (command == null || command.IsStarted || command.IsFinished) continue;
                    if (!IssuedAtHp.TryGetValue(command, out var issued)) continue;
                    now = now ?? AreasAround(unit);
                    if (now.Count == 0) return false;
                    if (issued.AreasPriced) continue;
                    if (issued.Areas != null && now.IsSubsetOf(issued.Areas)) continue;
                    issued.Areas = now;
                    why = $"now in a harmful area while '{command.GetType().Name}' waited to start";
                    return true;
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// ★ v0.3.18 (C3): the controller cancelled a command that did not start (stuck). When it is the one the engine
        /// issued last, that option is not chosen again this round, and an enemy it could not get to is not advanced on
        /// either (the same as a turn-based option that did nothing). It went to a tracker only the old engine reads.
        /// </summary>
        public static void RecordStuck(UnitEntityData unit, Kingmaker.UnitLogic.Commands.Base.UnitCommand command, bool targetProblem)
        {
            try
            {
                // ★ v0.3.19 (test2): the option is kept with the command. The round's memory starts afresh every 6 s and a
                // walk is called stuck after 6 s or more, so the issuing round's memory was always gone: no "was stuck"
                // line in the whole 0.3.19-test1 log, and Aeiluvetar got the same no-sight Move+Attack six times
                // ("walking for 6 s without getting nearer", one attack in a 16-round fight).
                var memory = CombatMemory.ForTurn(unit);
                string key = IssuedAtHp.TryGetValue(command, out var issued) ? issued.Key : null;
                if (key == null && memory.LastCommand == command) key = memory.LastCandidateKey;
                if (key == null) return;
                memory.Failed.Add(key);
                // Not offered again while the unit stands where it got stuck (two rounds, as a refused walk).
                CombatMemory.NoteRefusedWalk(unit, key);
                var target = command.TargetUnit;
                if (targetProblem && target != null)
                {
                    memory.Unreachable.Add(target.UniqueId);
                    // ★ v0.3.19 (review): the target too, not only this option (Attack, full attack and spells on it walk the
                    // same way), for as long as the unit stands where it got stuck.
                    CombatMemory.NoteRefusedWalk(unit, TacticalEngine.StuckTargetKey(target));
                    // ★ v0.3.19 (test9): no way to see it from here - kept while neither moves (CombatMemory.NoteNoSight).
                    if (command.NeedLoS && !command.SightIsClear()) CombatMemory.NoteNoSight(unit, target);
                }
                Main.Log($"[RT-Tactics] {unit.CharacterName}: '{key}' was stuck - not chosen again from here");
                if (memory.LastCommand == command)
                {
                    memory.LastCommand = null;
                    memory.LastCandidateKey = null;
                }
            }
            catch { }
        }

        /// <summary>HP of the unit when the engine issued each command (weak: dropped with the command).</summary>
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Kingmaker.UnitLogic.Commands.Base.UnitCommand, IssueState> IssuedAtHp =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Kingmaker.UnitLogic.Commands.Base.UnitCommand, IssueState>();

        /// <summary>
        /// ★ v0.3.14 (test5): one of the unit's commands that the engine issued has not started, and since then the unit
        /// lost a quarter of its HP or more and is below half. Decided once per command.
        /// </summary>
        public static bool ShouldReplanAfterDamage(UnitEntityData unit, out string why)
        {
            why = null;
            try
            {
                int max = Math.Max(1, unit.MaxHP);
                foreach (var command in unit.Commands.Raw)
                {
                    if (command == null || command.IsStarted || command.IsFinished) continue;
                    if (!IssuedAtHp.TryGetValue(command, out var issued)) continue;
                    int lost = issued.Hp - unit.HPLeft;
                    if (lost >= max / 4 && unit.HPLeft < max / 2)
                    {
                        IssuedAtHp.Remove(command);
                        why = $"lost {lost} HP ({issued.Hp} -> {unit.HPLeft}) while '{command.GetType().Name}' waited to start";
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        /// <summary>★ v0.3.20 (audit PERF-001): a unit waiting quietly for its standard action's cooldown (RealTimePacing).</summary>
        private sealed class QuietWait
        {
            public double Until;
            public RealTimePacing.Snapshot Then;
        }

        private static readonly Dictionary<string, QuietWait> Quiet = new Dictionary<string, QuietWait>();
        private static int _quietSkips;

        /// <summary>Decisions left out by quiet waits since the last call (the fight's [Perf] line).</summary>
        public static int TakeQuietSkips()
        {
            int n = _quietSkips;
            _quietSkips = 0;
            return n;
        }

        /// <summary>A new fight: no quiet wait carries over.</summary>
        public static void ClearQuiet()
        {
            Quiet.Clear();
            _quietSkips = 0;
        }

        private static RealTimePacing.Snapshot SnapshotOf(UnitEntityData unit)
        {
            var snapshot = new RealTimePacing.Snapshot { Hp = unit.HPLeft };
            try { snapshot.EngagedBy = unit.CombatState?.EngagedBy?.Count(e => e != null && e.Descriptor.State.IsConscious) ?? 0; } catch { }
            try { snapshot.Areas = AreasAround(unit).Count; } catch { }
            try
            {
                foreach (var other in Kingmaker.Game.Instance.State.Units)
                {
                    if (other == null || other.Descriptor?.State == null || !other.Descriptor.State.IsConscious ||
                        !(other.CombatState?.IsInCombat ?? false)) continue;
                    if (unit.IsEnemy(other)) snapshot.Enemies++;
                    else if (other.IsPlayerFaction) snapshot.Allies++;
                }
            }
            catch { }
            return snapshot;
        }

        /// <summary>Decide and issue one action; false when nothing is worth doing now (decided again later).</summary>
        public static bool Act(UnitEntityData unit)
        {
            if (unit == null) return false;
            try
            {
                // ★ v0.3.20 (audit PERF-001): still waiting quietly for the cooldown, and nothing changed since.
                if (Quiet.TryGetValue(unit.UniqueId, out var quiet))
                {
                    double now = Kingmaker.Game.Instance.TimeController.GameTime.TotalSeconds;
                    if (!RealTimePacing.ShouldWake(now, quiet.Until, quiet.Then, SnapshotOf(unit)))
                    {
                        _quietSkips++;
                        return false;
                    }
                    Quiet.Remove(unit.UniqueId);
                }

                CombatMemory.Sync();
                var memory = CombatMemory.ForTurn(unit);
                TacticalDriver.LearnFromLastCommand(unit, memory, "[RT-Tactics]");

                for (int attempt = 0; attempt < TacticalDriver.MaxAttemptsPerDecision; attempt++)
                {
                    var candidate = TacticalEngine.Decide(unit, memory);
                    if (candidate == null)
                    {
                        // ★ v0.3.20 (audit PERF-001): nothing worth doing with the standard action on cooldown - look again
                        // when it ends (at most 2 s), or as soon as something changes.
                        float seconds = 0f;
                        try
                        {
                            float interval = Settings.ModSettings.Instance?.RealTimeDecisionInterval ?? 0.5f;
                            seconds = RealTimePacing.QuietSeconds(unit.HasStandardAction(), unit.CombatState.Cooldown.StandardAction, interval);
                        }
                        catch { }
                        if (seconds > 0f)
                            Quiet[unit.UniqueId] = new QuietWait
                            {
                                Until = Kingmaker.Game.Instance.TimeController.GameTime.TotalSeconds + seconds,
                                Then = SnapshotOf(unit)
                            };
                        return false;
                    }

                    memory.Decisions++;
                    if (TacticalEngine.Execute(unit, candidate, out var command, out string failure))
                    {
                        TacticalDriver.RecordIssued(unit, memory, candidate, command);
                        // ★ v0.3.19 (release review): a toggle switched without a command (an infusion off, a performance
                        // the game starts at once) has none to remember; the weak table throws on null, which ended in the
                        // catch below - an error line and "nothing issued" for an option that was carried out.
                        if (command == null) return true;
                        IssuedAtHp.Remove(command);
                        IssuedAtHp.Add(command, new IssueState
                        {
                            Hp = unit.HPLeft, Areas = AreasAround(unit), Key = candidate.Key,
                            AreasPriced = candidate.Kind == CandidateKind.Move ||
                                          (candidate.Label?.Contains(TacticalEngine.Disengage.IntoTag) ?? false)
                        });
                        return true;
                    }
                    Main.Log($"[RT-Tactics] {unit.CharacterName}: '{candidate.Label}' could not be issued ({failure}) - trying next best");
                    TacticalDriver.RecordRejected(memory, candidate, failure);
                    if (failure != null && failure.StartsWith(GameInterface.CommandDispatcher.WalksIntoAreaFailure))
                        CombatMemory.NoteRefusedWalk(unit, candidate.Key);
                }
                return false;
            }
            catch (Exception ex)
            {
                Main.Error($"[RT-Tactics] {unit.CharacterName}: {ex}");
                return false;
            }
        }
    }
}
