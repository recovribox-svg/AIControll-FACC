using System;
using System.Linq;
using System.Reflection;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.Designers.Mechanics.Buffs;
using Kingmaker.Designers.Mechanics.Facts;
using Kingmaker.ElementsSystem;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Buffs.Components;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.UnitLogic.Mechanics.Components;

namespace CompanionAI_Pathfinder.Tactics
{
    public sealed partial class AbilityProfile
    {
        internal BlueprintBuff WorstOfTwoAttacks { get; private set; }
        internal BlueprintBuff RedirectCarrier { get; private set; }
        internal BlueprintBuff RedirectControl { get; private set; }
        internal float RedirectChance { get; private set; }

        internal BlueprintAbility DelayedControlBurst { get; private set; }
        internal OwnAttackMark AttackMark { get; private set; }
        internal bool StatDamageCarrier { get; private set; }

        internal sealed class OwnAttackMark
        {
            internal BlueprintBuff Buff;
            internal AttackBonusAgainstTarget Attack;
            internal DamageBonusAgainstTarget Damage;
            internal bool BypassesReduction;
            internal bool Unique;
            internal ContextCalculateSharedValue[] SharedValues;
        }

        private static readonly FieldInfo RandomBranches = HarmonyLib.AccessTools.Field(typeof(ContextActionRandomize), "m_Actions");
        private static readonly FieldInfo BranchAction = HarmonyLib.AccessTools.Field(RandomBranches?.FieldType.GetElementType(), "Action");
        private static readonly FieldInfo BranchWeight = HarmonyLib.AccessTools.Field(RandomBranches?.FieldType.GetElementType(), "Weight");

        private static bool NoActions(ActionList list) => list?.Actions == null || list.Actions.All(a => a == null);

        private void ReadSummonDebuffs()
        {
            if (Fusion != null) return;
            StatDamageCarrier = TargetBuffs.Count > 0 && TargetBuffs.Any(CombatMath.HasPoisonStatDamage);
            if (TargetBuffs.Count != 1 || RemovesAnything || Summons || Enchants || CasterBuffs.Count != 0 || DealsDamage || Heals.Count != 0 ||
                Maneuvers.Count != 0) return;
            var buff = TargetBuffs[0];
            if (MarksForOwnAttacks)
            {
                if (TryReadOwnAttackMark(buff, out var mark)) AttackMark = mark;
                return;
            }
            if (ReadDelayedBurst(buff)) return;
            BlueprintComponent payload = null;
            foreach (var component in buff.ComponentsArray)
            {
                if (component == null || component is SpellDescriptorComponent || component is RemoveWhenCombatEnded) continue;
                if (payload != null) return;
                payload = component;
            }

            if (payload is ModifyD20 roll && (roll.Rule & RuleType.AttackRoll) != 0 &&
                !roll.TakeBest && !roll.Replace && roll.RollsAmount == 1 && !roll.WithChance &&
                !roll.RerollOnlyIfFailed && !roll.RerollOnlyIfSuccess && (int)roll.RollCondition == 0 &&
                !roll.DispellOnRerollFinished && !roll.DispellOn20 && !roll.AgainstAlignment && !roll.AddBonus &&
                !roll.AddSavingThrowBonus && NoActions(roll.ActionsToTriggerBeforeRoll) && NoActions(roll.ActionsToTrigger))
            {
                WorstOfTwoAttacks = buff;
                return;
            }

            if (!(payload is AddFactContextActions cycle) || buff.Frequency != DurationRate.Rounds ||
                buff.TickEachSecond || !NoActions(cycle.Activated) || !NoActions(cycle.Deactivated) || !NoActions(cycle.Dispose) ||
                cycle.NewRound?.Actions?.Length != 1 || !(cycle.NewRound.Actions[0] is ContextActionRandomize random) ||
                random.CalculateSeed || !(RandomBranches?.GetValue(random) is Array branches) || branches.Length != 2 ||
                BranchAction == null || BranchWeight == null) return;

            ContextActionApplyBuff redirect = null;
            int total = 0, hit = 0;
            foreach (var branch in branches)
            {
                if (branch == null || !(BranchWeight.GetValue(branch) is int weight) || weight < 0 || weight > 10000) return;
                total += weight;
                var actions = BranchAction.GetValue(branch) as ActionList;
                if (NoActions(actions)) continue;
                if (redirect != null || actions.Actions.Length != 1 || !(actions.Actions[0] is ContextActionApplyBuff apply) ||
                    apply.ToCaster || apply.Permanent || !apply.AsChild || apply.UseDurationSeconds || apply.SameDuration ||
                    apply.DurationValue?.Rate != DurationRate.Rounds || apply.DurationValue.DiceType != Kingmaker.RuleSystem.DiceType.Zero ||
                    apply.DurationValue.BonusValue?.ValueType != ContextValueType.Simple || apply.DurationValue.BonusValue.Value != 1 ||
                    apply.Buff == null) return;
                var effect = apply.Buff.GetComponent<BuffStatusCondition>();
                if (effect?.Condition != UnitCondition.AttackNearest || effect.SaveEachRound ||
                    apply.Buff.ComponentsArray.Any(c => c != null && c != effect && !(c is RemoveWhenCombatEnded))) return;
                redirect = apply;
                hit = weight;
            }
            if (redirect == null || total > 0 && hit == 0) return;
            RedirectCarrier = buff;
            RedirectControl = redirect.Buff;
            RedirectChance = total == 0 ? 0.5f : (float)hit / total;
        }

