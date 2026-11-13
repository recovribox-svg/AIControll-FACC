// ★ v0.3.30: native activation costs for the boost and its proposed follow-up(s), without spending or reserving anything.
using System;
using System.Collections.Generic;
using Kingmaker.Blueprints;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Components;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class TacticalEngine
    {
        private sealed class SpellCostReader
        {
            private readonly struct CostQuote
            {
                internal readonly IReadOnlyList<JointCostBudget.Charge> Charges;
                internal readonly string Issue;
                internal CostQuote(IReadOnlyList<JointCostBudget.Charge> charges, string issue = null) { Charges = charges; Issue = issue; }
                internal static CostQuote Unknown(string issue) => new CostQuote(null, issue);
            }
            private const int MaxConversions = 16;
            private static readonly JointCostBudget.Charge[] Free = Array.Empty<JointCostBudget.Charge>();
            private readonly UnitEntityData unit;
            private readonly JointCostBudget budget = new JointCostBudget();
            private readonly Dictionary<AbilityData, CostQuote> quotes = new Dictionary<AbilityData, CostQuote>(BoostAbilityIdentity.Instance);
            internal string LastReason { get; private set; }

            internal SpellCostReader(UnitEntityData unit) { this.unit = unit; }
            internal bool CanPay(Candidate setup, Candidate main)
            {
                var a = Quote(setup); if (!Known(a, "setup")) return false;
                var b = Quote(main); if (!Known(b, "cast")) return false;
                bool paid = budget.CanPay(a.Charges, b.Charges);
                LastReason = paid ? null : "shared slot/resource demand exceeds availability";
                return paid;
            }
            internal bool CanPay(Candidate setup, Candidate main, Candidate second)
            {
                var a = Quote(setup); if (!Known(a, "setup")) return false;
                var b = Quote(main); if (!Known(b, "cast")) return false;
                var c = Quote(second); if (!Known(c, "second action")) return false;
                return budget.CanPay(a.Charges, b.Charges, c.Charges);
            }
            private bool Known(CostQuote quote, string stage)
            {
                if (quote.Charges != null) return true;
                LastReason = stage + ": " + quote.Issue;
                return false;
            }

            private CostQuote Quote(Candidate candidate)
            {
                if (candidate == null) return CostQuote.Unknown("no action");
                // This budget describes spell/ability activation, not recurring ammo or on-hit costs.
                if (candidate.Kind == CandidateKind.WeaponAttack) return new CostQuote(Free);
                if (candidate.Kind != CandidateKind.Ability || candidate.Ability == null) return CostQuote.Unknown("non-ability activation");
                var ability = candidate.Ability;
                if (!quotes.TryGetValue(ability, out var quote)) quotes[ability] = quote = Read(ability);
                return quote;
            }

            private CostQuote Read(AbilityData ability)
            {
                try
                {
                    if (!ReferenceEquals(ability.Caster?.Unit, unit)) return CostQuote.Unknown("different caster");
                    if (ability.SourceItem != null || ability.RequireMaterialComponent) return CostQuote.Unknown("item/material payment has no joint quote");
                    if (ability.StickyTouch != null) return CostQuote.Unknown("held-charge delivery");
                    var source = SlotSource(ability);
                    if (source == null || !ReferenceEquals(source.Caster?.Unit, unit)) return CostQuote.Unknown("conversion depth/caster");
                    var result = new List<JointCostBudget.Charge>();
                    var book = source.Spellbook;
                    if (book != null && !ability.SaveSpellbookSlot && !ability.CheckStatForSpellFool.HasValue)
                    {
                        int amount = Math.Max(0, ability.SpellSlotCost);
                        int level = book.GetSpellLevel(source);
                        if (level < 0 || level > 32) return CostQuote.Unknown("spell level outside supported bounds");
                        if (level > 0 && amount > 0)
                        {
                            if (book.Blueprint.Spontaneous)
                                result.Add(JointCostBudget.Charge.FromPool(book, level, book.GetSpontaneousSlots(level), amount));
                            else
                            {
                                var slots = PreparedSlots(book, source, level);
                                if (slots == null) return CostQuote.Unknown("prepared-slot enumeration bound");
                                result.Add(JointCostBudget.Charge.FromPrepared(slots, amount));
                            }
                        }
                    }
                    // Spend does spellbook first, then SpendResources: per-day fact usage before the resource logic.
                    var daily = ability.Fact?.UsagesPerDayResource;
                    if (daily != null) AddPool(result, daily, 1, true);
                    if (!ReadResource(ability, result, out string issue)) return CostQuote.Unknown(issue);
                    return new CostQuote(result);
                }
                catch (Exception ex) { return CostQuote.Unknown("native price query: " + ex.GetType().Name); }
            }

            private static AbilityData SlotSource(AbilityData ability)
            {
                for (int i = 0; i < MaxConversions; i++)
                {
                    var parent = ability.ConvertedFrom;
                    if (ReferenceEquals(parent, null)) return ability;
                    ability = parent;
                }
                return null;
            }

            private static IReadOnlyList<object> PreparedSlots(Spellbook book, AbilityData source, int level)
            {
                var slots = new List<object>();
                int seen = 0;
                foreach (var slot in book.GetMemorizedSpellSlots(level))
                {
                    if (++seen > JointCostBudget.MaxPreparedSlots) return null;
                    // SpendInternal walks backwards and uses AbilityData.Equals(object), including metamagic/source.
                    if (slot != null && slot.Available && source.Equals((object)slot.SpellShell)) slots.Add(slot);
                }
                slots.Reverse();
                return slots;
            }

            private bool ReadResource(AbilityData ability, List<JointCostBudget.Charge> result, out string issue)
            {
                issue = null;
                for (int i = 0; i < MaxConversions; i++)
                {
                    if (!ReferenceEquals(ability.Caster?.Unit, unit)) { issue = "resource source has another caster"; return false; }
                    var logic = ability.ResourceLogic;
                    if (logic != null)
                    {
                        // A derived/override Spend may use additional pools; do not infer it from CalculateCost alone.
                        if (logic.GetType() != typeof(AbilityResourceLogic)) { issue = "uninspected resource provider " + logic.GetType().FullName; return false; }
                        var native = (AbilityResourceLogic)logic;
                        if (!native.IsSpendResource) return true;
                        if (native.CostIsCustom) { issue = "custom resource calculator " +
                            (ability.Blueprint.GetComponent<Kingmaker.UnitLogic.Abilities.Components.Base.IAbilityResourceCostCalculator>()?.GetType().FullName ?? "missing"); return false; }
                        int amount = Math.Max(0, ability.ResourceCost);
                        if (amount == 0) return true;
                        var required = native.RequiredResource;
                        var paid = ability.OverrideRequiredResource ?? required;
                        if (required == null || paid == null) { issue = "missing resource identity"; return false; }
                        // Native restriction checks RequiredResource; Spend can debit an OverrideRequiredResource.
                        AddPool(result, required, amount, false);
                        AddPool(result, paid, amount, true);
                        return true;
                    }
                    if (ability.OverrideRequiredResource != null)
                    {
                        AddPool(result, ability.OverrideRequiredResource, 1, true);
                        return true;
                    }
                    var parent = ability.ConvertedFrom;
                    if (ReferenceEquals(parent, null)) return true;
                    ability = parent;
                }
                issue = "resource conversion depth";
                return false;
            }

            private void AddPool(List<JointCostBudget.Charge> result, BlueprintScriptableObject resource, int amount, bool consume) =>
                result.Add(JointCostBudget.Charge.FromPool(resource, -1, unit.Resources.GetResourceAmount(resource), amount, consume));
        }
    }
}
