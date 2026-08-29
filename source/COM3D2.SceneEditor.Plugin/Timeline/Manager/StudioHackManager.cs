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
        /// ボーン/IK 表示の所有者は SE 側の MaidManipulateManager.isBoneVisible
        /// (ボーン表示トグル) に一本化したため、ここでは編集モードの委譲だけを行い、
        /// 表示状態には触れない。
        /// 以前は編集モードを切り替えるたびに選択中のレイヤー種別で表示を上書きしており、
        /// トグルが意図せず落ちていた
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

            if (mte.isEnable)
            {
                var isPoseEditingNow = _studioHack?.isPoseEditing ?? false;
                if (isPoseEditingNow != _isPoseEditing)
                {
                    _isPoseEditing = isPoseEditingNow;
                    onPoseEditingChanged?.Invoke(isPoseEditingNow);
                }
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