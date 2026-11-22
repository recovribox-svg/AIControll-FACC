// Coverage7: a dispel roll is target evidence, even when it fails. Exercise the actual observer with inert native
// layout fixtures; no unit constructor, game world, Rulebook trigger, dispel or buff removal is executed.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.EntitySystem;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.PubSubSystem;
using Kingmaker.RuleSystem;
using Kingmaker.RuleSystem.Rules;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Actions;

namespace ProfileAudit
{
    internal static class CastOutcomeCheck
    {
        private const BindingFlags Hidden = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        internal static List<string> Run(out int failures)
        {
            int checks = 0, bad = 0; var lines = new List<string>();
            void Check(bool ok, string why) { checks++; if (!ok) { bad++; lines.Add("CAST-OUTCOME FAILED: " + why); } }
            try
            {
                var observer = typeof(CastOutcome);
                Check(typeof(IGlobalRulebookHandler<RuleDispelMagic>).IsAssignableFrom(observer), "the cast observer subscribes to dispel rolls");
                var handler = BuffSuppressCheck.Method(observer, "OnEventDidTrigger", typeof(RuleDispelMagic));
                var body = SaveBoostCheck.ReferencedMembers(handler).ToList();
                Check(new[] { "Mark", "MarkReached" }.All(name => body.Any(m => m.DeclaringType == observer && m.Name == name)),
                    "a dispel roll supplies general evidence and target reach evidence");
                Check(!body.Any(m => m.DeclaringType == typeof(RuleDispelMagic) && (m.Name == "get_Success" || m.Name == "get_Context")) &&
                      body.Any(m => m.DeclaringType == typeof(RuleReason) && m.Name == "get_Context"),
                    "target reach does not depend on dispel success or the removed buff's context; it uses the action's reason");
                var native = BuffSuppressCheck.BodyWithLambdas(typeof(ContextActionDispelMagic), "TryDispelBuff");
                Check(native.Any(m => m is ConstructorInfo c && c.DeclaringType == typeof(RuleDispelMagic)) &&
                      native.Any(m => m.DeclaringType == typeof(MechanicsContext) && m.Name == "TriggerRule"),
                    "the native buff-dispel action triggers RuleDispelMagic with its action context");

                var pendingType = observer.GetNestedType("Pending", Hidden);
                var pendingByCaster = (IDictionary)observer.GetField("ByCaster", Hidden).GetValue(null);
                var failed = (IDictionary)observer.GetField("Failures", Hidden).GetValue(null);
                var finish = observer.GetMethod("Finish", Hidden);
                var caster = BareUnit("audit-dispel-caster");
                var target = BareUnit("audit-dispel-target");
                string id = caster.UniqueId, key = "audit-dispel-outcome";
                bool hadPending = pendingByCaster.Contains(id), hadFailure = failed.Contains(key);
                object oldPending = pendingByCaster[id], oldFailure = failed[key];
                try
                {
                    foreach (int roll in new[] { 1, 20 })
                    {
                        var pending = Activator.CreateInstance(pendingType, true);
                        void Set(string name, object value) => pendingType.GetField(name, Hidden).SetValue(pending, value);
                        bool Read(string name) => (bool)pendingType.GetField(name, Hidden).GetValue(pending);
                        Set("Caster", caster); Set("Target", target); Set("Key", key); Set("WatchTarget", true); Set("Verifiable", true);
                        pendingByCaster[id] = pending;
                        failed.Remove(key);
                        var evt = Bare<RuleDispelMagic>();
                        typeof(RulebookEvent).GetField("Initiator", Hidden).SetValue(evt, caster);
                        typeof(RulebookTargetEvent).GetField("Target", Hidden).SetValue(evt, target);
                        var die = Bare<RuleRollD20>();
                        typeof(RuleRollDice).GetField("m_Result", Hidden).SetValue(die, roll);
                        typeof(RuleDispelMagic).GetProperty("CheckRoll").GetSetMethod(true).Invoke(evt, new object[] { die });
                        typeof(RuleDispelMagic).GetProperty("DC").GetSetMethod(true).Invoke(evt, new object[] { 10 });
                        Check(evt.Success == (roll == 20), "the inert native scalar fixture has the intended dispel result");
                        new CastOutcome().OnEventDidTrigger(evt);
                        Check(Read("Evidence") && Read("TargetReached"), "native dispel result " + evt.Success + " reaches its target");
                        finish.Invoke(null, new[] { pending, (object)Read("Evidence"), null });
                        Check(!pendingByCaster.Contains(id) && !failed.Contains(key), "a real dispel attempt finishes without a silent-failure strike");

                        Set("Evidence", false); Set("TargetReached", false); pendingByCaster[id] = pending;
                        typeof(RulebookTargetEvent).GetField("Target", Hidden).SetValue(evt, null);
                        new CastOutcome().OnEventDidTrigger(evt);
                        Check(Read("Evidence") && !Read("TargetReached"), "an area dispel with no unit target does not pretend to reach the anchor unit");
                    }
                }
                finally
                {
                    if (hadPending) pendingByCaster[id] = oldPending; else pendingByCaster.Remove(id);
                    if (hadFailure) failed[key] = oldFailure; else failed.Remove(key);
                }
            }
            catch (Exception ex) { Check(false, "exception " + ex.GetType().Name + ": " + ex.Message); }
            failures = bad;
            lines.Insert(0, $"CAST-OUTCOME {checks} checks, {bad} failures; native layout/scalars only, no live cast");
            return lines;
        }

        private static T Bare<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

        private static UnitEntityData BareUnit(string id)
        {
            var unit = Bare<UnitEntityData>();
            typeof(UnitEntityData).GetField("<Descriptor>k__BackingField", Hidden).SetValue(unit, Bare<UnitDescriptor>());
            typeof(EntityDataBase).GetField("<UniqueId>k__BackingField", Hidden).SetValue(unit, id);
            return unit;
        }
    }
}
