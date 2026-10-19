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
    public enum CandidateKind { WeaponAttack, Ability, Move }
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
        public float HpPct;
        public List<UnitEntityData> Engagers = new List<UnitEntityData>();

        public List<UnitEntityData> Enemies = new List<UnitEntityData>();
        public List<UnitEntityData> Allies = new List<UnitEntityData>();   // includes Unit
        /// <summary>★ v0.3.11: enemies no path could reach this turn (TurnMemory.Unreachable).</summary>
        public HashSet<string> Unreachable = new HashSet<string>();
        /// <summary>★ v0.3.9: party members down (unconscious, not dead): only heals are aimed at them.</summary>
        public List<UnitEntityData> DownedAllies = new List<UnitEntityData>();
        public float ExpectedRounds;
        public float Difficulty;
        public bool HasRangedWeapon;
        /// <summary>★ v0.3.14: what the unit's statuses let the game start (Nauseated, Staggered, CantMove).</summary>
        public ActionPermissions Permissions = ActionPermissions.None;
        /// <summary>★ v0.3.14: the unit's notable conditions, for the decision log (UnitStatus.Describe).</summary>
        public string Status = "";
        /// <summary>★ v0.3.14 (test5): enemies the game lists no weapon attack against (diagnostics).</summary>
        public int NoAttackAgainst;

        public float Distance(UnitEntityData other) => Vector3.Distance(Unit.Position, other.Position);
    }

    public static class TacticalEngine
    {
        private const float MinMainValue = 0.5f;
        private const float MinQuickValue = 2f;
        /// <summary>~25 ft: inside Close range for low-level casters, outside most melee reach.</summary>
        private const float CasterApproachRadius = 8f;

        #region Decide

        public static Candidate Decide(UnitEntityData unit, TurnMemory memory) => Decide(unit, memory, preview: false, out _);

        /// <summary>
        /// <paramref name="preview"/>: what the unit would do, without logging the decision (TacticalEngine.ShouldStandUp
        /// asks it as if the unit had just stood up, which spends its move action).
        /// </summary>
        private static Candidate Decide(UnitEntityData unit, TurnMemory memory, bool preview, out TacticalContext built)
        {
            var ctx = BuildContext(unit);
            built = ctx;
            if (ctx != null && preview)
            {
                ctx.HasMove = false;
                ctx.HasFullRound = ctx.CanFullAttack = ctx.CanFiveFoot = false;
                ctx.MoveRange = 0f;
            }
            if (ctx != null) ctx.Unreachable = memory.Unreachable;
            if (ctx == null || ctx.Enemies.Count == 0)
            {
                if (preview) return null;
                if (CombatMemory.IsRealTime) Main.Verbose($"[Tactics] {unit.CharacterName}: no reachable enemies - wait");
                else Main.Log($"[Tactics] {unit.CharacterName}: no reachable enemies - end turn");
                return null;
            }

            var candidates = new List<Candidate>();
            try { AddWeaponCandidates(ctx, candidates); } catch (Exception ex) { Main.Error($"[Tactics] weapon candidates: {ex}"); }
            try { AddAbilityCandidates(ctx, candidates); } catch (Exception ex) { Main.Error($"[Tactics] ability candidates: {ex}"); }
            try { AddMovementCandidates(ctx, candidates); } catch (Exception ex) { Main.Error($"[Tactics] movement candidates: {ex}"); }

            var usable = candidates
                .Where(c => c.Value > 0f && Affordable(ctx, c) && !memory.Failed.Contains(c.Key))
                .OrderByDescending(c => c.Value)
                .ToList();

            // ★ v0.3.7: Progressive spell policy - hold back higher spell levels while a lower one can do the job.
            if (ctx.Settings.SpellPolicy == SpellResourcePolicy.Progressive)
            {
                try { usable = ApplyProgressive(ctx, usable); }
                catch (Exception ex) { Main.Error($"[Progressive] {unit.CharacterName}: {ex.Message} - policy skipped"); }
            }

            try { usable = CoupleOwnAttackMarks(ctx, usable, memory); }
            catch (Exception ex) { Main.Error($"[Tactics] {unit.CharacterName} marks: {ex.Message}"); }

            var quick = usable.FirstOrDefault(c => c.Slot == ActionSlot.Free || c.Slot == ActionSlot.Swift);
            var main = usable.FirstOrDefault(c => c.Slot != ActionSlot.Free && c.Slot != ActionSlot.Swift);

            Candidate chosen = null;
            if (quick != null && quick.Value >= MinQuickValue) chosen = quick;
            else if (main != null && main.Value >= MinMainValue) chosen = main;

            if (preview) return chosen;
            LogDecision(ctx, usable, chosen);
            // ★ v0.3.14 (test5): a turn-based unit with its actions unused and not a single option: say why.
            if (candidates.Count == 0 && ctx.HasStandard && !CombatMemory.IsRealTime)
                Main.Log($"[Tactics] {unit.CharacterName}: no option at all - the game lists no weapon attack against " +
                         $"{ctx.NoAttackAgainst} of {ctx.Enemies.Count} enemies, no ability or move was usable" +
                         (ctx.Permissions.Notes.Length > 0 ? $" ({ctx.Permissions.Notes})" : ""));
            return chosen;
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
                case ActionSlot.Standard: return ctx.HasStandard && (!c.NeedsMove || (ctx.HasMove && !ctx.SurpriseRound && !ctx.NoMovementLeft));
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
            sb.Append(chosen != null ? $" => {chosen.Label}" : realTime ? " => wait" : " => END TURN");
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
            float partyPower = ctx.Allies.Sum(a => 4f + 3f * Math.Max(1, a.Descriptor?.Progression?.CharacterLevel ?? 1));
            float partyLevels = ctx.Allies.Sum(a => (float)Math.Max(1, a.Descriptor?.Progression?.CharacterLevel ?? 1));
            ctx.ExpectedRounds = Mathf.Clamp(1f + enemyHp / Math.Max(10f, partyLevels * 4f), 1f, 6f);
            ctx.Difficulty = Mathf.Clamp(enemyThreat / Math.Max(1f, partyPower), 0.1f, 2f);

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

        private static bool IsCoupDeGrace(AbilityData ability) =>
            string.Equals(ability?.Blueprint?.AssetGuid.ToString(), CoupDeGraceGuid, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// ★ v0.3.9: Coup de Grace was "utility 1.0" (its effect is a custom component); Seelah hit a sleeping wizard for
        /// 9.6 instead. Valued by the rules (CombatMath.EstimateCoupDeGrace): the kill chance takes the whole enemy out,
        /// otherwise the critical damage counts. The game's own target check already requires a helpless target. It is
        /// a full-round action that provokes: other enemies threatening the caster cost their attacks of opportunity.
        /// </summary>
        private static float CoupDeGraceValue(TacticalContext ctx, UnitEntityData target, out string why)
        {
            why = "dmg:cdg";   // "dmg" marks it offensive, like other damaging options
            if (!ctx.Unit.IsEnemy(target)) return 0f;
            if (!CombatMath.EstimateCoupDeGrace(ctx.Unit, target, out float damage, out float kill)) return 0f;
            float value = kill * (Math.Max(1, target.HPLeft) + CombatMath.Threat(target)) + (1f - kill) * DamageValue(target, damage);
            value *= RoleWeight(ctx, "damage");
            value -= ctx.Engagers.Where(e => e != target).Sum(CombatMath.Threat) * 0.35f;
            return Math.Max(0f, value);
        }

        /// <summary>
        /// ★ v0.3.9: damage an ally can expect before its next turn: each enemy that can reach it (a move and an attack,
        /// or any distance for ranged weapons and casters) adds its threat, shared among the party members it could
        /// equally reach. The chance of dropping is then modelled as exp(-HP / danger).
        /// </summary>
        private static float IncomingDamage(TacticalContext ctx, UnitEntityData ally) => IncomingDamage(ctx, ally, ally.Position);

        /// <summary>★ v0.3.10: the same, with the ally standing at <paramref name="at"/> (a withdraw destination).</summary>
        private static float IncomingDamage(TacticalContext ctx, UnitEntityData ally, Vector3 at)
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
        private sealed class Disengage
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
        private static Disengage MoveRisk(TacticalContext ctx, Vector3 destination)
        {
            var risk = DisengageRisk(ctx, destination);
            var unit = ctx.Unit;
            float danger = AoeDangerAnalyzer.EnteringDanger(unit, unit.Position, destination, out string area);
            if (danger > 0f)
            {
                risk = risk ?? new Disengage();
                risk.Area = area ?? "area";
                risk.Cost += danger * 0.5f * AllyWorth(ctx, unit);
            }

            // ★ v0.3.14 (test5): badly hurt, walking to where more enemies can hit it before its next turn: the higher chance
            // to drop there (the withdraw model in reverse). Aeiluvetar at 20 % HP took "Move+Attack" (15.2) into the
            // middle of the fight. Only below half HP, as for withdrawing: a healthy fighter's place is in melee.
            if (ctx.HpPct < 0.5f)
            {
                float hp = Math.Max(1f, unit.HPLeft);
                float exposure = RiskMath.RoundDropChance(hp, IncomingDamage(ctx, unit, destination)) -
                                 RiskMath.RoundDropChance(hp, IncomingDamage(ctx, unit, unit.Position));
                if (exposure > 0.02f)
                {
                    risk = risk ?? new Disengage();
                    risk.Exposed = true;
                    risk.Cost += exposure * AllyWorth(ctx, unit);
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
        private static Disengage DisengageRisk(TacticalContext ctx, Vector3 destination)
        {
            var unit = ctx.Unit;
            // A move that fits in the 5-foot step is made as one (CommandDispatcher.ApplyTurnBasedMovementLimit).
            if (ctx.CanFiveFoot && Vector3.Distance(unit.Position, destination) <= ctx.FiveFootRange * 0.9f) return null;
            var attackers = PathAttackers(ctx, unit.Position, destination);
            if (attackers.Count == 0) return null;

            float hp = Math.Max(1f, unit.HPLeft);
            PriceAttacks(unit, attackers, hp, out float damage, out float stopChance, out float dropNow);

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
                Cost = dropNow * worth + (1f - dropNow) * Math.Max(0f, after - before) * worth + damage * 0.3f
            };
        }

        /// <summary>
        /// ★ v0.3.14 (test5): one attack of opportunity from each of <paramref name="attackers"/> on a unit with
        /// <paramref name="hp"/> HP: their expected damage, the chance at least one hits (a hit stops a walk) and the
        /// chance they drop it (RiskMath: the chance one blow takes the unit out, not only its average).
        /// </summary>
        private static void PriceAttacks(UnitEntityData unit, List<UnitEntityData> attackers, float hp,
                                         out float damage, out float hitChance, out float dropChance)
        {
            damage = 0f;
            float noHit = 1f, noDrop = 1f;
            foreach (var enemy in attackers)
            {
                var est = CombatMath.EstimateWeaponAttack(enemy, unit, full: false);
                float hit = est?.FirstHitChance ?? 0.5f;
                float expected = est?.ExpectedDamage ?? CombatMath.Threat(enemy) * 0.35f;
                float perHit = est != null && est.FirstPerHit > 0f ? est.FirstPerHit : expected / Math.Max(0.05f, hit);
                noDrop *= 1f - RiskMath.AttackDropChance(hit, est?.FirstCritChance ?? 0.05f * hit, perHit,
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
        private static bool AttacksOnLeaving(UnitEntityData enemy, UnitEntityData unit)
        {
            var combat = enemy.CombatState;
            bool attacks = combat.ShouldAttackOnDisengage(unit, simulate: true);
            if (!attacks && CombatMemory.IsRealTime)
                attacks = combat.EngagedUnits.Count() <= 1 || combat.LastTarget == unit;
            return attacks && combat.AttackOfOpportunity(unit, tricksterAttack: false, simulate: true);
        }

        /// <summary>★ v0.3.13: where a move to act on <paramref name="target"/> from <paramref name="range"/> ends.</summary>
        private static Vector3 ApproachPoint(UnitEntityData unit, UnitEntityData target, float range)
        {
            Vector3 away = unit.Position - target.Position;
            away.y = 0f;
            if (away.sqrMagnitude < 0.0001f) return unit.Position;
            return target.Position + away.normalized * Math.Max(0.1f, Math.Min(away.magnitude, range * 0.9f));
        }

        /// <summary>★ v0.3.9: what keeping an ally in the fight is worth: its damage-equivalent output for up to 3 rounds.</summary>
        private static float AllyWorth(TacticalContext ctx, UnitEntityData ally)
        {
            int level = Math.Max(1, ally.Descriptor?.Progression?.CharacterLevel ?? 1);
            float worth = (4f + 3f * level) * Math.Min(Math.Max(1f, ctx.ExpectedRounds), 3f);
            // ★ v0.3.11: a summon is temporary and replaceable: half (the summoned Cambion's heal outscored Camellia's own).
            if (IsSummon(ally)) worth *= 0.5f;
            return worth;
        }

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

        /// <summary>Damage dealt to an enemy, capped at its HP, plus the value of removing it from the fight.</summary>
        private static float DamageValue(UnitEntityData enemy, float expectedDamage)
        {
            if (expectedDamage <= 0f) return 0f;
            float hp = Math.Max(1, enemy.HPLeft);
            float threat = CombatMath.Threat(enemy);
            float value = Math.Min(expectedDamage, hp);
            if (expectedDamage >= hp) value += threat;              // likely kill: its future damage is prevented
            else value += threat * 0.25f * (expectedDamage / hp);   // progress towards a kill, weighted by threat
            return value;
        }

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

        private static void AddWeaponCandidates(TacticalContext ctx, List<Candidate> list)
        {
            var unit = ctx.Unit;
            // ★ v0.3.14: real time lists them during the action cooldown too (not affordable, so never chosen then): the
            // movement step needs to know an attack is possible from here once the cooldown ends.
            if (!ctx.HasStandard && !ctx.CanFullAttack && !CombatMemory.IsRealTime) return;

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

                float weight = RoleWeight(ctx, "damage") * PreferenceWeight(ctx, single.IsRanged);
                if (ctx.Settings.Role == AIRole.Tank && enemy.CombatState.EngagedUnits.Any(u => u != unit && !unit.IsEnemy(u)))
                    weight *= 1.25f;

                float aooPenalty = 0f;
                if (single.IsRanged && ctx.Engagers.Count > 0)
                    aooPenalty = ctx.Engagers.Sum(CombatMath.Threat) * 0.35f;

                if (ctx.CanFullAttack && (inReach || fiveFootReach))
                {
                    var full = CombatMath.EstimateWeaponAttack(unit, enemy, full: true);
                    if (full != null && full.Attacks > 1)
                    {
                        list.Add(new Candidate
                        {
                            Kind = CandidateKind.WeaponAttack,
                            Offensive = true,
                            Slot = ActionSlot.FullRound,
                            Target = enemy,
                            FullAttack = true,
                            Value = DamageValue(enemy, full.ExpectedDamage) * weight * (fiveFootReach ? 0.95f : 1f) *
                                    WakeFactor(enemy, full.ExpectedDamage) - aooPenalty,
                            Label = $"FullAttack({full.Attacks})->{enemy.CharacterName}{(fiveFootReach ? "+5ft" : "")}",
                            Key = $"full:{enemy.UniqueId}"
                        });
                    }
                }

                if (!ctx.HasStandard) continue;

                if (inReach)
                {
                    list.Add(new Candidate
                    {
                        Kind = CandidateKind.WeaponAttack,
                        Offensive = true,
                        Slot = ActionSlot.Standard,
                        Target = enemy,
                        Value = DamageValue(enemy, single.ExpectedDamage) * weight * WakeFactor(enemy, single.ExpectedDamage) - aooPenalty,
                        Label = $"Attack->{enemy.CharacterName}",
                        Key = $"atk:{enemy.UniqueId}"
                    });
                }
                else if (ctx.HasMove && dist - reach <= ctx.MoveRange * 0.95f)
                {
                    float value = DamageValue(enemy, single.ExpectedDamage) * weight * 0.9f * WakeFactor(enemy, single.ExpectedDamage);
                    string label = $"Move+Attack->{enemy.CharacterName}";
                    // ★ v0.3.13: walking out of an enemy's reach provokes (melee and ranged alike).
                    var risk = MoveRisk(ctx, ApproachPoint(unit, enemy, reach));
                    if (risk != null)
                    {
                        value = value * (1f - risk.StopChance) - risk.Cost;
                        label += risk.Tag;
                    }
                    list.Add(new Candidate
                    {
                        Kind = CandidateKind.WeaponAttack,
                        Offensive = true,
                        Slot = ActionSlot.Standard,
                        NeedsMove = true,
                        Target = enemy,
                        Value = value,
                        Label = label,
                        Key = $"matk:{enemy.UniqueId}"
                    });
                }
            }
        }

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
                    if (AbilityClassifier.IsBlacklistedForCombat(ability)) continue;

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
            bool harmfulOnly = (profile.HarmfulEffects || profile.DealsDamage) && !profile.HelpfulToAllies && profile.Heals.Count == 0;

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

            Candidate best = null, bestViable = null, bestBoss = null;
            foreach (var target in targets.Distinct())
            {
                if (onlyTarget != null && target != onlyTarget) continue;
                var wrapper = AbilityProfile.TargetOn(ability, target);
                if (wrapper == null) continue;

                bool needsMove = false;
                Disengage risk = null;
                if (target != ctx.Unit)
                {
                    float range = ability.GetApproachDistance(wrapper.Unit);
                    bool needLos = new UnitUseAbility(ability, wrapper).NeedLoS;
                    if (!InRangeNow(ctx.Unit, target.Position, range, needLos))
                    {
                        if (slot != ActionSlot.Standard || !ctx.HasMove || ctx.Distance(target) - range > ctx.MoveRange * 0.95f)
                            continue;
                        needsMove = true;
                        // ★ v0.3.13: walking up to cast can provoke too.
                        risk = MoveRisk(ctx, ApproachPoint(ctx.Unit, target, range));
                    }
                }

                float value = ScoreAbility(ctx, ability, profile, slot, target, out string why);
                if (value <= 0f) continue;
                value *= consumable;
                if (needsMove) value *= 0.9f;
                // ★ v0.3.6: a standard-action spell cast in melee needs a defensive-casting check; failing it provokes an
                // attack of opportunity and can lose the spell (UnitUseAbility.OnTick). Camellia's Cure Light Wounds ended
                // "Fail" that way. Potions and wands do not provoke (AbilityData.ProvokeAttackOfOpportunity).
                if (slot == ActionSlot.Standard && ctx.Engagers.Count > 0 && !needsMove && ability.ProvokeAttackOfOpportunity)
                    value *= 0.85f;
                // ★ v0.3.14: entangled (or "casting is difficult"): a concentration check before the spell even starts.
                value *= CombatMath.CastingConcentrationChance(ctx.Unit, ability);
                // ★ v0.3.1: an archer walking up to an enemy to touch it gives up its ranged attacks and invites
                // attacks of opportunity (Lann walked 9 m for Aid Another).
                if (needsMove && ctx.HasRangedWeapon && ability.Range == AbilityRange.Touch && ctx.Unit.IsEnemy(target))
                    value *= 0.5f;
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
                    Label = $"{(ability.SourceItem != null ? "Item:" : "")}{ability.Name}{metamagic}->{target.CharacterName}({why}){risk?.Tag}",
                    Key = $"abl:{CombatMemory.AbilityKey(ability)}{metamagic}:{target.UniqueId}"
                };
                if (best == null || value > best.Value) best = candidate;
                if (!progressive || !candidate.Offensive) continue;

                candidate.SuccessChance = ProgressiveGate.SuccessChance(ctx.Unit, ctx.Enemies, ability, profile, candidate.Target, candidate.Point);
                float threshold = slot == ActionSlot.Free || slot == ActionSlot.Swift ? MinQuickValue : MinMainValue;
                if (candidate.SuccessChance >= SpellTierPolicy.MinSuccessChance && value >= threshold &&
                    (bestViable == null || value > bestViable.Value))
                    bestViable = candidate;
                if (bosses.Count > 0 && ThreatAssessment.HitsBoss(candidate.Target, candidate.Point, profile, bosses) &&
                    (bestBoss == null || value > bestBoss.Value))
                    bestBoss = candidate;
            }
            return new[] { best, bestViable, bestBoss }.Where(c => c != null).Distinct();
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
                if (token == "dmg" || token == "cc" || token == "debuff") return true;
            }
            return false;
        }

        private static string MetamagicTag(AbilityData ability) => AbilityDiscovery.MetamagicTag(ability);

        private static Candidate EvaluateCharge(TacticalContext ctx, AbilityData charge)
        {
            if (!ctx.CanFullAttack) return null;
            Candidate best = null;
            foreach (var enemy in ctx.Enemies)
            {
                if (!charge.CanTarget(new TargetWrapper(enemy))) continue;
                var single = CombatMath.EstimateWeaponAttack(ctx.Unit, enemy, full: false);
                if (single == null || InRangeNow(ctx.Unit, enemy.Position, single.Reach, true)) continue;
                // Only worth it when a normal move cannot get us there this turn.
                if (ctx.Distance(enemy) - single.Reach <= ctx.MoveRange * 0.95f) continue;

                float value = DamageValue(enemy, single.ExpectedDamage * 1.1f) * RoleWeight(ctx, "damage") *
                              PreferenceWeight(ctx, false) * 0.95f;
                string label = $"Charge->{enemy.CharacterName}";
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
            var reasons = new List<string>();

            if (IsCoupDeGrace(ability))
                return CoupDeGraceValue(ctx, target, out why);

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
                foreach (var enemy in affectedEnemies)
                {
                    if (CombatRulesAnalyzer.AnalyzeResistance(unit, enemy, ability).IsCompletelyIneffective) continue;
                    float dmg = CombatMath.EstimateAbilityDamage(ability, profile, enemy);
                    float p = CombatMath.EffectChance(unit, enemy, ability, profile, damage: true);
                    dmgValue += DamageValue(enemy, dmg * p) * WakeFactor(enemy, dmg * p);
                }

                // Friendly fire: hurting an ally costs twice what the same damage on an enemy is worth.
                // ★ v0.3.7: Selective metamagic spares allies. ★ v0.3.14: only where the game's rule says so (not in
                // lasting areas with Tabletop Tweaks): AbilityProfile.SelectiveSparesAllies.
                if (affectedAllies.Count > 0 && profile.IsAoE && !profile.SelectiveSparesAllies(ability))
                {
                    float dmg = CombatMath.EstimateAbilityDamage(ability, profile, affectedAllies[0]);
                    float allyCost = affectedAllies.Sum(a => Math.Min(dmg, a.HPLeft)) * 2f;
                    if (ctx.Settings.AvoidFriendlyFire && allyCost > 0f) { why = "friendly fire"; return 0f; }
                    dmgValue -= allyCost;
                }

                if (dmgValue > 0f)
                {
                    value += dmgValue * RoleWeight(ctx, "damage");
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

                    float urgency = hp <= 0.25f ? 3f : hp < 0.5f ? 2f : 1f;
                    bool threatened = ally.CombatState?.IsEngaged ?? false;
                    healValue += heal * 0.6f * urgency * (threatened ? 1.25f : 1f);

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
                    profile.TargetBuffs.Any(b => profile.BuffAppliesTo(b, ability, a) != EffectApplies.No && !a.Buffs.HasFact(b)) &&
                    !(profile.Control != CCType.None && AbilityClassifier.HasCCImmunity(a, profile.Control));
                var caughtAllies = !harmfulArea ? new List<UnitEntityData>() : affectedAllies.Where(wouldBeCaught).ToList();
                if (caughtAllies.Count > 0)
                {
                    if (ctx.Settings.AvoidFriendlyFire) { why = "friendly fire"; return 0f; }
                    allyControlCost = caughtAllies.Sum(a => (4f + 3f * Math.Max(1, a.Descriptor?.Progression?.CharacterLevel ?? 1)) *
                                                           strength * effectRounds);
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
                        .Sum(a => 0.5f * (4f + 3f * Math.Max(1, a.Descriptor?.Progression?.CharacterLevel ?? 1)) * strength * effectRounds);
                }
                foreach (var enemy in affectedEnemies)
                {
                    var buffs = profile.TargetBuffs.Where(b => profile.BuffAppliesTo(b, ability, enemy) != EffectApplies.No).ToList();
                    if (buffs.Count == 0) continue;
                    if (buffs.Any(b => enemy.Buffs.HasFact(b))) continue;
                    // ★ v0.3.13: already inside an active copy of this area from our side.
                    if (profile.IsAoE && profile.InOwnSideArea(unit, enemy)) continue;
                    if (profile.Control != CCType.None && AbilityClassifier.HasCCImmunity(enemy, profile.Control)) continue;
                    if (CombatRulesAnalyzer.AnalyzeResistance(unit, enemy, ability).IsCompletelyIneffective) continue;
                    float p = CombatMath.EffectChance(unit, enemy, ability, profile, damage: false);
                    float enemyValue = p * CombatMath.Threat(enemy) * strength * effectRounds;
                    // ★ v0.3.9: an enemy that already cannot act (asleep, paralysed, stunned) gains little from more
                    // control: Orvath stunned (Grasp Heart) the Mite Scout he had just put to sleep.
                    if (CombatMath.IsDisabled(enemy)) enemyValue *= 0.1f;
                    // A one-round mark only pays off when someone else in the party is fighting that enemy now.
                    if (oneShotMark && !(enemy.CombatState?.EngagedUnits?.Any(u => u != unit && !unit.IsEnemy(u)) ?? false))
                        enemyValue *= 0.5f;
                    controlValue += enemyValue;
                }
                controlValue -= allyControlCost;
                if (controlValue > 0f)
                {
                    value += controlValue;
                    reasons.Add(profile.ControlStrength == ControlStrength.Hard ? "cc" : isMark ? "mark" : "debuff");
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
                    float perRound = 1f + level * 1.5f;
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
                bool usedOnTarget = CombatMemory.LastCastRound(unit, ability, target).HasValue;
                if (!usedOnTarget)
                {
                    if (profile.Summons)
                    {
                        value += (3f + 3f * level) * Math.Min(ctx.ExpectedRounds, 4f) * 0.5f * (ctx.Enemies.Count >= 2 ? 1f : 0.6f);
                        reasons.Add("summon");
                    }
                    else if (profile.Enchants)
                    {
                        // ★ v0.3.8: the fallback is for effects we cannot see. An enchantment that is still in place
                        // (its item enchantment, or the buff that carries it) is visible: nothing to gain. Camellia
                        // recast Spirit Weapon Enchantment at the start of four fights while it was active.
                        if (profile.EnchantmentActive(unit, target)) { why = "already active"; return 0f; }
                        value += (1f + level * 1.5f) * ctx.ExpectedRounds * 0.5f * RoleWeight(ctx, "buff");
                        reasons.Add("enchant");
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
                    else if (CombatMemory.TimesUsed(unit, ability) == 0)
                    {
                        if (!profile.HasDetectableEffect) profile.DescribeStructureOnce();
                        value += 1f; // utility (e.g. Appraise Foe): only when nothing better exists
                        reasons.Add("utility");
                    }
                }
            }

            // Spell slots are a resource: in easy fights prefer weapons/cantrips over levelled spells.
            if (level > 0 && ability.Spellbook != null && ability.SourceItem == null)
                value *= Mathf.Clamp(0.55f + 0.45f * ctx.Difficulty, 0.55f, 1f);

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
            bool castsInMelee = bestMain != null && bestMain.Kind == CandidateKind.Ability && bestMain.Slot == ActionSlot.Standard &&
                                bestMain.Ability.ProvokeAttackOfOpportunity &&
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
                if (castsInMelee)
                    followUp = bestMain.Value / 0.85f;                    // the spell without the in-melee discount
                else if (ctx.HasRangedWeapon)
                    followUp = ctx.Enemies.Select(e =>
                    {
                        var est = CombatMath.EstimateWeaponAttack(unit, e, full: ctx.CanFullAttack);
                        return est != null && est.IsRanged ? DamageValue(e, est.ExpectedDamage) * RoleWeight(ctx, "damage") : 0f;
                    }).DefaultIfEmpty(0f).Max();
                else
                    followUp = bestMain?.Value ?? 0f;
                float stepDistance = realTime ? 4f : ctx.FiveFootRange * 0.9f;
                var point = followUp >= MinQuickValue ? FindRetreatPoint(ctx, stepDistance, keepsTarget) : null;
                if (point.HasValue)
                {
                    float value = Math.Min(ctx.Engagers.Sum(CombatMath.Threat) * 0.4f, followUp);
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
            if (ctx.HpPct < 0.5f)
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
                var single = ctx.Enemies
                    .Where(e => !ctx.Unreachable.Contains(e.UniqueId))
                    .Select(e => new { Enemy = e, Est = CombatMath.EstimateWeaponAttack(unit, e, false) })
                    .Where(x => x.Est != null)
                    .OrderByDescending(x => DamageValue(x.Enemy, x.Est.ExpectedDamage) / Math.Max(3f, ctx.Distance(x.Enemy)))
                    .FirstOrDefault();

                if (single != null)
                {
                    float radius = single.Est.IsRanged ? single.Est.Reach * 0.85f : single.Est.Reach;
                    bool needLos = single.Est.IsRanged;
                    // Casters without a ranged weapon close to spell range, not into melee.
                    if (!single.Est.IsRanged && rangedStyle && ctx.Settings.RangePreference != RangePreference.Melee)
                    {
                        radius = Math.Max(radius, CasterApproachRadius);
                        needLos = true;
                    }
                    // Already there: a zero-length move would just loop.
                    if (InRangeNow(unit, single.Enemy.Position, radius, needLos)) return;

                    float value = Math.Max(0.6f, DamageValue(single.Enemy, single.Est.ExpectedDamage) * 0.35f);
                    string label = $"Advance->{single.Enemy.CharacterName}";
                    // ★ v0.3.13: an advance that leaves an enemy's reach provokes (no attack follows, so only the cost).
                    var risk = MoveRisk(ctx, ApproachPoint(unit, single.Enemy, radius));
                    if (risk != null)
                    {
                        value -= risk.Cost;
                        label += risk.Tag;
                    }
                    list.Add(new Candidate
                    {
                        Kind = CandidateKind.Move, Slot = ActionSlot.Move, Target = single.Enemy, Point = single.Enemy.Position,
                        ApproachRadius = radius, NeedLoS = needLos,
                        Value = value,
                        Label = label, Key = $"move:adv:{single.Enemy.UniqueId}"
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
            if (inside.Areas.Count == 0 || inside.Danger <= 0f) return;
            float maxDistance = Math.Min(9f, ctx.HasMove ? ctx.MoveRange : unit.CurrentSpeedMps * 3f);
            if (maxDistance < 1f) return;

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
            if (!best.HasValue) return;

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
            if (value < MinMainValue) return;
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

        /// <summary>Builds the game command for a candidate and issues it through CommandDispatcher.</summary>
        public static bool Execute(UnitEntityData unit, Candidate c, out UnitCommand command, out string failure)
        {
            command = null;
            failure = null;
            switch (c.Kind)
            {
                case CandidateKind.WeaponAttack:
                {
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

            bool issued = CommandDispatcher.Run(unit, command, c.Label, out failure);
            if (issued && c.Kind == CandidateKind.Ability)
            {
                CombatMemory.RecordCast(unit, c.Ability, c.Target);
                AbilityProfile.Get(c.Ability)?.DescribeOnce();
            }
            return issued;
        }

        #endregion
    }
}
