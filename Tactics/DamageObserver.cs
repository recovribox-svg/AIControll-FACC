// ★ v0.3.14 (test5): what each enemy actually took from each damage type, read from the game's own damage results.
//
// The spell damage estimate subtracts resistances and immunities the AI can find on the target (CombatMath
// .AfterTargetReduction). Some defences are invisible to that model - another energy-immunity component, a mod's
// custom handler, a buff - and then the AI keeps casting a spell that does nothing: Orvath cast Fire Blast on Quasits
// 31 times in one session. The game reports every damage roll (RuleDealDamage, after resistances): for each damage type
// the target's flat reduction, its multiplier (partial immunity, vulnerability) and total immunity. The estimate then
// uses what was seen. Per target, plus per kind of creature (blueprint) for the next one of the same kind; cleared when
// the party's fight ends. Read-only: only the finished rule is looked at, in both combat modes.
using System;
using System.Collections.Generic;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Enums.Damage;
using Kingmaker.PubSubSystem;
using Kingmaker.RuleSystem.Rules.Damage;

namespace CompanionAI_Pathfinder.Tactics
{
    public sealed class DamageObserver : IGlobalRulebookHandler<RuleDealDamage>
    {
        public sealed class Seen
        {
            public bool Immune;
            public int Flat;
            public float Factor = 1f;
            public string Source;

            public float Apply(float value) => Immune ? 0f : Math.Max(0f, value * Factor - Flat);
        }

        private static DamageObserver _instance;
        private static readonly Dictionary<string, Seen> ByUnit = new Dictionary<string, Seen>();
        private static readonly Dictionary<string, Seen> ByKind = new Dictionary<string, Seen>();
        private static readonly HashSet<string> Reported = new HashSet<string>();

        /// <summary>★ v0.3.20 (playtest 1): per AI unit and ability, the damage of its hits against the estimate.</summary>
        private sealed class Check
        {
            public string Who, What;
            public int Hits;
            public float Dealt, Estimated;
        }

        /// <summary>★ v0.3.20: when each unit's ability last hit each target (its rolls within <see cref="OneHit"/> are one hit).</summary>
        private static readonly Dictionary<string, TimeSpan> LastHit = new Dictionary<string, TimeSpan>();

        private static readonly Dictionary<string, Check> Checks = new Dictionary<string, Check>();

        /// <summary>
        /// ★ v0.3.20 (0.3.19 feedback): per AI unit and kinetic blast, its hits over the whole session (not cleared with a
        /// fight: what the blast does is the caster's, not the enemy's) - see <see cref="BlastFactor"/>.
        /// </summary>
        private static readonly Dictionary<string, Check> Blasts = new Dictionary<string, Check>();

        /// <summary>The rolls of one cast on one target arrive within this (game) time: one hit.</summary>
        private static readonly TimeSpan OneHit = TimeSpan.FromSeconds(1.0);

        /// <summary>Pseudo-hits at "as estimated" a blast's learned share starts from.</summary>
        private const float BlastPriorHits = 3f;

        /// <summary>
        /// ★ v0.3.20 (0.3.19 feedback): what <paramref name="caster"/>'s kinetic blast really deals against the estimate, from
        /// its hits so far (critical hits and save halves left out), starting from three hits "as estimated", 0.67..1.6; 1
        /// with fewer than two hits and for everything that is not a kinetic blast. Some effects act inside the damage
        /// roll, out of the estimate's reach: DarkCodex's Elemental Scion raises a blast's dice a step (d6 to d8,
        /// KineticBlastDiceIncrease on RuleDealDamage), so Ember's "very strong single blast" lost to her bow. Kinetic
        /// blasts only: they hit one target once a cast, so a hit compares cleanly with its estimate.
        /// </summary>
        public static float BlastFactor(UnitEntityData caster, Kingmaker.UnitLogic.Abilities.Blueprints.BlueprintAbility blast)
        {
            if (Blasts.Count == 0 || caster == null || blast == null) return 1f;
            return Blasts.TryGetValue(caster.UniqueId + "|" + blast.AssetGuid, out var seen) ? ShareOf(seen) : 1f;
        }

        private static float ShareOf(Check seen)
        {
            if (seen.Hits < 2 || seen.Estimated <= 0f) return 1f;
            float perHit = seen.Estimated / seen.Hits;
            float share = (seen.Dealt + BlastPriorHits * perHit) / (seen.Estimated + BlastPriorHits * perHit);
            return Math.Max(0.67f, Math.Min(1.6f, share));
        }

