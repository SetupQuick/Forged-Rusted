namespace Networking
{
    public enum ClientConnectionState
    {
        Stopped,
        SendingHello,
        SendingReady,
        Connected,
        TimedOut
    }
}