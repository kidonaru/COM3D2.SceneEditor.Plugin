using COM3D2.MotionTimelineEditor;
using UnityEngine.SceneManagement;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// タイムライン機能 (MTE 移植コード) の SceneEditor への組み込み。
    /// レイヤー・Transform・マネージャの登録を一括で行う
    /// </summary>
    public static class TimelineIntegration
    {
        /// <summary>
        /// タイムライン系マネージャの更新を MTE 本体と同じガード付きフローで回す複合マネージャ。
        /// timeline 未生成時に TimelineManager.Update が defaultLayer を触って NPE するため、
        /// 個別登録ではなくこの複合マネージャ経由でのみ更新する
        /// </summary>
        private class TimelineUpdateManager : IManager
        {
            private static MTEP.StudioHackManager studioHackManager => MTEP.StudioHackManager.instance;
            private static MTEP.StudioHackBase studioHack => studioHackManager.studioHack;
            private static MTEP.MaidManager maidManager => MTEP.MaidManager.instance;
            private static MTEP.TimelineManager timelineManager => MTEP.TimelineManager.instance;

            private readonly IManager[] _managers =
            {
                MTEP.ConfigManager.instance,
                studioHackManager,
                MTEP.MaidManager.instance,
                MTEP.TimelineManager.instance,
                MTEP.TimelineHistoryManager.instance,
            };

            public void Init()
            {
                foreach (var manager in _managers)
                {
                    manager.Init();
                }
            }

            public void PreUpdate()
            {
            }

            /// <summary>MTE 本体 Update のガード順 (hack 選択 → メイド解決 → データ検証) を踏襲する</summary>
            private bool UpdateGuards()
            {
                studioHackManager.PreUpdate();

                if (studioHack == null || !studioHack.IsValid())
                {
                    return false;
                }

                maidManager.PreUpdate();

                if (maidManager.maid == null)
                {
                    return false;
                }

                return timelineManager.IsValidData();
            }

            public void Update()
            {
                if (!UpdateGuards())
                {
                    return;
                }

                foreach (var manager in _managers)
                {
                    manager.Update();
                }
            }

            public void LateUpdate()
            {
                if (studioHack == null || maidManager.maid == null ||
                    !timelineManager.IsValidData())
                {
                    return;
                }

                foreach (var manager in _managers)
                {
                    manager.LateUpdate();
                }
            }

            public void OnLoad()
            {
                // MTE 本体の OnLoad と同じガード。タイムライン未生成のままプラグインを
                // 有効化すると MaidManager.OnLoad が currentLayer (null) を触って NRE する
                if (studioHack == null || !studioHack.IsValid() ||
                    timelineManager.timeline == null)
                {
                    return;
                }

                foreach (var manager in _managers)
                {
                    manager.OnLoad();
                }
            }

            public void OnPluginDisable()
            {
                foreach (var manager in _managers)
                {
                    manager.OnPluginDisable();
                }
            }

            public void OnChangedSceneLevel(Scene scene, LoadSceneMode sceneMode)
            {
                foreach (var manager in _managers)
                {
                    manager.OnChangedSceneLevel(scene, sceneMode);
                }
            }
        }

        public static void Initialize(ManagerRegistry managerRegistry)
        {
            var timelineManager = MTEP.TimelineManager.instance;

            MTEP.StudioHackManager.instance.Register(new MTEP.SceneEditorHack());

            timelineManager.RegisterLayer(
                typeof(MTEP.MotionTimelineLayer), MTEP.MotionTimelineLayer.Create);

            timelineManager.RegisterTransform(
                MTEP.TransformType.ExtendBone,
                MTEP.TimelineManager.CreateTransform<MTEP.TransformDataExtendBone>);
            timelineManager.RegisterTransform(
                MTEP.TransformType.FingerBlend,
                MTEP.TimelineManager.CreateTransform<MTEP.TransformDataFingerBlend>);
            timelineManager.RegisterTransform(
                MTEP.TransformType.Grounding,
                MTEP.TimelineManager.CreateTransform<MTEP.TransformDataGrounding>);
            timelineManager.RegisterTransform(
                MTEP.TransformType.IKHold,
                MTEP.TimelineManager.CreateTransform<MTEP.TransformDataIKHold>);
            timelineManager.RegisterTransform(
                MTEP.TransformType.Root,
                MTEP.TimelineManager.CreateTransform<MTEP.TransformDataRoot>);
            timelineManager.RegisterTransform(
                MTEP.TransformType.Rotation,
                MTEP.TimelineManager.CreateTransform<MTEP.TransformDataRotation>);

            var updateManager = new TimelineUpdateManager();
            managerRegistry.RegisterManager(updateManager);

            // タイムライン作成・読み込み時の mte.OnLoad も同じ複合マネージャへ集約し、
            // OnLoad の二重発火とガード漏れを防ぐ
            MTEP.MotionTimelineEditor.instance.RegisterManager(updateManager);
        }
    }
}
