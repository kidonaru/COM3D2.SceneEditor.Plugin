using UnityEngine.SceneManagement;
using System.Collections.Generic;
using UnityEngine.Events;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    public class StudioHackManager : ManagerBase
    {
        private List<StudioHackBase> studioHacks = new List<StudioHackBase>();
        private List<StudioHackBase> activeStudioHacks = new List<StudioHackBase>();

        private StudioHackBase _studioHack = null;
        public override StudioHackBase studioHack => _studioHack;

        private bool _isPoseEditing = false;

        /// <summary>
        /// ポーズ編集モード。
        /// ボーン表示トグルは追従させない (モード外はボーン自体が出ないため、
        /// トグルの値はユーザーが決めた表示設定として保つ)
        /// </summary>
        public bool isPoseEditing
        {
            get => _isPoseEditing;
            set
            {
                if (_studioHack != null)
                {
                    _studioHack.isPoseEditing = value;
                }
            }
        }

        public static event UnityAction<bool> onPoseEditingChanged;

        private static StudioHackManager _instance;
        public static StudioHackManager instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new StudioHackManager();
                }

                return _instance;
            }
        }

        private StudioHackManager()
        {
        }

        public void Register(StudioHackBase studioHack)
        {
            if (studioHack == null || !studioHack.Init())
            {
                return;
            }

            studioHacks.Add(studioHack);
            studioHacks.Sort((a, b) => b.priority - a.priority);
        }

        public override void PreUpdate()
        {
            if (studioHacks.Count == 0)
            {
                return;
            }

            _studioHack = null;
            activeStudioHacks.Clear();

            foreach (var hack in studioHacks)
            {
                if (hack.isSceneActive)
                {
                    activeStudioHacks.Add(hack);
                }
            }

            foreach (var hack in activeStudioHacks)
            {
                if (hack.IsValid())
                {
                    _studioHack = hack;
                    break;
                }
            }

            if (_studioHack == null && activeStudioHacks.Count > 0)
            {
                _studioHack = activeStudioHacks[0];
            }

            SyncPoseEditing();
        }

        /// <summary>
        /// studioHack 側の編集モードをキャッシュへ反映し、変化していれば通知する。
        /// PreUpdate のほか、パラメータ変更で同フレーム中に編集モードへ入ったとき
        /// (AutoEditMode.Enter) にも呼び、以降の描画が古いキャッシュを見ないようにする
        /// </summary>
        public void SyncPoseEditing()
        {
            if (!mte.isEnable)
            {
                return;
            }

            var isPoseEditingNow = _studioHack?.isPoseEditing ?? false;
            if (isPoseEditingNow != _isPoseEditing)
            {
                _isPoseEditing = isPoseEditingNow;
                onPoseEditingChanged?.Invoke(isPoseEditingNow);
            }
        }

        public override void Update()
        {
            if (_studioHack != null)
            {
                _studioHack.Update();
            }
        }

        public override void OnChangedSceneLevel(Scene scene, LoadSceneMode sceneMode)
        {
            foreach (var studioHack in studioHacks)
            {
                studioHack.OnChangedSceneLevel(scene, sceneMode);
            }
        }
    }
}