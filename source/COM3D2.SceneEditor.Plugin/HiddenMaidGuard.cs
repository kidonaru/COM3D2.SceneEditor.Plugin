using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 退避中 (非表示) のメイドを操作させないためのガード。
    /// 退避中のメイドは画面外へ移動しており、表示に戻す際に戻り先の値で上書きされるため、
    /// その間の編集は黙って失われる。Inspector の各表示とタイムライン項目表示で共有する
    /// </summary>
    public static class HiddenMaidGuard
    {
        /// <summary>
        /// 退避中なら警告ラベルを描いて true を返す。
        /// 呼び出し側は true のとき編集UIを描かずに戻る
        /// </summary>
        /// <param name="message">何を操作できないかを伝える文言</param>
        public static bool DrawWarningIfHidden(
            GUIView view, Maid maid, string message, float rowHeight)
        {
            if (MaidManipulateManager.instance.IsVisible(maid))
            {
                return false;
            }

            view.DrawLabel(message, -1, rowHeight, textColor: Color.yellow);
            return true;
        }
    }
}
