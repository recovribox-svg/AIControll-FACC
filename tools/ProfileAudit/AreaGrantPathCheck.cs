// ★ v0.3.30 (competence): the shipped area-action reader on synthetic native-layout graphs.
// No actor, native condition evaluation, cast or blueprint-cache mutation is involved.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Blueprints;
using Kingmaker.Designers.EventConditionActionSystem.Actions;
using Kingmaker.ElementsSystem;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Mechanics.Actions;

namespace ProfileAudit
{
    internal static class AreaGrantPathCheck
    {
        internal static List<string> Run(out int failures)
        {
            var lines = new List<string>();
            int bad = 0, cases = 0;
            var collect = typeof(TacticalEngine).GetMethod("CollectGrants", BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new MissingMethodException("TacticalEngine.CollectGrants");
            var parameters = collect.GetParameters();
            var buff = Bare<BlueprintBuff>();
            var reference = Reference(buff);

            ContextActionApplyBuff Apply(bool toCaster = false, BlueprintBuffReference other = null)
            {
                var action = Bare<ContextActionApplyBuff>();
                typeof(ContextActionApplyBuff).GetField("m_Buff", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(action, other ?? reference);
                action.ToCaster = toCaster;
                return action;
            }
            Conditional Branch(ConditionsChecker checker, GameAction[] yes, GameAction[] no = null)
            {
                var action = Bare<Conditional>();
                action.ConditionsChecker = checker;
                action.IfTrue = Actions(yes);
                action.IfFalse = Actions(no);
                return action;
            }
            ContextActionSavingThrow Save(SavingThrowType type)
            {
                var failed = Bare<ContextActionConditionalSaved>();
                failed.Failed = Actions(Apply());
                failed.Succeed = Actions();
                var action = Bare<ContextActionSavingThrow>();
                action.Type = type;
                action.Actions = Actions(failed);
                return action;
            }
            IList Read(GameAction[] actions, bool followSaves = true, IList reuse = null)
            {
                var grants = reuse ?? (IList)Activator.CreateInstance(parameters[2].ParameterType);
                var args = new object[] { actions, Activator.CreateInstance(parameters[1].ParameterType), grants,
                    0, followSaves, SavingThrowType.Unknown, null };
                // Also supports the preserved pre-change binary for a paired reader regression demonstration.
                if (parameters.Length == 6) Array.Resize(ref args, 6);
                collect.Invoke(null, args);
                return grants;
            }
            void Check(bool ok, string label)
            {
                cases++;
                if (!ok) { bad++; lines.Add("AREA-GRANT-PATH FAILED: " + label); }
            }
            void Count(GameAction[] actions, int expected, string label, bool follow = true) =>
                Check(Read(actions, follow).Count == expected, label);

            var checker = new ConditionsChecker();
            Count(new GameAction[] { Branch(checker, new GameAction[] { Apply() }, new GameAction[] { Apply() }) },
                2, "opposite branches retain the same buff");
            Count(new GameAction[]
            {
                Branch(new ConditionsChecker(), new GameAction[] { Apply() }),
                Branch(new ConditionsChecker(), new GameAction[] { Apply() })
            }, 2, "different checker identities at equal depth");
            Count(new GameAction[] { Branch(checker, new GameAction[] { Apply() }), Branch(checker, new GameAction[] { Apply() }) },
                1, "identical checker and branch deduplicate");

            var secondChecker = new ConditionsChecker();
            Count(new GameAction[]
            {
                Branch(checker, new GameAction[] { Branch(secondChecker, new GameAction[] { Apply() }) }),
                Branch(secondChecker, new GameAction[] { Branch(checker, new GameAction[] { Apply() }) })
            }, 2, "ordered nested guard paths remain distinct");

            var duplicate = Apply();
            var enterRound = Read(new GameAction[] { duplicate });
            Read(new GameAction[] { duplicate }, reuse: enterRound);
            Check(enterRound.Count == 1, "identical unguarded enter/round grant deduplicates");
            Count(new GameAction[] { Apply(), Apply(other: Reference(Bare<BlueprintBuff>())) },
                2, "different buff identities remain distinct");
            Count(new GameAction[] { Save(SavingThrowType.Will), Save(SavingThrowType.Fortitude) },
                2, "different save types remain distinct");
            var will = Save(SavingThrowType.Will);
            Count(new GameAction[] { will, Save(SavingThrowType.Will) },
                2, "different wrappers of the same save type remain distinct");
            Count(new GameAction[] { will, will }, 1, "identical save wrapper deduplicates");
            Count(new GameAction[] { Apply(), will }, 2, "unsaved and saved paths remain distinct");
            Count(new GameAction[] { will }, 0, "followSaves false preserves wrapper exclusion", follow: false);
            Count(new GameAction[] { Apply(toCaster: true) }, 0, "caster-directed grant remains excluded");

            var branches = Read(new GameAction[] { Branch(checker, new GameAction[] { Apply() }, new GameAction[] { Apply() }) });
            if (branches.Count == 2)
            {
                var field = branches[0].GetType().GetField("Guards");
                var yes = ((ConditionsChecker Checker, bool Branch)[])field.GetValue(branches[0]);
                var no = ((ConditionsChecker Checker, bool Branch)[])field.GetValue(branches[1]);
                Check(yes.Length == 1 && no.Length == 1 && ReferenceEquals(yes[0].Checker, checker) &&
                    ReferenceEquals(no[0].Checker, checker) && yes[0].Branch && !no[0].Branch,
                    "retained guards preserve checker identity and true/false polarity");
            }
            else Check(false, "retained guard identity/polarity cannot be verified");

            var saved = Read(new GameAction[] { will });
            var saveAction = saved[0].GetType().GetField("SaveAction");
            Check((SavingThrowType)saved[0].GetType().GetField("Save").GetValue(saved[0]) == SavingThrowType.Will &&
                saveAction != null && ReferenceEquals(saveAction.GetValue(saved[0]), will),
                "retained nearest save identity and type");

            failures = bad;
            lines.Insert(0, $"AREA-GRANT-PATH {cases} synthetic native-layout checks, failures {bad}; no actor or class-graph coverage");
            return lines;
        }

        private static T Bare<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

        private static ActionList Actions(params GameAction[] actions) => new ActionList { Actions = actions ?? new GameAction[0] };

        private static BlueprintBuffReference Reference(BlueprintBuff buff)
        {
            var reference = new BlueprintBuffReference();
            var cached = typeof(BlueprintReferenceBase).GetProperty("Cached", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new MissingMemberException("BlueprintReferenceBase.Cached");
            cached.SetValue(reference, buff);
            return reference;
        }
    }
}
