// ★ v0.3.30 competence3 (author's play-test note, turn-based fight 3 of the competence2 log): the last enemy, a Cultist
// Conjurer held prone in Orvath's Grease and Entangle, turned invisible. The game then hid it from the party
// (UnitStealthController: an invisible unit enters stealth unless an enemy of it sees invisibility with line of sight or
// reaches it by blindsense), every party turn ended "no reachable enemies" for about twenty rounds, and Orvath's Faerie Fire
// was never cast - although its buff makes the creatures in it immune to the Invisible condition (AddConditionImmunity
// Invisible), which ends that stealth on the next stealth tick (ShouldBeInStealth needs the condition).
//
// Where the party saw it vanish: the game raises IUnitStealthHandler when a unit enters stealth; that spot is kept while the
// party had the unit in its memory then (UnitGroupMemory: a unit it knew of). The engine aims a revealing area there
// (TacticalEngine.Hidden.cs), once per vanishing: an enemy that has moved since is not found there, and the spot is not
// tried again until the enemy shows and vanishes anew. The engine never reads where a hidden enemy really is.
using System;
using System.Collections.Generic;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.PubSubSystem;
using UnityEngine;

namespace CompanionAI_Pathfinder.Tactics
{
    public sealed class HiddenEnemies : IUnitStealthHandler
    {
        private sealed class Vanish
        {
            public Vector3 At;
            public double When;
            public bool Tried;
        }

        private static HiddenEnemies _instance;
        /// <summary>Enemy unique id → where and when the party last saw it vanish into stealth.</summary>
        private static readonly Dictionary<string, Vanish> Vanished = new Dictionary<string, Vanish>();

        public static void Subscribe()
        {
            if (_instance != null) return;
            _instance = new HiddenEnemies();
            EventBus.Subscribe(_instance);
        }

        public static void Unsubscribe()
        {
            if (_instance == null) return;
            EventBus.Unsubscribe(_instance);
            _instance = null;
        }

        /// <summary>Called by CombatMemory.ResetCombat.</summary>
        public static void Reset() => Vanished.Clear();

        private static double GameSeconds => Game.Instance?.TimeController?.GameTime.TotalSeconds ?? 0d;

        public void HandleUnitSwitchStealthCondition(UnitEntityData unit, bool inStealth)
        {
            try
            {
                if (!inStealth || unit == null || !unit.IsPlayersEnemy) return;
                // Only where the party knew of it: an enemy that was never seen leaves nothing to search.
                bool known = false;
                foreach (var member in Game.Instance.Player.Party)
                {
                    if (member?.Group?.Memory != null && member.Group.Memory.Contains(unit)) { known = true; break; }
                }
                if (!known) return;
                Vanished[unit.UniqueId] = new Vanish { At = unit.Position, When = GameSeconds };
                Main.Verbose($"[Hidden] {unit.CharacterName} vanished from the party's sight at ({unit.Position.x:F1}, {unit.Position.z:F1})");
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Hidden] {unit?.CharacterName}: {ex.Message}");
            }
        }

        /// <summary>Where the party saw <paramref name="enemy"/> vanish, unless a revealing area was already aimed there.</summary>
        public static bool UntriedSpot(UnitEntityData enemy, out Vector3 at, out double secondsAgo)
        {
            at = default;
            secondsAgo = 0d;
            if (enemy == null || !Vanished.TryGetValue(enemy.UniqueId, out var vanish) || vanish.Tried) return false;
            at = vanish.At;
            secondsAgo = Math.Max(0d, GameSeconds - vanish.When);
            return true;
        }

        /// <summary>A revealing area was aimed at where these enemies vanished: not aimed there again.</summary>
        public static void NoteTried(IEnumerable<UnitEntityData> enemies)
        {
            if (enemies == null) return;
            foreach (var enemy in enemies)
                if (enemy != null && Vanished.TryGetValue(enemy.UniqueId, out var vanish)) vanish.Tried = true;
        }
    }
}
