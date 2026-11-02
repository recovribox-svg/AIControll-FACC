// ★ user-ability-blacklist: the lists as saved (Settings/AbilityBlocklist) and the matching rules the mod uses
// (Tactics/UserAbilityRules: the same code the game side calls, with plain strings for blueprint GUIDs).
using System.Collections.Generic;
using CompanionAI_Pathfinder.Settings;
using CompanionAI_Pathfinder.Tactics;
using Newtonsoft.Json;
using Xunit;

namespace CompanionAI.Tests
{
    public class UserAbilityBlocklistTests
    {
        private const string Bless = "90e59f4a4ada87243b7b3535a06d0638";
        private const string Cure = "5590652e1c2225c4ca30c4a699ab3649";
        private const string Command = "feb70aab86bc17f4bb1ed2b23a1ea8eb";
        private const string Halt = "aa3f6a32e5ff0e64897dc07a3df2e2ef";
        private const string Flee = "bc5f03d8f42bc1b48b46f6b0f8ae7b8c";
        private const string Delivery = "e5ab2a93a3fdf0d4ba0afa2193de08b5";
        private const string PowerAttack = "a7b339e4f6ff93a4697df5d7a87ff619";

        // --- Persistence shape ---------------------------------------------------------------------------------------

        /// <summary>The two list properties as ModSettings / CharacterSettings declare them (initialised to empty).</summary>
        private sealed class SettingsShape
        {
            public bool SomeOldSetting { get; set; } = true;
            public Dictionary<string, CharacterShape> CharacterSettings { get; set; } = new Dictionary<string, CharacterShape>();
            public List<AbilityBlockEntry> SharedBlockedAbilities { get; set; } = new List<AbilityBlockEntry>();
        }

        private sealed class CharacterShape
        {
            public string CharacterName { get; set; } = "";
            public List<AbilityBlockEntry> BlockedAbilities { get; set; } = new List<AbilityBlockEntry>();
        }

        [Fact]
        public void OldSettingsFileLoadsWithEmptyLists()
        {
            var old = JsonConvert.DeserializeObject<SettingsShape>(
                "{\"SomeOldSetting\":false,\"CharacterSettings\":{\"u1\":{\"CharacterName\":\"Seelah\"}}}");
            Assert.False(old.SomeOldSetting);
            Assert.Empty(AbilityBlocklist.Normalize(old.SharedBlockedAbilities));
            Assert.Empty(AbilityBlocklist.Normalize(old.CharacterSettings["u1"].BlockedAbilities));
        }

        [Fact]
        public void ExplicitNullListsBecomeEmpty()
        {
            var file = JsonConvert.DeserializeObject<SettingsShape>(
                "{\"SharedBlockedAbilities\":null,\"CharacterSettings\":{\"u1\":{\"BlockedAbilities\":null}}}");
            Assert.Null(file.SharedBlockedAbilities);
            Assert.Empty(AbilityBlocklist.Normalize(file.SharedBlockedAbilities));
            Assert.Empty(AbilityBlocklist.Normalize(file.CharacterSettings["u1"].BlockedAbilities));
        }

        [Fact]
        public void SaveAndReloadKeepsSharedAndCharacterEntries()
        {
            var settings = new SettingsShape();
            settings.CharacterSettings["u1"] = new CharacterShape { CharacterName = "Seelah" };
            Assert.True(AbilityBlocklist.Add(settings.SharedBlockedAbilities, Bless, "Bless", AbilityBlockEntry.AbilityKind));
            Assert.True(AbilityBlocklist.Add(settings.CharacterSettings["u1"].BlockedAbilities, PowerAttack, "Power Attack", AbilityBlockEntry.ToggleKind));
            string json = JsonConvert.SerializeObject(settings, Formatting.Indented, new JsonSerializerSettings());
            var back = JsonConvert.DeserializeObject<SettingsShape>(json);
            var shared = AbilityBlocklist.Normalize(back.SharedBlockedAbilities);
            var own = AbilityBlocklist.Normalize(back.CharacterSettings["u1"].BlockedAbilities);
            Assert.Single(shared);
            Assert.Equal(Bless, shared[0].Guid);
            Assert.Equal("Bless", shared[0].Name);
            Assert.Equal(AbilityBlockEntry.AbilityKind, shared[0].Kind);
            Assert.Single(own);
            Assert.Equal(PowerAttack, own[0].Guid);
            Assert.Equal(AbilityBlockEntry.ToggleKind, own[0].Kind);
        }

