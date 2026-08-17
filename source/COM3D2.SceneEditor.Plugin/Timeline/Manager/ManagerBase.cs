using UnityEngine.SceneManagement;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// タイムライン系マネージャの共通基底。
    /// MTE からの移植にあたり、未移植のマネージャ参照 (モデル/ライト/演出系) は削除している
    /// </summary>
    public class ManagerBase : IManager
    {
        public virtual Maid maid => maidManager.maid;
        public virtual MaidCache maidCache => maidManager.maidCache;
        public virtual Config config => ConfigManager.instance.config;
        public virtual TimelineData timeline => timelineManager.timeline;
        public virtual ITimelineLayer currentLayer => timelineManager.currentLayer;
        public virtual ITimelineLayer defaultLayer => timeline.defaultLayer;
        public virtual StudioHackBase studioHack => StudioHackManager.instance.studioHack;

        // PartsEdit 連携は未移植のため常に null (呼び出し側は null チェック済み)
        public virtual IPartsEditHack partsEditHack => null;

        protected static MotionTimelineEditor mte => MotionTimelineEditor.instance;
        protected static MaidManager maidManager => MaidManager.instance;
        protected static StudioHackManager studioHackManager => StudioHackManager.instance;

        protected static TimelineManager timelineManager => TimelineManager.instance;
        protected static TimelineHistoryManager historyManager => TimelineHistoryManager.instance;

        protected static ConfigManager configManager => ConfigManager.instance;
        protected static BoneMenuManager boneMenuManager => BoneMenuManager.Instance;

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
