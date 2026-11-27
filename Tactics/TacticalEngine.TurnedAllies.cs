// ★ coverage3 review (21 November 2026; docs/COVERAGE3_REVIEW.md): allies under an enemy's control, and casts that could kill an
// ally for good (Fusion coverage C3, the author's policy).
// Evidence (coverage3 log, fight 15): an enemy's Dominate put Seelah on its side (native ChangeFaction: a temporary faction
// switch, UnitDescriptor.FactionSwitcher). She became an enemy to every rule: Tripper, Wenduag, Bandit and Cambion Bard made
// weapon attacks on her, and Wenduag's and Bandit's last four hits (74 HP) killed her. Native contract: a temporary switch keeps
// UnitDescriptor.FactionOriginal (SwitchFactions permanently: false), so the unit's own side is known while it lasts.
// Policy: such an ally is to be stopped, not killed. It stays an enemy for its danger and for control (Sleep, a stun, a trip); no
// weapon attack, walk to attack it, leap or Coup de Grace is aimed at it; damage to it is priced as friendly fire.
using System;
using System.Collections.Generic;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Root;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Buffs;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.Utility;
using UnityEngine;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class TacticalEngine
    {
        /// <summary>
        /// A unit of the party's side that an effect has put on another side for now (Dominate, charm: native ChangeFaction):
        /// its original faction is the player's, its current one is not.
        /// </summary>
        internal static bool IsTurnedAlly(UnitEntityData unit)
        {
            try
            {
                var descriptor = unit?.Descriptor;
                var player = BlueprintRoot.Instance?.PlayerFaction;
                return descriptor != null && player != null && descriptor.Faction != player && descriptor.FactionOriginal == player;
            }
            catch { return false; }
        }

        /// <summary>A summoned creature (temporary: it is not killed for good).</summary>
        internal static bool IsSummonUnit(UnitEntityData unit) => IsSummon(unit);

        /// <summary>A dominated companion remains ours after it falls. Native enemy-only and Selective areas can
        /// still hit it, so retain it in the damage/death-safety recipients even though it is no longer a combat target.</summary>
        private static void IncludeDownedTurnedRecipients(TacticalContext ctx, AbilityData ability, AbilityProfile profile,
            Vector3 center, IReadOnlyCollection<UnitEntityData> geometry, List<UnitEntityData> enemies)
        {
            if (ctx.TurnedAllies.Count == 0 || !profile.IsAoE) return;
            foreach (var ally in ctx.TurnedAllies)
            {
                if (ally.Descriptor?.State?.IsConscious != false || enemies.Contains(ally)) continue;
                bool caught = profile.IsProjectileArea ? geometry != null && System.Linq.Enumerable.Contains(geometry, ally)
                    : profile.AoETargets != Kingmaker.UnitLogic.Abilities.Components.TargetType.Ally &&
                      Vector3.Distance(center, ally.Position) <= profile.AoERadius + ally.Corpulence && profile.AreaReaches(ability, ally);
                if (caught) enemies.Add(ally);
            }
        }

        private static void NoteTurnedAlly(TacticalContext ctx, UnitEntityData ally)
        {
            if (!CombatMemory.FirstThisFight($"turned|{ally.UniqueId}")) return;
            string by = null;
            try
            {
                foreach (var buff in ally.Buffs)
                    if (buff?.Blueprint?.GetComponent<Kingmaker.UnitLogic.FactLogic.ChangeFaction>() != null) { by = buff.Name; break; }
            }
            catch { }
            Main.Log($"[Tactics] {ally.CharacterName} is under an enemy's control{(by != null ? $" ({by})" : "")}: stopped, not killed - " +
                     "no weapon attack, Coup de Grace or killing damage on it; control still counts (first per fight)");
        }

        /// <summary>
        /// ★ coverage7 follow-ups (Codex's coverage7 review, domination timeline): a party unit's faction state when it changes
        /// between turned (an enemy's control) and not, as AI Control sees it: its current and original faction, whether it is
        /// of the player's faction and directly controllable, and the effect that switched it. Codex could not settle from the
        /// logs why party units still made attacks of opportunity on Aeiluvetar after her successful re-save; these rows give
        /// the next replay that state. A transition only, at most 16 rows a fight; no game state is changed.
        /// </summary>
        private static void ObserveFaction(UnitEntityData unit, bool turned, UnitEntityData observer)
        {
            if (unit == null) return;
            try
            {
                if (CombatMemory.Fight != s_FactionFight) ResetFactionObservations();
                string id = unit.UniqueId;
                bool known = s_TurnedState.TryGetValue(id, out bool was);
                if (known && was == turned) return;
                if (s_TurnedState.Count > 512) s_TurnedState.Clear();
                if (!known && !turned) return; // No entry or string formatting until this unit has actually turned.
                s_TurnedState[id] = turned;
                if (s_FactionRows >= 16 || CombatMemory.Fight != s_FactionFight)
                {
                    if (CombatMemory.Fight != s_FactionFight) { s_FactionFight = CombatMemory.Fight; s_FactionRows = 0; }
                    else return;
                }
                s_FactionRows++;
                string by = null;
                foreach (var buff in unit.Buffs)
                    if (buff?.Blueprint?.GetComponent<Kingmaker.UnitLogic.FactLogic.ChangeFaction>() != null) { by = buff.Name; break; }
                var d = unit.Descriptor;
                string where = observer == unit ? "AI Control decides for it" : "seen by " + observer?.CharacterName;
                Main.Log($"[Faction] {unit.CharacterName}[{ShortId(unit)}] {(turned ? "turned" : "back")} ({where}): faction " +
                         $"{d?.Faction?.name ?? "?"}, original {d?.FactionOriginal?.name ?? "?"}, player faction {unit.IsPlayerFaction}, " +
                         $"directly controllable {unit.IsDirectlyControllable}, switched by {by ?? "none"}");
            }
            catch { }
        }

        private static readonly Dictionary<string, bool> s_TurnedState = new Dictionary<string, bool>();
        private static int s_FactionRows, s_FactionFight = -1;

        internal static void ResetFactionObservations()
        {
            s_TurnedState.Clear();
            s_FactionRows = 0;
            s_FactionFight = CombatMemory.Fight;
        }

        private static string ShortId(UnitEntityData unit)
        {
            string id = unit?.UniqueId ?? "";
            return id.Length > 5 ? id.Substring(id.Length - 5) : id;
        }

        /// <summary>
        /// An option that only hurts an ally under an enemy's control: a weapon attack on it (its damage is all it does), a walk
        /// to attack it, a leap at it, or a Coup de Grace. Spells and maneuvers stay: their control counts, their damage is
        /// friendly fire (ScoreAbility).
        /// </summary>
        private static bool SparesTurnedAlly(TacticalContext ctx, Candidate c)
        {
            if (ctx.TurnedAllies.Count == 0 || c?.Target == null || !ctx.TurnedAllies.Contains(c.Target)) return false;
            if (c.Kind == CandidateKind.WeaponAttack || c.Griffon != null) return true;
            if (c.Kind == CandidateKind.Move && c.Ability == null) return true;
            if (c.Ability != null)
            {
                if (IsCoupDeGrace(c.Ability)) return true;
                var profile = AbilityProfile.Get(c.Ability);
                if (profile != null && profile.IsWeaponAttack) return true;
                // ★ v0.3.70 features3 review: a Magus spell the game delivers through a weapon attack on its target.
                if (c.MagusSourceStrike || DeliveredByWeapon(ctx.Unit, c.Ability, profile)) return true;
            }
            return false;
        }

        /// <summary>
        /// ★ v0.3.70 features3 review: with Spellstrike on, the game delivers this Magus spell through a weapon attack on an
        /// enemy target - an Eldritch Archer's spell through its bow (UnitUseAbility.OnAction queues a UnitAttack), a touch
        /// spell's charge through the melee weapon (UnitUseAbility.CreateCastCommand). A turned ally is an enemy to that test.
        /// Real-time fights 10 and 11: the Drow Magus cast Flare on dominated Camellia (four arrows, all missed) and on dominated
        /// Wenduag (an arrow for 13).
        /// </summary>
        private static bool DeliveredByWeapon(UnitEntityData unit, AbilityData ability, AbilityProfile profile)
        {
            try
            {
                var part = unit?.Get<Kingmaker.UnitLogic.Parts.UnitPartMagus>();
                if (part == null || !part.Spellstrike.Active || ability == null) return false;
                if (part.EldritchArcher) return NativeRangedStrike(unit, ability);
                return MagusSpell(part, ability) && Kingmaker.UnitLogic.UnitHelper.GetThreatHand(unit) != null && MagusMechanics.PlainTouch(ability, profile);
            }
            catch { return false; }
        }

        /// <summary>★ coverage4: a buff that holds its bearer on another side (an enabled ChangeFaction: Dominate, a charm).</summary>
        internal static bool HoldsUnderControl(Buff buff)
        {
            try { return buff?.Blueprint?.GetComponent<ChangeFaction>() != null && !buff.IsSuppressed; }
            catch { return false; }
        }

        /// <summary>A unit of the party's side by its own faction (under an enemy's control or not).</summary>
        internal static bool OfPartySide(UnitEntityData unit)
        {
            try
            {
                var player = BlueprintRoot.Instance?.PlayerFaction;
                return unit?.Descriptor != null && player != null && unit.Descriptor.FactionOriginal == player;
            }
            catch { return false; }
        }

        /// <summary>
        /// ★ coverage4 (author request): what taking <paramref name="hold"/> off <paramref name="ally"/> gives back. For each round
        /// it would still hold (ValueModel.HeldRounds: its NewRound re-save, its time left, at most six rounds of the fight),
        /// the enemy loses a fighter (its control worth, as a hard control would take) and the party has its ally back (its
        /// worth per round; less while it cannot act anyway, half for a summon).
        /// </summary>
        internal static float FreedWorth(TacticalContext ctx, UnitEntityData ally, Buff hold, float capRounds = float.PositiveInfinity)
        {
            try
            {
                float horizon = ValueModel.Horizon(ctx.ExpectedRounds, 6f);
                float rounds = hold.IsPermanent ? horizon : Math.Min(horizon, (float)hold.TimeLeft.TotalSeconds / 6f);
                rounds = Math.Min(rounds, capRounds);   // ★ coverage5: a suppression frees it only while it lasts
                if (!(rounds > 0f)) return 0f;
                float pass = RoundSavePass(hold, ally);
                float own = ValueModel.PerRound(Level(ally)) * (CombatMath.IsDisabled(ally) ? 0.4f : 1f) *
                            (IsSummon(ally) ? ValueModel.SummonFactor : 1f);
                return (CombatMath.ControlWorth(ally) + own) * ValueModel.HeldRounds(rounds, pass);
            }
            catch { return 0f; }
        }

        private static readonly Dictionary<BlueprintBuff, ContextActionSavingThrow> RoundSaves = new Dictionary<BlueprintBuff, ContextActionSavingThrow>();

        /// <summary>
        /// The chance <paramref name="bearer"/> ends <paramref name="hold"/> itself at its next round: a NewRound saving throw whose
        /// pass removes the buff (Dominate Person: Will, ContextActionRemoveSelf). 0 without one.
        /// </summary>
        private static float RoundSavePass(Buff hold, UnitEntityData bearer)
        {
            var blueprint = hold.Blueprint;
            if (!RoundSaves.TryGetValue(blueprint, out var save))
            {
                save = null;
                foreach (var component in blueprint.GetComponents<Kingmaker.UnitLogic.Mechanics.Components.AddFactContextActions>())
                    foreach (var action in component.NewRound?.Actions ?? Array.Empty<Kingmaker.ElementsSystem.GameAction>())
                        if (action is ContextActionSavingThrow throwing && throwing.Type != SavingThrowType.Unknown && EndsOnPass(throwing))
                            save = throwing;
                if (RoundSaves.Count > 256) RoundSaves.Clear();
                RoundSaves[blueprint] = save;
            }
            if (save == null) return 0f;
            int? dc = save.HasCustomDC ? save.CustomDC.Calculate(hold.Context) : (int?)null;
            return 1f - CombatMath.SaveFailChance(hold.Context, save.Type, bearer, dc);
        }

        private static bool EndsOnPass(ContextActionSavingThrow throwing)
        {
            foreach (var action in throwing.Actions?.Actions ?? Array.Empty<Kingmaker.ElementsSystem.GameAction>())
                if (action is ContextActionConditionalSaved saved)
                    foreach (var passed in saved.Succeed?.Actions ?? Array.Empty<Kingmaker.ElementsSystem.GameAction>())
                        if (passed is ContextActionRemoveSelf) return true;
            return false;
        }

        /// <summary>
        /// ★ coverage4 (author request): what <paramref name="ability"/> is worth for freeing the allies under an enemy's control it
        /// reaches. A dispel in the game's own order (DispelValue: the highest caster level first, its check, its count; a buff
        /// the party gave the ally is a loss), or a removal by name or descriptor of the hold itself, times the chance the cast
        /// reaches the ally (EffectChance). An area of allies only does not reach a unit on another side (native target filter).
        /// </summary>
        private static float FreeingValue(TacticalContext ctx, AbilityData ability, AbilityProfile profile, UnitEntityData target,
                                          List<UnitEntityData> affectedEnemies, out string note)
        {
            note = null;
            if (!DispelTargetsUnits(profile) || ctx.TurnedAllies.Count == 0 ||
                profile.DispelActions.Count == 0 && !profile.RemovesAnything && profile.TargetBuffs.Count == 0) return 0f;
            float total = 0f;
            List<string> freed = null;
            var reached = new List<UnitEntityData>();
            if (target != null && ctx.TurnedAllies.Contains(target)) reached.Add(target);
            foreach (var enemy in affectedEnemies)
                if (ctx.TurnedAllies.Contains(enemy) && !reached.Contains(enemy)) reached.Add(enemy);
            float horizon = Math.Min(ctx.ExpectedRounds, 3f);
            foreach (var ally in reached)
            {
                bool held = false;
                foreach (var buff in ally.Buffs.Enumerable) if (HoldsUnderControl(buff)) { held = true; break; }
                if (!held) continue;
                float reach;
                try { reach = profile.Fusion != null ? 1f : CombatMath.EffectChance(ctx.Unit, ally, ability, profile, damage: false); } catch { reach = 0f; }
                if (!(reach > 0f)) continue;
                MechanicsContext source = null;
                try { source = CombatMath.CreateContext(ability, new TargetWrapper(ally)); } catch { }
                float best = 0f, chance = 0f, ownLoss = 0f;
                CombatMath.FusionDamageRead damageRead = null;
                foreach (var dispel in profile.DispelActions)
                {
                    var applies = profile.AppliesTo(dispel, ability, ally);
                    if (applies == EffectApplies.No) continue;
                    float worth = DispelValue(ctx, dispel, source, ally, horizon, true, false, out float frees, out float actionLoss);
                    float gain = applies == EffectApplies.Unknown ? 0f : worth + actionLoss;
                    if (applies == EffectApplies.Unknown) frees = 0f;
                    if (profile.Fusion != null)
                    {
                        float factor = DispelEffectChance(ctx, ability, profile, dispel, ally, true, ref damageRead);
                        gain *= factor;
                        frees *= factor;
                        actionLoss *= DispelEffectChance(ctx, ability, profile, dispel, ally, false, ref damageRead);
                    }
                    ownLoss += actionLoss;
                    if (frees > 0f && gain > best) { best = gain; chance = frees; }
                }
                foreach (var buff in profile.BuffsRemovedFrom(ability, ally, certainOnly: true))
                {
                    if (!HoldsUnderControl(buff)) continue;
                    float worth = FreedWorth(ctx, ally, buff);
                    if (worth > best) { best = worth; chance = 1f; }
                }
                // ★ coverage5: a buff that suppresses the hold (Unbreakable Heart: Compulsion) frees it while it lasts.
                float suppressed = SuppressionRelief(ctx, ability, profile, ally, horizon, out bool freedBySuppression);
                if (freedBySuppression && suppressed > best) { best = suppressed; chance = 1f; }
                total += reach * (best - ownLoss);
                if (best > 0f && chance > 0f)
                    (freed ?? (freed = new List<string>())).Add($"{ally.CharacterName} {reach * chance:P0}");
            }
            if (freed != null)
            {
                string text = $"frees {string.Join(", ", freed)} from an enemy's control";
                note = text;
                ReportOnce($"freeing|{CombatMemory.Fight}|{ability.Blueprint.AssetGuid}|{ctx.Unit.UniqueId}",
                    () => $"[Tactics] {ctx.Unit.CharacterName}: {ability.Name} {text} = {total:F1} (first per fight, caster and ability)");
            }
            else if (total < 0f) note = "strips party buffs from an ally under enemy control";
            return total;
        }

        /// <summary>
        /// ★ Fusion coverage C3 (author's policy): a cast that may kill <paramref name="ally"/> for good. At the veto risk or
        /// above the cast is not used, whatever the settings and its value (<paramref name="why"/>); below it the chance costs a
        /// hundred times the ally's worth (<paramref name="cost"/>). A summon is not killed for good: it vanishes anyway, and
        /// keeps the ordinary friendly-fire price.
        /// </summary>
        private static bool PermanentDeathVetoes(TacticalContext ctx, AbilityData ability, AbilityProfile profile, UnitEntityData ally,
                                                 out float cost, out string why, SharedHdControl.Forecast pool = null)
        {
            cost = 0f;
            why = null;
            if (ally == null || IsSummon(ally) || !CombatMath.CanKillPermanently(ability)) return false;
            float risk;
            // ★ Fusion coverage C8: a Hit Dice pool's kill (Circle of Death) needs the pool to still cover the ally; a fused cast's
            // other spell may kill it either way. Both outcomes, by the forecast's chance.
            if (pool != null && pool.PoolsDamage)
            {
                float reach = Math.Max(0f, Math.Min(1f, pool.AllyReach(ally)));
                float covered = 0f, missed = 0f;
                if (reach > 0f) using (AbilityProfile.PoolOutcome(ability, true)) covered = CombatMath.PermanentDeathRisk(ability, profile, ally);
                if (reach < 1f) using (AbilityProfile.PoolOutcome(ability, false)) missed = CombatMath.PermanentDeathRisk(ability, profile, ally);
                risk = (reach > 0f ? reach * covered : 0f) + (reach < 1f ? (1f - reach) * missed : 0f);
            }
            else risk = CombatMath.PermanentDeathRisk(ability, profile, ally);
            if (risk <= 0f) return false; // NaN is unread, not a safe zero.
            if (PermanentDeathRules.Vetoed(risk))
            {
                string bound = float.IsNaN(risk) ? "unread risk" : $"risk bound {risk:P0}";
                why = $"could kill {ally.CharacterName} for good ({bound})";
                ReportOnce($"permadeath|{CombatMemory.Fight}|{ability.Blueprint.AssetGuid}|{ally.UniqueId}",
                    () => $"[Tactics] {ctx.Unit.CharacterName}: {ability.Name} not used - it could kill {ally.CharacterName} for good " +
                          $"({bound}: {ally.HPLeft} HP left) (first per fight, ability and ally)");
                return true;
            }
            cost = PermanentDeathRules.Cost(risk, AllyWorth(ctx, ally));
            return false;
        }

        /// <summary>
        /// ★ Fusion coverage C8: the damage a Hit Dice pool's cast does to an ally: with the pool covering it, by the forecast's
        /// chance (an ally it did not select, walking in, is assumed covered), and a fused cast's other spell either way.
        /// </summary>
        private static float PooledAllyDamage(AbilityData ability, AbilityProfile profile, SharedHdControl.Forecast pool, UnitEntityData ally)
        {
            float reach = Math.Max(0f, Math.Min(1f, pool.AllyReach(ally)));
            float covered = 0f, missed = 0f;
            if (reach > 0f) using (AbilityProfile.PoolOutcome(ability, true)) covered = CombatMath.EstimateAbilityDamage(ability, profile, ally);
            if (reach < 1f && profile.Fusion != null)
                using (AbilityProfile.PoolOutcome(ability, false)) missed = CombatMath.EstimateAbilityDamage(ability, profile, ally);
            return reach * covered + (1f - reach) * missed;
        }
    }
}
