using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.UnitLogic.Class.Kineticist.Properties;
using Kingmaker.UnitLogic.Mechanics.Properties;

namespace TomeBurnNative
{
    internal static class Program
    {
        private const BindingFlags Static = BindingFlags.NonPublic | BindingFlags.Static;
        private const BindingFlags Instance = BindingFlags.NonPublic | BindingFlags.Instance;
        private static int Main(string[] args)
        {
            if (args.Length < 4 || !File.Exists(args[0]) || !File.Exists(args[1]))
            { Console.Error.WriteLine("TomeBurn.Native <TTT-Core.dll> <Tome.dll> <export roots...> <report.txt>"); return 2; }
            int audit = (int)typeof(ProfileAudit.Program).GetMethod("Main", Static).Invoke(null,new object[]{args.Skip(2).ToArray()});
            if (audit != 0) return audit;
            var all = (List<SimpleBlueprint>)typeof(ProfileAudit.Audit).GetField("s_All", Static).GetValue(null);
            var property = all.OfType<BlueprintUnitProperty>().Single(b => b.AssetGuid.ToString()=="ac8b7875fff5c0643ac499d404947fff");
            var source = all.OfType<BlueprintFeature>().Single(b => b.name=="DarkElementalistBurnFeature");
            var feature = all.OfType<BlueprintFeature>().Single(b => b.AssetGuid.ToString()=="57e3577a0eb53294e9d7cc649d5239a3");
            var core = Assembly.LoadFrom(Path.GetFullPath(args[0]));
            var tome = Assembly.LoadFrom(Path.GetFullPath(args[1]));
            var type = core.GetType("TabletopTweaks.Core.NewComponents.Properties.CompositeCustomPropertyGetter",true);
            var conditionalType = tome.GetType("TomeOfTheFirebird.New_Components.Properties.HasFeaturePropertyGetter",true);
            var composite = (PropertyValueGetter)RuntimeHelpers.GetUninitializedObject(type);
            var conditional = (PropertyValueGetter)RuntimeHelpers.GetUninitializedObject(conditionalType);
            var field = type.GetField("Properties");
            var entryType = field.FieldType.GetElementType();
            object Entry(PropertyValueGetter value,float numerator)
            {
                var entry=RuntimeHelpers.GetUninitializedObject(entryType);
                entryType.GetField("Property").SetValue(entry,value);
                entryType.GetField("Numerator").SetValue(entry,numerator);
                entryType.GetField("Denominator").SetValue(entry,1f);
                return entry;
            }
            var reference=new BlueprintFeatureBaseReference();
            typeof(BlueprintReferenceBase).GetProperty("Cached",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).SetValue(reference,feature);
            conditionalType.GetField("featureBaseReference").SetValue(conditional,reference);
            conditionalType.GetField("Property").SetValue(conditional,new KineticistBurnPropertyGetter());
            var first=Entry(new KineticistBurnPropertyGetter{MultyplyOnCharacterLevel=true},1f);
            var second=Entry(conditional,-2f);
            var entries=Array.CreateInstance(entryType,2);entries.SetValue(first,0);entries.SetValue(second,1);
            field.SetValue(composite,entries);
            var mode=type.GetField("CalculationMode");mode.SetValue(composite,Enum.ToObject(mode.FieldType,0));
            var read=typeof(TacticalEngine).GetMethod("ReadBurnConsequencesWithOwnership",Static);
            var graphOnly=typeof(TacticalEngine).GetMethod("ReadBurnConsequences",Static);
            var delta=typeof(TacticalEngine).GetMethod("BurnConsequenceDelta",Static);
            int checks=0,bad=0;
            var lines=new List<string>();
            void Check(bool value,string label) { checks++;if(!value){bad++;lines.Add("TOME-BURN FAILED: "+label);} }
            bool Modeled(object model)=>(bool)model.GetType().GetField("Modeled",Instance).GetValue(model);
            object Model(bool owned)=>read.Invoke(null,new object[]{new BlueprintScriptableObject[]{source},3,7,11,
                new Func<BlueprintFeatureBase,bool>(f=>owned && f==feature)});
            void Delta(object model,int burn,int expected,string label)
            {
                var values=new object[]{model,burn,0,0f};
                Check((bool)delta.Invoke(null,values) && (int)values[2]==expected && (float)values[3]==0f,label);
            }
            var saved=property.ComponentsArray;
            try
            {
                property.ComponentsArray=new BlueprintComponent[]{composite};
                var plain=Model(false);Check(Modeled(plain),"actual provider shape without the feat");
                Delta(plain,0,0,"zero burn");Delta(plain,1,11,"one burn without feat");Delta(plain,2,22,"two burn without feat");
                var owned=Model(true);Check(Modeled(owned),"actual provider shape with the feat");
                Delta(owned,1,9,"one burn with two-level reduction");Delta(owned,2,18,"two burn with reduction");
                Delta(owned,1,9,"repeated quotes do not accumulate");Delta(Model(false),1,11,"new ownership snapshot is read afresh");
                Check(!Modeled(graphOnly.Invoke(null,new object[]{new BlueprintScriptableObject[]{source},3,7,11})),"missing ownership query remains unknown");
                mode.SetValue(composite,Enum.ToObject(mode.FieldType,1));Check(!Modeled(Model(true)),"uninspected composite mode stays unknown");
                mode.SetValue(composite,Enum.ToObject(mode.FieldType,0));
                entryType.GetField("Bonus").SetValue(second,1);Check(!Modeled(Model(true)),"extra constant stays unknown");
                entryType.GetField("Bonus").SetValue(second,0);
                entryType.GetField("Denominator").SetValue(second,2f);Check(!Modeled(Model(true)),"uninspected fractional term stays unknown");
                entryType.GetField("Denominator").SetValue(second,1f);
                conditionalType.GetField("featureBaseReference").SetValue(conditional,null);Check(!Modeled(Model(true)),"missing feature reference stays unknown");
            }
            finally { property.ComponentsArray=saved; }
            lines.Insert(0,$"TOME-BURN {checks} checks, failures {bad}; actual supplied provider layouts on Dark Elementalist graph; no mod getter/condition/factory, actor setup or native commands executed");
            foreach(var line in lines) Console.WriteLine(line);
            File.AppendAllText(args[args.Length-1],Environment.NewLine+string.Join(Environment.NewLine,lines));
            return bad==0?0:1;
        }
    }
}
