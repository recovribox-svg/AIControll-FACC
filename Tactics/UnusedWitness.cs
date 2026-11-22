using System;
using System.Text;
using CompanionAI_Pathfinder.GameInterface;
using Kingmaker.Blueprints;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;

namespace CompanionAI_Pathfinder.Tactics
{
    internal enum UnusedReason : byte
    {
        NotScored, Unavailable, CombatPolicy, ActionUnavailable, ScoreNotNeeded, Blocklist,
        SummonPolicy, Unread, CoupDelivery, FusionWithheld, PureMage, ConsumablePolicy, NoTarget,
        NoApproach, ItemOnSummon, FriendlyFire, NoGain, ItemBar, SlotCopy
    }

    /// <summary>Bounded fight evidence. Stores identities and numbers, with no live rules or queries at report time.</summary>
    internal static class UnusedWitness
    {
        internal const int MaxGroups = 16, MaxEntries = 32;
        private static readonly Group[] Groups = new Group[MaxGroups];
        private static int count;

        internal sealed class Group
        {
            internal object Key;
            internal UnitEntityData Unit;
            private readonly Entry[] entries = new Entry[MaxEntries];
            private int used;
            private bool full;

            private struct Entry
            {
                public BlueprintAbility Ability;
                public BlueprintItemIdentity Identity;
                public float Best;
                public int Issued;
                public UnusedReason Reason;
            }

            private readonly struct BlueprintItemIdentity
            {
                public readonly SimpleBlueprint Item, Book;
                public readonly bool ForOther;

                public BlueprintItemIdentity(AbilityData ability)
                {
                    Item = ability.SourceItem?.Blueprint;
                    Book = ability.Spellbook?.Blueprint;
                    ForOther = ability.PotionForOther;
                }

                public bool Matches(BlueprintItemIdentity other) =>
                    ReferenceEquals(Item, other.Item) && ReferenceEquals(Book, other.Book) && ForOther == other.ForOther;
            }

            internal int Observe(AbilityData ability, bool add = true)
            {
                if (ability?.Blueprint == null) return -1;
                var identity = new BlueprintItemIdentity(ability);
                for (int i = 0; i < used; i++)
                    if (ReferenceEquals(entries[i].Ability, ability.Blueprint) && entries[i].Identity.Matches(identity))
                        return i;
                if (!add) return -1;
                if (used == MaxEntries) { full = true; return -1; }
                entries[used].Ability = ability.Blueprint;
                entries[used].Identity = identity;
                return used++;
            }

            internal void Record(int index, UnusedReason reason, float value = 0f)
            {
                if (index < 0) return;
                ref var entry = ref entries[index];
                if (value > entry.Best) entry.Best = value;
                entry.Reason = reason;
            }

            internal void Score(int index, float value)
            {
                if (index >= 0 && value > entries[index].Best) entries[index].Best = value;
            }

            internal void NoteIssued(int index)
            {
                if (index >= 0) entries[index].Issued++;
            }

            internal void Report()
            {
                var line = new StringBuilder();
                for (int i = 0; i < used; i++)
                {
                    ref var entry = ref entries[i];
                    if (entry.Issued != 0) continue;
                    if (line.Length > 0) line.Append("; ");
                    if (entry.Identity.Item != null) line.Append("Item:").Append(entry.Identity.Item.name).Append('/');
                    line.Append(entry.Ability.Name).Append(" best ").Append(entry.Best.ToString("0.#"))
                        .Append(" (").Append(Describe(entry.Reason, entry.Best)).Append(')');
                }
                if (line.Length > 0 || full)
                    Main.Log($"[Unused] {Unit.CharacterName}: {line}{(full ? " [32-entry cap]" : "")}");
            }

            internal void Clear()
            {
                Array.Clear(entries, 0, used);
                used = 0;
                full = false;
                Key = null;
                Unit = null;
            }
        }

        private static object KeyFor(UnitEntityData unit) =>
            SummonControl.IsOrdinarySummon(unit) && unit.Blueprint != null ? (object)unit.Blueprint : unit;

        internal static Group For(UnitEntityData unit, bool add = true)
        {
            if (unit == null) return null;
            object key = KeyFor(unit);
            for (int i = 0; i < count; i++)
                if (ReferenceEquals(Groups[i].Key, key)) return Groups[i];
            if (!add || count == MaxGroups) return null;
            var group = Groups[count] ?? (Groups[count] = new Group());
            count++;
            group.Key = key;
            group.Unit = unit;
            return group;
        }

        internal static void Issued(UnitEntityData unit, AbilityData ability)
        {
            var group = For(unit, add: false);
            if (group != null) group.NoteIssued(group.Observe(ability, add: false));
        }

        internal static void Report()
        {
            try
            {
                for (int i = 0; i < count; i++)
                {
                    try { Groups[i].Report(); }
                    catch (Exception ex) { Main.Verbose($"[Unused] report unavailable: {ex.GetType().Name}"); }
                }
            }
            finally { Reset(); }
        }

        internal static void Reset()
        {
            for (int i = 0; i < count; i++) Groups[i].Clear();
            count = 0;
        }

        private static string Describe(UnusedReason reason, float best)
        {
            switch (reason)
            {
                case UnusedReason.Unavailable: return "unavailable";
                case UnusedReason.CombatPolicy: return "combat policy";
                case UnusedReason.ActionUnavailable: return "action unavailable";
                case UnusedReason.ScoreNotNeeded: return "no score needed";
                case UnusedReason.Blocklist: return "user blocklist";
                case UnusedReason.SummonPolicy: return "summon policy";
                case UnusedReason.Unread: return "effect unread";
                case UnusedReason.CoupDelivery: return "no native Coup de Grace delivery";
                case UnusedReason.FusionWithheld: return "fusion withheld";
                case UnusedReason.PureMage: return "Pure Mage";
                case UnusedReason.ConsumablePolicy: return "consumable setting or delivery chance";
                case UnusedReason.NoTarget: return "no legal aim";
                case UnusedReason.NoApproach: return "out of range without an affordable approach";
                case UnusedReason.ItemOnSummon: return "item on a summon";
                case UnusedReason.FriendlyFire: return "friendly fire";
                case UnusedReason.NoGain: return "no positive effect";
                case UnusedReason.ItemBar: return "below item bar";
                case UnusedReason.SlotCopy: return "slot copy available";
                default: return best > 0f ? "scored but never issued" : "no score recorded";
            }
        }
    }
}
