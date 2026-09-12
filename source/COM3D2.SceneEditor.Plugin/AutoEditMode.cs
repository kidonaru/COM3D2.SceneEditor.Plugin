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
        public static void Enter()
        {
            var studioHackManager = MTEP.StudioHackManager.instance;
            var studioHack = studioHackManager.studioHack;

            if (studioHack == null)
            {
                // タイムライン側が未登録 (タイトル画面等) なら SE 本体のフラグだけ立てる
                MaidManipulateManager.instance.isEditMode = true;
                return;
            }

            if (studioHack.isPoseEditing)
            {
                return;
            }

            // StudioHackManager 経由で入ると再生停止も一緒に行われる (SceneEditorHack.isPoseEditing)
            studioHackManager.isPoseEditing = true;

            // キャッシュとスナップショットを同フレームで揃える。
            // 翌フレームの PreUpdate / Update に任せると、このあと書く値が
            // OnPoseEditStart の ApplyCurrentFrame で上書きされる
            studioHackManager.SyncPoseEditing();
            MTEP.TimelineManager.instance.SyncPoseEditing();
        }
    }
}
