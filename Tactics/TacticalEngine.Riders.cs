// ★ v0.3.23 (bombs task, docs/TASK_BOMBS_CARNAGE_QUIVER.md §1): a self buff that its owner's next weapon attack uses up
// (AbilityProfile.BuffsSpentByWeaponHit - Grenadier Alchemical Weapon's flask buffs, Stunning Fist's and Elemental Fist's
// owner buffs) does something only for a weapon attack: the game fires its trigger on RuleAttackWithWeapon, and its extra
// dice (AdditionalDiceOnAttack OnlyWeaponAttacks) need that rule as the damage's parent. A bomb, a spell or an advance
// leaves it unused - while the flask or daily use is already spent. Like the one-shot marks (CoupleOwnAttackMarks), such a
// rider is kept only when the decision's best main action makes a weapon attack it can come before.
using System.Collections.Generic;
using System.Linq;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class TacticalEngine
    {
        private static List<Candidate> CoupleWeaponRiders(TacticalContext ctx, List<Candidate> usable, bool explain)
        {
            bool IsRider(Candidate c) => c.Kind == CandidateKind.Ability && c.Ability != null && c.Target == ctx.Unit &&
                                         (AbilityProfile.Get(c.Ability)?.BuffsSpentByWeaponHit ?? false);
            if (!usable.Any(IsRider)) return usable;

            var main = usable.FirstOrDefault(c => !QuickAction(c) && !IsRider(c) && c.Value >= MinMainValue);
            bool weapon = main != null && MakesWeaponAttack(main);
            // A rider that takes the Move action leaves only a single attack after it: not before a full attack (it needs
            // the Move), nor before an attack that walks first.
            bool singleInPlace = weapon && main.Slot == ActionSlot.Standard && !main.NeedsMove && !main.MoveFirst;

            var result = new List<Candidate>(usable.Count);
            foreach (var c in usable)
            {
                if (!IsRider(c)) { result.Add(c); continue; }
                bool keep = QuickAction(c) ? weapon : c.Slot == ActionSlot.Move && singleInPlace;
                if (keep) { result.Add(c); continue; }
                if (explain)
                    ReportOnce($"rider|{ctx.Unit.UniqueId}|{CombatMemory.Round}|{c.Key}",
                               $"[Buff] {ctx.Unit.CharacterName}: {c.Label} ({c.Value:0.#}) not used - it only adds to a weapon attack, " +
                               (main == null ? "and nothing else is worth doing" :
                                weapon ? $"and {main.Label} leaves no {(c.Slot == ActionSlot.Move ? "Move action" : "action")} for it"
                                       : $"and the best action is {main.Label} ({main.Value:0.#})"));
            }
            return result;
        }

        /// <summary>The option makes a weapon attack (RuleAttackWithWeapon): a weapon attack or Move+Attack, Charge, Vital
        /// Strike, Cleave, a melee-attack ability, Coup de Grace, or a kinetic archer blast (shot with the bow).</summary>
        private static bool MakesWeaponAttack(Candidate c)
        {
            if (c.Kind == CandidateKind.WeaponAttack) return true;
            if (c.Kind != CandidateKind.Ability || c.Ability == null) return false;
            var profile = AbilityProfile.Get(c.Ability);
            if (profile != null && (profile.IsCharge || profile.IsWeaponAttack)) return true;
            if (IsCoupDeGrace(c.Ability)) return true;
            try { return Kingmaker.UnitLogic.Class.Kineticist.AbilityKineticist.IsArcherBlast(c.Ability); }
            catch { return false; }
        }
    }
}
