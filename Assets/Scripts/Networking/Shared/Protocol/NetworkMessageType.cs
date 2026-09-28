namespace Networking
{
    public enum NetworkMessageType : byte
    {
        ClientHello = 1,
        ServerAccept = 2,
        ClientReady = 3,
        ServerReady = 4,
        Heartbeat = 5,
        HeartbeatAck = 6,
        Disconnect = 7,

        ObjectSpawn = 8,
        ObjectDespawn = 9,

        PlayerInput = 10,
        WorldSnapshot = 11,

        PlayerMovementState = 12
    }
}