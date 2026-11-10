using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Blueprints;
using Kingmaker.Designers.Mechanics.Buffs;
using Kingmaker.ElementsSystem;
using Kingmaker.EntitySystem;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.RuleSystem;
using Kingmaker.UnitLogic.Buffs;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.UnitLogic.Mechanics.Components;

namespace ProfileAudit
{
    internal static class AttachedDamageCheck
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        private static object Field(object obj, string field) => obj.GetType().GetField(field, Any).GetValue(obj);
        private static Delegate Reader(Type delegateType, Func<object, object> read)
        {
            var p = Expression.Parameter(delegateType.GenericTypeArguments[0]);
            return Expression.Lambda(delegateType, Expression.Convert(Expression.Invoke(Expression.Constant(read),
                Expression.Convert(p, typeof(object))), delegateType.GenericTypeArguments[1]), p).Compile();
        }

        internal static List<string> Run(IEnumerable<SimpleBlueprint> loaded, out int failures)
        {
            int checks = 0, bad = 0;
            var lines = new List<string>();
            void Check(bool ok, string why) { checks++; if (!ok) { bad++; lines.Add("ATTACHED FAILED: " + why); } }
            var buffs = loaded.OfType<BlueprintBuff>().ToList();
            var cling = buffs.SingleOrDefault(b => b.AssetGuid.ToString() == "2fea5f88e47e42b1a0037367dcb20470");
            if (cling == null) { failures = 0; return new List<string> { "ATTACHED SKIPPED: TickSwarmClingBuff is outside this scope" }; }
            try
            {
                var math = typeof(CombatMath);
                var planOf = math.GetMethod("PeriodicPlanOf", Any);
                bool Supported(object plan) => (bool)plan.GetType().GetProperty("Supported", Any).GetValue(plan);
                var plan = planOf.Invoke(null, new object[] { cling });
                Check(Supported(plan), "actual Cling graph and all child metadata bind");
                Check((bool)Field(plan, "HitPoints"), "actual Cling includes native HP damage");
                var removals = (IList)Field(plan, "EnergyRemovals");
                Check(removals.Count == 4 && removals.Cast<object>().All(x => !(bool)x.GetType().GetField("Item2").GetValue(x)),
                    "four actual energy removals do not require spells");
                Check(ReferenceEquals(plan, planOf.Invoke(null, new object[] { cling })), "metadata cache reuses plan");
                var disease = buffs.SingleOrDefault(b => b.AssetGuid.ToString()=="103aac6bc1cfd454492cee1fd680db05");
                Check(disease!=null && Supported(planOf.Invoke(null,new object[]{disease})), "actual secondary disease's native poison component and passive fatigue bind");

                var read = math.GetMethod("ReadPeriodicTick", Any);
                var affliction = math.GetNestedType("Affliction", Any);
                object Harm() => Activator.CreateInstance(affliction, true);
                var damage = Reader(read.GetParameters()[2].ParameterType, value => {
                    var d = (ContextActionDealDamage)value; var h = Harm();
                    float average = d.Value.DiceCountValue.Value * (d.Value.DiceType.Sides() + 1f) / 2f + d.Value.BonusValue.Value;
                    if (d.AbilityType == StatType.Constitution) ((IDictionary)Field(h,"Points"))[StatType.Constitution] = average;
                    else h.GetType().GetField("HitPoints", Any).SetValue(h, average);
                    return h;
                });
                var applied = Reader(read.GetParameters()[3].ParameterType, value => Harm());
                object Tick(float f) => read.Invoke(null, new object[] { Field(plan,"Round"), new Func<ContextActionSavingThrow,float>(_=>f), damage, applied });
                foreach (float f in new[] { 0f, .5f, 1f })
                {
                    var tick = Tick(f); var h = Field(tick,"Harm");
                    Check(Math.Abs((float)Field(h,"HitPoints") - 14f*f)<.001f, "actual 4d6 HP weighted by Reflex " + f);
                    var points = (IDictionary)Field(h,"Points");
                    float con = points.Contains(StatType.Constitution) ? (float)points[StatType.Constitution] : 0f;
                    Check(Math.Abs(con - 2.5f*f)<.001f && Math.Abs((float)Field(tick,"Survives")-f)<.001f,
                        "Constitution and next-tick survival follow the same save " + f);
                }
                var nested = read.Invoke(null, new object[] { Field(plan,"Round"),
                    new Func<ContextActionSavingThrow,float>(s => s.Type == SavingThrowType.Reflex ? .5f : .25f), damage,
                    Reader(read.GetParameters()[3].ParameterType, value => { var h=Harm();h.GetType().GetField("HitPoints",Any).SetValue(h,8f);return h; }) });
                Check(Math.Abs((float)Field(Field(nested,"Harm"),"HitPoints")-9f)<.001f,
                    "two secondary Fortitude branches retain independent probabilities inside failed Reflex");

                BlueprintBuff With(params GameAction[] actions) => new BlueprintBuff { ComponentsArray = new BlueprintComponent[] {
                    new AddFactContextActions { NewRound = new ActionList { Actions = actions } } } };
                Check(!Supported(planOf.Invoke(null,new object[]{With(new Kingmaker.Designers.EventConditionActionSystem.Actions.Conditional())})),
                    "unread/spatial Conditional is rejected instead of guessing a branch");
                Check(!Supported(planOf.Invoke(null,new object[]{With(new ContextActionConditionalSaved())})), "saved branch needs a real save scope");
                Check(!Supported(planOf.Invoke(null,new object[]{With(new ContextActionDealDamage { Value = new ContextDiceValue {
                    DiceType=DiceType.D6,DiceCountValue=new ContextValue { ValueType=ContextValueType.Shared },BonusValue=0 } })})),
                    "unread shared dice are rejected");
                Check(!Supported(planOf.Invoke(null,new object[]{With(new ContextActionDealDamage { AddAdditionalDamage=true,
                    Value=new ContextDiceValue { DiceType=DiceType.D6,DiceCountValue=1,BonusValue=0 } })})),
                    "additional caster damage needs a separate contract");

                var buff = (Buff)RuntimeHelpers.GetUninitializedObject(typeof(Buff));
                void SetTime(string name,double seconds) => typeof(Buff).GetProperty(name,Any).SetValue(buff,TimeSpan.FromSeconds(seconds));
                SetTime("NextTickTime",16);SetTime("TickTime",6);
                typeof(Buff).GetField("m_EndTime",Any).SetValue(buff,TimeSpan.FromSeconds(16));
                var schedule = math.GetMethod("ReadAttachedTicks",Any);
                object[] a = {buff,TimeSpan.FromSeconds(10),18d,0,0};
                Check((bool)schedule.Invoke(null,a) && (int)a[3]==1 && (int)a[4]==1, "native schedule ticks at equal expiry without a Game clock");
                typeof(Buff).GetField("m_EndTime",Any).SetValue(buff,null);
                a = new object[]{buff,TimeSpan.FromSeconds(10),18d,0,0};
                Check((bool)schedule.Invoke(null,a) && (int)a[3]==1 && (int)a[4]==3, "permanent native instance keeps its next tick");
                SetTime("TickTime",.01);
                Check(!(bool)schedule.Invoke(null,a), "unsupported subsecond cadence cannot create unbounded preview work");

                var poison = new BuffPoisonStatDamage { Stat=StatType.Constitution,SaveType=SavingThrowType.Fortitude,Ticks=6,SuccesfullSaves=2 };
                var t=poison.GetType().BaseType;
                while(t!=null && (!t.IsGenericType || t.GetGenericTypeDefinition().FullName!="Kingmaker.EntitySystem.EntityFactComponentDelegate`2")) t=t.BaseType;
                var runtimeType=t.GetNestedType("ComponentRuntime",Any);
                if(runtimeType.ContainsGenericParameters)runtimeType=runtimeType.MakeGenericType(t.GenericTypeArguments);
                var runtime=(EntityFactComponent)RuntimeHelpers.GetUninitializedObject(runtimeType);
                typeof(EntityFactComponent).GetField("<SourceBlueprintComponent>k__BackingField",Any).SetValue(runtime,poison);
                var data=new BuffPoisonStatDamageData { TicksPassed=3,SavesSucceeded=1,BonusDC=4 };
                runtimeType.GetField("m_Data",Any).SetValue(runtime,data);
                typeof(EntityFact).GetField("Components",Any).SetValue(buff,new List<EntityFactComponent>{runtime});
                var state=math.GetMethod("ReadAttachedPoisonState",Any);
                Check(ReferenceEquals(state.Invoke(null,new object[]{buff,poison}),data), "reads native existing poison runtime data without creating it");
                data.TicksPassed=6;
                Check(((BuffPoisonStatDamageData)state.Invoke(null,new object[]{buff,poison})).TicksPassed==6, "changed counters are read afresh");
                runtimeType.GetField("m_Data",Any).SetValue(runtime,null);
                Check(state.Invoke(null,new object[]{buff,poison})==null && runtimeType.GetField("m_Data",Any).GetValue(runtime)==null,
                    "missing poison data stays missing instead of resetting a poison");

                var ctx=new TacticalContext();var victim=(UnitEntityData)RuntimeHelpers.GetUninitializedObject(typeof(UnitEntityData));
                var field=typeof(TacticalContext).GetField("AttachedDamage",Any);
                var cache=(IDictionary)Activator.CreateInstance(field.FieldType);
                var snapshot=Activator.CreateInstance(math.GetNestedType("AttachedSnapshot",Any),true);
                cache.Add(victim,snapshot);field.SetValue(ctx,cache);
                var query=(Func<TacticalContext,UnitEntityData,object>)math.GetMethod("AttachedDamageOf",Any).CreateDelegate(typeof(Func<TacticalContext,UnitEntityData,object>));
                Check(ReferenceEquals(query(ctx,victim),snapshot), "per-decision cache uses recipient identity");
                for(int i=0;i<10;i++)query(ctx,victim);
                long before=GC.GetAllocatedBytesForCurrentThread();bool same=true;
                for(int i=0;i<10000;i++)same &= ReferenceEquals(query(ctx,victim),snapshot);
                long bytes=GC.GetAllocatedBytesForCurrentThread()-before;
                Check(same && bytes==0,"10000 warm danger lookups allocate zero bytes and reuse the same snapshot");
            }
            catch(Exception ex){bad++;lines.Add("ATTACHED FAILED: "+ex.GetBaseException());}
            failures=bad;
            lines.Insert(0,$"ATTACHED {checks} checks, failures {bad}; actual Cling graph, native schedule and existing poison data; no actor/setup, Rulebook or native commands executed");
            return lines;
        }
    }
}
