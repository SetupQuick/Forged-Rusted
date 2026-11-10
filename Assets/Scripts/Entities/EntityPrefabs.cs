using Networking;
using UnityEngine;

namespace Gameplay.Entities
{
    /// <summary>
    /// The network prefab used for every creature and dropped item, built
    /// in code (no prefab asset needed): NetObject + NetTransform +
    /// EntityState + EntityView. EntityView builds the look from the kind.
    /// Every machine registers it with the same id before the game starts.
    /// </summary>
    public static class EntityPrefabs
    {
        public const ushort EntityPrefabId = 1001;

        private static NetObject _template;

        public static NetObject Template
        {
            get
            {
                if (_template != null)
                    return _template;

                var go = new GameObject("Entity (template)");
                go.SetActive(false); // inactive: no Awake on the template itself
                Object.DontDestroyOnLoad(go);
                go.hideFlags = HideFlags.HideInHierarchy;

                // Order matters only for readability; NetObject caches behaviours on Awake.
                NetObject netObject = go.AddComponent<NetObject>();
                go.AddComponent<NetTransform>();
                go.AddComponent<EntityState>();
                go.AddComponent<EntityView>();

                netObject.AssignRuntimePrefabId(EntityPrefabId);
                _template = netObject;
                return _template;
            }
        }

        /// <summary>Registers the entity prefab with a network world (safe to call repeatedly).</summary>
        public static void Register(NetworkWorld world)
        {
            if (world == null || world.PrefabRegistry == null)
                return;

            world.PrefabRegistry.RegisterRuntime(Template);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _template = null;
        }
    }
}