        [Theory]
        [InlineData("90e59f4a4ada87243b7b3535a06d0638", Bless)]
        [InlineData("90E59F4A4ADA87243B7B3535A06D0638", Bless)]
        [InlineData("90e59f4a-4ada-8724-3b7b-3535a06d0638", Bless)]
        [InlineData(" {90e59f4a-4ada-8724-3b7b-3535a06d0638} ", Bless)]
        [InlineData("not a guid", null)]
        [InlineData("90e59f4a4ada87243b7b3535a06d063", null)]
        [InlineData("00000000000000000000000000000000", null)]
        [InlineData("", null)]
        [InlineData(null, null)]
        public void GuidsAreNormalisedToTheGameForm(string text, string expected)
        {
            Assert.Equal(expected, AbilityBlocklist.NormalizeGuid(text));
        }

        [Fact]
        public void MalformedEntriesAreDroppedOneByOneAndTheRestKept()
        {
            var rejected = new List<string>();
            var list = AbilityBlocklist.Normalize(new List<AbilityBlockEntry>
            {
                null,
                new AbilityBlockEntry { Guid = "garbage", Name = "Broken" },
                new AbilityBlockEntry { Guid = Bless.ToUpperInvariant(), Name = "  Bless ", Kind = "toggle" },
                new AbilityBlockEntry { Guid = Cure, Name = "Gone after a respec", Kind = "SomethingNew" },
                new AbilityBlockEntry { Guid = "90e59f4a-4ada-8724-3b7b-3535a06d0638", Name = "Bless (copy)" },
            }, rejected);
            Assert.Equal(2, rejected.Count);
            Assert.Equal(2, list.Count);
            Assert.Equal(Bless, list[0].Guid);
            Assert.Equal("Bless", list[0].Name);
            Assert.Equal(AbilityBlockEntry.ToggleKind, list[0].Kind);
            // An unknown ability (respec, mod removed) stays, with its stored name; an unknown kind becomes Ability.
            Assert.Equal(Cure, list[1].Guid);
            Assert.Equal("Gone after a respec", list[1].Name);
            Assert.Equal(AbilityBlockEntry.AbilityKind, list[1].Kind);
        }

        [Fact]
        public void DuplicateWithoutNameTakesTheLaterName()
        {
            var list = AbilityBlocklist.Normalize(new List<AbilityBlockEntry>
            {
                new AbilityBlockEntry { Guid = Bless },
                new AbilityBlockEntry { Guid = Bless, Name = "Bless" },
            });
            Assert.Single(list);
            Assert.Equal("Bless", list[0].Name);
        }

        [Fact]
        public void AddAndRemoveChangeTheListOnlyWhenNeeded()
        {
            var list = new List<AbilityBlockEntry>();
            Assert.True(AbilityBlocklist.Add(list, Bless, "Bless", AbilityBlockEntry.AbilityKind));
            Assert.False(AbilityBlocklist.Add(list, Bless.ToUpperInvariant(), "Bless", AbilityBlockEntry.AbilityKind));
            Assert.False(AbilityBlocklist.Add(list, "nope", "Nope", AbilityBlockEntry.AbilityKind));
            Assert.True(AbilityBlocklist.Contains(list, "90e59f4a-4ada-8724-3b7b-3535a06d0638"));
            Assert.False(AbilityBlocklist.Remove(list, Cure));
            Assert.True(AbilityBlocklist.Remove(list, Bless));
            Assert.Empty(list);
        }

        // --- Compiled lists: shared OR own ----------------------------------------------------------------------------

        private static bool TryKey(string text, out string key)
        {
            key = AbilityBlocklist.NormalizeGuid(text);
            return key != null;
        }

        private static CompiledBlocklist<string> Compile(IEnumerable<string> shared, params (string Unit, string[] Guids)[] units)
        {
            var byUnit = new List<KeyValuePair<string, IEnumerable<string>>>();
            foreach (var u in units) byUnit.Add(new KeyValuePair<string, IEnumerable<string>>(u.Unit, u.Guids));
            return CompiledBlocklist<string>.Compile(shared, byUnit, TryKey);
        }

