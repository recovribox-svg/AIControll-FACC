// ★ v0.3.25 (author request: Unraveling in automatic infusion selection, docs/TACTICAL_AI.md §84).
//
//   Unraveling (UnravelingInfusionBuff) is a substance infusion whose damage trigger runs ContextActionDispelMagic on the
//   blast's target: spell buffs only, stop after one removed, a caster-level check. It was withheld because the dispel
//   "resolves gameplay inside its execution path". The outcome is fully read from data: ContextActionDispelMagic.RunAction
//   tries the target's buffs sorted by their caster level, highest first, and TryDispelBuff filters them (not
//   dispellable, hidden, from spells or not, the spell-level cap, only enemy buffs, schools/descriptor);
//   RuleDispelMagic.OnTrigger succeeds when d20 + the dispeller's caster level (the reason context's - here the
//   infusion buff's own parameters) + bonus >= DC (CasterLevel check: 11 + the buff's caster level; DC check: its DC).
//   Nothing is executed or rolled here. The dispel does not skip the party's own debuffs on the target (OnlyTargetEnemyBuffs
//   false): one of ours first in line is a loss.
//
//   Worth of a removed buff: what it does for its bearer, as the share of the bearer's worth the same penalty would take
//   (ValueModel.DebuffShare, read from the buff's own context: the attack/AC/save/stat parts), for the rounds it still
//   has (at most the usual three-round horizon). Concealment, mirror images, extra attacks and damage reduction are
//   converted to an AC or attack equivalent (an AI policy: 5 % miss = 1 AC; one extra attack = +5 attack). A buff with no
//   readable part counts a quarter of its combat value (BuffEffectAnalyzer). One of our debuffs counts minus its control
//   or debuff share.
using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.RuleSystem.Rules;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Buffs;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Actions;
using CompanionAI_Pathfinder.Analysis;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class TacticalEngine
    {
        private static readonly System.Reflection.FieldInfo DispelStopField =
            HarmonyLib.AccessTools.Field(typeof(ContextActionDispelMagic), "m_StopAfterCountRemoved");
        private static readonly System.Reflection.FieldInfo DispelCountField =
            HarmonyLib.AccessTools.Field(typeof(ContextActionDispelMagic), "m_CountToRemove");
        private static readonly System.Reflection.FieldInfo DispelMaxLevelField =
            HarmonyLib.AccessTools.Field(typeof(ContextActionDispelMagic), "m_MaxSpellLevel");
        private static readonly System.Reflection.FieldInfo DispelCheckField =
            HarmonyLib.AccessTools.Field(typeof(ContextActionDispelMagic), "m_CheckType");
        private static readonly System.Reflection.FieldInfo DispelUseMaxCasterField =
            HarmonyLib.AccessTools.Field(typeof(ContextActionDispelMagic), "m_UseMaxCasterLevel");
        private static readonly System.Reflection.FieldInfo DispelMaxCasterField =
            HarmonyLib.AccessTools.Field(typeof(ContextActionDispelMagic), "m_MaxCasterLevel");

        /// <summary>The dispel's fields this reader supports (a caster-level or DC check); null for anything else.</summary>
        internal static string DispelUnsupported(ContextActionDispelMagic dispel)
        {
            if (dispel == null) return "no dispel";
            if (DispelStopField == null || DispelCountField == null || DispelMaxLevelField == null || DispelCheckField == null ||
                DispelUseMaxCasterField == null || DispelMaxCasterField == null || DispelBuffTypeField == null)
                return "dispel fields not found";
            var check = (RuleDispelMagic.CheckType)DispelCheckField.GetValue(dispel);
            if (check != RuleDispelMagic.CheckType.CasterLevel && check != RuleDispelMagic.CheckType.DC)
                return $"dispel check {check}";
            if (dispel.OnSuccess?.Actions?.Length > 0 || dispel.OnFail?.Actions?.Length > 0) return "dispel with follow-up actions";
            return null;
        }

        /// <summary>
        /// The expected worth of <paramref name="dispel"/> run on <paramref name="target"/> by the effect context
        /// <paramref name="source"/> (see the file header). 0 for an ally target or nothing to take.
        /// </summary>
        private static float DispelValue(TacticalContext ctx, ContextActionDispelMagic dispel, MechanicsContext source,
                                         UnitEntityData target, float horizon)
        {
            if (target == null || !ctx.Unit.IsEnemy(target) || DispelUnsupported(dispel) != null) return 0f;
            int casterLevel = source?.Params?.CasterLevel ?? Level(ctx.Unit);
            if ((bool)DispelUseMaxCasterField.GetValue(dispel))
                casterLevel = Math.Min(casterLevel, ((ContextValue)DispelMaxCasterField.GetValue(dispel)).Calculate(source));
            int bonus = dispel.CheckBonus + (dispel.ContextBonus?.Calculate(source) ?? 0);
            var check = (RuleDispelMagic.CheckType)DispelCheckField.GetValue(dispel);
            var maxLevel = (ContextValue)DispelMaxLevelField.GetValue(dispel);
            bool levelCap = maxLevel != null && (maxLevel.Value != 0 || !maxLevel.IsValueSimple);
            int cap = levelCap ? maxLevel.Calculate(source) : int.MaxValue;
            int stopAfter = (bool)DispelStopField.GetValue(dispel)
                ? Math.Max(1, ((ContextValue)DispelCountField.GetValue(dispel)).Calculate(source)) : 0;

            var tried = new List<KeyValuePair<int, KeyValuePair<float, float>>>();
            foreach (var buff in target.Buffs.Enumerable)
            {
                var context = buff?.Context;
                if (context == null || !DispelCanTake(dispel, buff)) continue;
                if (levelCap && context.SpellLevel > cap) continue;
                var giver = context.MaybeCaster;
                if (dispel.OnlyTargetEnemyBuffs && (giver == null || !giver.IsEnemy(ctx.Unit))) continue;
                int dc = check == RuleDispelMagic.CheckType.DC ? context.Params.DC : 11 + context.Params.CasterLevel;
                float chance = ValueModel.DispelChance(dc - casterLevel - bonus);
                float rounds = buff.IsPermanent ? horizon : Math.Min(horizon, (float)buff.TimeLeft.TotalSeconds / 6f);
                if (!(rounds > 0f)) continue;
                bool ours = giver != null && giver.IsEnemy(target);
                float share = ours ? -OwnDebuffShare(ctx, buff, target) : EnemyBuffShare(buff, target);
                tried.Add(new KeyValuePair<int, KeyValuePair<float, float>>(context.Params.CasterLevel,
                    new KeyValuePair<float, float>(chance, CombatMath.ControlWorth(target) * share * rounds)));
            }
            if (tried.Count == 0) return 0f;
            // The game sorts by caster level, highest first (List.Sort: equal levels in no set order).
            var ordered = tried.OrderByDescending(t => t.Key).Select(t => t.Value).ToList();
            return ValueModel.ExpectedDispelWorth(ordered, stopAfter);
        }

        /// <summary>What a buff does for the enemy carrying it, as the share of its worth the same penalty would take.</summary>
        private static float EnemyBuffShare(Buff buff, UnitEntityData bearer)
        {
            try
            {
                var list = BuffPartsReader.Read(buff.Blueprint);
                var context = buff.Context;
                int casterLevel = Math.Max(1, context?.Params?.CasterLevel ?? 1);
                var giver = context?.MaybeCaster ?? bearer;
                float attack = 0f, damage = 0f, armor = 0f, saves = 0f, strength = 0f, dexterity = 0f;
                bool any = false;
                foreach (var p in list.Parts.Where(p => p.CasterFact == null || (giver.Descriptor?.HasFact(p.CasterFact) ?? false)))
                {
                    float amount = PartAmount(p, giver, context, bearer, casterLevel);
                    if (p.Conditional) amount *= 0.5f;
                    if (!(amount > 0f)) continue;
                    any = true;
                    switch (p.Kind)
                    {
                        case BuffPartKind.Attack: attack += amount; break;
                        case BuffPartKind.Damage: damage += amount; break;
                        case BuffPartKind.ArmorClass: armor += amount; break;
                        case BuffPartKind.Saves: saves += amount * p.SaveShare; break;
                        case BuffPartKind.Strength: strength += amount; break;
                        case BuffPartKind.Dexterity: dexterity += amount; break;
                        case BuffPartKind.Constitution: saves += amount * 0.5f; break;
                        case BuffPartKind.MentalStat: saves += amount * 0.25f; break;
                        // AI policy conversions (file header).
                        case BuffPartKind.Concealment:
                        case BuffPartKind.MissChance: armor += amount / 5f; break;
                        case BuffPartKind.MirrorImages: armor += 100f * amount / (amount + 1f) / 5f; break;
                        case BuffPartKind.Invisibility: armor += 10f; break;
                        case BuffPartKind.ExtraAttack: attack += 5f * amount; break;
                        case BuffPartKind.PhysicalDR: armor += amount * 0.4f; break;
                        default: any = false; break;
                    }
                }
                if (any) return ValueModel.DebuffShare(attack, damage, armor, saves, strength, dexterity);
                return Math.Min(ValueModel.DebuffCap, 0.25f * BuffEffectAnalyzer.GetCombatValue(buff.Blueprint));
            }
            catch { return 0f; }
        }

        /// <summary>What one of the party's debuffs on <paramref name="enemy"/> takes from it (control or penalty share).</summary>
        private static float OwnDebuffShare(TacticalContext ctx, Buff buff, UnitEntityData enemy)
        {
            try
            {
                float control = AbilityProfile.BuffControl(buff.Blueprint) != ControlStrength.None
                    ? AbilityProfile.ControlShareOf(buff.Blueprint) : 0f;
                float parts = DebuffShareIn(ctx, buff.Context, enemy, new List<Kingmaker.UnitLogic.Buffs.Blueprints.BlueprintBuff> { buff.Blueprint }, out _);
                return Math.Max(control, Math.Max(0f, parts));
            }
            catch { return 0f; }
        }
    }
}
