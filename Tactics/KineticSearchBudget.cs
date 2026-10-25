// ★ v0.3.22: computation limits, never tactical weights. Expired searches publish no alternative configuration.
using System;
using System.Diagnostics;
namespace CompanionAI_Pathfinder.Tactics
{
    public sealed class KineticSearchBudget
    {
        private readonly Func<double> elapsed;
        private readonly double milliseconds;
        private readonly int evaluations;
        public int Evaluations { get; private set; }
        public bool Exhausted { get; private set; }
        public double ElapsedMilliseconds => elapsed();
        public KineticSearchBudget(double milliseconds, int evaluations, Func<double> elapsed = null)
        {
            if (milliseconds <= 0 || evaluations < 1) throw new ArgumentOutOfRangeException();
            this.milliseconds = milliseconds;
            this.evaluations = evaluations;
            if (elapsed == null) { var watch = Stopwatch.StartNew(); this.elapsed = () => watch.Elapsed.TotalMilliseconds; }
            else this.elapsed = elapsed;
        }
        public bool Continue()
        {
            Exhausted |= elapsed() >= milliseconds;
            return !Exhausted;
        }
        public void Check()
        {
            if (!Continue()) throw new KineticBudgetExceededException();
        }
        public void EvaluateAim()
        {
            Check();
            if (Evaluations == evaluations) { Exhausted = true; throw new KineticBudgetExceededException(); }
            Evaluations++;
        }
    }
    public sealed class KineticBudgetExceededException : Exception { }
}