        private bool ReadDelayedBurst(BlueprintBuff buff)
        {
            if (Blueprint.Range != AbilityRange.Personal || IsAoE || BuffApplications.Count != 1 || !BuffsLastOneRound ||
                BuffApplications[0].Action.DurationValue?.Rate != DurationRate.Rounds) return false;
            AddFactContextActions callback = null;
            foreach (var component in buff.ComponentsArray)
            {
                if (component == null || component is ContextCalculateAbilityParams) continue;
                if (callback != null || !(component is AddFactContextActions action)) return false;
                callback = action;
            }
            if (callback == null || !NoActions(callback.Activated) || !NoActions(callback.NewRound) || !NoActions(callback.Dispose) ||
                callback.Deactivated?.Actions?.Length != 1 || !(callback.Deactivated.Actions[0] is ContextActionCastSpell cast) ||
                cast.CastByTarget || cast.OverrideDC || cast.OverrideSpellLevel || cast.OverrideSpellbook ||
                cast.Spell == null || cast.Spell == Blueprint || cast.Spell.Range != AbilityRange.Personal ||
                cast.Spell.GetComponent<Kingmaker.UnitLogic.Abilities.Components.AbilityTargetsAround>() == null) return false;

            var payload = Get(cast.Spell);
            if (payload?.HarmfulEffects != true || payload.ControlStrength == ControlStrength.None ||
                payload.HelpfulToAllies || payload.DealsDamage || payload.Heals.Count != 0) return false;
            DelayedControlBurst = cast.Spell;
            return true;
        }

        private bool TryReadOwnAttackMark(BlueprintBuff buff, out OwnAttackMark mark)
        {
            mark = null;
            if (IsAoE || !Blueprint.CanTargetEnemies) return false;
            AttackBonusAgainstTarget attack = null;
            DamageBonusAgainstTarget damage = null;
            bool bypass = false;
            foreach (var component in buff.ComponentsArray)
            {
                if (component is AttackBonusAgainstTarget a)
                {
                    if (attack != null || !a.CheckCaster || a.CheckCasterFriend ||
                        a.Descriptor != Kingmaker.Enums.ModifierDescriptor.UntypedStackable) return false;
                    attack = a;
                }
                else if (component is DamageBonusAgainstTarget d)
                {
                    if (damage != null || !d.CheckCaster || d.CheckCasterFriend) return false;
                    damage = d;
                }
                else if (component is IgnoreTargetDR dr)
                {
                    if (!dr.CheckCaster || dr.CheckCasterFriend) return false;
                    bypass = true;
                }
            }
            if (attack == null && damage == null) return false;
            mark = new OwnAttackMark
            {
                Buff = buff, Attack = attack, Damage = damage, BypassesReduction = bypass,
                Unique = buff.GetComponent<UniqueBuff>() != null,
                SharedValues = buff.GetComponents<ContextCalculateSharedValue>().ToArray()
            };
            return true;
        }
    }
}
