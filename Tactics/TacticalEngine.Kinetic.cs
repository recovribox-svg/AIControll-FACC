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
// ★ v0.3.22: complete modifier/gather/blast configurations now compete through TacticalEngine.KineticPlans.
// This file keeps burn pricing, native restrictions and quiver recovery. It no longer estimates a disabled
// infusion by subtracting its declared burn Value: every new configuration asks the native cost handlers.
using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.ActivatableAbilities;
using Kingmaker.UnitLogic.Class.Kineticist;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Actions;
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
            AbilityData bladeActivation = null;
            try
            {
                var weapon = ctx.Unit.Body?.PrimaryHand?.MaybeWeapon;
                var blade = weapon == null ? null : Kingmaker.Blueprints.BlueprintExtenstions.GetComponent<WeaponKineticBlade>(weapon.Blueprint);
                var activation = blade?.GetActivationAbility(ctx.Unit.Descriptor);
                bladeActivation = activation;
                if (activation != null && !part.IsBladeActivated && (bladeBurn = BurnOf(activation)) > 0 &&
                    bladeBurn <= part.LeftBurnThisRound)
                    bladeCost = BurnCost(ctx, bladeBurn);
            }
            catch { }
            foreach (var c in candidates)
            {
                // ★ v0.3.22: native blade activation can queue gathers before a weapon command too.
                if (c.Kind == CandidateKind.WeaponAttack && bladeActivation != null)
                {
                    c.KineticValidationAbility = bladeActivation;
                    c.GatherPower = part.IsBladeActivated ? 0 : CombatMath.GatherPowerCount(ctx.Unit, bladeActivation);
                }
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
            if (ctx.Permissions?.MovementOnly == true) return; // ★ v0.3.22: no burn-recovery command can start
            var unit = ctx.Unit;
            var part = unit.Get<UnitPartKineticist>();
            if (part == null) return;
            var abilities = AbilityDiscovery.Gather(unit);
            var blasts = abilities.Where(a => IsOwnBlast(part, a)).ToList();
            if (blasts.Count == 0 || blasts.Any(a => a.IsAvailable)) return;
            var blocked = blasts.Where(BurnBlocked).ToList();
            if (blocked.Count == 0) return;

            float bestOther = list.Where(c => !c.KineticPreparation && (c.Slot == ActionSlot.Standard || c.Slot == ActionSlot.FullRound) && c.Value > 0f && Affordable(ctx, c))
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

            // ★ v0.3.22: modifier alternatives are already complete, native-costed blast candidates.
            // Do not recover by subtracting a component's declared Value from the current cost: that bypasses
            // cost handlers and can disagree with infusion specialization, quivers, archetypes and other mods.
            Report(ctx, preview, $"blasts blocked by burn ({BlockedNote(part)}); per-blast modifier alternatives were evaluated" +
                                 (why != null ? $"; {why}" : ""));
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
                    value = EvaluateAbility(ctx, blast).Where(c => c.Value > 0f && Affordable(ctx, c))
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
            bp != null && (ModifierGroups.Contains(bp.Group) || HasKineticModifier(bp));
    }
}
