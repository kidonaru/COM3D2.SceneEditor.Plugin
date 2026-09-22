using System;
using System.Collections.Generic;
using System.IO;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

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

        /// <summary>
        /// 一覧の絞り込み語。表示上のフィルタなので、前後送り (&lt; &gt;) の対象一覧には効かせない。
        /// マイポーズでは表示中フォルダ以下を再帰的に探し、結果をフラットに並べる
        /// </summary>
        private string _searchText = "";

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
        /// <summary>
        /// モーション一覧からカテゴリを取得できたか。
        /// ゲーム側の初期化が済む前は取得できず「マイポーズ」だけになるため、
        /// 取得できるまではキャッシュを確定させずに作り直す
        /// </summary>
        private bool _categoriesLoaded;

        /// <summary>マイポーズ一覧。描画のたびに再列挙はせず、表示時と保存時に更新する</summary>
        private List<string> _poseFileNames = null;

        /// <summary>表示中サブディレクトリ直下のフォルダ一覧。_poseFileNames と同時に更新する</summary>
        private List<string> _poseDirNames = null;

        /// <summary>
        /// 検索用の再帰列挙結果 (表示中サブディレクトリからの相対パス)。
        /// 全階層を走査するため、検索欄に入力があるときだけ作って _poseFileNames と同時に捨てる
        /// </summary>
        private List<string> _posePathsRecursive = null;

        /// <summary>マイポーズの表示中サブディレクトリ (ルートは "")</summary>
        private string _myPoseDir = "";

        /// <summary>選択中カテゴリのモーション一覧。OnGUI は毎フレーム複数回走るため列挙し直さない</summary>
        private List<PhotoMotionData> _motions = null;

        // 前後送り (< >) の送り先。再生中エントリが属する一覧を表示中カテゴリとは独立に解決して控える

        /// <summary>送り先がモーションの場合の一覧 (マイポーズの場合は null)</summary>
        private List<PhotoMotionData> _navMotions = null;

        /// <summary>送り先がマイポーズの場合の一覧 (モーションの場合は null)</summary>
        private List<string> _navPoseNames = null;

        /// <summary>_navPoseNames と対で入る保存フォルダ (モーションの場合は null)</summary>
        private string _navPoseDir = null;

        /// <summary>送り先一覧における再生中エントリの位置。解決できていなければ -1</summary>
        private int _navIndex = -1;

        /// <summary>送り先を解決したときの再生中エントリと対象メイド。変わったら解決し直す</summary>
        private string _navKey = null;
        private Maid _navMaid = null;

        /// <summary>
        /// レイヤー行の表示名の控え。一覧の引き当ては全件走査なので、
        /// 段のアニメ名が変わったときだけ解決し直す
        /// </summary>
        private string _layerDisplayAnmName = null;
        private string _layerDisplayName = null;

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

        public override bool TryFocusTimelineLayer(Type layerType)
        {
            // 指・IK・ボーン編集も同じモーションレイヤーへ記録するので、
            // 前面へ出す代表ウィンドウ (モーション / IK) は設定で選ぶ
            return MotionLayerFocusUtils.ShouldFocus(layerType, MTEP.MotionLayerFocusWindow.Motion);
        }

        /// <summary>
        /// 開いたときにマイポーズ一覧を取り直す。他所 (スタジオモード等) で
        /// 保存されたポーズも開き直せば一覧に反映される
        /// </summary>
        protected override void OnShowChanged(bool visible)
        {
            if (visible)
            {
                InvalidateMyPoseLists();
                InvalidateNavEntries();
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

            // 直前のガードと違い、レイヤー未登録では return しない（項目は見せたまま無効化する）
            TimelineLayerGate.Begin(view, typeof(MTEP.MotionTimelineLayer), target, ROW_HEIGHT);

            DrawTargetSection(view, target);
            DrawCategoryRow(view, target);
            view.DrawTextField("検索", LABEL_WIDTH, _searchText, -1, ROW_HEIGHT,
                value => _searchText = value);

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

        /// <summary>
        /// 適用先のエントリ名・前後送り・再生/停止の行。
        /// ベースならベースモーション、レイヤーならその段を映す
        /// </summary>
        private void DrawTargetRow(GUIView view, Maid maid, AnimationLayerInfo layerInfo,
            bool isLayerTarget)
        {
            var appliedMotion = MaidMotionState.GetAppliedMotion(maid);
            var currentClipName = MaidMotionState.GetCurrentClipName(maid);
            // レイヤー選択中は必ず非 null (未設定の段は空文字)。
            // ベース選択中だけ null で、以降の分岐はこの使い分けで見分ける
            var layerAnmName = isLayerTarget
                ? (layerInfo != null ? layerInfo.anmName ?? "" : "")
                : null;

            view.BeginHorizontal();
            {
                view.DrawLabel(isLayerTarget ? "レイヤー" + MaidAnimationBlendController.GetSelectedLayer(maid) : "再生中",
                    LABEL_WIDTH, ROW_HEIGHT, style: GUIView.gsLabelRight);

                // 記録があれば表示名、無ければクリップ名から拡張子を除いて出す。
                // クリップ名は長いと 150px で右端が切れるため、短い表示名を優先する
                var displayName = isLayerTarget
                    ? GetLayerDisplayName(layerAnmName)
                    : (appliedMotion != null
                        ? appliedMotion.displayName
                        : (currentClipName != null
                            ? Path.GetFileNameWithoutExtension(currentClipName)
                            : null));
                // 一覧を開かずに前後のエントリへ送れるようにする。送り先は適用先に
                // 載っているエントリが属する一覧で、カテゴリ選択とは独立に解決する。
                // どの一覧にも無いもの (シーンプリセットのポーズ等) は
                // 現在位置が定まらないため無効
                EnsureNavEntries(maid, appliedMotion, currentClipName, layerAnmName);
                // 1 件しかない一覧では送り先が自分自身になるだけなので無効にする
                var canNavigate = _navIndex >= 0 && GetNavCount() > 1;

                if (view.DrawButton("<", 25, ROW_HEIGHT, enabled: canNavigate))
                {
                    ApplyNavEntry(maid, -1);
                }

                view.DrawLabel(displayName ?? "なし", 150, ROW_HEIGHT);

                if (view.DrawButton(">", 25, ROW_HEIGHT, enabled: canNavigate))
                {
                    ApplyNavEntry(maid, 1);
                }

                view.AddRightAlignSpace(
                    isLayerTarget ? 30 + 50 + view.margin : 30, ROW_HEIGHT);

                // 再生中は停止、停止中は再生と、状態に応じて 1 つのボタンを切り替える。
                // 適用先に関わらずベースと全レイヤーをまとめて動かす
                // (PlayMotion の ResumeAfterPlay と anim.Stop() が層も一緒に扱う)
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
                        // 再生するなら編集モードは畳む
                        // (止めたポーズを基準にする編集と再生は両立しない)
                        AutoEditMode.Exit();
                        MaidMotionState.PlayMotion(maid);
                    }
                }

                if (isLayerTarget)
                {
                    var hasState = layerInfo != null && layerInfo.state != null
                        && !string.IsNullOrEmpty(layerInfo.anmName);
                    if (view.DrawButton("削除", 50, ROW_HEIGHT, enabled: hasState))
                    {
                        var layer = MaidAnimationBlendController.GetSelectedLayer(maid);
                        AutoEditMode.Enter();
                        HistoryManager.instance.BeforeEdit(maid, HistoryScope.Pose,
                            "ブレンド削除: レイヤー" + layer,
                            () => PoseSnapshot.GetAllBodyBones(maid));
                        MaidAnimationBlendController.RemoveLayer(maid, layer);
                    }
                }
            }
            view.EndLayout();
        }

        /// <summary>ベース専用の行。再生位置のシークと、ポーズの保存・反転・リセット</summary>
        private void DrawBaseRows(GUIView view, Maid maid)
        {
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

                view.AddRightAlignSpace(60 + 60 + view.margin, ROW_HEIGHT);

                // 現在のポーズを左右反転する。再生中は書き戻しが翌フレームに
                // 上書きされるため、他の編集操作と同じく先に停止させる
                // (停止操作を内包するので、リセットと違い再生中でも押せる)
                if (view.DrawButton("反転", 60, ROW_HEIGHT))
                {
                    MaidMotionState.StopMotion(maid);
                    HistoryManager.instance.BeforeEdit(maid, HistoryScope.Pose,
                        "ポーズ反転", PoseSnapshot.GetAllBodyBones(maid));
                    MaidPoseFlipper.Flip(maid);
                    MaidAnimationBlendController.MarkBoneEdit(maid);

                    // IK 固定も左右を入れ替える。ポーズの書き戻し後に記録するのは、
                    // 先に別スコープの BeforeEdit を挟むとポーズ側が変更前のまま確定してしまうため
                    // (IK 固定を使っていなければ変化なしとして履歴には積まれない)
                    HistoryManager.instance.BeforeEdit(maid, HistoryScope.IK, "IK固定反転");
                    maidManager.ikHoldController.FlipHolds(maid);
                }

                // 崩したポーズを復帰先 (停止前のモーション / 読み込んだポーズ) で元に戻すリセット
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
        /// ベースにスクリプト経由のモーション (エディット系) が当たっているか。
        /// direct_file を持たないエントリがそれで、ブレンド層へも載せられない
        /// </summary>
        private static bool IsScriptMotionApplied(Maid maid)
        {
            var applied = MaidMotionState.GetAppliedMotion(maid);
            return applied != null
                && !applied.isResidentPose
                && applied.myPosePath == null
                && string.IsNullOrEmpty(applied.motionFile);
        }

        /// <summary>
        /// 一覧のハイライト基準。適用先がレイヤーならその段に載っているアニメを基準にする
        /// (押した先がその段なので、当たっている印もその段に合わせる)
        /// </summary>
        private void GetHighlightSource(Maid maid,
            out MaidMotionState.AppliedMotionInfo appliedMotion, out string clipName)
        {
            appliedMotion = MaidMotionState.GetAppliedMotion(maid);
            clipName = MaidMotionState.GetCurrentClipName(maid);
            if (MaidAnimationBlendController.GetSelectedLayer(maid) == MaidPoseBlendRows.BaseLayer)
            {
                return;
            }

            // 層は anmName しか持たないので、ファイル名で突き合わせる
            appliedMotion = null;
            var info = GetTargetLayerInfo(maid);
            var anmName = info != null ? info.anmName : null;
            clipName = string.IsNullOrEmpty(anmName) ? null : Path.GetFileName(anmName);
        }

        /// <summary>適用先レイヤーの情報。適用先がベースなら null</summary>
        private AnimationLayerInfo GetTargetLayerInfo(Maid maid)
        {
            var layer = MaidAnimationBlendController.GetSelectedLayer(maid);
            if (layer == MaidPoseBlendRows.BaseLayer)
            {
                return null;
            }
            return MaidAnimationBlendController.GetLayerInfo(maid, layer);
        }

        /// <summary>
        /// 前後送りの対象一覧を用意する。適用先に載っているエントリが属する一覧を、
        /// 表示中カテゴリとは独立に解決する
        /// (別カテゴリのモーションや別フォルダのマイポーズでも送れるように)。
        /// 全カテゴリ走査になるため、基準のエントリが変わったときだけ解決し直す
        /// (見つからなかった結果も控えて毎フレームの再走査を避ける)。
        /// layerAnmName が非 null (適用先がレイヤー) のときは appliedMotion と
        /// currentClipName を使わず、その段のアニメ名から基準を取り直す
        /// </summary>
        private void EnsureNavEntries(Maid maid, MaidMotionState.AppliedMotionInfo appliedMotion,
            string currentClipName, string layerAnmName)
        {
            // 適用先がレイヤーなら、その段に載っているアニメを基準にする。
            // ベース基準のままだと送り先が毎回同じになってしまう
            // (層へ載せてもベースは変わらないため位置が進まない)
            string posePathHint = null;
            if (layerAnmName != null)
            {
                appliedMotion = null;
                currentClipName = null;

                if (layerAnmName.Length > 0)
                {
                    // 層が持つのは anmName だけなので、公式・Mod はファイル名で、
                    // マイポーズは保存フォルダからの相対パスで突き合わせる
                    currentClipName = Path.GetFileName(layerAnmName);
                    posePathHint = Path.Combine(
                        Path.GetDirectoryName(layerAnmName) ?? "",
                        Path.GetFileNameWithoutExtension(layerAnmName));
                }
            }

            // 適用先ごとに基準が変わるので、キーにも含めて切替時に解決し直す
            var navKey = MaidAnimationBlendController.GetSelectedLayer(maid) + "/" + GetNavKey(appliedMotion, currentClipName);
            if (_navMaid == maid && _navKey == navKey)
            {
                return;
            }

            _navMaid = maid;
            _navKey = navKey;
            _navMotions = null;
            _navPoseNames = null;
            _navPoseDir = null;
            _navIndex = -1;
            if (GetNavKey(appliedMotion, currentClipName) == null)
            {
                return;
            }

            if (appliedMotion != null && appliedMotion.myPosePath != null)
            {
                ResolveNavPose(appliedMotion.myPosePath);
                return;
            }

            ResolveNavMotion(maid, appliedMotion, currentClipName);
            if (_navIndex < 0 && posePathHint != null)
            {
                // 層には AppliedMotionInfo が無くマイポーズか公式かを区別できないため、
                // モーション一覧で見つからなければマイポーズとして解決し直す
                ResolveNavPose(posePathHint);
            }
        }

        /// <summary>
        /// 再生中エントリの識別子。これが変わったときだけ送り先一覧を解決し直す。
        /// 記録が無いエントリはクリップ名で代用する
        /// </summary>
        private static string GetNavKey(MaidMotionState.AppliedMotionInfo appliedMotion,
            string currentClipName)
        {
            if (appliedMotion != null)
            {
                if (appliedMotion.myPosePath != null)
                {
                    return "pose:" + appliedMotion.myPosePath;
                }
                return "motion:" + appliedMotion.motionId + ":" + appliedMotion.motionFile;
            }
            return currentClipName != null ? "clip:" + currentClipName : null;
        }

        /// <summary>基準のマイポーズが属するフォルダを送り先一覧にする</summary>
        private void ResolveNavPose(string myPosePath)
        {
            var poseDir = Path.GetDirectoryName(myPosePath) ?? "";
            var poseNames = MaidPoseFileManager.GetPoseFileNames(poseDir);
            for (var i = 0; i < poseNames.Count; i++)
            {
                // 一覧のハイライトと同じ突き合わせで位置を決める (フォルダは送り先に含めない)
                if (string.Equals(myPosePath, Path.Combine(poseDir, poseNames[i]),
                    System.StringComparison.OrdinalIgnoreCase))
                {
                    _navPoseNames = poseNames;
                    _navPoseDir = poseDir;
                    _navIndex = i;
                    return;
                }
            }
        }

        /// <summary>再生中モーションが属するカテゴリを全カテゴリから探して送り先一覧にする</summary>
        private void ResolveNavMotion(Maid maid, MaidMotionState.AppliedMotionInfo appliedMotion,
            string currentClipName)
        {
            if (!PhotoMotionUtils.EnsureMotionDataLoaded())
            {
                return;
            }

            foreach (var category in PhotoMotionUtils.GetCategories(maid.boMAN))
            {
                // 該当カテゴリだけリスト化する (全カテゴリぶんの確保を避ける)
                var index = 0;
                foreach (var data in PhotoMotionUtils.GetMotions(category, maid.boMAN))
                {
                    if (IsCurrentMotionEntry(appliedMotion, currentClipName, data))
                    {
                        _navMotions = new List<PhotoMotionData>(
                            PhotoMotionUtils.GetMotions(category, maid.boMAN));
                        _navIndex = index;
                        return;
                    }
                    index++;
                }
            }
        }

        /// <summary>送り先がマイポーズ側の一覧か。モーション側との分岐はここに集約する</summary>
        private bool isPoseNav => _navPoseNames != null;

        /// <summary>送り先一覧の件数。解決できていなければ 0</summary>
        private int GetNavCount()
        {
            if (isPoseNav)
            {
                return _navPoseNames.Count;
            }
            return _navMotions != null ? _navMotions.Count : 0;
        }

        /// <summary>
        /// 送り先一覧の delta 個先を適用先へ適用する。一覧の端は反対側へ折り返す。
        /// 適用で基準のエントリが変わるため、位置は次フレームの解決で取り直される
        /// </summary>
        private void ApplyNavEntry(Maid maid, int delta)
        {
            var count = GetNavCount();
            if (count <= 0 || _navIndex < 0)
            {
                return;
            }

            // 負の剰余があるため 2 段階で正の範囲へ寄せる
            var index = ((_navIndex + delta) % count + count) % count;
            if (isPoseNav)
            {
                LoadMyPoseEntryToTarget(maid, _navPoseDir, _navPoseNames[index]);
                return;
            }
            ApplyMotionEntryToTarget(maid, _navMotions[index]);
        }

        /// <summary>
        /// カテゴリ一覧のキャッシュを更新する。
        /// モーション一覧を取得できなかったフレームは「取得済み」にせず、
        /// 次の描画で作り直す (確定させると「マイポーズ」だけの一覧が以後ずっと残る)
        /// </summary>
        private void UpdateCategories(Maid maid)
        {
            if (_categoriesLoaded && _categoriesForMan == maid.boMAN)
            {
                return;
            }

            _categoriesLoaded = PhotoMotionUtils.EnsureMotionDataLoaded();
            if (!_categoriesLoaded && _categories != null && _categoriesForMan == maid.boMAN)
            {
                // 取得できないままなので前回と同じ一覧になる。描画のたびに作り直さない
                return;
            }

            _categories = new List<string> { MaidPoseFileManager.MY_POSE_CATEGORY };
            if (_categoriesLoaded)
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

        /// <summary>カテゴリ選択行。マイポーズ + スタジオモードのカテゴリ一覧</summary>
        private void DrawCategoryRow(GUIView view, Maid maid)
        {
            view.BeginHorizontal();
            {
                view.DrawLabel("カテゴリ", LABEL_WIDTH, ROW_HEIGHT, style: GUIView.gsLabelRight);

                UpdateCategories(maid);

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

            MaidMotionState.AppliedMotionInfo appliedMotion;
            string currentClipName;
            GetHighlightSource(maid, out appliedMotion, out currentClipName);
            // 適用先がレイヤーのときは、スクリプト経由 (エディット系) を選ばせない。
            // 載せられないうえ、ベースへ当たるとアニメレイヤーごと止められてしまう
            var layerTarget = MaidAnimationBlendController.GetSelectedLayer(maid) != MaidPoseBlendRows.BaseLayer;

            var matched = 0;
            foreach (var data in _motions)
            {
                if (!MatchesSearch(data.name))
                {
                    continue;
                }
                matched++;

                var isCurrent = IsCurrentMotionEntry(appliedMotion, currentClipName, data);
                var canApply = !layerTarget || !string.IsNullOrEmpty(data.direct_file);
                if (view.DrawButton(data.name, -1, ROW_HEIGHT,
                    enabled: canApply,
                    color: isCurrent ? (Color?)EditorSubWindow.ACCENT_COLOR : null))
                {
                    ApplyMotionEntryToTarget(maid, data);
                }
            }

            if (matched == 0)
            {
                view.DrawLabel("検索に一致するモーションはありません", -1, ROW_HEIGHT);
            }
        }

        /// <summary>検索語に部分一致するか。空欄なら全件を通す</summary>
        private bool MatchesSearch(string name)
        {
            return string.IsNullOrEmpty(_searchText) || name.IndexOf(_searchText,
                System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// 一覧のモーションが今当たっているものか。
        /// 記録があれば id で判定する (スクリプト経由エントリはクリップ名から特定できない)。
        /// Mod の id はファイル内容の CRC で更新されると変わるため、
        /// PhotoMotionUtils.Find と同じく direct_file でもフォールバックする。
        /// 記録が無い場合は従来のクリップ名突き合わせに任せる
        /// </summary>
        private static bool IsCurrentMotionEntry(MaidMotionState.AppliedMotionInfo appliedMotion,
            string currentClipName, PhotoMotionData data)
        {
            if (appliedMotion == null)
            {
                return PhotoMotionUtils.IsCurrentMotion(data, currentClipName);
            }

            // id 0 はシーンプリセット復元など「一覧のどのエントリでもない」記録の既定値。
            // 一致扱いにすると前後送りが無関係なモーションを掴みうるため除外する
            var matchesId = appliedMotion.motionId != 0 && appliedMotion.motionId == data.id;
            var matchesFile = !string.IsNullOrEmpty(appliedMotion.motionFile)
                && string.Equals(appliedMotion.motionFile, data.direct_file,
                    System.StringComparison.OrdinalIgnoreCase);
            return appliedMotion.myPosePath == null && (matchesId || matchesFile);
        }

        /// <summary>
        /// 適用先タブと、選んだ対象の操作。
        /// ベースならモーションの再生・シークとポーズ操作、
        /// レイヤーならその段の再生と重み等を描く。
        /// レイヤー側はゲートをアニメレイヤー (AnimationTimelineLayer) へ張り直し、
        /// 描き終えたらモーションレイヤーのゲートへ戻す
        /// (以降の一覧は従来どおりモーションレイヤーの範囲)
        /// </summary>
        private void DrawTargetSection(GUIView view, Maid maid)
        {
            MaidAnimationBlendController.SyncFromAnimation(maid);

            // エディット系はスクリプト再生で、スクリプトが冒頭で
            // @MotionLayerStop range=2-5 を走らせてアニメレイヤーを止めてしまう。
            // 併用できないので適用先そのものを出さない
            if (IsScriptMotionApplied(maid))
            {
                MaidAnimationBlendController.SetSelectedLayer(maid, MaidPoseBlendRows.BaseLayer);
                view.DrawLabel("エディット系はブレンド非対応", -1, ROW_HEIGHT,
                    textColor: Color.yellow);
            }
            else if (!MaidPoseBlendRows.DrawTargetTabs(view, maid,
                MaidAnimationBlendController.GetSelectedLayer(maid),
                layer => MaidAnimationBlendController.SetSelectedLayer(maid, layer),
                ROW_HEIGHT, LABEL_WIDTH))
            {
                // レイヤー情報が取れない (呼出直後など) ときはベースだけ操作させる
                MaidAnimationBlendController.SetSelectedLayer(maid, MaidPoseBlendRows.BaseLayer);
            }

            var isLayerTarget = MaidAnimationBlendController.GetSelectedLayer(maid) != MaidPoseBlendRows.BaseLayer;
            if (!isLayerTarget)
            {
                DrawTargetRow(view, maid, null, false);
                DrawBaseRows(view, maid);
                view.DrawHorizontalLine();
                return;
            }

            TimelineLayerGate.End(view);
            var gateState = TimelineLayerGate.Begin(
                view, typeof(MTEP.AnimationTimelineLayer), maid, ROW_HEIGHT);

            var layerInfo = GetTargetLayerInfo(maid);
            DrawTargetRow(view, maid, layerInfo, true);
            MaidPoseBlendRows.DrawLayerValues(view, maid, layerInfo, ROW_HEIGHT, LABEL_WIDTH,
                gateState == TimelineLayerGateState.Ready);

            TimelineLayerGate.End(view);
            TimelineLayerGate.Begin(view, typeof(MTEP.MotionTimelineLayer), maid, ROW_HEIGHT);

            view.DrawHorizontalLine();
        }

        /// <summary>
        /// 一覧クリックと前後送りの適用。
        /// 適用先が「ベース」ならベースを差し替え、レイヤーならブレンド層へ載せる
        /// </summary>
        private void ApplyMotionEntryToTarget(Maid maid, PhotoMotionData data)
        {
            var layer = MaidAnimationBlendController.GetSelectedLayer(maid);
            if (layer == MaidPoseBlendRows.BaseLayer)
            {
                ApplyMotionEntry(maid, data);
                return;
            }

            AutoEditMode.Enter();
            HistoryManager.instance.BeforeEdit(maid, HistoryScope.Pose,
                "ブレンド適用: " + data.name, PoseSnapshot.GetAllBodyBones(maid));
            ShowBlendApplyError(
                MaidAnimationBlendController.ApplyMotion(maid, layer, data),
                "このモーションはブレンドできません (スクリプト経由か読み込みに失敗)");
        }

        /// <summary>マイポーズ版の適用先振り分け。ApplyMotionEntryToTarget と同じ分岐</summary>
        private void LoadMyPoseEntryToTarget(Maid maid, string myPoseDir, string poseName)
        {
            var layer = MaidAnimationBlendController.GetSelectedLayer(maid);
            if (layer == MaidPoseBlendRows.BaseLayer)
            {
                LoadMyPoseEntry(maid, myPoseDir, poseName);
                return;
            }

            AutoEditMode.Enter();
            HistoryManager.instance.BeforeEdit(maid, HistoryScope.Pose,
                "ブレンド適用: " + poseName, PoseSnapshot.GetAllBodyBones(maid));
            ShowBlendApplyError(
                MaidAnimationBlendController.ApplyMyPose(maid, layer,
                    Path.Combine(myPoseDir, poseName)),
                "ポーズの読み込みに失敗しました");
        }

        /// <summary>層への適用が通らなかったときだけ理由を出す</summary>
        private static void ShowBlendApplyError(
            BlendApplyResult result, string notBlendableMessage)
        {
            if (result == BlendApplyResult.SameAsBase)
            {
                DialogPopupWindow.ShowDialog(
                    "ベースと同じモーションはレイヤーへ載せられません");
                return;
            }
            if (result == BlendApplyResult.NotBlendable)
            {
                DialogPopupWindow.ShowDialog(notBlendableMessage);
            }
        }

        /// <summary>履歴を残してモーションを適用する</summary>
        private static void ApplyMotionEntry(Maid maid, PhotoMotionData data)
        {
            // BeforeEdit が AutoEditMode.Enter で編集モードへ入れてしまうので、その前に控える
            var wasEditMode = MaidManipulateManager.instance.isEditMode;
            HistoryManager.instance.BeforeEdit(maid, HistoryScope.Pose,
                "モーション: " + data.name, PoseSnapshot.GetAllBodyBones(maid));
            if (!PhotoMotionUtils.Apply(maid, data))
            {
                // 当たっていないので停止もしない (無関係な再生中モーションを止めてしまう)
                return;
            }
            if (wasEditMode)
            {
                // 編集モード中の差し替えは止めたまま当てる (▶ で再生できる)
                MaidMotionState.StopAtStart(maid);
            }
        }

        /// <summary>
        /// マイポーズをボタンで列挙し、押したら即読み込む (編集を続けられる停止状態)。
        /// 現在当たっているポーズは緑色で示す
        /// </summary>
        private void DrawMyPoseButtons(GUIView view, Maid maid)
        {
            // 一覧の解決はボタンを 1 つも描く前に済ませる。フォルダ移動ボタンのクリックで
            // NavigateMyPoseDir が _myPoseDir を書き換えてキャッシュを捨てるため、後から
            // 解決すると移動前フォルダの内容をキャッシュへ書き戻してしまう。
            // このフレームは移動前の一覧をローカルに控えて描き切る (次フレームで再列挙される)
            var myPoseDir = _myPoseDir;
            var searching = !string.IsNullOrEmpty(_searchText);

            List<string> poseFileNames = null;
            List<string> poseDirNames = null;
            if (searching)
            {
                if (_posePathsRecursive == null)
                {
                    _posePathsRecursive = MaidPoseFileManager.GetPoseFileNamesRecursive(myPoseDir);
                }
                poseFileNames = _posePathsRecursive;
            }
            else
            {
                if (_poseFileNames == null || _poseDirNames == null)
                {
                    _poseFileNames = MaidPoseFileManager.GetPoseFileNames(myPoseDir);
                    _poseDirNames = MaidPoseFileManager.GetSubDirectoryNames(myPoseDir);
                }
                poseFileNames = _poseFileNames;
                poseDirNames = _poseDirNames;
            }

            // サブディレクトリ内では親へ戻るボタンを先頭に出す。
            // 検索中も出しておくと、一致が無いときに探す範囲を広げられる
            if (myPoseDir.Length > 0)
            {
                if (view.DrawButton("← " + myPoseDir, -1, ROW_HEIGHT))
                {
                    NavigateMyPoseDir(Path.GetDirectoryName(myPoseDir));
                }
            }

            if (searching)
            {
                // 検索中はサブフォルダの中身も結果に展開済みなので、フォルダボタンは出さない
                if (DrawMyPoseEntries(view, maid, myPoseDir, poseFileNames) == 0)
                {
                    view.DrawLabel("検索に一致するポーズはありません", -1, ROW_HEIGHT);
                }
                return;
            }

            if (poseFileNames.Count == 0 && poseDirNames.Count == 0)
            {
                view.DrawLabel("保存されたポーズはありません", -1, ROW_HEIGHT);
                return;
            }

            foreach (var dirName in poseDirNames)
            {
                if (view.DrawButton(dirName + "/", -1, ROW_HEIGHT))
                {
                    NavigateMyPoseDir(Path.Combine(myPoseDir, dirName));
                }
            }

            DrawMyPoseEntries(view, maid, myPoseDir, poseFileNames);
        }

        /// <summary>
        /// ポーズをボタンで並べ、描いた件数を返す。
        /// poseNames は myPoseDir からの相対パス (直下なら名前そのもの)。
        /// 検索していないときは MatchesSearch が全件を通すため、絞り込みの有無は呼び分けない
        /// </summary>
        private int DrawMyPoseEntries(GUIView view, Maid maid, string myPoseDir,
            List<string> poseNames)
        {
            // クリップ名は "ポーズ名.anm" 形式なので拡張子を除いて突き合わせる (フォールバック用)
            MaidMotionState.AppliedMotionInfo appliedMotion;
            string currentClipName;
            GetHighlightSource(maid, out appliedMotion, out currentClipName);
            var currentPoseName = currentClipName != null
                ? Path.GetFileNameWithoutExtension(currentClipName)
                : null;

            var drawn = 0;
            foreach (var poseName in poseNames)
            {
                if (!MatchesSearch(poseName))
                {
                    continue;
                }
                drawn++;

                var isCurrent = IsCurrentPoseEntry(appliedMotion, currentPoseName, myPoseDir, poseName);
                if (view.DrawButton(poseName, -1, ROW_HEIGHT,
                    color: isCurrent ? (Color?)EditorSubWindow.ACCENT_COLOR : null))
                {
                    LoadMyPoseEntryToTarget(maid, myPoseDir, poseName);
                }
            }
            return drawn;
        }

        /// <summary>
        /// 一覧のマイポーズが今当たっているものか。
        /// 記録があれば相対パスで判定する (別フォルダの同名ポーズを誤ってハイライトしない)。
        /// 記録が無い場合は従来どおりクリップ名 (ファイル名のみ) と突き合わせる
        /// </summary>
        private static bool IsCurrentPoseEntry(MaidMotionState.AppliedMotionInfo appliedMotion,
            string currentPoseName, string myPoseDir, string poseName)
        {
            if (appliedMotion != null)
            {
                return string.Equals(appliedMotion.myPosePath, Path.Combine(myPoseDir, poseName),
                    System.StringComparison.OrdinalIgnoreCase);
            }
            // poseName は相対パスのこともあるため、クリップ名と比べる側はファイル名だけにする
            return string.Equals(Path.GetFileName(poseName), currentPoseName,
                System.StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>履歴を残してマイポーズを読み込む。フォルダは呼び出し元が控えたものを渡す</summary>
        private static void LoadMyPoseEntry(Maid maid, string myPoseDir, string poseName)
        {
            // BeforeEdit が AutoEditMode.Enter で編集モードへ入れてしまうので、その前に控える
            var wasEditMode = MaidManipulateManager.instance.isEditMode;
            HistoryManager.instance.BeforeEdit(maid, HistoryScope.Pose,
                "ポーズ読込: " + poseName, PoseSnapshot.GetAllBodyBones(maid));
            MaidPoseFileManager.LoadPose(maid, Path.Combine(myPoseDir, poseName),
                startPlaying: !wasEditMode);
        }

        /// <summary>マイポーズ一覧のキャッシュを捨て、次の描画で取り直させる</summary>
        private void InvalidateMyPoseLists()
        {
            _poseFileNames = null;
            _poseDirNames = null;
            _posePathsRecursive = null;
        }

        /// <summary>マイポーズの表示ディレクトリを移動し、一覧を取り直させる</summary>
        private void NavigateMyPoseDir(string subDir)
        {
            _myPoseDir = subDir ?? "";
            InvalidateMyPoseLists();
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
            InvalidateMyPoseLists();
            InvalidateNavEntries();
        }

        /// <summary>
        /// 送り先一覧を解決し直させる。ファイルの増減 (保存・ウィンドウの開き直し) は
        /// 再生中エントリが変わらないため、識別子だけでは検知できない
        /// </summary>
        private void InvalidateNavEntries()
        {
            _navMaid = null;
            _navKey = null;
        }

        /// <summary>
        /// レイヤー行に出す名前。層が持つのは anmName (ファイル名や絶対パス) だけなので、
        /// 一覧のモーションならベース側と同じ表示名 (PhotoMotionData.name) へ引き直す。
        /// 一覧に無いもの (マイポーズ等) はファイル名から拡張子を除いて出す。
        /// 空文字 (未設定の段) は null
        /// </summary>
        private string GetLayerDisplayName(string layerAnmName)
        {
            if (string.IsNullOrEmpty(layerAnmName))
            {
                return null;
            }
            if (_layerDisplayAnmName == layerAnmName)
            {
                return _layerDisplayName;
            }

            var fallback = Path.GetFileNameWithoutExtension(layerAnmName);
            // 一覧が未構築 (遅延構築中) の間はファイル名で出すが、控えには残さない。
            // 残すと構築が終わっても同じ段はファイル名のまま固定される
            if (!PhotoMotionUtils.EnsureMotionDataLoaded())
            {
                return fallback;
            }

            var data = PhotoMotionUtils.FindByClipName(Path.GetFileName(layerAnmName));
            _layerDisplayAnmName = layerAnmName;
            _layerDisplayName = data != null && !string.IsNullOrEmpty(data.name)
                ? data.name
                : fallback;
            return _layerDisplayName;
        }
    }
}
