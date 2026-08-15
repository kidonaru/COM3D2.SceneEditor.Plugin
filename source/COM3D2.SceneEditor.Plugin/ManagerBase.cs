using COM3D2.MotionTimelineEditor;
using UnityEngine.SceneManagement;

namespace COM3D2.SceneEditor.Plugin
{
    public class ManagerBase : IManager
    {
        protected static SceneEditorPlugin plugin => SceneEditorPlugin.instance;
        protected static ConfigManager configManager => ConfigManager.instance;
        protected static Config config => ConfigManager.instance.config;
        protected static WindowManager windowManager => WindowManager.instance;
        protected static GameViewManager gameViewManager => GameViewManager.instance;
        protected static SelectionManager selectionManager => SelectionManager.instance;
        protected static CharacterMgr characterMgr => GameMain.Instance.CharacterMgr;

        public virtual void Init()
        {
        }

        public virtual void PreUpdate()
        {
        }

        public virtual void Update()
        {
        }

        public virtual void LateUpdate()
        {
        }

        public virtual void OnLoad()
        {
        }

        public virtual void OnPluginDisable()
        {
        }

        public virtual void OnChangedSceneLevel(Scene scene, LoadSceneMode sceneMode)
        {
        }
    }
}
