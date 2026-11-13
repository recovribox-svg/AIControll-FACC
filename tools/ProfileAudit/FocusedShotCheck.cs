// Actual exported metadata and production readers. No initialized actors, attacks, buff application or Unity clock.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Blueprints;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.UnitLogic.Mechanics.Components;

namespace ProfileAudit
{
    internal static class FocusedShotCheck
    {
        private sealed class UninspectedShot : ContextActionRangedAttack { }

        internal static List<string> Run(IEnumerable<SimpleBlueprint> loaded, out int failures)
        {
            int checks = 0, bad = 0; var lines = new List<string>();
            void Check(bool ok, string why) { checks++; if (!ok) { bad++; lines.Add("FOCUSED-SHOT FAILED: " + why); } }
            try
            {
                const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
                var abilities = loaded.OfType<BlueprintAbility>().ToArray();
                var blueprint = abilities.FirstOrDefault(b => b.AssetGuid.ToString() == "1dc6739a4d9543468d67d94a2bd4306e");
                if (blueprint == null) lines.Add("FOCUSED-SHOT SKIPPED: FocusedShotAction not loaded");
                else
                {
                    var reader = typeof(AbilityProfile).GetMethod("ReadRangedShot", all);
                    object Read() => reader.Invoke(null, new object[] { blueprint });
                    var shot = Read();
                    Check(shot != null, "actual Focused Shot structural graph is supported");
                    if (shot == null) throw new InvalidOperationException("Expected exported Focused Shot shape was not admitted");
                    var profile = AbilityProfile.Get(blueprint);
                    Check(profile.IsWeaponAttack && profile.HasDetectableEffect, "actual profile routes through weapon valuation and coverage");
                    var cast = blueprint.GetComponent<AbilityExecuteActionOnCast>();
                    var effect = blueprint.GetComponent<AbilityEffectRunAction>();
                    var attack = (ContextActionRangedAttack)effect.Actions.Actions.Single();
                    var apply = (ContextActionApplyBuff)cast.Actions.Actions.Single();
                    var buff = apply.Buff;
                    var bonus = buff.GetComponent<Kingmaker.UnitLogic.FactLogic.AddContextStatBonus>();
                    var removal = buff.GetComponent<AddInitiatorAttackWithWeaponTrigger>();
                    Check(removal.WeaponType == null && Read() != null, "native empty weapon-type reference is unrestricted");
                    Check(!attack.ExtraAttack && !attack.FullAttack && !attack.SelectNewTarget, "exported attack is one ordinary targeted shot");
                    Check(ReferenceEquals(shot.GetType().GetField("Buff", all).GetValue(shot), buff) &&
                          ReferenceEquals(shot.GetType().GetField("Bonus", all).GetValue(shot), bonus), "descriptor retains exact exported buff/bonus");
                    void RejectField(object value, string name, object changed)
                    {
                        var field = HarmonyLib.AccessTools.Field(value.GetType(), name);
                        var before = field.GetValue(value);
                        try { field.SetValue(value, changed); Check(Read() == null, "withholds changed " + value.GetType().Name + "." + name); }
                        finally { field.SetValue(value, before); }
                    }
                    foreach (var flag in new[] { "FullAttack", "SelectNewTarget", "AutoHit", "IgnoreStatBonus", "AutoCritThreat", "AutoCritConfirmation", "ExtraAttack", "ForceStartAnimation" })
                        RejectField(attack, flag, true);
                    var oldActions = effect.Actions.Actions;
                    try
                    {
                        effect.Actions.Actions = new Kingmaker.ElementsSystem.GameAction[] { (UninspectedShot)RuntimeHelpers.GetUninitializedObject(typeof(UninspectedShot)) };
                        Check(Read() == null, "uninspected action subclass is not admitted");
                        effect.Actions.Actions = oldActions.Concat(oldActions).ToArray();
                        Check(Read() == null, "two attacks cannot be priced as one");
                    }
                    finally { effect.Actions.Actions = oldActions; }
                    foreach (var flag in new[] { "Permanent", "SameDuration", "IgnoreParentContext", "IsFromSpell" }) RejectField(apply, flag, true);
                    foreach (var flag in new[] { "ToCaster", "AsChild", "UseDurationSeconds" }) RejectField(apply, flag, false);
                    RejectField(apply, "DurationSeconds", 2f);
                    RejectField(removal, "OnlyHit", true);
                    RejectField(removal, "OnlyOnFirstAttack", true);
                    RejectField(removal, "TriggerBeforeAttack", true);
                    RejectField(bonus, "Stat", StatType.Strength);
                    RejectField(bonus, "Descriptor", Kingmaker.Enums.ModifierDescriptor.Enhancement);
                    RejectField(bonus, "Restrictions", null);
                    var oldValue = bonus.Value; int oldMultiplier = bonus.Multiplier; bool oldMinimum = bonus.HasMinimal; int oldMin = bonus.Minimal;
                    var calculate = typeof(CombatMath).GetMethod("RangedShotBonus", all);
                    var context = (MechanicsContext)RuntimeHelpers.GetUninitializedObject(typeof(MechanicsContext));
                    float Calculate(bool present = false, bool disabled = false) =>
                        (float)calculate.Invoke(null, new[] { shot, context, (object)present, disabled });
                    try
                    {
                        bonus.Value = new ContextValue { ValueType = ContextValueType.Simple, Value = 6 };
                        bonus.Multiplier = 1; bonus.HasMinimal = false;
                        Check(Read() != null && Calculate() == 6f, "native simple stat-bonus value is projected");
                        Check(Calculate(present: true) == 0f, "already active modifier is not added twice");
                        Check(Calculate(disabled: true) == 0f, "native disabled/immunity gate gives no prospective bonus");
                        bonus.Value.Value = 0; Check(Calculate() == 0f, "known zero remains zero");
                        bonus.Value.Value = -3; Check(Calculate() == -3f, "native negative stat modifier is preserved");
                        bonus.HasMinimal = true; bonus.Minimal = 0; Check(Calculate() == 0f, "native minimum clamps the base value");
                        bonus.HasMinimal = false; bonus.Value.Value = 3; bonus.Multiplier = 2;
                        Check(Calculate() == 6f, "native component multiplier is retained");
                        bonus.Value.ValueType = ContextValueType.TargetProperty;
                        Check(Read() == null, "uninspected target-dependent bonus is withheld");
                    }
                    finally { bonus.Value = oldValue; bonus.Multiplier = oldMultiplier; bonus.HasMinimal = oldMinimum; bonus.Minimal = oldMin; }
                    var cascade = abilities.FirstOrDefault(b => b.AssetGuid.ToString() == "83e690c9f7ef4c64986c123e1db7af90");
                    Check(cascade != null && reader.Invoke(null, new object[] { cascade }) == null, "actual multi-recipient Penetrating Cascade stays outside this contract");
                    var matches = typeof(CombatMath).GetMethod("RangedShotMatches", all);
                    var first = (Kingmaker.Items.ItemEntityWeapon)RuntimeHelpers.GetUninitializedObject(typeof(Kingmaker.Items.ItemEntityWeapon));
                    var other = (Kingmaker.Items.ItemEntityWeapon)RuntimeHelpers.GetUninitializedObject(typeof(Kingmaker.Items.ItemEntityWeapon));
                    var quote = new WeaponAttackEstimate { Attacks = 1, IsRanged = true, Weapon = first };
                    bool Matches(WeaponAttackEstimate q, Kingmaker.Items.ItemEntityWeapon w) => (bool)matches.Invoke(null, new object[] { q, w });
                    Check(Matches(quote, first), "one ranged attack with the native hand matches");
                    Check(!Matches(quote, other), "different enumerated/native hand is withheld");
                    quote.Attacks = 2; Check(!Matches(quote, first), "multiple attacks are withheld");
                    quote.Attacks = 1; quote.IsRanged = false; Check(!Matches(quote, first), "melee estimate cannot become a ranged shot");
                    Check(!Matches(null, first) && !Matches(quote, null), "missing estimate/weapon remain unknown");
                    var estimate = SaveBoostCheck.ReferencedMembers(typeof(CombatMath).GetMethod("EstimateRangedShot", all)).ToArray();
                    Check(estimate.Any(m => m.Name == "GetThreatHandRanged") && estimate.Any(m => m.Name == "EstimateWeaponAttack") &&
                          estimate.Any(m => m.Name == "RangedShotMatches"), "compiled forecast uses the native hand and tested single-routine guard");
                    Check(!estimate.Any(m => m.Name == "RunAction" || m.Name == "AddBuff" || m.Name == "TriggerRule"), "forecast executes no action, buff application or attack rule");
                    var value = SaveBoostCheck.ReferencedMembers(typeof(TacticalEngine).GetMethod("RangedShotValue", all)).ToArray();
                    Check(value.Any(m => m.Name == "EstimateRangedShot") && value.Any(m => m.Name == "DamageValue") &&
                          value.Any(m => m.Name == "WeaponProvokes"), "compiled value uses the shared damage/kill and native weapon-provoke paths");
                    Check(Read() != null, "all fixture changes were restored");
                }
            }
            catch (Exception ex) { bad++; lines.Add("FOCUSED-SHOT FAILED: " + ex.GetBaseException()); }
            failures = bad;
            lines.Insert(0, $"FOCUSED-SHOT {checks} checks, failures {bad}; exported graphs, native scalar bonus and compiled routes; no actor, attack, buff application or Unity timing execution");
            return lines;
        }
    }
}
