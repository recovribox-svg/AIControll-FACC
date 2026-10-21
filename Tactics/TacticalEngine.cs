// ★ v0.3.0: Turn-based tactical engine - decides ONE action at a time from the unit's current action economy.
//
// Why step-wise instead of the old whole-turn TurnPlan: the old planner invalidated its own plan whenever the unit
// moved ("Position changed by 5.1m" → replan → "Retreat - no targets"), and it never considered plain weapon attacks,
// so archers (Lann) and animal companions (Albedo) had no attack options at all. Here every decision is re-derived from
// game state after the previous command finished, and every option is valued in the same unit (expected damage /
// damage-equivalent points) so weapons, spells, consumables, buffs, heals and movement compete fairly.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Kingmaker;
using Kingmaker.Controllers.Combat;
using Kingmaker.Controllers.Units;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Items;
using Kingmaker.Blueprints.Items.Equipment;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Commands;
using Kingmaker.UnitLogic.Commands.Base;
using Kingmaker.Utility;
using TurnBased.Controllers;
using UnityEngine;
using CompanionAI_Pathfinder.Analysis;
using CompanionAI_Pathfinder.GameInterface;
using CompanionAI_Pathfinder.Settings;

namespace CompanionAI_Pathfinder.Tactics
{
    /// <remarks>★ v0.3.18 (AA-2): Toggle - switch on an activatable ability whose activation is a game command
    /// (bardic performances, Rage family, shaman spirits): TacticalEngine.Toggles.cs.</remarks>
    public enum CandidateKind { WeaponAttack, Ability, Move, Toggle }
    public enum ActionSlot { Free, Swift, Move, Standard, FullRound }

    public sealed class Candidate
    {
        public CandidateKind Kind;
        public ActionSlot Slot;
        public float Value;
        public UnitEntityData Target;
        public Vector3? Point;
        public AbilityData Ability;
        public bool FullAttack;
        public bool NeedsMove;
        public float ApproachRadius;
        public bool NeedLoS;
        /// <summary>Damages or disables an enemy (as opposed to buffs, heals, utility).</summary>
        public bool Offensive;
        public string Label;
        public string Key;
        /// <summary>★ v0.3.7: chance the effect lands on its main target (1 for non-hostile options).</summary>
        public float SuccessChance = 1f;
        /// <summary>
        /// ★ v0.3.16: what casting this where the unit stands risks (attacks of opportunity), already in Value.
        /// ★ v0.3.17: all of it - the cost and the share lost to the chance the spell is spoilt.
        /// </summary>
        public float CastRiskCost;
        /// <summary>★ v0.3.17: the aimed enemy itself is affected (false: an area centred on it for the enemies around it).</summary>
        public bool AimAffected = true;
        /// <summary>
        /// ★ v0.3.17: walk to <see cref="MovePoint"/> first (a spot around a harmful area, the one the value was priced
        /// for), and act from there on the next decision.
        /// </summary>
        public bool MoveFirst;
        public Vector3? MovePoint;
        /// <summary>★ v0.3.17: the move was priced as entering no harmful area: refuse a game path that walks into one.</summary>
        public bool CheckPath;

        /// <summary>★ v0.3.18 (AA-2): the activatable ability a Toggle candidate switches on.</summary>
        public Kingmaker.UnitLogic.ActivatableAbilities.ActivatableAbility Toggle;
        /// <summary>★ v0.3.18 (AA-3): attack modes to switch (on / off) just before this weapon attack.</summary>
        public List<KeyValuePair<Kingmaker.UnitLogic.ActivatableAbilities.ActivatableAbility, bool>> Modes;
        /// <summary>★ v0.3.18 (AA-3): how much of a change in the attack's damage value reaches this option's value (its
        /// weights and risks); 0 for options that are not weapon attacks.</summary>
        public float ModeFactor;
        /// <summary>★ v0.3.18 (AA-3): where the attack is made from (a move's end), for the AC a mode adds there.</summary>
        public Vector3? ModeAt;
        /// <summary>★ v0.3.18 (AA-3): the move's risk, where it ends, and the option's value before it (Mobility).</summary>
        internal TacticalEngine.Disengage Risk;
        public Vector3? RiskAt;
        public float PreRiskValue;

        public override string ToString() => $"{Label}={Value:F1}";
    }

    /// <summary>Per-decision snapshot of the battlefield as seen by one unit.</summary>
    public sealed class TacticalContext
    {
        public UnitEntityData Unit;
        public TurnController Turn;
        public CharacterSettings Settings;
        public ModSettings Global;
        public int Round;

        public bool HasStandard, HasMove, HasSwift, HasFullRound, CanFullAttack, CanFiveFoot;
        public float MoveRange;
        /// <summary>★ v0.3.8: the game allows no more movement this turn (e.g. after a 5-foot step).</summary>
        public bool NoMovementLeft;
        /// <summary>★ v0.3.8: acting in a surprise round: one move OR one standard action, never both.</summary>
        public bool SurpriseRound;
        /// <summary>★ v0.3.10: a 5-foot step was taken this turn: only its remainder may still be walked.</summary>
        public bool StepTaken;
        /// <summary>★ v0.3.10: what is left of the 5-foot step (meters).</summary>
        public float FiveFootRange;
        /// <summary>★ v0.3.16: why no "Leave area" option was offered while the unit stands in a harmful area (diagnostics).</summary>
        public string LeaveNote;
        public float HpPct;
        public List<UnitEntityData> Engagers = new List<UnitEntityData>();

        public List<UnitEntityData> Enemies = new List<UnitEntityData>();
        public List<UnitEntityData> Allies = new List<UnitEntityData>();   // includes Unit
        /// <summary>★ v0.3.11: enemies no path could reach this turn (TurnMemory.Unreachable).</summary>
        public HashSet<string> Unreachable = new HashSet<string>();
        /// <summary>★ v0.3.9: party members down (unconscious, not dead): only heals are aimed at them.</summary>
        public List<UnitEntityData> DownedAllies = new List<UnitEntityData>();
        public float ExpectedRounds;
        /// <summary>★ v0.3.18: the party's damage per round behind <see cref="ExpectedRounds"/> (ValueModel, prior blended with this fight).</summary>
        public float PartyDamagePerRound;
        /// <summary>
        /// ★ v0.3.18: share of the predicted enemy damage the enemies really dealt in this fight (ValueModel.DangerScale);
        /// scales the drop-chance model (IncomingDamage).
        /// </summary>
        public float DangerScale = 1f;
        public float Difficulty;
        public bool HasRangedWeapon;
        /// <summary>★ v0.3.14: what the unit's statuses let the game start (Nauseated, Staggered, CantMove).</summary>
        public ActionPermissions Permissions = ActionPermissions.None;
        /// <summary>★ v0.3.14: the unit's notable conditions, for the decision log (UnitStatus.Describe).</summary>
        public string Status = "";
        /// <summary>★ v0.3.14 (test5): enemies the game lists no weapon attack against (diagnostics).</summary>
        public int NoAttackAgainst;

        public float Distance(UnitEntityData other) => Vector3.Distance(Unit.Position, other.Position);
        /// <summary>★ v0.3.17: set by ScoreAbility - the aimed enemy itself got some of the value (not only others in the area).</summary>
        public bool AimCounted;
        /// <summary>★ v0.3.17: approaches planned in this decision (TacticalEngine.PlanApproach), by target and range.</summary>
        internal readonly Dictionary<string, TacticalEngine.Approach> Approaches = new Dictionary<string, TacticalEngine.Approach>();
        /// <summary>★ v0.3.18: provoked attacks priced in this decision (TacticalEngine.ProvokeRisk), by place and chance.</summary>
        internal readonly Dictionary<string, TacticalEngine.Disengage> Provoked = new Dictionary<string, TacticalEngine.Disengage>();
        /// <summary>★ v0.3.18 (AA-3): the unit's attack modes, read once per decision (TacticalEngine.Modes.cs).</summary>
        internal List<TacticalEngine.UnitMode> AttackModes;
    }

    public static partial class TacticalEngine
    {
        private const float MinMainValue = 0.5f;
        private const float MinQuickValue = 2f;
        /// <summary>~25 ft: inside Close range for low-level casters, outside most melee reach.</summary>
        private const float CasterApproachRadius = 8f;

        #region Decide

        public static Candidate Decide(UnitEntityData unit, TurnMemory memory)
        {
            // ★ v0.3.17: how long a decision takes ([Perf] at the end of the fight) - nothing was measured before.
            AbilityProfile.TakeBuiltCount();
            CombatMath.ClearAttackCache();   // ★ v0.3.18: attack routines are read fresh for each decision
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var chosen = Decide(unit, memory, preview: false, out _);
            double ms = watch.Elapsed.TotalMilliseconds;
            FightStats.RecordDecision(unit, ms, chosen?.Label);
            // ★ v0.3.17: two decisions of the 0.3.17-test2 log took 0.8-0.9 s (median 3 ms); say which, when it happens.
            if (ms > 250d)
                Main.Log($"[Perf] slow decision: {ms:F0} ms - {unit.CharacterName} R{CombatMemory.Round}: {chosen?.Label ?? "nothing"} " +
                         $"(new ability profiles built this decision: {AbilityProfile.TakeBuiltCount()})");
            return chosen;
        }

        /// <summary>
        /// <paramref name="preview"/>: what the unit would do, without logging the decision (TacticalEngine.ShouldStandUp
        /// asks it as if the unit had just stood up, which spends its move action).
        /// </summary>
        private static Candidate Decide(UnitEntityData unit, TurnMemory memory, bool preview, out TacticalContext built)
        {
            // ★ v0.3.18 (AA-3): Mobility the AI switched on goes off once no enemy threatens the unit (full speed again),
            // before the context reads the unit's movement.
            if (!preview)
            {
                try { MobilityHousekeeping(unit); } catch (Exception ex) { Main.Verbose($"[Modes] {unit.CharacterName}: {ex.Message}"); }
            }
            var ctx = BuildContext(unit);
            built = ctx;
            if (ctx != null && preview)
            {
                ctx.HasMove = false;
                ctx.HasFullRound = ctx.CanFullAttack = ctx.CanFiveFoot = false;
                ctx.MoveRange = 0f;
            }
            if (ctx != null) ctx.Unreachable = memory.Unreachable;
            if (ctx != null && !preview)
            {
                FightStats.NoteContext(ctx);
                CoverageReport.ReportIfChanged(unit);   // ★ v0.3.17: diagnostics, once per fight at most
                // ★ v0.3.18: what the enemies can deal to the party this round, as predicted (uncalibrated), once per round:
                // the next rounds compare it with what they really dealt (ValueModel.DangerScale).
                if (ctx.Enemies.Count > 0 && FightStats.NeedsPrediction(ctx.Round))
                {
                    try { FightStats.NotePrediction(ctx.Round, ctx.Allies.Sum(a => IncomingDamageRaw(ctx, a, a.Position))); }
                    catch (Exception ex) { Main.Verbose($"[Calib] prediction: {ex.Message}"); }
                }
            }
            if (ctx == null || ctx.Enemies.Count == 0)
            {
                if (preview) return null;
                if (CombatMemory.IsRealTime) Main.Verbose($"[Tactics] {unit.CharacterName}: no reachable enemies - wait");
                else Main.Log($"[Tactics] {unit.CharacterName}: no reachable enemies - end turn");
                return null;
            }

            // ★ v0.3.15: judge the last ability this unit used before choosing again (did the game do anything with it?).
            if (!preview) CastOutcome.Settle(unit);

            var candidates = new List<Candidate>();
            try { AddWeaponCandidates(ctx, candidates); } catch (Exception ex) { Main.Error($"[Tactics] weapon candidates: {ex}"); }
            // ★ v0.3.18 (AA-3): each weapon option with its best attack modes.
            try { foreach (var c in candidates.Where(c => c.Kind == CandidateKind.WeaponAttack).ToList()) ChooseAttackModes(ctx, c); }
            catch (Exception ex) { Main.Error($"[Tactics] attack modes: {ex}"); }
            try { AddAbilityCandidates(ctx, candidates); } catch (Exception ex) { Main.Error($"[Tactics] ability candidates: {ex}"); }
            try { AddMovementCandidates(ctx, candidates); } catch (Exception ex) { Main.Error($"[Tactics] movement candidates: {ex}"); }
            // ★ v0.3.18 (AA-2): after the others, so a toggle that forbids spells is priced against the best spell.
            try { AddToggleCandidates(ctx, candidates, preview); } catch (Exception ex) { Main.Error($"[Tactics] toggle candidates: {ex}"); }
            // ★ v0.3.18 (AA-3): Mobility before a move that would provoke.
            try { AddMobilityCandidate(ctx, candidates); } catch (Exception ex) { Main.Error($"[Tactics] mobility: {ex}"); }
            try { PriceHeldCharge(ctx, candidates); } catch (Exception ex) { Main.Error($"[Tactics] held charge: {ex.Message}"); }

            var usable = candidates
                .Where(c => c.Value > 0f && Affordable(ctx, c) && !memory.Failed.Contains(c.Key) && !SilentlyFailing(ctx, c))
                .OrderByDescending(c => c.Value)
                .ToList();

            // ★ v0.3.7: Progressive spell policy - hold back higher spell levels while a lower one can do the job.
            if (ctx.Settings.SpellPolicy == SpellResourcePolicy.Progressive)
            {
                try { usable = ApplyProgressive(ctx, usable); }
                catch (Exception ex) { Main.Error($"[Progressive] {unit.CharacterName}: {ex.Message} - policy skipped"); }
            }

            var beforeMarks = usable;
            try { usable = CoupleOwnAttackMarks(ctx, usable, memory); }
            catch (Exception ex) { Main.Error($"[Tactics] {unit.CharacterName} marks: {ex.Message}"); }

            var quick = usable.FirstOrDefault(c => c.Slot == ActionSlot.Free || c.Slot == ActionSlot.Swift);
            var main = usable.FirstOrDefault(c => c.Slot != ActionSlot.Free && c.Slot != ActionSlot.Swift);

            Candidate chosen = null;
            if (quick != null && quick.Value >= MinQuickValue) chosen = quick;
            else if (main != null && main.Value >= MinMainValue) chosen = main;

            if (preview) return chosen;
            LogDecision(ctx, usable, chosen);
            // ★ v0.3.16 (test2): options exist but none is worth anything or affordable - name the best of them. Seelah
            // ended round 1 with all her actions and 4 enemies, and the log could not say why.
            // ★ v0.3.16: also on movement-only turns (nauseated), with why "Leave area" was not offered: Seelah stayed
            // nauseated next to the Corrupted Mongrels for three turns and the log could not say why.
            if (chosen == null && usable.Count == 0 && (candidates.Count > 0 || ctx.LeaveNote != null) &&
                (ctx.HasStandard || ctx.HasMove) && !CombatMemory.IsRealTime)
                Main.Log($"[Tactics] {unit.CharacterName}: nothing worth doing" +
                         (candidates.Count > 0
                             ? $" - best of {candidates.Count} options: " +
                               string.Join(" | ", candidates.OrderByDescending(c => c.Value).Take(4).Select(c =>
                                   $"{c.Label}={c.Value:F1}" + (!Affordable(ctx, c) ? " (not affordable)" :
                                                               memory.Failed.Contains(c.Key) ? " (failed this turn)" :
                                                               SilentlyFailing(ctx, c) ? " (did nothing before)" :
                                                               // ★ v0.3.17: Smite Evil listed as the best option of
                                                               // eleven idle turns - kept only with an attack after it.
                                                               beforeMarks.Contains(c) ? " (helps own attacks only: no attack this turn)" : "")))
                             : "") +
                         (ctx.LeaveNote != null ? $"; {ctx.LeaveNote}" : ""));
            // ★ v0.3.14 (test5): a turn-based unit with its actions unused and not a single option: say why.
            if (candidates.Count == 0 && ctx.HasStandard && !CombatMemory.IsRealTime)
                Main.Log($"[Tactics] {unit.CharacterName}: no option at all - the game lists no weapon attack against " +
                         $"{ctx.NoAttackAgainst} of {ctx.Enemies.Count} enemies, no ability or move was usable" +
                         (ctx.Permissions.Notes.Length > 0 ? $" ({ctx.Permissions.Notes})" : ""));
            return chosen;
        }

        /// <summary>
        /// ★ v0.3.15: casting another spell drops a held touch charge (TouchSpellsController: any spell or spell-like
        /// ability not from an item removes it). Those options pay for the charge they throw away: the best value the
        /// charge has now. Camellia held an Inflict Light Wounds charge and cast Divine Zap over it.
        /// </summary>
        private static void PriceHeldCharge(TacticalContext ctx, List<Candidate> candidates)
        {
            if (CastOutcome.HeldChargeOf(ctx.Unit) == null) return;
            var charges = candidates.Where(c => c.Kind == CandidateKind.Ability && c.Ability?.StickyTouch != null).ToList();
            if (charges.Count == 0) return;
            float held = charges.Max(c => c.Value);
            if (held <= 0f) return;
            foreach (var c in candidates)
            {
                if (c.Kind != CandidateKind.Ability || c.Ability == null || c.Ability.StickyTouch != null || c.Ability.SourceItem != null) continue;
                var type = c.Ability.Blueprint.Type;
                if (type != AbilityType.Spell && type != AbilityType.SpellLike) continue;
                c.Value -= held;
                c.Label += " [drops held touch]";
            }
        }

        /// <summary>
        /// ★ v0.3.15: an ability the game silently did nothing with twice on that target this fight (CastOutcome), or a
        /// new cast of the touch spell whose charge the unit already holds: that cast would replace the charge and spend
        /// another slot, scroll or potion, while delivering the held one is free. Camellia drank a second Cure Moderate
        /// Wounds potion while still holding the first one's charge.
        /// </summary>
        private static bool SilentlyFailing(TacticalContext ctx, Candidate c)
        {
            if (c.Kind != CandidateKind.Ability || c.Ability == null) return false;
            return CastOutcome.IsBlocked(ctx.Unit, c.Ability, c.Target) || CastOutcome.WouldReplaceHeldCharge(ctx.Unit, c.Ability, c.Target);
        }

        /// <summary>
        /// ★ v0.3.8: a mark that only helps the caster's own attacks (Smite Evil) is worth something this turn only on the
        /// enemy the caster is about to attack. The swift mark is decided before the attack, so it used to go to the most
        /// threatening evil enemy while the attack went elsewhere: Seelah smote one enemy and attacked another in four of
        /// six fights. Marks are kept only on the best main action's enemy (re-aimed there when possible) and dropped
        /// when the best main action attacks no one.
        /// </summary>
        private static List<Candidate> CoupleOwnAttackMarks(TacticalContext ctx, List<Candidate> usable, TurnMemory memory)
        {
            bool IsMark(Candidate c) => c.Kind == CandidateKind.Ability && c.Target != null && ctx.Unit.IsEnemy(c.Target) &&
                                        (AbilityProfile.Get(c.Ability)?.MarksForOwnAttacks ?? false);
            if (!usable.Any(IsMark)) return usable;

            var main = usable.FirstOrDefault(c => c.Slot != ActionSlot.Free && c.Slot != ActionSlot.Swift && !IsMark(c));
            bool attacks = main != null && main.Target != null && ctx.Unit.IsEnemy(main.Target) &&
                           (main.Kind == CandidateKind.WeaponAttack || main.Kind == CandidateKind.Move ||
                            (main.Kind == CandidateKind.Ability && (AbilityProfile.Get(main.Ability)?.IsCharge ?? false)));
            var focus = attacks ? main.Target : null;

            var result = new List<Candidate>(usable.Count);
            var reaimed = new HashSet<string>();
            foreach (var c in usable)
            {
                if (!IsMark(c)) { result.Add(c); continue; }
                if (focus == null) continue;
                if (c.Target == focus) { if (reaimed.Add(CombatMemory.AbilityKey(c.Ability))) result.Add(c); continue; }
                if (!reaimed.Add(CombatMemory.AbilityKey(c.Ability))) continue;
                var alt = EvaluateAbility(ctx, c.Ability, focus).FirstOrDefault();
                if (alt != null && alt.Value > 0f && Affordable(ctx, alt) && !memory.Failed.Contains(alt.Key))
                    result.Add(alt);
            }
            return result.Distinct().OrderByDescending(c => c.Value).ToList();
        }

