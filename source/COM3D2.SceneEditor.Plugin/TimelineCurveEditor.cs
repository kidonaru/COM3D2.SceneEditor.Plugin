using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
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
        private const float FIT_BUTTON_WIDTH = 50f;
        /// <summary>ホイール 1 ノッチの縦ズーム倍率</summary>
        private const float WHEEL_ZOOM_FACTOR = 1.2f;

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

        // 凡例 (チャンネル名) の高さと項目間の余白 (px)
        private const float LEGEND_ROW_HEIGHT = 16f;
        private const float LEGEND_ITEM_MARGIN = 10f;
        private const float LEGEND_PADDING = 4f;
        /// <summary>凡例を出すために最低限残したいグラフ高さ (px)。
        /// これを下回るペインでは凡例を省いて全面をグラフに使う</summary>
        private const float MIN_GRAPH_HEIGHT = 24f;

        /// <summary>プリセットサムネの生成解像度 (表示は幅・高さに合わせて縮小する)</summary>
        private const int PRESET_TEX_SIZE = 40;
        /// <summary>プリセットサムネの表示サイズの上下限 (px)</summary>
        private const float PRESET_DRAW_SIZE_MAX = 40f;
        private const float PRESET_DRAW_SIZE_MIN = 20f;
        /// <summary>タンジェント行のラベル幅 ("Out" が収まる幅)</summary>
        private const float TANGENT_LABEL_WIDTH = 30f;
        /// <summary>ラベルドラッグ 1px あたりのタンジェント増減量 (Inspector と同じ)</summary>
        private const float TANGENT_DRAG_SENSITIVITY = 0.01f;

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

        private static bool IsAxisRotationType(MTEP.TangentValueType valueType)
        {
            return valueType == MTEP.TangentValueType.X回転
                || valueType == MTEP.TangentValueType.Y回転
                || valueType == MTEP.TangentValueType.Z回転;
        }

        /// <summary>軸別回転種別の軸インデックス (X=0, Y=1, Z=2)。
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

        /// <summary>表示・編集の対象 (軸ごとの値種別 + カスタム値)。Inspector と同じ実装を共有する。
        /// W回転 はカーブに出さないため候補から外す (クォータニオン回転は Euler 表示に置き換わる)</summary>
        private readonly TangentTargetList _targets = new TangentTargetList
        {
            excludedValueTypes = { MTEP.TangentValueType.W回転 },
        };

        private readonly GUIComboBox<TangentTarget> _valueTypeComboBox
            = new GUIComboBox<TangentTarget>
        {
            getName = (target, index) => target.name,
            buttonSize = new Vector2(60, 20),
            showArrow = true,
        };

        /// <summary>コンボの &lt; &gt; ボタン 2 個分の幅 (GUIComboBox 側の固定値 20px × 2)</summary>
        private const float COMBO_ARROW_WIDTH = 40f;

        /// <summary>正規化タンジェント直接入力用のフィールドキャッシュ (In / Out)</summary>
        private readonly FloatFieldCache _inTangentFieldCache = new FloatFieldCache();
        private readonly FloatFieldCache _outTangentFieldCache = new FloatFieldCache();

        /// <summary>プリセットサムネ (添字は TangentType の値)。GUI.skin と同じく OnGUI 内で遅延生成する</summary>
        private Texture2D[] _presetTextures = null;

        /// <summary>直近の描画で使ったマッピング。ヒットテストは描画済みの座標系に合わせる</summary>
        private MTEP.CurveViewMapping _mapping = null;

        /// <summary>手動ズーム・パン中の値域。null なら表示範囲へ自動フィットする</summary>
        private float? _manualValueMin;
        private float? _manualValueMax;
        /// <summary>中ボタンでパン中か。押下位置からではなく前イベントとの差分で動かす</summary>
        private bool _isPanning;
        private float _panLastY;
        /// <summary>縦ズームを処理した直近のフレーム (ConsumeWheel が 1 フレーム 1 回に絞るのに使う)</summary>
        private int _wheelZoomFrame = -1;
        /// <summary>自動フィットへ戻す判定用。表示中チャンネルの種別と対象ボーンの組</summary>
        private string _valueRangeViewKey;

        /// <summary>縦方向を手動でズーム・パンしているか</summary>
        private bool isValueRangeManual => _manualValueMin.HasValue;
        private List<CurveChannel> _channels = new List<CurveChannel>();
        /// <summary>_channels を構築したときの入力シグネチャ。一致する間は再構築しない (GC 対策)</summary>
        private long _channelsSignature = 0;
        private bool _channelsValid = false;
        private int _totalChannelCount = 0;
        /// <summary>直近の凡例レイアウト。テキスト幅計測を含むので、
        /// チャンネル再構築時とペインサイズ変化時だけ作り直す (GC 対策)</summary>
        private LegendLayout _legend = null;
        /// <summary>_legend を構築したときのペインサイズ</summary>
        private Vector2 _legendPaneSize = Vector2.zero;
        /// <summary>シグネチャ計算用の作業バッファ (選択ボーン名を初出順に並べる)</summary>
        private readonly List<string> _signatureBoneNames = new List<string>(8);
        /// <summary>BuildMapping の縦軸フィット用バッファ (毎 Repaint の確保を避ける)</summary>
        private readonly List<float> _mappingValues = new List<float>(1024);

        private enum DragMode
        {
            None,
            KeyValue,
            Tangent,
        }

        private DragMode _dragMode = DragMode.None;
        /// <summary>ドラッグ対象のキー値。CurveChannel は入力が変わると作り直されるため実体を直接保持する</summary>
        private MTEP.ValueData _dragValue = null;
        /// <summary>ドラッグ中のタンジェント。Euler 表示では回転 4 成分がまとめて入る</summary>
        private MTEP.TangentData[] _dragTangents = null;
        private bool _dragTangentIsOut = false;
        /// <summary>タンジェント正規化の基準となる区間線形勾配 (値/フレーム)</summary>
        private float _dragBaseSlopePerFrame = 0f;
        private int _dragKeyFrameNo = 0;
        /// <summary>ドラッグ対象キーの表示値</summary>
        private float _dragKeyValue = 0f;
        private bool _dragChanged = false;

        /// <summary>チャンネルの表示モード</summary>
        private enum ChannelKind
        {
            /// <summary>値そのものを表示・編集する通常チャンネル</summary>
            Normal,
            /// <summary>クォータニオンから導出した Euler 角。
            /// 表示値から 4 成分への逆変換が無いため値は編集できないが、
            /// タンジェントは 4 成分へ同じ正規化値を流して編集できる</summary>
            EulerDisplay,
        }

        /// <summary>1 本のカーブ = 1 ボーン × 1 値チャンネル。
        /// EulerDisplay は frameNos / values / keyBones / rotationValues /
        /// segmentStFrames / segmentEdFrames を兄弟チャンネルと共有参照するため、
        /// 構築後にこれらのリストを変更してはならない</summary>
        private class CurveChannel
        {
            public ChannelKind kind = ChannelKind.Normal;
            /// <summary>単チャンネル型。カスタム値チャンネルでは未使用</summary>
            public MTEP.TangentValueType valueType;
            /// <summary>カスタム値チャンネルのキー名 (通常チャンネルは null)</summary>
            public string customKey;
            /// <summary>凡例に出す値名</summary>
            public string displayName;
            public Color color;
            /// <summary>フレーム番号順のキー列</summary>
            public List<int> frameNos = new List<int>();
            /// <summary>キーごとの値。Euler 表示チャンネルでは代表成分 (x) を積み、
            /// ドラッグ対象の特定とキー数依存ループの境界に使う</summary>
            public List<MTEP.ValueData> values = new List<MTEP.ValueData>();
            public List<MTEP.BoneData> keyBones = new List<MTEP.BoneData>();
            /// <summary>表示範囲のサンプル値 (SAMPLE_STEP px 刻み)</summary>
            public List<float> samples = new List<float>();

            /// <summary>このチャンネルに効く 1フレーム調整の種別</summary>
            public MTEP.SingleFrameType singleFrameType = MTEP.SingleFrameType.None;
            /// <summary>再生時の区間フレーム (1フレーム調整の反映後)。
            /// 添字 i は values[i] → values[i+1] の区間で、要素数はキー数 - 1。
            /// 1 フレームしかない区間は隣へ潰されるため、
            /// キーのフレーム番号とは一致しないことがある</summary>
            public List<int> segmentStFrames = new List<int>();
            public List<int> segmentEdFrames = new List<int>();

            public bool isEulerDisplay => kind == ChannelKind.EulerDisplay;

            /// <summary>Euler 表示チャンネルのキーごとのクォータニオン成分 (x,y,z,w)</summary>
            public List<MTEP.ValueData[]> rotationValues = new List<MTEP.ValueData[]>();

            /// <summary>Euler 表示チャンネルの軸 (0=X, 1=Y, 2=Z)。EulerDisplay 以外は -1</summary>
            public int eulerAxis = -1;
            /// <summary>Euler 表示チャンネルのキー表示値 (連続化済みの角度、度)</summary>
            public List<float> eulerKeyValues = new List<float>();

            /// <summary>キー位置の表示値。Euler 表示では連続化済みの角度 (度)</summary>
            public float GetKeyValue(int i)
            {
                return isEulerDisplay ? eulerKeyValues[i] : values[i].value;
            }

            /// <summary>
            /// キー i の片側タンジェント。通常チャンネルは代表値の 1 本。
            /// Euler 表示は表示値から 4 成分への逆変換ができないため、
            /// 同じ正規化値を回転 4 成分すべてへ反映する (4 本を返す)
            /// </summary>
            public MTEP.TangentData[] GetTangents(int i, bool isOut)
            {
                if (!isEulerDisplay)
                {
                    return new[] { isOut ? values[i].outTangent : values[i].inTangent };
                }

                var rotation = rotationValues[i];
                var result = new MTEP.TangentData[rotation.Length];
                for (var k = 0; k < rotation.Length; k++)
                {
                    result[k] = isOut ? rotation[k].outTangent : rotation[k].inTangent;
                }
                return result;
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

        /// <summary>開閉トグルを持つバーの描画。高さ変更はバー上辺のドラッグ (TimelineWindow 側) で行う</summary>
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

            // 手動ズーム中だけ出す。自動フィット中は押しても何も変わらないため
            if (isOpen && isValueRangeManual)
            {
                view.currentPos = new Vector2(barRect.x + 5 + TOGGLE_BUTTON_WIDTH + 5, barRect.y);
                if (view.DrawButton("フィット", FIT_BUTTON_WIDTH, TOGGLE_BAR_HEIGHT))
                {
                    ResetValueRange();
                }
            }

            if (isOpen)
            {
                _targets.Update(selectedBones);
            }
        }

        /// <summary>ペイン高さを範囲内へ丸めて設定する</summary>
        public void SetPaneHeight(float height)
        {
            var clamped = Mathf.Clamp((int)height, MIN_PANE_HEIGHT, MAX_PANE_HEIGHT);
            if (config.curveEditorHeight == clamped) return;
            config.curveEditorHeight = clamped;
            config.dirty = true;
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
            _valueTypeComboBox.items = _targets.items;
            // buttonSize は矢印を含まないため、矢印の分を引いて 1 行に収める
            _valueTypeComboBox.buttonSize = new Vector2(
                Mathf.Max(20f, width - COMBO_ARROW_WIDTH), TOOL_ROW_HEIGHT);
            _valueTypeComboBox.currentIndex = _targets.currentIndex;
            _valueTypeComboBox.onSelected = (target, index) => _targets.Select(target);
            _valueTypeComboBox.DrawButton(view);
            y += TOOL_ROW_HEIGHT + TOOL_ROW_SPACING;

            if (selectedBones.Count == 0)
            {
                return;
            }

            // 正規化タンジェント (線形勾配比、1=線形) の直接入力。
            // Inspector の補間曲線タブと同じく Out → In の順に並べる
            DrawTangentRow(view, x, ref y, width, "Out", true, _outTangentFieldCache);
            DrawTangentRow(view, x, ref y, width, "In", false, _inTangentFieldCache);

            var isSmooth = IsAllTangentSmooth();
            view.currentPos = new Vector2(x, y);
            view.DrawToggle("自動補間", isSmooth, width, TOOL_ROW_HEIGHT, newIsSmooth =>
            {
                ForEachTangent((tangent, isOut) => tangent.isSmooth = newIsSmooth);
                currentLayer.ApplyCurrentFrame(true);
                MTEP.TimelineHistoryManager.instance.AddHistory(timeline, "カーブ: 自動補間");
            });
            y += TOOL_ROW_HEIGHT + TOOL_ROW_SPACING;

            DrawPresetThumbnails(view, x, ref y, width, toolbarRect);
        }

        /// <summary>
        /// タンジェント 1 行。ラベルを左右ドラッグすると差分編集、数値欄への入力で絶対値編集。
        /// 選択内で値が混在していると数値欄は空欄 (NaN) になるが、差分編集は効かせたいので
        /// 絶対値しか渡さない DrawDragFloatField ではなく DrawDragLabel を直接使う
        /// </summary>
        private void DrawTangentRow(
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

            var diff = 0f;
            // 値が変わらない区間しか選ばれていなければ、ハンドルと同じく編集させない
            var wasEnabled = view.guiEnabled;
            view.SetEnabled(wasEnabled && HasEditableTangent(isOut));

            view.currentPos = new Vector2(x, y);
            view.BeginHorizontal();
            {
                view.DrawDragLabel(
                    label, TANGENT_LABEL_WIDTH, TOOL_ROW_HEIGHT,
                    TANGENT_DRAG_SENSITIVITY, value => diff += value);

                view.DrawFloatField(new GUIView.FloatFieldOption
                {
                    value = current,
                    fieldCache = fieldCache,
                    width = Mathf.Max(20f, width - TANGENT_LABEL_WIDTH),
                    height = TOOL_ROW_HEIGHT,
                    onChanged = value => ApplyNormalizedTangent(isOut, value),
                });
            }
            view.EndLayout();

            view.SetEnabled(wasEnabled);
            y += TOOL_ROW_HEIGHT + TOOL_ROW_SPACING;

            // 各タンジェントへ個別に足すので、混在 (空欄) でも値の差を保ったまま相対変更できる
            if (diff != 0f)
            {
                AddNormalizedTangent(isOut, diff);
            }
        }

        /// <summary>プリセットの曲線サムネを横一列に並べ、幅が足りない分だけ折り返す。
        /// サムネ一辺は全数が 1 行に収まる大きさにするので、
        /// 既定の menuWidth では折り返さず 1 行で並ぶ</summary>
        private void DrawPresetThumbnails(
            GUIView view, float x, ref float y, float width, Rect toolbarRect)
        {
            if (_presetTextures == null)
            {
                _presetTextures = TangentCurveTexture.CreatePresetTextures(
                    PRESET_TEX_SIZE, config.curveBgColor, config.curveLineColor);
            }

            var count = _presetTextures.Length;
            // 1 行へ詰めた一辺。下限を割るほど幅が狭いときだけ、下限のまま折り返しに任せる。
            // 端数を切り捨てて、合計幅が丸め誤差で width を越えないようにする
            var size = Mathf.Floor(Mathf.Clamp(
                (width - TOOL_ROW_SPACING * (count - 1)) / count,
                PRESET_DRAW_SIZE_MIN,
                PRESET_DRAW_SIZE_MAX));
            // ツールバーの残り高さからはみ出さないよう、1 行ぶんの高さにも収める
            var remainHeight = toolbarRect.yMax - TOOL_PADDING_Y - y;
            size = Mathf.Max(PRESET_DRAW_SIZE_MIN, Mathf.Min(size, remainHeight));

            var drawX = x;
            for (var i = 0; i < count; i++)
            {
                // 右端を越えるなら次の行へ送る (行頭の 1 個は必ずその行に置く)
                if (drawX > x && drawX + size > x + width)
                {
                    drawX = x;
                    y += size + TOOL_ROW_SPACING;
                }

                var tangentType = (MTEP.TangentType)i;
                view.currentPos = new Vector2(drawX, y);
                view.DrawTexture(
                    _presetTextures[i], size, size, Color.white, EventType.MouseDown,
                    _ => ApplyTangentPreset(tangentType));

                drawX += size + TOOL_ROW_SPACING;
            }

            y += size + TOOL_ROW_SPACING;
        }

        /// <summary>編集区間のうち、片側タンジェントが実際に効くものがあるか。
        /// 値が変わらない区間では TangentData.value が normalizedValue によらず 0 になり
        /// (UpdateValue: value = normalizedValue * baseTangent)、編集しても形が変わらない。
        /// ハンドルを出さない条件と揃えて、入力欄もこの条件で伏せる</summary>
        private bool HasEditableTangent(bool isOut)
        {
            foreach (var channel in _channels)
            {
                for (var i = 0; i < channel.values.Count; i++)
                {
                    if (IsTangentInEditRange(channel, i, isOut)
                        && TryGetBaseSlopePerFrame(channel, i, isOut, out _))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>編集区間の片側タンジェントが全て同値ならその正規化値、
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

        /// <summary>編集区間の片側タンジェントへ正規化値を一括適用する</summary>
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

        /// <summary>編集区間の片側タンジェントへ正規化値の差分を加算する</summary>
        private void AddNormalizedTangent(bool isOut, float diff)
        {
            ForEachTangent((tangent, tangentIsOut) =>
            {
                if (tangentIsOut != isOut)
                {
                    return;
                }
                tangent.normalizedValue += diff;
                tangent.isSmooth = false;
            });

            currentLayer.ApplyCurrentFrame(true);
            // ドラッグ中は毎フレーム呼ばれるため、履歴はマウスを離すまで集約させる
            timelineManager.RequestHistory("カーブ: タンジェント入力 " + (isOut ? "Out" : "In"));
        }

        /// <summary>編集区間 (前キー → 選択キー) のタンジェントを走査する。
        /// out は前キー側、in は選択キー側で、Inspector の補間曲線タブと同じ組み合わせ
        /// (KeyFrameTangentDrawer.CollectTangents)</summary>
        private void ForEachTangent(System.Action<MTEP.TangentData, bool> callback)
        {
            var target = _targets.current;

            foreach (var bone in selectedBones)
            {
                var transform = bone.transform;
                if (transform == null || bone.parentLayer != currentLayer)
                {
                    continue;
                }

                // 前キーが無い先頭キーは区間を成さないので編集対象にしない。
                // loopSearch を既定 (true) にすると、前キーが無くても末尾や自分自身へ
                // 回り込んだボーンが返り、ハンドルの出ない区間まで編集対象に入ってしまう
                var prevBone = currentLayer.GetPrevBone(bone.frameNo, bone.name, false);
                if (prevBone == null)
                {
                    continue;
                }

                foreach (var tangent in TangentTargetList.GetTangents(prevBone.transform, target, isOut: true))
                {
                    callback(tangent, true);
                }

                foreach (var tangent in TangentTargetList.GetTangents(transform, target, isOut: false))
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

        private void ApplyTangentPreset(MTEP.TangentType tangentType)
        {
            var tangentPair = MTEP.TangentPair.GetDefault(tangentType);

            ForEachTangent((tangent, isOut) =>
            {
                tangent.normalizedValue = isOut ? tangentPair.outTangent : tangentPair.inTangent;
                tangent.isSmooth = tangentPair.isSmooth;
            });

            currentLayer.ApplyCurrentFrame(true);
            MTEP.TimelineHistoryManager.instance.AddHistory(
                timeline,
                "カーブ: プリセット " + MTEP.TangentData.TangentTypeNames[(int)tangentType]);
        }

        /// <summary>カーブ描画領域。paneRect はウィンドウローカル座標</summary>
        public void DrawPane(GUIView view, Rect paneRect, float scrollX, float frameWidth, bool guiEnabled)
        {
            view.currentPos = new Vector2(paneRect.x, paneRect.y);
            view.DrawTexture(GUIView.texWhite, paneRect.width, paneRect.height, config.curveBgColor);

            // 毎パス作り直すと数百 KB/frame 確保するため、構築入力が変わったときだけ再収集する。
            // 値・タンジェントは ValueData の参照を共有しているので、その場編集は再構築なしで反映される
            var signature = ComputeChannelsSignature();
            var channelsRebuilt = false;
            if (!_channelsValid || signature != _channelsSignature)
            {
                _channels = CollectChannels(out _totalChannelCount);
                _channelsSignature = signature;
                _channelsValid = true;
                channelsRebuilt = true;

                // キーの追加・削除でも再構築は走るが、そこでズームを捨てると
                // ズームしたままキーを打つ操作が成り立たないため、表示対象の組が変わったときだけ戻す
                var viewKey = BuildValueRangeViewKey(_channels);
                if (viewKey != _valueRangeViewKey)
                {
                    _valueRangeViewKey = viewKey;
                    ResetValueRange();
                }
            }
            var totalChannelCount = _totalChannelCount;

            if (_channels.Count == 0)
            {
                _mapping = null;
                view.currentPos = new Vector2(paneRect.x + 8, paneRect.y + 4);
                // 種別フィルタの絞り込みで 0 本になった場合を選択なしと取り違えないよう文言を分ける
                var message = selectedBones.Count == 0
                    ? "カーブ対象のキーフレームが選択されていません"
                    : string.Format("選択中のボーンに「{0}」の値がありません", _targets.current.name);
                view.DrawLabel(message, 400, 20, Color.gray);
                return;
            }

            // 凡例はペイン下端の情報表示行なので、グラフはその分だけ縮めた領域に描く。
            // ヒットテストも同じ領域で行うため、マッピング構築より先に高さを決める
            var paneSize = new Vector2(paneRect.width, paneRect.height);
            if (_legend == null || channelsRebuilt || paneSize != _legendPaneSize)
            {
                _legend = BuildLegend(paneRect);
                _legendPaneSize = paneSize;
            }
            var graphRect = new Rect(
                paneRect.x, paneRect.y, paneRect.width, paneRect.height - _legend.height);

            // 表示中の座標系は Repaint 時に確定させ、他イベントは同じ座標系でヒットテストする。
            // Euler 表示のキー値も BuildMapping 内で同じタイミングにだけ更新される
            if (Event.current.type == EventType.Repaint || _mapping == null)
            {
                _mapping = BuildMapping(_channels, graphRect, scrollX, frameWidth, _mappingValues);
                // サンプル収集はカーブ描画に要るので BuildMapping は常に通し、値域だけ差し替える
                if (_manualValueMin.HasValue)
                {
                    _mapping = new MTEP.CurveViewMapping(
                        frameWidth, graphRect.height, _manualValueMin.Value, _manualValueMax.Value);
                }
            }

            if (Event.current.type == EventType.Repaint)
            {
                DrawValueScale(view, graphRect);

                foreach (var channel in _channels)
                {
                    DrawChannelCurve(view, channel, graphRect);
                }

                foreach (var channel in _channels)
                {
                    DrawChannelKeys(view, channel, graphRect, scrollX);
                    DrawChannelHandles(view, channel, graphRect, scrollX);
                }

                // 凡例はカーブより手前に重ねる
                DrawLegend(view, paneRect, _legend);

                if (totalChannelCount > _channels.Count)
                {
                    view.currentPos = new Vector2(paneRect.x + 8, paneRect.y + 2);
                    view.DrawLabel(
                        string.Format("表示上限 {0} チャンネル (全 {1})", MAX_CHANNELS, totalChannelCount),
                        260, 18, Color.gray);
                }
            }

            // 表示の拡縮は値を変えないので、編集不可の間も受け付ける
            HandleViewInput(view, graphRect);

            if (guiEnabled)
            {
                HandleInput(view, graphRect, scrollX);
            }
            else if (_dragMode != DragMode.None)
            {
                // 操作不能中は MouseUp を拾えない。次に有効化されたとき古い対象を
                // 掴み続けないよう、ここでドラッグを打ち切る
                EndDrag();
            }
        }

        /// <summary>自動フィットへ戻す</summary>
        public void ResetValueRange()
        {
            _manualValueMin = null;
            _manualValueMax = null;
        }

        private void SetManualRange(MTEP.CurveViewMapping mapping)
        {
            _manualValueMin = mapping.valueMin;
            _manualValueMax = mapping.valueMax;
            _mapping = mapping;
        }

        /// <summary>チャンネルの種別と対象ボーンの組。キー数には依存させない</summary>
        private static string BuildValueRangeViewKey(List<CurveChannel> channels)
        {
            var sb = new StringBuilder();
            foreach (var channel in channels)
            {
                sb.Append(channel.displayName).Append('|');
                string lastBone = null;
                foreach (var bone in channel.keyBones)
                {
                    if (bone != null && bone.name != lastBone)
                    {
                        lastBone = bone.name;
                        sb.Append(lastBone).Append(',');
                    }
                }
                sb.Append(';');
            }
            return sb.ToString();
        }

        /// <summary>
        /// 縦方向の表示操作。ホイールでカーソル位置を軸に拡縮し、中ボタンドラッグでパンする。
        /// Ctrl 付きのホイールは横ズームとして TimelineWindow が扱う
        /// </summary>
        private void HandleViewInput(GUIView view, Rect graphRect)
        {
            var origin = view.GetDrawRect(graphRect.x, graphRect.y, 1f, 1f);
            var mouse = Event.current.mousePosition - new Vector2(origin.x, origin.y);
            var e = Event.current;
            var inGraph = mouse.x >= 0f && mouse.x <= graphRect.width
                && mouse.y >= 0f && mouse.y <= graphRect.height;

            if (_isPanning)
            {
                if (!Input.GetMouseButton(2) || _mapping == null)
                {
                    _isPanning = false;
                }
                else if (e.type == EventType.MouseDrag)
                {
                    SetManualRange(_mapping.PanValue(mouse.y - _panLastY));
                    _panLastY = mouse.y;
                    e.Use();
                }
                return;
            }

            if (!inGraph || _mapping == null)
            {
                return;
            }

            if (e.type == EventType.MouseDown && e.button == 2)
            {
                _isPanning = true;
                _panLastY = mouse.y;
                e.Use();
                return;
            }

            if (MTEP.TimelineZoomMath.IsControlHeld())
            {
                return;
            }

            var wheel = MTEP.TimelineZoomMath.ConsumeWheel(ref _wheelZoomFrame);
            if (wheel == 0f)
            {
                return;
            }
            // 奥へ回す (プラス) と拡大。1 ノッチ 1.2 倍
            var factor = wheel > 0f ? WHEEL_ZOOM_FACTOR : 1f / WHEEL_ZOOM_FACTOR;
            SetManualRange(_mapping.ZoomValue(factor, mouse.y));
        }

        /// <summary>キー点とタンジェントハンドルのドラッグ処理。
        /// graphRect は凡例行を除いたグラフ領域 (描画と同じ座標系)</summary>
        private void HandleInput(GUIView view, Rect graphRect, float scrollX)
        {
            // graphRect はビューローカル。イベント座標系へ合わせるため原点を変換する
            var origin = view.GetDrawRect(graphRect.x, graphRect.y, 1f, 1f);
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
            if (mouse.x < 0f || mouse.x > graphRect.width || mouse.y < 0f || mouse.y > graphRect.height)
            {
                return;
            }

            // ハンドルはキーの上に描かれるため先にヒットテストする
            foreach (var channel in _channels)
            {
                for (var i = 0; i < channel.values.Count; i++)
                {
                    for (var side = 0; side < 2; side++)
                    {
                        var isOut = side == 0;
                        if (!IsTangentInEditRange(channel, i, isOut))
                        {
                            continue;
                        }
                        if (!TryGetBaseSlopePerFrame(channel, i, isOut, out var baseSlope))
                        {
                            continue;
                        }

                        var handlePos = GetHandlePos(channel, i, isOut, scrollX);
                        if (Vector2.Distance(handlePos, mouse) > HANDLE_HIT_RADIUS)
                        {
                            continue;
                        }

                        _dragMode = DragMode.Tangent;
                        _dragValue = channel.values[i];
                        _dragTangents = channel.GetTangents(i, isOut);
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
                // 値ドラッグだけは不可。Euler 表示値から回転 4 成分への逆変換が無いため
                if (channel.isEulerDisplay)
                {
                    continue;
                }

                for (var i = 0; i < channel.values.Count; i++)
                {
                    // 半透明表示の非選択キーを誤ってドラッグしないよう選択キーだけに絞る
                    if (!IsKeySelected(channel, i))
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
            if (_dragTangents.Length == 0)
            {
                return;
            }
            var current = _dragTangents[0];
            if (normalized == current.normalizedValue && !current.isSmooth)
            {
                return;
            }

            foreach (var tangent in _dragTangents)
            {
                tangent.normalizedValue = normalized;
                tangent.isSmooth = false;
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
            _dragTangents = null;
            _dragChanged = false;
        }

        /// <summary>選択中のキーか (キー自体のドラッグ可否)</summary>
        private bool IsKeySelected(CurveChannel channel, int keyIndex)
        {
            return selectedBones.Contains(channel.keyBones[keyIndex]);
        }

        /// <summary>そのキーの片側タンジェントが編集区間に属するか。
        /// 編集区間は Inspector と同じく「前キー → 選択キー」なので、
        /// in は選択キー自身、out は次キーが選択されているキーが対象になる。
        /// チャンネルの配列はフレーム昇順なので隣の添字が隣のキーになり、
        /// ForEachTangent の GetPrevBone と同じ区間を指す。
        /// 実際に編集が効くかは別途 TryGetBaseSlopePerFrame でも絞る
        /// (勾配 0 や 1フレーム調整の区間はここでは弾かない)</summary>
        private bool IsTangentInEditRange(CurveChannel channel, int keyIndex, bool isOut)
        {
            var selectedIndex = isOut ? keyIndex + 1 : keyIndex;
            return selectedIndex < channel.values.Count
                && IsKeySelected(channel, selectedIndex);
        }

        /// <summary>キーを積み終えたチャンネルへ、再生時の区間情報を持たせる。
        /// 1フレーム調整はレイヤーと transform の種別ごとに変わるので、
        /// チャンネルの元になった transform から引く。
        /// なお MotionData.stFrameActive はポーズ編集中だけ調整前のフレームを使うが、
        /// カーブは編集中も再生時の形を見せたいので isPoseEditing では切り替えない</summary>
        private static void FinalizeChannel(CurveChannel channel, MTEP.ITransformData transform)
        {
            channel.singleFrameType = currentLayer.GetSingleFrameType(transform.type);
            BuildSegmentFrames(channel);
        }

        /// <summary>
        /// 再生時の区間フレームを求める (MotionPlayData.Setup の移植)。
        /// 1 フレームしかない区間は隣の区間へ潰され、そこで値が瞬間的に切り替わる。
        /// Delay は手前の区間を伸ばして切り替えを区間の終端へ、
        /// Advance は次の区間を伸ばして切り替えを始端へ寄せる。
        /// 最後の区間は Setup の走査対象外なので潰れない。
        /// prev を i > 1 からしか見ないのも Setup と揃えてある (表示を再生に一致させるため)
        /// </summary>
        private static void BuildSegmentFrames(CurveChannel channel)
        {
            var stFrames = channel.segmentStFrames;
            var edFrames = channel.segmentEdFrames;
            stFrames.Clear();
            edFrames.Clear();

            for (var i = 0; i < channel.frameNos.Count - 1; i++)
            {
                stFrames.Add(channel.frameNos[i]);
                edFrames.Add(channel.frameNos[i + 1]);
            }

            if (channel.singleFrameType == MTEP.SingleFrameType.None)
            {
                return;
            }

            for (var i = 0; i < stFrames.Count - 1; i++)
            {
                if (!IsSingleFrameSegment(channel, i))
                {
                    continue;
                }

                if (channel.singleFrameType == MTEP.SingleFrameType.Delay)
                {
                    if (i > 1)
                    {
                        edFrames[i - 1] = edFrames[i];
                    }
                    stFrames[i] = edFrames[i];
                }
                else
                {
                    stFrames[i + 1] = stFrames[i];
                    edFrames[i] = stFrames[i];
                }
            }
        }

        /// <summary>1フレーム調整で潰される区間か。区間添字は始端キーの添字と同じ</summary>
        private static bool IsSingleFrameSegment(CurveChannel channel, int segmentIndex)
        {
            return MTEP.SingleFrameInterval.IsCollapsed(
                channel.singleFrameType, channel.frameNos, segmentIndex);
        }

        /// <summary>フレーム位置 frameNo を含む区間の添字。
        /// 始端が frameNo 以下で最後のものを選ぶ (PlayDataBase.Update と同じ)。
        /// 潰れて長さ 0 になった区間は、同じ始端を持つ後続の区間に必ず追い越される</summary>
        private static int FindSegmentIndex(CurveChannel channel, float frameNo)
        {
            // 始端は昇順なので二分探索する (BuildMapping がサンプル列 × チャンネル数だけ呼ぶ)。
            // 潰れた区間は始端が後続と同値になるため、等しい中でも後ろを選ぶ
            var lo = 0;
            var hi = channel.segmentStFrames.Count - 1;
            while (lo < hi)
            {
                var mid = (lo + hi + 1) / 2;
                if (channel.segmentStFrames[mid] <= frameNo)
                {
                    lo = mid;
                }
                else
                {
                    hi = mid - 1;
                }
            }
            return lo;
        }

        /// <summary>ハンドルの相手側キー (out は次キー、in は前キー)。
        /// 同フレームに重なったキーは区間を成さないため対象外。
        /// 区間の値が変わらないキーもハンドルは描くので、勾配の大小は問わない</summary>
        private static bool TryGetNeighborIndex(
            CurveChannel channel, int keyIndex, bool isOut, out int otherIndex)
        {
            otherIndex = isOut ? keyIndex + 1 : keyIndex - 1;
            if (otherIndex < 0 || otherIndex >= channel.values.Count)
            {
                return false;
            }

            return channel.frameNos[otherIndex] != channel.frameNos[keyIndex];
        }

        /// <summary>タンジェント正規化の基準となる区間線形勾配 (値/フレーム)。
        /// UpdateTangent と同じく inTangent は流入区間、outTangent は流出区間を基準にする。
        /// 勾配 0 の区間では TangentData.value が normalizedValue によらず 0 になり
        /// (UpdateValue: value = normalizedValue * baseTangent)、正規化も逆算もできない。
        /// 1フレーム調整で潰される区間も再生時に補間されない。
        /// どちらもタンジェントが効かないので false を返し、ハンドルと入力欄を伏せる</summary>
        private static bool TryGetBaseSlopePerFrame(
            CurveChannel channel, int keyIndex, bool isOut, out float baseSlope)
        {
            baseSlope = 0f;

            if (!TryGetNeighborIndex(channel, keyIndex, isOut, out var otherIndex))
            {
                return false;
            }

            // 2 キーは隣接しているので、添字の小さい方がその間の区間の添字になる
            if (IsSingleFrameSegment(channel, Mathf.Min(keyIndex, otherIndex)))
            {
                return false;
            }

            var dtFrames = channel.frameNos[otherIndex] - channel.frameNos[keyIndex];
            baseSlope = (channel.GetKeyValue(otherIndex) - channel.GetKeyValue(keyIndex)) / dtFrames;
            return baseSlope != 0f;
        }

        /// <summary>ハンドル先端のペイン内座標。
        /// 勾配は「そのチャンネルの表示値での区間勾配 × 正規化値」で求める。
        /// TangentData.value は成分の値域で計算されており Euler 表示の角度とは
        /// スケールが違うため、value ではなく normalizedValue から組み立てる</summary>
        private Vector2 GetHandlePos(
            CurveChannel channel, int keyIndex, bool isOut, float scrollX)
        {
            var keyX = _mapping.FrameToX(channel.frameNos[keyIndex]) - scrollX;
            var keyY = _mapping.ValueToY(channel.GetKeyValue(keyIndex));

            var tangents = channel.GetTangents(keyIndex, isOut);
            var normalized = tangents.Length > 0 ? tangents[0].normalizedValue : 0f;

            // 失敗時は 0 が入る (隣接キー無し・単一フレーム区間・勾配 0)
            TryGetBaseSlopePerFrame(channel, keyIndex, isOut, out var baseSlopePerFrame);

            var slopePerFrame = normalized * baseSlopePerFrame;
            var pxPerValue = _mapping.paneHeight / (_mapping.valueMax - _mapping.valueMin);

            var dx = isOut ? _mapping.frameWidth : -_mapping.frameWidth;
            var dy = -slopePerFrame * pxPerValue * (isOut ? 1f : -1f);

            var dir = new Vector2(dx, dy).normalized;
            return new Vector2(keyX, keyY) + dir * HANDLE_LEN;
        }

        /// <summary>
        /// CollectChannels の入力 (レイヤー・表示種別・選択ボーン・選択ボーン名のキー列) を
        /// 確保なしで要約したハッシュ。キー列は FrameData / BoneData / ITransformData の参照で見るので、
        /// キーの追加・削除・移動や Undo (オブジェクト再生成) で変わる。
        /// 値そのものは含めない (チャンネルが参照を共有していて再構築が要らないため)。
        /// 衝突すると次に入力が変わるまで古いチャンネルを描き続けるので、確率を下げるため 64bit で畳む
        /// </summary>
        private long ComputeChannelsSignature()
        {
            var layer = currentLayer;
            if (layer == null || selectedBones.Count == 0)
            {
                return 0;
            }

            unchecked
            {
                long hash = RuntimeHelpers.GetHashCode(layer);
                var target = _targets.current;
                hash = hash * 31 + (int)target.valueType;
                hash = hash * 31 + (target.customKey != null ? target.customKey.GetHashCode() : 0);

                // 選択は順序に依存しないので XOR で束ねる
                var selectionHash = 0;
                _signatureBoneNames.Clear();
                foreach (var bone in selectedBones)
                {
                    if (bone.parentLayer != layer)
                    {
                        continue;
                    }
                    selectionHash ^= RuntimeHelpers.GetHashCode(bone);
                    if (!_signatureBoneNames.Contains(bone.name))
                    {
                        _signatureBoneNames.Add(bone.name);
                    }
                }
                hash = hash * 31 + selectionHash;
                hash = hash * 31 + selectedBones.Count;

                var keyFrameCount = layer.keyFrameCount;
                for (var i = 0; i < keyFrameCount; i++)
                {
                    var frame = layer.GetKeyFrameAt(i);
                    for (var n = 0; n < _signatureBoneNames.Count; n++)
                    {
                        var bone = frame.GetBone(_signatureBoneNames[n]);
                        if (bone == null || bone.transform == null)
                        {
                            continue;
                        }
                        hash = hash * 31 + frame.frameNo;
                        hash = hash * 31 + RuntimeHelpers.GetHashCode(bone);
                        hash = hash * 31 + RuntimeHelpers.GetHashCode(bone.transform);
                        // 区間の潰れ方 (BuildSegmentFrames) は 1フレーム調整の設定にも依存する
                        hash = hash * 31 + (int)layer.GetSingleFrameType(bone.transform.type);
                    }
                }
                return hash;
            }
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

            // 選択順に依存しないよう、ボーン名は初出順で束ねる。
            // 選択は複数レイヤーにまたがるが、カーブはアクティブレイヤー分だけ表示する
            var boneNames = new List<string>();
            foreach (var bone in selectedBones)
            {
                if (bone.parentLayer != currentLayer)
                {
                    continue;
                }
                if (!boneNames.Contains(bone.name))
                {
                    boneNames.Add(bone.name);
                }
            }

            var target = _targets.current;
            var valueTypes = target.isCustom
                ? new List<MTEP.TangentValueType>()
                : ExpandValueTypes(target.valueType);

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

                // クォータニオン格納の回転は成分カーブを出さず、表示用 Euler へ置き換える
                var isQuaternionRotation = firstTransform.hasRotation;
                if (isQuaternionRotation && !target.isCustom)
                {
                    AddQuaternionRotationChannels(
                        channels, ref totalChannelCount, frameNos, bones);
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
                        FinalizeChannel(channel, firstTransform);
                        channels.Add(channel);
                    }
                }

                // カスタム値チャンネル (TangentValueType に含まれないためキー名で収集する)。
                // 「すべて」では全部、カスタム値を名指しで選んでいるときはそれ 1 本だけ出す
                if (!target.isCustom && target.valueType != MTEP.TangentValueType.すべて)
                {
                    continue;
                }

                var customIndex = 0;
                foreach (var customValue in firstTransform.GetCustomValueInfoMap())
                {
                    var customKey = customValue.Key;
                    // 色の割り当ては「すべて」表示時と揃えたいので、
                    // 名指しで絞る場合も customIndex は全キーぶん先に進める
                    customIndex++;
                    if (target.isCustom && customKey != target.customKey)
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
                        FinalizeChannel(channel, firstTransform);
                        channels.Add(channel);
                    }
                }
            }

            return channels;
        }

        /// <summary>クォータニオン格納ボーンの回転チャンネルを追加する。
        /// 回転/すべて → 表示用 Euler 3 本、X/Y/Z回転 → 該当軸の Euler 表示 1 本。
        /// Euler 表示は導出値のため値は編集できない (タンジェントは編集できる)</summary>
        private void AddQuaternionRotationChannels(
            List<CurveChannel> channels, ref int totalChannelCount,
            List<int> frameNos, List<MTEP.BoneData> bones)
        {
            var target = _targets.current;
            var addAllEulers = target.valueType == MTEP.TangentValueType.すべて
                || target.valueType == MTEP.TangentValueType.回転;
            var isAxisFilter = IsAxisRotationType(target.valueType);
            if (!addAllEulers && !isAxisFilter)
            {
                return;
            }

            var sourceChannel = BuildQuaternionSourceChannel(frameNos, bones);
            if (sourceChannel == null)
            {
                return;
            }

            totalChannelCount += (addAllEulers ? 3 : 0) + (isAxisFilter ? 1 : 0);

            if (addAllEulers)
            {
                for (var axis = 0; axis < 3 && channels.Count < MAX_CHANNELS; axis++)
                {
                    channels.Add(BuildEulerDisplayChannel(sourceChannel, axis));
                }
            }
            if (isAxisFilter && channels.Count < MAX_CHANNELS)
            {
                channels.Add(BuildEulerDisplayChannel(
                    sourceChannel, GetAxisIndex(target.valueType)));
            }
        }

        /// <summary>クォータニオン格納ボーンの回転から Euler 表示チャンネルの元データを作る。
        /// このチャンネル自体は表示せず、BuildEulerDisplayChannel の入力にのみ使う。
        /// キーが 1 つも拾えなければ null</summary>
        private static CurveChannel BuildQuaternionSourceChannel(
            List<int> frameNos, List<MTEP.BoneData> bones)
        {
            var channel = new CurveChannel
            {
                kind = ChannelKind.EulerDisplay,
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
            FinalizeChannel(channel, bones[0].transform);
            return channel;
        }

        /// <summary>元データチャンネルのキー列を流用して表示用 Euler チャンネルを作る。
        /// キー表示値は前キーと連続になるよう 360° 単位で寄せる (unwrap)。
        /// Euler 表現自体の切り替わり (ジンバル付近) までは補正しない表示近似</summary>
        private static CurveChannel BuildEulerDisplayChannel(CurveChannel source, int axis)
        {
            var channel = new CurveChannel
            {
                kind = ChannelKind.EulerDisplay,
                eulerAxis = axis,
                displayName = "Euler" + "XYZ"[axis],
                color = GetAxisColor(axis),
                frameNos = source.frameNos,
                values = source.values,
                keyBones = source.keyBones,
                rotationValues = source.rotationValues,
                // キー列が同じなら区間の潰れ方も同じなので、元データのものをそのまま使う
                singleFrameType = source.singleFrameType,
                segmentStFrames = source.segmentStFrames,
                segmentEdFrames = source.segmentEdFrames,
            };

            RefreshEulerKeyValues(channel);
            return channel;
        }

        /// <summary>Euler 表示チャンネルのキー表示値を回転成分から計算し直す。
        /// 回転成分は他所でその場編集されうるため、チャンネル再構築をスキップする Repaint パスでも呼び直す</summary>
        private static void RefreshEulerKeyValues(CurveChannel channel)
        {
            channel.eulerKeyValues.Clear();
            var prev = 0f;
            for (var k = 0; k < channel.rotationValues.Count; k++)
            {
                var raw = GetEulerAngle(ToQuaternion(channel.rotationValues[k]), channel.eulerAxis);
                var value = k == 0 ? raw : UnwrapAngle(raw, prev);
                channel.eulerKeyValues.Add(value);
                prev = value;
            }
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

        /// <summary>複合型の値種別を単チャンネル型へ展開する</summary>
        private static List<MTEP.TangentValueType> ExpandValueTypes(MTEP.TangentValueType valueType)
        {
            switch (valueType)
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
                    return new List<MTEP.TangentValueType> { valueType };
            }
        }

        /// <summary>表示範囲をサンプリングして縦軸マッピングを決める</summary>
        private static MTEP.CurveViewMapping BuildMapping(
            List<CurveChannel> channels, Rect graphRect, float scrollX, float frameWidth,
            List<float> allValues)
        {
            var columnCount = Mathf.Max(2, (int)(graphRect.width / SAMPLE_STEP) + 1);
            allValues.Clear();

            foreach (var channel in channels)
            {
                if (channel.isEulerDisplay)
                {
                    RefreshEulerKeyValues(channel);
                }
                channel.samples.Clear();
                for (var i = 0; i < columnCount; i++)
                {
                    var frameNo = (scrollX + i * SAMPLE_STEP) / frameWidth;
                    var value = EvaluateChannel(channel, frameNo);
                    channel.samples.Add(value);
                    allValues.Add(value);
                }
            }

            return MTEP.CurveViewMapping.AutoFit(frameWidth, graphRect.height, allValues);
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

            if (channel.isEulerDisplay)
            {
                return EvaluateEulerDisplay(channel, frameNo);
            }

            var index = FindSegmentIndex(channel, frameNo);
            return EvaluateSegment(
                channel.segmentStFrames[index], channel.values[index],
                channel.segmentEdFrames[index], channel.values[index + 1],
                frameNo);
        }

        /// <summary>区間 [frameA, frameB] 内のフレーム位置 f における実値を返す。
        /// frameA/frameB は 1フレーム調整の反映後 (segmentStFrames / segmentEdFrames)。
        /// 再生経路 (MoveTimelineLayer.ApplyMotionUpdateTangent) と同じ単位系:
        /// t0/t1 は秒、t は 0〜1 正規化、タンジェントは値/秒</summary>
        private static float EvaluateSegment(
            int frameA, MTEP.ValueData a,
            int frameB, MTEP.ValueData b,
            float f)
        {
            var dtFrames = frameB - frameA;
            if (dtFrames <= 0) return a.value;
            // 区間が伸ばされると端の外を引かれることがある (PlayDataBase.CalcLerpFrame と同じ)
            var t = Mathf.Clamp01((f - frameA) / dtFrames);

            // 再生と同一形状にするため t0/t1 は秒単位で渡す (単位系の原則を参照)
            var frameDuration = timeline.frameDuration;
            return MTEP.PluginUtils.Hermite(
                frameA * frameDuration, frameB * frameDuration,
                a.value, b.value,
                a.outTangent.value, b.inTangent.value, t);
        }

        /// <summary>Euler 表示チャンネルの実値 (度)。区間内はクォータニオン 4 成分を
        /// 再生経路と同じ Hermite で補間してから Euler 化し、キー値を線形でつないだ
        /// 基準角に最も近い周回へ寄せて連続性を保つ</summary>
        private static float EvaluateEulerDisplay(CurveChannel channel, float frameNo)
        {
            var i = FindSegmentIndex(channel, frameNo);
            var start = channel.rotationValues[i];
            var end = channel.rotationValues[i + 1];
            var frameA = channel.segmentStFrames[i];
            var frameB = channel.segmentEdFrames[i];

            // サンプルごとに呼ばれるので配列を作らない
            var q = new Quaternion(
                EvaluateSegment(frameA, start[0], frameB, end[0], frameNo),
                EvaluateSegment(frameA, start[1], frameB, end[1], frameNo),
                EvaluateSegment(frameA, start[2], frameB, end[2], frameNo),
                EvaluateSegment(frameA, start[3], frameB, end[3], frameNo));
            var raw = GetEulerAngle(q, channel.eulerAxis);

            var dtFrames = frameB - frameA;
            var t = dtFrames > 0 ? Mathf.Clamp01((frameNo - frameA) / (float)dtFrames) : 0f;
            var reference = Mathf.Lerp(
                channel.eulerKeyValues[i], channel.eulerKeyValues[i + 1], t);
            return UnwrapAngle(raw, reference);
        }

        /// <summary>凡例に並べる 1 項目</summary>
        private struct LegendItem
        {
            public string label;
            public float width;
            public Color color;
        }

        /// <summary>凡例のレイアウト結果 (1 行ぶん)</summary>
        private class LegendLayout
        {
            public readonly List<LegendItem> items = new List<LegendItem>();
            public float height;
        }


        /// <summary>凡例のラベルと並べる範囲を決める (描画はしない)。
        /// 凡例はペインを削るので 1 行に限り、入り切らないチャンネルは省く
        /// (カーブとの対応は色で付く)</summary>
        private LegendLayout BuildLegend(Rect paneRect)
        {
            var layout = new LegendLayout();
            var currentWidth = LEGEND_PADDING;

            foreach (var channel in _channels)
            {
                // 複数ボーンを選んでいてもボーン名は付けない (1 行に収める方を優先する)
                var label = channel.displayName ?? channel.customKey ?? channel.valueType.ToString();
                // 複数ボーンで同じ値名が並ぶので 1 つにまとめる
                if (HasLegendItem(layout, label, channel.color))
                {
                    continue;
                }

                var width = GUIView.CalcWidth(GUIView.gsLabel, label) + LEGEND_ITEM_MARGIN;

                // 幅を超えたら以降は省く。1 個目だけは幅によらず必ず出す
                if (layout.items.Count > 0 && currentWidth + width > paneRect.width)
                {
                    break;
                }

                layout.items.Add(new LegendItem
                {
                    label = label,
                    width = width,
                    color = channel.color,
                });
                currentWidth += width;
            }

            layout.height = layout.items.Count > 0 ? LEGEND_ROW_HEIGHT : 0f;

            // ペインが低すぎるとグラフが潰れるので、その場合は凡例を出さない
            if (paneRect.height - layout.height < MIN_GRAPH_HEIGHT)
            {
                layout.items.Clear();
                layout.height = 0f;
            }
            return layout;
        }

        /// <summary>同じ値名かつ同じ色の項目が既にあるか。
        /// 色まで見るのは、カスタム値の色が型ごとの定義順で決まるため
        /// (CustomChannelColors[customIndex - 1])、型の違うボーンを一緒に選ぶと
        /// 同じ値名でも色が変わりうるから。その場合は別物として両方並べる</summary>
        private static bool HasLegendItem(LegendLayout layout, string label, Color color)
        {
            foreach (var item in layout.items)
            {
                if (item.label == label && item.color == color)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>ペイン下部に半透明黒の帯を敷き、チャンネル名をその色で並べる</summary>
        private void DrawLegend(GUIView view, Rect paneRect, LegendLayout layout)
        {
            if (layout.items.Count == 0)
            {
                return;
            }

            var top = paneRect.height - layout.height;

            view.currentPos = new Vector2(paneRect.x, paneRect.y + top);
            view.DrawTexture(GUIView.texWhite, paneRect.width, layout.height,
                new Color(0f, 0f, 0f, 0.6f));

            var x = LEGEND_PADDING;
            foreach (var item in layout.items)
            {
                view.currentPos = new Vector2(paneRect.x + x, paneRect.y + top);
                view.DrawLabel(item.label, item.width, LEGEND_ROW_HEIGHT, item.color);
                x += item.width;
            }
        }

        /// <summary>縦軸の目盛りラベル (上端・中央・下端)</summary>
        private void DrawValueScale(GUIView view, Rect graphRect)
        {
            var centerY = graphRect.height * 0.5f;
            DrawValueLabel(view, graphRect, 0f, _mapping.valueMax);
            DrawValueLabel(view, graphRect, centerY, (_mapping.valueMin + _mapping.valueMax) * 0.5f);

            // グラフが低いと下端ラベルが中央ラベルと交差するため、その場合は省く
            var minLabelY = graphRect.height - VALUE_LABEL_HEIGHT;
            if (minLabelY >= centerY + VALUE_LABEL_HEIGHT)
            {
                DrawValueLabel(view, graphRect, minLabelY, _mapping.valueMin);
            }
        }

        private static void DrawValueLabel(GUIView view, Rect graphRect, float y, float value)
        {
            view.currentPos = new Vector2(graphRect.x + 2, graphRect.y + y);
            view.DrawLabel(value.ToString("F2"), 60, VALUE_LABEL_HEIGHT, new Color(1f, 1f, 1f, 0.5f));
        }

        /// <summary>サンプル値を SAMPLE_STEP 幅の矩形セグメントで折れ線描画する</summary>
        private void DrawChannelCurve(GUIView view, CurveChannel channel, Rect graphRect)
        {
            var samples = channel.samples;
            for (var i = 1; i < samples.Count; i++)
            {
                var y0 = _mapping.ValueToY(samples[i - 1]);
                var y1 = _mapping.ValueToY(samples[i]);
                var top = Mathf.Min(y0, y1);
                var bottom = Mathf.Max(y0, y1);

                // 表示値域外へ出た区間は描かない (グラフ外へはみ出させない)
                if (bottom < 0f || top > graphRect.height)
                {
                    continue;
                }
                top = Mathf.Clamp(top, 0f, graphRect.height);
                bottom = Mathf.Clamp(bottom, 0f, graphRect.height);

                var x = (i - 1) * SAMPLE_STEP;
                if (x + SAMPLE_STEP > graphRect.width)
                {
                    break;
                }

                view.currentPos = new Vector2(graphRect.x + x, graphRect.y + top);
                // 隣接セグメントとの縦ギャップを埋めるため線の太さ分だけ足す
                view.DrawTexture(
                    GUIView.texWhite,
                    SAMPLE_STEP,
                    Mathf.Min(bottom - top + CURVE_THICKNESS, graphRect.height - top),
                    channel.color);
            }
        }

        /// <summary>キー点マーカー。非選択キーは半透明で描く</summary>
        private void DrawChannelKeys(GUIView view, CurveChannel channel, Rect graphRect, float scrollX)
        {
            var half = KEY_MARKER_SIZE * 0.5f;

            for (var i = 0; i < channel.values.Count; i++)
            {
                var x = _mapping.FrameToX(channel.frameNos[i]) - scrollX;
                if (x < half || x > graphRect.width - half)
                {
                    continue;
                }

                var y = _mapping.ValueToY(channel.GetKeyValue(i));
                if (y < half || y > graphRect.height - half)
                {
                    continue;
                }

                var color = channel.color;
                if (!selectedBones.Contains(channel.keyBones[i]))
                {
                    color.a = 0.5f;
                }

                view.currentPos = new Vector2(graphRect.x + x - half, graphRect.y + y - half);
                view.DrawTexture(GUIView.texWhite, KEY_MARKER_SIZE, KEY_MARKER_SIZE, color);
            }
        }

        /// <summary>編集区間 (前キー → 選択キー) のタンジェントハンドルを描画する</summary>
        private void DrawChannelHandles(GUIView view, CurveChannel channel, Rect graphRect, float scrollX)
        {
            var handleColor = new Color(1f, 1f, 1f, 0.8f);
            var half = HANDLE_MARKER_SIZE * 0.5f;

            for (var i = 0; i < channel.values.Count; i++)
            {
                var keyPos = new Vector2(
                    _mapping.FrameToX(channel.frameNos[i]) - scrollX,
                    _mapping.ValueToY(channel.GetKeyValue(i)));

                for (var side = 0; side < 2; side++)
                {
                    var isOut = side == 0;
                    if (!IsTangentInEditRange(channel, i, isOut))
                    {
                        continue;
                    }
                    // 値が変わらない区間に面した側は編集しても形が変わらないため出さない
                    // (ツールバーの入力欄も DrawTangentRow で伏せる)
                    if (!TryGetBaseSlopePerFrame(channel, i, isOut, out _))
                    {
                        continue;
                    }

                    var handlePos = GetHandlePos(channel, i, isOut, scrollX);

                    // 線分はカーブと同じく小さな矩形の連続で描く
                    var steps = Mathf.CeilToInt(HANDLE_LEN / SAMPLE_STEP);
                    for (var s = 1; s <= steps; s++)
                    {
                        var p = Vector2.Lerp(keyPos, handlePos, s / (float)steps);
                        if (!IsInGraph(p, graphRect, 1f))
                        {
                            continue;
                        }
                        view.currentPos = new Vector2(graphRect.x + p.x - 1f, graphRect.y + p.y - 1f);
                        view.DrawTexture(GUIView.texWhite, SAMPLE_STEP, SAMPLE_STEP, handleColor);
                    }

                    if (IsInGraph(handlePos, graphRect, half))
                    {
                        view.currentPos = new Vector2(
                            graphRect.x + handlePos.x - half, graphRect.y + handlePos.y - half);
                        view.DrawTexture(
                            GUIView.texWhite, HANDLE_MARKER_SIZE, HANDLE_MARKER_SIZE, handleColor);
                    }
                }
            }
        }

        private static bool IsInGraph(Vector2 pos, Rect graphRect, float margin)
        {
            return pos.x >= margin && pos.x <= graphRect.width - margin
                && pos.y >= margin && pos.y <= graphRect.height - margin;
        }
    }
}
