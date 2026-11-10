using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace ForgedRustedEditor
{
    /// <summary>
    /// The first time the project is opened on a PC, opens the main scene
    /// (Assets/Scenes/Game.unity) instead of an empty untitled scene, so
    /// pressing Play starts the game right away. Only once per PC, and
    /// never over a scene with unsaved changes. Menu: Forged &amp; Rusted >
    /// Open Game Scene.
    /// </summary>
    [InitializeOnLoad]
    public static class OpenGameSceneOnLoad
    {
        public const string GameScenePath = "Assets/Scenes/Game.unity";
        private const string DoneKey = "ForgedRusted.OpenedGameSceneOnce";

        static OpenGameSceneOnLoad()
        {
            // Wait until the editor has finished loading.
            EditorApplication.delayCall += OpenOnce;
        }

        private static void OpenOnce()
        {
            if (EditorPrefs.GetBool(DoneKey + "." + PlayerSettings.productGUID, false))
                return;

            if (EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer)
                return;

            Scene active = SceneManager.GetActiveScene();
            bool emptyOrDefault = string.IsNullOrEmpty(active.path) ||
                                  active.path.EndsWith("SampleScene.unity");

            if (!emptyOrDefault || active.isDirty)
                return;

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(GameScenePath) == null)
                return;

            EditorPrefs.SetBool(DoneKey + "." + PlayerSettings.productGUID, true);
            EditorSceneManager.OpenScene(GameScenePath);
        }

        [MenuItem("Forged & Rusted/Open Game Scene", priority = 0)]
        public static void OpenGameScene()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(GameScenePath);
        }
    }
}
