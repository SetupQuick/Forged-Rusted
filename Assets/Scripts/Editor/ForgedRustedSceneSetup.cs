using System.Text;
using Networking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using World;
using World.Streaming;

namespace ForgedRustedEditor
{
    /// <summary>
    /// Editor menu "Forged &amp; Rusted": checks the open scene and adds and
    /// wires up anything the game needs (network runtime, player prefab,
    /// world, chunk streamer, sun). The game also completes a scene by
    /// itself at runtime (GameBootstrap), but doing it here makes the
    /// setup visible and saved in the scene.
    /// </summary>
    public static class ForgedRustedSceneSetup
    {
        private const string RegistryPath = "Assets/Resources/Network/DefaultNetPrefabRegistry.asset";
        private const string PlayerPrefabPath = "Assets/Resources/Network/NetworkPlayer.prefab";
        private const string WorldSettingsPath = "Assets/Resources/World/DefaultWorldSettings.asset";
        private const string ChunkMaterialPath = "Assets/Resources/World/ChunkMaterial.mat";
        private const string WaterMaterialPath = "Assets/Resources/World/WaterMaterial.mat";

        [MenuItem("Forged & Rusted/Check Current Scene")]
        public static void CheckScene()
        {
            var report = new StringBuilder();
            bool ok = Check(report);

            string message = (ok ? "Scene is ready to play.\n\n" : "Some things are missing (Set Up Current Scene fixes them):\n\n") + report;
            Debug.Log("[Forged & Rusted] " + message);
            EditorUtility.DisplayDialog("Forged & Rusted - scene check", message, "OK");
        }

        [MenuItem("Forged & Rusted/Set Up Current Scene")]
        public static void SetUpScene()
        {
            var registry = AssetDatabase.LoadAssetAtPath<NetPrefabRegistry>(RegistryPath);
            var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            var worldSettings = AssetDatabase.LoadAssetAtPath<WorldSettings>(WorldSettingsPath);
            var chunkMaterial = AssetDatabase.LoadAssetAtPath<Material>(ChunkMaterialPath);
            var waterMaterial = AssetDatabase.LoadAssetAtPath<Material>(WaterMaterialPath);

            // Network
            NetworkRuntime runtime = Object.FindFirstObjectByType<NetworkRuntime>();
            if (runtime == null)
            {
                var go = new GameObject("Network");
                Undo.RegisterCreatedObjectUndo(go, "Create Network");
                go.AddComponent<NetworkWorld>();
                runtime = go.AddComponent<NetworkRuntime>();
            }

            NetworkWorld networkWorld = runtime.GetComponent<NetworkWorld>();
            Assign(networkWorld, "prefabRegistry", registry, onlyIfEmpty: true);
            Assign(runtime, "world", networkWorld, onlyIfEmpty: true);

            PeerSpawnExample spawner = Object.FindFirstObjectByType<PeerSpawnExample>();
            if (spawner == null)
                spawner = Undo.AddComponent<PeerSpawnExample>(runtime.gameObject);

            Assign(spawner, "playerPrefab", playerPrefab != null ? playerPrefab.GetComponent<NetObject>() : null, onlyIfEmpty: true);

            // World
            WorldManager world = Object.FindFirstObjectByType<WorldManager>();
            if (world == null)
            {
                var go = new GameObject("World");
                Undo.RegisterCreatedObjectUndo(go, "Create World");
                go.SetActive(false);
                world = go.AddComponent<WorldManager>();
                go.SetActive(true);
            }

            Assign(world, "settings", worldSettings, onlyIfEmpty: true);
            Assign(world, "chunkMaterial", chunkMaterial, onlyIfEmpty: true);
            Assign(world, "waterMaterial", waterMaterial, onlyIfEmpty: true);

            ChunkStreamer streamer = Object.FindFirstObjectByType<ChunkStreamer>();
            if (streamer == null)
                streamer = Undo.AddComponent<ChunkStreamer>(world.gameObject);

            Assign(streamer, "worldManager", world, onlyIfEmpty: true);
            Assign(streamer, "networkWorld", networkWorld, onlyIfEmpty: true);

            // Sun (the game configures colour and angle at runtime)
            bool hasSun = false;
            foreach (Light light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                hasSun |= light.type == LightType.Directional;

            if (!hasSun)
            {
                var go = new GameObject("Sun");
                Undo.RegisterCreatedObjectUndo(go, "Create Sun");
                var light = go.AddComponent<Light>();
                light.type = LightType.Directional;
                light.shadows = LightShadows.Soft;
                go.transform.rotation = Quaternion.Euler(42f, -35f, 0f);
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            var report = new StringBuilder();
            bool ok = Check(report);
            Debug.Log("[Forged & Rusted] Scene set up. " + (ok ? "Everything is in place - save the scene." : "Still missing:\n" + report));

            if (ok)
                EditorUtility.DisplayDialog("Forged & Rusted", "The scene is set up. Save it (Ctrl+S) and press Play.", "OK");
            else
                EditorUtility.DisplayDialog("Forged & Rusted", "Some things could not be set up:\n\n" + report, "OK");
        }

        private static bool Check(StringBuilder report)
        {
            bool ok = true;

            void Line(bool good, string text)
            {
                report.AppendLine((good ? "OK      " : "MISSING ") + text);
                ok &= good;
            }

            NetworkRuntime runtime = Object.FindFirstObjectByType<NetworkRuntime>();
            Line(runtime != null, "NetworkRuntime");

            NetworkWorld networkWorld = runtime != null ? runtime.GetComponent<NetworkWorld>() : null;
            Line(networkWorld != null && Get(networkWorld, "prefabRegistry") != null, "NetworkWorld with a prefab registry");

            PeerSpawnExample spawner = Object.FindFirstObjectByType<PeerSpawnExample>();
            Line(spawner != null && Get(spawner, "playerPrefab") != null, "PeerSpawnExample with the player prefab");

            WorldManager world = Object.FindFirstObjectByType<WorldManager>();
            Line(world != null && Get(world, "settings") != null, "WorldManager with WorldSettings");

            Line(Object.FindFirstObjectByType<ChunkStreamer>() != null, "ChunkStreamer");

            Line(AssetDatabase.LoadAssetAtPath<Material>(ChunkMaterialPath) != null, "Voxel materials in Resources/World");

            report.AppendLine();
            report.AppendLine("Not needed in the scene (created by code): main menu, HUD, inventory UI, EventSystem, lighting/post-processing, sounds, player model.");

            return ok;
        }

        private static Object Get(Object target, string field)
        {
            SerializedProperty property = new SerializedObject(target).FindProperty(field);
            return property != null ? property.objectReferenceValue : null;
        }

        private static void Assign(Object target, string field, Object value, bool onlyIfEmpty)
        {
            if (target == null || value == null)
                return;

            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(field);

            if (property == null || (onlyIfEmpty && property.objectReferenceValue != null))
                return;

            property.objectReferenceValue = value;
            serialized.ApplyModifiedProperties();
        }
    }
}
