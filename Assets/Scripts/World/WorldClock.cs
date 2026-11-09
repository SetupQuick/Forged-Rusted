using System;

namespace World
{
    /// <summary>
    /// Time of day, shared by everything that animates with it (sun,
    /// moon, sky, clouds, wind gusts).
    ///
    /// The server owns the clock and sends it to clients every few
    /// seconds (TimeSync), so every player sees the same sky and the same
    /// clouds. Offline the clock simply runs. Advanced by the
    /// <see cref="WorldManager"/> each frame.
    ///
    /// TimeOfDay: 0 = midnight, 0.25 = sunrise, 0.5 = noon, 0.75 = sunset.
    /// </summary>
    public static class WorldClock
    {
        /// <summary>Seconds of world time since the world started.</summary>
        public static double WorldSeconds { get; private set; }

        /// <summary>Real seconds for one full day (default 20 minutes).</summary>
        public static float DayLengthSeconds { get; private set; } = 1200f;

        /// <summary>Time of day when the world started (0..1).</summary>
        public static float StartTimeOfDay { get; private set; } = 0.3f;

        /// <summary>Stops the clock (debug, screenshots). Not networked.</summary>
        public static bool Paused { get; set; }

        /// <summary>0..1 (0 midnight, 0.5 noon).</summary>
        public static float TimeOfDay
        {
            get
            {
                double t = StartTimeOfDay + WorldSeconds / Math.Max(1.0, DayLengthSeconds);
                return (float)(t - Math.Floor(t));
            }
        }

        /// <summary>Number of the current day, starting at 0.</summary>
        public static int Day =>
            (int)Math.Floor(StartTimeOfDay + WorldSeconds / Math.Max(1.0, DayLengthSeconds));

        /// <summary>Raised when the clock jumps (new world, sync correction, command).</summary>
        public static event Action Changed;

        public static void Reset(float dayLengthSeconds, float startTimeOfDay)
        {
            DayLengthSeconds = Math.Max(10f, dayLengthSeconds);
            StartTimeOfDay = startTimeOfDay - (float)Math.Floor(startTimeOfDay);
            WorldSeconds = 0;
            Changed?.Invoke();
        }

        public static void Advance(double deltaSeconds)
        {
            if (!Paused && deltaSeconds > 0)
                WorldSeconds += deltaSeconds;
        }

        /// <summary>Jumps to a time of day (keeps the day count; e.g. a "/time" command).</summary>
        public static void SetTimeOfDay(float timeOfDay)
        {
            timeOfDay -= (float)Math.Floor(timeOfDay);
            double days = Math.Floor(StartTimeOfDay + WorldSeconds / DayLengthSeconds);
            WorldSeconds = Math.Max(0, (days + timeOfDay - StartTimeOfDay) * DayLengthSeconds);
            Changed?.Invoke();
        }

        /// <summary>
        /// Applies the server's clock. Small differences are smoothed out
        /// (no visible jumps of the sun); large ones snap.
        /// </summary>
        public static void ApplyServerTime(double serverSeconds, float dayLengthSeconds, float startTimeOfDay)
        {
            bool configChanged =
                Math.Abs(dayLengthSeconds - DayLengthSeconds) > 0.01f ||
                Math.Abs(startTimeOfDay - StartTimeOfDay) > 0.0001f;

            DayLengthSeconds = Math.Max(10f, dayLengthSeconds);
            StartTimeOfDay = startTimeOfDay;

            double error = serverSeconds - WorldSeconds;

            if (configChanged || Math.Abs(error) > 3.0)
            {
                WorldSeconds = serverSeconds;
                Changed?.Invoke();
            }
            else
            {
                // Latency makes us slightly behind; take a third of the gap.
                WorldSeconds += error * 0.33;
            }
        }
    }
}
