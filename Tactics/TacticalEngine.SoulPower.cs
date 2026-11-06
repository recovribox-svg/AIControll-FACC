// ★ v0.3.28 test 10 (author's request: "can the AI play a Dark Elementalist intelligently"): Soul Power by the game's own
// rules. DarkElementalistSoulPowerAbility (a standard, full-round action; one daily use, Intelligence modifier uses a day):
// a pool of the caster's character level (ContextRankConfig CharacterLevel), the units around it its filter lets in (dead,
// not yet harvested: DarkELementalistSoulPowerOnceBuff), each one's character level taken off the pool
// (ContextConditionSharedValueHitDice / ContextActionChangeSharedValue SubHD); when they reach it, ContextActionHealBurn 1 and
// DarkElementalistSuccessfullSoulPowerBuff - a rank of DarkElementalistSuccessfullSoulPowerFeature, which the Overflow buff
// it switches on reads (AddKineticistElementalOverflow, IgnoreBurn: the Dark Elementalist's Overflow comes from Soul Power,
// not from burn), up to a third of the class level, until rest. Every unit in the burst is marked used, success or not.
// Until test 9 the engine counted corpses by the larger of character level and CR (the game counts character level), gave
// a success a fixed 4 for burn and 1.5 a round for Overflow (also past the cap: Soul Power was cast 24 times in one older
// log for nothing more), and never harvested out of combat, where the corpses still lie and no turn is spent. DarkCodex's
// "Dark Elementalist QoL" patch makes it a free action usable only out of combat with no filter; the same reader covers it
// (its availability and filter come from the ability).
using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Class.Kineticist;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.UnitLogic.Mechanics.Components;
using UnityEngine;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class TacticalEngine
    {
        internal sealed class SoulHarvest
        {
            public int Pool, Souls, Units, Burn;
            public bool Succeeds;
            /// <summary>Damage one more Overflow rank adds to a blast hit, and the attack bonus (0 at the cap).</summary>
            public int OverflowDamage, OverflowAttack;
            public string Describe() =>
                $"souls {Souls}/{Pool} from {Units} unit(s), burn {Burn}" +
                (OverflowDamage > 0 ? $", Overflow +{OverflowAttack} attack +{OverflowDamage} damage a blast hit" : ", Overflow at its cap");
        }

        /// <summary>What casting <paramref name="ability"/> (a burn healer on an area) would do now, by the game's rules.</summary>
        internal static SoulHarvest ReadSoulHarvest(UnitEntityData unit, AbilityData ability, AbilityProfile profile)
        {
            var h = new SoulHarvest();
            var part = unit?.Get<UnitPartKineticist>();
            if (part == null || profile == null) return h;
            h.Burn = part.AcceptedBurn;
            h.Pool = Math.Max(1, unit.Descriptor?.Progression?.CharacterLevel ?? 1);
            if (profile.IsAoE)
            {
                try
                {
                    foreach (var other in Game.Instance.State.Units)
                    {
                        if (other == null || !other.IsInGame && !(other.Descriptor?.State?.IsDead ?? false)) continue;
                        if (Vector3.Distance(other.Position, unit.Position) > profile.AoERadius + other.Corpulence) continue;
                        if (!profile.AreaReaches(ability, other)) continue;   // the ability's own filter (dead, not yet used)
                        h.Souls += Math.Max(0, other.Descriptor?.Progression?.CharacterLevel ?? 0);
                        h.Units++;
                    }
                }
                catch { }
            }
            else h.Souls = h.Pool;
            h.Succeeds = ValueModel.SoulHarvestSucceeds(h.Pool, h.Souls);
            foreach (var buff in profile.CasterBuffs.Concat(profile.TargetBuffs).Distinct())
                OverflowRankGain(unit, part, buff, ref h.OverflowDamage, ref h.OverflowAttack);
            return h;
        }

        /// <summary>
        /// The Overflow one more rank of a ranked feature <paramref name="buff"/> adds brings: for each Overflow component
        /// that reads that feature's rank - on the unit's active facts or on a buff <paramref name="buff"/> switches on
        /// (AddFactContextActions) - the ranks gained under the cap times the damage per rank (CombatMath.OverflowDamagePerRank).
        /// </summary>
        private static void OverflowRankGain(UnitEntityData unit, UnitPartKineticist part, BlueprintBuff buff, ref int damage, ref int attack)
        {
            if (buff?.ComponentsArray == null) return;
            var blast = part.Blasts?.FirstOrDefault();
            foreach (var add in buff.GetComponents<AddFacts>())
            {
                foreach (var fact in add.Facts)
                {
                    if (!(fact is BlueprintFeature feature) || feature.Ranks <= 1) continue;
                    int rank;
                    try { rank = unit.Descriptor.Progression.Features.GetRank(feature); } catch { continue; }
                    int ranks = ValueModel.OverflowRankGain(rank, part.ClassLevel);
                    if (ranks <= 0) continue;
                    var sources = unit.Facts.List.Where(f => f?.Active ?? false).Select(f => f.Blueprint as BlueprintScriptableObject)
                                      .Concat(BuffsSwitchedOn(buff)).Where(b => b != null).Distinct();
                    foreach (var source in sources)
                    {
                        if (!source.GetComponents<ContextRankConfig>().Any(c => ReadsRankOf(c, feature))) continue;
                        foreach (var overflow in source.GetComponents<AddKineticistElementalOverflow>())
                        {
                            damage += ranks * CombatMath.OverflowDamagePerRank(unit, part, overflow, blast, part.AcceptedBurn);
                            attack += ranks;
                        }
                    }
                }
            }
        }

        private static IEnumerable<BlueprintScriptableObject> BuffsSwitchedOn(BlueprintBuff buff)
        {
            foreach (var actions in buff.GetComponents<AddFactContextActions>())
                foreach (var action in actions.Activated?.Actions ?? Array.Empty<Kingmaker.ElementsSystem.GameAction>())
                    if (action is ContextActionApplyBuff apply && apply.Buff != null) yield return apply.Buff;
        }

        /// <summary>
        /// ★ v0.3.3, by the game's rules since v0.3.28 test 10: a burn healer's worth in a fight. Nothing unless it would
        /// succeed; then one burn's price (BurnCost: its nonlethal damage against the danger, the day's share) while there is
        /// burn to heal, and the Overflow rank it adds for the blasts left in this fight (ValueModel.KineticOverflowCredit,
        /// with the price book's blast comparison when it is there). The rank lasts the day, but the corpses still lie after
        /// the fight, where the harvest costs no turn (SoulHarvestToCast): in a fight it is worth only what it gives this fight.
        /// </summary>
        private static float BurnReliefValue(TacticalContext ctx, AbilityData ability, AbilityProfile profile, out string why)
        {
            var unit = ctx.Unit;
            why = null;
            BurnSnapshot(unit, profile, out int burn, out int deadAround);
            // ★ v0.3.18 (test10): nothing has changed since the last use (the same burn, no one new dead around).
            if (CombatMemory.BurnReliefUnchanged(unit, profile.Blueprint, burn, deadAround)) { why = "nothing new since the last use"; return 0f; }
            var h = ReadSoulHarvest(unit, ability, profile);
            if (!h.Succeeds) { why = $"not enough souls ({h.Souls}/{h.Pool})"; return 0f; }
            float relief = h.Burn > 0 ? BurnCost(ctx, 1) : 0f;
            var book = ctx.KineticPrices;
            float overflow = h.OverflowDamage > 0
                ? ValueModel.KineticOverflowCredit(h.OverflowDamage, book?.LandChance ?? 1f, book?.Shortfall, ValueModel.Horizon(ctx.ExpectedRounds, 4f))
                : 0f;
            if (!(relief + overflow > 0f)) { why = $"nothing to gain ({h.Describe()})"; return 0f; }
            Report(ctx, false, $"{ability.Name}: {h.Describe()} - burn {relief:F1} + Overflow {overflow:F1}");
            return relief + overflow;
        }

        private sealed class SoulMemo
        {
            public float CheckedAt, ListedAt;
            public List<AbilityData> Abilities;
        }

        private static readonly Dictionary<string, SoulMemo> SoulMemos = new Dictionary<string, SoulMemo>();
        private static readonly Dictionary<string, string> SoulNotes = new Dictionary<string, string>();

        /// <summary>
        /// ★ v0.3.28 test 10: the burn healer to cast now, out of combat, or null: right after a fight the corpses still lie
        /// around and the harvest costs no turn. Cast when it would succeed and gives something (burn to heal, or an
        /// Overflow rank under the cap); its daily uses are the game's (IsAvailableForCast). Checked at most every 1.5 s;
        /// the ability list is read again every 10 s.
        /// </summary>
        internal static AbilityData SoulHarvestToCast(UnitEntityData unit)
        {
            var part = unit?.Get<UnitPartKineticist>();
            if (part == null || (unit.CombatState?.IsInCombat ?? false)) return null;
            float now = Time.time;
            if (!SoulMemos.TryGetValue(unit.UniqueId, out var memo)) SoulMemos[unit.UniqueId] = memo = new SoulMemo { CheckedAt = -100f, ListedAt = -100f };
            if (now - memo.CheckedAt < 1.5f) return null;
            memo.CheckedAt = now;
            try
            {
                if (memo.Abilities == null || now - memo.ListedAt > 10f)
                {
                    memo.ListedAt = now;
                    memo.Abilities = AbilityDiscovery.Gather(unit).Where(a => AbilityProfile.Get(a)?.HealsBurn ?? false).ToList();
                }
                foreach (var ability in memo.Abilities)
                {
                    var profile = AbilityProfile.Get(ability);
                    string why;
                    AbilityData result = null;
                    if (UserAbilityPolicy.Blocks(unit, ability)) why = "on the block list";
                    else if (!ability.IsAvailableForCast) why = "no use left or not castable now";
                    else
                    {
                        var h = ReadSoulHarvest(unit, ability, profile);
                        if (!h.Succeeds) why = null;   // the usual case between fights: nothing to say
                        else if (h.Burn <= 0 && h.OverflowDamage <= 0) why = $"nothing to gain ({h.Describe()})";
                        else { why = $"harvest: {h.Describe()}"; result = ability; }
                    }
                    if (why != null && (!SoulNotes.TryGetValue(unit.UniqueId + ability.Blueprint.AssetGuid, out var last) || last != why))
                    {
                        SoulNotes[unit.UniqueId + ability.Blueprint.AssetGuid] = why;
                        if (result != null) Main.Log($"[Kinetic] {unit.CharacterName}: out of combat, {ability.Name} - {why}");
                        else Main.Verbose($"[Kinetic] {unit.CharacterName}: out of combat, no {ability.Name} - {why}");
                    }
                    if (result != null) return result;
                }
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Kinetic] {unit.CharacterName}: soul harvest check failed: {ex.Message}");
            }
            return null;
        }
    }
}
