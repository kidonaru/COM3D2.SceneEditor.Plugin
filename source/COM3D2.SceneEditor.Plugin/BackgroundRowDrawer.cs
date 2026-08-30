using System;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 背景まわりの行描画。
    /// BackgroundWindow と TimelineItemInspector (背景レイヤーの項目表示) で共有する。
    /// DrawObjectVector3Row / RecordObjectEdit は地面など他の Object スコープ編集行でも使う。
    ///
    /// 編集対象はタイムラインの背景レイヤーがキー化するのと同じ current_bg_object で、
    /// Inspector に出る Parent の座標 (ワールド値) とは別物
    /// </summary>
    public static class BackgroundRowDrawer
    {
        // 背景 Transform のリセット既定値（BgMgr が背景を生成した直後と同じ値）
        public static readonly Vector3 DefaultPosition = Vector3.zero;
        public static readonly Vector3 DefaultEulerAngles = Vector3.zero;
        public static readonly Vector3 DefaultScale = Vector3.one;

        /// <summary>座標行（Inspector の座標行と同じ形式）のドラッグ感度</summary>
        public const float PositionDragSensitivity = 0.01f;

        /// <summary>背景モデルのローカル Transform 編集行 (位置 / 回転 / 拡縮 + リセット)</summary>
        public static void DrawBgTransformRows(
            GUIView view, Transform transform, float labelWidth, float rowHeight)
        {
            DrawObjectVector3Row(view, "位置", "背景Transform: 位置",
                labelWidth, rowHeight, transform.localPosition,
                value => transform.localPosition = value,
                () => transform.localPosition = DefaultPosition, transform);
            DrawObjectVector3Row(view, "回転", "背景Transform: 回転",
                labelWidth, rowHeight, transform.localEulerAngles,
                value => transform.localEulerAngles = value,
                () => transform.localEulerAngles = DefaultEulerAngles, transform);
            DrawObjectVector3Row(view, "拡縮", "背景Transform: 拡縮",
                labelWidth, rowHeight, transform.localScale,
                value => transform.localScale = value,
                () => transform.localScale = DefaultScale, transform);

            if (view.DrawButton("Transformリセット", 140, rowHeight))
            {
                RecordObjectEdit("背景Transform: リセット", transform);
                transform.localPosition = DefaultPosition;
                transform.localEulerAngles = DefaultEulerAngles;
                transform.localScale = DefaultScale;
            }
        }

        /// <summary>
        /// ラベル + XYZ（ドラッグラベル + 数値入力）+ リセットボタンの 1 行。
        /// 背景ウィンドウの地面の行など、Object スコープで履歴を残す行でも使う
        /// </summary>
        public static void DrawObjectVector3Row(
            GUIView view, string label, string historyLabel,
            float labelWidth, float rowHeight, Vector3 value,
            Action<Vector3> onChanged, Action onReset, Transform target)
        {
            Vector3RowDrawer.Draw(view, label, PositionDragSensitivity, labelWidth, rowHeight,
                value,
                newValue =>
                {
                    RecordObjectEdit(historyLabel, target);
                    onChanged(newValue);
                },
                () =>
                {
                    RecordObjectEdit(historyLabel, target);
                    onReset();
                });
        }

        /// <summary>
        /// オブジェクトの Transform 操作を履歴へ記録する。
        /// Background スコープは Parent のワールド座標しか持たないため、
        /// 対象 Transform を直接記録する Object スコープを使う
        /// </summary>
        public static void RecordObjectEdit(string description, Transform target)
        {
            HistoryManager.instance.BeforeEdit(null, HistoryScope.Object,
                description, new[] { target });
        }
    }
}