        /// <summary>
        /// ★ v0.3.7: Progressive spell-resource policy (docs/PROGRESSIVE_SPELLS.md), applied to the already-eligible
        /// options: legality, targeting, immunity, action economy and repeat prevention have all run.
        /// </summary>
        private static List<Candidate> ApplyProgressive(TacticalContext ctx, List<Candidate> usable)
        {
            ProgressiveGate.LogShadowSignals(ctx);
            var items = usable.Select(c => new GateItem
            {
                Source = c,
                Label = c.Label,
                Ability = c.Kind == CandidateKind.Ability ? c.Ability : null,
                Offensive = c.Offensive,
                Value = c.Value,
                Threshold = c.Slot == ActionSlot.Free || c.Slot == ActionSlot.Swift ? MinQuickValue : MinMainValue,
                SuccessChance = c.SuccessChance,
                Target = c.Target,
                Point = c.Point
            }).ToList();
            var held = ProgressiveGate.HeldBack(ctx.Unit, ctx.Enemies, items, "TB", enemiesComplete: true);
            return held.Count == 0 ? usable : usable.Where(c => !held.Contains(c)).ToList();
        }

        private static bool Affordable(TacticalContext ctx, Candidate c)
        {
            // ★ v0.3.14: options the unit's statuses keep the game from starting (ActionRules): while nauseated only
            // plain movement starts, a staggered unit cannot walk and act, a webbed or held one cannot walk at all.
            // Orvath queued Gather Power while nauseated and stood 26 s in the Stinking Cloud waiting for it to start.
            if (ActionRules.Refusal(ctx.Permissions, plainMove: c.Kind == CandidateKind.Move,
                                    walks: c.Kind == CandidateKind.Move || c.NeedsMove,
                                    mainAction: c.Slot == ActionSlot.Standard || c.Slot == ActionSlot.FullRound) != null)
                return false;
            switch (c.Slot)
            {
                case ActionSlot.Free: return true;
                case ActionSlot.Swift: return ctx.HasSwift;
                case ActionSlot.Move: return ctx.HasMove || ctx.HasStandard; // a standard action can be spent as a move
                // ★ v0.3.15 (test3): after a 5-foot step the game allows no other movement (the dispatcher refuses it):
                // Orvath stepped out of melee and then had Move+Attack and Fire Blast (needing 1.5 m) refused.
                case ActionSlot.Standard: return ctx.HasStandard && (!c.NeedsMove || (ctx.HasMove && !ctx.SurpriseRound && !ctx.NoMovementLeft && !ctx.StepTaken));
                case ActionSlot.FullRound: return c.Kind == CandidateKind.WeaponAttack ? ctx.CanFullAttack : ctx.HasFullRound;
                default: return false;
            }
        }

