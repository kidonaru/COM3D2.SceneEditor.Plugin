using System;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// Inspector のヘッダー行 (表示トグル + 名前 + 右端のフォーカスボタン)。
    /// 通常オブジェクトとモデルで同じ見た目にするため、トグルの意味と名前は呼び出し側が決める
    /// </summary>
    public static class InspectorHeaderRowDrawer
    {
        // トグルとフォーカスボタンの幅 (どちらも正方形)
        private const float ToggleWidth = 20f;
        private const float FocusButtonWidth = 20f;

        public static void Draw(
            GUIView view,
            bool active,
            string label,
            float rowHeight,
            Action<bool> onActiveChanged,
            GameObject focusTarget)
        {
            // 名前ラベルを自動幅にするとフォーカスボタンが右端からはみ出すため、
            // 残り幅を明示計算して割り当てる。
            // margin は NextElement が要素ごとに加算するため、要素数ぶん引く
            var labelWidth = view.viewRect.width - view.padding.x * 2
                - (ToggleWidth + view.margin)
                - (FocusButtonWidth + view.margin)
                - view.margin;

            view.BeginHorizontal();
            {
                view.DrawToggle(active, ToggleWidth, rowHeight, onActiveChanged);
                view.DrawLabel(label, labelWidth, rowHeight);

                var focusIcon = ToolbarIcons.GetTexture(ToolbarIcons.Kind.Focus);
                if (view.DrawTextureButton(focusIcon, FocusButtonWidth, rowHeight, 4f, tooltip: "フォーカス"))
                {
                    // 明示的なフォーカス要求なのでオートフォーカス設定に関わらず寄せる
                    SceneViewWindow.instance.FocusOn(focusTarget, true);
                }
            }
            view.EndLayout();
        }
    }
}
