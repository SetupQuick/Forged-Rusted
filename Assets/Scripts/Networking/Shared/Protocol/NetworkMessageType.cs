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

        PlayerMovementState = 12,

        WorldInfo = 13,

        // Client -> server: inventory clicks, block break/place.
        PlayerAction = 14,

        // Server -> clients: one block changed.
        BlockChanged = 15,

        // Server -> owning client: full inventory + acknowledged action.
        InventorySnapshot = 16,

        // Server -> joining client: batch of earlier block changes.
        WorldEdits = 17,

        // Server -> clients: what a player is holding (for hands/avatars).
        Equipment = 18,

        // Server -> clients: world clock (day/night, clouds), every few seconds.
        TimeSync = 19,

        // Client -> server: persistent player id and name (for saved inventories).
        PlayerProfile = 20,

        // Server -> owner: health, mana, level, class, skills, quests.
        PlayerStatus = 21,

        // Server -> clients: visible effects of a player (invisible).
        PlayerEffects = 22
    }
}