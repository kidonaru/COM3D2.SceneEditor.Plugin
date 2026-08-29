using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
// Assembly-UnityScript-firstpass のグローバル名前空間には Unity 5 世代の DepthOfFieldScatter が
// 残骸として残っており、素の型名ではそちらに束縛されて Unity 2022 で削除された
// Graphics.DrawProceduralIndirect を呼んでしまう。ゲームが実際に使う PostEffects_Dummy 側へ束縛する
#if COM3D25
using DepthOfFieldEffect = PostEffects_Dummy.DepthOfFieldScatter;
#else
using DepthOfFieldEffect = global::DepthOfFieldScatter;
#endif

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

        /// <summary>ボーン/IK の表示。SE の「ボーン表示」トグルがそのまま実体</summary>
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

        // 詳細は ApplyMuneYure を参照
        public override bool useMuneKeyL
        {
            set => ApplyMuneYure(true, value);
        }

        public override bool useMuneKeyR
        {
            set => ApplyMuneYure(false, value);
        }

        /// <summary>
        /// 胸の揺れを呼出済みの全メイドへ反映する。
        /// useMuneKey* は「物理無効」なので、揺れの ON/OFF とは反転する。
        ///
        /// SE のトグルはメイド別だがタイムラインのフラグは全体設定のため、一括で書く。
        /// 逆方向 (SE のトグル → フラグ) は 1 体の操作で全体設定が動いてしまううえ、
        /// このフラグは胸ボーンをキー化できるかの判定 (TransformDataRotation) も
        /// 兼ねているため行わない。
        ///
        /// 対象は allMaids ではなく calledMaids。コントローラの記録は
        /// MaidManipulateManager の呼び出し管理と同じ寿命 (Release で破棄) を持つため、
        /// このプラグインが管理していないメイドの分を作らない
        /// </summary>
        private static void ApplyMuneYure(bool isLeft, bool useMuneKey)
        {
            var manager = manipulateManager;
            foreach (var maid in manager.calledMaids)
            {
                manager.muneYureController.SetYure(maid, isLeft, !useMuneKey);
            }
        }

        public override Camera subCamera => null;

        // SE のゲーム内メインカメラに DOF コンポーネントが無いケースへの保険として、
        // 基底の GetComponent (null あり得る) ではなく必要時に追加する
        public override DepthOfFieldEffect depthOfField
            => PluginUtils.MainCamera.gameObject.GetOrAddComponent<DepthOfFieldEffect>();

        public override bool isUIVisible
        {
            get => !SE.WindowManager.instance.isWindowsHidden;
            set => SE.WindowManager.instance.isWindowsHidden = !value;
        }

        public override bool Init()
        {
            // 登録がシーンロード後になるため、初期状態はアクティブ扱いにする
            isSceneActive = true;

            // SceneEdit では photo mode の背景オブジェクト CSV が未ロードのため明示的に読み込む
            // (StudioModelManager の BGObjectIdMap / モデル生成が PhotoBGObjectData.data に依存する)
            if (PhotoBGObjectData.data == null)
            {
                PhotoBGObjectData.Create();
            }
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

        // モデル配置は ModelPlacerProvider 経由の ExternalModelHack が持つ。
        // StudioHackBase.modelList が abstract のため、空リストを返す実装だけ残す
        private static readonly List<StudioModelStat> _emptyModelList = new List<StudioModelStat>();
        public override List<StudioModelStat> modelList => _emptyModelList;
    }
}
