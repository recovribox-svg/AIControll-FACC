using ProfileAudit;
using Xunit;

namespace CompanionAI.Tests
{
    public class FusionAuditOptionsTests
    {
        [Fact]
        public void CoverageReportRemainsSeparateFromInputsAndOrdinaryReport()
        {
            Assert.True(AuditOptions.TryParse(new[] { "--fusion-coverage", "fusion.json", "spells", "classes", "audit.txt" }, out var options, out _));
            Assert.Equal("fusion.json", options.FusionCoverageOutput);
            Assert.Equal(new[] { "spells", "classes" }, options.Roots);
            Assert.Equal("audit.txt", options.Output);
        }

        [Fact]
        public void CoexistsWithIntegratedSummonInventoryOptions()
        {
            Assert.True(AuditOptions.TryParse(new[] { "--fusion-coverage", "fusion.json", "--supplement", "stock", "--summon-kit-report", "kits.txt", "--summon-unit-list", "units.txt", "spells", "audit.txt" }, out var options, out _));
            Assert.Equal(new[] { "stock" }, options.Supplements);
            Assert.Equal("kits.txt", options.SummonKitReport);
            Assert.Equal(new[] { "units.txt" }, options.SummonUnitLists);
            Assert.Equal(new[] { "spells" }, options.Roots);
        }

        [Theory]
        [InlineData("--fusion-coverage", "audit.txt")]
        [InlineData("--fusion-coverage", "--blocklist-checks", "spells", "audit.txt")]
        [InlineData("--fusion-coverage", "same.txt", "spells", "./same.txt")]
        [InlineData("--fusion-coverage", "one.json", "--fusion-coverage", "two.json", "spells", "audit.txt")]
        [InlineData("--optional-provider", "provider.dll", "--fusion-coverage", "fusion.json", "audit.txt")]
        [InlineData("--fusion-coverage", "same.txt", "--summon-kit-report", "./same.txt", "spells", "audit.txt")]
        public void InvalidRequestsCannotOverwriteOrPretendToAuditMissingGraphs(params string[] args)
        {
            Assert.False(AuditOptions.TryParse(args, out _, out var error));
            Assert.False(string.IsNullOrEmpty(error));
        }
    }
}
