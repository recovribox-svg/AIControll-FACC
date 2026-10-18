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

        public float Distance(UnitEntityData other) => Vector3.Distance(Unit.Position, other.Position);
    }

    public static class TacticalEngine
    {
        private const float MinMainValue = 0.5f;
        private const float MinQuickValue = 2f;
        /// <summary>~25 ft: inside Close range for low-level casters, outside most melee reach.</summary>
        private const float CasterApproachRadius = 8f;

        #region Decide

        public static Candidate Decide(UnitEntityData unit, TurnMemory memory)
        {
            var ctx = BuildContext(unit);
            if (ctx != null) ctx.Unreachable = memory.Unreachable;
            if (ctx == null || ctx.Enemies.Count == 0)
            {
                Main.Log($"[Tactics] {unit.CharacterName}: no reachable enemies - end turn");
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

            LogDecision(ctx, usable, chosen);
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
            sb.Append($"[Tactics] {ctx.Unit.CharacterName} R{ctx.Round} HP{ctx.HpPct * 100:F0}% ");
            sb.Append($"[{(ctx.HasStandard ? "S" : "-")}{(ctx.HasMove ? "M" : "-")}{(ctx.HasSwift ? "W" : "-")}{(ctx.CanFullAttack ? "F" : "-")}{(ctx.CanFiveFoot ? "5" : "-")}]{(ctx.SurpriseRound ? " surprise" : "")}{(ctx.NoMovementLeft ? " no-move" : ctx.StepTaken ? " stepped" : "")} ");
            sb.Append($"enemies={ctx.Enemies.Count} rounds~{ctx.ExpectedRounds:F1} diff={ctx.Difficulty:F2} :: ");
            sb.Append(string.Join(" | ", usable.Take(5).Select(c => c.ToString())));
            sb.Append(chosen != null ? $" => {chosen.Label}" : " => END TURN");
            Main.Log(sb.ToString());
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
            else
            {
                ctx.MoveRange = ctx.HasMove ? unit.CurrentSpeedMps * 3f : 0f;
            }

            try { ctx.Engagers.AddRange(unit.CombatState.EngagedBy.Where(e => e != null && e.HPLeft > 0)); } catch { }

            foreach (var other in Game.Instance.State.Units)
            {
                if (other == null || other.Descriptor?.State == null) continue;
                if (other.Descriptor.State.IsDead) continue;
                if (other.HPLeft <= 0 || other.Descriptor.State.IsUnconscious)
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

        /// <summary>★ v0.3.9: what keeping an ally in the fight is worth: its damage-equivalent output for up to 3 rounds.</summary>
        private static float AllyWorth(TacticalContext ctx, UnitEntityData ally)
        {
            int level = Math.Max(1, ally.Descriptor?.Progression?.CharacterLevel ?? 1);
            float worth = (4f + 3f * level) * Math.Min(Math.Max(1f, ctx.ExpectedRounds), 3f);
            // ★ v0.3.11: a summon is temporary and replaceable: half (the summoned Cambion's heal outscored Camellia's own).
            if (IsSummon(ally)) worth *= 0.5f;
            return worth;
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
            if (!ctx.HasStandard && !ctx.CanFullAttack) return;

            foreach (var enemy in ctx.Enemies)
            {
                var single = CombatMath.EstimateWeaponAttack(unit, enemy, full: false);
                if (single == null) return; // unit cannot make weapon attacks at all

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
                    list.Add(new Candidate
                    {
                        Kind = CandidateKind.WeaponAttack,
                        Offensive = true,
                        Slot = ActionSlot.Standard,
                        NeedsMove = true,
                        Target = enemy,
                        // ranged units shooting from a new spot leave melee, so no AoO penalty after moving
                        Value = DamageValue(enemy, single.ExpectedDamage) * weight * 0.9f * WakeFactor(enemy, single.ExpectedDamage),
                        Label = $"Move+Attack->{enemy.CharacterName}",
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

        private static IEnumerable<AbilityData> GatherAbilities(UnitEntityData unit)
        {
            var seen = new HashSet<string>();
            var result = new List<AbilityData>();

            void Add(AbilityData a)
            {
                if (a?.Blueprint == null) return;
                // Variant parents cannot be cast ("Can't cast spell with variants"): use their variants instead.
                if (a.Blueprint.HasVariants)
                {
                    try
                    {
                        foreach (var conv in a.GetConversions().ToList())
                            if (conv?.Blueprint != null && a.Blueprint.HasVariant(conv.Blueprint)) Add(conv);
                    }
                    catch (Exception ex)
                    {
                        Main.Verbose($"[Tactics] variants of {a.Name}: {ex.Message}");
                    }
                    return;
                }
                string key = $"{a.Blueprint.AssetGuid}|{a.Spellbook?.Blueprint?.AssetGuid}|{a.SourceItem?.UniqueId}|{MetamagicTag(a)}";
                if (seen.Add(key)) result.Add(a);
            }

            foreach (var ability in unit.Abilities.Enumerable)
                Add(ability?.Data);

            foreach (var book in unit.Spellbooks ?? Enumerable.Empty<Kingmaker.UnitLogic.Spellbook>())
            {
                // ★ v0.3.12: a failing spell list is reported (once per book and list) instead of silently skipped.
                string bookId = book?.Blueprint?.AssetGuid.ToString();
                try { foreach (var s in book.GetAllKnownSpells()) Add(s); }
                catch (Exception ex) { ReportOnce($"book|{bookId}|known", $"[Tactics] {book?.Blueprint?.name}: known spells unreadable - {ex.Message}"); }
                try { foreach (var s in book.GetAllCustomSpells()) Add(s); }
                catch (Exception ex) { ReportOnce($"book|{bookId}|custom", $"[Tactics] {book?.Blueprint?.name}: metamagic spells unreadable - {ex.Message}"); }
                for (int level = 0; level <= 10; level++)
                {
                    try { foreach (var s in book.GetSpecialSpells(level)) Add(s); }
                    catch (Exception ex) { ReportOnce($"book|{bookId}|special{level}", $"[Tactics] {book?.Blueprint?.name}: special spells L{level} unreadable - {ex.Message}"); }
                }
            }

            return result;
        }

        private static void AddAbilityCandidates(TacticalContext ctx, List<Candidate> list)
        {
            foreach (var ability in GatherAbilities(ctx.Unit))
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

        private static readonly HashSet<string> Reported = new HashSet<string>();

        /// <summary>★ v0.3.12: one normal log line per key (bounded), for failures that remove an option silently.</summary>
        private static void ReportOnce(string key, string message)
        {
            if (Reported.Count > 1024) Reported.Clear();
            if (Reported.Add(key)) Main.Log(message);
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
                TargetWrapper wrapper = null;
                if (ability.CanTarget(new TargetWrapper(target))) wrapper = new TargetWrapper(target);
                else if (ability.CanTargetPoint && ability.CanTarget(new TargetWrapper(target.Position))) wrapper = new TargetWrapper(target.Position);
                if (wrapper == null) continue;

                bool needsMove = false;
                if (target != ctx.Unit)
                {
                    float range = ability.GetApproachDistance(wrapper.Unit);
                    bool needLos = new UnitUseAbility(ability, wrapper).NeedLoS;
                    if (!InRangeNow(ctx.Unit, target.Position, range, needLos))
                    {
                        if (slot != ActionSlot.Standard || !ctx.HasMove || ctx.Distance(target) - range > ctx.MoveRange * 0.95f)
                            continue;
                        needsMove = true;
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
                // ★ v0.3.1: an archer walking up to an enemy to touch it gives up its ranged attacks and invites
                // attacks of opportunity (Lann walked 9 m for Aid Another).
                if (needsMove && ctx.HasRangedWeapon && ability.Range == AbilityRange.Touch && ctx.Unit.IsEnemy(target))
                    value *= 0.5f;

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
                    Label = $"{(ability.SourceItem != null ? "Item:" : "")}{ability.Name}{metamagic}->{target.CharacterName}({why})",
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

        /// <summary>★ v0.3.7: metamagic and heightening make a different spell (cost, slot level): keep them apart.</summary>
        private static string MetamagicTag(AbilityData ability)
        {
            var data = ability?.MetamagicData;
            if (data == null || !data.NotEmpty && data.HeightenLevel == 0) return "";
            return $"[{data.MetamagicMask}{(data.HeightenLevel > 0 ? "+H" + data.HeightenLevel : "")}]";
        }

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
                if (best == null || value > best.Value)
                    best = new Candidate
                    {
                        Kind = CandidateKind.Ability, Offensive = true, Slot = ActionSlot.FullRound, Ability = charge, Target = enemy,
                        Value = value, Label = $"Charge->{enemy.CharacterName}", Key = $"charge:{enemy.UniqueId}"
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
                // ★ v0.3.7: Selective metamagic spares allies (the game skips them for every area type).
                if (affectedAllies.Count > 0 && profile.IsAoE && !ability.HasMetamagic(Kingmaker.UnitLogic.Abilities.Metamagic.Selective))
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
                    if (ally.HPLeft <= 0)
                    {
                        float needed = 1f - ally.HPLeft;
                        float upChance = heal >= needed ? 1f : 0.3f * heal / needed;
                        healValue += upChance * AllyWorth(ctx, ally);
                        continue;
                    }

                    float hp = CombatMath.HpPercent(ally);
                    float threshold = ability.SourceItem != null && ally == unit
                        ? (ctx.Global?.PotionHealThresholdPercent ?? 50) / 100f
                        : Math.Max(0.35f, ctx.Settings.HealAtHPPercent / 100f + 0.2f);
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
                // checks), immunity and an already-present copy; Selective metamagic spares all allies.
                float allyControlCost = 0f;
                bool harmfulArea = profile.IsAoE && profile.AoETargets != Kingmaker.UnitLogic.Abilities.Components.TargetType.Enemy &&
                                   (profile.HarmfulToEnemies || profile.ControlStrength != ControlStrength.None) &&
                                   !ability.HasMetamagic(Kingmaker.UnitLogic.Abilities.Metamagic.Selective);
                var caughtAllies = !harmfulArea ? new List<UnitEntityData>() : affectedAllies.Where(a =>
                    profile.TargetBuffs.Any(b => profile.BuffAppliesTo(b, ability, a) != EffectApplies.No && !a.Buffs.HasFact(b)) &&
                    !(profile.Control != CCType.None && AbilityClassifier.HasCCImmunity(a, profile.Control))).ToList();
                if (caughtAllies.Count > 0)
                {
                    if (ctx.Settings.AvoidFriendlyFire) { why = "friendly fire"; return 0f; }
                    allyControlCost = caughtAllies.Sum(a => (4f + 3f * Math.Max(1, a.Descriptor?.Progression?.CharacterLevel ?? 1)) *
                                                           strength * effectRounds);
                }
                foreach (var enemy in affectedEnemies)
                {
                    var buffs = profile.TargetBuffs.Where(b => profile.BuffAppliesTo(b, ability, enemy) != EffectApplies.No).ToList();
                    if (buffs.Count == 0) continue;
                    if (buffs.Any(b => enemy.Buffs.HasFact(b))) continue;
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
            if (!unit.Descriptor.State.CanMove) return;
            if (ctx.NoMovementLeft || ctx.StepTaken) return;

            // "Can I already hurt someone from here with what I have left this turn?"
            bool anyAttackNow = list.Any(c => c.Offensive && !c.NeedsMove && c.Value >= MinMainValue &&
                                             c.Slot != ActionSlot.Free && c.Slot != ActionSlot.Swift && Affordable(ctx, c));
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
            if ((rangedStyle || castsInMelee) && ctx.Engagers.Count > 0 && ctx.CanFiveFoot && (ctx.HasStandard || ctx.CanFullAttack))
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
                var point = FindRetreatPoint(ctx, ctx.FiveFootRange * 0.9f, keepsTarget);
                if (point.HasValue)
                {
                    list.Add(new Candidate
                    {
                        Kind = CandidateKind.Move, Slot = ActionSlot.Free, Point = point, ApproachRadius = 0.3f,
                        Value = ctx.Engagers.Sum(CombatMath.Threat) * 0.4f,
                        Label = "5ft step out of melee", Key = "move:5ft"
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
            if (!anyAttackNow)
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

                    list.Add(new Candidate
                    {
                        Kind = CandidateKind.Move, Slot = ActionSlot.Move, Target = single.Enemy, Point = single.Enemy.Position,
                        ApproachRadius = radius, NeedLoS = needLos,
                        Value = Math.Max(0.6f, DamageValue(single.Enemy, single.Est.ExpectedDamage) * 0.35f),
                        Label = $"Advance->{single.Enemy.CharacterName}", Key = $"move:adv:{single.Enemy.UniqueId}"
                    });
                }
            }
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
                    if (c.FullAttack) attack.ForceFullAttack = true;
                    else attack.IsSingleAttack = true;
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
