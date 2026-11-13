// Actual candidate/status guards and compiled routes only; no actor, path, command or Unity execution.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CompanionAI_Pathfinder.Tactics;

namespace ProfileAudit
{
    internal static class IdleAdvanceCheck
    {
        internal static List<string> Run(out int failures)
        {
            int checks = 0, bad = 0;
            var lines = new List<string>();
            void Check(bool ok, string why) { checks++; if (!ok) { bad++; lines.Add("IDLE-ADVANCE FAILED: " + why); } }
            try
            {
                const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
                var engine = typeof(TacticalEngine);
                var allowed = (Func<TacticalContext, bool>)engine.GetMethod("CanOfferIdleAdvance", flags)
                    .CreateDelegate(typeof(Func<TacticalContext, bool>));
                var waiting = (Func<bool, IReadOnlyList<Candidate>, bool>)engine.GetMethod("WaitingForInPlaceAttack", flags)
                    .CreateDelegate(typeof(Func<bool, IReadOnlyList<Candidate>, bool>));
                var offensive = (Func<Candidate, bool>)engine.GetMethod("IsOffensiveApproach", flags)
                    .CreateDelegate(typeof(Func<Candidate, bool>));
                TacticalContext Context() => new TacticalContext { HasMove = true, HasStandard = true, Permissions = new ActionPermissions() };
                var ctx = Context();
                Check(allowed(ctx), "ordinary remaining movement allows fallback");
                ctx.HasMove = false;
                Check(allowed(ctx), "a standard action may pay for the plain move");
                ctx.HasMove = true; ctx.HasStandard = false;
                Check(allowed(ctx), "remaining move alone may advance for next turn");
                ctx.HasMove = false;
                Check(!allowed(ctx), "no remaining actions forbids fallback");
                ctx = Context(); ctx.Permissions.CanWalk = false;
                Check(!allowed(ctx), "native movement ban retained");
                ctx = Context(); ctx.NoMovementLeft = true;
                Check(!allowed(ctx), "no movement left retained");
                ctx = Context(); ctx.StepTaken = true;
                Check(!allowed(ctx), "a prior five-foot step forbids an ordinary move");
                ctx = Context(); ctx.Permissions.MovementOnly = true;
                Check(!allowed(ctx), "nauseated units do not advance to fight");
                ctx = Context();
                typeof(TacticalContext).GetField("PureMageWeapon", flags).SetValue(ctx, false);
                Check(!allowed(ctx), "Pure Mage's weapon-advance admission retained");
                ctx = Context(); ctx.Permissions.OneActionOnly = true; ctx.HasStandard = false;
                Check(allowed(ctx), "staggered unit may spend its one remaining move without attacking");
                var attack = new Candidate { Kind = CandidateKind.WeaponAttack, Slot = ActionSlot.Standard, Offensive = true, Value = 10f };
                Check(waiting(true, new[] { attack }), "real time waits for an in-place attack cooldown");
                Check(!waiting(false, new[] { attack }), "turn-based has no real-time cooldown exception");
                var step = new Candidate { Kind = CandidateKind.WeaponAttack, FullAttack = true, Slot = ActionSlot.FullRound, Offensive = true, Value = 20f };
                typeof(Candidate).GetField("FullAttackApproach", flags).SetValue(step, true);
                Check(!waiting(true, new[] { step }), "an unproven step is not an in-place cooldown wait");
                attack.NeedsMove = true;
                Check(!waiting(true, new[] { attack }), "ordinary walking attack is not an in-place wait");
                attack.NeedsMove = false; attack.Slot = ActionSlot.Swift;
                Check(!waiting(true, new[] { attack }), "swift offense does not create a main-attack wait");
                attack.Slot = ActionSlot.Standard; attack.Offensive = false;
                Check(!waiting(true, new[] { attack }), "nonoffensive action does not create an attack wait");
                Check(offensive(step), "rejected step/full attack can open recovery");
                Check(!offensive(new Candidate { Kind = CandidateKind.Move, Offensive = true }), "a refused plain move alone cannot reopen itself");
                Check(!offensive(new Candidate { Kind = CandidateKind.Ability, NeedsMove = true }), "a refused ally heal alone cannot trigger combat closing");
                bool Calls(string owner, string member) => SaveBoostCheck.ReferencedMembers(engine.GetMethod(owner, flags)).Any(m => m.Name == member);
                Check(Calls("IdleAdvanceCandidates", "AddClosingCandidates") && Calls("IdleAdvanceCandidates", "RepriceRealPaths"),
                    "fallback reuses closing policy and actual-path risk repricing");
                var closingMembers = SaveBoostCheck.ReferencedMembers(engine.GetMethod("AddClosingCandidates", flags)).ToList();
                bool nativeQuotes = closingMembers.OfType<MethodInfo>()
                    .Where(m => m.DeclaringType.Assembly == engine.Assembly && m.Name.Contains("AddClosingCandidates"))
                    .SelectMany(SaveBoostCheck.ReferencedMembers).Any(m => m.Name == "EstimateWeaponAttack");
                Check(Calls("AddClosingCandidates", "AddAdvanceCandidate") && nativeQuotes,
                    "ordinary and lazy closing share native quotes and safe approach builder");
                Check(Calls("IdleAdvanceCandidates", "CanOfferIdleAdvance") && Calls("IdleAdvanceCandidates", "WaitingForInPlaceAttack"),
                    "status and real-time wait guards reach the actual fallback factory");
                Check(SelectedApproachFilter.MaxChecks == 3, "selected approach preparation budget remains three");
                var recoveryKey = (Func<Candidate, string>)engine.GetMethod("PlainMoveRecoveryKey", flags)
                    .CreateDelegate(typeof(Func<Candidate, string>));
                Check(recoveryKey(new Candidate { Kind = CandidateKind.Move, Key = "move:adv:A" }) == "move:adv:A",
                    "plain recovery uses its existing exact plan key");
                Check(recoveryKey(new Candidate { Kind = CandidateKind.WeaponAttack, Key = "full:A", FullAttack = true }) == null,
                    "attack refusal is not a plain-move recovery identity");
                Check(recoveryKey(new Candidate { Kind = CandidateKind.Move, Key = "move:A", NeedsMove = true }) == null &&
                    recoveryKey(new Candidate { Kind = CandidateKind.Move, Key = "move:A", MoveFirst = true }) == null,
                    "walk-then-act shapes do not acquire plain-move identity");
                Check(recoveryKey(new Candidate { Kind = CandidateKind.Move }) == null,
                    "missing key stays unidentified instead of becoming a global refusal");
            }
            catch (Exception ex) { bad++; lines.Add("IDLE-ADVANCE FAILED: " + ex.GetBaseException()); }
            failures = bad;
            lines.Insert(0, $"IDLE-ADVANCE {checks} checks, failures {bad}; actual candidate/status guards and compiled routes; no actor, native path, command or Unity timing execution");
            return lines;
        }
    }
}
