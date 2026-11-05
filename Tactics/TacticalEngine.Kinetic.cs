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
        /// of its best other action (PriceBurn). Overflow adds to blasts only (AddKineticistElementalOverflow: damage of an
        /// own blast, attack of a kinetic-blast weapon). Orvath's recorded comparisons favored his bow, yet five burn-3
        /// blast commands (four Exploding Arrows, one Magma Blast) were priced "-0.0". Null: about one blast a round.
        /// ★ v0.3.28 test 6: the Overflow is counted as the damage it adds to a later blast that lands
        /// (<paramref name="landChance"/>, that blast's hit chance), and only for what it lifts that blast above the best
        /// other action (ValueModel.KineticOverflowCredit) - the shortfall is in the same expected-damage points.
        /// </summary>
        internal static float BurnCost(TacticalContext ctx, int burn, float? blastShortfall = null, float landChance = 1f)
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
            cost -= ValueModel.KineticOverflowCredit(gain, landChance, blastShortfall, ValueModel.Horizon(ctx.ExpectedRounds, 4f));
            return Math.Max(0f, cost);
        }

        /// <summary>
        /// ★ v0.3.28 test 6: what burn and arrows cost in this decision, worked out once (PriceBurn) and shared with the
        /// infusion planner, so both price a blast the same way: the Overflow comparison (BlastShortfall), the arrows the
        /// quiver pays now (the game's rule: a ranged weapon or nothing in the main hand), how many one Create Quiver makes
        /// (class level + Constitution bonus, read from the ability) and whether another can be made.
        /// </summary>
        internal sealed class KineticPriceBook
        {
            public float? Shortfall;
            public float LandChance = 1f;
            public int QuiverRanks;
            public int ArrowsPerQuiver = 1;
            public bool CanRemake;
            public readonly Dictionary<int, float> BurnPrices = new Dictionary<int, float>();
        }

        /// <summary>The main hand lets the quiver pay (AbilityKineticist.GetQuiverResource: a ranged weapon or nothing).</summary>
        internal static bool QuiverPays(UnitEntityData unit)
        {
            var weapon = unit?.Body?.PrimaryHand?.MaybeWeapon;
            return weapon == null || weapon.Blueprint.IsRanged;
        }

        /// <summary>The arrows the quiver holds and would pay with now (0 when the weapon in hand stops it).</summary>
        internal static int QuiverRanks(UnitEntityData unit, UnitPartKineticist part)
        {
            if (part?.QuiverBuff == null || !QuiverPays(unit)) return 0;
            return unit.Buffs.GetBuff(part.QuiverBuff)?.Rank ?? 0;
        }

        private static KineticPriceBook PriceBookFor(TacticalContext ctx, UnitPartKineticist part, List<Candidate> candidates,
                                                     Dictionary<AbilityData, BurnQuote> costs)
        {
            var book = new KineticPriceBook();
            BlastShortfall(ctx, part, candidates, costs, out book.Shortfall, out book.LandChance);
            book.QuiverRanks = QuiverRanks(ctx.Unit, part);
            if (book.QuiverRanks > 0)
            {
                try
                {
                    var abilities = ctx.AllowedAbilities ??
                                    UserAbilityPolicy.Allowed(ctx.Blocks, ctx.Unit, ctx.DiscoveredAbilities ?? AbilityDiscovery.Gather(ctx.Unit));
                    var quiver = QuiverAbility(part, abilities);
                    if (quiver != null)
                    {
                        book.ArrowsPerQuiver = QuiverArrows(quiver, part);
                        book.CanRemake = part.LeftBurn >= Math.Max(1, BurnOf(quiver));
                    }
                }
                catch { }
            }
            ctx.KineticPrices = book;
            return book;
        }

        /// <summary>The price of accepting <paramref name="burn"/> under this decision's Overflow comparison.</summary>
        internal static float BurnPriceOf(TacticalContext ctx, KineticPriceBook book, int burn)
        {
            if (burn <= 0) return 0f;
            if (book == null) return BurnCost(ctx, burn);
            if (!book.BurnPrices.TryGetValue(burn, out float price))
                book.BurnPrices[burn] = price = BurnCost(ctx, burn, book.Shortfall, book.LandChance);
            return price;
        }

        /// <summary>★ v0.3.28 test 6: burn taken plus arrows spent (ValueModel.ArrowPrice), in damage-equivalent points.</summary>
        internal static float KineticPrice(TacticalContext ctx, KineticPriceBook book, int burn, int arrows)
        {
            float price = BurnPriceOf(ctx, book, burn);
            if (arrows > 0 && book != null)
                price += ValueModel.ArrowPrice(arrows, BurnPriceOf(ctx, book, 1), book.ArrowsPerQuiver, book.CanRemake);
            return price;
        }

        /// <summary>The arrows the quiver pays for <paramref name="ability"/> now: min(arrows, its whole burn) (Spend).</summary>
        private static int ArrowsFor(AbilityData ability, KineticPriceBook book)
        {
            if (book == null || book.QuiverRanks <= 0 || ability?.Blueprint == null) return 0;
            var k = Kingmaker.Blueprints.BlueprintExtenstions.GetComponent<AbilityKineticist>(ability.Blueprint);
            if (k == null || k.AllowOnlyBurnCost) return 0;
            return Math.Min(book.QuiverRanks, TotalBurn(ability));
        }

        /// <summary>Burn priced into the kineticist's ability candidates (BurnCost); the label says how much.</summary>
        private static void PriceBurn(TacticalContext ctx, List<Candidate> candidates)
        {
            var part = ctx.Unit.Get<UnitPartKineticist>();
            if (part == null) return;
            // ★ v0.3.28 (codex review): one native burn read per ability in this PriceBurn call, shared with shortfall.
            var costs = new Dictionary<AbilityData, BurnQuote>();
            var book = PriceBookFor(ctx, part, candidates, costs);
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
                    bladeCost = BurnPriceOf(ctx, book, bladeBurn);
            }
            catch { }
            foreach (var c in candidates)
            {
                if (bladeCost > 0f && c.Value > 0f && (c.Kind == CandidateKind.WeaponAttack || (c.Ability != null && AbilityProfile.Get(c.Ability)?.IsCharge == true)))
                {
                    c.Value -= bladeCost;
                    c.Label += $" [blade burn {bladeBurn}: -{bladeCost:F1}]";
                    continue;
                }
                if (c.Kind != CandidateKind.Ability || c.Ability == null || c.Value <= 0f) continue;
                var cost = BurnQuoteFor(c.Ability, costs);
                if (!cost.Priced)
                {
                    // ★ v0.3.28 test 6: the arrows it takes from the quiver are priced too (they cost burn to make).
                    cost.Arrows = ArrowsFor(c.Ability, book);
                    cost.Price = KineticPrice(ctx, book, cost.Burn, cost.Arrows);
                    cost.Priced = true;
                    costs[c.Ability] = cost;
                }
                c.NativeBurn = cost.Burn;
                if (cost.Burn <= 0 && cost.Arrows <= 0) continue;
                c.BurnPrice = cost.Price;
                c.Value -= cost.Price;
                if (cost.Burn > 0) c.Label += $" [burn {cost.Burn}: -{BurnPriceOf(ctx, book, cost.Burn):F1}]";
                if (cost.Arrows > 0) c.Label += $" [arrows {cost.Arrows}: -{cost.Price - BurnPriceOf(ctx, book, cost.Burn):F1}]";
            }
        }

        // ★ v0.3.28 (codex review): a quote is local to PriceBurn; no mutable game state is cached across decisions.
        private struct BurnQuote
        {
            public int Burn;
            public int Arrows;
            public float Price;
            public bool Priced;
        }

        private static BurnQuote BurnQuoteFor(AbilityData ability, Dictionary<AbilityData, BurnQuote> costs)
        {
            if (!costs.TryGetValue(ability, out var quote))
                costs[ability] = quote = new BurnQuote { Burn = BurnOf(ability) };
            return quote;
        }

        /// <summary>
        /// ★ v0.3.28 test 4: how far the best own blast that costs no burn falls short of the best other standard or
        /// full-round action among <paramref name="candidates"/> (0 when a blast is best), or null when there is no free
        /// blast to compare (BurnCost then keeps its one-blast-a-round assumption).
        /// ★ v0.3.28 test 6: the other action may be any option that spends no burn - a weapon attack, a spell, another
        /// ability - not only a weapon attack: a caster-kineticist's best other action is often a spell. And the best free
        /// blast's chance to land (its attack roll), which the Overflow gain is weighted by.
        /// </summary>
        private static void BlastShortfall(TacticalContext ctx, UnitPartKineticist part, List<Candidate> candidates,
                                           Dictionary<AbilityData, BurnQuote> costs, out float? shortfall, out float landChance)
        {
            shortfall = null;
            landChance = 1f;
            float blast = float.MinValue, other = 0f;
            Candidate best = null;
            foreach (var c in candidates)
            {
                if (c.Value <= 0f || (c.Slot != ActionSlot.Standard && c.Slot != ActionSlot.FullRound)) continue;
                if (c.Kind == CandidateKind.Ability && c.Ability != null)
                {
                    bool ownBlast = IsOwnBlast(part, c.Ability);
                    if (ownBlast && c.Value <= blast) continue;
                    if (!ownBlast && c.Value <= other) continue;
                    if (BurnQuoteFor(c.Ability, costs).Burn != 0) continue;   // its own price is not in its value yet
                    if (ownBlast) { blast = c.Value; best = c; }
                    else other = c.Value;
                }
                else if (c.Kind == CandidateKind.WeaponAttack && c.Value > other) other = c.Value;
            }
            if (best == null) return;
            shortfall = Math.Max(0f, other - blast);
            try
            {
                var profile = AbilityProfile.Get(best.Ability);
                if (profile != null && profile.NeedsAttackRoll && best.Target != null)
                    landChance = CombatMath.SpellAttackHitChance(ctx.Unit, best.Target, best.Ability);
            }
            catch { }
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
            if (blasts.Count == 0) return;
            // ★ v0.3.28 test 6: 1. the quiver, whenever its arrows run short of the blasts the unit may want - not only
            // when burn blocks every blast. A plain blast costs no burn, so that case never came; with the quiver empty
            // mid-fight, the forms (Exploding Arrows, Spindle...) stayed out of reach for the rest of it.
            if (TryQuiverRefill(ctx, list, preview, part, abilities, blasts, out string why)) return;
            if (blasts.Any(a => a.IsAvailable)) return;
            var blocked = blasts.Where(BurnBlocked).ToList();
            if (blocked.Count == 0) return;

            float bestOther = list.Where(c => (c.Slot == ActionSlot.Standard || c.Slot == ActionSlot.FullRound) && c.Value > 0f && Affordable(ctx, c))
                                  .Select(c => c.Value).DefaultIfEmpty(0f).Max();
            float horizon = ValueModel.Horizon(ctx.ExpectedRounds, 4f);
            var quiverBuff = part.QuiverBuff;
            bool rangedInHand = QuiverPays(unit);

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
        /// ★ v0.3.28 test 6: the costliest shot the unit may fire: the whole burn (before the quiver) of its costliest
        /// kinetic ability - each own blast with the substance infusion that is on, or with another one the AI may switch
        /// on (the infusion planner's own candidates, priced by the game's cost arithmetic: KineticInfusionCost.TryReplace,
        /// which keeps infusion specialisation and other reductions) - and any other kinetic ability the arrows pay
        /// (not Create Quiver, not the abilities that accept burn on cast). Grows with the forms and infusions the unit
        /// learns and shrinks with the reductions it gets, at every level. <paramref name="switchInfusions"/>: "AI manages
        /// toggles" - else the current setup only.
        /// </summary>
        internal static int PlannedShotBurn(UnitEntityData unit, UnitPartKineticist part, IEnumerable<AbilityData> abilities,
                                             AbilityData quiver, bool switchInfusions, out AbilityData costliest)
        {
            costliest = null;
            int best = 0;
            List<ActivatableAbility> options = null, active = null;
            if (switchInfusions)
            {
                try
                {
                    var group = SubstanceToggles(unit);
                    active = group.Where(t => InfusionInstalled(unit, t)).ToList();
                    options = group.Where(t => !active.Contains(t) && !CombatMemory.PlayerOwnsToggle(unit, t) &&
                                               !UserAbilityPolicy.Blocks(unit, t) && !t.Blueprint.HiddenInUI && t.IsAvailable &&
                                               DescribeInfusion(t.Blueprint.Buff) is var d && d != null && d.Unsupported == null).ToList();
                }
                catch { options = null; }
            }
            foreach (var a in abilities)
            {
                if (a == null || a == quiver || UserAbilityPolicy.Blocks(unit, a)) continue;
                var k = Kingmaker.Blueprints.BlueprintExtenstions.GetComponent<AbilityKineticist>(a.Blueprint);
                if (k == null || k.AllowOnlyBurnCost) continue;
                int total;
                try
                {
                    var cost = k.CalculateBurnCost(a);
                    total = Math.Max(0, cost.Total);
                    if (options != null && options.Count > 0 && IsOwnBlast(part, a))
                    {
                        var removed = active.Select(t => t.Blueprint.Buff).ToArray();
                        foreach (var option in options)
                        {
                            var description = DescribeInfusion(option.Blueprint.Buff);
                            if (description == null || !description.Applies(a)) continue;
                            if (KineticInfusionCost.TryReplace(cost, a, removed, option.Blueprint.Buff, out int planned, 0))
                                total = Math.Max(total, planned);
                        }
                    }
                }
                catch { continue; }
                if (total > best) { best = total; costliest = a; }
            }
            return best;
        }

        private struct RefillMemo
        {
            public int Fight, Round, Ranks;
            public float Value;
            public AbilityData Blast;
            public int PerBlast;
        }

        /// <summary>Per unit: the best burn-blocked blast the arrows would open, worked out once a round (it is costly).</summary>
        private static readonly Dictionary<string, RefillMemo> RefillMemos = new Dictionary<string, RefillMemo>();

        /// <summary>Blocked blasts scored for a refill, at most, per unit and round (each one is a full scoring pass).</summary>
        private const int RefillBlastBudget = 4;

        /// <summary>
        /// ★ v0.3.28 test 6: Create Quiver in combat when its arrows run short of the costliest shot the unit may fire
        /// (PlannedShotBurn). Worth the best blast the new arrows pay for over the best action without them, for the rounds
        /// they last (ValueModel.QuiverRefillWorth), less the quiver's own burn; it takes the standard action, so it competes
        /// with that best action. The blast with arrows is either one the unit can cast now while paying burn - worth the
        /// burn and arrows it would no longer pay - or one burn blocks (the forms beyond the round's burn at low levels, any
        /// blast once the day's burn is spent), scored as it would be cast: at most RefillBlastBudget of them, chosen by how
        /// well their element's plain blast does now and then by cost, once a round. The game's limits decide what is
        /// affordable (MaxBurnPerRound, LeftBurn after the quiver's own burn), so it works the same at every level.
        /// </summary>
        private static bool TryQuiverRefill(TacticalContext ctx, List<Candidate> list, bool preview, UnitPartKineticist part,
                                            List<AbilityData> abilities, List<AbilityData> blasts, out string why)
        {
            why = null;
            var unit = ctx.Unit;
            var quiver = QuiverAbility(part, abilities);
            if (quiver == null) return false;
            if (!QuiverPays(unit)) { why = "the weapon in hand stops the quiver"; return false; }
            if (!quiver.IsAvailable) { why = "Create Quiver not available now"; return false; }
            if (SlotOf(quiver) == ActionSlot.Standard && !ctx.HasStandard) return false;
            int ranks = QuiverRanks(unit, part);
            int reserve = PlannedShotBurn(unit, part, abilities, quiver, ctx.Global?.AiManagesToggles ?? true, out var planned);
            if (!ValueModel.NeedsKineticQuiver(ranks, reserve))
            {
                why = ranks > 0 ? $"the quiver still holds {ranks}" : "no kinetic ability costs burn now";
                return false;
            }
            int arrows = QuiverArrows(quiver, part);
            int quiverBurn = BurnOf(quiver);
            int after = ranks + arrows;
            var book = ctx.KineticPrices;
            float horizon = ValueModel.Horizon(ctx.ExpectedRounds, 4f);
            float bestOther = list.Where(c => (c.Slot == ActionSlot.Standard || c.Slot == ActionSlot.FullRound) && c.Value > 0f && Affordable(ctx, c))
                                  .Select(c => c.Value).DefaultIfEmpty(0f).Max();
            // What paying with arrows would cost a blast of this whole burn: the burn the arrows do not cover, and the arrows.
            Func<int, float> priceWith = total => KineticPrice(ctx, book, Math.Max(0, total - after), Math.Min(total, after));
            Func<int, bool> affordableWith = total =>
            {
                int burn = Math.Max(0, total - after);
                return burn <= part.MaxBurnPerRound && burn <= part.LeftBurn - quiverBurn;
            };

            // a) Blasts it can cast now, paying burn the arrows would pay: worth that price saved.
            float best = 0f;
            AbilityData blast = null;
            int perBlast = 1;
            foreach (var c in list)
            {
                if (c.Kind != CandidateKind.Ability || c.Ability == null || !(c.Value > 0f) || !IsOwnBlast(part, c.Ability) ||
                    (c.Slot != ActionSlot.Standard && c.Slot != ActionSlot.FullRound) || !Affordable(ctx, c)) continue;
                int total = TotalBurn(c.Ability);
                if (total <= ranks || !affordableWith(total)) continue;
                float with = c.Value + c.BurnPrice - priceWith(total);
                if (with > best) { best = with; blast = c.Ability; perBlast = Math.Max(1, total); }
            }

            // b) Blasts burn blocks now, scored as they would be cast with the arrows (once a round).
            string key = unit.UniqueId;
            int round = CombatMemory.Round;
            int fight = CombatMemory.Fight;
            if (RefillMemos.TryGetValue(key, out var memo) && memo.Fight == fight && memo.Round == round && memo.Ranks == ranks)
            {
                if (memo.Blast != null && memo.Value > best) { best = memo.Value; blast = memo.Blast; perBlast = memo.PerBlast; }
            }
            else if (!preview)
            {
                // How well each element does now: the best value of its plain blast (or any affordable variant).
                var elementScore = new Dictionary<Kingmaker.UnitLogic.Abilities.Blueprints.BlueprintAbility, float>();
                foreach (var c in list)
                {
                    if (c.Kind != CandidateKind.Ability || c.Ability == null || !(c.Value > 0f) || !IsOwnBlast(part, c.Ability)) continue;
                    var element = c.Ability.Blueprint.Parent ?? c.Ability.Blueprint;
                    if (!elementScore.TryGetValue(element, out float v) || c.Value > v) elementScore[element] = c.Value;
                }
                var starved = blasts.Where(b => !b.IsAvailable && BurnBlocked(b))
                    .Select(b => new { Blast = b, Total = TotalBurn(b) })
                    .Where(x => x.Total > ranks && affordableWith(x.Total))
                    .OrderByDescending(x => elementScore.TryGetValue(x.Blast.Blueprint.Parent ?? x.Blast.Blueprint, out float v) ? v : 0f)
                    .ThenByDescending(x => x.Total)
                    .Take(RefillBlastBudget).Select(x => x.Blast).ToList();
                float blockedBest = 0f;
                AbilityData blockedBlast = null;
                int blockedPer = 1;
                if (starved.Count > 0)
                {
                    float scored = BestBlast(ctx, starved, b => TotalBurn(b), out var chosen, out int whole, valueActiveInfusions: true);
                    if (chosen != null)
                    {
                        blockedBest = scored - priceWith(whole);
                        blockedBlast = chosen;
                        blockedPer = Math.Max(1, whole);
                    }
                }
                RefillMemos[key] = new RefillMemo { Fight = fight, Round = round, Ranks = ranks, Value = blockedBest, Blast = blockedBlast, PerBlast = blockedPer };
                if (blockedBlast != null && blockedBest > best) { best = blockedBest; blast = blockedBlast; perBlast = blockedPer; }
            }
            if (blast == null)
            {
                why = $"{arrows} more arrows would not open a better blast";
                return false;
            }
            float quiverPrice = BurnPriceOf(ctx, book, quiverBurn);
            float value = ValueModel.QuiverRefillWorth(best - bestOther, after, perBlast, horizon, quiverPrice);
            if (!(value > 0f))
            {
                why = $"with the arrows {blast.Name} ({best:F1}) is not worth the turn over the best other option ({bestOther:F1})";
                return false;
            }
            list.RemoveAll(c => c.Kind == CandidateKind.Ability && c.Ability == quiver);
            list.Add(new Candidate
            {
                Kind = CandidateKind.Ability,
                Slot = SlotOf(quiver),
                Ability = quiver,
                Target = unit,
                Value = value,
                Label = $"{quiver.Name}->{unit.CharacterName}(quiver: {ranks}+{arrows} arrows for {blast.Name}, " +
                        $"+{best - bestOther:F1} a round{(quiverBurn > 0 ? $", burn {quiverBurn}: -{quiverPrice:F1}" : "")})",
                Key = $"abl:{CombatMemory.AbilityKey(quiver)}:{unit.UniqueId}"
            });
            Report(ctx, preview, $"arrows short of the costliest shot ({planned?.Name} {reserve}, quiver {ranks}) - {list.Last().Label} = {value:F1}");
            return true;
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
                                       out AbilityData chosen, out int burn, bool valueActiveInfusions = false)
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
                    value = EvaluateAbility(ctx, blast, valueActiveInfusions: valueActiveInfusions).Where(c => c.Value > 0f && Affordable(ctx, c))
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

        /// <summary>Per unit: the signature of the last refusal and when it lapses (QuiverToPrepare).</summary>
        private static readonly Dictionary<string, KeyValuePair<long, float>> QuiverRefusals = new Dictionary<string, KeyValuePair<long, float>>();
        private const float QuiverRecheckSeconds = 10f;

        /// <summary>
        /// ★ v0.3.28 (review of PR #18): what QuiverToPrepare's answer depends on, other than the action bar: arrows, burn
        /// used and the day's limit, the main-hand weapon, the block lists, the infusions and other toggles that are on
        /// (they change each blast's burn), hit points and nonlethal damage, and character level. Rest, a fight, a weapon
        /// swap, a block-list edit or an infusion switch changes it, so the next check is a full one.
        /// </summary>
        private static long QuiverSignature(UnitEntityData unit, UnitPartKineticist part)
        {
            unchecked
            {
                long h = 17;
                h = h * 31 + (unit.Buffs.GetBuff(part.QuiverBuff)?.Rank ?? 0);
                h = h * 31 + part.AcceptedBurn;
                h = h * 31 + part.MaxBurn;
                h = h * 31 + (unit.Body?.PrimaryHand?.MaybeWeapon?.UniqueId?.GetHashCode() ?? 0);
                h = h * 31 + UserAbilityPolicy.Revision;
                h = h * 31 + unit.HPLeft;
                h = h * 31 + unit.MaxHP;
                try { h = h * 31 + unit.DamageNonLethal; } catch { }
                h = h * 31 + (unit.Descriptor?.Progression?.CharacterLevel ?? 0);
                var toggles = unit.ActivatableAbilities?.RawFacts;
                if (toggles != null)
                    for (int i = 0; i < toggles.Count; i++)
                        if (toggles[i] != null && toggles[i].IsOn) h = h * 31 + toggles[i].Blueprint.AssetGuid.GetHashCode();
                return h;
            }
        }

        /// <summary>
        /// ★ v0.3.28 test 4 (Kinetic Sharpshooter play-test): the Create Quiver to cast now, out of combat, or null. The quiver
        /// (KineticSharpshooterObtainQuiverBuff) lasts until rest and holds class level + Constitution bonus arrows for 1
        /// burn; every kinetic ability that is not Create Quiver itself takes its burn from the arrows first
        /// (AbilityKineticist.CalculateCost / Spend, with a ranged weapon or nothing in the main hand). In combat it costs a
        /// standard action, so the engine offered it only when burn blocked every blast. The supplied 19-fight log has
        /// 16 archer-choice diagnostics with "quiver 0"; its native burn-message amounts cannot establish actual accepted
        /// burn (the log message reads a mutable part later). Out of combat it takes no combat turn, so it is made when:
        /// the quiver holds fewer arrows than the costliest kinetic ability the unit can use now, that ability costs burn,
        /// the quiver is castable (burn left), the weapon lets the arrows pay, and the burn's nonlethal damage (burn x
        /// character level) leaves at least half the unit's hit points. Abilities that accept burn on cast (Searing Flesh,
        /// Enveloping Winds, Shroud of Water: AbilityAcceptBurnOnCast) are not paid by the arrows and do not count.
        /// </summary>
        internal static AbilityData QuiverToPrepare(UnitEntityData unit)
        {
            var part = unit?.Get<UnitPartKineticist>();
            if (part?.QuiverBuff == null || (unit.CombatState?.IsInCombat ?? false)) return null;
            // ★ v0.3.28 (review of PR #18): the exploration driver asks every 0.5 s. A refusal is kept while nothing it
            // depends on has changed (QuiverSignature), for at most QuiverRecheckSeconds: action-bar discovery and the burn
            // of every kinetic variant (~60 for Orvath, native cost events each) are not redone twice a second in
            // exploration. The backstop covers what the signature does not read (a newly learned form, a cast
            // restriction).
            long signature = QuiverSignature(unit, part);
            float now = Time.time;
            if (QuiverRefusals.TryGetValue(unit.UniqueId, out var refused) && refused.Key == signature && now < refused.Value)
                return null;
            string why = null;
            AbilityData result = null;
            try
            {
                // ★ v0.3.28 (codex review): reject the wrong weapon before discovery/profile reads, and an uncastable
                // quiver before raising native burn-cost events for every kinetic variant. Do not cache across ticks:
                // infusions, the action bar and the user's block list can all change the answer.
                var weapon = unit.Body?.PrimaryHand?.MaybeWeapon;
                if (weapon != null && !weapon.Blueprint.IsRanged)
                    why = $"{weapon.Name} in hand: the arrows pay only with a ranged weapon or empty hand";
                else
                {
                    var abilities = AbilityDiscovery.Gather(unit);
                    var quiver = QuiverAbility(part, abilities);
                    if (quiver == null) return null;
                    if (UserAbilityPolicy.Blocks(unit, quiver)) why = "Create Quiver is on the block list";
                    else if (!quiver.IsAvailableForCast) why = $"Create Quiver not castable (burn {part.AcceptedBurn}/{part.MaxBurn})";
                    else
                    {
                        int ranks = unit.Buffs.GetBuff(part.QuiverBuff)?.Rank ?? 0;
                        // ★ v0.3.28 test 6: the costliest shot it may fire, with a substance infusion the AI may switch on
                        // in the fight (it is off between fights) - not only the setup that happens to be on now.
                        int burn = PlannedShotBurn(unit, part, abilities, quiver, ModSettings.Instance?.AiManagesToggles ?? true,
                                                   out var costliest);
                        int quiverBurn = BurnOf(quiver);
                        float loss = quiverBurn * Level(unit);
                        if (costliest == null) why = "no kinetic ability costs burn now";
                        else if (!ValueModel.NeedsKineticQuiver(ranks, burn))
                            why = $"the quiver holds {ranks}, enough for {costliest.Name} (burn {burn})";
                        else if (ValueModel.PreparationBurnBelowHpFloor(CombatMath.RealHpBeforeDown(unit), unit.MaxHP, loss))
                            why = $"its burn ({loss:0} nonlethal) would leave less than half the hit points";
                        else
                        {
                            result = quiver;
                            why = $"make the quiver: {QuiverArrows(quiver, part)} arrows for burn {quiverBurn} " +
                                  $"(holds {ranks}; {costliest.Name} costs {burn}; burn {part.AcceptedBurn}/{part.MaxBurn})";
                        }
                    }
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
            if (result == null) QuiverRefusals[unit.UniqueId] = new KeyValuePair<long, float>(signature, now + QuiverRecheckSeconds);
            else QuiverRefusals.Remove(unit.UniqueId);
            return result;
        }

        /// <summary>★ v0.3.19 (test6): a kineticist infusion or gather mode - it changes the next blast, nothing else.</summary>
        internal static bool IsBlastModifier(Kingmaker.UnitLogic.ActivatableAbilities.BlueprintActivatableAbility bp) =>
            bp != null && ModifierGroups.Contains(bp.Group);
    }
}
