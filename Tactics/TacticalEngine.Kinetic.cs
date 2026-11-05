// ★ v0.3.19 (test6): kineticist burn, the Kinetic Sharpshooter's quiver and infusions that block every blast - from the
// game's Kineticist blueprints (docs/TACTICAL_AI.md §38).
//
//   - Burn is nonlethal damage: BurnFeature's AddKineticistBurnValueChangedTrigger applies BurnEffectBuff, an
//     AddContextStatBonus on DamageNonLethal of burn x character level (KineticNonLethalDamageProperty), and the game
//     knocks a unit out when damage + nonlethal reach its hit points (UnitLifeController). It stays until rest. A burn
//     cost (BurnCost) is the drop chance it adds - the heal model's drop-chance term, mirrored - and a small day cost,
//     less the Elemental Overflow it raises for the blasts still to come.
//   - A blast that costs burn passes the game's restriction only with that much burn left this round
//     (AbilityKineticist.IsAbilityRestrictionPassed: MaxBurnPerRound, LeftBurnThisRound = min(left today, left this
//     round)): 1 per round below level 6, and nothing once the day's burn is spent. A substance infusion is on by
//     default once learned (IsOnByDefault) and adds burn to every blast (AddKineticistBurnModifier on the infusion's
//     buff): Cluster Arrows +3 for a Kinetic Sharpshooter - more than a low-level kineticist may take in a round, so
//     no blast at all; for any kineticist, no blast once the day's burn is gone. Only the weapon was left
//     ("kineticists aren't blasting as much as they should, especially kinetic shooter just does standard attack").
//   - The Kinetic Sharpshooter's quiver (UnitPartKineticist.QuiverBuff, made by Create Quiver: class level + Constitution
//     bonus ranks for 1 burn) pays a blast's burn first (AbilityKineticist.CalculateCost / Spend, with a ranged weapon in
//     hand). Its buff does nothing the buff analyser can read, so the AI never made one.
//
// When no blast of the kineticist is possible and burn is what blocks them, two options compete with the rest:
//   1. Create Quiver, when the arrows make a blast affordable from next round: worth what the blast adds over the best
//      other option, for the rounds the arrows last, less the quiver's own burn.
//   2. Otherwise, with "AI manages toggles" on, switch off (free, at once) the infusion whose burn blocks them: worth what
//      the blast adds over the best other option for the rest of the fight. Not a toggle the player switched in this
//      fight; put back on when the fight ends (CombatMemory.SwitchOffAiToggles), so the player's setup stays.
using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.Blueprints;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.RuleSystem.Rules;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.ActivatableAbilities;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Class.Kineticist;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.UnitLogic.Mechanics.Components;
using UnityEngine;
using CompanionAI_Pathfinder.Settings;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class TacticalEngine
    {
        /// <summary>Share of each burn hit point counted as a day cost (it is not healed until rest).</summary>
        private const float BurnDayCostPerHp = 0.1f;

        /// <summary>The ability is one of the kineticist's own blasts (UnitPartKineticist.Blasts, the variant's parent).</summary>
        internal static bool IsOwnBlast(UnitPartKineticist part, AbilityData ability)
        {
            var bp = ability?.Blueprint;
            if (part?.Blasts == null || bp == null) return false;
            if (Kingmaker.Blueprints.BlueprintExtenstions.GetComponent<AbilityKineticist>(bp) == null) return false;
            var blast = bp.Parent ?? bp;
            return part.Blasts.Contains(blast);
        }

        /// <summary>The game's blast restriction fails: not enough burn left this round (or today) for its cost.</summary>
        private static bool BurnBlocked(AbilityData ability)
        {
            var k = Kingmaker.Blueprints.BlueprintExtenstions.GetComponent<AbilityKineticist>(ability.Blueprint);
            try { return k != null && !k.IsAbilityRestrictionPassed(ability); }
            catch { return false; }
        }

        /// <summary>The burn the game takes for this ability now (after the quiver), 0 for anything else.</summary>
        internal static int BurnOf(AbilityData ability)
        {
            if (ability?.Blueprint == null || ability.Caster?.Unit?.Get<UnitPartKineticist>() == null) return 0;
            int burn = 0;
            try
            {
                var k = Kingmaker.Blueprints.BlueprintExtenstions.GetComponent<AbilityKineticist>(ability.Blueprint);
                if (k != null) burn += Math.Max(0, k.CalculateCost(ability));
                // ★ v0.3.19 (test7): burn accepted on cast (AbilityAcceptBurnOnCast: Searing Flesh and other wild talents).
                var onCast = Kingmaker.Blueprints.BlueprintExtenstions.GetComponent<AbilityAcceptBurnOnCast>(ability.Blueprint);
                if (onCast != null) burn += Math.Max(0, onCast.BurnValue);
            }
            catch { }
            return burn;
        }

        /// <summary>
        /// ★ v0.3.19 (test7): the ability spends something limited - an ability resource (AbilityResourceLogic that spends,
        /// with a resource) or burn.
        /// </summary>
        internal static bool SpendsLimitedUse(AbilityData ability)
        {
            if (ability?.Blueprint == null) return false;
            try
            {
                foreach (var logic in Kingmaker.Blueprints.BlueprintExtenstions.GetComponents<Kingmaker.UnitLogic.Abilities.Components.AbilityResourceLogic>(ability.Blueprint))
                    if (logic.IsSpendResource && logic.RequiredResource != null) return true;
                return BurnOf(ability) > 0 ||
                       Kingmaker.Blueprints.BlueprintExtenstions.GetComponent<AbilityAcceptBurnOnCast>(ability.Blueprint) != null;
            }
            catch { return false; }
        }

        /// <summary>
        /// What accepting <paramref name="burn"/> costs the kineticist, in damage-equivalent points: the chance to drop
        /// it adds before the next turn (burn x character level of nonlethal damage), a small day cost, less the
        /// Elemental Overflow damage it adds to the blasts left in this fight. A burn that would knock the unit out
        /// costs its whole worth.
        /// ★ v0.3.28 test 4: <paramref name="blastShortfall"/> - how far the unit's best blast that costs no burn falls short
        /// of its best other attack (PriceBurn). Overflow adds to blasts only (AddKineticistElementalOverflow: damage of an
        /// own blast, attack of a kinetic-blast weapon), so when its gain does not close that gap the unit attacks with its
        /// weapon in the rounds after and the gain counts for nothing. Orvath's bow full attacks beat his blasts all play-test,
        /// yet a burn-3 Exploding Arrows was priced "-0.0" five times (30 nonlethal hit points of 3 burn a day, offset by
        /// Overflow for blasts he would not shoot). Null: about one blast a round, as before.
        /// </summary>
        internal static float BurnCost(TacticalContext ctx, int burn, float? blastShortfall = null)
        {
            if (burn <= 0) return 0f;
            var unit = ctx.Unit;
            float loss = burn * Level(unit);
            float hp = CombatMath.RealHpBeforeDown(unit);   // ★ v0.3.28 test 3: burn is nonlethal, not taken by temporary HP
            float worth = AllyWorth(ctx, unit);
            if (loss >= hp) return worth * 2f;
            float cost = loss * BurnDayCostPerHp;
            float danger = IncomingDamage(ctx, unit);
            if (danger > 0f)
                cost += (Mathf.Exp(-(hp - loss) / danger) - Mathf.Exp(-hp / danger)) * worth;
            // The Overflow the burn raises, for the blasts after this one (about one a round).
            int overflowNow = 0, overflowAfter = 0;
            foreach (var blast in (unit.Get<UnitPartKineticist>()?.Blasts ?? Enumerable.Empty<Kingmaker.UnitLogic.Abilities.Blueprints.BlueprintAbility>()).Take(1))
            {
                overflowNow = CombatMath.ElementalOverflowDamage(unit, blast);
                overflowAfter = CombatMath.ElementalOverflowDamage(unit, blast, burn);
            }
            int gain = overflowAfter - overflowNow;
            if (blastShortfall == null || gain >= blastShortfall.Value)
                cost -= gain * Math.Max(0f, ValueModel.Horizon(ctx.ExpectedRounds, 4f) - 1f);
            return Math.Max(0f, cost);
        }

        /// <summary>Burn priced into the kineticist's ability candidates (BurnCost); the label says how much.</summary>
        private static void PriceBurn(TacticalContext ctx, List<Candidate> candidates)
        {
            var part = ctx.Unit.Get<UnitPartKineticist>();
            if (part == null) return;
            var costs = new Dictionary<AbilityData, KeyValuePair<int, float>>();
            // A kinetic blade not yet activated this round: the game activates it before the attack, for its burn.
            float bladeCost = 0f;
            int bladeBurn = 0;
            try
            {
                var weapon = ctx.Unit.Body?.PrimaryHand?.MaybeWeapon;
                var blade = weapon == null ? null : Kingmaker.Blueprints.BlueprintExtenstions.GetComponent<WeaponKineticBlade>(weapon.Blueprint);
                var activation = blade?.GetActivationAbility(ctx.Unit.Descriptor);
                if (activation != null && !part.IsBladeActivated && (bladeBurn = BurnOf(activation)) > 0 &&
                    bladeBurn <= part.LeftBurnThisRound)
                    bladeCost = BurnCost(ctx, bladeBurn);
            }
            catch { }
            float? shortfall = BlastShortfall(part, candidates);
            foreach (var c in candidates)
            {
                if (bladeCost > 0f && c.Value > 0f && (c.Kind == CandidateKind.WeaponAttack || (c.Ability != null && AbilityProfile.Get(c.Ability)?.IsCharge == true)))
                {
                    c.Value -= bladeCost;
                    c.Label += $" [blade burn {bladeBurn}: -{bladeCost:F1}]";
                    continue;
                }
                if (c.Kind != CandidateKind.Ability || c.Ability == null || c.Value <= 0f) continue;
                if (!costs.TryGetValue(c.Ability, out var cost))
                {
                    int burn = BurnOf(c.Ability);
                    costs[c.Ability] = cost = new KeyValuePair<int, float>(burn, BurnCost(ctx, burn, shortfall));
                }
                c.NativeBurn = cost.Key;
                if (cost.Key <= 0) continue;
                c.BurnPrice = cost.Value;
                c.Value -= cost.Value;
                c.Label += $" [burn {cost.Key}: -{cost.Value:F1}]";
            }
        }

        /// <summary>
        /// ★ v0.3.28 test 4: how far the best own blast that costs no burn falls short of the best other standard or
        /// full-round attack among <paramref name="candidates"/> (0 when a blast is best), or null when there is no free
        /// blast to compare (BurnCost then keeps its one-blast-a-round assumption).
        /// </summary>
        private static float? BlastShortfall(UnitPartKineticist part, List<Candidate> candidates)
        {
            float blast = float.MinValue, other = 0f;
            Dictionary<AbilityData, bool> free = null;
            foreach (var c in candidates)
            {
                if (c.Value <= 0f || (c.Slot != ActionSlot.Standard && c.Slot != ActionSlot.FullRound)) continue;
                if (c.Kind == CandidateKind.Ability && c.Ability != null)
                {
                    if (c.Value <= blast) continue;
                    if (free == null) free = new Dictionary<AbilityData, bool>();
                    if (!free.TryGetValue(c.Ability, out bool ok))
                        free[c.Ability] = ok = IsOwnBlast(part, c.Ability) && BurnOf(c.Ability) == 0;
                    if (ok) blast = c.Value;
                }
                else if (c.Kind == CandidateKind.WeaponAttack && c.Value > other) other = c.Value;
            }
            if (blast == float.MinValue) return null;
            return Math.Max(0f, other - blast);
        }

        private static void AddKineticCandidates(TacticalContext ctx, List<Candidate> list, bool preview)
        {
            var unit = ctx.Unit;
            var part = unit.Get<UnitPartKineticist>();
            if (part == null) return;
            // ★ user-ability-blacklist: the quiver and the blasts that would justify it, as AI Control may use them.
            var abilities = ctx.AllowedAbilities ??
                            UserAbilityPolicy.Allowed(ctx.Blocks, unit, ctx.DiscoveredAbilities ?? AbilityDiscovery.Gather(unit));
            var blasts = abilities.Where(a => IsOwnBlast(part, a)).ToList();
            if (blasts.Count == 0 || blasts.Any(a => a.IsAvailable)) return;
            var blocked = blasts.Where(BurnBlocked).ToList();
            if (blocked.Count == 0) return;

            float bestOther = list.Where(c => (c.Slot == ActionSlot.Standard || c.Slot == ActionSlot.FullRound) && c.Value > 0f && Affordable(ctx, c))
                                  .Select(c => c.Value).DefaultIfEmpty(0f).Max();
            float horizon = ValueModel.Horizon(ctx.ExpectedRounds, 4f);
            string why = null;

            // 1. The quiver.
            var quiverBuff = part.QuiverBuff;
            var quiver = QuiverAbility(part, abilities);
            bool rangedInHand = unit.Body?.PrimaryHand?.MaybeWeapon?.Blueprint?.IsRanged ?? false;
            if (quiver != null && rangedInHand)
            {
                int ranks = unit.Buffs.GetBuff(quiverBuff)?.Rank ?? 0;
                int arrows = QuiverArrows(quiver, part);
                int quiverBurn = BurnOf(quiver);
                if (!quiver.IsAvailable) why = "Create Quiver not available now";
                else if (ranks > 0) why = $"the quiver still holds {ranks}";
                else
                {
                    var best = BestBlast(ctx, blocked, b =>
                    {
                        int total = TotalBurn(b);
                        int after = Math.Max(0, total - arrows);
                        return after <= part.MaxBurnPerRound && after <= part.LeftBurn - quiverBurn ? total : -1;
                    }, out var blast, out int perBlast);
                    if (blast == null) why = $"{arrows} arrows would not make a blast affordable";
                    else
                    {
                        float uses = Math.Min(horizon - 1f, arrows / (float)Math.Max(1, perBlast));
                        float burnCost = BurnCost(ctx, quiverBurn);
                        float value = Math.Max(0f, best - bestOther) * uses - burnCost;
                        if (value > 0f)
                        {
                            list.RemoveAll(c => c.Kind == CandidateKind.Ability && c.Ability == quiver);
                            list.Add(new Candidate
                            {
                                Kind = CandidateKind.Ability,
                                Slot = SlotOf(quiver),
                                Ability = quiver,
                                Target = unit,
                                Value = value,
                                Label = $"{quiver.Name}->{unit.CharacterName}(quiver: {arrows} arrows pay the burn of {blast.Name}, " +
                                        $"+{best - bestOther:F1} a round for {uses:F1} rounds{(quiverBurn > 0 ? $", burn {quiverBurn}: -{burnCost:F1}" : "")})",
                                Key = $"abl:{CombatMemory.AbilityKey(quiver)}:{unit.UniqueId}"
                            });
                            Report(ctx, preview, $"blasts blocked by burn - {list.Last().Label} = {value:F1}");
                            return;
                        }
                        why = $"the quiver's blast ({best:F1}) is not worth more than the best other option ({bestOther:F1})";
                    }
                }
            }

            // 2. Switch off the infusion whose burn blocks them.
            if (!(ModSettings.Instance?.AiManagesToggles ?? true))
            {
                Report(ctx, preview, $"blasts blocked by burn ({BlockedNote(part)}); infusions left alone (\"AI manages toggles\" is off)" +
                                     (why != null ? $"; {why}" : ""));
                return;
            }
            Candidate bestOff = null;
            foreach (var toggle in unit.ActivatableAbilities?.Enumerable ?? Enumerable.Empty<ActivatableAbility>())
            {
                if (toggle?.Blueprint == null || !(toggle.IsOn || toggle.IsStarted) || !IsBlastModifier(toggle.Blueprint)) continue;
                if (CombatMemory.PlayerOwnsToggle(unit, toggle) || CombatMemory.ToggleSwitchedThisRound(unit, toggle)) continue;
                var modifiers = toggle.Blueprint.Buff == null ? null
                    : Kingmaker.Blueprints.BlueprintExtenstions.GetComponents<AddKineticistBurnModifier>(toggle.Blueprint.Buff).Where(m => m.Value > 0).ToList();
                if (modifiers == null || modifiers.Count == 0) continue;
                int quiverRanks = rangedInHand && quiverBuff != null ? unit.Buffs.GetBuff(quiverBuff)?.Rank ?? 0 : 0;
                var best = BestBlast(ctx, blocked, b =>
                {
                    int total = TotalBurn(b);
                    int without = total - modifiers.Where(m => Applies(m, b)).Sum(m => m.Value);
                    int after = Math.Max(0, Math.Max(0, without) - quiverRanks);
                    return after <= part.LeftBurnThisRound ? after : -1;
                }, out var blast, out int burnAfter);
                if (blast == null) continue;
                float value = (best - BurnCost(ctx, burnAfter) - bestOther) * horizon;
                if (value <= 0f || (bestOff != null && value <= bestOff.Value)) continue;
                bestOff = new Candidate
                {
                    Kind = CandidateKind.Toggle,
                    Slot = ActionSlot.Free,
                    Toggle = toggle,
                    ToggleOff = true,
                    Value = value,
                    Label = $"Stop {toggle.Name} (its burn blocks every blast: {blast.Name} then costs {burnAfter}, " +
                            $"+{best - bestOther:F1} a round)",
                    Key = $"tgl-off:{toggle.Blueprint.AssetGuid}"
                };
            }
            if (bestOff != null)
            {
                list.Add(bestOff);
                Report(ctx, preview, $"blasts blocked by burn - {bestOff.Label} = {bestOff.Value:F1}");
            }
            else Report(ctx, preview, $"blasts blocked by burn ({BlockedNote(part)}); nothing to do about it" + (why != null ? $" ({why})" : ""));
        }

        /// <summary>
        /// ★ v0.3.23 (quiver task, docs/TASK_BOMBS_CARNAGE_QUIVER.md §3): a unit that shoots its blasts through the bow
        /// (UnitPartKineticist.CanUseArcherBlast) chose a weapon attack while a blast was there to use - once per fight, both
        /// values, and why they are compared as they are: a bow's arrows carry no blast damage in the game (only a kinetic
        /// blade's hits cast a blast, KineticistController.TryHandleKineticBladeAttack), and the quiver only pays a blast's
        /// burn (AbilityKineticist.CalculateCost / Spend). Evidence for the player's report; nothing is changed.
        /// </summary>
        private static void NoteArcherChoice(TacticalContext ctx, List<Candidate> usable, Candidate chosen)
        {
            try
            {
                if (chosen?.Kind != CandidateKind.WeaponAttack) return;
                var part = ctx.Unit.Get<UnitPartKineticist>();
                if (part == null || !part.CanUseArcherBlast()) return;
                var blast = usable.FirstOrDefault(c => c.Kind == CandidateKind.Ability && c.Ability != null && IsOwnBlast(part, c.Ability) &&
                                                       AbilityKineticist.IsArcherBlast(c.Ability));
                if (blast == null || !CombatMemory.FirstThisFight($"archer|{ctx.Unit.UniqueId}")) return;
                int quiver = part.QuiverBuff != null ? ctx.Unit.Buffs.GetBuff(part.QuiverBuff)?.Rank ?? 0 : 0;
                Main.Log($"[Kinetic] {ctx.Unit.CharacterName}: {chosen.Label} ({chosen.Value:0.#}) over the best blast through the bow, " +
                         $"{blast.Label} ({blast.Value:0.#}); quiver {quiver}. A bow's arrows add no blast damage in the game (only a " +
                         "kinetic blade's hits do) and the quiver pays a blast's burn only, so the two are compared as they are");
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Kinetic] {ctx.Unit?.CharacterName} archer note: {ex.Message}");
            }
        }

        private static void Report(TacticalContext ctx, bool preview, string text)
        {
            if (preview) return;
            ReportOnce($"kinetic|{ctx.Unit.UniqueId}|{CombatMemory.Round}|{text.GetHashCode()}", $"[Kinetic] {ctx.Unit.CharacterName}: {text}");
        }

        private static string BlockedNote(UnitPartKineticist part) =>
            $"burn {part.AcceptedBurn}/{part.MaxBurn}, {part.LeftBurnThisRound} of {part.MaxBurnPerRound} left this round";

        /// <summary>
        /// The best value among <paramref name="blocked"/> blasts that <paramref name="burnWith"/> says would be
        /// affordable (it returns a burn figure for the label, or -1), as the engine would score them if they were.
        /// </summary>
        private static float BestBlast(TacticalContext ctx, List<AbilityData> blocked, Func<AbilityData, int> burnWith,
                                       out AbilityData chosen, out int burn)
        {
            chosen = null;
            burn = 0;
            float best = 0f;
            foreach (var blast in blocked)
            {
                int b = burnWith(blast);
                if (b < 0) continue;
                float value;
                try
                {
                    // ★ kinetic-infusion-value: this existing recovery estimate may switch an infusion OFF. Do not
                    // credit an active infusion in that estimate; no hypothetical configuration is evaluated here.
                    value = EvaluateAbility(ctx, blast, valueActiveInfusions: false).Where(c => c.Value > 0f && Affordable(ctx, c))
                                                       .Select(c => c.Value).DefaultIfEmpty(0f).Max();
                }
                catch { continue; }
                if (value > best) { best = value; chosen = blast; burn = b; }
            }
            return best;
        }

        /// <summary>The ability's whole burn before the quiver (AbilityKineticist.CalculateBurnCost: infusions, gather power).</summary>
        private static int TotalBurn(AbilityData ability)
        {
            try { return Math.Max(0, AbilityKineticist.CalculateAbilityBurnCost(ability)?.Total ?? 0); }
            catch { return 0; }
        }

        /// <summary>AddKineticistBurnModifier's own test: no list, or the blast or its parent in it.</summary>
        private static bool Applies(AddKineticistBurnModifier modifier, AbilityData ability)
        {
            var list = modifier.AppliableTo;
            if (!list.Any()) return true;
            var bp = ability.Blueprint;
            return list.Contains(bp) || (bp.Parent != null && list.Contains(bp.Parent));
        }

        /// <summary>
        /// The arrows Create Quiver makes: the count of its ContextActionRepeatedActions (KineticSharpshooterQuiverProperty:
        /// class level + Constitution bonus), in the ability's own context; the class level when it cannot be read.
        /// </summary>
        private static int QuiverArrows(AbilityData quiver, UnitPartKineticist part)
        {
            try
            {
                var run = Kingmaker.Blueprints.BlueprintExtenstions.GetComponent<Kingmaker.UnitLogic.Abilities.Components.AbilityEffectRunAction>(quiver.Blueprint);
                var repeat = run?.Actions?.Actions?.OfType<ContextActionRepeatedActions>().FirstOrDefault();
                if (repeat != null)
                {
                    var context = CombatMath.CreateContext(quiver, new Kingmaker.Utility.TargetWrapper(quiver.Caster.Unit));
                    int count = Mathf.RoundToInt(CombatMath.EvaluateDice(repeat.Value, context, 0));
                    if (count > 0) return count;
                }
            }
            catch { }
            return Math.Max(1, part.ClassLevel);
        }

        /// <summary>The kineticist's Create Quiver: the ability that puts the quiver buff on its caster (AbilityProfile).</summary>
        private static AbilityData QuiverAbility(UnitPartKineticist part, IEnumerable<AbilityData> abilities)
        {
            var quiverBuff = part?.QuiverBuff;
            if (quiverBuff == null || abilities == null) return null;
            return abilities.FirstOrDefault(a =>
            {
                var p = AbilityProfile.Get(a);
                return p != null && (p.CasterBuffs.Contains(quiverBuff) || p.TargetBuffs.Contains(quiverBuff));
            });
        }

        private static readonly Dictionary<string, string> QuiverNotes = new Dictionary<string, string>();

        /// <summary>
        /// ★ v0.3.28 test 4 (Kinetic Sharpshooter play-test): the Create Quiver to cast now, out of combat, or null. The quiver
        /// (KineticSharpshooterObtainQuiverBuff) lasts until rest and holds class level + Constitution bonus arrows for 1
        /// burn; every kinetic ability that is not Create Quiver itself takes its burn from the arrows first
        /// (AbilityKineticist.CalculateCost / Spend, with a ranged weapon or nothing in the main hand). In combat it costs a
        /// standard action, so the engine offered it only when burn blocked every blast - and the AI never made one: 18
        /// fights with "quiver 0", while Orvath took 48 burn. Out of combat it costs no action, so it is made there when:
        /// the quiver holds fewer arrows than the costliest kinetic ability the unit can use now, that ability costs burn,
        /// the quiver is castable (burn left), the weapon lets the arrows pay, and the burn's nonlethal damage (burn x
        /// character level) leaves at least half the unit's hit points. Abilities that accept burn on cast (Searing Flesh,
        /// Enveloping Winds, Shroud of Water: AbilityAcceptBurnOnCast) are not paid by the arrows and do not count.
        /// </summary>
        internal static AbilityData QuiverToPrepare(UnitEntityData unit)
        {
            var part = unit?.Get<UnitPartKineticist>();
            if (part?.QuiverBuff == null || (unit.CombatState?.IsInCombat ?? false)) return null;
            string why = null;
            AbilityData result = null;
            try
            {
                var abilities = AbilityDiscovery.Gather(unit);
                var quiver = QuiverAbility(part, abilities);
                if (quiver == null) return null;
                var weapon = unit.Body?.PrimaryHand?.MaybeWeapon;
                int ranks = unit.Buffs.GetBuff(part.QuiverBuff)?.Rank ?? 0;
                AbilityData costliest = null;
                int burn = 0;
                foreach (var a in abilities)
                {
                    if (a == quiver || UserAbilityPolicy.Blocks(unit, a)) continue;
                    var k = Kingmaker.Blueprints.BlueprintExtenstions.GetComponent<AbilityKineticist>(a.Blueprint);
                    if (k == null || k.AllowOnlyBurnCost) continue;
                    int total = TotalBurn(a);
                    if (total > burn) { burn = total; costliest = a; }
                }
                int quiverBurn = BurnOf(quiver);
                float loss = quiverBurn * Level(unit);
                if (UserAbilityPolicy.Blocks(unit, quiver)) why = "Create Quiver is on the block list";
                else if (weapon != null && !weapon.Blueprint.IsRanged) why = $"{weapon.Name} in hand: the arrows pay only with a ranged weapon";
                else if (costliest == null) why = "no kinetic ability costs burn now";
                else if (ranks >= burn) why = $"the quiver holds {ranks}, enough for {costliest.Name} (burn {burn})";
                else if (!quiver.IsAvailableForCast) why = $"Create Quiver not castable (burn {part.AcceptedBurn}/{part.MaxBurn})";
                else if (CombatMath.RealHpBeforeDown(unit) - loss < 0.5f * Math.Max(1, unit.MaxHP))
                    why = $"its burn ({loss:0} nonlethal) would leave less than half the hit points";
                else
                {
                    result = quiver;
                    why = $"make the quiver: {QuiverArrows(quiver, part)} arrows for burn {quiverBurn} " +
                          $"(holds {ranks}; {costliest.Name} costs {burn}; burn {part.AcceptedBurn}/{part.MaxBurn})";
                }
            }
            catch (Exception ex)
            {
                why = $"quiver check failed: {ex.Message}";
                result = null;
            }
            if (why != null && (!QuiverNotes.TryGetValue(unit.UniqueId, out var last) || last != why))
            {
                QuiverNotes[unit.UniqueId] = why;
                if (result != null) Main.Log($"[Kinetic] {unit.CharacterName}: out of combat, {why}");
                else Main.Verbose($"[Kinetic] {unit.CharacterName}: out of combat, no quiver - {why}");
            }
            return result;
        }

        /// <summary>★ v0.3.19 (test6): a kineticist infusion or gather mode - it changes the next blast, nothing else.</summary>
        internal static bool IsBlastModifier(Kingmaker.UnitLogic.ActivatableAbilities.BlueprintActivatableAbility bp) =>
            bp != null && ModifierGroups.Contains(bp.Group);
    }
}
