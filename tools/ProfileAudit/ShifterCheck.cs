// Actual exported forms/aspects, native scalar dice table and compiled state/issue contracts. No scene actors.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CompanionAI_Pathfinder.Analysis;
using CompanionAI_Pathfinder.GameInterface;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Items.Weapons;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Enums;
using Kingmaker.RuleSystem;
using Kingmaker.RuleSystem.Rules;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.ActivatableAbilities;
using Kingmaker.UnitLogic.Buffs;
using Kingmaker.UnitLogic.Buffs.Blueprints;

namespace ProfileAudit
{
    internal static class ShifterCheck
    {
        internal static List<string> Run(IEnumerable<SimpleBlueprint> loaded, out int failures)
        {
            int checks = 0, bad = 0; var lines = new List<string>();
            void Check(bool ok, string why) { checks++; if (!ok) { bad++; lines.Add("SHIFTER FAILED: " + why); } }
            try
            {
                const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
                var blueprints = loaded.ToArray();
                var mod = typeof(TacticalEngine).Assembly;
                var facts = mod.GetType("CompanionAI_Pathfinder.Analysis.PolymorphFacts");
                var gives = facts.GetMethod("GivesForm", all);
                var formType = mod.GetType("CompanionAI_Pathfinder.Tactics.ShifterFormProjection");
                var supports = formType.GetMethod("Supports", all);
                var forms = blueprints.OfType<BlueprintBuff>().Where(b => b.name.StartsWith("Shifter", StringComparison.Ordinal) && b.GetComponent<Polymorph>() != null).ToArray();
                Check(forms.Length == 86, "all 86 actual exported Shifter polymorph buff graphs are present");
                int supported = forms.Count(b => (bool)supports.Invoke(null, new object[] { b.GetComponent<Polymorph>() }));
                lines.Add($"SHIFTER layouts: {supported}/{forms.Length} exported form buffs satisfy the bounded natural layout contract; not runtime class coverage");
                lines.Add("SHIFTER other layouts: " + string.Join(", ", forms.Where(b => !(bool)supports.Invoke(null, new object[] { b.GetComponent<Polymorph>() })).Select(b => b.name)));
                foreach (string name in new[] { "ShifterWildShapeGriffonBuff", "ShifterWildShapeGriffonBuff9", "ShifterWildShapeGriffonBuff14", "ShifterWildShapeBearBuff", "ShifterWildShapeWolfBuff", "ShifterWildShapeDinosaurBuff", "ShifterWildShapeSpiderBuff", "ShifterWildShapeManticoreBuff" })
                {
                    var buff = forms.FirstOrDefault(b => b.name == name);
                    Check(buff != null && (bool)supports.Invoke(null, new object[] { buff.GetComponent<Polymorph>() }), "actual core form layout supported: " + name);
                }
                var griffon = forms.First(b => b.name == "ShifterWildShapeGriffonBuff").GetComponent<Polymorph>();
                Check(griffon.Size == Size.Large && griffon.StrengthBonus == 4 && griffon.DexterityBonus == -2 && griffon.NaturalArmor == 4,
                    "Ulbrig's actual base form has Large/+4 Strength/-2 Dexterity/+4 natural armor metadata");
                Check(!griffon.KeepSlots && griffon.MainHand == null && griffon.OffHand == null && griffon.AdditionalLimbs.Count() == 3,
                    "Ulbrig's actual form replaces slots with three natural attacks");
                var shift = typeof(CombatMath).GetMethod("ShifterDiceShift", all);
                int diceShift = (int)shift.Invoke(null, new object[] { griffon, Size.Medium });
                var claw = griffon.AdditionalLimbs.First();
                var projected = WeaponDamageScaleTable.Scale(claw.Damage, Size.Medium.Shift(diceShift), Size.Medium, claw);
                var native = WeaponDamageScaleTable.Scale(claw.Damage, griffon.Size, griffon.Size, claw);
                Check(diceShift == 0 && projected == native && projected == claw.Damage, "Large griffon dice are not incorrectly enlarged a second time");
                Check((int)shift.Invoke(null, new object[] { griffon, Size.Large }) == -1, "projection normalizes an already-sized input weapon to the native dice base");
                var skipScaling = typeof(CombatMath).GetMethod("SkipFormScaling", all);
                Check((bool)skipScaling.Invoke(null, new object[] { claw, false }) == claw.IsDamageDiceOverridden,
                    "unmodified overridden form dice retain native scaling exemption");
                Check(!(bool)skipScaling.Invoke(null, new object[] { claw, true }), "transferred/modified dice retain native scaling route");
                var upkeep = typeof(RealTimeController).GetMethod("IsWorthKeepingUp", all);
                foreach (string name in new[] { "ShifterWildShapeGriffonAbillity", "ShifterWildShapeGriffonAbillity9", "ShifterWildShapeGriffonAbillity14", "ShifterWildShapeBearAbillity" })
                {
                    var ability = blueprints.OfType<BlueprintAbility>().FirstOrDefault(b => b.name == name);
                    Check(ability != null && (bool)gives.Invoke(null, new object[] { AbilityProfile.Get(ability) }), "actual ability graph is excluded from automatic OOC form upkeep: " + name);
                    Check(ability != null && !(bool)upkeep.Invoke(null, new object[] { CasterPolicyCheck.Data(ability), null }),
                        "production automatic upkeep rejects exported form before requiring an actor: " + name);
                }
                var haste = blueprints.OfType<BlueprintAbility>().First(b => b.name == "Haste");
                Check(!(bool)gives.Invoke(null, new object[] { AbilityProfile.Get(haste) }), "ordinary numeric buff is not classified as a transformation");
                var branch = typeof(TacticalEngine).GetMethod("KnownFormBranch", all);
                foreach (var applies in new[] { EffectApplies.Unconditional, EffectApplies.Yes, EffectApplies.No, EffectApplies.Unknown })
                    Check((bool)branch.Invoke(null, new object[] { applies }) == (applies == EffectApplies.Yes || applies == EffectApplies.Unconditional),
                        "form forecast admission follows actual effect status " + applies);

                var secondary = typeof(CombatMath).GetMethod("FurySecondary", all);
                Check((bool)secondary.Invoke(null, new object[] { claw, true }) == false, "selected claw remains primary");
                Check((bool)secondary.Invoke(null, new object[] { claw, false }), "other claw becomes secondary under Fury");
                // Native-layout variation of the actual claw, kept out of the loaded corpus. The stock items in this
                // export do not set this flag, but native/modded blueprints can; ordinary form rules must not leak in.
                var alwaysPrimary = (BlueprintItemWeapon)typeof(object).GetMethod("MemberwiseClone", all).Invoke(claw, null);
                typeof(BlueprintItemWeapon).GetField("m_AlwaysPrimary", all).SetValue(alwaysPrimary, true);
                Check(alwaysPrimary.AlwaysPrimary && (bool)secondary.Invoke(null, new object[] { alwaysPrimary, false }), "Fury overrides native AlwaysPrimary on a cloned native-layout claw fixture");
                var unarmed = blueprints.OfType<BlueprintItemWeapon>().First(w => w.Type != null && w.IsUnarmed);
                Check(secondary.Invoke(null, new object[] { unarmed, false }) == null, "Fury leaves unarmed stats to their ordinary native rules");
                var selector = blueprints.OfType<BlueprintActivatableAbility>().First(b => b.name == "ShifterFuryWeaponList");
                Check(selector.Group == ActivatableAbilityGroup.None && !selector.ActivateWithUnitCommand && selector.ComponentsArray.Length == 1 &&
                    selector.GetComponent<ShiftersFuryItemAbility>() != null, "actual generated selector has the inspected free native contract");
                var aspect = blueprints.OfType<BlueprintActivatableAbility>().First(b => b.name == "ShifterAspectGriffonAbility");
                var toggleProfile = typeof(TacticalEngine).GetMethod("ProfileOf", all).Invoke(null, new object[] { aspect });
                Check(aspect.Group == ActivatableAbilityGroup.ShifterAspect && toggleProfile.GetType().GetField("Excluded").GetValue(toggleProfile) == null,
                    "Ulbrig's minor aspect reaches existing command-toggle/resource/group planner");
                var parts = BuffPartsReader.Read(aspect.Buff).Parts;
                Check(parts.Any(p => p.Kind == BuffPartKind.Strength) && parts.Any(p => p.Kind == BuffPartKind.Saves), "actual Griffon minor aspect reads both Strength and Will benefits");

                bool Calls(MethodInfo m, string name) => SaveBoostCheck.ReferencedMembers(m).Any(x => x.Name == name);
                Check(Calls(typeof(RealTimeController).GetMethod("IsWorthKeepingUp", all), "GivesForm"), "automatic OOC upkeep reaches native form exclusion");
                var hasForm = facts.GetMethod("HasForm", all);
                Check(Calls(hasForm, "get_IsPolymorphed") && Calls(hasForm, "Form") && !Calls(hasForm, "get_Active"), "existing/suppressed blueprint form guard does not filter on active status");
                Check(Calls(typeof(TacticalEngine).GetMethod("AppraiseBuff", all), "AppraiseShifterForm"), "generic buff appraisal reaches dedicated form model");
                Check(Calls(typeof(TacticalEngine).GetMethod("Execute", all), "ValidateShifterIssue"), "final native command issue rechecks form/Fury state");
                var projectedStats = typeof(CombatMath).GetNestedType("ShifterStats", all).GetMethod("OnTrigger", all);
                Check(Calls(projectedStats, "set_IsSecondaryOverride") && Calls(projectedStats, "ShifterDiceShift"), "projected rule sets native secondary and dice-base inputs");
                Check(!SaveBoostCheck.ReferencedMembers(projectedStats).Any(m => m.Name == "ApplyPolymorphEffect" || m.Name == "AddFact" || m.Name == "AddBuff" || m.Name == "set_IsOn"),
                    "projection never applies a form, fact, buff or toggle to an actor");
                var count = typeof(CombatMath).GetNestedType("FuryCount", all).GetMethod("Configure", all);
                var limbs = typeof(CombatMath).GetNestedType("FuryLimbs", all).GetMethod("Configure", all);
                Check(Calls(count, "set_ForcePrimaryAttackWeapon") && Calls(limbs, "set_ForcePrimaryAttackWeapon"), "Fury uses separate native hand/limb force-primary rule inputs");
                var nativeStats = typeof(RuleCalculateWeaponStats).GetMethod("OnTrigger", all);
                Check(Calls(nativeStats, "get_IsSecondaryOverride") && Calls(nativeStats, "get_OverrideDamageBonus"), "supplied native stats consumes projected secondary/damage-multiplier inputs");
                var nativeSize = typeof(RuleCalculateWeaponSizeBonus).GetMethod("GetBaseDiceSize", all);
                Check(SaveBoostCheck.ReferencedMembers(nativeSize).Any(m => m.Name == "UseSizeAsBaseForDamage"), "supplied native dice rule changes its base size for polymorph");
                var execute = typeof(TacticalEngine).GetMethod("ExecuteShifterFury", all);
                Check(Calls(execute, "RecordToggle") && Calls(execute, "MagusStateChanged"), "real selection records original off state and invalidates attack/appraisal caches");
                var writes = SaveBoostCheck.ReferencedMembers(execute).ToList();
                Check(writes.FindIndex(m => m.Name == "RecordToggle") < writes.FindIndex(m => m.Name == "set_IsOn"),
                    "native activation attempt records original off state before a setter can partially succeed/throw");
                Check(Calls(execute, "IsFuryWeapon"), "actual selected weapon identity is verified after the native setter");
                Check(Calls(typeof(TacticalEngine).GetMethod("ValidFuryChoice", all), "FurySelectionPresent") &&
                    Calls(typeof(TacticalEngine).GetMethod("FurySelectionPresent", all), "get_IsStarted"),
                    "pending player/native selections are preserved before an applied Fury buff exists");
            }
            catch (Exception ex) { bad++; lines.Add("SHIFTER FAILED: " + ex.GetBaseException()); }
            failures = bad;
            lines.Insert(0, $"SHIFTER {checks} checks, failures {bad}; exported forms/aspect/selector, native scalar dice table and compiled guard/projection routes; no actor, command, transformation, combat or Unity timing execution");
            return lines;
        }
    }
}
