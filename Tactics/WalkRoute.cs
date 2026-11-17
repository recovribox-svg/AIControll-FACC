// ★ burst path forecast (17 November 2026, author-approved review follow-up A-R1; docs/BURST_PATH_FORECAST.md): a unit's
// walk as the game plans it, for the burst forecast. Pure: no game types; the game side is TacticalEngine.Burst.cs.
// Native contracts (read from the supplied assembly):
// - UnitCommand.TickApproaching asks the unit's movement agent for a walk to the command's approach point
//   (UnitEntityView.MoveTo → UnitMovementAgent.PathTo); the agent follows the path it got back (Path.vectorPath), one
//   waypoint after the other (m_NextPointIndex), at its MaxSpeed (the unit's CurrentSpeedMps);
// - the command stops once native mechanics distance is within the approach radius AND required LOS is clear
//   (UnitCommand.IsUnitCloseEnough). This route's geometric stop models the distance half, not a future LOS query.
// The straight walk the burst forecast used before (BurstRules.At, WalkMayBeInside, removed) is the two-point route.
using System;

namespace CompanionAI_Pathfinder.Tactics
{
    /// <summary>
    /// A walk as a line of points from where the unit stands, at its speed: where the unit is after some seconds, and
    /// whether it may be within reach of a point around a moment. Reused: <see cref="Start"/> clears it.
    /// </summary>
    public sealed class WalkRoute
    {
        /// <summary>The most points kept; a longer path is not read (the caller falls back to the straight walk).</summary>
        public const int MaxPoints = 64;

        // Grown on demand up to MaxPoints: most walks have a few points.
        private float[] m_X = new float[8];
        private float[] m_Y = new float[8];
        private float[] m_Z = new float[8];
        /// <summary>The horizontal distance along the route to each point.</summary>
        private float[] m_Along = new float[8];

        /// <summary>The points kept, the start included.</summary>
        public int Count { get; private set; }

        /// <summary>Metres per second (CurrentSpeedMps); BurstRules.Travel applies the floor for a unit read at rest.</summary>
        public float Speed { get; private set; }

        /// <summary>The horizontal length of the walk.</summary>
        public float Length => Count > 0 ? m_Along[Count - 1] : 0f;

        /// <summary>The route follows the game's own path for the walk (else it is the straight walk).</summary>
        public bool Native { get; set; }

        public float EndX => Count > 0 ? m_X[Count - 1] : 0f;
        public float EndY => Count > 0 ? m_Y[Count - 1] : 0f;
        public float EndZ => Count > 0 ? m_Z[Count - 1] : 0f;

        /// <summary>Starts a route where the unit stands.</summary>
        public void Start(float x, float y, float z, float speed)
        {
            m_X[0] = x;
            m_Y[0] = y;
            m_Z[0] = z;
            m_Along[0] = 0f;
            Count = 1;
            Speed = speed;
            Native = false;
        }

        /// <summary>Adds the next point; false when the route is full. A point on top of the last one is skipped.</summary>
        public bool Add(float x, float y, float z)
        {
            if (Count == 0 || Count >= MaxPoints) return false;
            int last = Count - 1;
            float dx = x - m_X[last], dz = z - m_Z[last];
            float step = (float)Math.Sqrt(dx * dx + dz * dz);
            if (!(step > 0.001f)) return true;
            if (Count == m_X.Length)
            {
                int size = Math.Min(MaxPoints, m_X.Length * 2);
                Array.Resize(ref m_X, size);
                Array.Resize(ref m_Y, size);
                Array.Resize(ref m_Z, size);
                Array.Resize(ref m_Along, size);
            }
            m_X[Count] = x;
            m_Y[Count] = y;
            m_Z[Count] = z;
            m_Along[Count] = m_Along[last] + step;
            Count++;
            return true;
        }

