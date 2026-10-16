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
        public float HpPct;
        public List<UnitEntityData> Engagers = new List<UnitEntityData>();

        public List<UnitEntityData> Enemies = new List<UnitEntityData>();
        public List<UnitEntityData> Allies = new List<UnitEntityData>();   // includes Unit
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

            var quick = usable.FirstOrDefault(c => c.Slot == ActionSlot.Free || c.Slot == ActionSlot.Swift);
            var main = usable.FirstOrDefault(c => c.Slot != ActionSlot.Free && c.Slot != ActionSlot.Swift);

            Candidate chosen = null;
            if (quick != null && quick.Value >= MinQuickValue) chosen = quick;
            else if (main != null && main.Value >= MinMainValue) chosen = main;

            LogDecision(ctx, usable, chosen);
            return chosen;
        }

        private static bool Affordable(TacticalContext ctx, Candidate c)
        {
            switch (c.Slot)
            {
                case ActionSlot.Free: return true;
                case ActionSlot.Swift: return ctx.HasSwift;
                case ActionSlot.Move: return ctx.HasMove || ctx.HasStandard; // a standard action can be spent as a move
                case ActionSlot.Standard: return ctx.HasStandard && (!c.NeedsMove || ctx.HasMove);
                case ActionSlot.FullRound: return c.Kind == CandidateKind.WeaponAttack ? ctx.CanFullAttack : ctx.HasFullRound;
                default: return false;
            }
        }

        private static void LogDecision(TacticalContext ctx, List<Candidate> usable, Candidate chosen)
        {
            var sb = new StringBuilder();
            sb.Append($"[Tactics] {ctx.Unit.CharacterName} R{ctx.Round} HP{ctx.HpPct * 100:F0}% ");
            sb.Append($"[{(ctx.HasStandard ? "S" : "-")}{(ctx.HasMove ? "M" : "-")}{(ctx.HasSwift ? "W" : "-")}{(ctx.CanFullAttack ? "F" : "-")}{(ctx.CanFiveFoot ? "5" : "-")}] ");
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
                ctx.MoveRange = ctx.HasMove ? ctx.Turn.GetRemainingMovementTime(unit, false, true) * unit.CurrentSpeedMps : 0f;
            }
            else
            {
                ctx.MoveRange = ctx.HasMove ? unit.CurrentSpeedMps * 3f : 0f;
            }

            try { ctx.Engagers.AddRange(unit.CombatState.EngagedBy.Where(e => e != null && e.HPLeft > 0)); } catch { }

            foreach (var other in Game.Instance.State.Units)
            {
                if (other == null || other.Descriptor?.State == null) continue;
                if (other.Descriptor.State.IsDead || other.HPLeft <= 0) continue;
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
                bool fiveFootReach = !inReach && !single.IsRanged && ctx.CanFiveFoot &&
                                     dist <= reach + TurnController.MetersOfFiveFootStep * 0.85f;

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
                            Value = DamageValue(enemy, full.ExpectedDamage) * weight - aooPenalty,
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
                        Value = DamageValue(enemy, single.ExpectedDamage) * weight - aooPenalty,
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
                        Value = DamageValue(enemy, single.ExpectedDamage) * weight * 0.9f,
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
                    foreach (var conv in a.GetConversions())
                        if (conv?.Blueprint != null && a.Blueprint.HasVariant(conv.Blueprint)) Add(conv);
                    return;
                }
                string key = $"{a.Blueprint.AssetGuid}|{a.Spellbook?.Blueprint?.AssetGuid}|{a.SourceItem?.UniqueId}";
                if (seen.Add(key)) result.Add(a);
            }

            foreach (var ability in unit.Abilities.Enumerable)
                Add(ability?.Data);

            foreach (var book in unit.Spellbooks ?? Enumerable.Empty<Kingmaker.UnitLogic.Spellbook>())
            {
                try { foreach (var s in book.GetAllKnownSpells()) Add(s); } catch { }
                try { foreach (var s in book.GetAllCustomSpells()) Add(s); } catch { }
                for (int level = 0; level <= 10; level++)
                {
                    try { foreach (var s in book.GetSpecialSpells(level)) Add(s); } catch { }
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

                    var best = EvaluateAbility(ctx, ability);
                    if (best != null) list.Add(best);
                }
                catch (Exception ex)
                {
                    Main.Verbose($"[Tactics] {ability?.Name}: {ex.Message}");
                }
            }
        }

        private static ActionSlot SlotOf(AbilityData ability)
        {
            if (ability.RequireFullRoundAction) return ActionSlot.FullRound;
            switch (ability.ActionType)
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
            var g = ctx.Global;
            if (g == null || !g.UseConsumables) return 0f;

            switch (item.Blueprint.Type)
            {
                case UsableItemType.Potion:
                    if (!g.UsePotions) return 0f;
                    // Healing potions are judged by the heal scoring (urgency); others only in real fights.
                    return profile.Heals.Count > 0 ? 1f : (ctx.Difficulty >= 0.8f ? 0.5f : 0f);
                case UsableItemType.Scroll:
                case UsableItemType.Wand:
                    if (!g.UseScrollsAndWands) return 0f;
                    return 0.7f * CombatMath.UseMagicDeviceChance(ability, ctx.Unit);
                default:
                    if (!g.UseThrownItems) return 0f;
                    return 0.85f * CombatMath.UseMagicDeviceChance(ability, ctx.Unit);
            }
        }

        private static Candidate EvaluateAbility(TacticalContext ctx, AbilityData ability)
        {
            var profile = AbilityProfile.Get(ability);
            if (profile == null) return null;

            float consumable = ConsumableFactor(ctx, ability, profile);
            if (consumable <= 0f) return null;

            var slot = SlotOf(ability);
            var targets = new List<UnitEntityData>();
            bool selfOnly = ability.TargetAnchor == Kingmaker.UnitLogic.Abilities.Blueprints.AbilityTargetAnchor.Owner ||
                            ability.Range == AbilityRange.Personal;

            if (profile.IsCharge)
                return EvaluateCharge(ctx, ability);

            // Intent decides which side an ability may be aimed at: never a harmful spell on a friend just because the
            // blueprint allows targeting friends, never a purely helpful one on an enemy.
            bool helpfulOnly = profile.HelpfulToAllies && !profile.HarmfulToEnemies && !profile.DealsDamage;
            bool harmfulOnly = (profile.HarmfulToEnemies || profile.DealsDamage) && !profile.HelpfulToAllies && profile.Heals.Count == 0;

            if (selfOnly)
            {
                if (!harmfulOnly || profile.IsAoE) targets.Add(ctx.Unit);
            }
            else
            {
                if (!helpfulOnly && (ability.Blueprint.CanTargetEnemies || (ability.CanTargetPoint && (profile.HarmfulToEnemies || profile.DealsDamage))))
                    targets.AddRange(ctx.Enemies);
                if (!harmfulOnly && (ability.Blueprint.CanTargetFriends || ability.Blueprint.CanTargetSelf))
                    targets.AddRange(ctx.Allies.Where(a => a == ctx.Unit ? ability.Blueprint.CanTargetSelf : ability.Blueprint.CanTargetFriends));
            }

            Candidate best = null;
            foreach (var target in targets.Distinct())
            {
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

                float value = ScoreAbility(ctx, ability, profile, target, out string why);
                if (value <= 0f) continue;
                value *= consumable;
                if (needsMove) value *= 0.9f;

                if (best == null || value > best.Value)
                {
                    best = new Candidate
                    {
                        Kind = CandidateKind.Ability,
                        Slot = slot,
                        Ability = ability,
                        Target = wrapper.Unit,
                        Point = wrapper.Unit == null ? wrapper.Point : (Vector3?)null,
                        NeedsMove = needsMove,
                        Value = value,
                        Offensive = why.Contains("dmg") || why.Contains("cc") || why.Contains("debuff"),
                        Label = $"{(ability.SourceItem != null ? "Item:" : "")}{ability.Name}->{target.CharacterName}({why})",
                        Key = $"abl:{CombatMemory.AbilityKey(ability)}:{target.UniqueId}"
                    };
                }
            }
            return best;
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

        private static float ScoreAbility(TacticalContext ctx, AbilityData ability, AbilityProfile profile, UnitEntityData target, out string why)
        {
            var unit = ctx.Unit;
            bool targetIsEnemy = unit.IsEnemy(target);
            int level = Math.Max(0, ability.SpellLevel);
            float value = 0f;
            var reasons = new List<string>();

            List<UnitEntityData> affectedEnemies = targetIsEnemy ? new List<UnitEntityData> { target } : new List<UnitEntityData>();
            List<UnitEntityData> affectedAllies = targetIsEnemy ? new List<UnitEntityData>() : new List<UnitEntityData> { target };
            if (profile.IsAoE)
            {
                var targetsType = profile.AoETargets;
                affectedEnemies = targetsType == Kingmaker.UnitLogic.Abilities.Components.TargetType.Ally
                    ? new List<UnitEntityData>() : UnitsAround(ctx.Enemies, target.Position, profile.AoERadius);
                affectedAllies = targetsType == Kingmaker.UnitLogic.Abilities.Components.TargetType.Enemy
                    ? new List<UnitEntityData>() : UnitsAround(ctx.Allies, target.Position, profile.AoERadius);
            }

            // --- damage ---
            if (profile.DealsDamage && affectedEnemies.Count > 0)
            {
                float dmgValue = 0f;
                foreach (var enemy in affectedEnemies)
                {
                    if (CombatRulesAnalyzer.AnalyzeResistance(unit, enemy, ability).IsCompletelyIneffective) continue;
                    float dmg = CombatMath.EstimateAbilityDamage(ability, profile, enemy);
                    float p = CombatMath.SpellResistancePass(unit, enemy, ability);
                    if (profile.NeedsAttackRoll) p *= CombatMath.SpellAttackHitChance(unit, enemy, ability);
                    if (profile.Save != Kingmaker.EntitySystem.Stats.SavingThrowType.Unknown)
                    {
                        float fail = CombatMath.SaveFailChance(ability, profile, enemy);
                        p *= fail + (1f - fail) * (profile.HalfOnSave ? 0.5f : 0f);
                    }
                    dmgValue += DamageValue(enemy, dmg * p);
                }

                // Friendly fire: hurting an ally costs twice what the same damage on an enemy is worth.
                if (affectedAllies.Count > 0 && profile.IsAoE)
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

            // --- healing (never on undead: positive energy harms them) ---
            if (profile.Heals.Count > 0 && affectedAllies.Count > 0)
            {
                float healValue = 0f;
                foreach (var ally in affectedAllies)
                {
                    if (AbilityClassifier.IsUndeadOrConstruct(ally)) continue;
                    float hp = CombatMath.HpPercent(ally);
                    float threshold = ability.SourceItem != null && ally == unit
                        ? (ctx.Global?.PotionHealThresholdPercent ?? 50) / 100f
                        : Math.Max(0.35f, ctx.Settings.HealAtHPPercent / 100f + 0.2f);
                    if (hp >= threshold) continue;

                    float missing = ally.MaxHP - ally.HPLeft;
                    float heal = Math.Min(CombatMath.EstimateAbilityHeal(ability, profile, ally), missing);
                    float urgency = hp < 0.25f ? 3f : hp < 0.5f ? 2f : 1f;
                    bool threatened = ally.CombatState?.IsEngaged ?? false;
                    healValue += heal * 0.6f * urgency * (threatened ? 1.25f : 1f);
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
                float strength = profile.ControlStrength == ControlStrength.Hard ? 1f :
                                 profile.ControlStrength == ControlStrength.Soft ? 0.4f :
                                 isMark ? 0.15f : 0.25f;
                // Non-control debuffs/marks repeated on new targets every turn are the "spam" pattern: diminish them.
                if (profile.ControlStrength == ControlStrength.None && CombatMemory.TimesUsed(unit, ability) >= 2)
                    strength *= 0.3f;
                foreach (var enemy in affectedEnemies)
                {
                    if (profile.TargetBuffs.Any(b => enemy.Buffs.HasFact(b))) continue;
                    if (profile.Control != CCType.None && AbilityClassifier.HasCCImmunity(enemy, profile.Control)) continue;
                    if (CombatRulesAnalyzer.AnalyzeResistance(unit, enemy, ability).IsCompletelyIneffective) continue;
                    float p = CombatMath.SpellResistancePass(unit, enemy, ability) * CombatMath.SaveFailChance(ability, profile, enemy);
                    if (profile.NeedsAttackRoll) p *= CombatMath.SpellAttackHitChance(unit, enemy, ability);
                    controlValue += p * CombatMath.Threat(enemy) * strength * Math.Min(ctx.ExpectedRounds, 3f);
                }
                if (controlValue > 0f)
                {
                    value += controlValue;
                    reasons.Add(profile.ControlStrength == ControlStrength.Hard ? "cc" : isMark ? "mark" : "debuff");
                }
            }

            // --- buffs on allies / self ---
            bool appliesHelpfulBuff = !profile.HarmfulToEnemies || profile.HelpfulToAllies
                ? (!targetIsEnemy && profile.TargetBuffs.Count > 0) || profile.CasterBuffs.Count > 0
                : profile.CasterBuffs.Count > 0;
            if (appliesHelpfulBuff && !profile.DealsDamage)
            {
                float buffValue = 0f;
                var recipients = profile.TargetBuffs.Count > 0 && !targetIsEnemy ? affectedAllies : new List<UnitEntityData> { unit };
                var buffs = profile.TargetBuffs.Count > 0 && !targetIsEnemy ? profile.TargetBuffs : profile.CasterBuffs;
                foreach (var ally in recipients.Take(5))
                {
                    if (buffs.Any(b => ally.Buffs.HasFact(b))) continue;                    // already active
                    if (CombatMemory.LastCastRound(unit, ability, ally) == ctx.Round) continue; // cast this round already
                    bool fighting = (ally.CombatState?.IsEngaged ?? false) || ally == unit;
                    float perRound = 1f + level * 1.5f;
                    float rounds = profile.HasLongBuff ? ctx.ExpectedRounds : Math.Min(ctx.ExpectedRounds, 3f);
                    buffValue += perRound * rounds * (fighting ? 1f : 0.6f);
                }
                if (buffValue > 0f)
                {
                    float timing = ctx.Round <= 2 ? 1f : 0.6f;
                    value += buffValue * timing * RoleWeight(ctx, "buff") * Math.Min(1.2f, 0.4f + ctx.Difficulty);
                    reasons.Add("buff");
                }
            }

            // --- effects the detector cannot verify: allow once per combat per target/caster ---
            if (value <= 0f && (profile.Enchants || profile.Summons || profile.Dispels || profile.RemovesBuffs || !profile.HasDetectableEffect))
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
                        value += (1f + level * 1.5f) * ctx.ExpectedRounds * 0.5f * RoleWeight(ctx, "buff");
                        reasons.Add("enchant");
                    }
                    else if (profile.Dispels || profile.RemovesBuffs)
                    {
                        value += 2f;
                        reasons.Add("dispel");
                    }
                    else if (CombatMemory.TimesUsed(unit, ability) == 0)
                    {
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

            // "Can I already hurt someone from here with what I have left this turn?"
            bool anyAttackNow = list.Any(c => c.Offensive && !c.NeedsMove && c.Value >= MinMainValue &&
                                             c.Slot != ActionSlot.Free && c.Slot != ActionSlot.Swift && Affordable(ctx, c));
            bool rangedStyle = ctx.HasRangedWeapon || ctx.Settings.RangePreference == RangePreference.Ranged ||
                               ctx.Settings.Role == AIRole.Support;

            // 1) Step out of melee before shooting/casting (5-foot step: no attack of opportunity, keeps full attack).
            if (rangedStyle && ctx.Engagers.Count > 0 && ctx.CanFiveFoot && (ctx.HasStandard || ctx.CanFullAttack))
            {
                var point = FindRetreatPoint(ctx, TurnController.MetersOfFiveFootStep * 0.9f);
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

            // 2) Critically hurt and in melee with no better option: withdraw towards the party.
            if (ctx.HpPct < 0.3f && ctx.Engagers.Count > 0)
            {
                var point = FindRetreatPoint(ctx, Math.Max(ctx.MoveRange, unit.CurrentSpeedMps * 3f));
                if (point.HasValue)
                {
                    list.Add(new Candidate
                    {
                        Kind = CandidateKind.Move, Slot = ActionSlot.Move, Point = point, ApproachRadius = 0.3f,
                        Value = ctx.Engagers.Sum(CombatMath.Threat) * 0.3f,
                        Label = "Withdraw (low HP)", Key = "move:withdraw"
                    });
                }
            }

            // 3) Nothing to do from here: close the distance for this or the next turn.
            if (!anyAttackNow)
            {
                var single = ctx.Enemies
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
        private static Vector3? FindRetreatPoint(TacticalContext ctx, float distance)
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
                CombatMemory.RecordCast(unit, c.Ability, c.Target);
            return issued;
        }

        #endregion
    }
}
