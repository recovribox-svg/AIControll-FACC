// Offensive on-hit utility: actual disease graphs and inert action lists, no world/actor setup or Rulebook execution.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.ElementsSystem;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.RuleSystem;
using Kingmaker.UnitLogic.Buffs;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.UnitLogic.Mechanics.Components;
using Kingmaker.Designers.EventConditionActionSystem.Actions;

namespace ProfileAudit
{
    internal static class OffensiveAfflictionCheck
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        private static List<Kingmaker.Blueprints.Items.Weapons.BlueprintItemWeapon> all_weapons(IEnumerable<SimpleBlueprint> loaded) =>
            loaded.OfType<Kingmaker.Blueprints.Items.Weapons.BlueprintItemWeapon>().ToList();

        internal static List<string> Run(IEnumerable<SimpleBlueprint> loaded, out int failures)
        {
            var lines = new List<string>(); int checks = 0, bad = 0;
            void Check(bool ok, string why) { checks++; if (!ok) { bad++; lines.Add("OFFENSIVE-AFFLICTION FAILED: " + why); } }
            try
            {
                var math = typeof(CombatMath);
                var read = math.GetMethod("ReadOffensiveAffliction", Any);
                var harm = math.GetMethod("ReadAffliction", Any);
                var triggerRead = math.GetMethod("SupportedOffensiveTrigger", Any);
                var suitsBlueprint = math.GetMethod("RiderSuitsBlueprint", Any);
                bool Suits(AddInitiatorAttackWithWeaponTrigger t, Kingmaker.Blueprints.Items.Weapons.BlueprintItemWeapon w) => (bool)suitsBlueprint.Invoke(null, new object[] { t, w });
                var weapons = all_weapons(loaded);
                ActionList Actions(params GameAction[] a) => new ActionList { Actions = a };
                object Benefit(ActionList a) => read.Invoke(null, new object[] { a, null, 15, null });
                object Harm(ActionList a) => harm.Invoke(null, new object[] { a, null, 15 });
                float Points(object a) { var p=(IDictionary)a.GetType().GetField("Points").GetValue(a);return p.Contains(StatType.Strength)?(float)p[StatType.Strength]:0f; }
                var all = loaded.OfType<BlueprintScriptableObject>().ToList();
                var gray = all.OfType<BlueprintBuff>().SingleOrDefault(b=>b.AssetGuid.ToString()=="02c357d8218b3fb4b9ffeb86953b8604");
                var disease = all.SingleOrDefault(b=>b.name=="Schir_DiseaseFeature");
                var shadow = all.SingleOrDefault(b=>b.name=="Shadow_Feature_StrengthDamage");
                Check(gray!=null && gray.Frequency==DurationRate.Days && !gray.TickEachSecond &&
                      gray.GetComponents<AddFactContextActions>().All(c=>!(c.Activated?.Actions?.Any()??false)),
                    "actual Gray Pox: daily ticks and no activated damage");
                var apply = new ContextActionApplyBuff { Permanent=true };
                var reference = new BlueprintBuffReference();
                typeof(BlueprintReferenceBase).GetProperty("Cached",Any).SetValue(reference,gray);
                typeof(ContextActionApplyBuff).GetField("m_Buff",Any).SetValue(apply,reference);
                Check(Points(Benefit(Actions(apply)))==0f && Points(Harm(Actions(apply)))>0f,
                    "daily disease earns no combat-tick benefit; incoming conservative disease forecast is retained");
                var diseaseTrigger=disease?.GetComponent<AddInitiatorAttackWithWeaponTrigger>();
                Check(diseaseTrigger!=null && diseaseTrigger.WeaponType!=null && (bool)triggerRead.Invoke(null,new object[]{diseaseTrigger}),
                    "actual Schir weapon-filtered disease is read per attack line (its weapon filter is applied line by line)");
                var ofType = weapons.FirstOrDefault(w => diseaseTrigger != null && w.Type == diseaseTrigger.WeaponType);
                var gore = weapons.FirstOrDefault(w => w.Type != null && w.Type.IsNatural && diseaseTrigger != null && w.Type != diseaseTrigger.WeaponType);
                Check(ofType != null && gore != null && Suits(diseaseTrigger, ofType) && !Suits(diseaseTrigger, gore),
                    "Schir's disease suits a weapon of its own type and not a natural weapon of another type (its gore)");
                Check(diseaseTrigger!=null && Points(Benefit(diseaseTrigger.Action))==0f && Points(Harm(diseaseTrigger.Action))>0f,
                    "actual Schir success guard/day ticks do not manufacture positive Strength damage");
                var shadowTrigger=shadow?.GetComponent<AddInitiatorAttackWithWeaponTrigger>();
                var shadowTouch = weapons.SingleOrDefault(w => w.AssetGuid.ToString() == "d427ae873c885444b8fbbef7f73a5bc2");
                Check(shadowTrigger!=null && (bool)triggerRead.Invoke(null,new object[]{shadowTrigger}) && shadowTouch != null &&
                      Suits(shadowTrigger, shadowTouch) && (ofType == null || !Suits(shadowTrigger, ofType)),
                    "actual Shadow Strength rider is supported and suits the Shadow's own touch, not another weapon type");
                Check(shadowTrigger!=null && Points(Benefit(shadowTrigger.Action))==0f,
                    "Shadow's target-fact gate earns nothing without a readable target and context");
                var factGate = math.GetMethod("ReadOnlyTargetFactCondition", Any);
                Check(factGate != null && (bool)factGate.Invoke(null, new object[] { new Kingmaker.UnitLogic.Mechanics.Conditions.ContextConditionHasFact() }) &&
                      (bool)factGate.Invoke(null, new object[] { new Kingmaker.UnitLogic.Mechanics.Conditions.ContextConditionHasBuff() }) &&
                      !(bool)factGate.Invoke(null, new object[] { new Kingmaker.UnitLogic.Mechanics.Conditions.ContextConditionHitDice() }),
                    "target buff and fact gates are read through the native checker; other gates stay withheld");
                var shadowDamage=shadowTrigger?.Action?.Actions?.OfType<Conditional>().FirstOrDefault()?.IfTrue?.Actions?
                    .OfType<ContextActionDealDamage>().FirstOrDefault();
                Check(shadowDamage!=null && Math.Abs(Points(Benefit(Actions(shadowDamage)))-3.5f)<.001f,
                    "Shadow's actual inner 1d6 Strength action retains readable immediate damage once applicability is established");
                var conditional=new Conditional { ConditionsChecker=new ConditionsChecker(),IfTrue=Actions(shadowDamage),IfFalse=Actions() };
                Check(Points(Benefit(Actions(conditional)))==0f && Points(Harm(Actions(conditional)))>0f,
                    "a conditional without a readable effect context earns no positive branch; harm still chooses its larger branch");
                var filtered = new AddInitiatorAttackWithWeaponTrigger { OnlyHit=true,OnlyOnFirstHit=true };
                Check((bool)triggerRead.Invoke(null,new object[]{filtered}), "first-hit trigger is read (OnHitRules counts it once at most)");
                foreach (var withheld in new[] { new AddInitiatorAttackWithWeaponTrigger { OnlyHit=true, CriticalHit=true },
                                                 new AddInitiatorAttackWithWeaponTrigger { OnlyHit=true, OnlySneakAttack=true },
                                                 new AddInitiatorAttackWithWeaponTrigger { OnlyHit=true, CheckDistance=true },
                                                 new AddInitiatorAttackWithWeaponTrigger { OnlyHit=true, OnCharge=true } })
                    Check(!(bool)triggerRead.Invoke(null,new object[]{withheld}), "roll- or moment-dependent trigger flags stay withheld");
                var plain=new AddInitiatorAttackWithWeaponTrigger { OnlyHit=true };
                Check((bool)triggerRead.Invoke(null,new object[]{plain}),"unfiltered on-hit trigger retains the direct reader");
                var frequency=SaveBoostCheck.ReferencedMembers(typeof(BlueprintBuff).GetMethod("get_TickTime",Any)).ToList();
                Check(frequency.Any(m=>m.Name=="Frequency") && frequency.Any(m=>m.Name=="ToRounds"),
                    "native BlueprintBuff.TickTime converts the configured frequency through its own duration rule");
                Check(BuffSuppressCheck.BodyWithLambdas(typeof(Buff),"OnAttach").Any(m=>m.Name=="get_TickTime") &&
                      BuffSuppressCheck.BodyWithLambdas(typeof(Buff),"TickMechanics").Any(m=>m.Name=="get_NextTickTime"),
                    "native buff lifecycle schedules the next tick from that interval, not each combat round");
                var onHp=BuffSuppressCheck.BodyWithLambdas(math,"OnHitHp");
                Check(onHp.Any(m=>m.Name=="OffensiveRiders") && onHp.Any(m=>m.Name=="RiderSuitsWeapon") &&
                      onHp.Any(m=>m.DeclaringType==typeof(OnHitRules)&&m.Name=="FiringHits") && !onHp.Any(m=>m.Name=="OnHitAffliction"),
                    "weapon benefit uses the stricter positive reader, line by line");
                foreach (var quote in new[] { "EstimateWeaponAttackCore", "GriffonModeQuote" })
                    Check(SaveBoostCheck.ReferencedMembers(math.GetMethod(quote, Any)).Any(m => m.Name == "LineHits"),
                        quote + " records each attack line's hit chance for the per-line rider reader");
                var onShare=BuffSuppressCheck.BodyWithLambdas(math,"OnHitShare");
                Check(onShare.Any(m=>m.Name=="OnHitAffliction") && !onShare.Any(m=>m.Name=="OffensiveRiders"),
                    "incoming harm share retains the conservative reader");
                var walk=BuffSuppressCheck.BodyWithLambdas(math,"WalkAffliction");
                Check(walk.Any(m=>m.DeclaringType==typeof(AbilityProfile)&&m.Name=="TryReadConditions"),
                    "readable target-buff gates use the existing native condition checker, not copied condition tables");
                var weaponValue=typeof(TacticalEngine).GetMethods(Any).Single(m=>m.Name=="DamageValue"&&m.GetParameters().Length==3&&
                    m.GetParameters()[1].ParameterType==typeof(WeaponAttackEstimate));
                var weaponBody=SaveBoostCheck.ReferencedMembers(weaponValue).ToList();
                Check(weaponBody.Any(m=>m.Name=="OnHitHp") && weaponBody.OfType<MethodInfo>().Any(m=>m.Name=="DamageValue"&&m.GetParameters().Length==6),
                    "weapon on-hit utility reaches the six-argument HP/utility-separated damage value");
                var scalarValue=typeof(TacticalEngine).GetMethods(Any).Single(m=>m.Name=="DamageValue"&&m.GetParameters().Length==6);
                var scalarBody=SaveBoostCheck.ReferencedMembers(scalarValue).ToList();
                Check(scalarBody.Any(m=>m.DeclaringType==typeof(ValueModel)&&m.Name=="DamageWithConsequences") &&
                      scalarBody.Any(m=>m.DeclaringType==typeof(ValueModel)&&m.Name=="KillChance"),
                    "HP-only kill chance and independently weighted on-hit consequences share the pure tested value model");
                Check(BuffSuppressCheck.BodyWithLambdas(typeof(CombatMemory),"ResetCombat").Any(m=>
                          m.DeclaringType==math&&m.Name=="ResetAfflictionSources") &&
                      BuffSuppressCheck.BodyWithLambdas(math,"ResetAfflictionSources").Count(m=>m.Name=="Clear")==3,
                    "the fact-bearing source and actor lookup caches clear at every combat reset");
                Check(BuffSuppressCheck.BodyWithLambdas(math,"OffensiveRiders").Any(m=>m.Name=="get_Active"),
                    "an inactive cached source cannot earn offensive benefit");
            }
            catch(Exception ex) { Check(false,"exception "+ex.GetBaseException()); }
            failures=bad;
            lines.Insert(0,$"OFFENSIVE-AFFLICTION {checks} checks, {bad} failures; native disease/Shadow graphs and routes, no actor or damage execution");
            return lines;
        }
    }
}
