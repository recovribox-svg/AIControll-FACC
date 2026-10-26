// ★ v0.3.19 (test6): kineticist burn, the Kinetic Sharpshooter's quiver and infusions that block every blast - from the
// game's Kineticist blueprints (docs/TACTICAL_AI.md §38).
//
//   - Burn is nonlethal damage: BurnFeature's AddKineticistBurnValueChangedTrigger applies BurnEffectBuff, an
//     AddContextStatBonus on DamageNonLethal of burn x character level (KineticNonLethalDamageProperty), and the game
//     knocks a unit out when damage + nonlethal reach its hit points (UnitLifeController). It stays until rest. A burn
//     cost (BurnCost) is the drop chance it adds - the heal model's drop-chance term, mirrored - and a small day cost,
//     less the Elemental Overflow it raises for the blasts still to come.
//   - A blast that costs burn passes the game's restriction only with that much burn left this round
//     (AbilityKineticist.IsAbilityRestrictionPassed: MaxBurnPerRound, LeftBurnThisRound = min(left today, left this
//     round)): 1 per round below level 6, and nothing once the day's burn is spent. A substance infusion is on by
//     default once learned (IsOnByDefault) and adds burn to every blast (AddKineticistBurnModifier on the infusion's
//     buff): Cluster Arrows +3 for a Kinetic Sharpshooter - more than a low-level kineticist may take in a round, so
//     no blast at all; for any kineticist, no blast once the day's burn is gone. Only the weapon was left
//     ("kineticists aren't blasting as much as they should, especially kinetic shooter just does standard attack").
//   - The Kinetic Sharpshooter's quiver (UnitPartKineticist.QuiverBuff, made by Create Quiver: class level + Constitution
//     bonus ranks for 1 burn) pays a blast's burn first (AbilityKineticist.CalculateCost / Spend, with a ranged weapon in
//     hand). Its buff does nothing the buff analyser can read, so the AI never made one.
//
// When no blast of the kineticist is possible and burn is what blocks them, two options compete with the rest:
//   1. Create Quiver, when the arrows make a blast affordable from next round: worth what the blast adds over the best
//      other option, for the rounds the arrows last, less the quiver's own burn.
//   2. Otherwise, with "AI manages toggles" on, switch off (free, at once) the infusion whose burn blocks them: worth what
//      the blast adds over the best other option for the rest of the fight. Not a toggle the player switched in this
//      fight; put back on when the fight ends (CombatMemory.SwitchOffAiToggles), so the player's setup stays.
using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.Blueprints;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.RuleSystem.Rules;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.ActivatableAbilities;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Class.Kineticist;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.UnitLogic.Mechanics.Components;
using UnityEngine;
using CompanionAI_Pathfinder.Settings;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class TacticalEngine
    {
        /// <summary>Share of each burn hit point counted as a day cost (it is not healed until rest).</summary>
        private const float BurnDayCostPerHp = 0.1f;

        /// <summary>The ability is one of the kineticist's own blasts (UnitPartKineticist.Blasts, the variant's parent).</summary>
        internal static bool IsOwnBlast(UnitPartKineticist part, AbilityData ability)
        {
            var bp = ability?.Blueprint;
            if (part?.Blasts == null || bp == null) return false;
            if (Kingmaker.Blueprints.BlueprintExtenstions.GetComponent<AbilityKineticist>(bp) == null) return false;
            var blast = bp.Parent ?? bp;
            return part.Blasts.Contains(blast);
        }

        // ★ kinetic-infusion-value: blueprint-only descriptions; never store actor contexts, rule results or state.
        private sealed class ActiveInfusionManeuver
        {
            internal readonly AbilityProfile.ManeuverEffect Maneuver;
            internal readonly HashSet<BlueprintAbility> Abilities;

            internal ActiveInfusionManeuver(ContextActionCombatManeuver action, HashSet<BlueprintAbility> abilities)
            {
                Maneuver = new AbilityProfile.ManeuverEffect { Type = action.Type, Action = action };
                Abilities = abilities;
            }
        }

        private static readonly Dictionary<BlueprintBuff, ActiveInfusionManeuver> ActiveInfusionManeuvers =
            new Dictionary<BlueprintBuff, ActiveInfusionManeuver>();

        /// <summary>
        /// ★ kinetic-infusion-value: recognize only one direct native trip/bull-rush action. Unknown layouts are cached
        /// as unsupported. No recursive action reader, synthetic ability or hypothetical infusion configuration.
        /// </summary>
        private static ActiveInfusionManeuver ReadActiveInfusionManeuver(BlueprintBuff blueprint)
        {
            if (blueprint == null) return null;
            if (ActiveInfusionManeuvers.TryGetValue(blueprint, out var cached)) return cached;
            ActiveInfusionManeuver description = null;
            try
            {
                var triggers = blueprint.GetComponents<AddKineticistInfusionDamageTrigger>().ToArray();
                if (triggers.Length == 1)
                {
                    var trigger = triggers[0];
                    var actions = trigger.Actions?.Actions;
                    if (trigger.GetType() == typeof(AddKineticistInfusionDamageTrigger) && trigger.TriggerOnDirectDamage &&
                        !trigger.TriggerOnStatDamageOrEnergyDrain && !trigger.CheckWeaponType && !trigger.CheckSpellDescriptor &&
                        trigger.CheckSpellParent && actions?.Length == 1 &&
                        actions[0]?.GetType() == typeof(ContextActionCombatManeuver))
                    {
                        var action = (ContextActionCombatManeuver)actions[0];
                        bool supported = (action.Type == CombatManeuver.Trip || action.Type == CombatManeuver.BullRush) &&
                            (action.OnSuccess?.Actions == null || action.OnSuccess.Actions.Length == 0) &&
                            action.IgnoreConcealment && !action.BatteringBlast && !action.UseCasterLevelAsBaseAttack &&
                            !action.UseCastingStat && !action.UseBestMentalStat &&
                            (!action.ReplaceStat || action.UseKineticistMainStat);
                        if (supported)
                        {
                            var abilities = new HashSet<BlueprintAbility>(trigger.AbilityList.Where(a => a != null));
                            if (abilities.Count > 0) description = new ActiveInfusionManeuver(action, abilities);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Kinetic] unsupported infusion layout {blueprint.name}: {ex.Message}");
            }
            ActiveInfusionManeuvers[blueprint] = description;
            return description;
        }

        /// <summary>
        /// ★ kinetic-infusion-value: only the primary recipient of an ordinary, actually configured projectile blast.
        /// The caller supplies its already-calculated hit/resistance chance; no native burn, gather, parameter or
        /// hit query is added. Maneuver odds remain target-dependent and use the existing native-rule reader.
        /// </summary>
        private static float ActiveInfusionManeuverValue(TacticalContext ctx, AbilityData ability, AbilityProfile profile,
            UnitEntityData enemy, float triggerChance, out string note)
        {
            note = null;
            if (ctx?.Unit == null || ability?.Blueprint == null || enemy == null || profile == null || triggerChance <= 0f ||
                ability.Caster?.Unit != ctx.Unit || profile.IsAoE || profile.Chain != null || profile.IsWeaponAttack ||
                !profile.NeedsAttackRoll || profile.Maneuvers.Count > 0 || profile.Save != SavingThrowType.Unknown || ability.ShadowSpellSettings != null ||
                ability.Blueprint.CanTargetPoint || !ctx.Unit.IsEnemy(enemy)) return 0f;
            // The ordinary target-effect gate has already proved a damage branch may run. Hybrid target effects could
            // pass that gate without any damage event, so leave them outside this slice instead of re-reading guards.
            if (profile.Heals.Count > 0 || profile.TargetBuffs.Count > 0 || profile.RemovesAnything || profile.RemovesBuffs || profile.Dispels)
                return 0f;

            var unit = ctx.Unit;
            var part = unit.Get<UnitPartKineticist>();
            if (!IsOwnBlast(part, ability) || part.ArcherBlast ||
                ability.Blueprint.GetComponent<AbilityDeliveredByWeapon>() != null ||
                ability.Blueprint.GetComponent<AbilityDeliverAttackWithWeapon>() != null) return 0f;
            var projectile = ability.Blueprint.GetComponent<AbilityDeliverProjectile>();
            if (projectile == null || !projectile.NeedAttackRoll || projectile.Type != AbilityProjectileType.Simple ||
                profile.Damage.Count == 0 || profile.Damage.Any(a => a?.GetType() != typeof(ContextActionDealDamage))) return 0f;

            float value = 0f;
            bool tripCounted = false, pushCounted = false;
            foreach (var toggle in unit.ActivatableAbilities?.Enumerable ?? Enumerable.Empty<ActivatableAbility>())
            {
                var bp = toggle?.Blueprint;
                if (bp == null || bp.Group != ActivatableAbilityGroup.SubstanceInfusion || !(toggle.IsOn || toggle.IsStarted)) continue;
                var buff = bp.Buff == null ? null : unit.Buffs.GetBuff(bp.Buff);
                if (buff == null || !buff.Active || !buff.IsTurnedOn || buff.Context?.MaybeCaster != unit) continue;
                var infusion = ReadActiveInfusionManeuver(bp.Buff);
                if (infusion == null || (!infusion.Abilities.Contains(ability.Blueprint) &&
                    !infusion.Abilities.Contains(ability.Blueprint.Parent))) continue;

                var maneuver = infusion.Maneuver;
                if ((maneuver.Type == CombatManeuver.Trip && tripCounted) ||
                    (maneuver.Type == CombatManeuver.BullRush && pushCounted)) continue;
                // Native ContextActionCombatManeuver returns before its roll when a trip target is already getting up.
                if (maneuver.Type == CombatManeuver.Trip &&
                    (enemy.Descriptor.State.Prone.Active || (enemy.View?.IsGetUp ?? false))) continue;
                float strength = ManeuverStrength(enemy, ability, maneuver.Type, out _);
                if (strength <= 0f) continue;
                CombatMath.ManeuverOdds(unit, enemy, ability, maneuver, out float chance, out _);
                float added = ValueModel.OnHitControlValue(triggerChance, chance, CombatMath.ControlWorth(enemy), strength);
                if (CombatMath.IsDisabled(enemy)) added *= 0.4f;
                if (added <= 0f) continue;
                value += added;
                if (maneuver.Type == CombatManeuver.Trip) tripCounted = true;
                else pushCounted = true;
                note = (note == null ? "" : note + "; ") + $"{toggle.Name} {maneuver.Type} +{added:F1}";
            }
            return value;
        }

        /// <summary>The game's blast restriction fails: not enough burn left this round (or today) for its cost.</summary>
        private static bool BurnBlocked(AbilityData ability)
        {
            var k = Kingmaker.Blueprints.BlueprintExtenstions.GetComponent<AbilityKineticist>(ability.Blueprint);
            try { return k != null && !k.IsAbilityRestrictionPassed(ability); }
            catch { return false; }
        }

        /// <summary>The burn the game takes for this ability now (after the quiver), 0 for anything else.</summary>
        internal static int BurnOf(AbilityData ability)
        {
            if (ability?.Blueprint == null || ability.Caster?.Unit?.Get<UnitPartKineticist>() == null) return 0;
            int burn = 0;
            try
            {
                var k = Kingmaker.Blueprints.BlueprintExtenstions.GetComponent<AbilityKineticist>(ability.Blueprint);
                if (k != null) burn += Math.Max(0, k.CalculateCost(ability));
                // ★ v0.3.19 (test7): burn accepted on cast (AbilityAcceptBurnOnCast: Searing Flesh and other wild talents).
                var onCast = Kingmaker.Blueprints.BlueprintExtenstions.GetComponent<AbilityAcceptBurnOnCast>(ability.Blueprint);
                if (onCast != null) burn += Math.Max(0, onCast.BurnValue);
            }
            catch { }
            return burn;
        }

        /// <summary>
        /// ★ v0.3.19 (test7): the ability spends something limited - an ability resource (AbilityResourceLogic that spends,
        /// with a resource) or burn.
        /// </summary>
        internal static bool SpendsLimitedUse(AbilityData ability)
        {
            if (ability?.Blueprint == null) return false;
            try
            {
                foreach (var logic in Kingmaker.Blueprints.BlueprintExtenstions.GetComponents<Kingmaker.UnitLogic.Abilities.Components.AbilityResourceLogic>(ability.Blueprint))
                    if (logic.IsSpendResource && logic.RequiredResource != null) return true;
                return BurnOf(ability) > 0 ||
                       Kingmaker.Blueprints.BlueprintExtenstions.GetComponent<AbilityAcceptBurnOnCast>(ability.Blueprint) != null;
            }
            catch { return false; }
        }

        /// <summary>
        /// What accepting <paramref name="burn"/> costs the kineticist, in damage-equivalent points: the chance to drop
        /// it adds before the next turn (burn x character level of nonlethal damage), a small day cost, less the
        /// Elemental Overflow damage it adds to the blasts left in this fight. A burn that would knock the unit out
        /// costs its whole worth.
        /// </summary>
        internal static float BurnCost(TacticalContext ctx, int burn)
        {
            if (burn <= 0) return 0f;
            var unit = ctx.Unit;
            float loss = burn * Level(unit);
            float hp = CombatMath.HpBeforeDown(unit);
            float worth = AllyWorth(ctx, unit);
            if (loss >= hp) return worth * 2f;
            float cost = loss * BurnDayCostPerHp;
            float danger = IncomingDamage(ctx, unit);
            if (danger > 0f)
                cost += (Mathf.Exp(-(hp - loss) / danger) - Mathf.Exp(-hp / danger)) * worth;
            // The Overflow the burn raises, for the blasts after this one (about one a round).
            int overflowNow = 0, overflowAfter = 0;
            foreach (var blast in (unit.Get<UnitPartKineticist>()?.Blasts ?? Enumerable.Empty<Kingmaker.UnitLogic.Abilities.Blueprints.BlueprintAbility>()).Take(1))
            {
                overflowNow = CombatMath.ElementalOverflowDamage(unit, blast);
                overflowAfter = CombatMath.ElementalOverflowDamage(unit, blast, burn);
            }
            cost -= (overflowAfter - overflowNow) * Math.Max(0f, ValueModel.Horizon(ctx.ExpectedRounds, 4f) - 1f);
            return Math.Max(0f, cost);
        }

        /// <summary>Burn priced into the kineticist's ability candidates (BurnCost); the label says how much.</summary>
        private static void PriceBurn(TacticalContext ctx, List<Candidate> candidates)
        {
            var part = ctx.Unit.Get<UnitPartKineticist>();
            if (part == null) return;
            var costs = new Dictionary<AbilityData, KeyValuePair<int, float>>();
            // A kinetic blade not yet activated this round: the game activates it before the attack, for its burn.
            float bladeCost = 0f;
            int bladeBurn = 0;
            try
            {
                var weapon = ctx.Unit.Body?.PrimaryHand?.MaybeWeapon;
                var blade = weapon == null ? null : Kingmaker.Blueprints.BlueprintExtenstions.GetComponent<WeaponKineticBlade>(weapon.Blueprint);
                var activation = blade?.GetActivationAbility(ctx.Unit.Descriptor);
                if (activation != null && !part.IsBladeActivated && (bladeBurn = BurnOf(activation)) > 0 &&
                    bladeBurn <= part.LeftBurnThisRound)
                    bladeCost = BurnCost(ctx, bladeBurn);
            }
            catch { }
            foreach (var c in candidates)
            {
                if (bladeCost > 0f && c.Value > 0f && (c.Kind == CandidateKind.WeaponAttack || (c.Ability != null && AbilityProfile.Get(c.Ability)?.IsCharge == true)))
                {
                    c.Value -= bladeCost;
                    c.Label += $" [blade burn {bladeBurn}: -{bladeCost:F1}]";
                    continue;
                }
                if (c.Kind != CandidateKind.Ability || c.Ability == null || c.Value <= 0f) continue;
                if (!costs.TryGetValue(c.Ability, out var cost))
                {
                    int burn = BurnOf(c.Ability);
                    costs[c.Ability] = cost = new KeyValuePair<int, float>(burn, BurnCost(ctx, burn));
                }
                if (cost.Key <= 0) continue;
                c.Value -= cost.Value;
                c.Label += $" [burn {cost.Key}: -{cost.Value:F1}]";
            }
        }

        private static void AddKineticCandidates(TacticalContext ctx, List<Candidate> list, bool preview)
        {
            var unit = ctx.Unit;
            var part = unit.Get<UnitPartKineticist>();
            if (part == null) return;
            var abilities = AbilityDiscovery.Gather(unit);
            var blasts = abilities.Where(a => IsOwnBlast(part, a)).ToList();
            if (blasts.Count == 0 || blasts.Any(a => a.IsAvailable)) return;
            var blocked = blasts.Where(BurnBlocked).ToList();
            if (blocked.Count == 0) return;

            float bestOther = list.Where(c => (c.Slot == ActionSlot.Standard || c.Slot == ActionSlot.FullRound) && c.Value > 0f && Affordable(ctx, c))
                                  .Select(c => c.Value).DefaultIfEmpty(0f).Max();
            float horizon = ValueModel.Horizon(ctx.ExpectedRounds, 4f);
            string why = null;

            // 1. The quiver.
            var quiverBuff = part.QuiverBuff;
            var quiver = quiverBuff == null ? null : abilities.FirstOrDefault(a =>
            {
                var p = AbilityProfile.Get(a);
                return p != null && (p.CasterBuffs.Contains(quiverBuff) || p.TargetBuffs.Contains(quiverBuff));
            });
            bool rangedInHand = unit.Body?.PrimaryHand?.MaybeWeapon?.Blueprint?.IsRanged ?? false;
            if (quiver != null && rangedInHand)
            {
                int ranks = unit.Buffs.GetBuff(quiverBuff)?.Rank ?? 0;
                int arrows = QuiverArrows(quiver, part);
                int quiverBurn = BurnOf(quiver);
                if (!quiver.IsAvailable) why = "Create Quiver not available now";
                else if (ranks > 0) why = $"the quiver still holds {ranks}";
                else
                {
                    var best = BestBlast(ctx, blocked, b =>
                    {
                        int total = TotalBurn(b);
                        int after = Math.Max(0, total - arrows);
                        return after <= part.MaxBurnPerRound && after <= part.LeftBurn - quiverBurn ? total : -1;
                    }, out var blast, out int perBlast);
                    if (blast == null) why = $"{arrows} arrows would not make a blast affordable";
                    else
                    {
                        float uses = Math.Min(horizon - 1f, arrows / (float)Math.Max(1, perBlast));
                        float burnCost = BurnCost(ctx, quiverBurn);
                        float value = Math.Max(0f, best - bestOther) * uses - burnCost;
                        if (value > 0f)
                        {
                            list.RemoveAll(c => c.Kind == CandidateKind.Ability && c.Ability == quiver);
                            list.Add(new Candidate
                            {
                                Kind = CandidateKind.Ability,
                                Slot = SlotOf(quiver),
                                Ability = quiver,
                                Target = unit,
                                Value = value,
                                Label = $"{quiver.Name}->{unit.CharacterName}(quiver: {arrows} arrows pay the burn of {blast.Name}, " +
                                        $"+{best - bestOther:F1} a round for {uses:F1} rounds{(quiverBurn > 0 ? $", burn {quiverBurn}: -{burnCost:F1}" : "")})",
                                Key = $"abl:{CombatMemory.AbilityKey(quiver)}:{unit.UniqueId}"
                            });
                            Report(ctx, preview, $"blasts blocked by burn - {list.Last().Label} = {value:F1}");
                            return;
                        }
                        why = $"the quiver's blast ({best:F1}) is not worth more than the best other option ({bestOther:F1})";
                    }
                }
            }

            // 2. Switch off the infusion whose burn blocks them.
            if (!(ModSettings.Instance?.AiManagesToggles ?? true))
            {
                Report(ctx, preview, $"blasts blocked by burn ({BlockedNote(part)}); infusions left alone (\"AI manages toggles\" is off)" +
                                     (why != null ? $"; {why}" : ""));
                return;
            }
            Candidate bestOff = null;
            foreach (var toggle in unit.ActivatableAbilities?.Enumerable ?? Enumerable.Empty<ActivatableAbility>())
            {
                if (toggle?.Blueprint == null || !(toggle.IsOn || toggle.IsStarted) || !IsBlastModifier(toggle.Blueprint)) continue;
                if (CombatMemory.PlayerOwnsToggle(unit, toggle) || CombatMemory.ToggleSwitchedThisRound(unit, toggle)) continue;
                var modifiers = toggle.Blueprint.Buff == null ? null
                    : Kingmaker.Blueprints.BlueprintExtenstions.GetComponents<AddKineticistBurnModifier>(toggle.Blueprint.Buff).Where(m => m.Value > 0).ToList();
                if (modifiers == null || modifiers.Count == 0) continue;
                int quiverRanks = rangedInHand && quiverBuff != null ? unit.Buffs.GetBuff(quiverBuff)?.Rank ?? 0 : 0;
                var best = BestBlast(ctx, blocked, b =>
                {
                    int total = TotalBurn(b);
                    int without = total - modifiers.Where(m => Applies(m, b)).Sum(m => m.Value);
                    int after = Math.Max(0, Math.Max(0, without) - quiverRanks);
                    return after <= part.LeftBurnThisRound ? after : -1;
                }, out var blast, out int burnAfter);
                if (blast == null) continue;
                float value = (best - BurnCost(ctx, burnAfter) - bestOther) * horizon;
                if (value <= 0f || (bestOff != null && value <= bestOff.Value)) continue;
                bestOff = new Candidate
                {
                    Kind = CandidateKind.Toggle,
                    Slot = ActionSlot.Free,
                    Toggle = toggle,
                    ToggleOff = true,
                    Value = value,
                    Label = $"Stop {toggle.Name} (its burn blocks every blast: {blast.Name} then costs {burnAfter}, " +
                            $"+{best - bestOther:F1} a round)",
                    Key = $"tgl-off:{toggle.Blueprint.AssetGuid}"
                };
            }
            if (bestOff != null)
            {
                list.Add(bestOff);
                Report(ctx, preview, $"blasts blocked by burn - {bestOff.Label} = {bestOff.Value:F1}");
            }
            else Report(ctx, preview, $"blasts blocked by burn ({BlockedNote(part)}); nothing to do about it" + (why != null ? $" ({why})" : ""));
        }

        private static void Report(TacticalContext ctx, bool preview, string text)
        {
            if (preview) return;
            ReportOnce($"kinetic|{ctx.Unit.UniqueId}|{CombatMemory.Round}|{text.GetHashCode()}", $"[Kinetic] {ctx.Unit.CharacterName}: {text}");
        }

        private static string BlockedNote(UnitPartKineticist part) =>
            $"burn {part.AcceptedBurn}/{part.MaxBurn}, {part.LeftBurnThisRound} of {part.MaxBurnPerRound} left this round";

        /// <summary>
        /// The best value among <paramref name="blocked"/> blasts that <paramref name="burnWith"/> says would be
        /// affordable (it returns a burn figure for the label, or -1), as the engine would score them if they were.
        /// </summary>
        private static float BestBlast(TacticalContext ctx, List<AbilityData> blocked, Func<AbilityData, int> burnWith,
                                       out AbilityData chosen, out int burn)
        {
            chosen = null;
            burn = 0;
            float best = 0f;
            foreach (var blast in blocked)
            {
                int b = burnWith(blast);
                if (b < 0) continue;
                float value;
                try
                {
                    // ★ kinetic-infusion-value: this existing recovery estimate may switch an infusion OFF. Do not
                    // credit an active infusion in that estimate; no hypothetical configuration is evaluated here.
                    value = EvaluateAbility(ctx, blast, valueActiveInfusions: false).Where(c => c.Value > 0f && Affordable(ctx, c))
                                                       .Select(c => c.Value).DefaultIfEmpty(0f).Max();
                }
                catch { continue; }
                if (value > best) { best = value; chosen = blast; burn = b; }
            }
            return best;
        }

        /// <summary>The ability's whole burn before the quiver (AbilityKineticist.CalculateBurnCost: infusions, gather power).</summary>
        private static int TotalBurn(AbilityData ability)
        {
            try { return Math.Max(0, AbilityKineticist.CalculateAbilityBurnCost(ability)?.Total ?? 0); }
            catch { return 0; }
        }

        /// <summary>AddKineticistBurnModifier's own test: no list, or the blast or its parent in it.</summary>
        private static bool Applies(AddKineticistBurnModifier modifier, AbilityData ability)
        {
            var list = modifier.AppliableTo;
            if (!list.Any()) return true;
            var bp = ability.Blueprint;
            return list.Contains(bp) || (bp.Parent != null && list.Contains(bp.Parent));
        }

        /// <summary>
        /// The arrows Create Quiver makes: the count of its ContextActionRepeatedActions (KineticSharpshooterQuiverProperty:
        /// class level + Constitution bonus), in the ability's own context; the class level when it cannot be read.
        /// </summary>
        private static int QuiverArrows(AbilityData quiver, UnitPartKineticist part)
        {
            try
            {
                var run = Kingmaker.Blueprints.BlueprintExtenstions.GetComponent<Kingmaker.UnitLogic.Abilities.Components.AbilityEffectRunAction>(quiver.Blueprint);
                var repeat = run?.Actions?.Actions?.OfType<ContextActionRepeatedActions>().FirstOrDefault();
                if (repeat != null)
                {
                    var context = CombatMath.CreateContext(quiver, new Kingmaker.Utility.TargetWrapper(quiver.Caster.Unit));
                    int count = Mathf.RoundToInt(CombatMath.EvaluateDice(repeat.Value, context, 0));
                    if (count > 0) return count;
                }
            }
            catch { }
            return Math.Max(1, part.ClassLevel);
        }

        /// <summary>★ v0.3.19 (test6): a kineticist infusion or gather mode - it changes the next blast, nothing else.</summary>
        internal static bool IsBlastModifier(Kingmaker.UnitLogic.ActivatableAbilities.BlueprintActivatableAbility bp) =>
            bp != null && ModifierGroups.Contains(bp.Group);
    }
}
