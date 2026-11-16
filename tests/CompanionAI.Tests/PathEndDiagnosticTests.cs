using System;
using CompanionAI_Pathfinder.Core;
using Xunit;

namespace CompanionAI.Tests
{
    public class PathEndDiagnosticTests
    {
        private static PathEndDiagnosticLog.Snapshot Sample(float endX = 95f) => new PathEndDiagnosticLog.Snapshot
        {
            EndX = endX, EndY = 41f, EndZ = 47.1f, GoalX = 93.6f, GoalY = 45.1f, GoalZ = 48.9f,
            Radius = 16.24f, Distance = 2.22f, PathLength = 14f, NeedSight = true, Reason = 1
        };

        [Fact]
        public void FirstWitnessIsKeptAndIdenticalPreviewsStayQuiet()
        {
            var log = new PathEndDiagnosticLog();
            Assert.True(log.Observe(1, "caster", Sample()));
            for (int i = 0; i < 1000; i++) Assert.False(log.Observe(1, "caster", Sample()));
        }

        [Theory]
        [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
        public void ChangedNativeQueryGetsItsOwnWitness(int change)
        {
            var log = new PathEndDiagnosticLog(); var value = Sample();
            Assert.True(log.Observe(1, "caster", value));
            switch (change)
            {
                case 0: value.GoalX += 0.1f; break;
                case 1: value.GoalY += 0.1f; break;
                case 2: value.GoalZ += 0.1f; break;
                case 3: value.Radius -= 0.1f; break;
                case 4: value.NeedSight = false; break;
                case 5: value.Reason++; break;
            }
            Assert.True(log.Observe(1, "caster", value));
            Assert.False(log.Observe(1, "caster", value));
        }

        [Theory]
        [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
        public void MaterialPathChangesAreStillReported(int change)
        {
            var log = new PathEndDiagnosticLog(); var value = Sample();
            Assert.True(log.Observe(1, "caster", value));
            switch (change)
            {
                case 0: value.EndX += 0.25f; break;
                case 1: value.EndY += 0.25f; break;
                case 2: value.Distance += 0.5f; break;
                case 3: value.PathLength += 0.5f; break;
                case 4: value.Trimmed++; break;
            }
            Assert.True(log.Observe(1, "caster", value));
        }

        [Fact]
        public void SmallJitterDoesNotResetTheLastLoggedPosition()
        {
            var log = new PathEndDiagnosticLog();
            Assert.True(log.Observe(1, "caster", Sample()));
            Assert.False(log.Observe(1, "caster", Sample(95.1f)));
            Assert.False(log.Observe(1, "caster", Sample(95.2f)));
            Assert.True(log.Observe(1, "caster", Sample(95.3f)));
        }

        [Fact]
        public void ChangedWitnessesCannotProduceAnUnboundedStream()
        {
            var log = new PathEndDiagnosticLog(); int reports = 0;
            for (int i = 0; i < 100; i++) if (log.Observe(1, "caster", Sample(95f + i))) reports++;
            Assert.Equal(PathEndDiagnosticLog.MaxReportsPerQuery, reports);
            Assert.True(log.Observe(2, "caster", Sample()));
        }

        [Fact]
        public void UnitAndQueryCapacityPreserveEarlierSuppression()
        {
            var log = new PathEndDiagnosticLog(); var value = Sample();
            for (int i = 0; i < PathEndDiagnosticLog.MaxUnits; i++) Assert.True(log.Observe(1, "caster" + i, value));
            Assert.False(log.Observe(1, "overflow", value));
            Assert.False(log.Observe(1, "caster0", value));
            for (int i = 1; i < PathEndDiagnosticLog.MaxQueriesPerUnit; i++)
            { value.Radius++; Assert.True(log.Observe(1, "caster0", value)); }
            value.Radius++; Assert.False(log.Observe(1, "caster0", value));
            Assert.False(log.Observe(1, "caster0", Sample()));
            Assert.True(log.Observe(2, "overflow", value));
        }

        [Fact]
        public void NoActorIdentityIsQuietAndActorsAreIndependent()
        {
            var log = new PathEndDiagnosticLog(); var value = Sample();
            Assert.False(log.Observe(1, null, value)); Assert.False(log.Observe(1, "", value));
            Assert.True(log.Observe(1, "one", value)); Assert.True(log.Observe(1, "two", value));
            Assert.False(log.Observe(1, "one", value));
        }

        [Fact]
        public void RepeatedWarmChecksAllocateNothing()
        {
            var log = new PathEndDiagnosticLog(); var value = Sample(); log.Observe(1, "caster", value);
            for (int i = 0; i < 100; i++) log.Observe(1, "caster", value);
            long before = GC.GetAllocatedBytesForCurrentThread(); int reports = 0;
            for (int i = 0; i < 10000; i++) if (log.Observe(1, "caster", value)) reports++;
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(0, reports); Assert.Equal(0, allocated);
        }
    }
}
