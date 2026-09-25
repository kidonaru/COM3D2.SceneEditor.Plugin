using System.Collections.Generic;
using System.IO;
using COM3D2.SceneEditor.Plugin;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    using SE = SceneEditor.Plugin;

    /// <summary>
    /// タイムライン (MTE 移植コード) からのメイド・編集状態アクセスを
    /// SceneEditor の各マネージャへ橋渡しする。
    /// MTE では StudioHackBase 派生をスタジオ種別ごとに切り替えていたが、
    /// SceneEditor 以外の実装は持たないので具象クラス 1 つに畳んでいる
    /// </summary>
    public sealed class SceneEditorHack
    {
        /// <summary>TimelineXml がモデルの pluginName を寄せる比較キー。既存 XML 互換のため固定</summary>
        public const string pluginName = "SceneEditor";

        /// <summary>タイトル画面のシーン名。タイムラインが動かない唯一の場面</summary>
        public const string titleSceneName = "SceneTitle";

        /// <summary>
        /// 今がタイトル画面か。_isSceneActive はシーン遷移の途中ではまだ前シーンの値なので、
        /// 遷移中に判定したい呼び出し元は現在のシーン名を直接見る
        /// </summary>
        public static bool isTitleScene
            => SceneManager.GetActiveScene().name == titleSceneName;

        private static SceneEditorHack _instance;
        private static bool _isSceneActive;

        /// <summary>
        /// Initialize 前とタイトル画面では null を返す。
        /// 呼び出し側の null ガードは「タイムラインが動く場面か」の判定として使われている
        /// </summary>
        public static SceneEditorHack instance => _isSceneActive ? _instance : null;

        /// <summary>
        /// ポーズ編集モード。instance の値をそのまま返す (キャッシュしない)。
        /// フレーム頭で同期するキャッシュを挟むと、同フレーム中に編集モードへ
        /// 入った直後の読み手が古い値を見て食い違う。
        /// instance が null (タイトル画面) のときは false 扱いで、書き込みは無視する
        /// </summary>
        public static bool isPoseEditing
        {
            get
            {
                var hack = instance;
                return hack != null && hack.isPoseEditingInternal;
            }
            set
            {
                var hack = instance;
                if (hack != null)
                {
                    hack.isPoseEditingInternal = value;
                }
            }
        }

        public static void Initialize()
        {
            if (_instance != null)
            {
                return;
            }
            _instance = new SceneEditorHack();

            // 登録がシーンロード後になるため、初期状態はアクティブ扱いにする
            _isSceneActive = true;

            // SceneEdit では photo mode の背景オブジェクト CSV が未ロードのため明示的に読み込む
            // (StudioModelManager の BGObjectIdMap / モデル生成が PhotoBGObjectData.data に依存する)
            if (PhotoBGObjectData.data == null)
            {
                PhotoBGObjectData.Create();
            }
        }

        public static void OnChangedSceneLevel(Scene scene, LoadSceneMode sceneMode)
        {
            _isSceneActive = scene.name != titleSceneName;
        }

        private SceneEditorHack()
        {
        }

        private static SE.MaidManipulateManager manipulateManager
            => SE.MaidManipulateManager.instance;

        private static MaidManager maidManager => MaidManager.instance;

        public Maid selectedMaid => manipulateManager.targetMaid;

        private readonly List<Maid> _allMaids = new List<Maid>();
        public List<Maid> allMaids
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

        public int selectedMaidSlotNo => allMaids.IndexOf(selectedMaid);

        public string outputAnmPath
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

        // 旧 StudioHackBase.isPoseEditing。外からは static isPoseEditing 経由で触る
        private bool isPoseEditingInternal
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

        public bool isAnmPlaying
        {
            get => maidManager.isAnmPlaying;
            set
            {
                if (value && isPoseEditingInternal)
                {
                    isPoseEditingInternal = false;
                }
                maidManager.isAnmPlaying = value;
            }
        }

        /// <summary>
        /// モーションの有効/無効。SE では再生 (PlayMotion) に写像している。
        /// 編集モード中の有効化は無視する: 再生すると speed=0 の anm を Unity が毎フレームサンプルし、
        /// LateUpdate の IK 固定と交互にボーンを書いてポーズがブレるため
        /// (モーション以外のレイヤーがアクティブだと TimelineManager.isMotionEditing 経由で true が来る)。
        /// シーク時のポーズ反映は MaidCache.motionSliderRate が停止中でも担う
        /// </summary>
        public bool isAnmEnabled
        {
            get
            {
                var maid = selectedMaid;
                return maid != null && !SE.MaidMotionState.IsMotionStopped(maid);
            }
            set
            {
                if (value && isPoseEditingInternal)
                {
                    return;
                }

                foreach (var maid in allMaids)
                {
                    if (value)
                    {
                        if (SE.MaidMotionState.IsMotionStopped(maid))
                        {
                            SE.MaidMotionState.PlayMotion(maid, resetSpeed: false);
                        }
                    }
                    else
                    {
                        SE.MaidMotionState.StopMotion(maid);
                    }
                }
            }
        }

        // 詳細は ApplyMuneYure を参照
        public bool useMuneKeyL
        {
            set => ApplyMuneYure(true, value);
        }

        public bool useMuneKeyR
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

        private static void DeleteBGObject()
        {
            BgMgr bgMgr = GameMain.Instance.BgMgr;
            UnityEngine.Object.Destroy(bgMgr.current_bg_object);
            bgMgr.DeleteBg();
        }

        public void ChangeBackground(string bgName)
        {
            if (bgName != GameMain.Instance.BgMgr.GetBGName())
            {
                DeleteBGObject();
                BackgroundUtils.ChangeBgByName(bgName);
            }
        }

        public void SetBackgroundVisible(bool visible)
        {
            var bgObject = GameMain.Instance.BgMgr.current_bg_object;
            if (bgObject != null)
            {
                bgObject.SetActive(visible);
            }
        }
    }
}
