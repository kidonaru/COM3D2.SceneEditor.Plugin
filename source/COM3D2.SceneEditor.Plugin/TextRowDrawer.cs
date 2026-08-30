using System;
using System.Linq;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// フリーテキスト 1 つ分の内容・スタイル・テキスト枠の Transform の行
    /// (テキスト / フォント / サイズ / 行間 / 整列 / 幅 / 高さ / 色 / 位置 / 回転 / 拡縮)。
    ///
    /// 委譲先の個別ウィンドウが無いため、旧レイヤー編集ウィンドウの編集 UI を
    /// ここへ引き継いでいる (書き込み先は TimelineTextManager の FreeTextSet)。
    /// 書き込み先はどのスナップショットにも含まれないため履歴は記録しない。
    ///
    /// コンボボックスの開閉状態を持つため、テキストごと・描画するビューごとに
    /// インスタンスを分ける
    /// </summary>
    public class TextRowDrawer
    {
        private static MTEP.TimelineTextManager textManager => MTEP.TimelineTextManager.instance;

        /// <summary>スライダーのラベル幅 (「サイズ」「行間」が収まる幅)</summary>
        private const float SliderLabelWidth = 30f;

        /// <summary>テキスト入力の最大行数 (レイヤー UI と同じ)</summary>
        private const int TextMaxLines = 3;

        /// <summary>ドラッグラベルの 1px あたりの増減量 (レイヤー UI と同じ値)</summary>
        private const float RectSensitivity = 1f;
        private const float ScaleSensitivity = 0.01f;

        private readonly GUIComboBox<string> _fontNameComboBox = new GUIComboBox<string>
        {
            getName = (fontName, _) => fontName,
        };

        private readonly GUIComboBox<TextAnchor> _alignmentComboBox = new GUIComboBox<TextAnchor>
        {
            items = Enum.GetValues(typeof(TextAnchor)).Cast<TextAnchor>().ToList(),
            getName = (alignment, _) => alignment.ToString(),
        };

        /// <param name="boneName">
        /// 対象テキストのメニュー項目名 ("Text0" 等)。直前キーの参照キーであり、
        /// 複数テキストを並べたときに色ピッカーの対象が混ざらないための同定キーでもある
        /// </param>
        public void Draw(
            GUIView view, MTEP.FreeTextSet freeTextSet, float rowHeight, string boneName)
        {
            var colorLabel = boneName + "/色";
            var text = freeTextSet.text;
            var rect = freeTextSet.rect;

            view.DrawLabel("テキスト", -1, rowHeight);

            view.DrawTextField(new GUIView.TextFieldOption
            {
                value = text.text,
                onChanged = value => text.text = value,
                maxLines = TextMaxLines,
            });

            _fontNameComboBox.items = MTEP.TimelineTextManager.fontNames;
            _fontNameComboBox.currentIndex = MTEP.TimelineTextManager.fontNames.IndexOf(
                text.font != null ? text.font.name : "");
            _fontNameComboBox.onSelected = (fontName, _) =>
            {
                text.font = textManager.GetFont(fontName);
            };

            _fontNameComboBox.DrawButton("フォント", view);

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "サイズ",
                labelWidth = SliderLabelWidth,
                fieldType = FloatFieldType.Int,
                min = 0,
                max = 200,
                step = 1,
                defaultValue = 50,
                value = text.fontSize,
                onChanged = value => text.fontSize = (int)value,
            });

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "行間",
                labelWidth = SliderLabelWidth,
                fieldType = FloatFieldType.Int,
                min = 0,
                max = 200,
                step = 1,
                defaultValue = 50,
                value = text.lineSpacing,
                onChanged = value => text.lineSpacing = value,
            });

            _alignmentComboBox.currentIndex = (int)text.alignment;
            _alignmentComboBox.onSelected = (alignment, _) =>
            {
                text.alignment = alignment;
            };

            _alignmentComboBox.DrawButton("整列", view);

            DrawSizeSlider(view, "幅", rect.sizeDelta.x, value =>
            {
                var sizeDelta = rect.sizeDelta;
                sizeDelta.x = value;
                rect.sizeDelta = sizeDelta;
            });

            DrawSizeSlider(view, "高さ", rect.sizeDelta.y, value =>
            {
                var sizeDelta = rect.sizeDelta;
                sizeDelta.y = value;
                rect.sizeDelta = sizeDelta;
            });

            var colorFieldCache = view.GetColorFieldCache(colorLabel, true);
            view.DrawColor(colorFieldCache, text.color, Color.white, value => text.color = value);

            DrawTransformRows(view, rect, boneName);
        }

        /// <summary>
        /// テキスト枠の位置・回転・拡縮の行。
        /// レイヤー UI の DrawTransformRect (初期値は位置 0 / 回転 0 / 拡縮 1) と同じ内容を、
        /// protected な行描画を使わずに同じ共有ヘルパーで組み直したもの
        /// </summary>
        private static void DrawTransformRows(GUIView view, RectTransform rect, string boneName)
        {
            var transformCache = view.GetTransformCache(rect);

            // 位置はピクセル指定のため 1px 単位の整数入力 (レイヤー UI の DrawPositionRect と同値)
            var position = transformCache.position;
            if (MTEP.TimelineLayerBase.DrawTransformVector3(
                    view, "位置", RectSensitivity, position, Vector3.zero,
                    value => position = value, fieldType: FloatFieldType.Int))
            {
                transformCache.position = position;
                transformCache.Apply();
            }

            // 回転はキーフレーム間で角度が飛ばないよう直前キーの角度を基準にする
            MTEP.TimelineLayerBase.DrawEulerAngles(
                view,
                transformCache,
                MTEP.TimelineLayerBase.TransformEditType.全て,
                TimelinePrevKeyUtils.GetPrevEulerAngles<MTEP.TextTimelineLayer>(
                    boneName, Vector3.zero),
                Vector3.zero);

            var scale = transformCache.scale;
            if (MTEP.TimelineLayerBase.DrawTransformVector3(
                    view, "拡縮", ScaleSensitivity, scale, Vector3.one,
                    value => scale = value, linkable: true))
            {
                transformCache.scale = scale;
                transformCache.Apply();
            }
        }

        /// <summary>テキスト枠の幅・高さのスライダー 1 行</summary>
        private static void DrawSizeSlider(
            GUIView view, string label, float value, Action<float> onChanged)
        {
            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = label,
                labelWidth = SliderLabelWidth,
                fieldType = FloatFieldType.Int,
                min = 0,
                max = 2000,
                step = 1,
                defaultValue = 1000,
                value = value,
                onChanged = onChanged,
            });
        }
    }
}
