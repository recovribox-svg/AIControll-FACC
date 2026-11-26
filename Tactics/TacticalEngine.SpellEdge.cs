using System;
using System.Collections.Generic;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.UnitLogic.Mechanics;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class TacticalEngine
    {
        // ★ v0.3.70 playtest: see SpellEdgeMemory. Session-wide; bounded by the memory itself.
        private static readonly SpellEdgeMemory s_SpellEdges = new SpellEdgeMemory();

        /// <summary>
        /// The game forbids a unit's spells and spell-like abilities alike while it rages (UnitCondition.SpellcastingForbidden
        /// in AbilityData.CanBeCastByCaster); an item's spell stays usable. So a spell here is an own spell or spell-like ability.
        /// </summary>
        private static bool OwnSpellOption(Candidate c) =>
            c.Kind == CandidateKind.Ability && c.Ability?.Blueprint != null && c.Ability.SourceItem == null &&
            (c.Ability.Blueprint.Type == AbilityType.Spell || c.Ability.Blueprint.Type == AbilityType.SpellLike);

        /// <summary>
        /// Records this decision's edge of spells over the best other main action. Read only from a decision whose spells
        /// were allowed (under a prohibition they are not scored, and the edge would read 0) and that has its main action.
        /// </summary>
        private static void NoteSpellEdge(TacticalContext ctx, List<Candidate> usable)
        {
            var unit = ctx.Unit;
            if (unit?.Descriptor?.State == null) return;
            if (unit.Descriptor.State.HasCondition(UnitCondition.SpellcastingForbidden))
            {
                LearnSpellBan(unit);
                return;
            }
            if (!ctx.HasStandard) return;
            float bestSpell = 0f, bestOther = 0f;
            bool anyMain = false;
            foreach (var c in usable)
            {
                if (c.Slot != ActionSlot.Standard && c.Slot != ActionSlot.FullRound) continue;
                anyMain = true;
                if (OwnSpellOption(c)) bestSpell = Math.Max(bestSpell, c.Value);
                else bestOther = Math.Max(bestOther, c.Value);
            }
            if (!anyMain) return;
            s_SpellEdges.Note(unit.UniqueId, KindKeyOf(unit), SpellEdgeMemory.EdgeOf(bestSpell, bestOther));
        }

        /// <summary>The measured edge of <paramref name="ally"/>'s spells per round (its own decisions, else its kind's).</summary>
        internal static bool TrySpellEdge(UnitEntityData ally, out float edge, out int samples) =>
            s_SpellEdges.TryGet(ally?.UniqueId, KindKeyOf(ally), out edge, out samples);

        // ★ v0.3.70 playtest: the toggles whose buffs were seen forbidding an ally's spells. Tome of the Firebird and Mystical
        // Mayhem (KRXLib) make Inspired Rage apply InspiredRageEffectBuffBeforeMasterSkald (ForbidSpellCasting) at run time,
        // which the exported blueprints do not show, so the appraisal read no prohibition. Learnt from the game's own state:
        // the banning buff's context chain names the toggle buff whose area applied it (MechanicsContext.ParentContext).
        private const int MaxLearnedBans = 64;
        private static readonly HashSet<BlueprintBuff> s_BanningToggleBuffs = new HashSet<BlueprintBuff>();
        private static readonly HashSet<string> s_BanReadThisFight = new HashSet<string>(StringComparer.Ordinal);
        private static int s_BanReadFight = -1;

        private static void LearnSpellBan(UnitEntityData unit)
        {
            if (s_BanReadFight != CombatMemory.Fight) { s_BanReadFight = CombatMemory.Fight; s_BanReadThisFight.Clear(); }
            if (s_BanReadThisFight.Count >= 256 || !s_BanReadThisFight.Add(unit.UniqueId)) return;
            foreach (var buff in unit.Buffs.Enumerable)
            {
                var blueprint = buff?.Blueprint;
                if (blueprint == null || Kingmaker.Blueprints.BlueprintExtenstions.GetComponent<ForbidSpellCasting>(blueprint) == null) continue;
                MechanicsContext context = buff.Context;
                for (int depth = 0; depth < 6 && context != null; depth++, context = context.ParentContext)
                {
                    if (!(context.AssociatedBlueprint is BlueprintBuff source) || source == blueprint) continue;
                    if (s_BanningToggleBuffs.Count < MaxLearnedBans && s_BanningToggleBuffs.Add(source))
                        Main.Log($"[Toggle] {unit.CharacterName}: {blueprint.name} forbids its spells, applied through {source.name}; " +
                                 "that toggle's recipients are priced for the spells they lose from now on");
                }
            }
        }

        /// <summary>The toggle (by its buff, <paramref name="toggleCtx"/>'s blueprint) was seen forbidding its recipients' spells.</summary>
        internal static bool ToggleBansSpells(MechanicsContext toggleCtx) =>
            toggleCtx?.AssociatedBlueprint is BlueprintBuff buff && s_BanningToggleBuffs.Contains(buff);

        private static string KindKeyOf(UnitEntityData unit)
        {
            var bp = unit?.Blueprint;
            return bp == null ? null : "kind:" + bp.AssetGuid;
        }
    }
}