        private static bool IsKineticBlast(Kingmaker.UnitLogic.Abilities.Blueprints.BlueprintAbility blueprint) =>
            Kingmaker.Blueprints.BlueprintExtenstions.GetComponent<Kingmaker.UnitLogic.Class.Kineticist.AbilityKineticist>(blueprint) != null;

        public static void Subscribe()
        {
            if (_instance != null) return;
            _instance = new DamageObserver();
            EventBus.Subscribe(_instance);
        }

        public static void Unsubscribe()
        {
            if (_instance == null) return;
            EventBus.Unsubscribe(_instance);
            _instance = null;
            Clear();
        }

        /// <summary>The party's fight ended: per-unit observations no longer apply.</summary>
        public static void Clear()
        {
            ByUnit.Clear();
            // ★ v0.3.18 (review): what one creature of a kind showed (a cultist under Resist Energy, a +4 contextual save)
            // was applied to every later creature of that blueprint for the whole session: kinds are learned per fight.
            ByKind.Clear();
            Reported.Clear();
            Checks.Clear();
            LastHit.Clear();
        }

        /// <summary>
        /// ★ v0.3.20 (playtest 1): at the end of a fight, each AI unit's damaging ability whose hits dealt more than a
        /// quarter more or less than the engine estimated (at least two hits; critical hits and halves for a passed save
        /// left out). An effect the estimate does not read - a mod's bonus to a blast (Elemental Scion), a class's
        /// own damage feature - shows here. Nothing is logged when the estimates were right.
        /// </summary>
        public static void ReportFight()
        {
            foreach (var check in Checks.Values)
            {
                if (check.Hits < 2 || check.Estimated <= 0f) continue;
                float dealt = check.Dealt / check.Hits, estimated = check.Estimated / check.Hits;
                if (Math.Abs(dealt - estimated) <= 0.25f * estimated) continue;
                Main.Log($"[DmgCheck] {check.Who}'s {check.What}: {check.Hits} hits dealt {dealt:0.#} a hit, estimated {estimated:0.#}");
            }
            // ★ v0.3.20: a blast whose learned share has become worth knowing, once each (when it first moves 10 %).
            foreach (var blast in Blasts)
            {
                float factor = ShareOf(blast.Value);
                if (Math.Abs(factor - 1f) < 0.1f || !ReportedBlasts.Add(blast.Key)) continue;
                Main.Log($"[DmgCheck] {blast.Value.Who}'s {blast.Value.What} deals x{factor:0.00} of its estimate ({blast.Value.Hits} hits so far): " +
                         "its estimates use this");
            }
            if (Blasts.Count > 128) { Blasts.Clear(); ReportedBlasts.Clear(); }
        }

        private static readonly HashSet<string> ReportedBlasts = new HashSet<string>();

        /// <summary>★ v0.3.20: one damage roll of an AI unit's ability (the rolls of one cast on one target are one hit).</summary>
        private static void NoteEstimate(RuleDealDamage evt, UnitEntityData attacker, UnitEntityData target)
        {
            var ability = evt.Reason.Ability;
            if (ability?.Blueprint == null || evt.HalfBecauseSavingThrow || (evt.AttackRoll?.IsCriticalConfirmed ?? false)) return;
            // A kinetic blade's blast is estimated with the blade's attack (CombatMath.BladeBlastPerHit).
            if (evt.DamageBundle?.Weapon?.Blueprint != null &&
                Kingmaker.Blueprints.BlueprintExtenstions.GetComponent<Kingmaker.UnitLogic.Class.Kineticist.WeaponKineticBlade>(evt.DamageBundle.Weapon.Blueprint) != null) return;
            if (!GameInterface.CustomBrainPatch.ShouldControlUnit(attacker)) return;
            string key = attacker.UniqueId + "|" + ability.Blueprint.AssetGuid;
            // ★ v0.3.20: the rolls of one cast (a composite blast's two damage actions, a spell's missiles) arrive over a
            // moment, not in one frame.
            var now = Kingmaker.Game.Instance.TimeController.GameTime;
            bool blast = IsKineticBlast(ability.Blueprint);
            Blasts.TryGetValue(key, out var session);
            string hitKey = key + "|" + target.UniqueId;
            if (Checks.TryGetValue(key, out var check) && LastHit.TryGetValue(hitKey, out var last) && now - last <= OneHit)
            {
                check.Dealt += evt.Result;
                if (blast && session != null) session.Dealt += evt.Result;
                return;
            }
            var profile = AbilityProfile.Get(ability);
            if (profile == null || profile.Damage.Count == 0) return;
            // The estimate before any learned share (it is what is being checked).
            float estimate = CombatMath.EstimateAbilityDamageRaw(ability, profile, target, null);
            if (estimate <= 0f) return;
            if (check == null)
            {
                if (Checks.Count > 256) return;
                Checks[key] = check = new Check { Who = attacker.CharacterName, What = ability.Name };
            }
            check.Hits++;
            check.Dealt += evt.Result;
            check.Estimated += estimate;
            if (LastHit.Count > 512) LastHit.Clear();
            LastHit[hitKey] = now;
            if (blast)
            {
                if (session == null) Blasts[key] = session = new Check { Who = attacker.CharacterName, What = ability.Name };
                session.Hits++;
                session.Dealt += evt.Result;
                session.Estimated += estimate;
            }
        }

