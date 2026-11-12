using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using CompanionAI_Pathfinder.Analysis;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Blueprints;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Buffs.Blueprints;

namespace ProfileAudit
{
    internal static class ResistanceMemoCheck
    {
        private delegate bool TryRead(UnitEntityData caster,UnitEntityData target,AbilityData source,out float result);
        private const BindingFlags Static = BindingFlags.NonPublic | BindingFlags.Static;
        internal static List<string> Run(IEnumerable<SimpleBlueprint> loaded,out int failures)
        {
            int checks=0,bad=0;var lines=new List<string>();
            void Check(bool ok,string why){checks++;if(!ok){bad++;lines.Add("RESISTANCE-MEMO FAILED: "+why);}}
            var all=loaded.ToList();var death=all.OfType<BlueprintAbility>().FirstOrDefault(a=>a.name=="DeathWard");
            if(death==null){failures=0;return new List<string>{"RESISTANCE-MEMO SKIPPED: DeathWard is outside this input scope"};}
            try
            {
                var begin=(Func<IDisposable>)typeof(CombatMath).GetMethod("MemoizeResistance",Static).CreateDelegate(typeof(Func<IDisposable>));
                var read=(TryRead)typeof(CombatMath).GetMethod("TryResistanceMemo",Static).CreateDelegate(typeof(TryRead));
                var store=(Action<UnitEntityData,UnitEntityData,AbilityData,float>)typeof(CombatMath).GetMethod("StoreResistanceMemo",Static)
                    .CreateDelegate(typeof(Action<UnitEntityData,UnitEntityData,AbilityData,float>));
                var invalidate=(Action)typeof(CombatMath).GetMethod("InvalidateResistanceMemo",Static).CreateDelegate(typeof(Action));
                var caster=(UnitEntityData)RuntimeHelpers.GetUninitializedObject(typeof(UnitEntityData));
                var target=(UnitEntityData)RuntimeHelpers.GetUninitializedObject(typeof(UnitEntityData));
                var other=(UnitEntityData)RuntimeHelpers.GetUninitializedObject(typeof(UnitEntityData));
                var ability=(AbilityData)RuntimeHelpers.GetUninitializedObject(typeof(AbilityData));
                var copy=(AbilityData)RuntimeHelpers.GetUninitializedObject(typeof(AbilityData));
                store(caster,target,ability,.4f);Check(!read(caster,target,ability,out _),"unscoped callers retain native reads");
                using(begin())
                {
                    Check(!read(caster,target,ability,out _),"new evaluation has no stale result");store(caster,target,ability,0f);
                    Check(read(caster,target,ability,out float immune)&&immune==0f,"native immune zero survives reuse");
                    Check(!read(caster,target,copy,out _),"distinct native ability copies remain distinct");
                    Check(!read(caster,other,ability,out _),"distinct native recipients remain distinct");
                    Check(!read(other,target,ability,out _),"distinct native casters remain distinct");
                    using(begin())
                    {
                        Check(!read(caster,target,ability,out _),"nested evaluation is isolated");store(caster,target,ability,.612345f);
                        Check(read(caster,target,ability,out float exact)&&exact==.612345f,"full native probability retained");
                    }
                    Check(read(caster,target,ability,out float restored)&&restored==0f,"outer evaluation restored after nested disposal");
                    invalidate();Check(!read(caster,target,ability,out _),"state preview epoch invalidates open scope");
                    store(caster,target,ability,1f);Check(read(caster,target,ability,out float bypass)&&bypass==1f,"new native bypass result retained");
                }
                Check(!read(caster,target,ability,out _),"end of evaluation releases result access");
                using(begin())Check(!read(caster,target,ability,out _),"next evaluation is fresh in the same offline frame");

                var equipment=typeof(TacticalEngine).GetMethod("BuffContextAddsEquipmentParts",Static);
                var numerical=typeof(TacticalEngine).GetMethod("HasReadableParts",Static);
                var dp=AbilityProfile.Get(death);var parts=dp.TargetBuffs.SelectMany(b=>BuffPartsReader.Read(b).Parts).ToList();
                Check(dp.TargetBuffs.Count>0 && (bool)numerical.Invoke(null,new object[]{parts}),
                    "actual Death Ward has numerical parts and retains its source context");
                var empty=all.OfType<BlueprintAbility>().Select(AbilityProfile.Get).FirstOrDefault(p=>p.TargetBuffs.Count>0 &&
                    !(bool)numerical.Invoke(null,new object[]{p.TargetBuffs.SelectMany(b=>BuffPartsReader.Read(b).Parts).ToList()}) &&
                    !(bool)equipment.Invoke(null,new object[]{p,p.TargetBuffs}));
                Check(empty!=null,"actual non-numerical non-equipment buff exists for the early return");
                if(empty!=null)lines.Add("RESISTANCE-MEMO empty-appraisal fixture: "+empty.TargetBuffs[0].name);
                var weapon=all.OfType<BlueprintAbility>().Select(AbilityProfile.Get).FirstOrDefault(p=>p.WeaponEnchants.Count>0);
                Check(weapon!=null && (bool)equipment.Invoke(null,new object[]{weapon,weapon.TargetBuffs}),"actual weapon-enchantment carrier keeps context path");
                var armor=all.OfType<BlueprintBuff>().FirstOrDefault(b=>b.GetComponent<Kingmaker.UnitLogic.FactLogic.BuffEnchantArmor>()!=null);
                Check(armor!=null && (bool)equipment.Invoke(null,new object[]{dp,new List<BlueprintBuff>{armor}}),"actual armor-enchantment carrier keeps context path");
                var haste=all.OfType<BlueprintAbility>().FirstOrDefault(a=>a.name=="Haste");
                var hp=AbilityProfile.Get(haste);var numericalParts=hp.TargetBuffs.SelectMany(b=>BuffPartsReader.Read(b).Parts).ToList();
                Check((bool)numerical.Invoke(null,new object[]{numericalParts}),"actual numerical Haste appraisal keeps its context");
            }
            catch(Exception ex){bad++;lines.Add("RESISTANCE-MEMO FAILED: "+ex.GetBaseException());}
            failures=bad;lines.Insert(0,$"RESISTANCE-MEMO {checks} checks, failures {bad}; native identity/scope bindings and actual buff carriers; no actor setup, native resistance query or command executed");return lines;
        }
    }
}
