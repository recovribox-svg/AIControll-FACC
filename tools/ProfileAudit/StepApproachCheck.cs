// Author-approved movement repair. Real candidate classification and bounded scheduling only; no actor/path execution.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CompanionAI_Pathfinder.Tactics;

namespace ProfileAudit
{
    internal static class StepApproachCheck
    {
        internal static List<string> Run(out int failures)
        {
            int checks = 0, bad = 0; var lines = new List<string>();
            void Check(bool ok, string why) { checks++; if (!ok) { bad++; lines.Add("STEP-APPROACH FAILED: " + why); } }
            try
            {
                const BindingFlags hidden = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var flag = typeof(Candidate).GetField("FullAttackApproach", hidden);
                var method = typeof(TacticalEngine).GetMethod("RequiresApproachValidation", hidden);
                var needs = (Func<Candidate, bool>)method.CreateDelegate(typeof(Func<Candidate, bool>));
                Candidate Step(float value = 10f)
                {
                    var c = new Candidate { Kind = CandidateKind.WeaponAttack, FullAttack = true, Slot = ActionSlot.FullRound, Value = value };
                    flag.SetValue(c, true); return c;
                }
                var step = Step();
                var inPlace = new Candidate { Kind = CandidateKind.WeaponAttack, FullAttack = true, Slot = ActionSlot.FullRound, Value = 5f };
                Check(needs(step), "full attack needing a five-foot step enters selected-path validation");
                Check(!step.NeedsMove && step.Slot == ActionSlot.FullRound, "step intent does not spend a normal Move action");
                Check(!needs(inPlace), "in-reach full routine stays on the no-path fast path");
                Check(needs(new Candidate { Kind = CandidateKind.Ability, NeedsMove = true }), "ordinary walking cast retains validation");
                Check(needs(new Candidate { Kind = CandidateKind.Move }), "plain move retains existing classification");
                Check(!needs(new Candidate { Kind = CandidateKind.Ability }), "in-place ability does not acquire path work");
                Check(needs(Step(0.1f)), "weak fallback full routine still requires its step path");
                var malformed = new Candidate { Kind = CandidateKind.WeaponAttack };
                flag.SetValue(malformed, true);
                Check(!needs(malformed), "step flag cannot turn an ordinary single into a full-routine approach");
                int calls = 0;
                Candidate Main(IReadOnlyList<Candidate> list) => list.FirstOrDefault(c => c.Slot != ActionSlot.Free);
                var boost = new Candidate { Kind = CandidateKind.Ability, Slot = ActionSlot.Free, Value = 2f };
                var fallback = SelectedApproachFilter.Apply(new[] { boost, step, inPlace }, Main, needs,
                    c => { calls++; return false; });
                Check(calls == 1 && !fallback.Contains(step) && Main(fallback) == inPlace && fallback.Contains(boost),
                    "refused step exposes already-scored in-place action before boost coupling");
                var many = Enumerable.Range(0, 6).Select(i => Step()).ToArray();
                int rejected = 0, omitted = 0; calls = 0;
                var bounded = SelectedApproachFilter.Apply(many.Concat(new[] { inPlace }), Main, needs,
                    c => { calls++; return false; }, c => rejected++, count => omitted = count);
                Check(calls == 3 && rejected == 3 && omitted == 3 && bounded.Single() == inPlace,
                    "three-check budget bounds native-candidate scheduling and withholds unchecked steps");
                calls = 0;
                var changed = SelectedApproachFilter.Apply(new[] { step, inPlace }, Main, needs,
                    c => { calls++; return true; });
                Check(calls == 1 && Main(changed) == step, "fresh decision can accept the same plan after geometry changes");
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 10000; i++) needs(inPlace);
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Check(allocated == 0, "in-place predicate allocates no bytes in 10000 offline reads");
                var producer = typeof(TacticalEngine).GetMethod("AddWeaponCandidates", hidden);
                Check(SaveBoostCheck.ReferencedMembers(producer).Any(m => m.Module == flag.Module && m.MetadataToken == flag.MetadataToken),
                    "compiled weapon candidate producer writes the step intent field");
            }
            catch (Exception ex) { bad++; lines.Add("STEP-APPROACH FAILED: " + ex.GetBaseException()); }
            failures = bad;
            lines.Insert(0, $"STEP-APPROACH {checks} checks, failures {bad}; actual candidates and bounded scheduler; no actor, native path, command or Unity timing execution");
            return lines;
        }
    }
}
