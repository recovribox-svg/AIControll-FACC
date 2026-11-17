// ★ burst path forecast (17 November 2026, author-approved review follow-up A-R1; docs/BURST_PATH_FORECAST.md): a unit's
// walk as the game plans it, for the burst forecast. Pure: no game types; the game side is TacticalEngine.Burst.cs.
// Native contracts (read from the supplied assembly):
// - UnitCommand.TickApproaching asks the unit's movement agent for a walk to the command's approach point
//   (UnitEntityView.MoveTo → UnitMovementAgent.PathTo); the agent follows the path it got back (Path.vectorPath), one
//   waypoint after the other (m_NextPointIndex), at its MaxSpeed (the unit's CurrentSpeedMps);
// - the command stops the walk once the unit is within its approach radius of the approach point
//   (UnitCommand.IsUnitCloseEnough), which can be before the path's last point.
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
        /// The walk ends where it first comes within <paramref name="radius"/> of (<paramref name="cx"/>, <paramref name="cz"/>)
        /// (the command's approach test); a route that starts within it is no walk.
        /// </summary>
        public void StopWithin(float cx, float cz, float radius)
        {
            if (Count == 0 || float.IsNaN(radius)) return;
            float r = Math.Max(0f, radius);
            float sx = m_X[0] - cx, sz = m_Z[0] - cz;
            if (sx * sx + sz * sz <= r * r)
            {
                Count = 1;
                return;
            }
            for (int i = 0; i + 1 < Count; i++)
            {
                float ax = m_X[i], az = m_Z[i];
                float dx = m_X[i + 1] - ax, dz = m_Z[i + 1] - az;
                float fx = ax - cx, fz = az - cz;
                float a = dx * dx + dz * dz;
                if (!(a > 0f)) continue;
                float b = 2f * (fx * dx + fz * dz);
                float c = fx * fx + fz * fz - r * r;
                float disc = b * b - 4f * a * c;
                if (disc < 0f) continue;
                float t = (-b - (float)Math.Sqrt(disc)) / (2f * a);   // the first crossing; the start is outside
                if (t < 0f || t > 1f) continue;
                float y = m_Y[i] + (m_Y[i + 1] - m_Y[i]) * t;
                m_X[i + 1] = ax + dx * t;
                m_Y[i + 1] = y;
                m_Z[i + 1] = az + dz * t;
                m_Along[i + 1] = m_Along[i] + (float)Math.Sqrt(a) * t;
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
