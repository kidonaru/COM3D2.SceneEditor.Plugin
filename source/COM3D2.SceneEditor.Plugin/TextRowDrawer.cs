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
    /// 委譲先の個別ウィンドウが無いため、編集 UI はここが受け持つ
    /// (書き込み先は TimelineTextManager の FreeTextSet)。
    /// 書き込み先はどのスナップショットにも含まれないため履歴は記録しない。
    ///
    /// コンボボックスの開閉状態を持つため、テキストごと・描画するビューごとに
    /// インスタンスを分ける
    /// </summary>
    public class TextRowDrawer
    {
        private static MTEP.TimelineTextManager textManager => MTEP.TimelineTextManager.instance;

        /// <summary>ドラッグラベルの幅 (「サイズ」「行間」「幅」「高さ」が収まる幅)</summary>
        private const float ValueLabelWidth = 30f;

        /// <summary>テキスト入力の最大行数 (レイヤー UI と同じ)</summary>
        private const int TextMaxLines = 3;

        /// <summary>ドラッグラベルの 1px あたりの増減量 (レイヤー UI と同じ値)</summary>
        private const float RectSensitivity = 1f;
        private const float ScaleSensitivity = 0.01f;

        /// <summary>ドラッグ感度に GUIView の既定値を使わせる指定</summary>
        private const float UseDefaultDragSensitivity = 0f;

        /// <summary>枠サイズは範囲が広く、既定の 0.5/px ではドラッグで端まで届かない</summary>
        private const float SizeDragSensitivity = 5f;

        /// <summary>フォントサイズの既定値と上限 (TimelineTextManager.InitTexts と対)</summary>
        private const int DefaultFontSize = 50;
        private const int MaxFontSize = 200;

        /// <summary>行間の既定値と上限 (TimelineTextManager.InitTexts と対)</summary>
        private const int DefaultLineSpacing = 50;
        private const int MaxLineSpacing = 200;

        /// <summary>テキスト枠の幅・高さの既定値と上限 (TimelineTextManager.InitTexts と対)</summary>
        private const int DefaultSize = 1000;
        private const int MaxSize = 2000;

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

            var fieldWidth = GetPairedFieldWidth(view);

            view.BeginHorizontal();
            {
                DrawValueField(view, "サイズ", rowHeight, fieldWidth,
                    text.fontSize, MaxFontSize, DefaultFontSize, UseDefaultDragSensitivity,
                    value => text.fontSize = value);

                DrawValueField(view, "行間", rowHeight, fieldWidth,
                    (int)text.lineSpacing, MaxLineSpacing, DefaultLineSpacing,
                    UseDefaultDragSensitivity,
                    value => text.lineSpacing = value);
            }
            view.EndLayout();

            _alignmentComboBox.currentIndex = (int)text.alignment;
            _alignmentComboBox.onSelected = (alignment, _) =>
            {
                text.alignment = alignment;
            };

            _alignmentComboBox.DrawButton("整列", view);

            view.BeginHorizontal();
            {
                DrawValueField(view, "幅", rowHeight, fieldWidth,
                    (int)rect.sizeDelta.x, MaxSize, DefaultSize, SizeDragSensitivity,
                    value =>
                    {
                        var sizeDelta = rect.sizeDelta;
                        sizeDelta.x = value;
                        rect.sizeDelta = sizeDelta;
                    });

                DrawValueField(view, "高さ", rowHeight, fieldWidth,
                    (int)rect.sizeDelta.y, MaxSize, DefaultSize, SizeDragSensitivity,
                    value =>
                    {
                        var sizeDelta = rect.sizeDelta;
                        sizeDelta.y = value;
                        rect.sizeDelta = sizeDelta;
                    });
            }
            view.EndLayout();

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

        /// <summary>
        /// ドラッグラベル + 数値入力 1 個分。
        /// サイズ・行間・幅・高さはいずれも 0 以上の整数として扱うため、
        /// 元の型 (fontSize は int、lineSpacing と sizeDelta は float) を問わず int で描く
        /// </summary>
        private static void DrawValueField(
            GUIView view, string label, float rowHeight, float fieldWidth,
            int value, int maxValue, int defaultValue, float dragSensitivity,
            Action<int> onChanged)
        {
            view.DrawDragIntField(new GUIView.DragIntFieldOption
            {
                label = label,
                labelWidth = ValueLabelWidth,
                value = value,
                minValue = 0,
                maxValue = maxValue,
                fieldWidth = fieldWidth,
                height = rowHeight,
                dragSensitivity = dragSensitivity,
                onChanged = onChanged,
                onReset = () => onChanged(defaultValue),
            });
        }

        /// <summary>
        /// ドラッグラベル + 数値入力を 2 つ 1 行に詰めたときの、数値入力欄 1 個分の幅。
        /// 1 要素は「ラベル + margin + 入力欄 + リセットボタン」を消費し、
        /// 要素間にも margin が 1 つ入るので、その内訳から逆算する
        /// </summary>
        private static float GetPairedFieldWidth(GUIView view)
        {
            var available = view.viewRect.width - view.padding.x * 2 - view.margin;
            return available / 2f - ValueLabelWidth - view.margin - GUIView.ResetButtonWidth;
        }
    }
}
