namespace Networking
{
    public static class NetworkTime
    {
        public const int TickRate = 33;

        public const double TickInterval =
            1.0 / TickRate;

        public const float TickDelta =
            1f / TickRate;
    }
}