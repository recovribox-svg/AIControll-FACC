using System;
using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    public class ReferenceReadMemoTests
    {
        private sealed class EqualObjects
        {
            public override bool Equals(object obj) => obj is EqualObjects;
            public override int GetHashCode() => 1;
        }
        [Fact]
        public void UsesExactCasterTargetAndSpellCopyIdentity()
        {
            var memo=new ReferenceReadMemo();var caster=new EqualObjects();var target=new EqualObjects();var source=new EqualObjects();
            memo.Store(caster,target,source,1,.347891f);
            Assert.True(memo.TryGet(caster,target,source,1,out float value));Assert.Equal(.347891f,value);
            Assert.False(memo.TryGet(new EqualObjects(),target,source,1,out _));
            Assert.False(memo.TryGet(caster,new EqualObjects(),source,1,out _));
            Assert.False(memo.TryGet(caster,target,new EqualObjects(),1,out _));
        }
        [Theory]
        [InlineData(0f)]
        [InlineData(1f)]
        [InlineData(.65f)]
        public void RetainsFullNativeResultIncludingImmunity(float result)
        {
            var memo=new ReferenceReadMemo();var c=new object();var t=new object();var a=new object();
            memo.Store(c,t,a,0,result);Assert.True(memo.TryGet(c,t,a,0,out float value));Assert.Equal(result,value);
        }
        [Fact]
        public void PreviewEpochAndFreshEvaluationDiscardEarlierAnswers()
        {
            var memo=new ReferenceReadMemo();var c=new object();var t=new object();var a=new object();
            memo.Store(c,t,a,1,0f);Assert.False(memo.TryGet(c,t,a,2,out _));
            memo.Store(c,t,a,2,1f);Assert.True(memo.TryGet(c,t,a,2,out float value));Assert.Equal(1f,value);
            Assert.False(new ReferenceReadMemo().TryGet(c,t,a,2,out _));
        }
        [Fact]
        public void WarmReadsAllocateNothing()
        {
            var memo=new ReferenceReadMemo();var c=new object();var t=new object();var a=new object();memo.Store(c,t,a,0,1f);
            for(int i=0;i<10;i++)memo.TryGet(c,t,a,0,out _);
            long before=GC.GetAllocatedBytesForCurrentThread();bool all=true;
            for(int i=0;i<10000;i++)all &= memo.TryGet(c,t,a,0,out float value) && value==1f;
            long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
            Assert.True(all);Assert.Equal(0,allocated);
        }
    }
}
