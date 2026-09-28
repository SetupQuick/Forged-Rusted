namespace Networking
{
    public enum NetworkDelivery : byte
    {
        Unreliable = 0,
        UnreliableSequenced = 1,
        ReliableOrdered = 2
    }
}