using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Kingmaker.Blueprints;
using Kingmaker.EntitySystem;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.UnitLogic.Mechanics.Components;
using UnityEngine;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class CombatMath
    {
        // The native facts manager exposes a List, not an activation generation. Compare identities and Active values:
        // equal counts do not mean equal active sources. Nothing is allocated on an unchanged snapshot.
        private sealed class SwarmFacts
        {
            internal EntityFact[] Facts = Array.Empty<EntityFact>();
            internal bool[] Active = Array.Empty<bool>();
            internal BlueprintComponent[][] Components = Array.Empty<BlueprintComponent[]>();
            internal readonly List<AfflictionSource> Rounds = new List<AfflictionSource>();
            internal int Count;
            internal bool Body;

            internal bool Update(List<EntityFact> facts)
            {
                bool same = facts.Count == Count;
                for (int i = 0; same && i < facts.Count; i++)
                    same = ReferenceEquals(facts[i], Facts[i]) && (facts[i]?.Active == true) == Active[i] &&
                           ReferenceEquals(facts[i]?.Blueprint?.ComponentsArray, Components[i]);
                if (same) return false;
                if (Facts.Length < facts.Count)
                {
                    int capacity = Math.Max(facts.Count, Math.Max(4, Facts.Length * 2));
                    Array.Resize(ref Facts, capacity); Array.Resize(ref Active, capacity); Array.Resize(ref Components, capacity);
                }
                else if (Count > facts.Count)
                {
                    Array.Clear(Facts, facts.Count, Count - facts.Count);
                    Array.Clear(Components, facts.Count, Count - facts.Count);
                }
                Count = facts.Count;
                Rounds.Clear(); Body = false;
                for (int i = 0; i < facts.Count; i++)
                {
                    var fact = facts[i]; var components = fact?.Blueprint?.ComponentsArray;
                    Facts[i] = fact; Active[i] = fact?.Active == true; Components[i] = components;
                    if (!Active[i] || components == null) continue;
                    for (int c = 0; c < components.Length; c++)
                    {
                        if (components[c] is SwarmDamageResistance) Body = true;
                        if (components[c] is AddFactContextActions round && HasSwarmAttack(round.NewRound, 0))
                            Rounds.Add(new AfflictionSource { Actions = round.NewRound, Fact = fact });
                    }
                }
                return true;
            }
        }

        private sealed class SwarmState
        {
            internal readonly SwarmFacts Facts = new SwarmFacts();
            internal readonly SwarmSpace Space = new SwarmSpace();
            internal SwarmCast KnownCast;
            internal int Frame = -1, Decision = -1;
            internal bool SpaceRead, DcRead;
        }
        private static readonly ConditionalWeakTable<UnitEntityData, SwarmState> s_SwarmStates =
            new ConditionalWeakTable<UnitEntityData, SwarmState>();
        private static int s_SwarmDecision;

        private static SwarmState SwarmStateOf(UnitEntityData unit)
        {
            var state = s_SwarmStates.GetValue(unit, _ => new SwarmState());
            int frame = Time.frameCount;
            if (state.Frame == frame && state.Decision == s_SwarmDecision) return state;
            state.Frame = frame; state.Decision = s_SwarmDecision;
            state.SpaceRead = state.DcRead = false;
            state.Facts.Update(unit.Facts.List);
            return state;
        }

        /// <summary>
        /// ★ summon control: this creature is a swarm by its own active facts - a swarm body (SwarmDamageResistance) or a
        /// swarm round attack (ContextActionSwarmAttack on a new round). The same read as the swarm danger's; no name list.
        /// </summary>
        internal static bool IsSwarmActor(UnitEntityData unit)
        {
            if (unit?.Facts?.List == null) return false;
            var facts = SwarmStateOf(unit).Facts;
            return facts.Body || facts.Rounds.Count > 0;
        }

        private static List<AfflictionSource> SwarmSourcesOf(UnitEntityData unit)
        {
            var state = SwarmStateOf(unit);
            if (!state.DcRead)
            {
                foreach (var source in state.Facts.Rounds) source.Dc = FactDc(unit, source.Fact);
                state.DcRead = true;
            }
            return state.Facts.Rounds;
        }

        private static void NewSwarmDecision()
        {
            unchecked { s_SwarmDecision++; }
            s_SwarmFrame = -1;
        }
    }
}
