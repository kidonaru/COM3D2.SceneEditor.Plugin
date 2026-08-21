using System.Collections.Generic;
using System.IO;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// モーションの再生・シークとポーズの保存/読込を行うウィンドウ。
    /// 一覧はスタジオモードのモーションウィンドウと同じカテゴリ分け (PhotoMotionData) で、
    /// マイポーズのみ編集向けの停止読込 (MaidPoseFileManager) を使う。
    /// ボーンごとの回転操作は Inspector のボーン表示が担う
    /// </summary>
    public class MaidPoseWindow : MaidWindowBase
    {
        public static readonly int WINDOW_ID = 8903354;

        protected override int windowId => WINDOW_ID;
        protected override string windowTitle => "モーション";

        /// <summary>選択中カテゴリ。マイポーズだけ読込経路が異なる</summary>
        private string _category = MaidPoseFileManager.MY_POSE_CATEGORY;

        private readonly GUIComboBox<string> _categoryComboBox = new GUIComboBox<string>
        {
            getName = (name, _) => name,
            buttonSize = new Vector2(150, 20),
            contentSize = new Vector2(150, 300),
        };

        /// <summary>カテゴリ一覧のキャッシュ。毎フレームの再構築を避ける</summary>
        private List<string> _categories = null;
        /// <summary>カテゴリ一覧を構築したときの対象が男か。対象が変わったら作り直す</summary>
        private bool _categoriesForMan;

        /// <summary>マイポーズ一覧。描画のたびに再列挙はせず、表示時と保存時に更新する</summary>
        private List<string> _poseFileNames = null;

        /// <summary>表示中サブディレクトリ直下のフォルダ一覧。_poseFileNames と同時に更新する</summary>
        private List<string> _poseDirNames = null;

        /// <summary>マイポーズの表示中サブディレクトリ (ルートは "")</summary>
        private string _myPoseDir = "";

        /// <summary>選択中カテゴリのモーション一覧。OnGUI は毎フレーム複数回走るため列挙し直さない</summary>
        private List<PhotoMotionData> _motions = null;

        private static MaidPoseWindow _instance = null;
        public static MaidPoseWindow instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new MaidPoseWindow();
                }
                return _instance;
            }
        }

        private MaidPoseWindow()
        {
        }

        protected override void LoadPlacement(out int x, out int y, out int width, out int height)
        {
            x = config.maidPosePosX;
            y = config.maidPosePosY;
            width = config.maidPoseWidth;
            height = config.maidPoseHeight;
        }

        protected override void StorePlacement(int x, int y, int width, int height)
        {
            config.maidPosePosX = x;
            config.maidPosePosY = y;
            config.maidPoseWidth = width;
            config.maidPoseHeight = height;
        }

        public override bool savedVisible
        {
            get => config.maidPoseVisible;
            set => config.maidPoseVisible = value;
        }

        /// <summary>
        /// 開いたときにマイポーズ一覧を取り直す。他所 (スタジオモード等) で
        /// 保存されたポーズも開き直せば一覧に反映される
        /// </summary>
        protected override void OnShowChanged(bool visible)
        {
            if (visible)
            {
                _poseFileNames = null;
                _poseDirNames = null;
            }
        }

        protected override void DrawMaidContent(Maid target)
        {
            if (target == null)
            {
                return;
            }

            // 退避中は表示に戻す際に上書きされるため操作させない
            if (!maidManager.IsVisible(target))
            {
                view.DrawLabel("非表示中はモーションを操作できません", -1, ROW_HEIGHT,
                    textColor: Color.yellow);
                return;
            }

            DrawPlaybackRows(view, target);
            view.DrawHorizontalLine();
            DrawCategoryRow(view, target);

            view.DrawHorizontalLine(Color.gray);
            view.AddSpace(5);

            view.BeginScrollView(-1, -1, GUIView.AutoScrollViewRect, false, true);
            if (_category == MaidPoseFileManager.MY_POSE_CATEGORY)
            {
                DrawMyPoseButtons(view, target);
            }
            else
            {
                DrawMotionButtons(view, target);
            }
            view.EndScrollView();
        }

        /// <summary>再生中クリップ名・再生/停止・リセット・保存の行と再生位置スライダー</summary>
        private void DrawPlaybackRows(GUIView view, Maid maid)
        {
            view.BeginHorizontal();
            {
                view.DrawLabel("再生中", LABEL_WIDTH, ROW_HEIGHT, style: GUIView.gsLabelRight);

                // 記録があれば表示名、無ければクリップ名から拡張子を除いて出す。
                // クリップ名は長いと 150px で右端が切れるため、短い表示名を優先する
                var appliedMotion = MaidMotionState.GetAppliedMotion(maid);
                var currentClipName = MaidMotionState.GetCurrentClipName(maid);
                var displayName = appliedMotion != null
                    ? appliedMotion.displayName
                    : (currentClipName != null
                        ? Path.GetFileNameWithoutExtension(currentClipName)
                        : null);
                view.DrawLabel(displayName ?? "なし", 150, ROW_HEIGHT);

                AddRightAlignSpace(view, 30);

                // 再生中は停止、停止中は再生と、状態に応じて 1 つのボタンを切り替える
                var isPlaying = MaidMotionState.IsPlaying(maid);
                if (view.DrawButton(isPlaying ? "■" : "▶", 30, ROW_HEIGHT,
                    enabled: isPlaying || MaidMotionState.CanPlayMotion(maid)))
                {
                    if (isPlaying)
                    {
                        MaidMotionState.StopMotion(maid);
                    }
                    else
                    {
                        MaidMotionState.PlayMotion(maid);
                    }
                }
            }
            view.EndLayout();

            // シークすると停止中でもポーズへ即反映される
            var animState = MaidMotionState.GetCurrentAnimationState(maid);
            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "再生位置",
                labelWidth = LABEL_WIDTH,
                width = -1,
                min = 0f,
                max = animState != null ? Mathf.Max(animState.length, 0.01f) : 1f,
                value = animState != null ? MaidMotionState.GetWrappedTime(animState) : 0f,
                hiddenResetButton = true,
                onChanged = value =>
                {
                    // 停止中のシークはポーズを確定的に変えるため記録する。
                    // ドラッグ中ほぼ毎フレーム走るため、骨格の走査は遅延評価版に任せる
                    HistoryManager.instance.BeforeEdit(maid, HistoryScope.Pose,
                        "再生位置", () => PoseSnapshot.GetAllBodyBones(maid));
                    MaidMotionState.SetPlaybackTime(maid, value);
                },
            });

            view.BeginHorizontal();
            {
                // 現在のポーズをマイポーズへ保存。名前はポップアップで入力させる。
                // 保存先は表示中サブディレクトリで、ポップアップの上書き判定と
                // 実保存が同じ場所を指すよう表示時点の値を控えて渡す
                if (view.DrawButton("ポーズ保存", 90, ROW_HEIGHT))
                {
                    var saveDir = _myPoseDir;
                    SavePosePopupWindow.Show(poseName => SavePose(maid, saveDir, poseName), saveDir);
                }

                // 保存先フォルダをエクスプローラーで開く
                if (view.DrawButton("開く", 50, ROW_HEIGHT))
                {
                    var folder = Path.Combine(MaidPoseFileManager.poseFolderPath, _myPoseDir);
                    Directory.CreateDirectory(folder);
                    MTEUtils.OpenDirectory(folder);
                }

                AddRightAlignSpace(view, 60);

                // 崩したポーズを停止前モーションで元に戻すリセット
                if (view.DrawButton("リセット", 60, ROW_HEIGHT,
                    enabled: MaidMotionState.IsMotionStopped(maid)))
                {
                    HistoryManager.instance.BeforeEdit(maid, HistoryScope.Pose,
                        "ポーズリセット", PoseSnapshot.GetAllBodyBones(maid));
                    MaidMotionState.ResetPose(maid);
                }
            }
            view.EndLayout();
        }

        /// <summary>
        /// 横並び行の残り幅ぶんの空白を挿入し、以降の要素を右端に寄せる。
        /// contentWidth には右揃えする要素の幅とその要素間 margin の合計を渡す
        /// </summary>
        private void AddRightAlignSpace(GUIView view, float contentWidth)
        {
            // viewRect はスクロールビュー中もコンテンツ幅を返す (GetDrawRect の auto-width と同じ式)。
            // 空白自身の後ろにも margin が入るためそのぶんも差し引く
            var space = view.viewRect.width - view.padding.x * 2
                - view.currentPos.x - view.margin - contentWidth;
            if (space > 0f)
            {
                view.AddSpace(space, ROW_HEIGHT);
            }
        }

        /// <summary>カテゴリ選択行。マイポーズ + スタジオモードのカテゴリ一覧</summary>
        private void DrawCategoryRow(GUIView view, Maid maid)
        {
            view.BeginHorizontal();
            {
                view.DrawLabel("カテゴリ", LABEL_WIDTH, ROW_HEIGHT, style: GUIView.gsLabelRight);

                if (_categories == null || _categoriesForMan != maid.boMAN)
                {
                    _categories = new List<string> { MaidPoseFileManager.MY_POSE_CATEGORY };
                    if (PhotoMotionUtils.EnsureMotionDataLoaded())
                    {
                        _categories.AddRange(PhotoMotionUtils.GetCategories(maid.boMAN));
                    }
                    _categoriesForMan = maid.boMAN;
                    // 対象が変わって消えたカテゴリを選択したままだと一覧が空になる
                    if (!_categories.Contains(_category))
                    {
                        _category = MaidPoseFileManager.MY_POSE_CATEGORY;
                    }
                    _motions = null;
                }

                _categoryComboBox.items = _categories;
                _categoryComboBox.currentIndex = Mathf.Max(0, _categories.IndexOf(_category));
                _categoryComboBox.onSelected = (name, _) =>
                {
                    _category = name;
                    _motions = null;
                };
                _categoryComboBox.DrawButton(view);
            }
            view.EndLayout();
        }

        /// <summary>
        /// スタジオモードのモーションをボタンで列挙し、押したら即適用する。
        /// 現在当たっているモーションは緑色で示す
        /// </summary>
        private void DrawMotionButtons(GUIView view, Maid maid)
        {
            if (_motions == null)
            {
                _motions = new List<PhotoMotionData>(
                    PhotoMotionUtils.GetMotions(_category, maid.boMAN));
            }

            if (_motions.Count == 0)
            {
                view.DrawLabel("このカテゴリにモーションはありません", -1, ROW_HEIGHT);
                return;
            }

            var appliedMotion = MaidMotionState.GetAppliedMotion(maid);
            var currentClipName = MaidMotionState.GetCurrentClipName(maid);

            foreach (var data in _motions)
            {
                // 記録があれば id で判定する (スクリプト経由エントリはクリップ名から特定できない)。
                // Mod の id はファイル内容の CRC で更新されると変わるため、
                // PhotoMotionUtils.Find と同じく direct_file でもフォールバックする。
                // 記録が無い場合は従来のクリップ名突き合わせに任せる
                var isCurrent = appliedMotion != null
                    ? appliedMotion.myPosePath == null
                        && (appliedMotion.motionId == data.id
                            || (!string.IsNullOrEmpty(appliedMotion.motionFile)
                                && string.Equals(appliedMotion.motionFile, data.direct_file,
                                    System.StringComparison.OrdinalIgnoreCase)))
                    : PhotoMotionUtils.IsCurrentMotion(data, currentClipName);
                if (view.DrawButton(data.name, -1, ROW_HEIGHT,
                    color: isCurrent ? (Color?)EditorSubWindow.ACCENT_COLOR : null))
                {
                    HistoryManager.instance.BeforeEdit(maid, HistoryScope.Pose,
                        "モーション: " + data.name, PoseSnapshot.GetAllBodyBones(maid));
                    PhotoMotionUtils.Apply(maid, data);
                }
            }

        }

        /// <summary>
        /// マイポーズをボタンで列挙し、押したら即読み込む (編集を続けられる停止状態)。
        /// 現在当たっているポーズは緑色で示す
        /// </summary>
        private void DrawMyPoseButtons(GUIView view, Maid maid)
        {
            if (_poseFileNames == null || _poseDirNames == null)
            {
                _poseFileNames = MaidPoseFileManager.GetPoseFileNames(_myPoseDir);
                _poseDirNames = MaidPoseFileManager.GetSubDirectoryNames(_myPoseDir);
            }

            // ボタンクリックの NavigateMyPoseDir がフィールドを null に戻すため、
            // このフレームは移動前の一覧をローカルに控えて描き切る (次フレームで再列挙される)
            var poseFileNames = _poseFileNames;
            var poseDirNames = _poseDirNames;
            var myPoseDir = _myPoseDir;

            // サブディレクトリ内では親へ戻るボタンを先頭に出す
            if (myPoseDir.Length > 0)
            {
                if (view.DrawButton("← " + myPoseDir, -1, ROW_HEIGHT))
                {
                    NavigateMyPoseDir(Path.GetDirectoryName(myPoseDir));
                }
            }

            foreach (var dirName in poseDirNames)
            {
                if (view.DrawButton(dirName + "/", -1, ROW_HEIGHT))
                {
                    NavigateMyPoseDir(Path.Combine(myPoseDir, dirName));
                }
            }

            if (poseFileNames.Count == 0 && poseDirNames.Count == 0)
            {
                view.DrawLabel("保存されたポーズはありません", -1, ROW_HEIGHT);
                return;
            }

            // クリップ名は "ポーズ名.anm" 形式なので拡張子を除いて突き合わせる (フォールバック用)
            var appliedMotion = MaidMotionState.GetAppliedMotion(maid);
            var currentClipName = MaidMotionState.GetCurrentClipName(maid);
            var currentPoseName = currentClipName != null
                ? Path.GetFileNameWithoutExtension(currentClipName)
                : null;

            foreach (var poseName in poseFileNames)
            {
                // 記録があれば相対パスで判定する (別フォルダの同名ポーズを誤ってハイライトしない)。
                // 記録が無い場合は従来どおりクリップ名 (ファイル名のみ) と突き合わせる
                var isCurrent = appliedMotion != null
                    ? string.Equals(appliedMotion.myPosePath, Path.Combine(myPoseDir, poseName),
                        System.StringComparison.OrdinalIgnoreCase)
                    : string.Equals(poseName, currentPoseName,
                        System.StringComparison.OrdinalIgnoreCase);
                if (view.DrawButton(poseName, -1, ROW_HEIGHT,
                    color: isCurrent ? (Color?)EditorSubWindow.ACCENT_COLOR : null))
                {
                    HistoryManager.instance.BeforeEdit(maid, HistoryScope.Pose,
                        "ポーズ読込: " + poseName, PoseSnapshot.GetAllBodyBones(maid));
                    MaidPoseFileManager.LoadPose(maid, Path.Combine(myPoseDir, poseName));
                }
            }
        }

        /// <summary>マイポーズの表示ディレクトリを移動し、一覧を取り直させる</summary>
        private void NavigateMyPoseDir(string subDir)
        {
            _myPoseDir = subDir ?? "";
            _poseFileNames = null;
            _poseDirNames = null;
        }

        /// <summary>
        /// ポップアップで確定した名前で保存し、一覧を更新する。
        /// 名前検証と上書き確認はポップアップ側で済んでいる
        /// </summary>
        private void SavePose(Maid maid, string subDir, string poseName)
        {
            // ポップアップ表示中に操作対象が変わっていたら、別メイドのポーズを保存しないよう中止する
            if (maidManager.targetMaid != maid)
            {
                DialogPopupWindow.ShowDialog("操作対象が変わったため保存を中止しました");
                return;
            }

            MaidPoseFileManager.SavePose(maid, Path.Combine(subDir, poseName));
            _poseFileNames = null;
            _poseDirNames = null;
        }
    }
}