        [Fact]
        public void SharedOrOwnListBlocks()
        {
            var lists = Compile(new[] { Bless }, ("seelah", new[] { Cure }));
            Assert.Equal(BlockSource.Shared, lists.For("seelah").SourceOf(Bless));
            Assert.Equal(BlockSource.Character, lists.For("seelah").SourceOf(Cure));
            Assert.Equal(BlockSource.Shared, lists.For("lann").SourceOf(Bless));
            Assert.Equal(BlockSource.None, lists.For("lann").SourceOf(Cure));
            Assert.Equal(BlockSource.None, lists.For("seelah").SourceOf(Command));
        }

        [Fact]
        public void BothListsCanNameTheSameAbility()
        {
            var lists = Compile(new[] { Bless }, ("seelah", new[] { Bless }));
            Assert.Equal(BlockSource.Shared | BlockSource.Character, lists.For("seelah").SourceOf(Bless));
        }

        [Fact]
        public void RemovingTheOwnEntryDoesNotLiftTheSharedOne()
        {
            var own = new List<AbilityBlockEntry>();
            var shared = new List<AbilityBlockEntry>();
            AbilityBlocklist.Add(own, Bless, "Bless", AbilityBlockEntry.AbilityKind);
            AbilityBlocklist.Add(shared, Bless, "Bless", AbilityBlockEntry.AbilityKind);
            AbilityBlocklist.Remove(own, Bless);
            var lists = Compile(Guids(shared), ("seelah", Guids(own)));
            Assert.Equal(BlockSource.Shared, lists.For("seelah").SourceOf(Bless));
            // Unblocking at the source restores it.
            AbilityBlocklist.Remove(shared, Bless);
            Assert.Equal(BlockSource.None, Compile(Guids(shared), ("seelah", Guids(own))).For("seelah").SourceOf(Bless));
        }

        private static string[] Guids(List<AbilityBlockEntry> list) => list.ConvertAll(e => e.Guid).ToArray();

        [Fact]
        public void EmptyListsTakeTheFastPath()
        {
            Assert.True(CompiledBlocklist<string>.Empty.IsEmpty);
            Assert.True(CompiledBlocklist<string>.Empty.For("anyone").IsEmpty);
            Assert.True(default(BlockScope<string>).IsEmpty);
            var lists = Compile(new string[0], ("seelah", new string[0]));
            Assert.True(lists.IsEmpty);
            Assert.True(lists.For("seelah").IsEmpty);
            // Another unit's list does not make this unit's checks do anything.
            Assert.True(Compile(new string[0], ("seelah", new[] { Bless })).For("lann").IsEmpty);
        }

        [Fact]
        public void MalformedTextsAreLeftOutWhenCompiling()
        {
            var lists = Compile(new[] { "oops", null, Bless });
            Assert.Equal(1, lists.SharedCount);
            Assert.Equal(BlockSource.Shared, lists.For("x").SourceOf(Bless));
        }

        // --- Ability identity -----------------------------------------------------------------------------------------

        private sealed class Bp
        {
            public string Guid;
            public Bp Parent;
        }

        private struct Tree : IBlueprintTree<Bp, string>
        {
            public string KeyOf(Bp blueprint) => blueprint.Guid;
            public Bp ParentOf(Bp blueprint) => blueprint.Parent;
        }

        private static BlockSource Match(BlockScope<string> scope, Bp blueprint, Bp convertedFrom = null, bool convertedFromHasIt = false,
                                         Bp touchOrigin = null) =>
            UserAbilityRules.MatchAbility<Bp, string, Tree>(scope, blueprint, convertedFrom, convertedFromHasIt, touchOrigin, default);

        private static readonly Bp CommandBp = new Bp { Guid = Command };
        private static readonly Bp HaltBp = new Bp { Guid = Halt, Parent = CommandBp };
        private static readonly Bp FleeBp = new Bp { Guid = Flee, Parent = CommandBp };

        [Fact]
        public void AnEntryOnTheParentBlocksAllVariants()
        {
            var scope = Compile(new[] { Command }).For("x");
            Assert.Equal(BlockSource.Shared, Match(scope, HaltBp));
            Assert.Equal(BlockSource.Shared, Match(scope, FleeBp));
        }

