using System.Collections.Generic;
using System.Linq;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// タイムライン下部の実値カーブエディタ。
    /// 選択中キーフレームのチャンネル値をカーブとして描画し、
    /// キー点の値ドラッグとタンジェントハンドル編集を提供する
    /// </summary>
    public class TimelineCurveEditor
    {
        // コンボボックスとトグルを 1 行に並べるため 20px 確保する
        public const float TOGGLE_BAR_HEIGHT = 20f;
        private const int MIN_PANE_HEIGHT = 80;
        private const int MAX_PANE_HEIGHT = 400;
        private const float TOGGLE_BUTTON_WIDTH = 80f;

        /// <summary>カーブ折れ線のサンプリング間隔 (px)</summary>
        private const float SAMPLE_STEP = 2f;
        /// <summary>カーブ折れ線の太さ (px)</summary>
        private const float CURVE_THICKNESS = 1f;
        /// <summary>全身ボーン選択時の draw call 急増を避けるための表示上限</summary>
        private const int MAX_CHANNELS = 12;
        // 以下はいずれも px。マーカーより当たり判定をひと回り広く取る
        private const float KEY_MARKER_SIZE = 6f;
        private const float HANDLE_LEN = 30f;
        private const float HANDLE_MARKER_SIZE = 6f;
        private const float KEY_HIT_RADIUS = 8f;
        private const float HANDLE_HIT_RADIUS = 6f;

        /// <summary>左ツールバーの行高・行間と内側余白 (px)</summary>
        private const float TOOL_ROW_HEIGHT = 20f;
        private const float TOOL_ROW_SPACING = 2f;
        private const float TOOL_PADDING_X = 2f;
        private const float TOOL_PADDING_Y = 1f;

        /// <summary>縦軸の目盛りラベルの高さ (px)</summary>
        private const float VALUE_LABEL_HEIGHT = 14f;

        // 凡例 (チャンネル名) の 1 行の高さと項目間の余白 (px)
        private const float LEGEND_ROW_HEIGHT = 16f;
        private const float LEGEND_ITEM_MARGIN = 10f;
        private const float LEGEND_PADDING = 4f;

        private static MTEP.Config config => MTEP.ConfigManager.instance.config;
        private static MTEP.TimelineManager timelineManager => MTEP.TimelineManager.instance;
        private static MTEP.TimelineData timeline => timelineManager.timeline;
        private static MTEP.ITimelineLayer currentLayer => timelineManager.currentLayer;
        private static HashSet<MTEP.BoneData> selectedBones => timelineManager.selectedBones;

        private static TimelineCurveEditor _instance = null;
        public static TimelineCurveEditor instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new TimelineCurveEditor();
                }
                return _instance;
            }
        }

        private TimelineCurveEditor()
        {
        }

        public bool isOpen
        {
            get => config.isCurveEditorOpen;
            set
            {
                if (config.isCurveEditorOpen == value) return;
                config.isCurveEditorOpen = value;
                config.dirty = true;
            }
        }

        public float paneHeight => config.curveEditorHeight;

        /// <summary>ペイン高さのドラッグリサイズ状態 (menuWidth リサイザと同じ流儀)</summary>
        private readonly GUIView.DragInfo _resizeDragInfo = new GUIView.DragInfo();

        /// <summary>表示するチャンネルの種別フィルタ</summary>
        private MTEP.TangentValueType _valueTypeFilter = MTEP.TangentValueType.すべて;

        /// <summary>選択中ボーンが実際に持つ値種別だけを候補にする (毎フレーム更新)</summary>
        private readonly List<MTEP.TangentValueType> _availableValueTypes
            = new List<MTEP.TangentValueType>();

        /// <summary>コンボに出すフィルタ候補。「回転」はクォータニオン格納なら進行度 + Euler 表示、
        /// オイラー角格納なら軸別の実カーブ。X/Y/Z回転 はオイラー角格納では実カーブ、
        /// クォータニオン格納では表示専用の Euler 変換カーブになる (CollectChannels)</summary>
        private static readonly MTEP.TangentValueType[] FilterCandidates = {
            MTEP.TangentValueType.すべて,
            MTEP.TangentValueType.移動,
            MTEP.TangentValueType.X移動, MTEP.TangentValueType.Y移動, MTEP.TangentValueType.Z移動,
            MTEP.TangentValueType.回転,
            MTEP.TangentValueType.X回転, MTEP.TangentValueType.Y回転, MTEP.TangentValueType.Z回転,
            MTEP.TangentValueType.拡縮,
            MTEP.TangentValueType.X拡縮, MTEP.TangentValueType.Y拡縮, MTEP.TangentValueType.Z拡縮,
        };

        private static bool IsAxisRotationType(MTEP.TangentValueType valueType)
        {
            return valueType == MTEP.TangentValueType.X回転
                || valueType == MTEP.TangentValueType.Y回転
                || valueType == MTEP.TangentValueType.Z回転;
        }

        /// <summary>軸別回転型の軸インデックス (X=0, Y=1, Z=2)。
        /// X/Y/Z回転 以外を渡さないこと (それ以外は Z 扱いになる)</summary>
        private static int GetAxisIndex(MTEP.TangentValueType valueType)
        {
            if (valueType == MTEP.TangentValueType.X回転) return 0;
            if (valueType == MTEP.TangentValueType.Y回転) return 1;
            return 2;
        }

        /// <summary>軸インデックスに対応する色 (X=赤 / Y=緑 / Z=青)</summary>
        private static Color GetAxisColor(int axis)
        {
            switch (axis)
            {
                case 0: return ColorX;
                case 1: return ColorY;
                default: return ColorZ;
            }
        }

        private readonly GUIComboBox<MTEP.TangentValueType> _valueTypeComboBox
            = new GUIComboBox<MTEP.TangentValueType>
        {
            getName = (type, index) => type.ToString(),
            buttonSize = new Vector2(60, 20),
            showArrow = true,
        };

        /// <summary>コンボの &lt; &gt; ボタン 2 個分の幅 (GUIComboBox 側の固定値 20px × 2)</summary>
        private const float COMBO_ARROW_WIDTH = 40f;

        /// <summary>プリセットボタンの表示名と適用する TangentPair。
        /// 選択キー自身の in/out ハンドルへ適用するため、
        /// EaseIn は in 側 (キーへ入る側)、EaseOut は out 側 (キーから出る側) が緩やかになる。
        /// タンジェントは線形勾配比 (1=線形, 0=完全に緩やか, 負=オーバーシュート)</summary>
        private static readonly KeyValuePair<string, MTEP.TangentPair>[] TangentPresets = {
            MakePreset("EaseInOut", 0f, 0f),
            MakePreset("EaseIn", 1f, 0f),
            MakePreset("EaseOut", 0f, 1f),
            MakePreset("線形", 1f, 1f),
        };

        /// <summary>正規化タンジェント直接入力用のフィールドキャッシュ (In / Out)</summary>
        private readonly FloatFieldCache _inTangentFieldCache = new FloatFieldCache();
        private readonly FloatFieldCache _outTangentFieldCache = new FloatFieldCache();

        private static KeyValuePair<string, MTEP.TangentPair> MakePreset(
            string label, float outTangent, float inTangent)
        {
            return new KeyValuePair<string, MTEP.TangentPair>(
                label,
                new MTEP.TangentPair { outTangent = outTangent, inTangent = inTangent });
        }

        /// <summary>プリセットボタンのスタイル。組み込みスタイルの解決には GUI.skin が要るため
        /// 静的初期化子ではなく OnGUI 内で遅延構築する (GUIView.InitStyles と同じ理由)</summary>
        private static GUIStyle _gsPresetButton = null;

        // 2 列に収めるためラベル幅に応じて縮める font size の範囲
        private const int PRESET_FONT_SIZE_MAX = 12;
        private const int PRESET_FONT_SIZE_MIN = 8;

        /// <summary>ボタン幅に一番長いラベルが収まる font size を選ぶ
        /// (menuWidth を詰めるとラベルが見切れるため)</summary>
        private static GUIStyle GetPresetButtonStyle(float buttonWidth)
        {
            if (_gsPresetButton == null)
            {
                _gsPresetButton = new GUIStyle("button")
                {
                    alignment = TextAnchor.MiddleCenter,
                };
            }

            var longestLabel = TangentPresets
                .Select(preset => preset.Key)
                .OrderByDescending(label => label.Length)
                .First();

            for (var fontSize = PRESET_FONT_SIZE_MAX; fontSize > PRESET_FONT_SIZE_MIN; fontSize--)
            {
                _gsPresetButton.fontSize = fontSize;
                if (GUIView.CalcWidth(_gsPresetButton, longestLabel) <= buttonWidth)
                {
                    break;
                }
            }

            return _gsPresetButton;
        }

        /// <summary>直近の描画で使ったマッピング。ヒットテストは描画済みの座標系に合わせる</summary>
        private MTEP.CurveViewMapping _mapping = null;
        private List<CurveChannel> _channels = new List<CurveChannel>();

        private enum DragMode
        {
            None,
            KeyValue,
            Tangent,
        }

        private DragMode _dragMode = DragMode.None;
        /// <summary>ドラッグ対象のキー値。CurveChannel は毎フレーム作り直すため実体を直接保持する</summary>
        private MTEP.ValueData _dragValue = null;
        /// <summary>ドラッグ対象のタンジェント。回転進行度チャンネルでは代表成分 (x) を指す
        /// (_dragTangents に含まれる同一実体で、常に一括更新されるため重複判定にも使える)</summary>
        private MTEP.TangentData _dragTangent = null;
        /// <summary>回転進行度チャンネルのタンジェントドラッグでは 4 成分を一括で動かす (通常は null)</summary>
        private List<MTEP.TangentData> _dragTangents = null;
        private bool _dragTangentIsOut = false;
        /// <summary>タンジェント正規化の基準となる区間線形勾配 (値/フレーム)</summary>
        private float _dragBaseSlopePerFrame = 0f;
        private int _dragKeyFrameNo = 0;
        /// <summary>ドラッグ対象キーの表示値 (回転進行度チャンネルではキー番号)</summary>
        private float _dragKeyValue = 0f;
        private bool _dragChanged = false;

        /// <summary>チャンネルの表示モード</summary>
        private enum ChannelKind
        {
            /// <summary>値そのものを表示・編集する通常チャンネル</summary>
            Normal,
            /// <summary>クォータニオン回転の進行度 1 本。値ドラッグ不可、タンジェントは 4 成分一括</summary>
            RotationProgress,
            /// <summary>クォータニオンから導出した表示専用の Euler 角。値・タンジェントとも編集不可</summary>
            EulerDisplay,
        }

        /// <summary>1 本のカーブ = 1 ボーン × 1 値チャンネル。
        /// RotationProgress / EulerDisplay は frameNos / values / keyBones / rotationValues を
        /// 兄弟チャンネルと共有参照するため、構築後にこれらのリストを変更してはならない</summary>
        private class CurveChannel
        {
            public ChannelKind kind = ChannelKind.Normal;
            public string boneName;
            /// <summary>単チャンネル型。カスタム値チャンネルでは未使用</summary>
            public MTEP.TangentValueType valueType;
            /// <summary>カスタム値チャンネルのキー名 (通常チャンネルは null)</summary>
            public string customKey;
            /// <summary>凡例に出す値名</summary>
            public string displayName;
            public Color color;
            /// <summary>フレーム番号順のキー列</summary>
            public List<int> frameNos = new List<int>();
            /// <summary>キーごとの値。導出チャンネル (進行度/Euler) では代表成分 (x) を積み、
            /// ドラッグ対象の特定とキー数依存ループの境界に使う</summary>
            public List<MTEP.ValueData> values = new List<MTEP.ValueData>();
            public List<MTEP.BoneData> keyBones = new List<MTEP.BoneData>();
            /// <summary>表示範囲のサンプル値 (SAMPLE_STEP px 刻み)</summary>
            public List<float> samples = new List<float>();

            public bool isRotationProgress => kind == ChannelKind.RotationProgress;
            public bool isEulerDisplay => kind == ChannelKind.EulerDisplay;

            /// <summary>回転進行度 / Euler 表示チャンネルのキーごとのクォータニオン成分 (x,y,z,w)</summary>
            public List<MTEP.ValueData[]> rotationValues = new List<MTEP.ValueData[]>();

            /// <summary>Euler 表示チャンネルの軸 (0=X, 1=Y, 2=Z)。EulerDisplay 以外は -1</summary>
            public int eulerAxis = -1;
            /// <summary>Euler 表示チャンネルのキー表示値 (連続化済みの角度、度)</summary>
            public List<float> eulerKeyValues = new List<float>();

            /// <summary>キー位置の表示値。回転進行度ではキー番号 (区間ごとに +1 進む)</summary>
            public float GetKeyValue(int i)
            {
                if (isEulerDisplay) return eulerKeyValues[i];
                return isRotationProgress ? i : values[i].value;
            }
        }

        // 描画チャンネルへの展開用 (ExpandValueTypes)。グループ別に分けてインデックス依存を避ける
        private static readonly MTEP.TangentValueType[] MoveChannelTypes = {
            MTEP.TangentValueType.X移動, MTEP.TangentValueType.Y移動, MTEP.TangentValueType.Z移動,
        };

        /// <summary>クォータニオン格納時は W も持つ。Euler 格納では W の値リストが空になり 3 本に落ちる</summary>
        private static readonly MTEP.TangentValueType[] RotationChannelTypes = {
            MTEP.TangentValueType.X回転, MTEP.TangentValueType.Y回転, MTEP.TangentValueType.Z回転,
            MTEP.TangentValueType.W回転,
        };

        private static readonly MTEP.TangentValueType[] ScaleChannelTypes = {
            MTEP.TangentValueType.X拡縮, MTEP.TangentValueType.Y拡縮, MTEP.TangentValueType.Z拡縮,
        };

        private static readonly Color ColorX = new Color(0.9f, 0.3f, 0.3f);   // 赤
        private static readonly Color ColorY = new Color(0.3f, 0.9f, 0.3f);   // 緑
        private static readonly Color ColorZ = new Color(0.4f, 0.6f, 1.0f);   // 青
        private static readonly Color ColorW = new Color(0.85f, 0.85f, 0.85f); // 白
        private static readonly Color ColorRotationProgress = new Color(0.95f, 0.6f, 0.25f); // 橙

        /// <summary>チャンネルの軸に対応する色</summary>
        private static Color GetChannelColor(MTEP.TangentValueType valueType)
        {
            switch (valueType)
            {
                case MTEP.TangentValueType.X移動:
                case MTEP.TangentValueType.X回転:
                case MTEP.TangentValueType.X拡縮:
                    return ColorX;
                case MTEP.TangentValueType.Y移動:
                case MTEP.TangentValueType.Y回転:
                case MTEP.TangentValueType.Y拡縮:
                    return ColorY;
                case MTEP.TangentValueType.Z移動:
                case MTEP.TangentValueType.Z回転:
                case MTEP.TangentValueType.Z拡縮:
                    return ColorZ;
                case MTEP.TangentValueType.W回転:
                default:
                    return ColorW;
            }
        }

        /// <summary>カスタム値チャンネル用のパレット (XYZ と混同しない色味)</summary>
        private static readonly Color[] CustomChannelColors = {
            new Color(0.95f, 0.85f, 0.3f),  // 黄
            new Color(0.95f, 0.6f, 0.25f),  // 橙
            new Color(0.8f, 0.5f, 0.95f),   // 紫
        };

        /// <summary>開閉トグルと高さリサイズを持つバーの描画</summary>
        public void DrawToggleBar(GUIView view, Rect barRect)
        {
            view.currentPos = new Vector2(barRect.x, barRect.y);
            view.DrawTexture(GUIView.texWhite, barRect.width, barRect.height,
                new Color(0.2f, 0.2f, 0.2f, 0.8f));

            view.currentPos = new Vector2(barRect.x + 5, barRect.y);
            if (view.DrawButton(isOpen ? "▼ カーブ" : "▲ カーブ", TOGGLE_BUTTON_WIDTH, TOGGLE_BAR_HEIGHT))
            {
                isOpen = !isOpen;
            }

            if (!isOpen)
            {
                return;
            }

            UpdateValueTypeFilter();

            // 開いている間はトグルボタン右のバー領域を上下ドラッグして高さを変更する
            var resizeX = barRect.x + 5 + TOGGLE_BUTTON_WIDTH + 5;
            var resizeWidth = Mathf.Max(0f, barRect.xMax - resizeX);
            view.currentPos = new Vector2(resizeX, barRect.y);
            view.InvokeActionOnDragging(
                resizeWidth,
                barRect.height,
                _resizeDragInfo,
                new Vector2(0f, config.curveEditorHeight),
                null,
                value =>
                {
                    // DragInfo は上方向ドラッグで y が増える。上へ引くほどペインを高くする
                    config.curveEditorHeight = Mathf.Clamp(
                        (int)value.y, MIN_PANE_HEIGHT, MAX_PANE_HEIGHT);
                    config.dirty = true;
                });
        }

        /// <summary>選択中ボーンが持つ値種別だけをフィルタ候補にし、
        /// 候補から外れた種別が残っていたら「すべて」へ戻す。
        /// (持っていない種別が residual で残るとカーブが 1 本も出ず、
        ///  キーフレーム未選択と見分けが付かなくなるため)</summary>
        private void UpdateValueTypeFilter()
        {
            _availableValueTypes.Clear();
            _availableValueTypes.Add(MTEP.TangentValueType.すべて);

            foreach (var valueType in FilterCandidates)
            {
                if (valueType != MTEP.TangentValueType.すべて && HasValueType(valueType))
                {
                    _availableValueTypes.Add(valueType);
                }
            }

            if (!_availableValueTypes.Contains(_valueTypeFilter))
            {
                _valueTypeFilter = MTEP.TangentValueType.すべて;
            }
        }

        /// <summary>選択中ボーンのいずれかが指定種別の値を持つか</summary>
        private static bool HasValueType(MTEP.TangentValueType valueType)
        {
            foreach (var bone in selectedBones)
            {
                var transform = bone.transform;
                if (transform == null)
                {
                    continue;
                }
                if (transform.GetValueDataList(valueType).Length > 0)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>ボーンメニュー下部に置く縦並びツールバー。
        /// 表示値種別・Ease プリセット・自動補間をまとめる</summary>
        public void DrawSideToolbar(GUIView view, Rect toolbarRect)
        {
            if (!isOpen)
            {
                return;
            }

            view.currentPos = new Vector2(toolbarRect.x, toolbarRect.y);
            view.DrawTexture(GUIView.texWhite, toolbarRect.width, toolbarRect.height,
                new Color(0.2f, 0.2f, 0.2f, 0.8f));

            var x = toolbarRect.x + TOOL_PADDING_X;
            var y = toolbarRect.y + TOOL_PADDING_Y;
            var width = Mathf.Max(20f, toolbarRect.width - TOOL_PADDING_X * 2f);

            view.currentPos = new Vector2(x, y);
            _valueTypeComboBox.items = _availableValueTypes;
            // buttonSize は矢印を含まないため、矢印の分を引いて 1 行に収める
            _valueTypeComboBox.buttonSize = new Vector2(
                Mathf.Max(20f, width - COMBO_ARROW_WIDTH), TOOL_ROW_HEIGHT);
            _valueTypeComboBox.currentIndex = Mathf.Max(0, _availableValueTypes.IndexOf(_valueTypeFilter));
            _valueTypeComboBox.onSelected = (type, index) => _valueTypeFilter = type;
            _valueTypeComboBox.DrawButton(view);
            y += TOOL_ROW_HEIGHT + TOOL_ROW_SPACING;

            if (selectedBones.Count == 0)
            {
                return;
            }

            var isSmooth = IsAllTangentSmooth();
            view.currentPos = new Vector2(x, y);
            view.DrawToggle("自動補間", isSmooth, width, TOOL_ROW_HEIGHT, newIsSmooth =>
            {
                ForEachTangent((tangent, isOut) => tangent.isSmooth = newIsSmooth);
                currentLayer.ApplyCurrentFrame(true);
                MTEP.TimelineHistoryManager.instance.AddHistory(timeline, "カーブ: 自動補間");
            });
            y += TOOL_ROW_HEIGHT + TOOL_ROW_SPACING;

            // プリセットは 2 列に並べて縦幅を節約する
            var halfWidth = (width - TOOL_ROW_SPACING) * 0.5f;
            var presetStyle = GetPresetButtonStyle(halfWidth);
            for (var i = 0; i < TangentPresets.Length; i++)
            {
                var preset = TangentPresets[i];
                var isRightColumn = (i % 2) == 1;

                view.currentPos = new Vector2(
                    x + (isRightColumn ? halfWidth + TOOL_ROW_SPACING : 0f), y);
                if (view.DrawButton(preset.Key, halfWidth, TOOL_ROW_HEIGHT, true, null, presetStyle))
                {
                    ApplyTangentPreset(preset.Key, preset.Value);
                }

                // 行の最後を描いたら改行する (プリセットが奇数個でも行送りが止まらないように)
                if (isRightColumn || i == TangentPresets.Length - 1)
                {
                    y += TOOL_ROW_HEIGHT + TOOL_ROW_SPACING;
                }
            }

            // 正規化タンジェント (線形勾配比、1=線形) の直接入力
            DrawTangentField(view, x, ref y, width, "In", false, _inTangentFieldCache);
            DrawTangentField(view, x, ref y, width, "Out", true, _outTangentFieldCache);
        }

        /// <summary>選択キーの片側タンジェントの正規化値を表示・編集するテキストフィールド。
        /// 選択内で値が揃っていなければ空欄になる</summary>
        private void DrawTangentField(
            GUIView view, float x, ref float y, float width,
            string label, bool isOut, FloatFieldCache fieldCache)
        {
            var current = GetUniformNormalizedTangent(isOut);
            // NaN は NaN と不一致扱いになり毎フレーム text が空に戻って入力中の文字
            // ("-" など未確定の文字列) を潰すため、双方 NaN のときは更新しない
            if (!(float.IsNaN(current) && float.IsNaN(fieldCache.value)))
            {
                fieldCache.UpdateValue(current);
            }

            view.currentPos = new Vector2(x, y);
            view.DrawFloatField(new GUIView.FloatFieldOption
            {
                label = label,
                labelWidth = 26,
                fieldCache = fieldCache,
                width = width,
                height = TOOL_ROW_HEIGHT,
                onChanged = value => ApplyNormalizedTangent(isOut, value),
            });
            y += TOOL_ROW_HEIGHT + TOOL_ROW_SPACING;
        }

        /// <summary>選択キーの片側タンジェントが全て同値ならその正規化値、
        /// 未選択または混在なら NaN (フィールドは空欄表示になる)</summary>
        private float GetUniformNormalizedTangent(bool isOut)
        {
            var result = float.NaN;
            var isMixed = false;
            ForEachTangent((tangent, tangentIsOut) =>
            {
                if (tangentIsOut != isOut || isMixed)
                {
                    return;
                }
                if (float.IsNaN(result))
                {
                    result = tangent.normalizedValue;
                }
                else if (result != tangent.normalizedValue)
                {
                    isMixed = true;
                }
            });
            return isMixed ? float.NaN : result;
        }

        /// <summary>選択キーの片側タンジェントへ正規化値を一括適用する</summary>
        private void ApplyNormalizedTangent(bool isOut, float value)
        {
            ForEachTangent((tangent, tangentIsOut) =>
            {
                if (tangentIsOut != isOut)
                {
                    return;
                }
                tangent.normalizedValue = value;
                tangent.isSmooth = false;
            });

            currentLayer.ApplyCurrentFrame(true);
            MTEP.TimelineHistoryManager.instance.AddHistory(
                timeline, "カーブ: タンジェント入力 " + (isOut ? "Out" : "In"));
        }

        /// <summary>選択キー自身の out/in Tangent を走査する
        /// (前キー側ではなく、選択している頂点の両ハンドルが対象)</summary>
        private void ForEachTangent(System.Action<MTEP.TangentData, bool> callback)
        {
            foreach (var bone in selectedBones)
            {
                foreach (var tangent in bone.transform.GetOutTangentDataList(_valueTypeFilter))
                {
                    callback(tangent, true);
                }

                foreach (var tangent in bone.transform.GetInTangentDataList(_valueTypeFilter))
                {
                    callback(tangent, false);
                }
            }
        }

        private bool IsAllTangentSmooth()
        {
            var isSmooth = true;
            var hasAny = false;
            ForEachTangent((tangent, isOut) =>
            {
                hasAny = true;
                if (!tangent.isSmooth) isSmooth = false;
            });
            return hasAny && isSmooth;
        }

        private void ApplyTangentPreset(string presetName, MTEP.TangentPair tangentPair)
        {
            ForEachTangent((tangent, isOut) =>
            {
                tangent.normalizedValue = isOut ? tangentPair.outTangent : tangentPair.inTangent;
                tangent.isSmooth = tangentPair.isSmooth;
            });

            currentLayer.ApplyCurrentFrame(true);
            MTEP.TimelineHistoryManager.instance.AddHistory(
                timeline, "カーブ: プリセット " + presetName);
        }

        /// <summary>カーブ描画領域。paneRect はウィンドウローカル座標</summary>
        public void DrawPane(GUIView view, Rect paneRect, float scrollX, float frameWidth, bool guiEnabled)
        {
            view.currentPos = new Vector2(paneRect.x, paneRect.y);
            view.DrawTexture(GUIView.texWhite, paneRect.width, paneRect.height, config.curveBgColor);

            var totalChannelCount = 0;
            _channels = CollectChannels(out totalChannelCount);

            if (_channels.Count == 0)
            {
                _mapping = null;
                view.currentPos = new Vector2(paneRect.x + 8, paneRect.y + 4);
                // 種別フィルタの絞り込みで 0 本になった場合を選択なしと取り違えないよう文言を分ける
                var message = selectedBones.Count == 0
                    ? "カーブ対象のキーフレームが選択されていません"
                    : string.Format("選択中のボーンに「{0}」の値がありません", _valueTypeFilter);
                view.DrawLabel(message, 400, 20, Color.gray);
                return;
            }

            // 表示中の座標系は Repaint 時に確定させ、他イベントは同じ座標系でヒットテストする
            if (Event.current.type == EventType.Repaint || _mapping == null)
            {
                _mapping = BuildMapping(_channels, paneRect, scrollX, frameWidth);
            }

            if (Event.current.type == EventType.Repaint)
            {
                var legend = BuildLegend(paneRect);

                DrawValueScale(view, paneRect, legend.height);

                foreach (var channel in _channels)
                {
                    DrawChannelCurve(view, channel, paneRect);
                }

                foreach (var channel in _channels)
                {
                    DrawChannelKeys(view, channel, paneRect, scrollX);
                    DrawChannelHandles(view, channel, paneRect, scrollX);
                }

                // 凡例はカーブより手前に重ねる
                DrawLegend(view, paneRect, legend);

                if (totalChannelCount > _channels.Count)
                {
                    view.currentPos = new Vector2(paneRect.x + 8, paneRect.y + 2);
                    view.DrawLabel(
                        string.Format("表示上限 {0} チャンネル (全 {1})", MAX_CHANNELS, totalChannelCount),
                        260, 18, Color.gray);
                }
            }

            if (guiEnabled)
            {
                HandleInput(view, paneRect, scrollX);
            }
            else if (_dragMode != DragMode.None)
            {
                // 操作不能中は MouseUp を拾えない。次に有効化されたとき古い対象を
                // 掴み続けないよう、ここでドラッグを打ち切る
                EndDrag();
            }
        }

        /// <summary>キー点とタンジェントハンドルのドラッグ処理</summary>
        private void HandleInput(GUIView view, Rect paneRect, float scrollX)
        {
            // paneRect はビューローカル。イベント座標系へ合わせるため原点を変換する
            var origin = view.GetDrawRect(paneRect.x, paneRect.y, 1f, 1f);
            var mouse = Event.current.mousePosition - new Vector2(origin.x, origin.y);
            var e = Event.current;

            if (_dragMode != DragMode.None)
            {
                if (!Input.GetMouseButton(0))
                {
                    EndDrag();
                }
                else
                {
                    UpdateDrag(mouse, scrollX);
                    if (e.type == EventType.MouseDrag)
                    {
                        e.Use();
                    }
                }
                return;
            }

            if (e.type != EventType.MouseDown || e.button != 0)
            {
                return;
            }
            if (mouse.x < 0f || mouse.x > paneRect.width || mouse.y < 0f || mouse.y > paneRect.height)
            {
                return;
            }

            // ハンドルはキーの上に描かれるため先にヒットテストする
            foreach (var channel in _channels)
            {
                // Euler 表示チャンネルは表示専用 (タンジェント編集も不可)
                if (channel.isEulerDisplay)
                {
                    continue;
                }

                for (var i = 0; i < channel.values.Count; i++)
                {
                    if (!IsHandleVisible(channel, i))
                    {
                        continue;
                    }

                    for (var side = 0; side < 2; side++)
                    {
                        var isOut = side == 0;
                        if (!TryGetBaseSlopePerFrame(channel, i, isOut, out var baseSlope))
                        {
                            continue;
                        }

                        var handlePos = GetHandlePos(channel, i, isOut, scrollX, baseSlope);
                        if (Vector2.Distance(handlePos, mouse) > HANDLE_HIT_RADIUS)
                        {
                            continue;
                        }

                        _dragMode = DragMode.Tangent;
                        _dragValue = channel.values[i];
                        _dragTangent = isOut ? _dragValue.outTangent : _dragValue.inTangent;
                        // 回転進行度チャンネルはクォータニオン 4 成分のタンジェントを一括で動かす
                        _dragTangents = channel.isRotationProgress
                            ? channel.rotationValues[i]
                                .Select(comp => isOut ? comp.outTangent : comp.inTangent)
                                .ToList()
                            : null;
                        _dragTangentIsOut = isOut;
                        _dragBaseSlopePerFrame = baseSlope;
                        _dragKeyFrameNo = channel.frameNos[i];
                        _dragKeyValue = channel.GetKeyValue(i);
                        _dragChanged = false;
                        e.Use();
                        return;
                    }
                }
            }

            foreach (var channel in _channels)
            {
                // 回転進行度・Euler 表示は導出値のため値ドラッグ不可 (表示値→成分の逆変換ができない)
                if (channel.isRotationProgress || channel.isEulerDisplay)
                {
                    continue;
                }

                for (var i = 0; i < channel.values.Count; i++)
                {
                    // 半透明表示の非選択キーを誤ってドラッグしないようハンドルと同じ条件で絞る
                    if (!IsHandleVisible(channel, i))
                    {
                        continue;
                    }

                    var keyPos = new Vector2(
                        _mapping.FrameToX(channel.frameNos[i]) - scrollX,
                        _mapping.ValueToY(channel.values[i].value));
                    if (Vector2.Distance(keyPos, mouse) > KEY_HIT_RADIUS)
                    {
                        continue;
                    }

                    _dragMode = DragMode.KeyValue;
                    _dragValue = channel.values[i];
                    _dragKeyFrameNo = channel.frameNos[i];
                    _dragKeyValue = channel.values[i].value;
                    _dragChanged = false;
                    e.Use();
                    return;
                }
            }

            // 何も掴まなかった押下も消費する。残すと GUI.DragWindow が拾って
            // カーブ編集中にウィンドウごと動いてしまう
            e.Use();
        }

        private void UpdateDrag(Vector2 mouse, float scrollX)
        {
            if (_dragMode == DragMode.KeyValue)
            {
                var newValue = _mapping.YToValue(mouse.y);
                if (newValue == _dragValue.value)
                {
                    return;
                }
                _dragValue.value = newValue;
                _dragChanged = true;
                currentLayer.ApplyCurrentFrame(true);
                return;
            }

            // タンジェント: キー点からマウスまでの画面勾配を「値/フレーム」へ戻して正規化する
            var keyX = _mapping.FrameToX(_dragKeyFrameNo) - scrollX;
            var keyY = _mapping.ValueToY(_dragKeyValue);
            var dx = mouse.x - keyX;
            var dy = mouse.y - keyY;

            // out は右側、in は左側のハンドル。逆方向へ回り込んだ入力は無視する
            if (_dragTangentIsOut ? dx <= 0f : dx >= 0f)
            {
                return;
            }

            var valueSlope = _mapping.ScreenSlopeToValueSlope(dx, dy);
            var normalized = valueSlope / _dragBaseSlopePerFrame;
            if (float.IsNaN(normalized) || float.IsInfinity(normalized))
            {
                return;
            }
            // 重複判定は代表成分だけで足りる (回転進行度では 4 成分が常に一括更新され同値になるため)
            if (normalized == _dragTangent.normalizedValue && !_dragTangent.isSmooth)
            {
                return;
            }

            if (_dragTangents != null)
            {
                foreach (var tangent in _dragTangents)
                {
                    tangent.normalizedValue = normalized;
                    tangent.isSmooth = false;
                }
            }
            else
            {
                _dragTangent.normalizedValue = normalized;
                _dragTangent.isSmooth = false;
            }
            _dragChanged = true;
            currentLayer.ApplyCurrentFrame(true);
        }

        private void EndDrag()
        {
            if (_dragChanged)
            {
                MTEP.TimelineHistoryManager.instance.AddHistory(
                    timeline,
                    _dragMode == DragMode.KeyValue ? "カーブ: 値変更" : "カーブ: タンジェント変更");
            }

            _dragMode = DragMode.None;
            _dragValue = null;
            _dragTangent = null;
            _dragTangents = null;
            _dragChanged = false;
        }

        /// <summary>選択中キーのみハンドルを出す</summary>
        private bool IsHandleVisible(CurveChannel channel, int keyIndex)
        {
            return selectedBones.Contains(channel.keyBones[keyIndex]);
        }

        /// <summary>タンジェント正規化の基準となる区間線形勾配 (値/フレーム)。
        /// UpdateTangent と同じく inTangent は流入区間、outTangent は流出区間を基準にする</summary>
        private static bool TryGetBaseSlopePerFrame(
            CurveChannel channel, int keyIndex, bool isOut, out float baseSlope)
        {
            baseSlope = 0f;

            var otherIndex = isOut ? keyIndex + 1 : keyIndex - 1;
            if (otherIndex < 0 || otherIndex >= channel.values.Count)
            {
                return false;
            }

            var dtFrames = channel.frameNos[otherIndex] - channel.frameNos[keyIndex];
            if (dtFrames == 0)
            {
                return false;
            }

            baseSlope = (channel.GetKeyValue(otherIndex) - channel.GetKeyValue(keyIndex)) / dtFrames;
            return baseSlope != 0f;
        }

        /// <summary>ハンドル先端のペイン内座標。baseSlopePerFrame は呼び出し側が
        /// TryGetBaseSlopePerFrame で取得済みの値を渡す (再計算を避ける)</summary>
        private Vector2 GetHandlePos(
            CurveChannel channel, int keyIndex, bool isOut, float scrollX, float baseSlopePerFrame)
        {
            var keyX = _mapping.FrameToX(channel.frameNos[keyIndex]) - scrollX;
            var keyY = _mapping.ValueToY(channel.GetKeyValue(keyIndex));

            float slopePerFrame;
            if (channel.isRotationProgress)
            {
                // 進行度領域に成分タンジェントをそのまま使えないため、
                // 4 成分の正規化勾配の平均を進行度の区間線形勾配へ掛けて近似する
                slopePerFrame = GetAverageNormalizedTangent(channel, keyIndex, isOut) * baseSlopePerFrame;
            }
            else
            {
                var tangent = isOut
                    ? channel.values[keyIndex].outTangent
                    : channel.values[keyIndex].inTangent;

                // TangentData.value は値/秒なのでフレームあたり勾配へ換算してから画面勾配にする
                slopePerFrame = tangent.value * timeline.frameDuration;
            }
            var pxPerValue = _mapping.paneHeight / (_mapping.valueMax - _mapping.valueMin);

            var dx = isOut ? _mapping.frameWidth : -_mapping.frameWidth;
            var dy = -slopePerFrame * pxPerValue * (isOut ? 1f : -1f);

            var dir = new Vector2(dx, dy).normalized;
            return new Vector2(keyX, keyY) + dir * HANDLE_LEN;
        }

        /// <summary>回転進行度チャンネルのハンドル表示用。4 成分の正規化タンジェントの平均。
        /// 成分間で符号が逆転していると打ち消し合い、表示勾配が実際より平坦になりうる (表示専用の近似)</summary>
        private static float GetAverageNormalizedTangent(CurveChannel channel, int keyIndex, bool isOut)
        {
            var comps = channel.rotationValues[keyIndex];
            var sum = 0f;
            foreach (var comp in comps)
            {
                var tangent = isOut ? comp.outTangent : comp.inTangent;
                sum += tangent.normalizedValue;
            }
            return sum / comps.Length;
        }

        /// <summary>選択ボーンから描画対象チャンネルを収集する</summary>
        private List<CurveChannel> CollectChannels(out int totalChannelCount)
        {
            var channels = new List<CurveChannel>();
            totalChannelCount = 0;

            if (currentLayer == null || selectedBones.Count == 0)
            {
                return channels;
            }

            // 選択順に依存しないよう、ボーン名は初出順で束ねる
            var boneNames = new List<string>();
            foreach (var bone in selectedBones)
            {
                if (!boneNames.Contains(bone.name))
                {
                    boneNames.Add(bone.name);
                }
            }

            var valueTypes = ExpandValueTypes(_valueTypeFilter);

            foreach (var boneName in boneNames)
            {
                // レイヤーの全キーフレームから同名ボーンを時系列で拾う
                var frameNos = new List<int>();
                var bones = new List<MTEP.BoneData>();
                foreach (var frame in currentLayer.keyFrames)
                {
                    var bone = frame.GetBone(boneName);
                    if (bone != null && bone.transform != null)
                    {
                        frameNos.Add(frame.frameNo);
                        bones.Add(bone);
                    }
                }
                if (bones.Count < 1)
                {
                    continue;
                }

                var firstTransform = bones[0].transform;

                // クォータニオン格納の回転は成分カーブを出さず、進行度 + Euler 表示に置き換える
                var isQuaternionRotation = firstTransform.hasRotation;
                if (isQuaternionRotation)
                {
                    AddQuaternionRotationChannels(
                        channels, ref totalChannelCount, boneName, frameNos, bones);
                }

                for (var i = 0; i < valueTypes.Count; i++)
                {
                    var valueType = valueTypes[i];
                    if (isQuaternionRotation
                        && (IsAxisRotationType(valueType) || valueType == MTEP.TangentValueType.W回転))
                    {
                        continue;
                    }
                    if (firstTransform.GetValueDataList(valueType).Length == 0)
                    {
                        continue;
                    }

                    totalChannelCount++;
                    if (channels.Count >= MAX_CHANNELS)
                    {
                        continue;
                    }

                    var channel = new CurveChannel
                    {
                        boneName = boneName,
                        valueType = valueType,
                        displayName = valueType.ToString(),
                        color = GetChannelColor(valueType),
                    };
                    for (var k = 0; k < bones.Count; k++)
                    {
                        var valueList = bones[k].transform.GetValueDataList(valueType);
                        if (valueList.Length == 0)
                        {
                            continue;
                        }
                        channel.frameNos.Add(frameNos[k]);
                        channel.values.Add(valueList[0]);
                        channel.keyBones.Add(bones[k]);
                    }
                    if (channel.values.Count > 0)
                    {
                        channels.Add(channel);
                    }
                }

                // カスタム値チャンネル (TangentValueType に含まれないためキー名で収集する)
                if (_valueTypeFilter != MTEP.TangentValueType.すべて)
                {
                    continue;
                }

                var customIndex = 0;
                foreach (var customValue in firstTransform.GetCustomValueInfoMap())
                {
                    var customKey = customValue.Key;
                    totalChannelCount++;
                    customIndex++;
                    if (channels.Count >= MAX_CHANNELS)
                    {
                        continue;
                    }

                    var channel = new CurveChannel
                    {
                        boneName = boneName,
                        customKey = customKey,
                        displayName = customValue.Value.name,
                        color = CustomChannelColors[(customIndex - 1) % CustomChannelColors.Length],
                    };
                    for (var k = 0; k < bones.Count; k++)
                    {
                        var transform = bones[k].transform;
                        if (!transform.HasCustomValue(customKey))
                        {
                            continue;
                        }
                        channel.frameNos.Add(frameNos[k]);
                        channel.values.Add(transform.GetCustomValue(customKey));
                        channel.keyBones.Add(bones[k]);
                    }
                    if (channel.values.Count > 0)
                    {
                        channels.Add(channel);
                    }
                }
            }

            return channels;
        }

        /// <summary>クォータニオン格納ボーンの回転チャンネルを追加する。
        /// 回転/すべて → 進行度 1 本 + 表示用 Euler 3 本、X/Y/Z回転 → 該当軸の Euler 表示 1 本</summary>
        private void AddQuaternionRotationChannels(
            List<CurveChannel> channels, ref int totalChannelCount,
            string boneName, List<int> frameNos, List<MTEP.BoneData> bones)
        {
            var isCompositeFilter = _valueTypeFilter == MTEP.TangentValueType.回転
                || _valueTypeFilter == MTEP.TangentValueType.すべて;
            if (!isCompositeFilter && !IsAxisRotationType(_valueTypeFilter))
            {
                return;
            }

            var progressChannel = BuildRotationProgressChannel(boneName, frameNos, bones);
            if (progressChannel == null)
            {
                return;
            }

            if (!isCompositeFilter)
            {
                totalChannelCount++;
                if (channels.Count < MAX_CHANNELS)
                {
                    channels.Add(BuildEulerDisplayChannel(
                        progressChannel, GetAxisIndex(_valueTypeFilter)));
                }
                return;
            }

            totalChannelCount += 4;
            if (channels.Count < MAX_CHANNELS)
            {
                channels.Add(progressChannel);
            }
            for (var axis = 0; axis < 3 && channels.Count < MAX_CHANNELS; axis++)
            {
                channels.Add(BuildEulerDisplayChannel(progressChannel, axis));
            }
        }

        /// <summary>クォータニオン格納ボーンの回転進行度チャンネルを構築する。
        /// キーが 1 つも拾えなければ null</summary>
        private static CurveChannel BuildRotationProgressChannel(
            string boneName, List<int> frameNos, List<MTEP.BoneData> bones)
        {
            var channel = new CurveChannel
            {
                kind = ChannelKind.RotationProgress,
                boneName = boneName,
                displayName = "回転進行度",
                color = ColorRotationProgress,
            };

            for (var k = 0; k < bones.Count; k++)
            {
                var valueList = bones[k].transform.GetValueDataList(MTEP.TangentValueType.回転);
                if (valueList.Length != 4)
                {
                    continue;
                }
                channel.frameNos.Add(frameNos[k]);
                channel.rotationValues.Add(valueList);
                // 代表成分 (x)。用途は CurveChannel.values のコメントを参照
                channel.values.Add(valueList[0]);
                channel.keyBones.Add(bones[k]);
            }
            if (channel.values.Count == 0)
            {
                return null;
            }
            return channel;
        }

        /// <summary>回転進行度チャンネルのキー列を流用して表示用 Euler チャンネルを作る。
        /// キー表示値は前キーと連続になるよう 360° 単位で寄せる (unwrap)。
        /// Euler 表現自体の切り替わり (ジンバル付近) までは補正しない表示近似</summary>
        private static CurveChannel BuildEulerDisplayChannel(CurveChannel source, int axis)
        {
            var channel = new CurveChannel
            {
                kind = ChannelKind.EulerDisplay,
                boneName = source.boneName,
                eulerAxis = axis,
                displayName = "Euler" + "XYZ"[axis],
                color = GetAxisColor(axis),
                frameNos = source.frameNos,
                values = source.values,
                keyBones = source.keyBones,
                rotationValues = source.rotationValues,
            };

            var prev = 0f;
            for (var k = 0; k < channel.rotationValues.Count; k++)
            {
                var raw = GetEulerAngle(ToQuaternion(channel.rotationValues[k]), axis);
                var value = k == 0 ? raw : UnwrapAngle(raw, prev);
                channel.eulerKeyValues.Add(value);
                prev = value;
            }
            return channel;
        }

        private static Quaternion ToQuaternion(MTEP.ValueData[] values)
        {
            return new Quaternion(values[0].value, values[1].value, values[2].value, values[3].value);
        }

        /// <summary>クォータニオンの指定軸の Euler 角 (度)。
        /// 補間途中の非単位クォータニオンも扱えるよう正規化してから変換する</summary>
        private static float GetEulerAngle(Quaternion q, int axis)
        {
            var mag = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            if (mag == 0f)
            {
                return 0f;
            }
            var euler = new Quaternion(q.x / mag, q.y / mag, q.z / mag, q.w / mag).eulerAngles;
            return axis == 0 ? euler.x : axis == 1 ? euler.y : euler.z;
        }

        /// <summary>angle を reference に最も近い周回へ寄せる (±180° 以内の差にする)</summary>
        private static float UnwrapAngle(float angle, float reference)
        {
            return angle + 360f * Mathf.Round((reference - angle) / 360f);
        }

        /// <summary>複合型フィルタを単チャンネル型へ展開する</summary>
        private static List<MTEP.TangentValueType> ExpandValueTypes(MTEP.TangentValueType filter)
        {
            switch (filter)
            {
                case MTEP.TangentValueType.すべて:
                    return MoveChannelTypes
                        .Concat(RotationChannelTypes)
                        .Concat(ScaleChannelTypes)
                        .ToList();
                case MTEP.TangentValueType.移動:
                    return MoveChannelTypes.ToList();
                case MTEP.TangentValueType.回転:
                    return RotationChannelTypes.ToList();
                case MTEP.TangentValueType.拡縮:
                    return ScaleChannelTypes.ToList();
                default:
                    return new List<MTEP.TangentValueType> { filter };
            }
        }

        /// <summary>表示範囲をサンプリングして縦軸マッピングを決める</summary>
        private static MTEP.CurveViewMapping BuildMapping(
            List<CurveChannel> channels, Rect paneRect, float scrollX, float frameWidth)
        {
            var columnCount = Mathf.Max(2, (int)(paneRect.width / SAMPLE_STEP) + 1);
            var allValues = new List<float>();

            foreach (var channel in channels)
            {
                channel.samples.Clear();
                for (var i = 0; i < columnCount; i++)
                {
                    var frameNo = (scrollX + i * SAMPLE_STEP) / frameWidth;
                    var value = EvaluateChannel(channel, frameNo);
                    channel.samples.Add(value);
                    allValues.Add(value);
                }
            }

            return MTEP.CurveViewMapping.AutoFit(frameWidth, paneRect.height, allValues);
        }

        /// <summary>チャンネルのフレーム位置 frameNo における実値。キー範囲外は端の値で保持する</summary>
        private static float EvaluateChannel(CurveChannel channel, float frameNo)
        {
            var count = channel.values.Count;
            if (count == 0)
            {
                return 0f;
            }
            if (frameNo <= channel.frameNos[0])
            {
                return channel.GetKeyValue(0);
            }
            if (frameNo >= channel.frameNos[count - 1])
            {
                return channel.GetKeyValue(count - 1);
            }

            if (channel.isRotationProgress)
            {
                return EvaluateRotationProgress(channel, frameNo);
            }
            if (channel.isEulerDisplay)
            {
                return EvaluateEulerDisplay(channel, frameNo);
            }

            for (var i = 0; i < count - 1; i++)
            {
                if (frameNo <= channel.frameNos[i + 1])
                {
                    return EvaluateSegment(
                        channel.frameNos[i], channel.values[i],
                        channel.frameNos[i + 1], channel.values[i + 1],
                        frameNo);
                }
            }
            return channel.values[count - 1].value;
        }

        /// <summary>キー区間 [a, b] 内のフレーム位置 f における実値を返す。
        /// 再生経路 (MoveTimelineLayer.ApplyMotionUpdateTangent) と同じ単位系:
        /// t0/t1 は秒、t は 0〜1 正規化、タンジェントは値/秒</summary>
        private static float EvaluateSegment(
            int frameA, MTEP.ValueData a,
            int frameB, MTEP.ValueData b,
            float f)
        {
            var dtFrames = frameB - frameA;
            if (dtFrames <= 0) return a.value;
            var t = (f - frameA) / dtFrames;

            // 再生と同一形状にするため t0/t1 は秒単位で渡す (単位系の原則を参照)
            var frameDuration = timeline.frameDuration;
            return MTEP.PluginUtils.Hermite(
                frameA * frameDuration, frameB * frameDuration,
                a.value, b.value,
                a.outTangent.value, b.inTangent.value, t);
        }

        /// <summary>回転進行度チャンネルの実値。キー i を高さ i に置き、
        /// 区間内は補間の進み具合 (0→1) を足して描く。傾き = 回転の速さで、
        /// タンジェントの緩急やオーバーシュートがそのまま形状に出る</summary>
        private static float EvaluateRotationProgress(CurveChannel channel, float frameNo)
        {
            var count = channel.rotationValues.Count;
            for (var i = 0; i < count - 1; i++)
            {
                if (frameNo <= channel.frameNos[i + 1])
                {
                    return i + EvaluateSegmentProgress(channel, i, frameNo);
                }
            }
            return count - 1;
        }

        /// <summary>区間 [keyIndex, keyIndex+1] 内の進行度 (0→1)。
        /// 各成分の正規化進行度を変化量で重み付け平均する (再生経路と同じ Hermite 形状)。
        /// 全成分が同値の区間は緩急が存在しないため時間そのまま (線形) にフォールバックする</summary>
        private static float EvaluateSegmentProgress(CurveChannel channel, int keyIndex, float frameNo)
        {
            var start = channel.rotationValues[keyIndex];
            var end = channel.rotationValues[keyIndex + 1];
            var frameA = channel.frameNos[keyIndex];
            var frameB = channel.frameNos[keyIndex + 1];

            // Σ(補間変位 × 符号) / Σ|delta| の形にして、極小 delta の成分を
            // 割り算で増幅しない (全成分が同比率なら単純平均と一致する)
            var weightedSum = 0f;
            var weightTotal = 0f;
            for (var c = 0; c < start.Length; c++)
            {
                var delta = end[c].value - start[c].value;
                if (delta == 0f)
                {
                    continue;
                }
                var value = EvaluateSegment(frameA, start[c], frameB, end[c], frameNo);
                weightedSum += (value - start[c].value) * Mathf.Sign(delta);
                weightTotal += Mathf.Abs(delta);
            }
            if (weightTotal > 0f)
            {
                return weightedSum / weightTotal;
            }

            var dtFrames = frameB - frameA;
            return dtFrames > 0 ? (frameNo - frameA) / (float)dtFrames : 0f;
        }

        /// <summary>Euler 表示チャンネルの実値 (度)。区間内はクォータニオン 4 成分を
        /// 再生経路と同じ Hermite で補間してから Euler 化し、キー値を線形でつないだ
        /// 基準角に最も近い周回へ寄せて連続性を保つ</summary>
        private static float EvaluateEulerDisplay(CurveChannel channel, float frameNo)
        {
            var count = channel.rotationValues.Count;
            for (var i = 0; i < count - 1; i++)
            {
                if (frameNo <= channel.frameNos[i + 1])
                {
                    var start = channel.rotationValues[i];
                    var end = channel.rotationValues[i + 1];
                    var frameA = channel.frameNos[i];
                    var frameB = channel.frameNos[i + 1];

                    var comps = new float[4];
                    for (var c = 0; c < 4; c++)
                    {
                        comps[c] = EvaluateSegment(frameA, start[c], frameB, end[c], frameNo);
                    }
                    var raw = GetEulerAngle(
                        new Quaternion(comps[0], comps[1], comps[2], comps[3]), channel.eulerAxis);

                    var dtFrames = frameB - frameA;
                    var t = dtFrames > 0 ? (frameNo - frameA) / (float)dtFrames : 0f;
                    var reference = Mathf.Lerp(
                        channel.eulerKeyValues[i], channel.eulerKeyValues[i + 1], t);
                    return UnwrapAngle(raw, reference);
                }
            }
            return channel.eulerKeyValues[count - 1];
        }

        /// <summary>凡例の折り返しレイアウト結果</summary>
        private class LegendLayout
        {
            /// <summary>行ごとのチャンネル番号</summary>
            public readonly List<List<int>> rows = new List<List<int>>();
            public readonly List<string> labels = new List<string>();
            public float height;
        }

        /// <summary>凡例のラベルと折り返し位置を決める (描画はしない)</summary>
        private LegendLayout BuildLegend(Rect paneRect)
        {
            var layout = new LegendLayout();

            // ボーンが 1 つだけなら値名だけで十分なのでボーン名は省く
            var isMultiBone = _channels.Select(channel => channel.boneName).Distinct().Count() > 1;
            foreach (var channel in _channels)
            {
                var name = channel.displayName ?? channel.customKey ?? channel.valueType.ToString();
                layout.labels.Add(isMultiBone ? channel.boneName + "." + name : name);
            }

            // 中央の目盛りラベル (ペイン中央) より下に収まる行数までに抑える。
            // これを超えると下端の目盛りラベルが中央ラベルを追い越して重なる
            var maxRowCount = Mathf.Max(
                1, (int)((paneRect.height * 0.5f - VALUE_LABEL_HEIGHT) / LEGEND_ROW_HEIGHT));

            var currentRow = new List<int>();
            var currentWidth = LEGEND_PADDING;

            for (var i = 0; i < layout.labels.Count; i++)
            {
                var itemWidth = GUIView.CalcWidth(GUIView.gsLabel, layout.labels[i]) + LEGEND_ITEM_MARGIN;
                if (currentRow.Count > 0 && currentWidth + itemWidth > paneRect.width)
                {
                    layout.rows.Add(currentRow);
                    if (layout.rows.Count >= maxRowCount)
                    {
                        currentRow = null;
                        break;
                    }
                    currentRow = new List<int>();
                    currentWidth = LEGEND_PADDING;
                }

                currentRow.Add(i);
                currentWidth += itemWidth;
            }

            if (currentRow != null && currentRow.Count > 0)
            {
                layout.rows.Add(currentRow);
            }

            layout.height = layout.rows.Count * LEGEND_ROW_HEIGHT;
            return layout;
        }

        /// <summary>ペイン下部に半透明黒の帯を敷き、チャンネル名をその色で並べる</summary>
        private void DrawLegend(GUIView view, Rect paneRect, LegendLayout layout)
        {
            if (layout.rows.Count == 0)
            {
                return;
            }

            var top = paneRect.height - layout.height;

            view.currentPos = new Vector2(paneRect.x, paneRect.y + top);
            view.DrawTexture(GUIView.texWhite, paneRect.width, layout.height,
                new Color(0f, 0f, 0f, 0.6f));

            for (var r = 0; r < layout.rows.Count; r++)
            {
                var x = LEGEND_PADDING;
                var y = top + r * LEGEND_ROW_HEIGHT;

                foreach (var index in layout.rows[r])
                {
                    var label = layout.labels[index];
                    var itemWidth = GUIView.CalcWidth(GUIView.gsLabel, label) + LEGEND_ITEM_MARGIN;

                    view.currentPos = new Vector2(paneRect.x + x, paneRect.y + y);
                    view.DrawLabel(label, itemWidth, LEGEND_ROW_HEIGHT, _channels[index].color);
                    x += itemWidth;
                }
            }
        }

        /// <summary>縦軸の目盛りラベル (上端・中央・下端)。
        /// 下端は凡例に隠れないよう legendHeight 分だけ持ち上げる</summary>
        private void DrawValueScale(GUIView view, Rect paneRect, float legendHeight)
        {
            var centerY = paneRect.height * 0.5f;
            var labels = new List<KeyValuePair<float, float>>
            {
                new KeyValuePair<float, float>(0f, _mapping.valueMax),
                new KeyValuePair<float, float>(centerY, (_mapping.valueMin + _mapping.valueMax) * 0.5f),
            };

            // ペインが低いと凡例に押し上げられて中央ラベルと交差するため、その場合は下端を省く
            var minLabelY = paneRect.height - VALUE_LABEL_HEIGHT - legendHeight;
            if (minLabelY >= centerY + VALUE_LABEL_HEIGHT)
            {
                labels.Add(new KeyValuePair<float, float>(minLabelY, _mapping.valueMin));
            }

            foreach (var label in labels)
            {
                view.currentPos = new Vector2(paneRect.x + 2, paneRect.y + label.Key);
                view.DrawLabel(label.Value.ToString("F2"), 60, VALUE_LABEL_HEIGHT,
                    new Color(1f, 1f, 1f, 0.5f));
            }
        }

        /// <summary>サンプル値を SAMPLE_STEP 幅の矩形セグメントで折れ線描画する</summary>
        private void DrawChannelCurve(GUIView view, CurveChannel channel, Rect paneRect)
        {
            var samples = channel.samples;
            for (var i = 1; i < samples.Count; i++)
            {
                var y0 = _mapping.ValueToY(samples[i - 1]);
                var y1 = _mapping.ValueToY(samples[i]);
                var top = Mathf.Min(y0, y1);
                var bottom = Mathf.Max(y0, y1);

                // 表示値域外へ出た区間は描かない (ペイン外へはみ出させない)
                if (bottom < 0f || top > paneRect.height)
                {
                    continue;
                }
                top = Mathf.Clamp(top, 0f, paneRect.height);
                bottom = Mathf.Clamp(bottom, 0f, paneRect.height);

                var x = (i - 1) * SAMPLE_STEP;
                if (x + SAMPLE_STEP > paneRect.width)
                {
                    break;
                }

                view.currentPos = new Vector2(paneRect.x + x, paneRect.y + top);
                // 隣接セグメントとの縦ギャップを埋めるため線の太さ分だけ足す
                view.DrawTexture(
                    GUIView.texWhite,
                    SAMPLE_STEP,
                    Mathf.Min(bottom - top + CURVE_THICKNESS, paneRect.height - top),
                    channel.color);
            }
        }

        /// <summary>キー点マーカー。非選択キーは半透明で描く</summary>
        private void DrawChannelKeys(GUIView view, CurveChannel channel, Rect paneRect, float scrollX)
        {
            var half = KEY_MARKER_SIZE * 0.5f;

            for (var i = 0; i < channel.values.Count; i++)
            {
                var x = _mapping.FrameToX(channel.frameNos[i]) - scrollX;
                if (x < half || x > paneRect.width - half)
                {
                    continue;
                }

                var y = _mapping.ValueToY(channel.GetKeyValue(i));
                if (y < half || y > paneRect.height - half)
                {
                    continue;
                }

                var color = channel.color;
                if (!selectedBones.Contains(channel.keyBones[i]))
                {
                    color.a = 0.5f;
                }

                view.currentPos = new Vector2(paneRect.x + x - half, paneRect.y + y - half);
                view.DrawTexture(GUIView.texWhite, KEY_MARKER_SIZE, KEY_MARKER_SIZE, color);
            }
        }

        /// <summary>選択キーの in/out タンジェントハンドルを描画する</summary>
        private void DrawChannelHandles(GUIView view, CurveChannel channel, Rect paneRect, float scrollX)
        {
            // Euler 表示チャンネルは表示専用のためハンドルを出さない
            if (channel.isEulerDisplay)
            {
                return;
            }

            var handleColor = new Color(1f, 1f, 1f, 0.8f);
            var half = HANDLE_MARKER_SIZE * 0.5f;

            for (var i = 0; i < channel.values.Count; i++)
            {
                if (!IsHandleVisible(channel, i))
                {
                    continue;
                }

                var keyPos = new Vector2(
                    _mapping.FrameToX(channel.frameNos[i]) - scrollX,
                    _mapping.ValueToY(channel.GetKeyValue(i)));

                for (var side = 0; side < 2; side++)
                {
                    var isOut = side == 0;
                    if (!TryGetBaseSlopePerFrame(channel, i, isOut, out var baseSlope))
                    {
                        continue;
                    }

                    var handlePos = GetHandlePos(channel, i, isOut, scrollX, baseSlope);

                    // 線分はカーブと同じく小さな矩形の連続で描く
                    var steps = Mathf.CeilToInt(HANDLE_LEN / SAMPLE_STEP);
                    for (var s = 1; s <= steps; s++)
                    {
                        var p = Vector2.Lerp(keyPos, handlePos, s / (float)steps);
                        if (!IsInPane(p, paneRect, 1f))
                        {
                            continue;
                        }
                        view.currentPos = new Vector2(paneRect.x + p.x - 1f, paneRect.y + p.y - 1f);
                        view.DrawTexture(GUIView.texWhite, SAMPLE_STEP, SAMPLE_STEP, handleColor);
                    }

                    if (IsInPane(handlePos, paneRect, half))
                    {
                        view.currentPos = new Vector2(
                            paneRect.x + handlePos.x - half, paneRect.y + handlePos.y - half);
                        view.DrawTexture(
                            GUIView.texWhite, HANDLE_MARKER_SIZE, HANDLE_MARKER_SIZE, handleColor);
                    }
                }
            }
        }

        private static bool IsInPane(Vector2 pos, Rect paneRect, float margin)
        {
            return pos.x >= margin && pos.x <= paneRect.width - margin
                && pos.y >= margin && pos.y <= paneRect.height - margin;
        }
    }
}
