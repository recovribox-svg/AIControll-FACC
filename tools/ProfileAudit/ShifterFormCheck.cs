// ★ shifter-forms (docs/SHIFTER_FORM_DECISIONS.md, docs/TACTICAL_AI.md §174): the actual form/aspect blueprints behind
// the form timing and Fury rules, and the compiled routes. Actor-free: no Game/Player, cast, transformation or Fury command.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Commands.Base;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.UnitLogic.Mechanics.Components;

namespace ProfileAudit
{
    internal static class ShifterFormCheck
    {
        internal static List<string> Run(IEnumerable<SimpleBlueprint> loaded, out int failures)
        {
            int checks = 0, bad = 0; var lines = new List<string>();
            void Check(bool ok, string why) { checks++; if (!ok) { bad++; lines.Add("SHIFTER-FORMS FAILED: " + why); } }
            try
            {
                const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
                var blueprints = loaded.ToList();
                var mod = typeof(TacticalEngine).Assembly;
                var givesShifterForm = mod.GetType("CompanionAI_Pathfinder.Analysis.PolymorphFacts").GetMethod("GivesShifterForm", all);
                bool ShifterForm(BlueprintAbility bp)
                {
                    try { return (bool)givesShifterForm.Invoke(null, new object[] { AbilityProfile.Get(bp) }); }
                    catch { return false; }
                }

                // 1. The action a shift takes: ValueModel.FormAttackRounds removes this round's form attack for a
                //    Standard or full-round shift (the engine reads AbilityData.RuntimeActionType through SlotOf).
                var forms = blueprints.OfType<BlueprintAbility>().Where(ShifterForm).ToList();
                string Slot(BlueprintAbility bp) => bp.IsFullRoundAction ? "FullRound" : bp.ActionType.ToString();
                var bySlot = forms.GroupBy(Slot).OrderBy(g => g.Key).Select(g => $"{g.Key} {g.Count()}");
                lines.Add($"SHIFTER-FORMS shift actions: {forms.Count} form abilities: {string.Join(", ", bySlot)}");
                foreach (var name in new[] { "ShifterWildShapeGriffonAbillity", "ShifterWildShapeGriffonAbillity9", "ShifterWildShapeGriffonAbillity14" })
                {
                    var bp = forms.FirstOrDefault(b => b.name == name);
                    Check(bp != null && !bp.IsFullRoundAction && bp.ActionType == UnitCommand.CommandType.Standard,
                        "actual Griffon Shape tier is a Standard-action shift: " + name);
                }

                // 2. Minor aspects survive the shift and stack with the form: the shape removes only buffs carrying its
                //    own removal descriptor (Polymorph), and no aspect buff carries it.
                var griffon = forms.FirstOrDefault(b => b.name == "ShifterWildShapeGriffonAbillity");
                var removed = griffon?.GetComponents<AbilityExecuteActionOnCast>()
                    .SelectMany(c => c.Actions?.Actions ?? Array.Empty<Kingmaker.ElementsSystem.GameAction>())
                    .OfType<ContextActionRemoveBuffsByDescriptor>().Select(a => a.SpellDescriptor.Value)
                    .Aggregate(SpellDescriptor.None, (x, y) => x | y) ?? SpellDescriptor.None;
                Check(removed != SpellDescriptor.None, "Griffon Shape removes buffs by a descriptor before applying the form");
                var aspects = blueprints.OfType<BlueprintBuff>()
                    .Where(b => b.name.StartsWith("ShifterAspect", StringComparison.Ordinal) && b.name.EndsWith("Buff", StringComparison.Ordinal)).ToList();
                var lost = aspects.Where(b => ((b.GetComponent<SpellDescriptorComponent>()?.Descriptor.Value ?? SpellDescriptor.None) & removed) != SpellDescriptor.None).ToList();
                lines.Add($"SHIFTER-FORMS aspects: {aspects.Count} aspect buffs, {lost.Count} removed by a shift" +
                          (lost.Count > 0 ? ": " + string.Join(", ", lost.Select(b => b.name)) : ""));
                Check(aspects.Count > 0 && aspects.Any(b => b.name == "ShifterAspectGriffonBuff"), "actual aspect buffs present, Griffon among them");
                Check(lost.Count == 0, "no minor aspect is removed by the shape's descriptor removal (it stays on through the form)");

                // 3. Compiled routes.
                bool Calls(MethodInfo m, string name) => m != null && SaveBoostCheck.ReferencedMembers(m).Any(x => x.Name == name);
                var engine = typeof(TacticalEngine);
                var appraise = engine.GetMethod("AppraiseShifterForm", all);
                Check(Calls(appraise, "FormAttackRounds") && Calls(appraise, "SlotOf"), "form appraisal counts its attacks from the round after a turn-taking shift");
                var fury = engine.GetMethod("AddShifterFuryCandidates", all);
                Check(fury != null && !Calls(fury, "GivesShifterForm"), "a positive form no longer keeps Fury candidates out");
                var hold = engine.GetMethod("HoldFuryForForm", all);
                Check(Calls(hold, "HoldFuryForForm") && Calls(hold, "get_IsPolymorphed") && Calls(hold, "IsNewShifterForm"),
                    "Fury waits only for a new form in a human body, by the shared rule");
                Check(!Calls(hold, "Trigger") && !Calls(hold, "CreateContext") && !Calls(hold, "ShifterFormDamage") && !Calls(hold, "ShifterFuryDamage"),
                    "the Fury hold adds no rule, context or forecast");
                Check(engine.GetMethods(all).Where(m => m.Name == "Decide").Any(m => Calls(m, "HoldFuryForForm")), "Decide applies the Fury hold to the turn's quick action");
                var finish = engine.GetMethod("FinishFormAppraisal", all);
                Check(Calls(finish, "FormNoteChanged") && !Calls(finish, "Trigger") && !Calls(finish, "CreateContext") && !Calls(finish, "ShifterFormDamage"),
                    "form appraisal rows are bounded and add no calculation");

                // 4. The shared rules on the played numbers (fusion2g1b).
                Check(Math.Abs(ValueModel.FormAttackRounds(4.4f, true) - 3.4f) < 0.001f && ValueModel.FormAttackRounds(1f, true) == 0f,
                    "a Standard shift's attacks begin next round");
                Check(!ValueModel.HoldFuryForForm(22.8f, 28.4f, 0f), "fight 3 R1: Griffon Shape 22.8 against the full attack 28.4 does not hold the Fury");
                Check(ValueModel.HoldFuryForForm(34.9f, 6.7f, 8f), "RT1: Griffon Shape 34.9 against a walking attack 6.7 holds the Fury for one turn");
            }
            catch (Exception ex) { bad++; lines.Add("SHIFTER-FORMS FAILED: " + ex.GetBaseException()); }
            failures = bad;
            lines.Insert(0, $"SHIFTER-FORMS {checks} checks, failures {bad}; actual form/aspect blueprints, shared rules and compiled routes; no actor, cast, transformation or Unity timing execution");
            return lines;
        }
    }
}
