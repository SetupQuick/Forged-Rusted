using UnityEngine;

namespace Gameplay.Entities
{
    public enum EntityKind : byte
    {
        None = 0,
        DroppedItem = 1,
        Pig = 2,
        Chicken = 3,
        Zombie = 4
    }

    /// <summary>Numbers for each creature type (placeholders until the real creatures exist).</summary>
    public struct EntityDefinition
    {
        public EntityKind Kind;
        public string Name;
        public float Width;          // metres
        public float Height;
        public float MaxHealth;
        public float WalkSpeed;      // m/s
        public float RunSpeed;
        public bool Hostile;
        public float AttackDamage;
        public float AttackRange;    // metres from body centre (horizontal)
        public float AttackCooldown; // seconds
        public float SightRange;
        public string DropItemId;    // item dropped on death (null = none)
        public int DropMin, DropMax;
        public int Experience;       // XP for the killer
    }

    public static class EntityDefinitions
    {
        public static readonly EntityDefinition DroppedItem = new EntityDefinition
        {
            Kind = EntityKind.DroppedItem, Name = "Item", Width = 0.3f, Height = 0.3f, MaxHealth = 1f
        };

        public static readonly EntityDefinition Pig = new EntityDefinition
        {
            Kind = EntityKind.Pig, Name = "Pig", Width = 0.9f, Height = 0.9f, MaxHealth = 10f,
            WalkSpeed = 1.2f, RunSpeed = 3.2f, DropItemId = "Pork", DropMin = 1, DropMax = 3, Experience = 2
        };

        public static readonly EntityDefinition Chicken = new EntityDefinition
        {
            Kind = EntityKind.Chicken, Name = "Chicken", Width = 0.45f, Height = 0.7f, MaxHealth = 4f,
            WalkSpeed = 1f, RunSpeed = 3f, DropItemId = "Chicken", DropMin = 1, DropMax = 1, Experience = 1
        };

        public static readonly EntityDefinition Zombie = new EntityDefinition
        {
            Kind = EntityKind.Zombie, Name = "Zombie", Width = 0.6f, Height = 1.9f, MaxHealth = 20f,
            WalkSpeed = 1.1f, RunSpeed = 2.6f, Hostile = true, AttackDamage = 3f, AttackRange = 1.3f,
            AttackCooldown = 1f, SightRange = 20f, Experience = 5
        };

        public static EntityDefinition Get(EntityKind kind)
        {
            switch (kind)
            {
                case EntityKind.DroppedItem: return DroppedItem;
                case EntityKind.Pig: return Pig;
                case EntityKind.Chicken: return Chicken;
                case EntityKind.Zombie: return Zombie;
                default: return default;
            }
        }
    }

    /// <summary>A creature or dropped item on the server (not a Unity object - testable).</summary>
    public sealed class ServerEntity
    {
        public uint Id;
        public EntityKind Kind;
        public EntityDefinition Definition;
        public Vector3 Position;      // feet (bottom centre), metres
        public Vector3 Velocity;
        public float Yaw;
        public float Health;
        public bool OnGround;
        public bool InWater;
        public bool Dead;

        // Dropped items
        public string ItemId;
        public int Count;
        public uint DroppedBy;
        public float PickupDelay;

        public float Age;
        public float HurtTimer;      // > 0 right after being hit (red flash, fleeing)
        public float AttackTimer;
        public float WanderTimer;
        public Vector3 WanderDirection;
        public uint TargetPeer;
        public uint LastAttacker;

        /// <summary>Set when something clients see changed (health, item, ...).</summary>
        public bool StateDirty = true;
    }
}
