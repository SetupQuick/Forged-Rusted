using Networking;

namespace Gameplay.Entities
{
    /// <summary>
    /// What clients need to know about a creature or dropped item besides
    /// its position (that is NetTransform): type, health, the item it
    /// shows. Written by the server, replicated when it changes.
    /// </summary>
    public sealed class EntityState : NetBehaviour
    {
        public override NetBehaviourType ComponentType => NetBehaviourType.Entity;

        public EntityKind Kind { get; private set; }
        public float Health { get; private set; }
        public float MaxHealth { get; private set; }
        public ushort ItemNetworkId { get; private set; }
        public int Count { get; private set; }

        /// <summary>Raised on clients when new state arrived.</summary>
        public event System.Action Changed;

        /// <summary>Server: copy the simulation's values (only marks dirty on change).</summary>
        public void Set(EntityKind kind, float health, float maxHealth, ushort item, int count)
        {
            if (kind == Kind && health == Health && maxHealth == MaxHealth && item == ItemNetworkId && count == Count)
                return;

            Kind = kind;
            Health = health;
            MaxHealth = maxHealth;
            ItemNetworkId = item;
            Count = count;
            MarkDirty();
            Changed?.Invoke();
        }

        public override void WriteState(PacketWriter writer)
        {
            writer.Write((byte)Kind);
            writer.Write(Health);
            writer.Write(MaxHealth);
            writer.Write(ItemNetworkId);
            writer.Write((ushort)System.Math.Min(Count, ushort.MaxValue));
        }

        public override void ReadState(PacketReader reader, uint serverTick)
        {
            Kind = (EntityKind)reader.ReadByte();
            Health = reader.ReadFloat();
            MaxHealth = reader.ReadFloat();
            ItemNetworkId = reader.ReadUInt16();
            Count = reader.ReadUInt16();
            Changed?.Invoke();
        }
    }
}