        [Fact]
        public void AnEntryOnOneVariantBlocksOnlyThatVariant()
        {
            var scope = Compile(new[] { Halt }).For("x");
            Assert.Equal(BlockSource.Shared, Match(scope, HaltBp));
            Assert.Equal(BlockSource.None, Match(scope, FleeBp));
        }

        [Fact]
        public void RuntimeVariantParentCountsOnlyWhenItHasTheVariant()
        {
            // A variant whose blueprint has no Parent link, expanded at run time from its parent (ConvertedFrom).
            var child = new Bp { Guid = Halt };
            var scope = Compile(new[] { Command }).For("x");
            Assert.Equal(BlockSource.Shared, Match(scope, child, CommandBp, convertedFromHasIt: true));
            Assert.Equal(BlockSource.None, Match(scope, child, CommandBp, convertedFromHasIt: false));
        }

        [Fact]
        public void CureConvertedFromABlessSlotDoesNotInheritABlessBan()
        {
            var bless = new Bp { Guid = Bless };
            var cure = new Bp { Guid = Cure };
            var scope = Compile(new[] { Bless }).For("x");
            Assert.Equal(BlockSource.None, Match(scope, cure, bless, convertedFromHasIt: false));
            Assert.Equal(BlockSource.Shared, Match(Compile(new[] { Cure }).For("x"), cure, bless, convertedFromHasIt: false));
        }

        [Fact]
        public void HeldTouchChargeIsBlockedThroughItsSpell()
        {
            var cure = new Bp { Guid = Cure };
            var delivery = new Bp { Guid = Delivery };
            Assert.Equal(BlockSource.Character,
                         Match(Compile(new string[0], ("x", new[] { Cure })).For("x"), delivery, touchOrigin: cure));
            Assert.Equal(BlockSource.None, Match(Compile(new[] { Bless }).For("x"), delivery, touchOrigin: cure));
            // The spell's own family too (a held variant).
            Assert.Equal(BlockSource.Shared, Match(Compile(new[] { Command }).For("x"), delivery, touchOrigin: HaltBp));
        }

        [Fact]
        public void CopiesFromBooksMetamagicAndItemsShareTheBlueprint()
        {
            // The game gives the spell from a second spellbook, with metamagic, or from a scroll the same blueprint.
            var scope = Compile(new[] { Cure }).For("x");
            var cure = new Bp { Guid = Cure };
            for (int copy = 0; copy < 3; copy++) Assert.Equal(BlockSource.Shared, Match(scope, cure));
            // A different blueprint with a similar name is a different ability.
            Assert.Equal(BlockSource.None, Match(scope, new Bp { Guid = Bless }));
        }

        [Fact]
        public void ParentLoopsStop()
        {
            var a = new Bp { Guid = Halt };
            var b = new Bp { Guid = Flee, Parent = a };
            a.Parent = b;
            Assert.Equal(BlockSource.None, Match(Compile(new[] { Bless }).For("x"), a));
            var self = new Bp { Guid = Halt };
            self.Parent = self;
            Assert.Equal(BlockSource.None, Match(Compile(new[] { Bless }).For("x"), self));
            Assert.Equal(BlockSource.Shared, Match(Compile(new[] { Flee }).For("x"), a));
        }

        [Fact]
        public void ParentsBeyondTheDepthLimitAreNotFollowed()
        {
            var root = new Bp { Guid = Command };
            var node = root;
            for (int i = 0; i < UserAbilityRules.MaxParentDepth + 1; i++) node = new Bp { Guid = $"{i:x32}", Parent = node };
            Assert.Equal(BlockSource.None, Match(Compile(new[] { Command }).For("x"), node));
        }

        // --- Refusals ------------------------------------------------------------------------------------------------

        [Fact]
        public void PolicyRefusalsAreNotFailures()
        {
            string refusal = UserAbilityRules.RefusalPrefix + ": Bless";
            Assert.True(UserAbilityRules.IsPolicyRefusal(refusal));
            Assert.False(UserAbilityRules.CountsAsFailure(refusal));
            Assert.True(UserAbilityRules.CountsAsFailure("path ends out of reach (1.2m short)"));
            Assert.True(UserAbilityRules.CountsAsFailure(null));
        }
    }
}
