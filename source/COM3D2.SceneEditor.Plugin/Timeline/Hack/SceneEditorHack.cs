using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    using SE = SceneEditor.Plugin;

    /// <summary>
    /// SceneEditor 環境向けの StudioHack 実装。
    /// タイムライン (MTE 移植コード) からのメイド・編集状態アクセスを
    /// SceneEditor の各マネージャへ橋渡しする
    /// </summary>
    public class SceneEditorHack : StudioHackBase
    {
        public override string pluginName => "SceneEditor";
        public override int priority => 0;

        private static SE.MaidManipulateManager manipulateManager
            => SE.MaidManipulateManager.instance;

        public override Maid selectedMaid => manipulateManager.targetMaid;

        private readonly List<Maid> _allMaids = new List<Maid>();
        public override List<Maid> allMaids
        {
            get
            {
                // SceneEditor はスタジオ専用ではないため、シーン上でアクティブな
                // メイド全員を対象にする
                _allMaids.Clear();
                var characterMgr = GameMain.Instance.CharacterMgr;
                var count = characterMgr.GetMaidCount();
                for (var i = 0; i < count; i++)
                {
                    var maid = characterMgr.GetMaid(i);
                    if (maid != null && maid.isActiveAndEnabled)
                    {
                        _allMaids.Add(maid);
                    }
                }
                return _allMaids;
            }
        }

        public override int selectedMaidSlotNo => allMaids.IndexOf(selectedMaid);

        public override string outputAnmPath
        {
            get
            {
                var path = PhotoModePoseSave.folder_path;
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }
                return path;
            }
        }

        public override bool isPoseEditing
        {
            get => manipulateManager.isEditMode;
            set
            {
                if (value && isAnmPlaying)
                {
                    isAnmPlaying = false;
                }
                manipulateManager.isEditMode = value;
            }
        }

        public override bool isIKVisible
        {
            get => manipulateManager.isBoneVisible;
            set => manipulateManager.isBoneVisible = value;
        }

        public override bool isAnmEnabled
        {
            get
            {
                var maid = selectedMaid;
                return maid != null && !SE.MaidMotionState.IsMotionStopped(maid);
            }
            set
            {
                foreach (var maid in allMaids)
                {
                    if (value)
                    {
                        if (SE.MaidMotionState.IsMotionStopped(maid))
                        {
                            SE.MaidMotionState.PlayMotion(maid);
                        }
                    }
                    else
                    {
                        SE.MaidMotionState.StopMotion(maid);
                    }
                }
            }
        }

        // タイムライン側が再生時間を直接制御するため、スライダー同期は不要
        public override float motionSliderRate
        {
            set { }
        }

        public override bool useMuneKeyL
        {
            set { }
        }

        public override bool useMuneKeyR
        {
            set { }
        }

        public override Camera subCamera => null;

        public override bool isUIVisible
        {
            get => !SE.WindowManager.instance.isWindowsHidden;
            set => SE.WindowManager.instance.isWindowsHidden = !value;
        }

        public override bool Init()
        {
            // 登録がシーンロード後になるため、初期状態はアクティブ扱いにする
            isSceneActive = true;
            return true;
        }

        public override void OnChangedSceneLevel(Scene scene, LoadSceneMode sceneMode)
        {
            isSceneActive = scene.name != "SceneTitle";
        }

        public override bool IsValid()
        {
            _errorMessage = "";
            return true;
        }
    }
}