        /// <summary>Key of a damage type as an ability describes it (ContextActionDealDamage.DamageType).</summary>
        public static string KeyOf(DamageTypeDescription type)
        {
            if (type == null) return null;
            switch (type.Type)
            {
                case DamageType.Energy: return "E:" + type.Energy;
                case DamageType.Physical: return null;    // weapon-like damage: the damage-reduction helper handles it
                default: return type.Type.ToString();
            }
        }

        private static string KeyOf(BaseDamage damage)
        {
            if (damage is EnergyDamage energy) return "E:" + energy.EnergyType;
            if (damage == null || damage.Type == DamageType.Physical) return null;
            return damage.Type.ToString();
        }

        public static Seen Lookup(UnitEntityData target, string key)
        {
            if (target == null || key == null) return null;
            if (ByUnit.TryGetValue(target.UniqueId + "|" + key, out var seen)) return seen;
            string kind = target.Blueprint?.AssetGuid.ToString();
            return kind != null && ByKind.TryGetValue(kind + "|" + key, out seen) ? seen : null;
        }

        public void OnEventAboutToTrigger(RuleDealDamage evt)
        {
        }

        public void OnEventDidTrigger(RuleDealDamage evt)
        {
            try
            {
                if (evt == null || evt.IsFake || evt.ResultList == null) return;
                var attacker = evt.Initiator;
                var target = evt.Target;
                if (attacker == null || target == null || !attacker.IsPlayerFaction || !attacker.IsEnemy(target)) return;
                float swarmArea = CombatMath.SwarmAreaFactor(evt.Reason.Ability, target,
                    evt.Reason.Context?.AssociatedBlueprint is Kingmaker.Blueprints.Area.BlueprintArea);
                foreach (var result in evt.ResultList)
                {
                    var damage = result.Source;
                    string key = KeyOf(damage);
                    if (key == null || result.RollAndBonusValue <= 0) continue;
                    // ★ v0.3.20 (playtest 1): a swarm's extra damage from areas (x1.5) belongs to that hit, not to the
                    // damage type: the estimate adds it for area damage only (CombatMath.SwarmAreaFactor).
                    float factor = CombatMath.DeclineFactor(damage.Decline.Value) * damage.Vulnerability / swarmArea;
                    var seen = new Seen
                    {
                        Immune = damage.Immune,
                        Flat = Math.Max(0, result.Reduction),
                        Factor = factor,
                        Source = evt.SourceAbility?.Name ?? damage.GetType().Name
                    };
                    ByUnit[target.UniqueId + "|" + key] = seen;
                    string kind = target.Blueprint?.AssetGuid.ToString();
                    if (kind != null) ByKind[kind + "|" + key] = seen;
                    if ((seen.Immune || seen.Flat > 0 || Math.Abs(seen.Factor - 1f) > 0.01f) && Reported.Add(target.UniqueId + "|" + key))
                        Main.Log($"[Damage] {target.CharacterName} takes {key} damage " +
                                 (seen.Immune ? "not at all (immune)" : $"x{seen.Factor:0.##} - {seen.Flat}") +
                                 $" (seen on {seen.Source}); spell estimates use this");
                }
                // ★ v0.3.20: after the observation above, so the estimate it is compared with already knows this hit's
                // resistance (a first fire blast on a fire-resistant enemy is not a weak blast).
                NoteEstimate(evt, attacker, target);
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Damage] observer: {ex.Message}");
            }
        }
    }
}
