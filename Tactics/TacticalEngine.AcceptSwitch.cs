using System;
using System.Collections.Generic;
using System.Linq;
using CompanionAI_Pathfinder.Analysis;
using Kingmaker.Blueprints;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.ActivatableAbilities;
using Kingmaker.UnitLogic.Buffs;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Conditions;
using CompanionAI_Pathfinder.Settings;

namespace CompanionAI_Pathfinder.Tactics
{
    // ★ v0.3.70 P2 (author-approved): a summon AI Control drives switches off its acceptance of an ally's area effect when the
    // effect costs it more than it gives. In the v0.3.70 mastermind save Camellia's Inspired Rage forbade every summon's
    // spells and spell-like abilities (stock InspiredRageEffectBuff -> InspiredRageEffectBuffBeforeMasterSkald): the Shadow
    // Demon's choices fell from 35 to 9, the Nabasu's from 48 to 8. The game gives each recipient a toggle that decides
    // whether the area reaches it ("Inspire Rage - Accept Effect", InspiredRageAllyToggleSwitch): the area applies its buff
    // only to an ally with that toggle's buff (ContextConditionHasFact), and turning the toggle off removes the effect (its
    // buff's Deactivated actions; the prohibition is the effect's child, AsChild). A player's party member is never switched:
    // only summons, and never a toggle the player switched during the fight. The fight's end puts it back (RecordToggle).
    public static partial class TacticalEngine
    {
        private static readonly Dictionary<BlueprintAbilityAreaEffect, List<AreaGrant>> s_AcceptGrants =
            new Dictionary<BlueprintAbilityAreaEffect, List<AreaGrant>>();

        /// <summary>"summon|toggle" -> what the declined effect gave per round when it was declined, this fight.</summary>
        private static readonly Dictionary<string, float> s_Declined = new Dictionary<string, float>(StringComparer.Ordinal);
        private static int s_DeclinedFight = -1;

        private static List<AreaGrant> AcceptGrantsOf(BlueprintAbilityAreaEffect area)
        {
            if (s_AcceptGrants.TryGetValue(area, out var grants)) return grants;
            grants = new List<AreaGrant>();
            try { ReadAreaGrants(area, grants, followSaves: false); }
            catch { grants.Clear(); }
            if (s_AcceptGrants.Count < 128) s_AcceptGrants[area] = grants;
            return grants;
        }

        /// <summary>
        /// The recipient's own toggle the grant requires: a branch condition "has the buff of this toggle" (not negated, in an
        /// all-of checker) on the way to the buff. Switching it off is the game's way to decline the effect.
        /// </summary>
        private static ActivatableAbility AcceptSwitchFor(UnitEntityData recipient, AreaGrant grant)
        {
            if (recipient?.ActivatableAbilities == null || grant?.Guards == null) return null;
            foreach (var (checker, branch) in grant.Guards)
            {
                if (!branch || checker?.Conditions == null) continue;
                if (checker.Operation != Kingmaker.ElementsSystem.Operation.And && checker.Conditions.Length != 1) continue;
                foreach (var condition in checker.Conditions)
                {
                    if (!(condition is ContextConditionHasFact has) || has.Not || !(has.Fact is BlueprintBuff required)) continue;
                    foreach (var toggle in recipient.ActivatableAbilities.Enumerable)
                        if (toggle?.Blueprint?.Buff == required) return toggle;
                }
            }
            return null;
        }

        /// <summary>
        /// Performer side: <paramref name="ally"/>, a summon AI Control drives, will decline these grants (its acceptance toggle
        /// is the AI's to switch), so a negative share counts nothing instead of a loss.
        /// </summary>
        private static bool DrivenSummonDeclines(UnitEntityData ally, IEnumerable<AreaGrant> grants)
        {
            if (!(ModSettings.Instance?.AiManagesToggles ?? true) || !GameInterface.SummonControl.IsOrdinarySummon(ally) ||
                !GameInterface.SummonControl.ShouldDrive(ally)) return false;
            foreach (var grant in grants)
            {
                var toggle = AcceptSwitchFor(ally, grant);
                if (toggle != null && !CombatMemory.PlayerOwnsToggle(ally, toggle)) return true;
            }
            return false;
        }

        /// <summary>
        /// Summon side, before the decision is scored: switch off the acceptance of an effect that forbids this summon's
        /// spells when their measured edge beats what the effect gives it; switch a declined one back on when the effect
        /// clearly gives more. True when the unit's spells were given back now (the caller rebuilds its context).
        /// </summary>
        private static bool SwitchCostlyAccept(TacticalContext ctx)
        {
            var unit = ctx.Unit;
            if (!(ctx.Global?.AiManagesToggles ?? true) || !GameInterface.SummonControl.IsOrdinarySummon(unit)) return false;
            if (s_DeclinedFight != CombatMemory.Fight) { s_DeclinedFight = CombatMemory.Fight; s_Declined.Clear(); }
            try
            {
                if (unit.Descriptor.State.HasCondition(UnitCondition.SpellcastingForbidden)) return Decline(ctx);
                Reaccept(ctx);
            }
            catch (Exception ex) { Main.Verbose($"[Toggle] {unit.CharacterName}: acceptance switch: {ex.Message}"); }
            return false;
        }