        /// <summary>
        /// Earliest distance-eligible stop under native SqrMechanicsDistance: horizontal radius, and above 1.5 m of height
        /// difference, half-height at most the radius. LOS can delay the real stop; this geometric forecast does not query it.
        /// </summary>
        public void StopWithin(float cx, float cy, float cz, float radius)
        {
            if (Count == 0 || float.IsNaN(radius)) return;
            double r = Math.Max(0f, radius);
            // Native GeometryUtils.SqrMechanicsDistance ignores height through 1.5 m, then uses max(XZ², Y² / 4).
            double height = Math.Max(1.5, 2 * r);
            double sx = m_X[0] - cx, sz = m_Z[0] - cz;
            if (sx * sx + sz * sz <= r * r && Math.Abs(m_Y[0] - cy) <= height)
            {
                Count = 1;
                return;
            }
            for (int i = 0; i + 1 < Count; i++)
            {
                double ax = m_X[i], az = m_Z[i];
                double dx = m_X[i + 1] - ax, dz = m_Z[i + 1] - az;
                double fx = ax - cx, fz = az - cz;
                double a = dx * dx + dz * dz;
                if (!(a > 0f)) continue;
                double b = 2 * (fx * dx + fz * dz);
                double c = fx * fx + fz * fz - r * r;
                double disc = b * b - 4 * a * c;
                if (disc < 0f) continue;
                double root = Math.Sqrt(disc);
                double enter = Math.Max(0, (-b - root) / (2 * a));
                double exit = Math.Min(1, (-b + root) / (2 * a));
                double fy = m_Y[i] - cy, dy = m_Y[i + 1] - m_Y[i];
                if (dy == 0)
                {
                    if (Math.Abs(fy) > height) continue;
                }
                else
                {
                    double y1 = (-height - fy) / dy, y2 = (height - fy) / dy;
                    enter = Math.Max(enter, Math.Min(y1, y2));
                    exit = Math.Min(exit, Math.Max(y1, y2));
                }
                if (enter > exit) continue;
                float y = (float)(m_Y[i] + dy * enter);
                m_X[i + 1] = (float)(ax + dx * enter);
                m_Y[i + 1] = y;
                m_Z[i + 1] = (float)(az + dz * enter);
                m_Along[i + 1] = m_Along[i] + (float)(Math.Sqrt(a) * enter);
                Count = i + 2;
                return;
            }
        }

        /// <summary>Where the unit is after <paramref name="seconds"/> on this route, stopping at its end.</summary>
        public void At(float seconds, out float x, out float y, out float z)
        {
            if (Count == 0)
            {
                x = y = z = 0f;
                return;
            }
            float travel = BurstRules.Travel(Length, Speed, seconds);
            if (!(travel > 0f) || Count == 1)
            {
                x = m_X[0];
                y = m_Y[0];
                z = m_Z[0];
                return;
            }
            int i = 1;
            while (i < Count - 1 && m_Along[i] < travel) i++;
            float span = m_Along[i] - m_Along[i - 1];
            float t = span > 0f ? Math.Min(1f, Math.Max(0f, (travel - m_Along[i - 1]) / span)) : 1f;
            x = m_X[i - 1] + (m_X[i] - m_X[i - 1]) * t;
            y = m_Y[i - 1] + (m_Y[i] - m_Y[i - 1]) * t;
            z = m_Z[i - 1] + (m_Z[i] - m_Z[i - 1]) * t;
        }

        /// <summary>
        /// The unit may be within <paramref name="reach"/> of (<paramref name="cx"/>, <paramref name="cz"/>) when a burst lands
        /// <paramref name="landing"/> seconds from now: at any of the window's samples, with the uncertainty of a walk's
        /// predicted position.
        /// </summary>
        public bool MayBeInside(float cx, float cz, float reach, float landing)
        {
            float from = Math.Max(0f, landing - BurstRules.WindowHalfSeconds);
            float to = Math.Max(from, landing + BurstRules.WindowHalfSeconds);
            int samples = BurstRules.WindowSamples;
            for (int i = 0; i < samples; i++)
            {
                float t = samples == 1 ? landing : from + (to - from) * i / (samples - 1);
                At(t, out float x, out _, out float z);
                float dx = x - cx, dz = z - cz;
                if (BurstRules.MaybeInside((float)Math.Sqrt(dx * dx + dz * dz), reach, BurstRules.Uncertainty(true, Speed, t))) return true;
            }
            return false;
        }
    }
}
