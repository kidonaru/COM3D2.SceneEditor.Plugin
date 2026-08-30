using System;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

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

        /// <summary>地面の広さのスライダーのラベル幅（SX / SZ の 2 文字ぶん）</summary>
        private const float GroundScaleLabelWidth = 30f;

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
        /// 背景を消しているときに見える色の編集行。
        /// アルファを下げると撮影時に透過 PNG として保存される。
        ///
        /// ColorPickerWindow はラベルで編集対象を識別するが、背景色・地面色は
        /// 呼び出し元によらず同じ 1 つの実体を指すため、ライトやマテリアルのように
        /// 呼び出し元ごとのラベル一意化はしない (背景ウィンドウと Inspector で
        /// 同じピッカーを共有するのが正しい状態)
        /// </summary>
        public static void DrawBgColorRow(GUIView view, float rowHeight)
        {
            var fieldCache = view.GetColorFieldCache("背景色", true);
            view.DrawColor(fieldCache, BackgroundUtils.bgColor, BackgroundUtils.defaultBgColor,
                value =>
                {
                    HistoryManager.instance.BeforeEdit(null, HistoryScope.Background, "背景色");
                    BackgroundUtils.bgColor = value;
                });

            view.DrawLabel("アルファを下げると透過PNGで撮影されます", -1, rowHeight,
                textColor: Color.gray);
        }

        /// <summary>
        /// 地面の表示・色・位置・広さ。
        /// 編集対象はタイムラインの背景色レイヤーがキー化するのと同じ BGGround
        /// </summary>
        public static void DrawGroundRows(GUIView view, float labelWidth, float rowHeight)
        {
            var groundManager = MTEP.BGGroundManager.instance;
            var ground = groundManager.bgGround;

            view.DrawToggle("地面を表示", ground != null && ground.visible, -1, rowHeight,
                value =>
                {
                    // 表示するまで実体を作らない（タイムラインを使わない間は生成しない）
                    var target = groundManager.GetOrCreate();
                    RecordObjectEdit("地面: 表示", target.transform);
                    target.visible = value;
                });

            if (ground == null)
            {
                return;
            }

            var groundTransform = ground.transform;

            // 地面色は履歴のスナップショットが持たないため記録しない
            // （ObjectSnapshot は Transform とアクティブ状態しか復元できない）
            var fieldCache = view.GetColorFieldCache("地面色", false);
            view.DrawColor(fieldCache, ground.color, MTEP.BGGround.DefaultColor,
                value => ground.color = value);

            DrawObjectVector3Row(view, "位置", "地面: 位置",
                labelWidth, rowHeight, ground.position,
                value => ground.position = value,
                () => ground.position = MTEP.BGGround.DefaultPosition,
                groundTransform);

            var scale = ground.scale;
            DrawGroundScaleSlider(view, "SX", scale.x, MTEP.BGGround.DefaultScale.x,
                groundTransform, value => ground.scale = new Vector3(value, scale.y, scale.z));
            DrawGroundScaleSlider(view, "SZ", scale.z, MTEP.BGGround.DefaultScale.z,
                groundTransform, value => ground.scale = new Vector3(scale.x, scale.y, value));
        }

        /// <summary>地面の広さのスライダー 1 行</summary>
        private static void DrawGroundScaleSlider(
            GUIView view, string label, float value, float defaultValue, Transform target,
            Action<float> onChanged)
        {
            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = label,
                labelWidth = GroundScaleLabelWidth,
                width = -1,
                min = 0f,
                max = 1000f,
                step = 1f,
                defaultValue = defaultValue,
                value = value,
                onChanged = newValue =>
                {
                    RecordObjectEdit("地面: " + label, target);
                    onChanged(newValue);
                },
            });
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