        private static bool Decline(TacticalContext ctx)
        {
            var unit = ctx.Unit;
            if (!TrySpellEdge(unit, out float edge, out int samples)) return false;
            foreach (var ban in unit.Buffs.Enumerable.ToList())
            {
                var banBlueprint = ban?.Blueprint;
                if (banBlueprint == null || Kingmaker.Blueprints.BlueprintExtenstions.GetComponent<ForbidSpellCasting>(banBlueprint) == null) continue;
                // ContextActionApplyBuff -> UnitDescriptor.AddBuff(parent.CloneFor(buff)): the prohibition's context chain
                // names the effect that gave it and the area that gave the effect.
                Buff effect = null;
                BlueprintAbilityAreaEffect area = null;
                MechanicsContext context = ban.Context?.ParentContext;
                for (int depth = 0; depth < 6 && context != null; depth++, context = context.ParentContext)
                {
                    var associated = context.AssociatedBlueprint;
                    if (associated is BlueprintBuff held && effect == null) effect = unit.Buffs.GetBuff(held);
                    else if (associated is BlueprintAbilityAreaEffect a) { area = a; break; }
                }
                // An area that gives the prohibition itself (no effect buff between them).
                effect = effect ?? ban;
                if (area == null) continue;
                foreach (var grant in AcceptGrantsOf(area))
                {
                    if (grant.Buff != effect.Blueprint) continue;
                    var toggle = AcceptSwitchFor(unit, grant);
                    if (toggle == null || !toggle.IsOn || CombatMemory.PlayerOwnsToggle(unit, toggle) ||
                        CombatMemory.ToggleSwitchedThisRound(unit, toggle)) continue;
                    float? given = HeldGrantPerRound(ctx, effect);
                    if (!given.HasValue || !ValueModel.DeclinesGrant(edge, given.Value)) return false;
                    bool wasOn = toggle.IsOn;
                    if (toggle.Blueprint.GetComponent<TurnOffImmediatelyWithUnitCommand>() == null) toggle.TurnOffImmediately();
                    else toggle.IsOn = false;
                    CombatMemory.RecordToggle(unit, toggle, false, wasOn);
                    if (s_Declined.Count < 256) s_Declined[$"{unit.UniqueId}|{toggle.Blueprint.AssetGuid}"] = given.Value;
                    bool spellsBack = !unit.Descriptor.State.HasCondition(UnitCondition.SpellcastingForbidden);
                    Main.Log($"[Toggle] {unit.CharacterName}: {toggle.Name} off - {effect.Blueprint.name} stops its spells " +
                             $"(-{edge:0.#}/rd, {samples} decisions) and gives {given.Value:0.#}/rd" +
                             (spellsBack ? "" : "; the prohibition ends when the area next updates"));
                    return spellsBack;
                }
            }
            return false;
        }

        private static void Reaccept(TacticalContext ctx)
        {
            if (s_Declined.Count == 0) return;
            var unit = ctx.Unit;
            foreach (var toggle in unit.ActivatableAbilities.Enumerable)
            {
                if (toggle?.Blueprint == null || toggle.IsOn) continue;
                string key = $"{unit.UniqueId}|{toggle.Blueprint.AssetGuid}";
                if (!s_Declined.TryGetValue(key, out float given)) continue;
                if (CombatMemory.PlayerOwnsToggle(unit, toggle) || CombatMemory.ToggleSwitchedThisRound(unit, toggle) ||
                    UserAbilityPolicy.Blocks(unit, toggle) || !toggle.IsAvailable) continue;
                // Its spells are allowed now, so its own decisions keep measuring them (NoteSpellEdge).
                if (!TrySpellEdge(unit, out float edge, out _) || !ValueModel.ReacceptsGrant(edge, given)) continue;
                toggle.IsOn = true;
                CombatMemory.RecordToggle(unit, toggle, true, false);
                s_Declined.Remove(key);
                Main.Log($"[Toggle] {unit.CharacterName}: {toggle.Name} on again - its spells are worth {edge:0.#}/rd now, " +
                         $"the effect gave {given:0.#}/rd");
            }
        }

        /// <summary>
        /// What <paramref name="effect"/>, held now, gives the unit for a round apart from its spell prohibition: the shared
        /// appraisal with the effect's own context, its own bonuses left out of the unit's current numbers (as for a unique
        /// buff held elsewhere). Null when none of its parts can be read.
        /// </summary>
        private static float? HeldGrantPerRound(TacticalContext ctx, Buff effect)
        {
            var blueprint = effect.Blueprint;
            var context = effect.Context;
            var giver = context?.MaybeCaster ?? ctx.Unit;
            var read = BuffPartsReader.Read(blueprint);
            var parts = read.Parts.Where(p => p.Kind != BuffPartKind.ForbidsSpells).ToList();
            if (!HasReadableParts(parts)) return null;
            var unread = new HashSet<string>(read.UnreadFor(giver));
            int level = Math.Max(1, giver.Descriptor?.Progression?.CharacterLevel ?? 1);
            var previous = s_AppraiseWithout;
            s_AppraiseWithout = effect;
            try
            {
                AppraisalFrame();
                var appraisal = AppraiseParts(ctx, giver, context, level, new List<BlueprintBuff> { blueprint }, parts, unread,
                                              ctx.Unit, 1f, false, -1f, $"accept|{ctx.Unit.UniqueId}|{blueprint.AssetGuid}", true,
                                              new BuffAppraisal());
                return appraisal.Modelled ? Math.Max(0f, appraisal.Value) : (float?)null;
            }
            finally { s_AppraiseWithout = previous; }
        }
    }
}
