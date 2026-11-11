using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.UnitLogic.Class.Kineticist.Properties;
using Kingmaker.UnitLogic.Mechanics.Properties;

namespace CompanionAI_Pathfinder.Tactics
{
    // Inspected installed Tome Burn Resistance: sum(character-level burn, -2 * burn when the feat is owned).
    // Read the actual objects; never invoke a mod getter, condition, factory or initializer.
    internal static class ModdedBurnProperties
    {
        private const string Composite = "TabletopTweaks.Core.NewComponents.Properties.CompositeCustomPropertyGetter";
        private const string HasFeature = "TomeOfTheFirebird.New_Components.Properties.HasFeaturePropertyGetter";
        private static readonly Guid CoreModule = new Guid("cdfed43f-53a0-4186-a181-75496eecec8f");
        private static readonly Guid TomeModule = new Guid("6b63b5c2-ba69-4676-992f-ec22b4b92cb7");
        private const BindingFlags Fields = BindingFlags.Public | BindingFlags.Instance;
        private sealed class Layout
        {
            internal readonly FieldInfo Properties, Mode, Property, Bonus, Numerator, Denominator;
            internal Layout(Type type)
            {
                if (type.FullName != Composite || type.Module.ModuleVersionId != CoreModule) return;
                Properties = type.GetField("Properties", Fields); Mode = type.GetField("CalculationMode", Fields);
                var entry = Properties?.FieldType.GetElementType();
                if (entry?.FullName != Composite + "+ComplexCustomProperty" || entry.Module.ModuleVersionId != CoreModule) return;
                Property = entry.GetField("Property", Fields); Bonus = entry.GetField("Bonus", Fields);
                Numerator = entry.GetField("Numerator", Fields); Denominator = entry.GetField("Denominator", Fields);
            }
        }
        private static readonly ConditionalWeakTable<Type, Layout> Layouts = new ConditionalWeakTable<Type, Layout>();

        internal static bool TryRead(PropertyValueGetter getter, Func<BlueprintFeatureBase, bool> ownsFeature,
            out KineticistBurnPropertyGetter basis, out bool composite, out int featureAdjustment)
        {
            basis = null; composite = false; featureAdjustment = 0;
            if (getter == null) return false;
            if (getter.GetType() == typeof(KineticistBurnPropertyGetter))
            { basis = (KineticistBurnPropertyGetter)getter; return true; }
            // Native settings are applied to native burn below the property and rank layers. The inspected
            // optional composite contract still requires unchanged settings at every nested provider boundary.
            if (getter.Settings != null) return false;
            if (ownsFeature == null) return false;
            var layout = Layouts.GetValue(getter.GetType(), t => new Layout(t));
            if (layout.Property == null || layout.Bonus == null || layout.Numerator == null || layout.Denominator == null ||
                layout.Mode == null || Convert.ToInt32(layout.Mode.GetValue(getter)) != 0 ||
                !(layout.Properties?.GetValue(getter) is Array entries) || entries.Length != 2) return false;
            bool conditional = false;
            foreach (var entry in entries)
            {
                if (entry == null || (int)layout.Bonus.GetValue(entry) != 0 || (float)layout.Denominator.GetValue(entry) != 1f) return false;
                var property = layout.Property.GetValue(entry) as PropertyValueGetter;
                float numerator = (float)layout.Numerator.GetValue(entry);
                if (property?.GetType() == typeof(KineticistBurnPropertyGetter) && property.Settings == null && numerator == 1f)
                {
                    var burn = (KineticistBurnPropertyGetter)property;
                    if (basis != null || burn.MultiplyOnClassLevel || !burn.MultyplyOnCharacterLevel) return false;
                    basis = burn;
                    continue;
                }
                var type = property?.GetType();
                if (conditional || type?.FullName != HasFeature || type.Module.ModuleVersionId != TomeModule || property.Settings != null || numerator != -2f ||
                    type.BaseType?.FullName != "TomeOfTheFirebird.New_Components.Properties.ConditionalPropertyGetter") return false;
                var nested = type.GetField("Property", Fields)?.GetValue(property) as KineticistBurnPropertyGetter;
                var feature = (type.GetField("featureBaseReference", Fields)?.GetValue(property) as BlueprintFeatureBaseReference)?.Get();
                if (nested?.GetType() != typeof(KineticistBurnPropertyGetter) || nested.Settings != null || nested.MultiplyOnClassLevel ||
                    nested.MultyplyOnCharacterLevel || feature == null) return false;
                featureAdjustment = ownsFeature(feature) ? -2 : 0;
                conditional = true;
            }
            composite = basis != null && conditional;
            return composite;
        }
    }
}
