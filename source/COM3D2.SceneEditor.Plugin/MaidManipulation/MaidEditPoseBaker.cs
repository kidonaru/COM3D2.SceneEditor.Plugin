using COM3D2.MotionTimelineEditor;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 手編集したポーズの anm 化。編集モードを抜けるとき (▶ / Space / メニューの OFF) と、
    /// 編集モード中にレイヤータブへ切り替えるときに、現在ポーズを anm にしてベースへ差し替える。
    /// 層を有効へ戻すと Unity が毎フレームサンプルして手編集を上書きするため、
    /// 先にベース自体を編集込みの anm にしておく (タイムラインがキーから anm を作るのと同じ考え方)。
    /// 経路はシーンプリセット復元と同じ CapturePoseBinary → 常駐クリップの差し替え
    /// </summary>
    public static class MaidEditPoseBaker
    {
        /// <summary>anm 化後の「再生中」の表示名。一覧のどのエントリでもないのでハイライトしない</summary>
        public const string EditPoseDisplayName = "編集ポーズ";

        /// <summary>
        /// ボーンを触っていれば anm 化する。タイムラインに対象メイドのモーションレイヤーがあるときは
        /// タイムライン側が anm を作るので行わない。anm 化したら true
        /// </summary>
        public static bool BakeIfEdited(Maid maid)
        {
            if (maid == null || !MaidAnimationBlendController.HasBoneEdit(maid))
            {
                return false;
            }
            if (HasMotionTimelineLayer(maid))
            {
                MaidAnimationBlendController.ClearBoneEdit(maid);
                return false;
            }
            if (MaidMotionState.IsPlaying(maid))
            {
                // 再生中は手編集が既に流れて消えているので固めるものが無い
                MaidAnimationBlendController.ClearBoneEdit(maid);
                return false;
            }

            // 直前の編集に確定待ちがあればそこへマージされる (ラベルは元の操作名のまま)。
            // 編集モードを抜ける途中で呼ばれるので、自動移行しない版を使う
            // (通常版だと抜ける操作を打ち消したうえ、停止したベースの再生まで再開してしまう)
            HistoryManager.instance.BeforeEditWhileLeavingEditMode(maid, HistoryScope.Pose,
                "編集ポーズ", () => PoseSnapshot.GetAllBodyBones(maid));

            var binary = MaidPoseFileManager.CapturePoseBinary(maid);
            if (binary == null)
            {
                MTEUtils.LogWarning("編集ポーズの anm 化に失敗しました (ポーズを取得できません)");
                return false;
            }

            // 常駐クリップだけ差し替える (停止・IK 解除・ダイアログを伴う ApplyPoseBinary は使わない)。
            // ポーズは既に Transform に乗っているのでサンプルは要らず、
            // 呼び出し元 (OnEditModeChanged / SetSelectedLayer) が層を戻したあとに再サンプルする
            var state = MaidPoseFileManager.ReplaceResidentClip(maid, binary);
            if (state == null)
            {
                return false;
            }
            state.enabled = false;
            state.weight = 1f;
            state.time = 0f;

            // ▶ の再開先とリセットの戻り先を固めたポーズ自身にする (プリセット復元と同じ扱い)
            MaidMotionState.SetResumeClip(maid, MaidPoseFileManager.PoseClipTag);
            MaidMotionState.SetAppliedMotion(maid, new MaidMotionState.AppliedMotionInfo
            {
                displayName = EditPoseDisplayName,
                isResidentPose = true,
            });
            MaidPoseFileManager.MarkPoseAsResetTarget(maid);
            MaidBoneSliderController.CaptureBasePose(maid);
            MaidAnimationBlendController.ClearBoneEdit(maid);
            MTEUtils.LogDebug("編集ポーズを anm 化しました: " + maid.name);
            return true;
        }

        private static bool HasMotionTimelineLayer(Maid maid)
        {
            var timelineManager = MTEP.TimelineManager.instance;
            if (timelineManager == null || timelineManager.timeline == null)
            {
                return false;
            }
            var cache = MTEP.MaidManager.instance.GetMaidCache(maid);
            return cache != null
                && timelineManager.GetLayer(typeof(MTEP.MotionTimelineLayer), cache.slotNo) != null;
        }
    }
}
