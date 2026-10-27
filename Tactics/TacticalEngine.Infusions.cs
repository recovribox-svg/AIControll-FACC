// ★ infusion-selection: one bounded substance change competing with the actual next action.
using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.Blueprints;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.ActivatableAbilities;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Class.Kineticist;
using Kingmaker.UnitLogic.Commands;
using Kingmaker.UnitLogic.Commands.Base;
using Kingmaker.UnitLogic.Mechanics;
using CompanionAI_Pathfinder.GameInterface;
using CompanionAI_Pathfinder.Settings;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class TacticalEngine
    {
        internal sealed class InfusionSwitch
        {
            internal ActivatableAbility Activate;
            internal List<ActivatableAbility> Stop;
            internal AbilityData Blast;
            internal int ExpectedBurn;
            internal string Explanation;
        }

        private static bool InfusionInstalled(UnitEntityData unit, ActivatableAbility toggle)
        {
            var buff = toggle?.AppliedBuff;
            return buff != null && buff.Active && buff.IsTurnedOn && buff.Context?.MaybeCaster == unit;
        }

        private static List<ActivatableAbility> SubstanceToggles(UnitEntityData unit) =>
            (unit.ActivatableAbilities?.Enumerable ?? Enumerable.Empty<ActivatableAbility>())
                .Where(t => t?.Blueprint?.Group == ActivatableAbilityGroup.SubstanceInfusion && t.Blueprint.Buff != null)
                .GroupBy(t => t.Blueprint.AssetGuid).Select(g => g.FirstOrDefault(t => InfusionInstalled(unit, t)) ?? g.First()).ToList();

        private static void AddInfusionChoice(TacticalContext ctx, List<Candidate> usable, bool preview)
        {
            if (preview || !(ctx.Global?.AiManagesToggles ?? true) || !ctx.Unit.IsInCombat) return;
            var part = ctx.Unit.Get<UnitPartKineticist>();
            if (part == null || !ctx.HasStandard) return;
            var group = SubstanceToggles(ctx.Unit);
            if (group.Count == 0) return;
            if (group.Any(t => CombatMemory.PlayerOwnsToggle(ctx.Unit, t)))
            {
                ReportOnce($"infusion-owner|{ctx.Unit.UniqueId}",
                    $"[Kinetic] {ctx.Unit.CharacterName}: substance choices left to the player for this fight (player-toggle protection)");
                return;
            }
            if (group.Any(t => CombatMemory.ToggleSwitchedThisRound(ctx.Unit, t))) return;
            var active = group.Where(t => InfusionInstalled(ctx.Unit, t)).ToList();
            if (active.Select(t => t.Blueprint.Buff).Distinct().Count() != active.Count) return;
            // Drivers already wait for actual commands/queues. An IsOn request by itself may be stale after failed
            // activation, so it must not permanently block selection. A started fact still losing its buff can settle.
            if (group.Any(t => t.IsStarted && !InfusionInstalled(ctx.Unit, t))) return;
            var main = usable.FirstOrDefault(c => !QuickAction(c));
            float bestNow = Math.Max(0f, main?.Value ?? 0f);
            var aims = usable.Concat(ctx.InfusionProspects).Where(c => c.Kind == CandidateKind.Ability && c.Ability != null &&
                    c.Ability.Spellbook == null && c.InfusionAim?.Hits?.Count > 0 &&
                    c.InfusionAim.Hits.Count <= InfusionSearchBudget.MaxRecipients &&
                    !c.NeedsMove && !c.MoveFirst && Affordable(ctx, c) && IsOwnBlast(part, c.Ability))
                .GroupBy(c => c.Key).Select(g => g.First()).OrderByDescending(c => c.Value).Take(InfusionSearchBudget.MaxAims).ToList();
            if (aims.Count == 0) return; // Walk first, then evaluate the actual reachable blast.
            int capacity = ctx.Unit.Descriptor.Get<Kingmaker.UnitLogic.Parts.UnitPartActivatableAbility>()?
                .GetGroupSize(ActivatableAbilityGroup.SubstanceInfusion) ?? 1;
            var options = group.Where(t => t.Blueprint.WeightInGroup <= capacity).GroupBy(t => t.Blueprint.Buff)
                .Select(g => g.FirstOrDefault(active.Contains) ?? g.First()).ToList();
            options.Insert(0, null); // Price the off baseline before spending the rest of the search budget.
            var budget = new InfusionSearchBudget();
            var roots = new Dictionary<BlueprintBuff, MechanicsContext>();
            var burnPrices = new Dictionary<int, float>();
            Candidate best = null;
            foreach (var aim in aims)
            {
                if (budget.Exhausted) break;
                var native = aim.Ability.Blueprint.GetComponent<AbilityKineticist>();
                if (native == null) continue;
                if (aim.Ability.Blueprint.GetComponent<AbilityAcceptBurnOnCast>() != null) continue;
                var cost = native.CalculateBurnCost(aim.Ability); // Once per actual sampled blast, not per target/option.
                int quiver = KineticInfusionCost.QuiverAllowance(native, part);
                foreach (var option in options)
                {
                    var removed = active.Where(t => t != option).ToList();
                    if (removed.Count == 0 && (option == null || active.Contains(option))) continue;
                    if (budget.Exhausted) break;
                    if (option != null)
                    {
                        var description = DescribeInfusion(option.Blueprint.Buff);
                        if (description?.Unsupported != null)
                        {
                            ReportOnce($"infusion-unsupported|{option.Blueprint.AssetGuid}",
                                $"[Kinetic] {option.Name}: automatic selection withheld ({description.Unsupported})");
                            continue;
                        }
                        if (description == null || !description.Applies(aim.Ability)) continue;
                    }
                    if (!budget.TryBegin()) break;
                    if (option != null && (option.Blueprint.HiddenInUI || option.Blueprint.IsTargeted ||
                        !option.Blueprint.ActivateWithUnitCommand ||
                        UnitActivateAbility.GetCommandType(option) != UnitCommand.CommandType.Free ||
                        option.ResourceCount.HasValue || !option.IsAvailable)) continue;
                    // Unknown active effects cannot safely be subtracted from the blast's current value.
                    if (removed.Any(t => DescribeInfusion(t.Blueprint.Buff)?.Unsupported != null)) continue;
                    var removeBuffs = removed.Select(t => t.Blueprint.Buff).ToArray();
                    BlueprintBuff add = option != null && !active.Contains(option) ? option.Blueprint.Buff : null;
                    if (!KineticInfusionCost.TryReplace(cost, aim.Ability, removeBuffs, add, out int burn, quiver))
                    {
                        ReportOnce($"infusion-cost|{aim.Ability.Blueprint.AssetGuid}|{option?.Blueprint.AssetGuid}",
                            $"[Kinetic] {aim.Ability.Name}: substance change withheld; native modifier layout is not a constant marginal cost");
                        continue;
                    }
                    if (burn > part.LeftBurnThisRound) continue;
                    // A new Gather sequence is not priced by this task. Keep the native mode and avoid assuming an
                    // increased prospective cost has the old blast's movement/action schedule.
                    if (part.GatherPowerMode != KineticistGatherPowerMode.Off && burn > aim.NativeBurn) continue;
                    // Native Total owns the reducers. Unknown hybrid wild costs/missing quiver queries retain an
                    // upper bound; current native cost is authoritative again after the real switch.
                    if (!burnPrices.TryGetValue(burn, out float burnPrice)) burnPrices[burn] = burnPrice = BurnCost(ctx, burn);
                    float added = 0f, lost = 0f;
                    MechanicsContext source = null;
                    if (add != null && !roots.TryGetValue(add, out source))
                        roots[add] = source = InfusionContext(ctx.Unit, add, ctx.Unit);
                    bool firstHit = true;
                    foreach (var hit in aim.InfusionAim.Hits)
                    {
                        added += InfusionDamageChange(ctx, aim.Ability, hit, add, removeBuffs,
                            out float resultingChance, out float resultingDamage);
                        lost += hit.CurrentValue;
                        // Reprice retained effects too: removing SR bypass or changing immediate damage changes
                        // their delivery/survival value even though that toggle remains installed.
                        foreach (var retained in active.Except(removed))
                        {
                            added += InfusionEffectValue(ctx, aim.Ability, retained.Blueprint.Buff, hit.Target, resultingChance,
                                retained.AppliedBuff?.Context, out _, resultingDamage, firstHit);
                        }
                        if (add != null)
                            added += InfusionEffectValue(ctx, aim.Ability, add, hit.Target, resultingChance, source, out _, resultingDamage, firstHit);
                        firstHit = false; // A non-stacking caster discount is one benefit, not one per area recipient.
                    }
                    float gain = ValueModel.InfusionPlanGain(aim.Value, added, lost, aim.InfusionAim.DeliveryFactor,
                        aim.BurnPrice, burnPrice, bestNow);
                    float after = bestNow + gain;
                    if (!(gain > 0f) || best != null && gain <= best.Value) continue;
                    var change = new InfusionSwitch
                    {
                        Activate = add == null ? null : option, Stop = removed, Blast = aim.Ability, ExpectedBurn = burn,
                        Explanation = $"{aim.Ability.Name}->{aim.Target?.CharacterName}: plan {after:F1} vs next action {bestNow:F1}; " +
                            $"effects {added:+0.0;-0.0;0}/-{lost:F1}, delivery x{aim.InfusionAim.DeliveryFactor:F2}, native total burn <= {burn}" +
                            (source?.Params != null ? $", effect DC {source.Params.DC}" : "")
                    };
                    best = new Candidate
                    {
                        Kind = CandidateKind.Toggle, Slot = ActionSlot.Free, Toggle = option ?? removed.First(),
                        ToggleOff = add == null, InfusionSwitch = change, Value = gain,
                        Label = $"{(add == null ? "Stop substance infusion" : "Use " + option.Name)} (blast gain {gain:F1})",
                        Key = $"infusion:{option?.Blueprint.AssetGuid.ToString() ?? "off"}:{aim.Key}"
                    };
                }
            }
            if (best != null) usable.Add(best);
            usable.Sort((a, b) => b.Value.CompareTo(a.Value));
            ReportOnce($"infusion-search|{ctx.Unit.UniqueId}|{CombatMemory.Round}|{best?.Key}",
                $"[Kinetic] {ctx.Unit.CharacterName}: compared {budget.Comparisons} substance/blast plans" +
                (budget.Exhausted ? " (work limit reached)" : "") +
                (best == null ? "; keep actual setup" : $"; {best.Label}; {best.InfusionSwitch.Explanation}"));
        }

        private static bool ExecuteInfusionSwitch(UnitEntityData unit, Candidate candidate, out UnitCommand command, out string failure)
        {
            command = null;
            failure = null;
            var plan = candidate.InfusionSwitch;
            var group = SubstanceToggles(unit);
            if (!(ModSettings.Instance?.AiManagesToggles ?? true) || group.Any(t => CombatMemory.PlayerOwnsToggle(unit, t) ||
                CombatMemory.ToggleSwitchedThisRound(unit, t))) { failure = "infusion ownership/round changed"; return false; }
            var before = group.ToDictionary(t => t, t => t.IsOn);
            bool issued;
            try { issued = CommandDispatcher.Run(unit, plan.Activate, plan.Stop, candidate.Label, out command, out failure); }
            finally
            {
                CombatMath.InfusionStateChanged();
                // A partially refused native request may already have stopped another group member.
                foreach (var toggle in group)
                    if (before[toggle] != toggle.IsOn || toggle == plan.Activate || plan.Stop.Contains(toggle))
                        CombatMemory.RecordToggle(unit, toggle, toggle.IsOn, before[toggle]);
            }
            if (!issued) return false;
            Main.Log($"[Kinetic] {unit.CharacterName}: issued {candidate.Label}; {plan.Explanation}; " +
                "next decision checks installed buff and actual native affordability");
            return true;
        }
    }
}
