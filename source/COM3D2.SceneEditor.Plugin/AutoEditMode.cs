using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// パラメータ変更時に編集モードへ自動で入るための唯一の入口。
    /// 値を書き換える「前」に呼ぶこと。編集モード外はタイムラインのレイヤーが
    /// 毎フレーム再生値を書き戻すため、先に入っておかないと変更が巻き戻る。
    /// 既に編集モードなら何もしない。
    ///
    /// 行ドロワーの値変更は GUIView の onBeforeValueChanged 経由で自動的にここを通る
    /// (GUIViewAutoEditModeExtensions.BeginAutoEditMode)。
    /// ボタン (DrawButton) やドラッグ図のようにコールバックを経由しない操作は、
    /// 値を書く側が自分でこれを呼ぶこと
    /// </summary>
    public static class AutoEditMode
    {
        /// <summary>
        /// 編集モードを抜ける。モーションを流し始めるときに呼ぶ
        /// (止めたポーズを基準にする編集モードと、再生は両立しない)。
        /// Enter と同じく、タイムライン側が生きていればそちら経由で抜ける
        /// </summary>
        public static void Exit()
        {
            if (MTEP.SceneEditorHack.instance == null)
            {
                MaidManipulateManager.instance.isEditMode = false;
                return;
            }
            MTEP.SceneEditorHack.isPoseEditing = false;
        }

        public static void Enter()
        {
            // 値を書く直前に必ず通る場所なので、どのレイヤーのゲート内で
            // 触ったかをここで控える (編集モードへ既に入っていても控えは要る)
            TimelineLayerGate.RecordEditedLayerFromOpenGate();

            if (MTEP.SceneEditorHack.instance == null)
            {
                // タイムライン側が未初期化 (タイトル画面等) なら SE 本体のフラグだけ立てる
                MaidManipulateManager.instance.isEditMode = true;
                return;
            }

            if (MTEP.SceneEditorHack.isPoseEditing)
            {
                return;
            }

            // SceneEditorHack 経由で入ると再生停止も一緒に行われる
            MTEP.SceneEditorHack.isPoseEditing = true;

            // スナップショットを同フレームで取る。
            // 翌フレームの TimelineManager.Update に任せると、このあと書く値が
            // OnPoseEditStart の ApplyCurrentFrame で上書きされる
            MTEP.TimelineManager.instance.SyncPoseEditing();
        }
    }
}