        private static void LogDecision(TacticalContext ctx, List<Candidate> usable, Candidate chosen)
        {
            var sb = new StringBuilder();
            // ★ v0.3.14: real-time decisions carry "RT" (rounds are six-second spans there).
            bool realTime = CombatMemory.IsRealTime;
            sb.Append($"[Tactics] {ctx.Unit.CharacterName} {(realTime ? "RT" : "R")}{ctx.Round} HP{ctx.HpPct * 100:F0}% ");
            // ★ v0.3.14: the unit's statuses and what they forbid ("{nauseated | nauseated: move only}").
            if (ctx.Status.Length > 0 || ctx.Permissions.Notes.Length > 0)
                sb.Append($"{{{ctx.Status}{(ctx.Permissions.Notes.Length > 0 ? " | " + ctx.Permissions.Notes : "")}}} ");
            sb.Append($"[{(ctx.HasStandard ? "S" : "-")}{(ctx.HasMove ? "M" : "-")}{(ctx.HasSwift ? "W" : "-")}{(ctx.CanFullAttack ? "F" : "-")}{(ctx.CanFiveFoot ? "5" : "-")}]{(ctx.SurpriseRound ? " surprise" : "")}{(ctx.NoMovementLeft ? " no-move" : ctx.StepTaken ? " stepped" : "")} ");
            sb.Append($"enemies={ctx.Enemies.Count} rounds~{ctx.ExpectedRounds:F1} diff={ctx.Difficulty:F2} :: ");
            sb.Append(string.Join(" | ", usable.Take(5).Select(c => c.ToString())));
            // ★ v0.3.17: the chosen option's value, and its rank when it is not among the five listed (a quick action
            // ranked lower is taken first: 218 such choices looked like unlisted ones in the audit).
            int rank = chosen != null ? usable.IndexOf(chosen) + 1 : 0;
            sb.Append(chosen != null
                ? $" => {chosen.Label} ={chosen.Value:F1}{(rank > 5 ? $" (#{rank})" : "")}"
                : realTime ? " => wait" : " => END TURN");
            // Real time decides again every half second while waiting for a cooldown: those waits only in verbose logs.
            if (realTime && chosen == null) Main.Verbose(sb.ToString());
            else Main.Log(sb.ToString());
        }

        #endregion

        #region Context

        private static TacticalContext BuildContext(UnitEntityData unit)
        {
            var ctx = new TacticalContext
            {
                Unit = unit,
                Turn = Game.Instance?.TurnBasedCombatController?.CurrentTurn,
                Global = ModSettings.Instance,
                Settings = ModSettings.Instance?.GetOrCreateSettings(unit.UniqueId, unit.CharacterName) ?? new CharacterSettings(),
                Round = CombatMemory.Round,
                HpPct = CombatMath.HpPercent(unit)
            };

            ctx.HasStandard = unit.HasStandardAction();
            ctx.HasMove = unit.HasMoveAction();
            ctx.HasSwift = unit.HasSwiftAction();
            ctx.HasFullRound = unit.HasFullRoundAction();
            ctx.CanFullAttack = ctx.HasFullRound && !unit.UsedOneMoveAction() && !unit.IsMoveActionRestricted() &&
                                !unit.Descriptor.State.HasCondition(UnitCondition.Staggered);
            ctx.Permissions = UnitStatus.Permissions(unit, turnBased: !CombatMemory.IsRealTime);
            ctx.Status = UnitStatus.Describe(unit);

            if (ctx.Turn != null && ctx.Turn.Rider == unit)
            {
                ctx.CanFiveFoot = ctx.Turn.HasFiveFootStep(unit);
                ctx.FiveFootRange = ctx.CanFiveFoot ? Math.Max(0f, ctx.Turn.GetRemainingFiveFootStepRange(unit)) : 0f;
                ctx.MoveRange = ctx.HasMove ? ctx.Turn.GetRemainingMovementTime(unit, false, true) * unit.CurrentSpeedMps : 0f;
                // ★ v0.3.8: the same test the dispatcher (and native AiAction.ScorePath) applies. A 5-foot step forbids
                // any other movement this turn, yet "Advance" was still offered and then rejected (Seelah, twice).
                ctx.NoMovementLeft = ctx.Turn.GetRemainingMovementRange(unit, true, false) <= 0f;
                // ★ v0.3.8: in a surprise round a move uses the standard action's budget (ActionsState.UpdateMoveDistance),
                // so "move + attack" cannot happen: Albedo walked 8 m in round 0 and could not attack.
                try { ctx.SurpriseRound = unit.IsSurprising(); } catch { }
                // ★ v0.3.10: after a 5-foot step the game forbids any other movement this turn
                // (TurnController.ShouldRestrictNormalMovement); GetRemainingMovementRange only says so outside
                // five-foot-step mode. Lann stepped out of melee and lost the rest of his turn to moves the game refused.
                if (ctx.Turn.GetMetersMovedByFiveFootStep(unit) > 0f)
                {
                    ctx.StepTaken = true;
                    ctx.MoveRange = Math.Min(ctx.MoveRange, ctx.FiveFootRange);
                    if (ctx.FiveFootRange < 0.1f) ctx.NoMovementLeft = true;
                }
            }
            else if (CombatMemory.IsRealTime)
            {
                // ★ v0.3.14: real time. Walking is free there (UnitMoveTo ignores cooldowns; an attack or spell walks to its
                // target by itself), so moving is limited only by being able to move; the horizon is one round of it.
                // The game's move cooldown after a standard action would otherwise forbid Move+Attack for 3 seconds.
                ctx.HasMove = unit.Descriptor.State.CanMove;
                ctx.MoveRange = ctx.HasMove ? unit.CurrentSpeedMps * 6f : 0f;
            }
            else
            {
                ctx.MoveRange = ctx.HasMove ? unit.CurrentSpeedMps * 3f : 0f;
            }

            try { ctx.Engagers.AddRange(unit.CombatState.EngagedBy.Where(e => e != null && e.Descriptor.State.IsConscious)); } catch { }

            foreach (var other in Game.Instance.State.Units)
            {
                if (other == null || other.Descriptor?.State == null) continue;
                if (other.Descriptor.State.IsDead) continue;
                // ★ v0.3.14: by life state: a unit that stays conscious at 0 HP or less (Diehard, ferocity) still fights
                // and is still a target; it was dropped from every list, the acting unit included.
                if (!other.Descriptor.State.IsConscious)
                {
                    // ★ v0.3.9: Lann and Camellia went down in the Large Water Elemental fight; nobody could aim a heal at
                    // them because downed allies were not in any list.
                    // ★ v0.3.11: only companions and pets go unconscious; a summon at 0 HP is gone (Camellia healed a
                    // dropped Dretch at 37 % HP instead of herself, then went down).
                    if (other != unit && !unit.IsEnemy(other) && IsRevivable(other) &&
                        Vector3.Distance(unit.Position, other.Position) < 30f)
                        ctx.DownedAllies.Add(other);
                    continue;
                }
                if (!(other.CombatState?.IsInCombat ?? false)) continue;

                if (unit.IsEnemy(other))
                {
                    if (other.Descriptor.State.IsUnconscious) continue;
                    if (UnitCommand.CommandTargetUntargetable(unit, other)) continue;
                    if (other.InStealthFor(unit.Group)) continue;
                    ctx.Enemies.Add(other);
                }
                else if (other.IsPlayerFaction || other == unit)
                {
                    ctx.Allies.Add(other);
                }
            }
            if (!ctx.Allies.Contains(unit)) ctx.Allies.Add(unit);

            float enemyHp = ctx.Enemies.Sum(e => (float)e.HPLeft);
            float enemyThreat = ctx.Enemies.Sum(CombatMath.Threat);
            float partyPower = ctx.Allies.Sum(a => ValueModel.PerRound(Math.Max(1, a.Descriptor?.Progression?.CharacterLevel ?? 1)));
            float partyLevels = ctx.Allies.Sum(a => (float)Math.Max(1, a.Descriptor?.Progression?.CharacterLevel ?? 1));
            // ★ v0.3.18 (B2): the party's damage per round from its levels (1.5 per level, measured in the 0.3.17 test logs;
            // it was 4, and fights lasted about twice the expected rounds), moving towards what it really dealt in the
            // completed rounds of this fight. Enemies that join mid-fight add their HP here as they appear.
            FightStats.PartyObserved(ctx.Round, out float partyDealt, out int roundsSeen);
            ctx.PartyDamagePerRound = ValueModel.PartyDamagePerRound(partyLevels, partyDealt, roundsSeen);
            ctx.ExpectedRounds = ValueModel.ExpectedRounds(enemyHp, ctx.PartyDamagePerRound);
            ctx.Difficulty = Mathf.Clamp(enemyThreat / Math.Max(1f, partyPower), 0.1f, 2f);
            // ★ v0.3.18 (N5): the enemies' real damage to the party against the engine's prediction for the same rounds.
            FightStats.DangerObserved(ctx.Round, out float enemyDealt, out float predicted, out int roundsPredicted);
            ctx.DangerScale = ValueModel.DangerScale(enemyDealt, predicted, roundsPredicted);
            FightStats.NoteCalibration(ctx.PartyDamagePerRound, ctx.DangerScale);

            var weapon = unit.Body?.PrimaryHand?.MaybeWeapon;
            ctx.HasRangedWeapon = weapon?.Blueprint?.IsRanged ?? false;
            return ctx;
        }

        #endregion

        #region Shared value helpers

        /// <summary>
        /// ★ v0.3.9: damage that does not kill a sleeping enemy wakes it (the Sleep buff ends on damage) and throws away
        /// the control already paid for: worth half. Seelah and Camellia hit sleeping enemies instead of awake ones.
        /// </summary>
        private static float WakeFactor(UnitEntityData enemy, float expectedDamage)
        {
            bool asleep;
            try { asleep = enemy.Descriptor.State.HasCondition(UnitCondition.Sleeping); } catch { asleep = false; }
            return asleep && expectedDamage < enemy.HPLeft ? 0.5f : 1f;
        }

        /// <summary>Vanilla Coup de Grace (Tabletop Tweaks keeps the blueprint and replaces its effect component).</summary>
        private static readonly string CoupDeGraceGuid = "32280b137ca642c45be17e2d92898758";

        internal static bool IsCoupDeGrace(AbilityData ability) =>
            string.Equals(ability?.Blueprint?.AssetGuid.ToString(), CoupDeGraceGuid, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// ★ v0.3.9: Coup de Grace was "utility 1.0" (its effect is a custom component); Seelah hit a sleeping wizard for
        /// 9.6 instead. Valued by the rules (CombatMath.EstimateCoupDeGrace): the kill chance takes the whole enemy out,
        /// otherwise the critical damage counts. The game's own target check already requires a helpless target. It is
        /// a full-round action that provokes: other enemies threatening the caster cost their attacks of opportunity.
        /// </summary>
        private static float CoupDeGraceValue(TacticalContext ctx, AbilityData ability, UnitEntityData target, out string why)
        {
            why = "dmg:cdg";   // "dmg" marks it offensive, like other damaging options
            if (!ctx.Unit.IsEnemy(target)) return 0f;
            if (!CombatMath.EstimateCoupDeGrace(ctx.Unit, target, out float damage, out float kill)) return 0f;
            float value = kill * (Math.Max(1, target.HPLeft) + CombatMath.Threat(target)) + (1f - kill) * DamageValue(target, damage, 0f);
            value *= RoleWeight(ctx, "damage");
            // ★ v0.3.18: priced like every other provoking action (ProvokeRisk), when it provokes: Tabletop Tweaks' version
            // makes the caster provoke (AbilityProfile.CasterProvokes), the vanilla one does not. The target is helpless.
            bool provokes = (AbilityProfile.Get(ability)?.CasterProvokes ?? false) || ability.ProvokeAttackOfOpportunity;
            var provoke = provokes ? ProvokeRisk(ctx, ctx.Unit.Position, 1f, spoiledByHit: 0f) : null;
            if (provoke != null) value -= provoke.Cost;
            return Math.Max(0f, value);
        }

        /// <summary>
        /// ★ v0.3.9: damage an ally can expect before its next turn: each enemy that can reach it (a move and an attack,
        /// or any distance for ranged weapons and casters) adds its threat, shared among the party members it could
        /// equally reach. The chance of dropping is then modelled as exp(-HP / danger).
        /// </summary>
        private static float IncomingDamage(TacticalContext ctx, UnitEntityData ally) => IncomingDamage(ctx, ally, ally.Position);

        /// <summary>
        /// ★ v0.3.10: the same, with the ally standing at <paramref name="at"/> (a withdraw destination).
        /// ★ v0.3.18 (N5): scaled by what the enemies really dealt in this fight against this prediction
        /// (<see cref="TacticalContext.DangerScale"/>): in the 0.3.17 test logs the enemies' Threat was 16-82 per round
        /// and they dealt 0-13.
        /// </summary>
        private static float IncomingDamage(TacticalContext ctx, UnitEntityData ally, Vector3 at) =>
            IncomingDamageRaw(ctx, ally, at) * ctx.DangerScale;

        /// <summary>★ v0.3.18: the prediction itself, from the enemies' Threat (what FightStats compares with the damage dealt).</summary>
        private static float IncomingDamageRaw(TacticalContext ctx, UnitEntityData ally, Vector3 at)
        {
            float total = 0f;
            foreach (var enemy in ctx.Enemies)
            {
                float zone;
                try
                {
                    bool ranged = (enemy.Body?.PrimaryHand?.MaybeWeapon?.Blueprint?.IsRanged ?? false) ||
                                  (enemy.Descriptor?.Spellbooks?.Any() ?? false);
                    zone = ranged ? 20f : enemy.CurrentSpeedMps * 3f + 2f + enemy.Corpulence;
                }
                catch
                {
                    zone = 11f;
                }
                if (Vector3.Distance(enemy.Position, at) > zone + ally.Corpulence) continue;
                int sharedBy = Math.Max(1, ctx.Allies.Count(a => Vector3.Distance(enemy.Position, a == ally ? at : a.Position) <= zone + a.Corpulence));
                total += CombatMath.Threat(enemy) / sharedBy;
            }
            return total;
        }

        /// <summary>★ v0.3.13: what leaving melee to reach <c>destination</c> would cost (see <see cref="DisengageRisk"/>).
        /// ★ v0.3.14: plus walking into a harmful area (see <see cref="MoveRisk"/>).</summary>
        internal sealed class Disengage
        {
            public int Attacks;
            /// <summary>Chance at least one attack of opportunity hits; a hit stops the move and its action.</summary>
            public float StopChance;
            /// <summary>★ v0.3.14 (test5): chance the attacks drop the unit on the way.</summary>
            public float DropChance;
            /// <summary>Damage-equivalent cost of the expected damage (higher chance to drop, HP to heal later).</summary>
            public float Cost;
            /// <summary>★ v0.3.14: the harmful area the move enters, if any.</summary>
            public string Area;
            /// <summary>★ test5: the destination is more dangerous for a badly hurt unit.</summary>
            public bool Exposed;
            public Disengage Copy() => (Disengage)MemberwiseClone();
            public string Tag => (Attacks > 0 ? $" [AoO {Attacks}{(DropChance >= 0.1f ? $", {DropChance:P0} down" : "")}]" : "") +
                                 (Area != null ? $" [into {Area}]" : "") + (Exposed ? " [exposed]" : "");
        }

        /// <summary>
        /// ★ v0.3.14: everything a move to <paramref name="destination"/> risks: attacks of opportunity
        /// (<see cref="DisengageRisk"/>) and entering a harmful area on the way or at the end (Entangle, Grease, a fire
        /// cloud; the party's own ones too when they can affect allies, see SelectiveRule). Seelah walked into Orvath's
        /// Entangle to attack an enemy inside and was entangled. Priced like a share of the unit's worth for the next
        /// rounds: the area's danger level (0.4 movement-impairing, 0.6 damaging, more for death or paralysis effects)
        /// times half of it. Null when the move risks nothing.
        /// </summary>
        private static Disengage MoveRisk(TacticalContext ctx, Vector3 destination, bool alwaysExposure = false)
        {
            var risk = DisengageRisk(ctx, destination);
            var unit = ctx.Unit;
            float danger = AoeDangerAnalyzer.EnteringDanger(unit, unit.Position, destination, out string area,
                                                            out float roundsLeft, out bool endsInside, out float areaStop);
            if (danger > 0f)
            {
                // ★ v0.3.17: an area that impedes movement can stop the walk on the way in (a failed save: fallen or
                // held), like an attack of opportunity that hits - whatever the move was for does not happen.
                if (areaStop > 0f)
                {
                    risk = risk ?? new Disengage();
                    risk.StopChance = 1f - (1f - risk.StopChance) * (1f - areaStop);
                }
                // ★ v0.3.15: the danger level is the share of a round the area takes from the unit each round it is
                // affected (0.4 for Grease's fall, more for nausea or damage), times the chance it fails the save that
                // round. Passing through risks one round; stopping inside to fight risks every round it stays, until the
                // fight or the area ends. Melee fighters charged into Grease to reach enemies lying in it and fell in
                // too (34 moves "into Grease" in one log): the cost was half a fight's worth × one round's chance.
                float perRound = AllyWorth(ctx, unit) / ValueModel.Horizon(ctx.ExpectedRounds, 3f);
                float rounds = endsInside ? Mathf.Clamp(Math.Min(roundsLeft, ctx.ExpectedRounds), 1f, 3f) : 1f;
                risk = risk ?? new Disengage();
                risk.Area = area ?? "area";
                // Inside, a fall is worse than its share of a round: standing up provokes from every enemy next to the
                // unit, and they hit it at +4 while it lies there.
                risk.Cost += danger * perRound * rounds * (endsInside ? 2f : 1f);
            }

            // ★ v0.3.14 (test5): badly hurt, walking to where more enemies can hit it before its next turn: the higher chance
            // to drop there (the withdraw model in reverse). Aeiluvetar at 20 % HP took "Move+Attack" (15.2) into the
            // middle of the fight. Only below half HP, as for withdrawing: a healthy fighter's place is in melee.
            // ★ v0.3.16: a caster walking up to touch an enemy pays it at any HP - it stands among them afterwards (the
            // weapon fighter's place is in melee, so its moves keep the half-HP rule).
            if (alwaysExposure || ctx.HpPct < CautionBelow(ctx))
            {
                float hp = Math.Max(1f, unit.HPLeft);
                float exposure = RiskMath.RoundDropChance(hp, IncomingDamage(ctx, unit, destination)) -
                                 RiskMath.RoundDropChance(hp, IncomingDamage(ctx, unit, unit.Position));
                if (exposure > 0.02f)
                {
                    risk = risk ?? new Disengage();
                    risk.Exposed = true;
                    risk.Cost += exposure * AllyWorth(ctx, unit) * RiskTolerance(ctx);
                }
            }
            return risk;
        }

        /// <summary>
        /// ★ v0.3.13: the attacks of opportunity a move to <paramref name="destination"/> provokes, asked from the game the
        /// way its own path preview does (EngagementSimulator): each enemy engaging the unit whose reach the destination
        /// leaves (<c>IsReach</c>), that would attack on disengage and can make an attack of opportunity now (both with
        /// <c>simulate: true</c>, which has no side effects; a 5-foot step makes the game answer no). Seelah left the
        /// Monitor Lizard (23 % HP) and the Small Earth Elemental for another enemy: both moves were stopped without an
        /// attack and the turn was lost - Move+Attack and Charge did not count attacks of opportunity at all.
        /// Null when nothing would attack.
        /// </summary>
        /// <remarks>★ v0.3.18 (AA-3): with Mobility (<paramref name="mobility"/>; null: whether the unit has it on now) each
        /// attack happens only when the unit fails its Mobility check against that enemy's CMD.</remarks>
        private static Disengage DisengageRisk(TacticalContext ctx, Vector3 destination, bool? mobility = null)
        {
            var unit = ctx.Unit;
            // A move that fits in the 5-foot step is made as one (CommandDispatcher.ApplyTurnBasedMovementLimit).
            if (ctx.CanFiveFoot && Vector3.Distance(unit.Position, destination) <= ctx.FiveFootRange * 0.9f) return null;
            var attackers = PathAttackers(ctx, unit.Position, destination);
            if (attackers.Count == 0) return null;

            float hp = Math.Max(1f, unit.HPLeft);
            bool mobile = mobility ?? UsesMobility(unit);
            PriceAttacks(unit, attackers, hp, out float damage, out float stopChance, out float dropNow,
                         mobile ? (Func<UnitEntityData, float>)(e => 1f - MobilityAvoids(unit, e)) : null);

            // Dropped now: the unit is out for the rest of the fight. Otherwise the same model as healing and withdraw,
            // the chance to drop before the next turn, P = exp(-HP / danger), with the attacks' damage taken first; plus
            // the HP itself (someone has to heal it).
            float worth = AllyWorth(ctx, unit);
            float danger = Math.Max(1f, IncomingDamage(ctx, unit, destination));
            float before = RiskMath.RoundDropChance(hp, danger);
            float after = RiskMath.RoundDropChance(Math.Max(1f, hp - damage), danger);
            return new Disengage
            {
                Attacks = attackers.Count,
                StopChance = stopChance,
                DropChance = dropNow,
                Cost = dropNow * worth + ((1f - dropNow) * Math.Max(0f, after - before) * worth + damage * 0.3f) * RiskTolerance(ctx)
            };
        }

        /// <summary>
        /// ★ v0.3.15: share of a move's risk the unit's role pays. A tank's job is to stand in front and take the hits:
        /// it pays 60 % of the damage and exposure it walks into. Being dropped outright is priced in full for every
        /// role - a tank that goes down protects no one.
        /// </summary>
        private static float RiskTolerance(TacticalContext ctx) => ctx.Settings.Role == AIRole.Tank ? 0.6f : 1f;

        /// <summary>
        /// ★ v0.3.15: HP share below which the unit turns careful (prices exposure when moving, considers withdrawing):
        /// half its HP, a third for a tank, which holds the line longer.
        /// </summary>
        private static float CautionBelow(TacticalContext ctx) => ctx.Settings.Role == AIRole.Tank ? 0.35f : 0.5f;

        /// <summary>
        /// ★ v0.3.14 (test5): one attack of opportunity from each of <paramref name="attackers"/> on a unit with
        /// <paramref name="hp"/> HP: their expected damage, the chance at least one hits (a hit stops a walk) and the
        /// chance they drop it (RiskMath: the chance one blow takes the unit out, not only its average).
        /// </summary>
        /// <remarks>★ v0.3.18: <paramref name="happens"/> - the chance each attack is made at all (Mobility); null: always.</remarks>
        private static void PriceAttacks(UnitEntityData unit, List<UnitEntityData> attackers, float hp,
                                         out float damage, out float hitChance, out float dropChance,
                                         Func<UnitEntityData, float> happens = null)
        {
            damage = 0f;
            float noHit = 1f, noDrop = 1f;
            foreach (var enemy in attackers)
            {
                var est = CombatMath.EstimateWeaponAttack(enemy, unit, full: false);
                float made = happens == null ? 1f : Mathf.Clamp01(happens(enemy));
                float hitIfMade = est?.FirstHitChance ?? 0.5f;
                float expectedIfMade = est?.ExpectedDamage ?? CombatMath.Threat(enemy) * 0.35f;
                float perHit = est != null && est.FirstPerHit > 0f ? est.FirstPerHit : expectedIfMade / Math.Max(0.05f, hitIfMade);
                float hit = hitIfMade * made;
                float expected = expectedIfMade * made;
                noDrop *= 1f - RiskMath.AttackDropChance(hit, (est?.FirstCritChance ?? 0.05f * hitIfMade) * made, perHit,
                                                         est?.CritMultiplier ?? 2f, Math.Max(1f, hp - damage));
                damage += expected;
                noHit *= 1f - hit;
            }
            hitChance = 1f - noHit;
            dropChance = 1f - noDrop;
        }

        /// <summary>
        /// ★ v0.3.14 (test5): a downed ally brought back to <paramref name="hpAfter"/> HP lies there until its turn (enemies
        /// get +4 to hit it in melee), then standing up provokes every enemy that threatens it (unless it has a feat
        /// against that). The chance it is still up when it can act. Seelah was healed to 4-6 HP three times next to an
        /// enemy and dropped again by the attack of opportunity as she stood up. Healed with more, or once the enemy is
        /// dealt with, she stays up.
        /// </summary>
        private static float ReviveStaysUp(TacticalContext ctx, UnitEntityData ally, float hpAfter)
        {
            try
            {
                float roundDrop = RiskMath.RoundDropChance(hpAfter, IncomingDamage(ctx, ally, ally.Position) * 1.25f);
                float standDrop = 0f;
                if (!ally.Descriptor.State.Features.GetUpWithoutAttackOfOpportunity)
                {
                    var threats = ctx.Enemies.Where(e => Threatens(e, ally, ally.Position)).ToList();
                    if (threats.Count > 0) PriceAttacks(ally, threats, hpAfter, out _, out _, out standDrop);
                }
                return (1f - roundDrop) * (1f - standDrop);
            }
            catch
            {
                return 1f;
            }
        }

        /// <summary>
        /// ★ v0.3.14 (test5): turn-based, lying down: stand up now, or stay down this turn? Standing provokes every enemy
        /// that threatens the unit; lying down loses the turn and gives enemies +4 to hit it in melee. Compared on the
        /// engine's scale: the chance to be dropped (the unit's worth) and, if it stands and survives, the best thing it
        /// can do then (asked of the engine as if standing - the same toggle UnitEntityData.IsAbleToAct uses - with the
        /// move action spent on standing). True when standing is at least as good; <paramref name="why"/> says why not.
        /// </summary>
        public static bool ShouldStandUp(UnitEntityData unit, TurnMemory memory, out string why)
        {
            why = null;
            List<UnitEntityData> attackers;
            var state = unit.Descriptor.State;
            try
            {
                if (state.Features.GetUpWithoutAttackOfOpportunity) return true;
                attackers = unit.CombatState.EngagedBy
                    .Where(e => e != null && e.Descriptor.State.IsConscious &&
                                e.CombatState.AttackOfOpportunity(unit, tricksterAttack: false, simulate: true))
                    .ToList();
            }
            catch
            {
                return true;
            }
            if (attackers.Count == 0) return true;

            Candidate best;
            TacticalContext ctx;
            bool wasProne = state.Prone.Active;
            state.Prone.Active = false;
            try
            {
                best = Decide(unit, memory, preview: true, out ctx);
            }
            catch
            {
                return true;
            }
            finally
            {
                state.Prone.Active = wasProne;
                CombatMath.ClearAttackCache();   // ★ v0.3.18: estimates made while "standing" must not be reused
            }
            if (ctx == null) return true;

            float hp = Math.Max(1f, unit.HPLeft);
            PriceAttacks(unit, attackers, hp, out float damage, out _, out float dropNow);
            float worth = AllyWorth(ctx, unit);
            float danger = IncomingDamage(ctx, unit, unit.Position);
            float stayDrop = RiskMath.RoundDropChance(hp, danger * 1.25f);
            float afterDrop = RiskMath.RoundDropChance(Math.Max(1f, hp - damage), danger);
            float act = best?.Value ?? 0f;
            float stand = -dropNow * worth + (1f - dropNow) * (act - afterDrop * worth);
            float stay = -stayDrop * worth;
            if (stand >= stay) return true;
            why = $"standing up provokes {attackers.Count} ({dropNow:P0} to be dropped), the best action after standing is " +
                  $"worth {act:F1} ({best?.Label ?? "nothing"}); lying down: {stayDrop:P0} to be dropped before the next turn";
            return false;
        }

        /// <summary>
        /// ★ v0.3.14 (test5): the enemies that get an attack of opportunity on a walk from <paramref name="from"/> to
        /// <paramref name="to"/>, by the game's rule for movement (Kingmaker.Controllers.Combat.EngagementSimulator, which
        /// drives its own path preview and native AI): stepping along the path, every enemy whose reach the unit enters is
        /// engaged, and each engaged enemy whose reach it then leaves attacks once. Only enemies already engaging the unit
        /// were checked before, and only at the destination: Camellia charged past the Large Water Elemental, priced as
        /// safe, and its one slam dropped her. The walk is taken as a straight line (charges are; other paths may bend).
        /// </summary>
        private static List<UnitEntityData> PathAttackers(TacticalContext ctx, Vector3 from, Vector3 to)
        {
            var unit = ctx.Unit;
            var attackers = new List<UnitEntityData>();
            var engaged = new HashSet<UnitEntityData>(ctx.Engagers);
            float length = Vector3.Distance(from, to);
            int steps = Math.Max(1, Mathf.CeilToInt(length / 0.75f));
            for (int i = 1; i <= steps; i++)
            {
                var point = Vector3.Lerp(from, to, i / (float)steps);
                foreach (var enemy in ctx.Enemies)
                {
                    if (attackers.Contains(enemy)) continue;
                    try
                    {
                        if (Threatens(enemy, unit, point)) { engaged.Add(enemy); continue; }
                        if (!engaged.Remove(enemy)) continue;
                        if (AttacksOnLeaving(enemy, unit)) attackers.Add(enemy);
                    }
                    catch (Exception ex)
                    {
                        Main.Verbose($"[Tactics] attack of opportunity check {enemy?.CharacterName} -> {unit.CharacterName}: {ex.Message}");
                    }
                }
            }
            return attackers;
        }

        /// <summary>EngagementSimulator.EngagedBy: the enemy can act, has a threatening weapon reaching <paramref name="at"/>,
        /// and sees the unit.</summary>
        /// <summary>
        /// ★ v0.3.16: the attacks a provoking cast at <paramref name="castAt"/> risks: the chance the defensive-casting
        /// check fails (the game's rule) times the attacks of every enemy that would engage the caster there, priced like
        /// any attack of opportunity (chance to be dropped, damage). StopChance: the spell is lost (the caster drops, or
        /// a hit spoils it about half the time). Null when nothing would attack.
        /// </summary>
        private static Disengage CastProvokeRisk(TacticalContext ctx, AbilityData ability, Vector3 castAt)
        {
            float fail;
            try
            {
                var p = ability.CalculateParams();
                int dc = 15 + 2 * Math.Max(0, p.SpellLevel);
                fail = Mathf.Clamp01((dc - p.Concentration - 1) / 20f);
            }
            catch
            {
                fail = 0.5f;
            }
            // A hit spoils the spell about half the time (the concentration check for damage while casting).
            return ProvokeRisk(ctx, castAt, fail, spoiledByHit: 0.5f);
        }

        /// <summary>
        /// ★ v0.3.18 (B10): the attacks of opportunity an action at <paramref name="at"/> provokes with
        /// <paramref name="chance"/> (a failed defensive-casting check; 1 for a ranged attack roll): every enemy that
        /// would engage the unit there and can make one, priced like any attack of opportunity (chance to be dropped,
        /// damage). StopChance: the action is lost - the unit drops, or a hit spoils it (<paramref name="spoiledByHit"/>
        /// of the time). The cost is the one DisengageRisk uses for leaving melee: dropped now, or more likely to drop
        /// before its next turn with the damage taken, plus the HP to heal (0.3.16-0.3.17 casting left out the second part).
        /// Null when nothing would attack.
        /// </summary>
        private static Disengage ProvokeRisk(TacticalContext ctx, Vector3 at, float chance, float spoiledByHit)
        {
            if (chance <= 0f) return null;
            // Asked for every target of every ability and weapon from the same spot: priced once per decision.
            string key = $"{at.x:F1}|{at.z:F1}|{chance:F3}|{spoiledByHit:F2}";
            if (ctx.Provoked.TryGetValue(key, out var known)) return known?.Copy();
            var risk = PriceProvoked(ctx, at, chance, spoiledByHit);
            ctx.Provoked[key] = risk;
            return risk?.Copy();
        }

        private static Disengage PriceProvoked(TacticalContext ctx, Vector3 at, float chance, float spoiledByHit)
        {
            var unit = ctx.Unit;
            var attackers = ctx.Enemies.Where(e =>
            {
                try { return Threatens(e, unit, at) && e.CombatState.AttackOfOpportunity(unit, tricksterAttack: false, simulate: true); }
                catch { return false; }
            }).ToList();
            if (attackers.Count == 0) return null;
            float hp = Math.Max(1f, unit.HPLeft);
            PriceAttacks(unit, attackers, hp, out float damage, out float hitChance, out float dropNow);
            float worth = AllyWorth(ctx, unit);
            float danger = Math.Max(1f, IncomingDamage(ctx, unit, at));
            float dropLater = Math.Max(0f, RiskMath.RoundDropChance(Math.Max(1f, hp - damage), danger) - RiskMath.RoundDropChance(hp, danger));
            return new Disengage
            {
                Attacks = attackers.Count,
                DropChance = chance * dropNow,
                StopChance = chance * (dropNow + (hitChance - dropNow) * spoiledByHit),
                Cost = chance * (dropNow * worth + ((1f - dropNow) * dropLater * worth + damage * 0.3f) * RiskTolerance(ctx))
            };
        }

        /// <summary>
        /// ★ v0.3.18 (B10): a ranged attack roll at <paramref name="at"/> provokes from every enemy engaging the unit
        /// there (UnitCombatEngagementController, for each ranged and ranged-touch roll), unless the game's rule says it
        /// does not (Point Blank Master, spell lists that ignore attacks of opportunity, mods: ProvokeObserver). The
        /// attack itself is made first: only the attacks still to come are lost when the unit drops
        /// (<paramref name="attacks"/> rolls in all). 0.3.17 priced shooting in melee as 35 % of the engagers' Threat and
        /// rays not at all (defensive casting removes the casting check, not the roll's).
        /// </summary>
        private static Disengage AttackRollProvokeRisk(TacticalContext ctx, Vector3 at, int attacks)
        {
            var risk = ProvokeRisk(ctx, at, 1f, spoiledByHit: 0f);
            if (risk != null) risk.StopChance = risk.DropChance * Math.Max(0, attacks - 1) / Math.Max(1, attacks);
            return risk;
        }

        private static bool Threatens(UnitEntityData enemy, UnitEntityData unit, Vector3 at)
        {
            if (!enemy.Descriptor.State.CanAct) return false;
            var hand = enemy.GetThreatHand();
            if (hand == null || !enemy.IsReach(unit, hand, at)) return false;
            return Kingmaker.UnitLogic.Parts.UnitPartConcealment.Calculate(enemy, unit) != Kingmaker.Enums.Concealment.Total;
        }

        /// <summary>
        /// The engaged enemy attacks when the unit leaves its reach. ★ v0.3.14 (test5): in real time the game's preview
        /// (ShouldAttackOnDisengage with simulate) skips two rules its real check applies: an enemy engaging only this
        /// unit, or whose last target it is, always attacks. Lann and Aeiluvetar backed off "safely" from a lone enemy
        /// and were dropped by its attack. Turn-based is unchanged: there the preview already answers yes.
        /// </summary>
        /// <remarks>
        /// ★ v0.3.18: in real time the game's check looks up when the enemy engaged the unit, and throws for a reach the
        /// move only walks into on the way (KeyNotFoundException, 500-700 times per real-time log): such attacks of
        /// opportunity were never priced. For an engagement that has just begun the game's rule is "attacks unless it is
        /// moving itself" (UnitCombatState.ShouldAttackOnDisengage: engaged less than 6 s, and not moved since). And the
        /// check now says it is a disengage, so features that let a unit leave without provoking count
        /// (DisengageWithoutAttackOfOpportunity).
        /// </remarks>
        private static bool AttacksOnLeaving(UnitEntityData enemy, UnitEntityData unit)
        {
            var combat = enemy.CombatState;
            bool attacks;
            if (CombatMemory.IsRealTime && !combat.EngagedUnits.Contains(unit))
            {
                bool moving = (Game.Instance.TimeController.GameTime - enemy.LastMoveTime).TotalSeconds < 0.5;
                attacks = (bool)enemy.Descriptor.State.Features.DuelistNoRetreat ||
                          (!moving && Kingmaker.UnitLogic.Parts.UnitPartConcealment.Calculate(enemy, unit) != Kingmaker.Enums.Concealment.Total);
            }
            else
            {
                attacks = combat.ShouldAttackOnDisengage(unit, simulate: true);
                if (!attacks && CombatMemory.IsRealTime)
                    attacks = combat.EngagedUnits.Count() <= 1 || combat.LastTarget == unit;
            }
            return attacks && combat.AttackOfOpportunity(unit, tricksterAttack: false, simulate: true, disengaging: true);
        }

        /// <summary>★ v0.3.13: where a move to act on <paramref name="target"/> from <paramref name="range"/> ends.</summary>
        private static Vector3 ApproachPoint(UnitEntityData unit, UnitEntityData target, float range)
        {
            Vector3 away = unit.Position - target.Position;
            away.y = 0f;
            if (away.sqrMagnitude < 0.0001f) return unit.Position;
            return target.Position + away.normalized * Math.Max(0.1f, Math.Min(away.magnitude, range * 0.9f));
        }

        /// <summary>★ v0.3.17: where a move to act on a target ends, what it risks, and whether it goes around an area.</summary>
        internal sealed class Approach
        {
            public Vector3 Point;
            public Disengage Risk;
            /// <summary>The straight approach entered a harmful area; <see cref="Point"/> is a spot outside it instead.</summary>
            public string Around;
            /// <summary>How close to <see cref="Point"/> the walk must end for the target to stay within reach.</summary>
            public float Arrival = 0.3f;
            /// <summary>★ v0.3.18: the straight approach enters an area and no spot around it was found: why (decision log).</summary>
            public string NoSpot;
        }

        /// <summary>
        /// ★ v0.3.17: the approach to act on <paramref name="target"/> from <paramref name="range"/>. The straight line
        /// first; when it walks into a harmful area, the cheapest spot within reach of the target that does not (two rings
        /// around the target, 16 directions each), on walkable ground the unit can get to, not on another unit. Seelah stood
        /// idle for eleven turns while her enemies lay in Orvath's Grease: every approach was the one straight line into
        /// it (log 06aebf2d). When no such spot exists the straight approach keeps its honest price.
        /// <paramref name="orNearby"/> (advancing, no action follows): failing that, a spot by the area's edge just out of
        /// reach, where the enemy walks into the unit's reach as it comes out.
        /// </summary>
        private static Approach PlanApproach(TacticalContext ctx, UnitEntityData target, float range, bool alwaysExposure = false,
                                             bool orNearby = false)
        {
            var unit = ctx.Unit;
            // Asked per ability and target: the same approach is computed once per decision.
            string key = $"{target.UniqueId}|{range:F1}|{alwaysExposure}|{orNearby}";
            if (ctx.Approaches.TryGetValue(key, out var cached)) return cached;
            var direct = ApproachPoint(unit, target, range);
            var plan = new Approach { Point = direct, Risk = MoveRisk(ctx, direct, alwaysExposure) };
            ctx.Approaches[key] = plan;
            if (plan.Risk?.Area == null || ctx.NoMovementLeft) return plan;
            string directArea = plan.Risk.Area;
            try
            {
                float best = float.MaxValue;
                float body = unit.Corpulence;
                int far = 0, taken = 0, inArea = 0, unwalkable = 0, noCloser = 0;
                var others = ctx.Allies.Concat(ctx.Enemies).Where(u => u != unit && u != target).ToList();
                var rings = new List<float> { Math.Max(0.6f, range * 0.85f), Math.Max(0.6f, range * 0.6f) };
                if (orNearby) rings.AddRange(new[] { range + 1.5f, range + 3f });
                float distanceNow = Vector3.Distance(unit.Position, target.Position);
                foreach (float ring in rings)
                {
                    // ★ v0.3.18 (test2): a spot out of reach is only worth walking to if it is nearer the enemy than the unit
                    // already is. Seelah, a full turn left, stepped 1 m to another spot by the Grease each turn, no
                    // nearer than before (0.3.18-test2 turn-based log, R4-R5), and the turn ended there.
                    if (ring > range && distanceNow <= ring + 0.75f) { noCloser++; continue; }
                    for (int i = 0; i < 16; i++)
                    {
                        float angle = i * 22.5f * Mathf.Deg2Rad;
                        var point = target.Position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * ring;
                        float walk = Vector3.Distance(unit.Position, point);
                        // ★ v0.3.18: an advance (no action follows) may aim beyond this turn's movement: the unit walks
                        // towards the spot, on a path checked to stay out of the area, and goes on next turn.
                        if (walk > ctx.MoveRange * 0.95f && !(orNearby && ctx.HasMove)) { far++; continue; }
                        if (others.Any(u => Vector3.Distance(u.Position, point) < (u.Corpulence + body) * 0.9f)) { taken++; continue; }
                        if (AoeDangerAnalyzer.EnteringDanger(unit, unit.Position, point, out _) > 0f) { inArea++; continue; }
                        if (!AoeDangerAnalyzer.IsPositionValid(unit.Position, point)) { unwalkable++; continue; }
                        var risk = MoveRisk(ctx, point, alwaysExposure);
                        if (risk?.Area != null) { inArea++; continue; }
                        // Cheapest by risk; a shorter walk breaks ties.
                        float cost = (risk?.Cost ?? 0f) + 0.05f * walk;
                        if (cost >= best) continue;
                        best = cost;
                        plan = new Approach
                        {
                            Point = point, Risk = risk, Around = directArea,
                            Arrival = ring < range ? Mathf.Clamp(range - ring, 0.1f, 0.3f) : 0.3f
                        };
                    }
                    if (plan.Around != null) break;
                }
                if (plan.Around == null)
                    plan.NoSpot = $"{inArea} in or through an area, {far} beyond this turn's move, {taken} taken, {unwalkable} not walkable" +
                                  (noCloser > 0 ? $", {noCloser} ring(s) no nearer than now" : "");
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Tactics] approach around an area for {unit.CharacterName}: {ex.Message}");
            }
            ctx.Approaches[key] = plan;
            return plan;
        }

        /// <summary>★ v0.3.9: what keeping an ally in the fight is worth: its damage-equivalent output for up to 3 rounds.</summary>
        /// <remarks>★ v0.3.11: a summon is temporary and replaceable: half (the summoned Cambion's heal outscored Camellia's own).
        /// ★ v0.3.18: the formula is ValueModel.AllyWorth.</remarks>
        private static float AllyWorth(TacticalContext ctx, UnitEntityData ally) =>
            ValueModel.AllyWorth(Level(ally), ctx.ExpectedRounds, IsSummon(ally));

        private static int Level(UnitEntityData unit) => Math.Max(1, unit.Descriptor?.Progression?.CharacterLevel ?? 1);

        private static bool UsesRangedWeapon(UnitEntityData unit)
        {
            try { return unit.Body?.PrimaryHand?.MaybeWeapon?.Blueprint?.IsRanged ?? false; } catch { return false; }
        }

        private static bool IsSummon(UnitEntityData unit)
        {
            try { return unit.Get<Kingmaker.UnitLogic.Parts.UnitPartSummonedMonster>() != null; } catch { return false; }
        }

        /// <summary>★ v0.3.11: goes unconscious at 0 HP instead of dying: a party member or pet, not a summon.</summary>
        private static bool IsRevivable(UnitEntityData unit)
        {
            try
            {
                return !IsSummon(unit) && (Game.Instance?.Player?.PartyAndPets?.Contains(unit) ?? false);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Damage dealt to an enemy, capped at its HP, plus the value of removing it from the fight.
        /// ★ v0.3.18 (B5): the removal by the chance to kill (ValueModel.DamageValue), from <paramref name="landChance"/>,
        /// the chance the damage lands at all (an attack hits, the target fails its save; 0: no kill counted); the
        /// expected damage over it is the damage when it lands.
        /// </summary>
        private static float DamageValue(UnitEntityData enemy, float expectedDamage, float landChance)
        {
            if (expectedDamage <= 0f) return 0f;
            float hp = Math.Max(1, enemy.HPLeft);
            float kill = landChance > 0f ? ValueModel.KillChance(landChance, expectedDamage / landChance, hp) : 0f;
            return ValueModel.DamageValue(expectedDamage, hp, CombatMath.Threat(enemy), kill);
        }

        /// <summary>★ v0.3.18: a weapon attack estimate's damage value (<paramref name="factor"/>: a bonus on the damage, Charge).</summary>
        private static float DamageValue(UnitEntityData enemy, WeaponAttackEstimate est, float factor = 1f) =>
            est == null ? 0f : DamageValue(enemy, est.ExpectedDamage * factor, est.AnyHitChance);

        private static float RoleWeight(TacticalContext ctx, string category)
        {
            switch (ctx.Settings.Role)
            {
                case AIRole.Support:
                    return category == "damage" ? 0.85f : category == "heal" ? 1.6f : category == "buff" ? 1.5f : 1f;
                case AIRole.Tank:
                    return category == "damage" ? 1.0f : category == "buff" ? 0.9f : 1f;
                default: // DPS
                    return category == "damage" ? 1.1f : category == "buff" ? 0.7f : category == "heal" ? 0.9f : 1f;
            }
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        private static bool InRangeNow(UnitEntityData unit, Vector3 point, float radius, bool needLos)
        {
            return UnitCommand.IsUnitCloseEnough(point, unit.Position, unit.EyePosition, radius, needLos, 0f);
        }

        private static List<UnitEntityData> UnitsAround(List<UnitEntityData> units, Vector3 center, float radius)
        {
            return units.Where(u => Vector3.Distance(u.Position, center) <= radius + u.Corpulence).ToList();
        }

        #endregion

        #region Weapon attacks

        /// <summary>The role and preference weight of a weapon attack on <paramref name="enemy"/> (a tank protecting an ally: x1.25).</summary>
        private static float AttackWeight(TacticalContext ctx, UnitEntityData enemy, bool ranged)
        {
            float weight = RoleWeight(ctx, "damage") * PreferenceWeight(ctx, ranged);
            if (ctx.Settings.Role == AIRole.Tank && enemy.CombatState.EngagedUnits.Any(u => u != ctx.Unit && !ctx.Unit.IsEnemy(u)))
                weight *= 1.25f;
            return weight;
        }

        private static void AddWeaponCandidates(TacticalContext ctx, List<Candidate> list)
        {
            var unit = ctx.Unit;
            // ★ v0.3.14: real time lists them during the action cooldown too (not affordable, so never chosen then): the
            // movement step needs to know an attack is possible from here once the cooldown ends.
            if (!ctx.HasStandard && !ctx.CanFullAttack && !CombatMemory.IsRealTime) return;

            ItemEntityWeapon provokeWeapon = null;   // the weapon weaponProvokes was asked for (the same for most enemies)
            bool weaponProvokes = true;
            foreach (var enemy in ctx.Enemies)
            {
                var single = CombatMath.EstimateWeaponAttack(unit, enemy, full: false);
                // ★ v0.3.14 (test5): no attack against this enemy (the game lists none). It used to end the whole loop as
                // "cannot attack at all": Seelah ended two full turns with no option at all against two enemies.
                if (single == null) { ctx.NoAttackAgainst++; continue; }

                float reach = single.Reach;
                bool inReach = InRangeNow(unit, enemy.Position, reach, needLos: true);
                float dist = ctx.Distance(enemy);
                // ★ v0.3.9: ranged weapons too - Lann made single "Move+Attack" shots after moving 0.1-0.7 m (out of range
                // or line of sight by a step) and lost his second attack five times. The dispatcher refuses a full
                // attack whose path is longer than the 5-foot step, and the next best option is then tried.
                bool fiveFootReach = !inReach && ctx.CanFiveFoot &&
                                     dist <= reach + ctx.FiveFootRange * 0.85f;

                float weight = AttackWeight(ctx, enemy, single.IsRanged);

                // ★ v0.3.18 (B10): a ranged attack roll provokes by the game's rule (AttackRollProvokeRisk).
                if (single.IsRanged && single.Weapon != provokeWeapon)
                {
                    provokeWeapon = single.Weapon;
                    weaponProvokes = ProvokeObserver.WeaponProvokes(unit, single.Weapon);
                }
                bool shotProvokes = single.IsRanged && weaponProvokes;

                if (ctx.CanFullAttack && (inReach || fiveFootReach))
                {
                    var full = CombatMath.EstimateWeaponAttack(unit, enemy, full: true);
                    if (full != null && full.Attacks > 1)
                    {
                        float fullValue = DamageValue(enemy, full) * weight * (fiveFootReach ? 0.95f : 1f) * WakeFactor(enemy, full.ExpectedDamage);
                        // After a 5-foot step the unit shoots from about 1.5 m nearer its target; priced where it stands.
                        var provoke = shotProvokes ? AttackRollProvokeRisk(ctx, unit.Position, full.Attacks) : null;
                        if (provoke != null) fullValue = fullValue * (1f - provoke.StopChance) - provoke.Cost;
                        list.Add(new Candidate
                        {
                            Kind = CandidateKind.WeaponAttack,
                            Offensive = true,
                            Slot = ActionSlot.FullRound,
                            Target = enemy,
                            FullAttack = true,
                            Value = fullValue,
                            ModeFactor = weight * (fiveFootReach ? 0.95f : 1f) * WakeFactor(enemy, full.ExpectedDamage) * (1f - (provoke?.StopChance ?? 0f)),
                            Label = $"FullAttack({full.Attacks})->{enemy.CharacterName}{(fiveFootReach ? "+5ft" : "")}{ProvokeTag(provoke)}",
                            Key = $"full:{enemy.UniqueId}"
                        });
                    }
                }

                if (!ctx.HasStandard) continue;

                if (inReach)
                {
                    var provoke = shotProvokes ? AttackRollProvokeRisk(ctx, unit.Position, 1) : null;
                    float singleValue = DamageValue(enemy, single) * weight * WakeFactor(enemy, single.ExpectedDamage);
                    if (provoke != null) singleValue = singleValue * (1f - provoke.StopChance) - provoke.Cost;
                    list.Add(new Candidate
                    {
                        Kind = CandidateKind.WeaponAttack,
                        Offensive = true,
                        Slot = ActionSlot.Standard,
                        Target = enemy,
                        Value = singleValue,
                        ModeFactor = weight * WakeFactor(enemy, single.ExpectedDamage) * (1f - (provoke?.StopChance ?? 0f)),
                        Label = $"Attack->{enemy.CharacterName}{ProvokeTag(provoke)}",
                        Key = $"atk:{enemy.UniqueId}"
                    });
                }
                else if (ctx.HasMove && dist - reach <= ctx.MoveRange * 0.95f)
                {
                    float value = DamageValue(enemy, single) * weight * 0.9f * WakeFactor(enemy, single.ExpectedDamage);
                    float preRisk = value;
                    string label = $"Move+Attack->{enemy.CharacterName}";
                    // ★ v0.3.13: walking out of an enemy's reach provokes (melee and ranged alike).
                    // ★ v0.3.17: around a harmful area when the straight approach walks into it (PlanApproach).
                    var approach = PlanApproach(ctx, enemy, reach);
                    var risk = approach.Risk;
                    if (risk != null)
                    {
                        value = value * (1f - risk.StopChance) - risk.Cost;
                        label += risk.Tag;
                    }
                    // ★ v0.3.18 (B10): shooting from where the move ends, next to an enemy, provokes.
                    var provoke = shotProvokes ? AttackRollProvokeRisk(ctx, approach.Point, 1) : null;
                    if (provoke != null)
                    {
                        value = value * (1f - provoke.StopChance) - provoke.Cost;
                        label += ProvokeTag(provoke);
                    }
                    if (approach.Around != null) label += $" (around {approach.Around})";
                    list.Add(new Candidate
                    {
                        Kind = CandidateKind.WeaponAttack,
                        Offensive = true,
                        Slot = ActionSlot.Standard,
                        NeedsMove = true,
                        Target = enemy,
                        Value = value,
                        ModeFactor = weight * 0.9f * WakeFactor(enemy, single.ExpectedDamage) * (1f - (risk?.StopChance ?? 0f)) *
                                     (1f - (provoke?.StopChance ?? 0f)),
                        ModeAt = approach.Point,
                        Risk = risk,
                        RiskAt = approach.Point,
                        PreRiskValue = preRisk,
                        Label = label,
                        Key = $"matk:{enemy.UniqueId}",
                        MoveFirst = approach.Around != null,
                        MovePoint = approach.Around != null ? approach.Point : (Vector3?)null,
                        ApproachRadius = approach.Arrival,
                        CheckPath = risk?.Area == null
                    });
                }
            }
        }

        /// <summary>★ v0.3.18: the decision-log tag of a provoking ranged attack roll.</summary>
        private static string ProvokeTag(Disengage provoke) =>
            provoke == null ? "" : $" [shot provokes {provoke.Attacks}{(provoke.DropChance >= 0.1f ? $", {provoke.DropChance:P0} down" : "")}]";

        private static float PreferenceWeight(TacticalContext ctx, bool rangedAttack)
        {
            switch (ctx.Settings.RangePreference)
            {
                case RangePreference.Ranged: return rangedAttack ? 1.1f : 0.7f;
                case RangePreference.Melee: return rangedAttack ? 0.8f : 1.1f;
                default: return 1f;
            }
        }

        #endregion

        #region Abilities, spells and items

        private static void AddAbilityCandidates(TacticalContext ctx, List<Candidate> list)
        {
            foreach (var ability in AbilityDiscovery.Gather(ctx.Unit))   // ★ v0.3.14: shared with real time
            {
                try
                {
                    if (!ability.IsAvailable) continue;
                    if (AbilityClassifier.IsBlacklistedForCombat(ability, valuedByEffect: true)) continue;

                    list.AddRange(EvaluateAbility(ctx, ability));
                }
                catch (Exception ex)
                {
                    // ★ v0.3.12: an ability that throws drops out of the decision; say so once without debug mode.
                    ReportOnce($"eval|{ability?.Blueprint?.AssetGuid}", $"[Tactics] {ability?.Name} ({ability?.Blueprint?.AssetGuid}): " +
                               $"not evaluated - {ex.GetType().Name}: {ex.Message}");
                }
            }
        }

        private static void ReportOnce(string key, string message) => AbilityDiscovery.ReportOnce(key, message);

        /// <summary>
        /// ★ v0.3.14: the turn-based buff value (damage-equivalent points) of casting <paramref name="ability"/> on
        /// <paramref name="recipient"/>: what the buff does there (BuffQuality, stacking-aware) × the per-round worth of
        /// its spell level × the rounds it lasts, less for someone not fighting. Real time's buff scoring had no such
        /// scale: Guidance, Virtue and Resistance on Aeiluvetar outscored every attack Orvath had (GM 0.80 vs 0.65).
        /// </summary>
        public static float BuffWorth(AbilityData ability, UnitEntityData caster, UnitEntityData recipient, float expectedRounds = 3f)
        {
            try
            {
                var profile = AbilityProfile.Get(ability);
                if (profile == null || recipient == null || (profile.HarmfulEffects && !profile.HelpfulToAllies)) return 0f;
                var all = profile.TargetBuffs.Count > 0 ? profile.TargetBuffs : profile.CasterBuffs;
                var buffs = all.Where(b => profile.BuffAppliesTo(b, ability, recipient) != EffectApplies.No && !recipient.Buffs.HasFact(b)).ToList();
                if (buffs.Count == 0) return 0f;
                float quality = BuffQuality(ability, profile, buffs, recipient);
                if (quality <= 0f) return 0f;
                float perRound = 1f + Math.Max(0, ability.SpellLevel) * 1.5f;
                float rounds = profile.BuffsLastOneRound ? 1f
                             : profile.BuffsSpentByAttack ? Math.Min(expectedRounds, 2f)
                             : profile.HasLongBuff ? expectedRounds : Math.Min(expectedRounds, 3f);
                bool fighting = (recipient.CombatState?.IsEngaged ?? false) || recipient == caster;
                return perRound * rounds * quality * (fighting ? 1f : 0.6f);
            }
            catch
            {
                return 0f;
            }
        }

        /// <summary>
        /// ★ v0.3.2: 0..1 combat worth of the best of <paramref name="buffs"/> on <paramref name="recipient"/>, from the
        /// buff's components (BuffEffectAnalyzer, shared with real-time) and stacking with what the recipient already has
        /// (BuffStackingAnalyzer: e.g. Mage Armor under real armor). Effects the analyzer does not recognise count 0.5 for
        /// renewable abilities and 0 for consumables (an item is gone once used), and items need a clear combat effect.
        /// </summary>
        private static float BuffQuality(AbilityData ability, AbilityProfile profile,
                                         List<Kingmaker.UnitLogic.Buffs.Blueprints.BlueprintBuff> buffs, UnitEntityData recipient)
        {
            bool item = ability.SourceItem != null;
            float best = 0f;
            foreach (var buff in buffs)
            {
                // Descriptor-based stacking only (BuffStackingAnalyzer.WouldBeEffective also matches names: "shield").
                if (!BuffStackingAnalyzer.Analyze(buff, recipient).IsEffective) continue;
                var analysis = BuffEffectAnalyzer.Analyze(buff);
                // ★ v0.3.3: a day-long buff with no recognisable combat effect is a "used today" marker (Treat Affliction:
                // "you can treat each character only once per day"), not a buff.
                if (!analysis.Recognized && profile.IsDayLong(buff)) continue;
                // ★ v0.3.4: an enchanting ability's buff is often an empty carrier while the enchantment itself is applied
                // by the ability's actions (Spirit Weapon Enchantment: "Components=[NoComponents]", valued 0.1 in 0.3.3).
                float q = analysis.Recognized ? analysis.CombatValue
                        : profile.Enchants ? 0.8f
                        : analysis.OnlyUnrecognized ? (item ? 0f : 0.5f)
                        : 0.05f; // no components at all: a marker or cooldown
                if (item && q < 0.5f) q = 0f;
                best = Math.Max(best, q);
            }
            return best;
        }

        /// <summary>
        /// ★ v0.3.3: Soul Power (Dark Elementalist): "gather up the souls of dead creatures with HD sum equal to or higher
        /// than her character level ... reducing her current burn by 1", and each successful use raises her elemental
        /// overflow attack/damage bonus for the day. Orvath cast it at round 1 with no corpses, every combat. Needs dead
        /// creatures in its area not yet marked by it (the marker buff it applies to every creature in the burst) whose
        /// HD reach the caster's level; worth more when there is burn to remove.
        /// </summary>
        private static float BurnReliefValue(TacticalContext ctx, AbilityProfile profile)
        {
            var unit = ctx.Unit;
            int burn = 0;
            try { burn = unit.Get<Kingmaker.UnitLogic.Class.Kineticist.UnitPartKineticist>()?.AcceptedBurn ?? 0; } catch { }

            if (profile.IsAoE && profile.TargetBuffs.Count > 0)
            {
                int needed = Math.Max(1, unit.Descriptor?.Progression?.CharacterLevel ?? 1);
                int souls = 0;
                foreach (var other in Game.Instance.State.Units)
                {
                    if (other == null || other == unit || !(other.Descriptor?.State?.IsDead ?? false)) continue;
                    if (Vector3.Distance(other.Position, unit.Position) > profile.AoERadius + other.Corpulence) continue;
                    if (profile.TargetBuffs.Any(b => other.Buffs.HasFact(b))) continue;   // souls already used
                    souls += Math.Max(other.Descriptor?.Progression?.CharacterLevel ?? 0, other.Blueprint?.CR ?? 1);
                }
                if (souls < needed) return 0f;
            }

            float overflow = 1.5f * Math.Min(ctx.ExpectedRounds, 3f);   // +attack/+damage for the rest of the day
            return (burn > 0 ? 4f : 0f) + overflow;
        }

        /// <summary>
        /// ★ v0.3.4: the "dispel" fallback needs a target: an enemy with a spell buff to strip (Dispel Magic), or an ally with
        /// a harmful effect. Treat Affliction kept a 2.0 "dispel" option on Orvath with nothing to treat in 0.3.3.
        /// </summary>
        private static bool HasSomethingToDispel(UnitEntityData caster, UnitEntityData target, AbilityProfile profile)
        {
            if (target?.Buffs == null) return false;
            try
            {
                bool enemy = caster.IsEnemy(target);
                foreach (var buff in target.Buffs.Enumerable)
                {
                    bool harmful = buff.Blueprint.Harmful ||
                                   (buff.MaybeContext?.MaybeCaster != null && buff.MaybeContext.MaybeCaster.IsEnemy(target));
                    if (!enemy && harmful) return true;
                    if (enemy && profile.Dispels && !harmful && buff.Blueprint.IsFromSpell) return true;
                }
            }
            catch { }
            return false;
        }

        private static ActionSlot SlotOf(AbilityData ability)
        {
            if (ability.RequireFullRoundAction) return ActionSlot.FullRound;
            // UnitUseAbility uses RuntimeActionType (metamagic such as Quicken changes it), so must we.
            switch (ability.RuntimeActionType)
            {
                case UnitCommand.CommandType.Free: return ActionSlot.Free;
                case UnitCommand.CommandType.Swift: return ActionSlot.Swift;
                case UnitCommand.CommandType.Move: return ActionSlot.Move;
                default: return ActionSlot.Standard;
            }
        }

        /// <summary>Consumable policy multiplier; 0 means "do not use this item now".</summary>
        private static float ConsumableFactor(TacticalContext ctx, AbilityData ability, AbilityProfile profile)
        {
            if (!(ability.SourceItem is ItemEntityUsable item)) return 1f;
            if (!ConsumablePolicy.IsAllowed(ability)) return 0f;

            switch (item.Blueprint.Type)
            {
                case UsableItemType.Potion:
                    // Healing potions are judged by the heal scoring (urgency); others only in real fights.
                    return profile.Heals.Count > 0 ? 1f : (ctx.Difficulty >= 0.8f ? 0.5f : 0f);
                case UsableItemType.Scroll:
                case UsableItemType.Wand:
                    return 0.7f * CombatMath.UseMagicDeviceChance(ability, ctx.Unit);
                default:
                    return 0.85f * CombatMath.UseMagicDeviceChance(ability, ctx.Unit);
            }
        }

        /// <summary>
        /// The best target for this ability. ★ v0.3.7: with the Progressive policy also the best target the spell has a
        /// fair chance against and the best one that hits a boss, since the tier gate judges those (a spell whose
        /// highest-value target saves easily can still be the right tier against another enemy).
        /// </summary>
        private static IEnumerable<Candidate> EvaluateAbility(TacticalContext ctx, AbilityData ability,
                                                              UnitEntityData onlyTarget = null)
        {
            var none = Enumerable.Empty<Candidate>();
            var profile = AbilityProfile.Get(ability);
            if (profile == null) return none;

            float consumable = ConsumableFactor(ctx, ability, profile);
            if (consumable <= 0f) return none;

            var slot = SlotOf(ability);
            var targets = new List<UnitEntityData>();
            bool selfOnly = ability.TargetAnchor == Kingmaker.UnitLogic.Abilities.Blueprints.AbilityTargetAnchor.Owner ||
                            ability.Range == AbilityRange.Personal;

            if (profile.IsCharge)
            {
                var charge = EvaluateCharge(ctx, ability);
                return charge != null ? new[] { charge } : none;
            }

            bool progressive = ctx.Settings.SpellPolicy == SpellResourcePolicy.Progressive && ability.Spellbook != null &&
                               ability.SourceItem == null;
            var bosses = progressive && (ctx.Global?.ProgressiveBossBypass ?? true)
                ? ctx.Enemies.Where(ThreatAssessment.IsBoss).ToList() : new List<UnitEntityData>();
            string metamagic = MetamagicTag(ability);

            // Intent decides which side an ability may be aimed at: never a harmful spell on a friend just because the
            // blueprint allows targeting friends, never a purely helpful one on an enemy.
            // ★ v0.3.12: "harmful" includes what it applies (Entangle's Entangled buff), not only the blueprint's flag.
            bool helpfulOnly = profile.HelpfulToAllies && !profile.HarmfulEffects && !profile.DealsDamage;
            bool harmfulOnly = (profile.HarmfulEffects || profile.DealsDamage || profile.IsWeaponAttack || profile.Maneuvers.Count > 0) && !profile.HelpfulToAllies && profile.Heals.Count == 0;

            if (selfOnly)
            {
                if (!harmfulOnly || profile.IsAoE) targets.Add(ctx.Unit);
            }
            else
            {
                if (!helpfulOnly && (ability.Blueprint.CanTargetEnemies || (ability.CanTargetPoint && (profile.HarmfulEffects || profile.DealsDamage))))
                    targets.AddRange(ctx.Enemies);
                if (!harmfulOnly && (ability.Blueprint.CanTargetFriends || ability.Blueprint.CanTargetSelf))
                    targets.AddRange(ctx.Allies.Where(a => a == ctx.Unit ? ability.Blueprint.CanTargetSelf : ability.Blueprint.CanTargetFriends));
                if (profile.Heals.Count > 0 && ability.Blueprint.CanTargetFriends)
                    targets.AddRange(ctx.DownedAllies);
            }

            // ★ v0.3.18 (AA-1): a summon that can only be aimed at a point (Summon Monster, Summon Nature's Ally) had no
            // target side at all and was never offered. It is aimed at a spot near the fight (SummonPoint) and scored as
            // the caster's own action.
            Vector3? summonPoint = null;
            if (!selfOnly && targets.Count == 0 && profile.Summons && ability.CanTargetPoint)
            {
                summonPoint = SummonPoint(ctx, ability);
                if (summonPoint.HasValue) targets.Add(ctx.Unit);
            }

            Candidate best = null, bestViable = null, bestBoss = null;
            foreach (var target in targets.Distinct())
            {
                if (onlyTarget != null && target != onlyTarget) continue;
                var wrapper = summonPoint.HasValue ? new TargetWrapper(summonPoint.Value) : AbilityProfile.TargetOn(ability, target);
                if (wrapper == null) continue;

                bool needsMove = false;
                Disengage risk = null;
                Approach approach = null;
                if (target != ctx.Unit && !summonPoint.HasValue)
                {
                    float range = ability.GetApproachDistance(wrapper.Unit);
                    bool needLos = new UnitUseAbility(ability, wrapper).NeedLoS;
                    if (!InRangeNow(ctx.Unit, target.Position, range, needLos))
                    {
                        // ★ v0.3.16 (test3): the game measures range flat unless the height difference is over 1.5 m, then
                        // by the larger of the flat distance and half the height (GeometryUtils.MechanicsDistance). Within
                        // reach on the ground but not by that rule, the target is over twice the reach above or below (a
                        // ledge): a touch cannot reach it from this level. Camellia stepped 0.5 m towards such a Corrupted
                        // Mongrel and was still out of reach; in 0.3.15 a 0.0 m approach repeated until her turn ended.
                        if (ability.Blueprint.StickyTouch != null && HorizontalDistance(ctx.Unit.Position, target.Position) <= range * 0.9f)
                            continue;
                        if (slot != ActionSlot.Standard || !ctx.HasMove || ctx.Distance(target) - range > ctx.MoveRange * 0.95f)
                            continue;
                        needsMove = true;
                        // ★ v0.3.13: walking up to cast can provoke too.
                        // ★ v0.3.17: around a harmful area when the straight approach walks into it (PlanApproach).
                        approach = PlanApproach(ctx, target, range, alwaysExposure: true);
                        risk = approach.Risk;
                    }
                }

                float value = ScoreAbility(ctx, ability, profile, slot, target, out string why);
                if (value <= 0f) continue;
                // ★ v0.3.18: an area that can only be aimed at a creature (not at the ground) is applied to that creature
                // first; its spell resistance or immunity stops the whole area (AbilityProfile.TargetOn).
                if (profile.IsAoE && !profile.SelectsTargets && wrapper.Unit != null && wrapper.Unit != ctx.Unit)
                {
                    float aimPass = CombatMath.SpellResistancePass(ctx.Unit, wrapper.Unit, ability);
                    if (aimPass <= 0f) continue;
                    value *= aimPass;
                }
                // ★ v0.3.17 (test3): does the aimed enemy itself get any of the value, or only the others in the area?
                bool aimAffected = !(profile.IsAoE && target != ctx.Unit && ctx.Unit.IsEnemy(target)) || ctx.AimCounted;
                value *= consumable;
                if (needsMove) value *= 0.9f;
                // ★ v0.3.6: a standard-action spell cast in melee needs a defensive-casting check; failing it provokes an
                // attack of opportunity and can lose the spell (UnitUseAbility.OnTick). Camellia's Cure Light Wounds ended
                // "Fail" that way. Potions and wands do not provoke (AbilityData.ProvokeAttackOfOpportunity).
                // ★ v0.3.16: priced by the game's rule instead of a flat 15 %: a check of d20 + concentration against
                // 15 + 2 x spell level (RuleCheckCastingDefensively), and on a failure every enemy engaging the caster
                // where it casts attacks (CombatEngagementController.ProvokeAttackOfOpportunityAfterDefensivelyFail).
                // Camellia walked 3.4 m in among three Corrupted Mongrels to read an Inflict Moderate Wounds scroll, all
                // three struck and she went down before the spell went off.
                // ★ v0.3.18 (B10): and a ray's ranged attack roll provokes too, cast defensively or not (ProvokeLoss).
                var castAt = needsMove && approach != null ? approach.Point : ctx.Unit.Position;
                float provokeLoss = ProvokeLoss(ctx, ability, profile, slot, wrapper.Unit, castAt, value, out var provoke, out var rollProvoke);
                value -= provokeLoss;
                // ★ v0.3.14: entangled (or "casting is difficult"): a concentration check before the spell even starts.
                value *= CombatMath.CastingConcentrationChance(ctx.Unit, ability);
                // ★ v0.3.1: an archer walking up to an enemy to touch it gives up its ranged attacks and invites
                // attacks of opportunity (Lann walked 9 m for Aid Another).
                if (needsMove && ctx.HasRangedWeapon && ability.Range == AbilityRange.Touch && ctx.Unit.IsEnemy(target))
                    value *= 0.5f;
                float preRisk = value;
                if (risk != null) value = value * (1f - risk.StopChance) - risk.Cost;

                var candidate = new Candidate
                {
                    Kind = CandidateKind.Ability,
                    Slot = slot,
                    Ability = ability,
                    Target = wrapper.Unit,
                    Point = wrapper.Unit == null ? wrapper.Point : (Vector3?)null,
                    NeedsMove = needsMove,
                    Value = value,
                    Offensive = IsOffensive(why),
                    Label = $"{(ability.SourceItem != null ? "Item:" : "")}{ability.Name}{metamagic}->{(summonPoint.HasValue ? "a spot near the fight" : target.CharacterName)}({why}){risk?.Tag}" +
                            (approach?.Around != null ? $" (around {approach.Around})" : "") +
                            (provoke != null ? $" [casting provokes {provoke.Attacks}{(provoke.DropChance >= 0.1f ? $", {provoke.DropChance:P0} down" : "")}]" : "") +
                            (rollProvoke != null ? $" [ray provokes {rollProvoke.Attacks}{(rollProvoke.DropChance >= 0.1f ? $", {rollProvoke.DropChance:P0} down" : "")}]" : ""),
                    CastRiskCost = provokeLoss,
                    AimAffected = aimAffected,
                    MoveFirst = approach?.Around != null,
                    MovePoint = approach?.Around != null ? approach.Point : (Vector3?)null,
                    ApproachRadius = approach?.Arrival ?? 0f,
                    CheckPath = needsMove && risk?.Area == null,
                    Risk = needsMove ? risk : null,
                    RiskAt = needsMove ? approach?.Point : null,
                    PreRiskValue = preRisk,
                    Key = $"abl:{CombatMemory.AbilityKey(ability)}{metamagic}:{target.UniqueId}"
                };
                if (Better(candidate, best)) best = candidate;
                if (!progressive || !candidate.Offensive) continue;

                candidate.SuccessChance = ProgressiveGate.SuccessChance(ctx.Unit, ctx.Enemies, ability, profile, candidate.Target, candidate.Point);
                float threshold = slot == ActionSlot.Free || slot == ActionSlot.Swift ? MinQuickValue : MinMainValue;
                if (candidate.SuccessChance >= SpellTierPolicy.MinSuccessChance && value >= threshold &&
                    Better(candidate, bestViable))
                    bestViable = candidate;
                if (bosses.Count > 0 && ThreatAssessment.HitsBoss(candidate.Target, candidate.Point, profile, bosses) &&
                    Better(candidate, bestBoss))
                    bestBoss = candidate;
            }
            return new[] { best, bestViable, bestBoss }.Where(c => c != null).Distinct();
        }

        /// <summary>
        /// ★ v0.3.18 (AA-1): where a summon aimed at a point goes: towards the nearest enemy, a few metres short of it so
        /// the creature arrives beside the fight rather than on it, within the ability's range and the caster's line of
        /// sight, on walkable ground outside harmful areas. Null when no such spot exists (then it is not offered).
        /// </summary>
        private static Vector3? SummonPoint(TacticalContext ctx, AbilityData ability)
        {
            var unit = ctx.Unit;
            var enemy = ctx.Enemies.OrderBy(e => ctx.Distance(e)).FirstOrDefault();
            if (enemy == null) return null;
            try
            {
                float range = ability.GetApproachDistance();
                Vector3 dir = enemy.Position - unit.Position;
                dir.y = 0f;
                float dist = dir.magnitude;
                if (dist < 0.5f) return null;
                dir /= dist;
                foreach (float wanted in new[] { dist - 2.5f, dist - 4f, range * 0.9f, range * 0.5f, 2f })
                {
                    float along = Mathf.Clamp(wanted, 1.5f, Math.Min(range * 0.9f, dist - 1f));
                    var point = unit.Position + dir * along;
                    if (!ability.CanTarget(new TargetWrapper(point))) continue;
                    if (!InRangeNow(unit, point, range, needLos: true)) continue;
                    if (!AoeDangerAnalyzer.IsPositionValid(unit.Position, point)) continue;
                    if (AoeDangerAnalyzer.EnteringDanger(unit, point, point, out _) > 0f) continue;
                    return point;
                }
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Tactics] summon point for {ability.Name}: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// ★ v0.3.16: a standard-action spell cast in melee needs a defensive-casting check (CastProvokeRisk).
        /// ★ v0.3.18 (B10): a ray (an ability with a ranged attack roll) aimed at an enemy provokes by its roll as well,
        /// whatever the casting check did. An enemy makes one attack of opportunity a round (more with Combat Reflexes),
        /// so the two share it: the roll always provokes, its cost replaces the casting one, and the spell is still lost
        /// when the casting check fails and a hit spoils it. What the action loses by provoking, from <paramref name="value"/>.
        /// </summary>
        private static float ProvokeLoss(TacticalContext ctx, AbilityData ability, AbilityProfile profile, ActionSlot slot,
                                         UnitEntityData target, Vector3 at, float value, out Disengage cast, out Disengage roll)
        {
            cast = roll = null;
            float loss = 0f;
            // Potions and wands do not provoke (AbilityData.ProvokeAttackOfOpportunity); a potion is drunk, never cast.
            if (slot == ActionSlot.Standard && ability.ProvokeAttackOfOpportunity &&
                ability.SourceItemUsableBlueprint?.Type != UsableItemType.Potion)
            {
                cast = CastProvokeRisk(ctx, ability, at);
                if (cast != null) loss = value * cast.StopChance + cast.Cost;
            }
            // ★ v0.3.18 (AA-1): an ability whose own actions make the caster provoke (AbilityProfile.CasterProvokes).
            if (cast == null && profile != null && profile.CasterProvokes)
            {
                cast = ProvokeRisk(ctx, at, 1f, spoiledByHit: 0f);
                if (cast != null) loss = cast.Cost;
            }
            if (profile != null && profile.RangedAttackRoll && target != null && ctx.Unit.IsEnemy(target) &&
                ProvokeObserver.AbilityProvokes(ctx.Unit, ability))
            {
                roll = AttackRollProvokeRisk(ctx, at, 1);
                if (roll != null) loss += Math.Max(0f, roll.Cost - (cast?.Cost ?? 0f));
            }
            return loss;
        }

        /// <summary>
        /// ★ v0.3.12: the option damages, controls or debuffs an enemy: one of ScoreAbility's own reason tokens ("dmg",
        /// "dmg:cdg", "cc", "debuff"), matched exactly instead of by substring. Progressive's scope depends on it.
        /// </summary>
        private static bool IsOffensive(string why)
        {
            if (string.IsNullOrEmpty(why)) return false;
            foreach (var part in why.Split('+'))
            {
                int colon = part.IndexOf(':');
                string token = colon >= 0 ? part.Substring(0, colon) : part;
                if (token == "dmg" || token == "cc" || token == "debuff" || token == "maneuver") return true;
            }
            return false;
        }

        private static string MetamagicTag(AbilityData ability) => AbilityDiscovery.MetamagicTag(ability);

        /// <summary>
        /// ★ v0.3.17 (test4): the better aim of the same ability. More value always wins: an area centred on an immune
        /// enemy that catches more of the others is the better cast. Only on equal value (the same enemies covered) does
        /// a centre on an enemy the ability affects win - "Sleep->Giant Spider" (immune) was cast for the wolves beside it.
        /// </summary>
        private static bool Better(Candidate candidate, Candidate than)
        {
            if (than == null) return true;
            float tolerance = 1e-3f * Math.Max(1f, Math.Abs(than.Value));
            if (candidate.Value > than.Value + tolerance) return true;
            if (candidate.Value < than.Value - tolerance) return false;
            return candidate.AimAffected && !than.AimAffected;
        }

        private static Candidate EvaluateCharge(TacticalContext ctx, AbilityData charge)
        {
            if (!ctx.CanFullAttack) return null;
            bool pounce;
            try
            {
                pounce = ctx.Unit.Descriptor.State.Features.Pounce &&
                         !ctx.Unit.Descriptor.State.HasCondition(UnitCondition.Staggered);
            }
            catch { pounce = false; }
            Candidate best = null;
            foreach (var enemy in ctx.Enemies)
            {
                if (!charge.CanTarget(new TargetWrapper(enemy))) continue;
                var single = CombatMath.EstimateWeaponAttack(ctx.Unit, enemy, full: false);
                if (single == null || InRangeNow(ctx.Unit, enemy.Position, single.Reach, true)) continue;
                // Only worth it when a normal move cannot get us there this turn.
                if (ctx.Distance(enemy) - single.Reach <= ctx.MoveRange * 0.95f) continue;

                // ★ v0.3.18 (AA-4): with Pounce the charge ends in a full attack (UnitAttack: a charge is a full attack
                // when the unit has Pounce and is not staggered).
                var hit = pounce ? CombatMath.EstimateWeaponAttack(ctx.Unit, enemy, full: true) ?? single : single;
                float value = DamageValue(enemy, hit, 1.1f) * RoleWeight(ctx, "damage") *
                              PreferenceWeight(ctx, false) * 0.95f;
                string label = $"Charge->{enemy.CharacterName}{(pounce && hit.Attacks > 1 ? $" (pounce, {hit.Attacks} attacks)" : "")}";
                // ★ v0.3.13: a charge out of melee provokes like any other move.
                var risk = MoveRisk(ctx, ApproachPoint(ctx.Unit, enemy, single.Reach));
                if (risk != null)
                {
                    value = value * (1f - risk.StopChance) - risk.Cost;
                    label += risk.Tag;
                }
                if (best == null || value > best.Value)
                    best = new Candidate
                    {
                        Kind = CandidateKind.Ability, Offensive = true, Slot = ActionSlot.FullRound, Ability = charge, Target = enemy,
                        Value = value, Label = label, Key = $"charge:{enemy.UniqueId}"
                    };
            }
            return best;
        }

        private static float ScoreAbility(TacticalContext ctx, AbilityData ability, AbilityProfile profile, ActionSlot slot,
                                          UnitEntityData target, out string why)
        {
            var unit = ctx.Unit;
            bool targetIsEnemy = unit.IsEnemy(target);
            int level = Math.Max(0, ability.SpellLevel);
            float value = 0f;
            float slotSensitive = 0f;   // ★ v0.3.17: the damage and control parts (spell-slot discount)
            ctx.AimCounted = false;
            var reasons = new List<string>();

            if (IsCoupDeGrace(ability))
                return CoupDeGraceValue(ctx, ability, target, out why);
            // ★ v0.3.18 (AA-4): Vital Strike and Cleave are weapon attacks (TacticalEngine.Martial.cs).
            if (profile.IsWeaponAttack)
                return WeaponAbilityValue(ctx, profile, target, out why);

            List<UnitEntityData> affectedEnemies = targetIsEnemy ? new List<UnitEntityData> { target } : new List<UnitEntityData>();
            List<UnitEntityData> affectedAllies = targetIsEnemy ? new List<UnitEntityData>() : new List<UnitEntityData> { target };
            if (profile.IsAoE)
            {
                var targetsType = profile.AoETargets;
                affectedEnemies = targetsType == Kingmaker.UnitLogic.Abilities.Components.TargetType.Ally
                    ? new List<UnitEntityData>() : UnitsAround(ctx.Enemies, target.Position, profile.AoERadius);
                affectedAllies = targetsType == Kingmaker.UnitLogic.Abilities.Components.TargetType.Enemy
                    ? new List<UnitEntityData>() : UnitsAround(ctx.Allies, target.Position, profile.AoERadius);
                // ★ v0.3.8: who the area can affect at all (Sleep: creatures with few hit dice).
                affectedEnemies = affectedEnemies.Where(u => profile.AreaReaches(ability, u)).ToList();
                affectedAllies = affectedAllies.Where(u => profile.AreaReaches(ability, u)).ToList();
            }
            // ★ v0.3.18 (AA-4): a chain goes on to the units nearest its first target within its radius, in the game's
            // order (AbilityDeliverChain): enemies, allies or both by its target type (Selective spares allies).
            bool chainCatchesAllies = false;
            if (profile.Chain != null && !profile.IsAoE && target != unit)
            {
                int more = CombatMath.ChainCount(ability, profile, target) - 1;
                float radius = profile.Chain.Radius.Meters;
                var chainType = profile.ChainTargets;
                bool selective = false;
                try { selective = ability.HasMetamagic(Metamagic.Selective); } catch { }
                IEnumerable<UnitEntityData> pool;
                if (chainType == Kingmaker.UnitLogic.Abilities.Components.TargetType.Ally) pool = ctx.Allies;
                else if (chainType == Kingmaker.UnitLogic.Abilities.Components.TargetType.Enemy || selective) pool = ctx.Enemies;
                else pool = ctx.Enemies.Concat(ctx.Allies);
                foreach (var next in pool.Where(u => u != target && u != unit && Vector3.Distance(u.Position, target.Position) <= radius)
                                         .OrderBy(u => Vector3.Distance(u.Position, target.Position)).Take(Math.Max(0, more)))
                {
                    if (unit.IsEnemy(next)) { if (!affectedEnemies.Contains(next)) affectedEnemies.Add(next); }
                    else if (!affectedAllies.Contains(next)) { affectedAllies.Add(next); chainCatchesAllies = true; }
                }
            }

            // ★ v0.3.1: never aim a heal at an enemy. With per-target branches, Cure on an undead enemy (damage branch)
            // and Lay On Hands on undead stay valid, but Lay On Hands / Cure on a living enemy and Inflict on an undead
            // one are refused. A heal whose branch cannot be evaluated counts as reaching the enemy.
            if (targetIsEnemy && !profile.IsAoE &&
                profile.Heals.Any(h => profile.AppliesTo(h, ability, target) != EffectApplies.No))
            {
                why = "heals enemy";
                return 0f;
            }

            // ★ v0.3.2: none of its effects on the target applies to this target (Lay On Hands - Others on a living
            // enemy kept only the paladin's own side buff, valued up to 8.8).
            if (!profile.IsAoE && target != unit && profile.HasTargetEffects && !profile.AnyTargetEffectApplies(ability, target))
            {
                why = "no effect on target";
                return 0f;
            }

            // --- kineticist burn relief (Dark Elementalist Soul Power) ---
            if (profile.HealsBurn)
            {
                float burnValue = BurnReliefValue(ctx, profile);
                if (burnValue <= 0f)
                {
                    why = "nothing to gain";
                    return 0f;
                }
                value += burnValue;
                reasons.Add("burn");
            }

            // --- damage ---
            if (profile.DealsDamage && affectedEnemies.Count > 0)
            {
                float dmgValue = 0f;
                // ★ v0.3.18 (C2): one resistance path - spell resistance (with penetration) and spell immunity in the
                // effect chance, energy immunity and resistance in the damage estimate. A second analyser filtered here
                // first, with its own rules (no penetration, immunity only for spells that allow spell resistance).
                foreach (var enemy in affectedEnemies)
                {
                    float dmg = CombatMath.EstimateAbilityDamage(ability, profile, enemy);
                    float p = CombatMath.EffectChance(unit, enemy, ability, profile, damage: true);
                    float part = DamageValue(enemy, dmg * p, p) * WakeFactor(enemy, dmg * p);
                    // ★ v0.3.18 (AA-1): a kill action (Isekai's Instakill, Power Word Kill) removes the enemy when it lands:
                    // on a failed save and through spell resistance. It subsumes the damage, so the better of the two.
                    if (CombatMath.KillApplies(ability, profile, enemy))
                    {
                        float pk = CombatMath.EffectChance(unit, enemy, ability, profile, damage: false);
                        float hp = Math.Max(1, enemy.HPLeft);
                        part = Math.Max(part, ValueModel.DamageValue(hp * pk, hp, CombatMath.Threat(enemy), pk));
                    }
                    dmgValue += part;
                    if (enemy == target && part > 0f) ctx.AimCounted = true;
                }

                // Friendly fire: hurting an ally costs twice what the same damage on an enemy is worth.
                // ★ v0.3.7: Selective metamagic spares allies. ★ v0.3.14: only where the game's rule says so (not in
                // lasting areas with Tabletop Tweaks): AbilityProfile.SelectiveSparesAllies.
                if (affectedAllies.Count > 0 && (profile.IsAoE || chainCatchesAllies) && !profile.SelectiveSparesAllies(ability))
                {
                    float dmg = CombatMath.EstimateAbilityDamage(ability, profile, affectedAllies[0]);
                    float allyCost = affectedAllies.Sum(a => Math.Min(dmg, a.HPLeft)) * 2f;
                    if (ctx.Settings.AvoidFriendlyFire && allyCost > 0f) { why = "friendly fire"; return 0f; }
                    dmgValue -= allyCost;
                }

                if (dmgValue > 0f)
                {
                    value += dmgValue * RoleWeight(ctx, "damage");
                    slotSensitive += dmgValue * RoleWeight(ctx, "damage");
                    reasons.Add("dmg");
                }
            }

            // --- healing (undead only when the ability's own branch heals them: positive energy harms them) ---
            // ★ v0.3.9: a heal's area also reaches downed allies inside it.
            var healTargets = profile.IsAoE && profile.AoETargets != Kingmaker.UnitLogic.Abilities.Components.TargetType.Enemy
                ? affectedAllies.Concat(UnitsAround(ctx.DownedAllies, target.Position, profile.AoERadius)
                                        .Where(u => profile.AreaReaches(ability, u))).ToList()
                : affectedAllies;
            if (profile.Heals.Count > 0 && healTargets.Count > 0)
            {
                float healValue = 0f;
                string reviveNote = null;
                foreach (var ally in healTargets)
                {
                    var applies = profile.Heals.Select(h => profile.AppliesTo(h, ability, ally)).ToList();
                    if (applies.All(a => a == EffectApplies.No)) continue;             // e.g. Inflict Wounds on the living
                    // Positive energy harms undead: only heal them when the ability's own branch says it heals them.
                    if (!applies.Contains(EffectApplies.Yes) && AbilityClassifier.IsUndeadOrConstruct(ally)) continue;
                    float missing = ally.MaxHP - ally.HPLeft;
                    float heal = Math.Min(CombatMath.EstimateAbilityHeal(ability, profile, ally), missing);
                    if (heal <= 0f) continue;

                    // ★ v0.3.9: a downed ally gets back up once its HP is above 0: worth what it does for the rest of the
                    // fight. A heal too small to bring it up counts only a little (the next one will).
                    // ★ v0.3.14: a heal that covers part of what is needed is that share of the way there (x0.6: the
                    // ally is still down until the next heal lands). 0.3.9 valued it at 0.3 x the share: Camellia's
                    // Cure Light Wounds took Seelah part of the way, then nobody healed her again and she stayed down
                    // for the rest of the fight (the game wakes a unit only once its HP is above 0, and revives
                    // everyone after combat, which is when she got up).
                    if (ally.HPLeft <= 0 || !ally.Descriptor.State.IsConscious)
                    {
                        float needed = Math.Max(1f, 1f - ally.HPLeft + ally.DamageNonLethal);
                        float share = Math.Min(1f, heal / needed);
                        float upChance = share >= 1f ? 1f : 0.6f * share;
                        reviveNote = $"revive {ally.CharacterName}: needs {needed:F0} HP, heals ~{heal:F0}";
                        // ★ v0.3.14 (test5): worth only as much as the chance it stays up (ReviveStaysUp).
                        if (share >= 1f)
                        {
                            float staysUp = ReviveStaysUp(ctx, ally, Math.Max(1f, heal - needed + 1f));
                            upChance *= staysUp;
                            reviveNote += $", stays up ~{staysUp:P0}";
                        }
                        healValue += upChance * AllyWorth(ctx, ally);
                        continue;
                    }

                    float hp = CombatMath.HpPercent(ally);
                    float threshold = CombatMath.HealThreshold(ability, unit, ally, ctx.Settings);
                    if (hp >= threshold) continue;

                    // ★ v0.3.18 (B4b): restoring HP is worth more the further below the threshold the ally is, on one
                    // continuous curve that reaches 0 at the threshold (ValueModel.HealPerHp). The danger of dropping is
                    // the drop-chance term below, and only there: 0.3.17 also counted it here (x1.25 when engaged, x1 / x2
                    // / x3 by HP band), and a heal was the best-valued option of the logs (median 18-21, weapons 7-8).
                    // ★ v0.3.17: a summon is worth half (AllyWorth) in this part too: at full HP Camellia walked into
                    // Grease to heal a summoned Dretch, the restoring part valued like a companion's (log 06aebf2d).
                    healValue += heal * ValueModel.HealPerHp(hp, threshold) * (IsSummon(ally) ? ValueModel.SummonFactor : 1f);

                    // ★ v0.3.9: how much less likely the ally is to drop before its next turn. Lann at 25 % HP valued his
                    // potion at 5.9 against a 7.6 full attack, shot, and went down; the heal ignored who could hit him.
                    float danger = IncomingDamage(ctx, ally);
                    if (danger > 0f)
                    {
                        float before = Mathf.Exp(-Math.Max(1f, ally.HPLeft) / danger);
                        float after = Mathf.Exp(-(Math.Max(1f, ally.HPLeft) + heal) / danger);
                        healValue += (before - after) * AllyWorth(ctx, ally);
                    }
                }
                if (healValue > 0f)
                {
                    value += healValue * RoleWeight(ctx, "heal");
                    reasons.Add("heal");
                    if (reviveNote != null) ReportOnce($"revive|{unit.UniqueId}|{CombatMemory.Round}|{reviveNote}", $"[Tactics] {unit.CharacterName}: {ability.Name} {reviveNote}");
                }
            }

            // --- control / debuffs / marks on enemies ---
            // Harmful buffs are debuffs or crowd control; a non-harmful buff placed on an enemy is a "mark" that helps
            // us against it (Smite Evil, judgements, studied target...) and is worth less than a real debuff.
            if (profile.TargetBuffs.Count > 0 && affectedEnemies.Count > 0)
            {
                float controlValue = 0f;
                bool isMark = !profile.HarmfulToEnemies && profile.ControlStrength == ControlStrength.None;
                // ★ v0.3.2: a mark is aimed at one enemy. A helpful area buff centred on the caster (Soul Power, Light
                // of the Angels) counted the enemies standing in its radius as "marks" and scored 26-38 every round.
                if (isMark && profile.IsAoE) affectedEnemies = new List<UnitEntityData>();
                // ★ v0.3.1: a one-round / one-attack mark (Aid Another: +2 for one ally, one round) is worth 0.1 × threat
                // for one round; it out-valued Lann's full attack and walked him 9 m into melee. Lasting marks (Smite
                // Evil, judgements, studied target) keep 0.15 × threat × rounds.
                bool oneShotMark = isMark && profile.IsOneShot;
                float strength = profile.ControlStrength == ControlStrength.Hard ? 1f :
                                 profile.ControlStrength == ControlStrength.Soft ? 0.4f :
                                 isMark ? (oneShotMark ? 0.1f : 0.15f) : 0.25f;
                // Non-control debuffs/marks repeated on new targets every turn are the "spam" pattern: diminish them.
                if (profile.ControlStrength == ControlStrength.None && CombatMemory.TimesUsed(unit, ability) >= 2)
                    strength *= 0.3f;
                // A one-round / next-attack effect lasts one round, not the rest of the fight.
                float effectRounds = profile.IsOneShot ? 1f : Math.Min(ctx.ExpectedRounds, 3f);

                // ★ v0.3.7: control or debuff areas that also catch allies (Grease, Web, Stinking Cloud now that areas
                // are analysed). Same rule as damage: dropped with "Avoid friendly fire", otherwise each caught ally
                // costs as much as an enemy of the same strength would gain. Only allies the effect would actually
                // reach count: the per-target conditions (a caster-centred aura that spares the caster, "is enemy"
                // checks), immunity and an already-present copy; Selective metamagic spares allies where the game's
                // rule says so (★ v0.3.14: AbilityProfile.SelectiveSparesAllies).
                float allyControlCost = 0f;
                bool harmfulArea = profile.IsAoE && profile.AoETargets != Kingmaker.UnitLogic.Abilities.Components.TargetType.Enemy &&
                                   (profile.HarmfulToEnemies || profile.ControlStrength != ControlStrength.None) &&
                                   !profile.SelectiveSparesAllies(ability);
                Func<UnitEntityData, bool> wouldBeCaught = a =>
                    profile.TargetBuffs.Any(b => profile.BuffAppliesTo(b, ability, a) != EffectApplies.No && !a.Buffs.HasFact(b) &&
                                                 !CombatMath.BuffImmune(a, b, ability)) &&
                    !(profile.Control != CCType.None && AbilityClassifier.HasCCImmunity(a, profile.Control));
                var caughtAllies = !harmfulArea ? new List<UnitEntityData>() : affectedAllies.Where(wouldBeCaught).ToList();
                if (caughtAllies.Count > 0)
                {
                    if (ctx.Settings.AvoidFriendlyFire) { why = "friendly fire"; return 0f; }
                    allyControlCost = caughtAllies.Sum(a => ValueModel.PerRound(Level(a)) * strength * effectRounds);
                }
                // ★ v0.3.14: a lasting area also costs the melee allies fighting at its edge: they walk in (a save, then
                // entangled) or give up the enemies inside. Seelah was next to the Baphomet Cultist when Orvath's
                // Entangle went down on it and walked in next turn. Half the cost of an ally caught inside.
                // Also melee allies further away whose nearest enemy stands inside: they will walk in to fight it
                // (Seelah charged into Orvath's round-1 Entangle twice in the 0.3.14 test).
                if (harmfulArea && profile.SpawnedAreas.Any())
                {
                    Func<UnitEntityData, bool> headsInto = a =>
                    {
                        var nearest = ctx.Enemies.OrderBy(e => Vector3.Distance(a.Position, e.Position)).FirstOrDefault();
                        return nearest != null && Vector3.Distance(nearest.Position, target.Position) <= profile.AoERadius;
                    };
                    allyControlCost += ctx.Allies
                        .Where(a => a != unit && !affectedAllies.Contains(a) && !UsesRangedWeapon(a) &&
                                    (Vector3.Distance(a.Position, target.Position) <= profile.AoERadius + 2.5f + a.Corpulence || headsInto(a)) &&
                                    wouldBeCaught(a))
                        .Sum(a => 0.5f * ValueModel.PerRound(Level(a)) * strength * effectRounds);
                }
                foreach (var enemy in affectedEnemies)
                {
                    // ★ v0.3.18 (C2): the game's buff immunities (descriptor, specific buff) too.
                    var buffs = profile.TargetBuffs.Where(b => profile.BuffAppliesTo(b, ability, enemy) != EffectApplies.No &&
                                                               !CombatMath.BuffImmune(enemy, b, ability)).ToList();
                    if (buffs.Count == 0) continue;
                    if (buffs.Any(b => enemy.Buffs.HasFact(b))) continue;
                    // ★ v0.3.13: already inside an active copy of this area from our side.
                    if (profile.IsAoE && profile.InOwnSideArea(unit, enemy)) continue;
                    if (profile.Control != CCType.None && AbilityClassifier.HasCCImmunity(enemy, profile.Control)) continue;
                    // Spell resistance and spell immunity: in the effect chance below (0 when immune).
                    float p = CombatMath.EffectChance(unit, enemy, ability, profile, damage: false);
                    // ★ v0.3.15: the duration of the branch that applies to this enemy, when the profile can tell.
                    float rounds = effectRounds;
                    float own = profile.EffectRoundsOn(ability, enemy);
                    if (own > 0f) rounds = Math.Min(rounds, Math.Max(1f, own));
                    float enemyValue = p * CombatMath.Threat(enemy) * strength * rounds;
                    // ★ v0.3.9: an enemy that already cannot act (asleep, paralysed, stunned) gains little from more
                    // control: Orvath stunned (Grasp Heart) the Mite Scout he had just put to sleep.
                    if (CombatMath.IsDisabled(enemy)) enemyValue *= 0.1f;
                    // A one-round mark only pays off when someone else in the party is fighting that enemy now.
                    if (oneShotMark && !(enemy.CombatState?.EngagedUnits?.Any(u => u != unit && !unit.IsEnemy(u)) ?? false))
                        enemyValue *= 0.5f;
                    controlValue += enemyValue;
                    if (enemy == target && enemyValue > 0f) ctx.AimCounted = true;
                }
                controlValue -= allyControlCost;
                if (controlValue > 0f)
                {
                    value += controlValue;
                    slotSensitive += controlValue;
                    reasons.Add(profile.ControlStrength == ControlStrength.Hard ? "cc" : isMark ? "mark" : "debuff");
                }
            }

            // --- combat maneuvers (★ v0.3.18, AA-4) ---
            if (profile.Maneuvers.Count > 0 && affectedEnemies.Count > 0)
            {
                float maneuverValue = 0f;
                foreach (var enemy in affectedEnemies)
                {
                    foreach (var maneuver in profile.Maneuvers)
                    {
                        if (profile.AppliesTo(maneuver.Action, ability, enemy) == EffectApplies.No) continue;
                        float strength = ManeuverStrength(enemy, ability, maneuver.Type, out bool lasting);
                        if (strength <= 0f) continue;
                        CombatMath.ManeuverOdds(unit, enemy, ability, maneuver, out float chance, out float rounds);
                        float part = chance * CombatMath.Threat(enemy) * strength *
                                     (lasting ? Math.Min(Math.Max(1f, rounds), Math.Min(ctx.ExpectedRounds, 3f)) : 1f);
                        if (CombatMath.IsDisabled(enemy)) part *= 0.1f;
                        maneuverValue += part;
                        if (enemy == target && part > 0f) ctx.AimCounted = true;
                    }
                }
                if (maneuverValue > 0f)
                {
                    value += maneuverValue;
                    slotSensitive += maneuverValue;
                    reasons.Add("maneuver");
                }
            }

            // --- buffs on allies / self ---
            bool appliesHelpfulBuff = !profile.HarmfulEffects || profile.HelpfulToAllies
                ? (!targetIsEnemy && profile.TargetBuffs.Count > 0) || profile.CasterBuffs.Count > 0
                : profile.CasterBuffs.Count > 0;
            // ★ v0.3.1: buffs that come with a heal for that recipient (Lay On Hands mercies, hidden trackers) are part of
            // the heal, not a reason to cast it at full HP; a cleanse's buffs are judged by what it removes (below); a
            // one-round self-buff (Perfect Strike) is useless when no attack is left after paying for it.
            bool attackAfter = slot == ActionSlot.Free || slot == ActionSlot.Swift ? ctx.HasStandard || ctx.CanFullAttack
                             : slot == ActionSlot.Move ? ctx.HasStandard
                             : false;
            if (appliesHelpfulBuff && !profile.DealsDamage && !profile.IsCleanse && !profile.HealsBurn)
            {
                float buffValue = 0f;
                var recipients = profile.TargetBuffs.Count > 0 && !targetIsEnemy ? affectedAllies : new List<UnitEntityData> { unit };
                var allBuffs = profile.TargetBuffs.Count > 0 && !targetIsEnemy ? profile.TargetBuffs : profile.CasterBuffs;
                foreach (var ally in recipients.Take(5))
                {
                    var buffs = allBuffs.Where(b => profile.BuffAppliesTo(b, ability, ally) != EffectApplies.No).ToList();
                    if (buffs.Count == 0) continue;
                    if (buffs.Any(b => ally.Buffs.HasFact(b))) continue;                    // already active
                    // ★ v0.3.8: an enchanting ability whose enchantment is still on the weapon.
                    if (profile.Enchants && profile.EnchantmentsPresent(unit, ally) == true) continue;
                    if (profile.Heals.Any(h => profile.AppliesTo(h, ability, ally) != EffectApplies.No)) continue;
                    if (profile.BuffsLastOneRound && ally == unit && !attackAfter) continue;
                    if (CombatMemory.LastCastRound(unit, ability, ally) == ctx.Round) continue; // cast this round already

                    // ★ v0.3.2: what the buff actually does, for this recipient. Treat Affliction, Feather Step and Mage
                    // Armor on a paladin in plate were valued like Enlarge Person in v0.3.1.
                    float quality = BuffQuality(ability, profile, buffs, ally);
                    if (quality <= 0f) continue;
                    // A mode switch (Shift Form: Overlord ↔ Mortal Guise) removes a good buff: only worth it if better.
                    var replaced = profile.BeneficialBuffsRemoved(ability, ally);
                    if (replaced.Count > 0)
                    {
                        float lost = replaced.Max(b => BuffEffectAnalyzer.GetCombatValue(b.Blueprint));
                        if (quality <= lost + 0.05f) continue;
                        quality -= lost;
                    }

                    bool fighting = (ally.CombatState?.IsEngaged ?? false) || ally == unit;
                    float perRound = ValueModel.BuffPerRound(level);
                    // One-round buffs last one round. Buffs spent by the next attack count ~2 rounds: that keeps a swift
                    // use before attacking (Perfect Strike before a full attack) above the quick-action threshold.
                    float rounds = profile.BuffsLastOneRound ? 1f
                                 : profile.BuffsSpentByAttack ? Math.Min(ctx.ExpectedRounds, 2f)
                                 : profile.HasLongBuff ? ctx.ExpectedRounds : Math.Min(ctx.ExpectedRounds, 3f);
                    buffValue += perRound * rounds * quality * (fighting ? 1f : 0.6f);
                }
                if (buffValue > 0f)
                {
                    float timing = ctx.Round <= 2 ? 1f : 0.6f;
                    value += buffValue * timing * RoleWeight(ctx, "buff") * Math.Min(1.2f, 0.4f + ctx.Difficulty);
                    reasons.Add("buff");
                }
            }

            // --- cleanses (Remove Fear, Treat Affliction, Lay On Hands mercies...): only when the target has something to remove ---
            if (profile.RemovesAnything && affectedAllies.Count > 0)
            {
                float cleanseValue = 0f;
                foreach (var ally in affectedAllies.Where(a => profile.WouldRemoveSomething(ability, a)))
                {
                    bool fighting = (ally.CombatState?.IsEngaged ?? false) || ally == unit;
                    cleanseValue += (2f + 2f * level) * Math.Min(ctx.ExpectedRounds, 3f) * (fighting ? 1f : 0.6f);
                }
                if (cleanseValue > 0f)
                {
                    value += cleanseValue * RoleWeight(ctx, "heal");
                    reasons.Add("cleanse");
                }
            }

            // --- effects the detector cannot verify: allow once per combat per target/caster ---
            // ★ v0.3.1: not for abilities whose effect IS verifiable (damage, heal, cleanse): a zero value there means
            // "not useful now" - a healing potion at full HP fell through to the "dispel" fallback (2.0) in v0.3.0.
            // Items never use the dispel/utility fallback: a consumable is gone once used.
            bool verifiable = profile.DealsDamage || profile.Heals.Count > 0 || (profile.RemovesFromTarget && !targetIsEnemy);
            if (value <= 0f && !verifiable &&
                (profile.Enchants || profile.Summons || profile.Dispels || profile.RemovesBuffs || !profile.HasDetectableEffect))
            {
                // ★ v0.3.18: a summon is once per fight whatever it was aimed at (a point summon has no target unit).
                bool usedOnTarget = CombatMemory.LastCastRound(unit, ability, target).HasValue ||
                                    (profile.Summons && CombatMemory.TimesUsed(unit, ability) > 0);
                if (!usedOnTarget)
                {
                    // ★ v0.3.18 (AA-1): the order is summon, then consumables (never a fallback), then dispel and removal
                    // before enchant (a dispel that also names an enchantment was valued as an enchant), and no "utility"
                    // value any more for an ability with no recognised effect at all: it was never chosen in seven logs,
                    // and it offered abilities such as Isekai's Planar Coliseum Return or Cosmic Store in combat. The
                    // coverage report lists those abilities ("effect not understood").
                    if (profile.Summons)
                    {
                        value += (3f + 3f * level) * Math.Min(ctx.ExpectedRounds, 4f) * 0.5f * (ctx.Enemies.Count >= 2 ? 1f : 0.6f);
                        reasons.Add("summon");
                    }
                    else if (ability.SourceItem != null)
                    {
                        // consumables: nothing verifiable to gain
                    }
                    else if (profile.Dispels || profile.RemovesBuffs)
                    {
                        // ★ v0.3.5: nothing to remove = nothing to gain; do not fall through to "utility" (Treat Affliction
                        // was offered as utility=1.0 on everyone in 0.3.4).
                        if (HasSomethingToDispel(unit, target, profile))
                        {
                            value += 2f;
                            reasons.Add("dispel");
                        }
                    }
                    else if (profile.Enchants)
                    {
                        // ★ v0.3.8: the fallback is for effects we cannot see. An enchantment that is still in place
                        // (its item enchantment, or the buff that carries it) is visible: nothing to gain. Camellia
                        // recast Spirit Weapon Enchantment at the start of four fights while it was active.
                        if (profile.EnchantmentActive(unit, target)) { why = "already active"; return 0f; }
                        value += ValueModel.BuffPerRound(level) * ctx.ExpectedRounds * 0.5f * RoleWeight(ctx, "buff");
                        reasons.Add("enchant");
                    }
                    else if (!profile.HasDetectableEffect)
                    {
                        profile.DescribeStructureOnce();
                    }
                }
            }

            // Spell slots are a resource: in easy fights prefer weapons/cantrips over levelled spells.
            // ★ v0.3.17: only the damage and control parts, as the Spell Use setting says ("damage and control spells";
            // "buffs, heals and items are unaffected"). It discounted levelled heals and buffs too.
            if (level > 0 && ability.Spellbook != null && ability.SourceItem == null && slotSensitive > 0f)
                value -= slotSensitive * (1f - Mathf.Clamp(0.55f + 0.45f * ctx.Difficulty, 0.55f, 1f));

            why = reasons.Count > 0 ? string.Join("+", reasons) : "none";
            return value;
        }

        #endregion

        #region Movement

        private static void AddMovementCandidates(TacticalContext ctx, List<Candidate> list)
        {
            var unit = ctx.Unit;
            if (!ctx.HasMove && !ctx.HasStandard) return;
            if (!ctx.Permissions.CanWalk) return;
            if (ctx.NoMovementLeft || ctx.StepTaken) return;

            AddLeaveAreaCandidate(ctx, list);

            // "Can I already hurt someone from here with what I have left this turn?"
            // ★ v0.3.14: in real time "from here" counts while the action cooldown runs: a melee character next to an
            // enemy waits for its next swing instead of walking to another enemy (and provoking) every half second.
            bool anyAttackNow = list.Any(c => c.Offensive && !c.NeedsMove && c.Value >= MinMainValue &&
                                             c.Slot != ActionSlot.Free && c.Slot != ActionSlot.Swift &&
                                             (CombatMemory.IsRealTime || Affordable(ctx, c)));
            bool rangedStyle = ctx.HasRangedWeapon || ctx.Settings.RangePreference == RangePreference.Ranged ||
                               ctx.Settings.Role == AIRole.Support;

            // ★ v0.3.6: anyone whose best main action is a spell that provokes in melee steps out first too (Camellia's
            // Cure Light Wounds failed while she was engaged) - unless that spell is aimed at an enemy next to her.
            var bestMain = list.Where(c => c.Slot != ActionSlot.Free && c.Slot != ActionSlot.Swift && !c.NeedsMove && Affordable(ctx, c))
                               .OrderByDescending(c => c.Value).FirstOrDefault();
            // ★ v0.3.18: or a ray, whose attack roll provokes (CastRiskCost holds what provoking costs it).
            bool castsInMelee = bestMain != null && bestMain.Kind == CandidateKind.Ability && bestMain.Slot == ActionSlot.Standard &&
                                (bestMain.Ability.ProvokeAttackOfOpportunity || bestMain.CastRiskCost > 0f) &&
                                !(bestMain.Target != null && ctx.Engagers.Contains(bestMain.Target));

            // 1) Step out of melee before shooting/casting (5-foot step: no attack of opportunity, keeps full attack).
            // ★ v0.3.14: real time has no 5-foot step; there a ranged character backs off a few metres instead, paying
            // for the attacks of opportunity that provokes (MoveRisk), while its action cooldown runs or right before acting.
            bool realTime = CombatMemory.IsRealTime;
            if ((rangedStyle || castsInMelee) && ctx.Engagers.Count > 0 && (ctx.CanFiveFoot || (realTime && ctx.HasMove)) &&
                (ctx.HasStandard || ctx.CanFullAttack || realTime))
            {
                // ★ v0.3.10: an archer steps to a spot from which it can still shoot someone (Lann stepped out of melee
                // and then had nothing in range or sight without more movement, which the step forbids).
                Func<Vector3, bool> keepsTarget = null;
                // ★ v0.3.17: a caster's step must keep the spell's target in its range (only archers were checked; a
                // short-range spell could be left out of reach, and after a 5-foot step no other movement is allowed).
                if (castsInMelee && bestMain.Target != null)
                {
                    float spellRange = bestMain.Ability.GetApproachDistance(bestMain.Target) * 0.95f;
                    var spellTarget = bestMain.Target;
                    Vector3 eye = unit.EyePosition - unit.Position;
                    keepsTarget = p => UnitCommand.IsUnitCloseEnough(spellTarget.Position, p, p + eye, spellRange, true, 0f);
                }
                if (ctx.HasRangedWeapon && !castsInMelee)
                {
                    float shotRange = ctx.Enemies.Select(e => CombatMath.EstimateWeaponAttack(unit, e, false))
                                                 .FirstOrDefault(x => x != null && x.IsRanged)?.Reach ?? 0f;
                    Vector3 eyeOffset = unit.EyePosition - unit.Position;
                    if (shotRange > 0f)
                        keepsTarget = p => ctx.Enemies.Any(e => UnitCommand.IsUnitCloseEnough(e.Position, p, p + eyeOffset, shotRange * 0.95f, true, 0f));
                }
                // ★ v0.3.14: the step is worth no more than what it makes possible. Aeiluvetar stepped out of melee
                // (valued 7.7, from the engagers' threat) and then had nothing better than a 0.5 shot: turn wasted.
                float followUp;
                int shots = 1;
                ItemEntityWeapon shotWeapon = null;
                if (castsInMelee)
                    followUp = bestMain.Value + bestMain.CastRiskCost;    // the spell without the risk of casting in melee
                else if (ctx.HasRangedWeapon)
                {
                    followUp = 0f;
                    foreach (var e in ctx.Enemies)
                    {
                        var est = CombatMath.EstimateWeaponAttack(unit, e, full: ctx.CanFullAttack);
                        if (est == null || !est.IsRanged) continue;
                        float shot = DamageValue(e, est) * RoleWeight(ctx, "damage");
                        if (shot <= followUp) continue;
                        followUp = shot;
                        shots = est.Attacks;
                        shotWeapon = est.Weapon;
                    }
                }
                else
                    followUp = bestMain?.Value ?? 0f;
                float stepDistance = realTime ? 4f : ctx.FiveFootRange * 0.9f;
                var point = followUp >= MinQuickValue ? FindRetreatPoint(ctx, stepDistance, keepsTarget) : null;
                if (point.HasValue)
                {
                    // ★ v0.3.17: a caster's step is worth what casting here would lose (the risk priced into the spell);
                    // with a good Concentration that is little, and it casts defensively where it stands.
                    float value;
                    if (castsInMelee)
                    {
                        // What the cast still risks from the new spot (a step may leave it next to someone).
                        float lossThere = ProvokeLoss(ctx, bestMain.Ability, AbilityProfile.Get(bestMain.Ability), bestMain.Slot,
                                                      bestMain.Target, point.Value, followUp, out _, out _);
                        value = Math.Min(followUp, Math.Max(0f, bestMain.CastRiskCost - lossThere));
                    }
                    else if (ctx.HasRangedWeapon)
                    {
                        // ★ v0.3.18 (B10): an archer's step is worth what shooting here would lose by provoking, less
                        // what it still risks from the new spot: nothing with Point Blank Master. It was 40 % of the
                        // engagers' Threat, whatever the shot risked.
                        Func<Vector3, float> loss = at =>
                        {
                            var r = AttackRollProvokeRisk(ctx, at, shots);
                            return r == null ? 0f : followUp * r.StopChance + r.Cost;
                        };
                        value = !ProvokeObserver.WeaponProvokes(unit, shotWeapon) ? 0f
                              : Math.Min(followUp, Math.Max(0f, loss(unit.Position) - loss(point.Value)));
                    }
                    else value = Math.Min(ctx.Engagers.Sum(CombatMath.Threat) * 0.4f, followUp);
                    string label = realTime ? "Back off out of melee" : "5ft step out of melee";
                    if (realTime)
                    {
                        var risk = MoveRisk(ctx, point.Value);
                        if (risk != null) { value -= risk.Cost; label += risk.Tag; }
                    }
                    list.Add(new Candidate
                    {
                        Kind = CandidateKind.Move, Slot = realTime ? ActionSlot.Move : ActionSlot.Free, Point = point, ApproachRadius = 0.3f,
                        Value = value,
                        Label = label, Key = "move:5ft"
                    });
                }
            }

            // 2) Badly hurt and within reach of enemies: withdraw towards the party.
            // ★ v0.3.10: valued like a heal - by how much less likely the unit is to drop before its next turn at the new
            // spot, including the attacks of opportunity for leaving melee. 0.3.9 offered it only when engaged and at
            // a fixed value: Lann (20 %, in the Large Water Elemental's reach) shot instead and went down.
            if (ctx.HpPct < CautionBelow(ctx))
            {
                float hp = Math.Max(1f, unit.HPLeft);
                float dangerHere = IncomingDamage(ctx, unit, unit.Position);
                if (dangerHere > 0f)
                {
                    var point = FindRetreatPoint(ctx, ctx.HasMove ? ctx.MoveRange : unit.CurrentSpeedMps * 3f);
                    if (point.HasValue)
                    {
                        float aoo = ctx.Engagers.Sum(CombatMath.Threat) * 0.5f;
                        float dangerThere = IncomingDamage(ctx, unit, point.Value) + aoo;
                        float pHere = Mathf.Exp(-hp / dangerHere);
                        float pThere = dangerThere > 0f ? Mathf.Exp(-hp / dangerThere) : 0f;
                        float value = (pHere - pThere) * AllyWorth(ctx, unit);
                        if (value >= MinMainValue)
                        {
                            list.Add(new Candidate
                            {
                                Kind = CandidateKind.Move, Slot = ActionSlot.Move, Point = point, ApproachRadius = 0.3f,
                                Value = value,
                                Label = "Withdraw (low HP)", Key = "move:withdraw"
                            });
                        }
                    }
                }
            }

            // 3) Nothing to do from here: close the distance for this or the next turn.
            // ★ v0.3.14 (test5): not while only movement is possible (nauseated): arriving, the unit could not attack and
            // would only stand there being hit (Camellia walked up to a Dretch and a Cambion, nauseated, in real time).
            if (!anyAttackNow && !ctx.Permissions.MovementOnly)
            {
                // ★ v0.3.18: the three best enemies to close on, each with its own approach. Only the best one was tried:
                // Seelah ended nine turns idle (0.3.18 log 611fe6bf, R9-R17) because the approach to that enemy, lying
                // in Grease, walked into it, while other enemies stood clear of it.
                var ranked = ctx.Enemies
                    .Where(e => !ctx.Unreachable.Contains(e.UniqueId))
                    .Select(e => new { Enemy = e, Est = CombatMath.EstimateWeaponAttack(unit, e, false) })
                    .Where(x => x.Est != null)
                    .OrderByDescending(x => DamageValue(x.Enemy, x.Est) / Math.Max(3f, ctx.Distance(x.Enemy)))
                    .Take(3)
                    .ToList();

                for (int i = 0; i < ranked.Count; i++)
                {
                    var single = ranked[i];
                    float radius = single.Est.IsRanged ? single.Est.Reach * 0.85f : single.Est.Reach;
                    bool needLos = single.Est.IsRanged;
                    // Casters without a ranged weapon close to spell range, not into melee.
                    if (!single.Est.IsRanged && rangedStyle && ctx.Settings.RangePreference != RangePreference.Melee)
                    {
                        radius = Math.Max(radius, CasterApproachRadius);
                        needLos = true;
                    }
                    // Already there: a zero-length move would just loop. Within reach of the best one: no advance at all.
                    if (InRangeNow(unit, single.Enemy.Position, radius, needLos))
                    {
                        if (i == 0) return;
                        continue;
                    }

                    float value = Math.Max(0.6f, DamageValue(single.Enemy, single.Est) * 0.35f);
                    string label = $"Advance->{single.Enemy.CharacterName}";
                    // ★ v0.3.13: an advance that leaves an enemy's reach provokes (no attack follows, so only the cost).
                    // ★ v0.3.17: to a spot outside a harmful area the enemy stands in, when there is one within reach of it
                    // (PlanApproach): from there the unit attacks next, or catches the enemy as it comes out.
                    var approach = PlanApproach(ctx, single.Enemy, radius, orNearby: true);
                    var risk = approach.Risk;
                    if (risk != null)
                    {
                        value -= risk.Cost;
                        label += risk.Tag;
                    }
                    bool around = approach.Around != null;
                    // ★ v0.3.17 (test3): waiting at the area's edge where the unit already stands is no move. Seelah
                    // "advanced" 0.0 m around Grease twice in a row (log 9a7bb1f9).
                    if (around && Vector3.Distance(unit.Position, approach.Point) < 0.5f) continue;
                    if (around) label += $" (around {approach.Around})";
                    else if (approach.NoSpot != null) label += $" (no spot around: {approach.NoSpot})";
                    list.Add(new Candidate
                    {
                        Kind = CandidateKind.Move, Slot = ActionSlot.Move, Target = single.Enemy,
                        Point = around ? approach.Point : single.Enemy.Position,
                        ApproachRadius = around ? approach.Arrival : radius, NeedLoS = !around && needLos,
                        Value = value,
                        Label = label, Key = $"move:adv:{single.Enemy.UniqueId}",
                        CheckPath = risk?.Area == null
                    });
                }
            }
        }

        /// <summary>
        /// ★ v0.3.14: walk out of the harmful area the unit stands in (docs/STATUS_AWARE_PLAN.md, F3). Moves only priced
        /// walking *into* an area (MoveRisk); staying in one was free, so in turn-based combat nobody ever left a Stinking
        /// Cloud, and a nauseated unit (movement only) ended its turn inside. Valued like entering it - the area's danger
        /// level times half the unit's worth for the next rounds - less the attacks of opportunity of the move, so leaving
        /// competes with acting on the same scale. A melee character that has an enemy in reach steps out where it keeps
        /// one in reach if it can, so it can still attack after leaving.
        /// </summary>
        private static void AddLeaveAreaCandidate(TacticalContext ctx, List<Candidate> list)
        {
            var unit = ctx.Unit;
            var inside = AoeDangerAnalyzer.InsideDanger(unit);
            if (inside.Areas.Count == 0 || inside.Danger <= 0f)
            {
                // A condition can outlast its area (Stinking Cloud's nausea lasts 1d4 rounds after leaving it).
                if (ctx.Permissions.MovementOnly) ctx.LeaveNote = "not inside a harmful area (its effect outlasts it)";
                return;
            }
            float maxDistance = Math.Min(9f, ctx.HasMove ? ctx.MoveRange : unit.CurrentSpeedMps * 3f);
            if (maxDistance < 1f)
            {
                ctx.LeaveNote = $"in {inside.Name ?? "a harmful area"}, no movement left to leave it";
                return;
            }

            Func<Vector3, bool> keepsTarget = null;
            if (!UsesRangedWeapon(unit))
            {
                var inReach = ctx.Enemies
                    .Select(e => new { Enemy = e, Est = CombatMath.EstimateWeaponAttack(unit, e, false) })
                    .Where(x => x.Est != null && !x.Est.IsRanged && InRangeNow(unit, x.Enemy.Position, x.Est.Reach, true))
                    .ToList();
                Vector3 eyeOffset = unit.EyePosition - unit.Position;
                if (inReach.Count > 0)
                    keepsTarget = p => inReach.Any(x => UnitCommand.IsUnitCloseEnough(x.Enemy.Position, p, p + eyeOffset, x.Est.Reach * 0.95f, true, 0f));
            }

            var best = AoeDangerAnalyzer.FindExit(unit, inside, maxDistance, keepsTarget, out _);
            if (!best.HasValue)
            {
                ctx.LeaveNote = $"in {inside.Name ?? "a harmful area"}, no way out within {maxDistance:F1} m";
                return;
            }

            // inside.Danger already counts the chance the area affects this unit (its save, immunities, time left).
            float value = inside.Danger * 0.5f * AllyWorth(ctx, unit);
            string label = $"Leave {inside.Name ?? "area"}";
            var risk = MoveRisk(ctx, best.Value);
            if (risk != null)
            {
                // ★ v0.3.14 (test5): a hit stops the walk, and the unit stays inside having taken it (Camellia, 2 attacks).
                value = value * (1f - risk.StopChance) - risk.Cost;
                label += risk.Tag;
            }
            if (value < MinMainValue)
            {
                ctx.LeaveNote = $"in {inside.Name ?? "a harmful area"}: {label}={value:F1}" +
                                (risk != null ? $" (the walk costs {risk.Cost:F1}, stopped {risk.StopChance:P0})" : "") + " - not worth it";
                return;
            }
            list.Add(new Candidate
            {
                // Close to the spot: it was chosen with the unit's body just outside the area.
                Kind = CandidateKind.Move, Slot = ActionSlot.Move, Point = best, ApproachRadius = 0.15f,
                Value = value,
                Label = label, Key = "move:leave-area"
            });
        }

        /// <summary>Point within <paramref name="distance"/> that maximises distance from enemies, biased towards allies.</summary>
        private static Vector3? FindRetreatPoint(TacticalContext ctx, float distance, Func<Vector3, bool> accept = null)
        {
            var unit = ctx.Unit;
            var allies = ctx.Allies.Where(a => a != unit).ToList();
            Vector3 allyCenter = allies.Count > 0
                ? allies.Aggregate(Vector3.zero, (s, a) => s + a.Position) / allies.Count
                : unit.Position;

            Vector3? best = null;
            float bestScore = float.MinValue;
            for (int i = 0; i < 12; i++)
            {
                float angle = i * 30f * Mathf.Deg2Rad;
                var candidate = unit.Position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance;
                if (accept != null && !accept(candidate)) continue;
                // ★ v0.3.14: never step back into a harmful area.
                if (AoeDangerAnalyzer.EnteringDanger(unit, unit.Position, candidate, out _) > 0f) continue;
                // ★ v0.3.14: real time has no path check before the move (the turn-based dispatcher computes one): a spot
                // off the navmesh or behind a wall left Orvath standing still for 3 s, 19 times in one session.
                if (CombatMemory.IsRealTime && !AoeDangerAnalyzer.IsPositionValid(unit.Position, candidate)) continue;
                float nearestEnemy = ctx.Enemies.Min(e => Vector3.Distance(candidate, e.Position) - e.Corpulence);
                float score = nearestEnemy - 0.15f * Vector3.Distance(candidate, allyCenter);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            float current = ctx.Enemies.Min(e => Vector3.Distance(unit.Position, e.Position) - e.Corpulence);
            return best.HasValue && bestScore > current - 0.15f * Vector3.Distance(unit.Position, allyCenter) + 0.5f ? best : null;
        }

        #endregion

        #region Execute

        /// <summary>
        /// ★ v0.3.14: the order that makes the game stand a lying unit up in turn-based combat (TurnBasedController.StandUp):
        /// a short walk, as a player's click on the ground; the game cancels it as the unit gets up. A spot that is safe to
        /// walk to in case it is carried out after all. <paramref name="cost"/> says what standing up costs, for the log.
        /// </summary>
        public static bool IssueStandUp(UnitEntityData unit, out UnitCommand command, out string cost, out string failure)
        {
            command = null;
            failure = "no spot to walk to";
            int provokes = 0;
            try
            {
                if (!unit.Descriptor.State.Features.GetUpWithoutAttackOfOpportunity)
                    provokes = unit.CombatState.EngagedBy.Count(e => e != null && e.Descriptor.State.IsConscious);
            }
            catch { }
            cost = (unit.HasMoveAction() ? "move action" : "standard action") + (provokes > 0 ? $", provokes up to {provokes}" : "");

            var enemies = new List<UnitEntityData>();
            try { enemies.AddRange(unit.CombatState.EngagedBy.Where(e => e != null)); } catch { }
            int attempts = 0;
            foreach (float ring in new[] { 1.5f, 2.5f })
            {
                var points = Enumerable.Range(0, 12)
                    .Select(i => unit.Position + new Vector3(Mathf.Cos(i * 30f * Mathf.Deg2Rad), 0f, Mathf.Sin(i * 30f * Mathf.Deg2Rad)) * ring)
                    .Where(p => AoeDangerAnalyzer.EnteringDanger(unit, unit.Position, p, out _) <= 0f &&
                                AoeDangerAnalyzer.IsPositionValid(unit.Position, p))
                    // Staying next to whoever engages the unit: if the walk is carried out after all, it provokes nothing.
                    .OrderBy(p => enemies.Count == 0 ? 0f : enemies.Max(e => Vector3.Distance(p, e.Position)));
                foreach (var point in points)
                {
                    if (attempts++ >= 4) return false;
                    var move = new UnitMoveTo(point, 0.3f);
                    if (CommandDispatcher.Run(unit, move, "Stand up", out failure))
                    {
                        command = move;
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// ★ v0.3.15: the ability's effect is one the game always reports when it happens (damage, a heal, a debuff or
        /// control: an attack roll, a save or the buff on the enemy), so "nothing reported" means it did nothing.
        /// Helpful buffs are not judged (re-applying one the unit has raises no event), nor summons, areas,
        /// enchantments, toggles and utility.
        /// </summary>
        private static bool IsVerifiable(AbilityProfile profile)
        {
            if (profile == null || profile.Summons || profile.Enchants || profile.IsToggle || profile.SpawnedAreas.Any()) return false;
            if (profile.Maneuvers.Count > 0 || profile.IsWeaponAttack) return true;   // ★ v0.3.18 (AA-4): its roll is reported (CastOutcome)
            return profile.DealsDamage || profile.Heals.Count > 0 || (profile.TargetBuffs.Count > 0 && profile.HarmfulEffects);
        }

        /// <summary>Builds the game command for a candidate and issues it through CommandDispatcher.</summary>
        public static bool Execute(UnitEntityData unit, Candidate c, out UnitCommand command, out string failure)
        {
            command = null;
            failure = null;
            // ★ v0.3.17: an approach around a harmful area walks to the spot it was priced for, then acts on the next
            // decision (as touch spells do in turn-based). The plain path the game would pick to the target goes through
            // the area.
            if (c.MoveFirst && c.MovePoint.HasValue)
            {
                var walk = new UnitMoveTo(c.MovePoint.Value, c.ApproachRadius > 0f ? c.ApproachRadius : 0.3f);
                if (CommandDispatcher.Run(unit, walk, $"Approach: {c.Label}", out failure, avoidAreas: true))
                {
                    command = walk;
                    c.Kind = CandidateKind.Move;
                    c.Slot = ActionSlot.Move;
                    CastOutcome.Forget(unit);
                    return true;
                }
                return false;
            }
            switch (c.Kind)
            {
                case CandidateKind.Toggle:
                    return ExecuteToggle(unit, c, out command, out failure);
                case CandidateKind.WeaponAttack:
                {
                    ApplyModes(unit, c);   // ★ v0.3.18 (AA-3)
                    var attack = new UnitAttack(c.Target);
                    // The game would otherwise take full/single from the UI smart-cursor mode (TurnController.GetEnabledFullAttack).
                    // ★ v0.3.14: turn-based only. In real time the game makes a full attack whenever movement allows
                    // (UnitAttack.IsAttackFull); forcing a single attack there would throw away iterative attacks.
                    if (!CombatMemory.IsRealTime)
                    {
                        if (c.FullAttack) attack.ForceFullAttack = true;
                        else attack.IsSingleAttack = true;
                    }
                    command = attack;
                    break;
                }
                case CandidateKind.Ability:
                {
                    var wrapper = c.Target != null ? new TargetWrapper(c.Target) : new TargetWrapper(c.Point ?? unit.Position);
                    // ★ v0.3.15: turn-based, a touch spell (Cure/Inflict Wounds, from a scroll or a slot) is cast where the
                    // caster stands and the game then queues the touch itself as a walk with no path - which turn-based
                    // movement cancels (TickApproaching needs a ForcedPath). Camellia's Inflict Light Wounds scroll on an
                    // enemy 5.6 m away: cast, never touched, and the charge was thrown away by her next spell. Walk
                    // first (with our path), cast from touch range on the next decision.
                    // ★ v0.3.16: the walk counts as a move (so a walk that went nowhere is not retried, and one that
                    // arrived does not mark the spell as failed for leaving its standard action unused). A target out of
                    // touch reach by height alone is not offered at all (EvaluateAbility).
                    // The walk goes to the target itself, stopping within reach by the game's own arrival test, so the
                    // dispatcher's path (stairs, around obstacles) ends where the touch can be made.
                    if (c.NeedsMove && c.Target != null && !CombatMemory.IsRealTime && c.Ability.StickyTouch == null &&
                        c.Ability.Blueprint.StickyTouch != null)
                    {
                        var approach = new UnitMoveTo(c.Target.Position, c.Ability.GetApproachDistance(c.Target) * 0.8f);
                        if (CommandDispatcher.Run(unit, approach, $"Approach to touch: {c.Label}", out failure, avoidAreas: c.CheckPath))
                        {
                            command = approach;
                            c.Kind = CandidateKind.Move;
                            c.Slot = ActionSlot.Move;
                            return true;
                        }
                        return false;
                    }
                    command = new UnitUseAbility(c.Ability, wrapper);
                    break;
                }
                case CandidateKind.Move:
                {
                    var move = new UnitMoveTo(c.Point ?? unit.Position, c.ApproachRadius) { NeedLoS = c.NeedLoS };
                    command = move;
                    break;
                }
            }

            if (command == null)
            {
                failure = "no command";
                return false;
            }

            bool issued = CommandDispatcher.Run(unit, command, c.Label, out failure, avoidAreas: c.CheckPath);
            if (issued && c.Kind == CandidateKind.Ability)
            {
                CombatMemory.RecordCast(unit, c.Ability, c.Target);
                var profile = AbilityProfile.Get(c.Ability);
                profile?.DescribeOnce();
                // ★ v0.3.17: also watched: did it do anything to the enemy it was aimed at? Not for lasting areas, whose
                // saves come later, summons, or Charge, whose attack the game makes after the ability ends. Twice nothing
                // on that enemy, it is not aimed there again this fight - unless the ability runs its own logic, whose
                // effect may come later too (logged only).
                bool watch = profile != null && !profile.SpawnedAreas.Any() && !profile.Summons && !profile.IsCharge;
                bool ownLogic = Kingmaker.Blueprints.BlueprintExtenstions.GetComponent<Kingmaker.UnitLogic.Abilities.Components.Base.AbilityCustomLogic>(c.Ability.Blueprint) != null;
                // Blocking also needs effects on the target itself: an ability aimed at an enemy may only buff its caster.
                CastOutcome.Begin(unit, c.Ability, c.Target, command, IsVerifiable(profile), watchTarget: watch,
                                  blockOnNoReach: !ownLogic && profile != null && profile.HasTargetEffects,
                                  area: profile != null && profile.IsAoE, intendedBuffs: profile?.TargetBuffs);
            }
            else if (issued)
            {
                CastOutcome.Forget(unit);   // its attacks must not count as the last ability's effect
            }
            return issued;
        }

        #endregion
    }
}
