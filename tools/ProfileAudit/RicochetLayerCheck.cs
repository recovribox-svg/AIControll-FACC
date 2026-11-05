// ★ v0.3.28 (codex review): exercise the shipped ricochet branch reader, without an actor or a fake cast.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Blueprints;
using Kingmaker.ElementsSystem;
using Kingmaker.RuleSystem.Rules.Damage;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Mechanics.Actions;
using IsEqual = Kingmaker.Designers.EventConditionActionSystem.Conditions.IsEqual;
using Layer = Kingmaker.Designers.EventConditionActionSystem.Evaluators.DeliverEffectLayer;
using Constant = Kingmaker.Designers.EventConditionActionSystem.Evaluators.IntConstant;

namespace ProfileAudit
{
    internal static class RicochetLayerCheck
    {
        // These six ordinary blast graphs have a full layer-0 action and a half-damage nonzero-layer action.
        private static readonly string[] PlainBlasts =
        {
            "31f668b12011e344aa542aa07ab6c8d9", "24f26ac07d21a0e4492899085d1302f6",
            "f6d32ecd20ebacb4e964e2ece1c70826", "e3f41966c2d662a4e9582a0497621c46",
            "b28c336c10eb51c4a8ded0258d5742e1", "7b4f0c9a06db79345b55c39b2d5fb510"
        };

        private sealed class OtherEqual : IsEqual { }

        internal static List<string> Run(IEnumerable<SimpleBlueprint> loaded, out int failures)
        {
            var lines = new List<string>();
            int bad = 0, cases = 0;
            var read = typeof(AbilityProfile).GetMethod("TryReadDeliveryLayer", BindingFlags.NonPublic | BindingFlags.Static);
            var applies = typeof(AbilityProfile).GetMethod("AppliesAtDeliveryLayer", BindingFlags.NonPublic | BindingFlags.Instance);
            if (read == null || applies == null) throw new MissingMethodException("AbilityProfile delivery-layer reader");

            ConditionsChecker Checker(params Condition[] conditions) => new ConditionsChecker { Conditions = conditions };
            IsEqual Equal(int value = 0, bool reverse = false, bool not = false) => new IsEqual
            {
                FirstValue = reverse ? (IntEvaluator)new Constant { Value = value } : new Layer(),
                SecondValue = reverse ? (IntEvaluator)new Layer() : new Constant { Value = value }, Not = not
            };
            void Check(ConditionsChecker checker, int layer, bool known, bool value, string label)
            {
                cases++;
                var args = new object[] { checker, layer, false };
                bool actual = (bool)read.Invoke(null, args);
                if (actual != known || known && (bool)args[2] != value)
                { bad++; lines.Add("RICOCHET-LAYER FAILED: " + label); }
            }
            Check(Checker(Equal()), 0, true, true, "primary layer");
            Check(Checker(Equal()), 1, true, false, "jump layer");
            Check(Checker(Equal(2, reverse: true)), 2, true, true, "swapped operands/nondefault constant");
            Check(Checker(Equal(2, reverse: true)), 1, true, false, "swapped unequal");
            Check(Checker(Equal(not: true)), 0, true, false, "Not equal");
            Check(Checker(Equal(not: true)), 1, true, true, "Not unequal");
            Check(new ConditionsChecker { Operation = Operation.Or, Conditions = new Condition[] { Equal() } },
                0, true, true, "single Or condition");
            Check(null, 1, false, false, "missing checker");
            Check(Checker(Equal(), Equal()), 1, false, false, "compound checker stays unread");
            Check(Checker(new IsEqual { FirstValue = new Constant(), SecondValue = new Constant() }), 1, false, false,
                "non-layer expression stays unread");
            Check(Checker(new OtherEqual { FirstValue = new Layer(), SecondValue = new Constant() }), 1, false, false,
                "subclass stays unread");
            lines.Add($"RICOCHET-LAYER checker cases {cases}: {(bad == 0 ? "passed" : "FAILED")}");

            int graphs = 0, graphChecks = 0;
            foreach (var bp in loaded.OfType<BlueprintAbility>().Where(b => PlainBlasts.Contains(b.AssetGuid.ToString())))
            {
                graphs++;
                var profile = AbilityProfile.Get(bp);
                foreach (int layer in new[] { 0, 1, 7 })
                {
                    graphChecks++;
                    var selected = profile.Damage.OfType<ContextActionDealDamage>().Where(d =>
                        (EffectApplies)applies.Invoke(profile, new object[] { d, null, null, layer }) != EffectApplies.No).ToList();
                    bool ok = selected.Count == 1 && selected[0].Half == (layer != 0) &&
                        (EffectApplies)applies.Invoke(profile, new object[] { selected[0], null, null, layer }) == EffectApplies.Yes;
                    if (!ok) { bad++; lines.Add($"RICOCHET-LAYER FAILED: {bp.name} layer {layer}, expected one {(layer == 0 ? "full" : "half")} action"); }
                }
                graphChecks++;
                if (profile.Damage.Any(d => profile.AppliesTo(d, null, null) != EffectApplies.Unknown))
                { bad++; lines.Add($"RICOCHET-LAYER FAILED: ordinary {bp.name} branch reading changed"); }
            }
            lines.Add(graphs == 0 ? "RICOCHET-LAYER plain-blast checks SKIPPED: none of the six reference graphs loaded"
                : $"RICOCHET-LAYER {graphs}/6 plain-blast graphs, {graphChecks} branch checks; failures {bad}");

            // Native rule data only: no actor, constructor, Rulebook.Trigger or damage application. Exercise the real
            // observer gate, which feeds its existing whole-hit withdrawal logic when any packet is deliberately half.
            var clean = typeof(DamageObserver).GetMethod("CleanRoll", BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new MissingMethodException("DamageObserver.CleanRoll");
            var results = typeof(RuleDealDamage).GetProperty("ResultList")?.GetSetMethod(true)
                ?? throw new MissingMemberException("RuleDealDamage.ResultList");
            var rule = (RuleDealDamage)RuntimeHelpers.GetUninitializedObject(typeof(RuleDealDamage));
            results.Invoke(rule, new object[] { new List<DamageValue>() });
            int calibrationFailures = 0;
            foreach (bool half in new[] { false, true })
            {
                rule.Half = half;
                if ((bool)clean.Invoke(null, new object[] { rule }) == half)
                { bad++; calibrationFailures++; lines.Add($"RICOCHET-LAYER FAILED: CleanRoll with Half={half}"); }
            }
            lines.Add($"RICOCHET-LAYER calibration 2 checks, failures {calibrationFailures}");
            failures = bad;
            return lines;
        }
    }
}
