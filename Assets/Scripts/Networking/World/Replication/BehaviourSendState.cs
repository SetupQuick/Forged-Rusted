namespace Networking
{
    public readonly struct BehaviourSendState
    {
        public uint Version { get; }

        public uint LastSentTick { get; }

        public BehaviourSendState(
            uint version,
            uint lastSentTick)
        {
            Version = version;
            LastSentTick = lastSentTick;
        }
    }
}