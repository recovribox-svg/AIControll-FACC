// ★ v0.3.22: learned infusion/metakinesis sets compete as complete blast candidates, not priority buffs.
using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.Blueprints;
using Kingmaker.Designers.Mechanics.Facts;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.ActivatableAbilities;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Class.Kineticist;
using Kingmaker.UnitLogic.Mechanics.Components;
using Kingmaker.UnitLogic.Parts;
using CompanionAI_Pathfinder.GameInterface;
using CompanionAI_Pathfinder.Settings;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class TacticalEngine
    {
        internal static bool IsKineticWildTalent(AbilityData ability) => ability?.SourceItem == null && ability?.Spellbook == null &&
            ability?.Caster?.Unit?.Get<UnitPartKineticist>() != null &&
            (ability.Blueprint.GetComponent<AbilityKineticist>() != null ||
             ability.Range == Kingmaker.UnitLogic.Abilities.Blueprints.AbilityRange.Personal &&
             ability.Blueprint.Type != Kingmaker.UnitLogic.Abilities.Blueprints.AbilityType.Spell);

        internal static bool HasKineticModifier(BlueprintActivatableAbility bp) => bp?.Buff != null &&
            (bp.Buff.GetComponent<AddKineticistBurnModifier>() != null || bp.Buff.GetComponent<SetKineticistGatherPowerMode>() != null);

        private static bool CanProjectModifier(BlueprintActivatableAbility bp)
        {
            if (!HasKineticModifier(bp) || bp.IsTargeted) return false;
            // Only callbacks covered by the read-only projection may be switched. Unknown rule handlers stay live
            // and fixed: projecting burn alone while ignoring their other effects would produce a false comparison.
            return bp.Buff.ComponentsArray.All(c => c == null || c is AddKineticistBurnModifier ||
                c is Kingmaker.UnitLogic.Mechanics.Components.AddKineticistInfusionDamageTrigger || c is AutoMetamagic ||
                c is IgnoreSpellResistanceForSpells ||
                c is IgnoreDamageReductionOnTarget ||
                c is SetKineticistGatherPowerMode ||
                // ★ v0.3.22: native MechanicsContext dispatches this calculator's virtual Calculate method.
                // This also covers modded multi-class calculators without copying their DC/level formulas.
                c is ContextRankConfig || c is ContextAbilityParamsCalculator ||
                c is RecalculateOnStatChange || c is RecalculateOnFactsChange ||
                c is Kingmaker.Blueprints.Classes.Spells.SpellDescriptorComponent);
        }

        internal static string KineticModifierNote(BlueprintActivatableAbility bp)
        {
            if (!HasKineticModifier(bp)) return null;
            var effects = bp.Buff.GetComponents<Kingmaker.UnitLogic.Mechanics.Components.AddKineticistInfusionDamageTrigger>()
                .Select(t => DescribeActions(t.Actions?.Actions)).ToList();
            effects.AddRange(bp.Buff.GetComponents<AutoMetamagic>().Select(m => "metakinesis " + m.Metamagic));
            if (bp.Buff.GetComponent<IgnoreSpellResistanceForSpells>() != null) effects.Add("native spell-resistance bypass");
            if (bp.Buff.GetComponent<IgnoreDamageReductionOnTarget>() != null) effects.Add("native damage-reduction bypass");
            return (CanProjectModifier(bp) ? "per-blast native-cost configuration" : "fixed configuration (additional effects not projected)") +
                (effects.Count > 0 ? "; " + string.Join("; ", effects) : "");
        }

        private static void AddKineticPlanCandidates(TacticalContext ctx, List<Candidate> list, bool preview)
        {
            // ★ v0.3.22: no ability or toggle command starts while nauseated; keep movement decisions cheap.
            if (ctx.Permissions?.MovementOnly == true) return;
            var part = ctx.Unit.Get<UnitPartKineticist>();
            if (part == null) return;
            // ★ v0.3.22 (review): preparation is for a blast the unit cannot cast now - with the Standard action spent,
            // its own blasts were skipped (AddAbilityCandidates) and are scored here, burn priced as the others.
            var plans = list.ToList();
            if (!CombatMemory.IsRealTime && ctx.SlotDeferred.Count > 0 && part.GatherPowerAbility?.Data?.IsAvailable == true)
            {
                var deferred = EvaluateDeferred(ctx, a => IsOwnBlast(part, a)).ToList();
                PriceBurn(ctx, deferred);
                plans.AddRange(deferred);
            }
            AddGatherPreparations(ctx, list, plans);
            if (!(ctx.Global?.AiManagesToggles ?? true)) return;
            // RT retains baseline offense estimates while waiting, but modifier search cannot create a usable
            // Standard/Swift action. Keep movement-only wait ticks cheap; genuine Free blasts remain eligible.
            if (CombatMemory.IsRealTime && !ctx.HasStandard && !ctx.HasSwift &&
                !AbilityDiscovery.Gather(ctx.Unit).Any(a => IsOwnBlast(part, a) && SlotOf(a) == ActionSlot.Free)) return;
            if (!KineticPreview.CostHookInstalled)
            {
                Report(ctx, preview, "modifier alternatives unavailable: native cost projection is not installed");
                return;
            }
            var toggles = ctx.Unit.ActivatableAbilities?.Enumerable.Where(t => HasKineticModifier(t?.Blueprint)).ToList();
            if (toggles == null || toggles.Count == 0) return;
            var variable = toggles.Where(t => !t.Blueprint.HiddenInUI && CanProjectModifier(t.Blueprint) && !t.ResourceCount.HasValue &&
                (!t.Blueprint.ActivateWithUnitCommand || Kingmaker.UnitLogic.Commands.UnitActivateAbility.GetCommandType(t) ==
                    Kingmaker.UnitLogic.Commands.Base.UnitCommand.CommandType.Free) &&
                !CombatMemory.PlayerOwnsToggle(ctx.Unit, t) && !CombatMemory.ToggleSwitchedThisRound(ctx.Unit, t) &&
                (!CombatMemory.IsRealTime || t.Blueprint.Buff.GetComponent<SetKineticistGatherPowerMode>() == null || NativeActionCost.CanQuery) &&
                (t.IsOn || t.IsAvailable) && t.IsOn == t.IsStarted).ToList();
            if (variable.Count == 0) return;
            // ★ v0.3.22: preparation above must still see next-round blasts. Immediate modifier comparisons,
            // however, need an action now: current free/swift blasts or a learned Quicken projection can qualify.
            if (!ctx.HasStandard && !ctx.HasFullRound && !ctx.CanFullAttack &&
                !(!CombatMemory.IsRealTime && ctx.HasMove && part.GatherPowerAbility?.Data?.IsAvailable == true && NativeActionCost.CanQuery) &&
                !AbilityDiscovery.Gather(ctx.Unit).Any(a => IsOwnBlast(part, a) &&
                    ActionRules.HasSlot(SlotOf(a), ctx.HasStandard, ctx.HasMove, ctx.HasSwift, ctx.HasFullRound)) &&
                !(ctx.HasSwift && variable.Any(t => t.Blueprint.Buff.GetComponents<AutoMetamagic>()
                    .Any(m => (m.Metamagic & Metamagic.Quicken) != 0)))) return;

            // Include every member of affected groups, including non-kinetic/player-owned members, as fixed choices.
            var groups = new HashSet<ActivatableAbilityGroup>(variable.Select(t => t.Blueprint.Group).Where(g => g != ActivatableAbilityGroup.None));
            var choices = ctx.Unit.ActivatableAbilities.Enumerable.Where(t => toggles.Contains(t) || groups.Contains(t.Blueprint.Group)).ToList();
            // A detached native default part is safe here: GetGroupSize reads its initialized size array only.
            var groupPart = ctx.Unit.Get<UnitPartActivatableAbility>() ?? new UnitPartActivatableAbility();
            var options = choices.Select((t, i) => new KineticConfigurations.Option
            {
                Group = t.Blueprint.HasGroup ? (int)t.Blueprint.Group : -i - 1,
                Capacity = t.Blueprint.HasGroup ? groupPart?.GetGroupSize(t.Blueprint.Group) ?? 0 : t.Blueprint.WeightInGroup,
                Weight = t.Blueprint.WeightInGroup,
                Current = t.IsOn || t.IsStarted,
                Locked = !variable.Contains(t)
            }).ToArray();
            var cache = KineticDecisionScope.Current;
            var budget = new KineticSearchBudget(40d, 64); // CPU/evaluation limits, not score weights
            if (cache != null) cache.Budget = budget;
            try
            {
                var configurations = KineticConfigurations.OneGroupNeighbors(options, 128, out bool complete, budget.Continue);
                if (!complete) { ctx.KineticBudgetExceeded = true; return; }
                if (!configurations.Any(set => set.Where((on, i) => on != options[i].Current).Any())) return;
                // Current scorer supplies the shortlist, including its actual target and wall orientation.
                var seeds = list.Where(c => c.Kind == CandidateKind.Ability && !c.KineticPreparation && IsOwnBlast(part, c.Ability))
                    .OrderByDescending(c => c.Value).Take(3).ToList();
                var blade = ctx.Unit.Body?.PrimaryHand?.MaybeWeapon?.Blueprint.GetComponent<WeaponKineticBlade>();
                var activation = blade?.GetActivationAbility(ctx.Unit.Descriptor);
                if (activation != null)
                    seeds.AddRange(list.Where(c => c.Kind == CandidateKind.WeaponAttack).OrderByDescending(c => c.Value).Take(1));
                if (!seeds.Any(c => c.Kind == CandidateKind.Ability))
                {
                    // An unavailable current setup still needs a path out of burn blockage. Score a bounded
                    // recovery shortlist on one current focus, without a variants × targets × angles expansion.
                    var focus = list.Where(c => c.Target != null && ctx.Unit.IsEnemy(c.Target)).OrderByDescending(c => c.Value)
                        .Select(c => c.Target).FirstOrDefault() ?? ctx.Enemies.OrderByDescending(CombatMath.Threat).FirstOrDefault();
                    var recovery = new List<Candidate>();
                    if (focus != null)
                        foreach (var blast in AbilityDiscovery.Gather(ctx.Unit).Where(a => IsOwnBlast(part, a)))
                        {
                            budget.Check();
                            var aim = AbilityProfile.TargetOn(blast, focus);
                            if (aim != null) recovery.AddRange(EvaluateAbility(ctx, blast, focus, aim));
                        }
                    PriceBurn(ctx, recovery);
                    seeds.AddRange(recovery.OrderByDescending(c => c.Value).Take(3));
                }
                if (seeds.Count == 0) return;
                var validation = seeds.Select(c => c.KineticValidationAbility ?? c.Ability).Where(a => a != null).Distinct().ToList();
                var currentCosts = validation.Select(b => (Ability: b, Burn: BurnOf(b), Available: b.IsAvailable, Slot: SlotOf(b))).ToList();
                using (new KineticPreview(ctx.Unit, variable, variable.Where(t => t.IsOn)))
                    if (currentCosts.Any(b => BurnOf(b.Ability) != b.Burn || b.Ability.IsAvailable != b.Available || SlotOf(b.Ability) != b.Slot))
                    {
                        Report(ctx, preview, "current-state projection differs from native costs/action types; modifier alternatives disabled");
                        return;
                    }
                var alternatives = new List<Candidate>(); // transaction: never append a timed-out prefix to the live list
                foreach (var set in configurations)
                {
                    budget.Check();
                    if (!set.Where((on, i) => on != options[i].Current).Any()) continue;
                    var selected = variable.Where(t => set[choices.IndexOf(t)]).ToList();
                    using (new KineticPreview(ctx.Unit, variable, selected))
                    {
                        if (variable.Any(t => t.Blueprint.Buff.GetComponent<SetKineticistGatherPowerMode>() != null))
                            KineticPreview.Current.GatherMode = choices.Where((t, i) => set[i]).Select(t => t.Blueprint.Buff?.GetComponent<SetKineticistGatherPowerMode>())
                                .FirstOrDefault(g => g != null)?.Mode ?? KineticistGatherPowerMode.Off;
                        var candidates = new List<Candidate>();
                        foreach (var seed in seeds)
                        {
                            budget.Check();
                            if (seed.Kind == CandidateKind.Ability)
                            {
                                if (!seed.Ability.IsAvailable) continue;
                                var target = seed.EvaluationTarget ?? seed.Target;
                                if (target == null) continue;
                                var aim = seed.Point.HasValue ? new Kingmaker.Utility.TargetWrapper(seed.Point.Value, seed.Orientation, null)
                                                              : new Kingmaker.Utility.TargetWrapper(seed.Target);
                                candidates.AddRange(EvaluateAbility(ctx, seed.Ability, target, aim));
                            }
                            else if (activation != null && (part.IsBladeActivated || activation.IsAvailable))
                            {
                                budget.EvaluateAim();
                                var attacks = new List<Candidate>();
                                AddWeaponCandidates(ctx, attacks, seed.Target, seed.FullAttack);
                                foreach (var attack in attacks) { ChooseAttackModes(ctx, attack); attack.KineticValidationAbility = activation; }
                                candidates.AddRange(attacks);
                            }
                        }
                        PriceBurn(ctx, candidates);
                        foreach (var candidate in candidates)
                        {
                            candidate.KineticModes = variable.Select(t => new KeyValuePair<ActivatableAbility, bool>(t, selected.Contains(t))).ToList();
                            candidate.KineticFixedModes = choices.Where(t => !variable.Contains(t))
                                .Select(t => new KeyValuePair<ActivatableAbility, bool>(t, t.IsOn || t.IsStarted)).ToList();
                            candidate.KineticBurn = BurnOf(candidate.KineticValidationAbility ?? candidate.Ability);
                            candidate.Key += "|kinetic:" + string.Join(",", variable.Select(t => $"{t.Blueprint.AssetGuid}:{selected.Contains(t)}"));
                            candidate.Label += " [modifiers " + (selected.Count == 0 ? "none" : string.Join(", ", selected.Select(t => t.Name))) + "]";
                        }
                        alternatives.AddRange(candidates);
                        AddGatherPreparations(ctx, alternatives, candidates);
                    }
                }
                budget.Check();
                list.AddRange(alternatives);
                ctx.KineticPlanCount = alternatives.Count;
            }
            catch (KineticBudgetExceededException) { ctx.KineticBudgetExceeded = true; }
            finally
            {
                ctx.KineticSearchMilliseconds = budget.ElapsedMilliseconds;
                ctx.KineticEvaluations = budget.Evaluations;
                if (cache != null) cache.Budget = null;
            }
        }

        private static bool ApplyKineticPlan(UnitEntityData unit, Candidate candidate, out string failure,
                                             out Kingmaker.UnitLogic.Commands.Base.UnitCommand activation)
        {
            failure = null;
            activation = null;
            if (candidate.KineticModes == null) return true;
            if (!(ModSettings.Instance?.AiManagesToggles ?? true)) { failure = "toggle ownership changed"; return false; }
            if (candidate.KineticFixedModes?.Any(c => (c.Key.IsOn || c.Key.IsStarted) != c.Value) == true)
            { failure = "a fixed/player-owned group member changed after the preview"; return false; }
            foreach (var change in candidate.KineticModes)
                if (change.Key.IsOn != change.Value && (CombatMemory.PlayerOwnsToggle(unit, change.Key) ||
                    CombatMemory.ToggleSwitchedThisRound(unit, change.Key)))
                { failure = "kinetic modifier is player-owned or already switched this round"; return false; }

            // Switch off first so the native group rule never evicts a player-owned member while making room.
            foreach (var change in candidate.KineticModes.OrderBy(c => c.Value))
            {
                if (change.Key.IsOn == change.Value) continue;
                bool wasOn = change.Key.IsOn;
                if (!CommandDispatcher.Run(unit, change.Key, change.Value, candidate.Label, out failure, out activation)) return false;
                CombatMemory.RecordToggle(unit, change.Key, change.Value, wasOn);
                if (activation != null)
                {
                    Main.Log($"[Kinetic] {unit.CharacterName}: wait for native free activation of {change.Key.Name}; re-evaluate the blast afterward");
                    return true;
                }
            }
            if (candidate.KineticModes.Any(c => c.Key.IsOn != c.Value || c.Key.IsStarted != c.Value))
            { failure = "native group rules changed the selected modifier configuration"; return false; }
            // Query the real state after switching. Never cast against a stale or only partly applied preview.
            var validation = candidate.KineticValidationAbility ?? candidate.Ability;
            var part = unit.Get<UnitPartKineticist>();
            if (validation == null || BurnOf(validation) != candidate.KineticBurn ||
                (!(candidate.Kind == CandidateKind.WeaponAttack && part?.IsBladeActivated == true) && !validation.IsAvailable) ||
                (candidate.Kind == CandidateKind.Ability && (SlotOf(validation) != (candidate.KineticCastSlot ?? candidate.Slot) ||
                CombatMath.GatherPowerCount(unit, validation) != candidate.GatherPower)) ||
                (candidate.Kind == CandidateKind.WeaponAttack &&
                 (part?.IsBladeActivated == true ? 0 : CombatMath.GatherPowerCount(unit, validation)) != candidate.GatherPower))
            { failure = "kinetic state changed after modifier switches; re-evaluate before casting"; return false; }
            Main.Log($"[Kinetic] {unit.CharacterName}: {candidate.Label}; burn {candidate.KineticBurn}, gather {candidate.GatherPower}, net {candidate.Value:F1}");
            return true;
        }
    }
}
