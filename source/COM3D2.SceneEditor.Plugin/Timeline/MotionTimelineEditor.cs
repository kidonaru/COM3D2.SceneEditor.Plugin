using System.Collections.Generic;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// MTE のプラグイン本体 (MotionTimelineEditor クラス) の代替ファサード。
    /// 移植コードが参照する mte.OnLoad() / isEnable / SaveScreenShot を、
    /// SceneEditor 統合環境向けに提供する
    /// </summary>
    public class MotionTimelineEditor
    {
        private static MotionTimelineEditor _instance;
        public static MotionTimelineEditor instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new MotionTimelineEditor();
                }
                return _instance;
            }
        }

        private MotionTimelineEditor()
        {
        }

        /// <summary>タイムライン読み込み時に OnLoad を再通知するマネージャ群 (統合初期化時に登録)</summary>
        private readonly List<IManager> _managers = new List<IManager>();

        public void RegisterManager(IManager manager)
        {
            if (!_managers.Contains(manager))
            {
                _managers.Add(manager);
            }
        }

        /// <summary>SceneEditor 本体の有効状態に追従する</summary>
        public bool isEnable => SceneEditor.Plugin.SceneEditorPlugin.instance != null
            && SceneEditor.Plugin.SceneEditorPlugin.instance.isEnable;

        private static StudioHackBase studioHack => StudioHackManager.instance.studioHack;
        private static TimelineData timeline => TimelineManager.instance.timeline;

        /// <summary>
        /// タイムライン作成・読み込み後の再初期化。
        /// MTE 本体の OnLoad と同じく、登録済みマネージャへ OnLoad を伝搬する
        /// </summary>
        public void OnLoad()
        {
            MTEUtils.LogDebug("MotionTimelineEditor.OnLoad");

            if (studioHack == null || !studioHack.IsValid())
            {
                return;
            }
            if (timeline == null)
            {
                return;
            }

            foreach (var manager in _managers)
            {
                manager.OnLoad();
            }
        }

        /// <summary>サムネ用にゲーム画面を保存する (実体は ThumbnailCapture.Save)</summary>
        public void SaveScreenShot(string filePath, int width, int height)
        {
            SceneEditor.Plugin.ThumbnailCapture.Save(filePath, width, height);
        }
    }
}
