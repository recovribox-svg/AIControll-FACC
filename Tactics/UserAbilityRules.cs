// ★ user-ability-blacklist (docs/TASK_USER_ABILITY_BLACKLIST.md): the game-independent rules of the "Abilities AI must not
// use" lists - the compiled lookup sets, "shared list OR the character's own list", and the bounded walk up an ability's
// variant parents. Unit-tested (tests/CompanionAI.Tests); the game side is UserAbilityPolicy.
using System;
using System.Collections.Generic;

namespace CompanionAI_Pathfinder.Tactics
{
    /// <summary>Which list blocks something (both can).</summary>
    [Flags]
    public enum BlockSource
    {
        None = 0,
        /// <summary>The list for every character AI Control controls.</summary>
        Shared = 1,
        /// <summary>The character's own list.</summary>
        Character = 2
    }

    /// <summary>The two sets that apply to one unit: the shared list and its own. A default value blocks nothing.</summary>
    public readonly struct BlockScope<TKey>
    {
        private readonly HashSet<TKey> _shared;
        private readonly HashSet<TKey> _own;

        public BlockScope(HashSet<TKey> shared, HashSet<TKey> own)
        {
            _shared = shared != null && shared.Count > 0 ? shared : null;
            _own = own != null && own.Count > 0 ? own : null;
        }

        /// <summary>Nothing is blocked for this unit: every check returns at once.</summary>
        public bool IsEmpty => _shared == null && _own == null;

        public BlockSource SourceOf(TKey key)
        {
            var source = BlockSource.None;
            if (_shared != null && _shared.Contains(key)) source |= BlockSource.Shared;
            if (_own != null && _own.Contains(key)) source |= BlockSource.Character;
            return source;
        }
    }

    /// <summary>
    /// The lists compiled into lookup sets (built when the settings load or change, never during a decision). Keys are the
    /// game's blueprint GUIDs in the mod, plain strings in the tests.
    /// </summary>
    public sealed class CompiledBlocklist<TKey>
    {
        private readonly HashSet<TKey> _shared;
        private readonly Dictionary<string, HashSet<TKey>> _byUnit;

        public static readonly CompiledBlocklist<TKey> Empty =
            new CompiledBlocklist<TKey>(new HashSet<TKey>(), new Dictionary<string, HashSet<TKey>>());

        private CompiledBlocklist(HashSet<TKey> shared, Dictionary<string, HashSet<TKey>> byUnit)
        {
            _shared = shared;
            _byUnit = byUnit;
        }

        /// <summary>No list names anything.</summary>
        public bool IsEmpty => _shared.Count == 0 && _byUnit.Count == 0;
        public int SharedCount => _shared.Count;

        /// <summary>
        /// Compiles normalised GUID texts (Settings.AbilityBlocklist.Normalize) with <paramref name="toKey"/>. A text the
        /// key function refuses (returns false) is left out; units with nothing listed get no set.
        /// </summary>
        public static CompiledBlocklist<TKey> Compile(IEnumerable<string> shared,
                                                      IEnumerable<KeyValuePair<string, IEnumerable<string>>> byUnit,
                                                      TryKey toKey)
        {
            var sharedSet = new HashSet<TKey>();
            if (shared != null)
                foreach (var text in shared)
                    if (text != null && toKey(text, out var key)) sharedSet.Add(key);
            var units = new Dictionary<string, HashSet<TKey>>(StringComparer.Ordinal);
            if (byUnit != null)
                foreach (var unit in byUnit)
                {
                    if (string.IsNullOrEmpty(unit.Key) || unit.Value == null) continue;
                    HashSet<TKey> set = null;
                    foreach (var text in unit.Value)
                    {
                        if (text == null || !toKey(text, out var key)) continue;
                        if (set == null && !units.TryGetValue(unit.Key, out set)) units[unit.Key] = set = new HashSet<TKey>();
                        set.Add(key);
                    }
                }
            return new CompiledBlocklist<TKey>(sharedSet, units);
        }

        public delegate bool TryKey(string text, out TKey key);

        /// <summary>The sets that apply to the unit with this id (its UniqueId).</summary>
        public BlockScope<TKey> For(string unitId)
        {
            HashSet<TKey> own = null;
            if (unitId != null && _byUnit.Count > 0) _byUnit.TryGetValue(unitId, out own);
            return new BlockScope<TKey>(_shared, own);
        }
    }

    /// <summary>How an ability blueprint is identified and what variant parent it belongs to (the game: BlueprintAbility).</summary>
    public interface IBlueprintTree<TBp, TKey> where TBp : class
    {
        TKey KeyOf(TBp blueprint);
        TBp ParentOf(TBp blueprint);
    }

    public static class UserAbilityRules
    {
        /// <summary>How far up variant parents are followed (the game nests them one level; a loop stops here).</summary>
        public const int MaxParentDepth = 4;

        /// <summary>The start of every refusal text this policy produces (the drivers read it: not a failed option).</summary>
        public const string RefusalPrefix = "blocked by the 'Abilities AI must not use' list";

        public static bool IsPolicyRefusal(string failure) =>
            failure != null && failure.StartsWith(RefusalPrefix, StringComparison.Ordinal);

        /// <summary>The lists that block this blueprint or one of its variant parents (an entry on a parent is "all variants").</summary>
        public static BlockSource MatchBlueprint<TBp, TKey, TTree>(BlockScope<TKey> scope, TBp blueprint, TTree tree)
            where TBp : class where TTree : struct, IBlueprintTree<TBp, TKey>
        {
            if (scope.IsEmpty) return BlockSource.None;
            var source = BlockSource.None;
            var node = blueprint;
            for (int depth = 0; node != null && depth <= MaxParentDepth; depth++)
            {
                source |= scope.SourceOf(tree.KeyOf(node));
                var parent = tree.ParentOf(node);
                if (ReferenceEquals(parent, node)) break;
                node = parent;
            }
            return source;
        }

        /// <summary>
        /// The lists that block an ability AI Control would use: its blueprint and variant parents; the ability it was
        /// converted from (<paramref name="convertedFrom"/>) only when that one really has it as a variant
        /// (<paramref name="convertedFromHasIt"/>: a variant expanded from its parent, a spell a shadow spell copies) - a Cure
        /// converted from a Bless slot is not Bless; for a held touch charge, the spell it came from
        /// (<paramref name="touchOrigin"/>).
        /// </summary>
        public static BlockSource MatchAbility<TBp, TKey, TTree>(BlockScope<TKey> scope, TBp blueprint, TBp convertedFrom,
                                                                 bool convertedFromHasIt, TBp touchOrigin, TTree tree)
            where TBp : class where TTree : struct, IBlueprintTree<TBp, TKey>
        {
            if (scope.IsEmpty || blueprint == null) return BlockSource.None;
            var source = MatchBlueprint<TBp, TKey, TTree>(scope, blueprint, tree);
            if (convertedFrom != null && convertedFromHasIt && !ReferenceEquals(convertedFrom, blueprint))
                source |= MatchBlueprint<TBp, TKey, TTree>(scope, convertedFrom, tree);
            if (touchOrigin != null && !ReferenceEquals(touchOrigin, blueprint))
                source |= MatchBlueprint<TBp, TKey, TTree>(scope, touchOrigin, tree);
            return source;
        }

        /// <summary>Whether a refused option counts as one that failed (remembered for the turn, learned from). A refusal
        /// by the lists does not: the option is simply not one AI Control may take, and lifting the block restores it.</summary>
        public static bool CountsAsFailure(string failure) => !IsPolicyRefusal(failure);
    }
}
