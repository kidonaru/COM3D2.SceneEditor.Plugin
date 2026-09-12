using COM3D2.MotionTimelineEditor;   // GUIView の名前空間 (GUIView.cs:6)

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 「この区間の値変更は編集モードへ自動移行する」を宣言する GUIView 拡張。
    /// 従来 view.SetEnabled(focusedComboBox == null &amp;&amp; isPoseEditing) で塞いでいた区間を
    /// 常時操作可にし、代わりに値を書く直前に AutoEditMode.Enter を呼ばせる
    /// </summary>
    public static class GUIViewAutoEditModeExtensions
    {
        public static void BeginAutoEditMode(this GUIView view)
        {
            view.SetEnabled(view.focusedComboBox == null);
            view.onBeforeValueChanged = AutoEditMode.Enter;
        }

        public static void EndAutoEditMode(this GUIView view)
        {
            view.onBeforeValueChanged = null;
            view.SetEnabled(view.focusedComboBox == null);
        }
    }
}
