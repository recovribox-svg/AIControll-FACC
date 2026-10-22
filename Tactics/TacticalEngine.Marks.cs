// ★ v0.3.19 (test7): marks whose effect lives on the caster's side, not in the mark - the Ring of Foe Focus ("Ring of the
// Mad Duelist" in the game; RingOfFoeFocusAbility / Buff / Feature): a free action puts RingOfFoeFocusBuff on one enemy
// (UniqueBuff: one enemy at a time, the old mark goes when a new one is placed), and the wearer's feature gives +2 AC
// against that enemy's attacks (ACBonusAgainstBuffOwner) and +2 on saves against its effects
// (SavingThrowBonusAgainstFactMultiple). The buff itself carries no effect, so the AI took it for a generic mark: Camellia
// marked a far goblin archer and a Zombie in one turn and attacked neither (0.3.19-test6 logs).
//
// Valued by what the caster's own components give against that enemy: the share of the enemy's threat that reaches the
// caster (IncomingParts, the engine's incoming-damage model) times the hits +AC turns away (5 % of the d20 per point,
// relative to an average 55 % hit chance) and the saves +X wins, for the rounds it lasts. A unique mark moved from
// another enemy loses what it gave there.
using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using UnityEngine;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class TacticalEngine
    {
        /// <summary>What a mark gives its caster against the marked enemy, read from the caster's own facts.</summary>
        internal sealed class CasterSideMark
        {
            public int ArmorClass;
            public int Saves;
            public bool Unique;
            public bool Any => ArmorClass > 0 || Saves > 0;
        }

        private static readonly Dictionary<string, KeyValuePair<int, CasterSideMark>> CasterMarks =
            new Dictionary<string, KeyValuePair<int, CasterSideMark>>();

        internal static CasterSideMark CasterMarkOf(UnitEntityData caster, BlueprintBuff buff)
        {
            if (caster == null || buff == null) return new CasterSideMark();
            string key = $"{caster.UniqueId}|{buff.AssetGuid}";
            int frame = Time.frameCount;
            if (CasterMarks.TryGetValue(key, out var cached) && cached.Key == frame) return cached.Value;
            var mark = new CasterSideMark();
            try
            {
                mark.Unique = Kingmaker.Blueprints.BlueprintExtenstions.GetComponent<Kingmaker.UnitLogic.FactLogic.UniqueBuff>(buff) != null;
                foreach (var fact in caster.Facts.List)
                {
                    if (fact?.Blueprint?.ComponentsArray == null || !fact.Active) continue;
                    foreach (var component in fact.Blueprint.ComponentsArray)
                    {
                        if (component is Kingmaker.Designers.Mechanics.Facts.ACBonusAgainstBuffOwner ac && ac.CheckedBuff == buff && ac.Bonus > 0)
                            mark.ArmorClass += ac.Bonus;
                        else if (component is Kingmaker.Designers.Mechanics.Facts.SavingThrowBonusAgainstFactMultiple saves &&
                                 saves.Value > 0 && saves.Facts.Contains(buff))
                            mark.Saves += saves.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Tactics] caster-side mark {buff.name}: {ex.Message}");
            }
            if (CasterMarks.Count > 128) CasterMarks.Clear();
            CasterMarks[key] = new KeyValuePair<int, CasterSideMark>(frame, mark);
            return mark;
        }

        /// <summary>Per round, what <paramref name="mark"/> on <paramref name="enemy"/> saves the caster.</summary>
        private static float GuardPerRound(TacticalContext ctx, UnitEntityData enemy, CasterSideMark mark)
        {
            var part = IncomingParts(ctx, ctx.Unit, ctx.Unit.Position).FirstOrDefault(p => p.Key == enemy);
            if (part.Key == null) return 0f;
            float fromEnemy = part.Value * ctx.DangerScale;
            return fromEnemy * (mark.ArmorClass * 0.05f / 0.55f + mark.Saves * 0.05f * 0.5f);
        }

        /// <summary>
        /// The value of placing a caster-side mark on <paramref name="target"/>, or -1 when <paramref name="profile"/>'s mark
        /// is not one (the generic mark rules apply then). 0 with <paramref name="why"/> when it gives nothing now.
        /// </summary>
        private static float CasterSideMarkValue(TacticalContext ctx, AbilityData ability, AbilityProfile profile, UnitEntityData target,
                                                 out string why)
        {
            why = null;
            var unit = ctx.Unit;
            BlueprintBuff buff = null;
            CasterSideMark mark = null;
            foreach (var b in profile.TargetBuffs)
            {
                var m = CasterMarkOf(unit, b);
                if (m.Any) { buff = b; mark = m; break; }
            }
            if (mark == null) return -1f;

            bool OursOn(UnitEntityData e) => e.Buffs.Enumerable.Any(x => x.Blueprint == buff && x.Context?.MaybeCaster == unit);
            if (OursOn(target)) { why = "already marked"; return 0f; }

            float rounds = ValueModel.Horizon(ctx.ExpectedRounds, 3f);
            float gain = GuardPerRound(ctx, target, mark) * rounds;
            if (mark.Unique)
            {
                var current = ctx.Enemies.FirstOrDefault(e => e != target && OursOn(e));
                if (current != null) gain -= GuardPerRound(ctx, current, mark) * rounds;
                if (gain <= 0f) { why = current != null ? $"{current.CharacterName} is the better mark" : "it does not threaten the caster"; return 0f; }
            }
            if (gain <= 0f) why = "it does not threaten the caster";
            return Math.Max(0f, gain);
        }
    }
}
