using System;
using System.Collections.Generic;
using System.Linq;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// キーフレーム詳細のタンジェント曲線エディタ (MTE KeyFrameUI.DrawTangent の移植)。
    /// 選択キーフレームの区間曲線 (正規化タンジェント形状) を 1 枚のテクスチャに重ね描きし、
    /// In/Out タンジェントの数値編集・自動補間トグル・プリセット適用を行う。
    /// 区間ごとの実値カーブ編集は TimelineCurveEditor が担当し、こちらは別機能
    /// </summary>
    public class KeyFrameTangentDrawer
    {
        private static MTEP.Config config => MTEP.ConfigManager.instance.config;
        private static MTEP.TimelineManager timelineManager => MTEP.TimelineManager.instance;
        private static MTEP.ITimelineLayer currentLayer => timelineManager.currentLayer;
        private static HashSet<MTEP.BoneData> selectedBones => timelineManager.selectedBones;

        private const float RowHeight = 20f;
        /// <summary>曲線プレビューの一辺 (MTE と同じ 150px)</summary>
        private const int CurveTexSize = 150;
        /// <summary>プリセットサムネの一辺 (MTE と同じ 40px)</summary>
        private const int PresetTexSize = 40;
        /// <summary>プリセットサムネの線幅 (40px では 1px だと細くて形が読めない)</summary>
        private const int PresetLineWidth = 3;
        /// <summary>曲線プレビューの内側余白。勾配 0 のハンドルが枠に載って消えないようにする</summary>
        private const int CurvePadding = 12;
        /// <summary>プリセットサムネの内側余白 (40px なので控えめに)</summary>
        private const int PresetPadding = 3;
        /// <summary>テクスチャと数値列の間隔</summary>
        private const float ColumnSpacing = 10f;
        /// <summary>タンジェント行のラベル幅 ("OutTangent" が収まる幅)</summary>
        private const float TangentLabelWidth = 80f;
        /// <summary>タンジェント行の数値入力欄の幅</summary>
        private const float TangentFieldWidth = 50f;
        /// <summary>ラベルドラッグ 1px あたりのタンジェント増減量</summary>
        private const float DragSensitivity = 0.01f;
        /// <summary>ハンドル線の長さ (px)</summary>
        private const float HandleLength = 30f;
        /// <summary>ハンドル先端のマーカー一辺 (px)</summary>
        private const float HandleMarkerSize = 6f;
        /// <summary>ハンドルを掴める距離 (px)</summary>
        private const float HandleHitRadius = 8f;
        /// <summary>ハンドル線を描く点の間隔 (px)</summary>
        private const float HandleSampleStep = 2f;
        /// <summary>ハンドルの色 (曲線より手前に見せたいので不透明寄りの白)</summary>
        private static readonly Color HandleColor = new Color(1f, 1f, 1f, 0.8f);
        /// <summary>基準線 (値 0/1 と区間の始点/終点で囲む枠) の色</summary>
        private static readonly Color GuideFrameColor = new Color(1f, 1f, 1f, 0.35f);
        /// <summary>線形補間 (勾配 1) を示す対角線の色</summary>
        private static readonly Color GuideLinearColor = new Color(1f, 1f, 1f, 0.25f);
        /// <summary>対角線の破線パターン (この px 数ごとに描画と空白を切り替える)</summary>
        private const int GuideDashLength = 4;

        private Texture2D _tangentTex;
        private Texture2D[] _presetTextures;
        private HashSet<MTEP.TangentPair> _cachedTangents = new HashSet<MTEP.TangentPair>();
        private readonly HashSet<MTEP.TangentPair> _workTangents = new HashSet<MTEP.TangentPair>();
        /// <summary>
        /// コンボで選ぶ編集対象。軸ごとの値種別 (TangentValueType) と、
        /// 軸に属さないカスタム値 (ポストエフェクトの焦点距離など) を同じ土俵で扱う
        /// </summary>
        private struct TangentTarget
        {
            public string name;
            public MTEP.TangentValueType valueType;
            /// <summary>カスタム値のキー。null なら軸ごとの値種別</summary>
            public string customKey;

            public bool isCustom => customKey != null;

            /// <summary>
            /// 選択状態を覚えるための識別子。
            /// 候補は毎フレーム作り直すので添字では覚えられない。
            /// 軸 (axis) とカスタム値でキー空間が衝突しないよう接頭辞で分ける
            /// </summary>
            public string id => isCustom ? CustomIdPrefix + customKey : AxisIdPrefix + valueType;
        }

        private const string AxisIdPrefix = "a:";
        private const string CustomIdPrefix = "c:";

        /// <summary>軸ごとの値種別を表す候補を作る</summary>
        private static TangentTarget CreateAxisTarget(MTEP.TangentValueType valueType)
        {
            return new TangentTarget
            {
                name = valueType.ToString(),
                valueType = valueType,
            };
        }

        private static readonly MTEP.TangentData[] EmptyTangents = new MTEP.TangentData[0];

        /// <summary>選択中の編集対象の識別子</summary>
        private string _targetId = CreateAxisTarget(MTEP.TangentValueType.すべて).id;

        /// <summary>ドラッグ中のハンドル (true=Out / false=In)。null ならドラッグしていない</summary>
        private bool? _draggingIsOut = null;

        /// <summary>
        /// 選択キーフレームが実際に持つ編集対象だけを入れたコンボ候補。
        /// 先頭には必ず「すべて」が入るので、添字 0 は常に有効
        /// </summary>
        private readonly List<TangentTarget> _availableTargets = new List<TangentTarget>();

        /// <summary>候補へ入れ終えたカスタム値キー (重複判定用。毎フレームの確保を避ける)</summary>
        private readonly HashSet<string> _addedCustomKeys = new HashSet<string>();

        // 候補は選択内容で変わるため、items は毎フレーム差し替える
        private readonly GUIComboBox<TangentTarget> _targetComboBox =
            new GUIComboBox<TangentTarget>
            {
                getName = (target, index) => target.name,
                buttonSize = new Vector2(100, 20),
            };

        private static KeyFrameTangentDrawer _instance = null;
        public static KeyFrameTangentDrawer instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new KeyFrameTangentDrawer();
                }
                return _instance;
            }
        }

        private KeyFrameTangentDrawer()
        {
        }

        /// <summary>
        /// タンジェント編集 UI を描く。
        /// 選択キーフレームにタンジェント対応の transform がひとつも無ければ
        /// 何も描かずに false を返す (呼び出し側が代わりの案内を出す)
        /// </summary>
        public bool Draw(GUIView view)
        {
            UpdateAvailableTargets();

            if (!CollectTangents())
            {
                return false;
            }

            EnsureTextures();
            UpdateCurveTextureIfNeeded();

            view.DrawLabel("補間曲線", 100, RowHeight);

            // 数値編集列は親レイアウトに参加しない独立 GUIView としてテクスチャの右に置く。
            // BeginSubView/EndSubView は EndSubView が親の NextElement を呼び縦の高さを
            // 二重消費するため使わない。行送りは後続の DrawCurveArea に任せる。
            // 矩形は親の GetDrawRect 経由で求めるので絶対座標になり、
            // subView 側の padding は BeginSubView と同じく 0 にする
            var subViewRect = view.GetDrawRect(
                view.currentPos.x + CurveTexSize + ColumnSpacing,
                view.currentPos.y,
                view.viewRect.width - view.padding.x * 2
                    - view.currentPos.x - CurveTexSize - ColumnSpacing,
                CurveTexSize);
            var subView = new GUIView(subViewRect)
            {
                parent = view,
                padding = Vector2.zero,
            };
            DrawTangentFields(subView);

            DrawCurveArea(view);

            DrawPresets(view);
            return true;
        }

        /// <summary>
        /// 選択キーフレームから区間タンジェント (前キーの out + 当該キーの in) を集める。
        /// タンジェント対応の transform がひとつもなければ false
        /// </summary>
        private bool CollectTangents()
        {
            _workTangents.Clear();
            var hasTangent = false;
            var target = currentTarget;

            foreach (var bone in selectedBones)
            {
                if (!bone.transform.hasTangent)
                {
                    continue;
                }
                hasTangent = true;

                var prevBone = currentLayer.GetPrevBone(bone);
                if (prevBone == null)
                {
                    continue;
                }
                var outTangents = GetTangents(prevBone.transform, target, isOut: true);
                var inTangents = GetTangents(bone.transform, target, isOut: false);

                for (var i = 0; i < outTangents.Length && i < inTangents.Length; i++)
                {
                    _workTangents.Add(new MTEP.TangentPair
                    {
                        outTangent = outTangents[i].normalizedValue,
                        inTangent = inTangents[i].normalizedValue,
                        isSmooth = outTangents[i].isSmooth && inTangents[i].isSmooth,
                    });

                    if (_workTangents.Count >= config.detailTangentCount)
                    {
                        return hasTangent;
                    }
                }
            }

            return hasTangent;
        }

        private void EnsureTextures()
        {
            if (_tangentTex != null)
            {
                return;
            }

            _tangentTex = new Texture2D(CurveTexSize, CurveTexSize);

            // 自動補間 (Smooth) は固定形状を持たないためプリセットから除く
            _presetTextures = new Texture2D[(int)MTEP.TangentType.Smooth];
            for (var i = 0; i < _presetTextures.Length; i++)
            {
                var texture = new Texture2D(PresetTexSize, PresetTexSize);
                _presetTextures[i] = texture;
                TextureUtils.ClearTexture(texture, config.curveBgColor);
                var pair = MTEP.TangentPair.GetDefault((MTEP.TangentType)i);
                UpdateTangentTexture(
                    texture, pair.outTangent, pair.inTangent, config.curveLineColor,
                    lineWidth: PresetLineWidth, padding: PresetPadding);
                texture.Apply();
            }
        }

        /// <summary>タンジェント集合が前回描画時と変わったときだけ曲線を描き直す</summary>
        private void UpdateCurveTextureIfNeeded()
        {
            if (_cachedTangents.SetEquals(_workTangents))
            {
                return;
            }

            MTEUtils.LogDebug("補間曲線画像を更新します：" + _workTangents.Count);
            _cachedTangents = new HashSet<MTEP.TangentPair>(_workTangents);

            TextureUtils.ClearTexture(_tangentTex, config.curveBgColor);
            DrawGuideLines(_tangentTex, CurvePadding);
            foreach (var tangent in _workTangents)
            {
                var color = tangent.isSmooth ? config.curveLineSmoothColor : config.curveLineColor;
                UpdateTangentTexture(
                    _tangentTex, tangent.outTangent, tangent.inTangent, color,
                    lineWidth: 1, padding: CurvePadding);
            }

            // 基準線と全曲線を描き終えてから 1 回だけ GPU へ転送する
            _tangentTex.Apply();
        }

        private void DrawTangentFields(GUIView subView)
        {
            _targetComboBox.items = _availableTargets;
            _targetComboBox.currentIndex = Mathf.Max(0, FindTargetIndex(_targetId));
            _targetComboBox.onSelected = (target, index) =>
            {
                _targetId = target.id;
            };
            _targetComboBox.DrawButton(subView);

            KeyFrameTangentLogic.GetUniformTangents(
                _workTangents, out var outTangent, out var inTangent);

            var newOutTangent = outTangent;
            var newInTangent = inTangent;
            var diffOutTangent = 0f;
            var diffInTangent = 0f;

            // コンボ展開中はポップアップが重なるので下の行を触らせない
            subView.SetEnabled(subView.focusedComboBox == null);

            DrawTangentRow(subView, "OutTangent", outTangent,
                diff => diffOutTangent += diff,
                value => newOutTangent = value);

            DrawTangentRow(subView, "InTangent", inTangent,
                diff => diffInTangent += diff,
                value => newInTangent = value);

            subView.SetEnabled(true);

            // 新値の適用 (NaN=混在のままなら何もしない)
            ApplyValueIfChanged(newOutTangent, outTangent, ForEachOutTangent, "Out");
            ApplyValueIfChanged(newInTangent, inTangent, ForEachInTangent, "In");

            // 差分の適用。各タンジェントへ個別に足すので、
            // 混在 (NaN) でも値の差を保ったまま相対変更できる
            ApplyDiff(diffOutTangent, ForEachOutTangent, "Out");
            ApplyDiff(diffInTangent, ForEachInTangent, "In");

            var isSmooth = _workTangents.Count > 0 && _workTangents.All(tangent => tangent.isSmooth);
            subView.DrawToggle("自動補間", isSmooth, 100, RowHeight, newIsSmooth =>
            {
                ForEachOutTangent(data => data.isSmooth = newIsSmooth);
                ForEachInTangent(data => data.isSmooth = newIsSmooth);
                ApplyAndRecord("タンジェント: 自動補間");
            });
        }

        /// <summary>数値欄へ入った絶対値を対象タンジェントすべてへ書き込む</summary>
        private void ApplyValueIfChanged(
            float newValue,
            float oldValue,
            Action<Action<MTEP.TangentData>> forEachTangent,
            string label)
        {
            if (float.IsNaN(newValue) || newValue == oldValue)
            {
                return;
            }

            forEachTangent(data =>
            {
                data.normalizedValue = newValue;
                data.isSmooth = false;
            });
            ApplyAndRecord("タンジェント: " + label + " 変更");
        }

        /// <summary>ラベルドラッグの差分を対象タンジェントそれぞれへ加算する</summary>
        private void ApplyDiff(
            float diff, Action<Action<MTEP.TangentData>> forEachTangent, string label)
        {
            if (diff == 0f)
            {
                return;
            }

            forEachTangent(data =>
            {
                data.normalizedValue += diff;
                data.isSmooth = false;
            });
            ApplyAndRecord("タンジェント: " + label + " 変更");
        }

        /// <summary>
        /// タンジェント 1 行。ラベルを左右ドラッグすると差分編集、数値欄への入力で絶対値編集。
        /// value が NaN (複数選択で値が混在) でも差分編集を残したいため、
        /// 絶対値しか渡さない DrawDragFloatField ではなく DrawDragLabel を直接使う
        /// </summary>
        private void DrawTangentRow(
            GUIView view, string label, float value, Action<float> onDiff, Action<float> onValue)
        {
            view.BeginHorizontal();
            {
                view.DrawDragLabel(label, TangentLabelWidth, RowHeight, DragSensitivity, onDiff);

                var fieldCache = view.GetFieldCache(label);
                fieldCache.UpdateValue(value);
                view.DrawFloatField(new GUIView.FloatFieldOption
                {
                    value = value,
                    width = TangentFieldWidth,
                    height = RowHeight,
                    fieldCache = fieldCache,
                    onChanged = onValue,
                });
            }
            view.EndLayout();
        }

        /// <summary>
        /// ハンドルのドラッグ状態を捨てる。
        /// タブ切替や選択解除で Draw が呼ばれなくなるとマウスアップを拾えないため、
        /// 描画しない側から明示的に打ち切ってもらう
        /// (TimelineCurveEditor が guiEnabled オフ時に EndDrag するのと同じ意図)
        /// </summary>
        public void CancelDrag()
        {
            _draggingIsOut = null;
        }

        /// <summary>曲線テクスチャと In/Out ハンドルを描き、ハンドルのドラッグを処理する</summary>
        private void DrawCurveArea(GUIView view)
        {
            // テクスチャ左上のスクリーン座標。ハンドル位置とマウス位置の基準にする
            var texRect = view.GetDrawRect(
                view.currentPos.x, view.currentPos.y, CurveTexSize, CurveTexSize);
            var texPos = view.currentPos;

            view.DrawTexture(_tangentTex);

            KeyFrameTangentLogic.GetUniformTangents(
                _workTangents, out var outTangent, out var inTangent);

            DrawHandle(view, texPos, true, outTangent);
            DrawHandle(view, texPos, false, inTangent);

            // ハンドル描画で currentPos を動かしたので、テクスチャの下へ戻す
            view.currentPos = new Vector2(texPos.x, texPos.y + CurveTexSize + view.margin);

            HandleCurveInput(texRect, view.focusedComboBox != null);
        }

        /// <summary>
        /// ハンドル線と先端マーカーを小さな矩形の連続で描く
        /// (TimelineCurveEditor.DrawChannelHandles と同じ方式)。
        /// currentPos を直接動かすため、IsInCurveArea でテクスチャ矩形の外に出る点を落とし、
        /// レイアウトの縦幅がハンドルで伸びないようにしている
        /// </summary>
        private void DrawHandle(GUIView view, Vector2 texPos, bool isOut, float normalizedValue)
        {
            var origin = KeyFrameTangentLogic.GetCurveOrigin(isOut, CurveTexSize, CurvePadding);
            var handlePos = KeyFrameTangentLogic.GetHandlePos(
                isOut, normalizedValue, CurveTexSize, CurvePadding, HandleLength);
            var half = HandleMarkerSize * 0.5f;

            var steps = Mathf.CeilToInt(HandleLength / HandleSampleStep);
            for (var i = 1; i <= steps; i++)
            {
                var p = Vector2.Lerp(origin, handlePos, i / (float)steps);
                if (!IsInCurveArea(p, 1f))
                {
                    continue;
                }
                view.currentPos = new Vector2(texPos.x + p.x - 1f, texPos.y + p.y - 1f);
                view.DrawTexture(GUIView.texWhite, HandleSampleStep, HandleSampleStep, HandleColor);
            }

            if (IsInCurveArea(handlePos, half))
            {
                view.currentPos = new Vector2(
                    texPos.x + handlePos.x - half, texPos.y + handlePos.y - half);
                view.DrawTexture(GUIView.texWhite, HandleMarkerSize, HandleMarkerSize, HandleColor);
            }
        }

        private static bool IsInCurveArea(Vector2 pos, float margin)
        {
            return pos.x >= margin && pos.x <= CurveTexSize - margin
                && pos.y >= margin && pos.y <= CurveTexSize - margin;
        }

        /// <summary>ハンドルの掴み・ドラッグ・離しを処理する</summary>
        private void HandleCurveInput(Rect texRect, bool comboBoxOpen)
        {
            var e = Event.current;
            var mouse = e.mousePosition - new Vector2(texRect.x, texRect.y);

            // 値種別コンボのポップアップは別ウィンドウとしてプレビューの上に重なりうるので、
            // 展開中はハンドル操作を受け付けない
            if (comboBoxOpen)
            {
                _draggingIsOut = null;
                return;
            }

            if (_draggingIsOut != null)
            {
                if (!Input.GetMouseButton(0))
                {
                    _draggingIsOut = null;
                    return;
                }

                var isOut = _draggingIsOut.Value;
                if (KeyFrameTangentLogic.TryGetNormalizedTangent(
                        isOut, mouse, CurveTexSize, CurvePadding, out var newValue))
                {
                    ApplyTangent(isOut, newValue);
                }
                if (e.type == EventType.MouseDrag)
                {
                    e.Use();
                }
                return;
            }

            if (e.type != EventType.MouseDown || e.button != 0)
            {
                return;
            }

            KeyFrameTangentLogic.GetUniformTangents(
                _workTangents, out var outTangent, out var inTangent);

            for (var side = 0; side < 2; side++)
            {
                // 近接して重なった場合は Out を優先する
                var isOut = side == 0;
                var handlePos = KeyFrameTangentLogic.GetHandlePos(
                    isOut, isOut ? outTangent : inTangent, CurveTexSize, CurvePadding, HandleLength);
                if (Vector2.Distance(handlePos, mouse) > HandleHitRadius)
                {
                    continue;
                }

                _draggingIsOut = isOut;
                // 掴んだ押下だけ消費する。プレビュー上の空クリックまで消すと
                // Inspector のスクロールドラッグができなくなる
                e.Use();
                return;
            }
        }

        /// <summary>
        /// ハンドルドラッグの結果を選択キーフレーム全体へ適用する。
        /// Draw は 1 フレームに複数回呼ばれるので、値が変わっていないなら
        /// ApplyCurrentFrame を呼ばない (TimelineCurveEditor.UpdateDrag と同じガード)
        /// </summary>
        private void ApplyTangent(bool isOut, float normalizedValue)
        {
            KeyFrameTangentLogic.GetUniformTangents(
                _workTangents, out var currentOut, out var currentIn);
            if (normalizedValue == (isOut ? currentOut : currentIn))
            {
                return;
            }

            if (isOut)
            {
                ForEachOutTangent(data =>
                {
                    data.normalizedValue = normalizedValue;
                    data.isSmooth = false;
                });
            }
            else
            {
                ForEachInTangent(data =>
                {
                    data.normalizedValue = normalizedValue;
                    data.isSmooth = false;
                });
            }
            ApplyAndRecord(isOut ? "タンジェント: Out 変更" : "タンジェント: In 変更");
        }

        private void DrawPresets(GUIView view)
        {
            view.DrawLabel("プリセット反映", 100, RowHeight);
            view.BeginHorizontal();
            {
                for (var i = 0; i < _presetTextures.Length; i++)
                {
                    var tangentType = (MTEP.TangentType)i;
                    var typeName = MTEP.TangentData.TangentTypeNames[i];
                    view.DrawTexture(
                        _presetTextures[i],
                        PresetTexSize,
                        PresetTexSize,
                        Color.white,
                        EventType.MouseDown,
                        _ =>
                        {
                            if (selectedBones.Count == 0)
                            {
                                return;
                            }
                            var pair = MTEP.TangentPair.GetDefault(tangentType);
                            ForEachOutTangent(data =>
                            {
                                data.normalizedValue = pair.outTangent;
                                data.isSmooth = pair.isSmooth;
                            });
                            ForEachInTangent(data =>
                            {
                                data.normalizedValue = pair.inTangent;
                                data.isSmooth = pair.isSmooth;
                            });
                            ApplyAndRecord("タンジェント: プリセット " + typeName);
                        });
                }
            }
            view.EndLayout();
        }

        /// <summary>選択キーフレームの前キー側 (区間始点) の out タンジェントを走査する</summary>
        private void ForEachOutTangent(Action<MTEP.TangentData> callback)
        {
            foreach (var prevBone in currentLayer.GetPrevBones(selectedBones))
            {
                foreach (var data in GetTangents(prevBone.transform, currentTarget, isOut: true))
                {
                    callback(data);
                }
            }
        }
        private void ForEachInTangent(Action<MTEP.TangentData> callback)
        {
            foreach (var bone in selectedBones)
            {
                if (!bone.transform.hasTangent)
                {
                    continue;
                }
                foreach (var data in GetTangents(bone.transform, currentTarget, isOut: false))
                {
                    callback(data);
                }
            }
        }


        /// <summary>
        /// コンボ候補を選択キーフレームが実際に持つ対象だけに絞る。
        /// 軸ごとの値種別 (TimelineCurveEditor.UpdateValueTypeFilter と同じ流儀) に加え、
        /// 軸に属さないカスタム値も 1 パラメータずつ候補へ並べる。
        /// 「すべて」は常に候補に残し、選択中の対象が消えたらそこへ戻す
        /// </summary>
        private void UpdateAvailableTargets()
        {
            _availableTargets.Clear();
            _availableTargets.Add(CreateAxisTarget(MTEP.TangentValueType.すべて));

            foreach (MTEP.TangentValueType valueType in
                Enum.GetValues(typeof(MTEP.TangentValueType)))
            {
                if (valueType != MTEP.TangentValueType.すべて && HasValueType(valueType))
                {
                    _availableTargets.Add(CreateAxisTarget(valueType));
                }
            }

            AddCustomValueTargets();

            if (FindTargetIndex(_targetId) < 0)
            {
                _targetId = _availableTargets[0].id;
            }
        }

        /// <summary>
        /// カスタム値 (ポストエフェクトの焦点距離など) を候補へ足す。
        /// 選択キーフレームで種類が違うこともあるので、いずれかが持つキーをすべて並べる
        /// </summary>
        private void AddCustomValueTargets()
        {
            _addedCustomKeys.Clear();

            foreach (var bone in selectedBones)
            {
                var transform = bone.transform;
                if (transform == null || !transform.hasTangent)
                {
                    continue;
                }

                foreach (var pair in transform.GetCustomValueInfoMap())
                {
                    var customKey = pair.Key;
                    if (!_addedCustomKeys.Add(customKey))
                    {
                        continue;
                    }

                    _availableTargets.Add(new TangentTarget
                    {
                        name = transform.GetCustomValueName(customKey),
                        customKey = customKey,
                    });
                }
            }
        }

        /// <summary>選択キーフレームのいずれかが指定種別の値を持つか</summary>
        private static bool HasValueType(MTEP.TangentValueType valueType)
        {
            foreach (var bone in selectedBones)
            {
                var transform = bone.transform;
                if (transform != null && transform.GetValueDataList(valueType).Length > 0)
                {
                    return true;
                }
            }
            return false;
        }

        private int FindTargetIndex(string targetId)
        {
            return _availableTargets.FindIndex(target => target.id == targetId);
        }

        /// <summary>選択中の編集対象。候補から消えていたら先頭 (すべて) を返す</summary>
        private TangentTarget currentTarget
        {
            get
            {
                var index = FindTargetIndex(_targetId);
                return index >= 0 ? _availableTargets[index] : _availableTargets[0];
            }
        }

        /// <summary>編集対象に対応するタンジェントを取り出す。対象を持たない transform では空</summary>
        private static MTEP.TangentData[] GetTangents(
            MTEP.ITransformData transform, TangentTarget target, bool isOut)
        {
            if (target.isCustom)
            {
                if (!transform.HasCustomValue(target.customKey))
                {
                    return EmptyTangents;
                }
                var value = transform.GetCustomValue(target.customKey);
                return new[] { isOut ? value.outTangent : value.inTangent };
            }

            return isOut
                ? transform.GetOutTangentDataList(target.valueType)
                : transform.GetInTangentDataList(target.valueType);
        }

        /// <summary>選択キーフレーム側 (区間終点) の in タンジェントを走査する</summary>
        private void ApplyAndRecord(string description)
        {
            MTEUtils.LogDebug(description);
            currentLayer.ApplyCurrentFrame(true);
            // ドラッグ中は毎フレーム呼ばれるため、履歴はマウスを離すまで集約させる
            timelineManager.RequestHistory(description);
        }

        /// <summary>
        /// 曲線を読む基準線を描く。内側領域の枠 (下辺=値 0 / 上辺=値 1 /
        /// 左辺=区間の始点 / 右辺=終点) と、線形補間を示す対角線 (破線)。
        /// 曲線より先に描いて背面に置く。
        /// GPU への転送をまとめるため Apply は呼び出し側で行う
        /// </summary>
        private static void DrawGuideLines(Texture2D texture, int padding)
        {
            var size = texture.width;
            // 曲線と同じ写像で端を求め、枠と曲線がずれないようにする
            var min = KeyFrameTangentLogic.ValueToTextureY(0f, size, padding);
            var max = KeyFrameTangentLogic.ValueToTextureY(1f, size, padding);

            // 対角線は内側領域が正方形であることを使い、x と y に同じ値を使う。
            // 曲線と紛れないよう GuideDashLength px ごとに描画と空白を切り替える。
            // 枠より先に描いて、重なる角は枠の色を残す
            for (var i = min; i <= max; i++)
            {
                var dashSegment = (i - min) / GuideDashLength;
                if (dashSegment % 2 == 0)
                {
                    texture.SetPixel(i, i, GuideLinearColor);
                }
            }

            for (var i = min; i <= max; i++)
            {
                texture.SetPixel(i, min, GuideFrameColor);
                texture.SetPixel(i, max, GuideFrameColor);
                texture.SetPixel(min, i, GuideFrameColor);
                texture.SetPixel(max, i, GuideFrameColor);
            }
        }

        /// <summary>
        /// 正規化タンジェント形状 (t は 0..1) の Hermite 曲線をテクスチャへ描く
        /// (MTE KeyFrameUI.UpdateTangentTexture の移植)。
        /// padding px は四辺の余白として空け、内側領域だけに描く。
        /// 複数の曲線を重ねられるよう Apply は呼び出し側で行う
        /// </summary>
        private static void UpdateTangentTexture(
            Texture2D texture,
            float outTangent,
            float inTangent,
            Color lineColor,
            int lineWidth,
            int padding)
        {
            var size = texture.width;
            var halfLineWidth = lineWidth / 2;
            var innerWidth = size - padding * 2;
            // 最終列で t=1 に到達させ、曲線の終端を in ハンドルの原点へ合わせる
            var lastX = Mathf.Max(1, innerWidth - 1);

            for (var x = 0; x < innerWidth; x++)
            {
                var t = x / (float)lastX;
                var value = MTEP.PluginUtils.HermiteSimplified(outTangent, inTangent, t);
                var y = KeyFrameTangentLogic.ValueToTextureY(value, size, padding) - halfLineWidth;

                for (var i = 0; i < lineWidth; i++)
                {
                    var yy = Mathf.Clamp(y + i, padding, size - 1 - padding);
                    texture.SetPixel(padding + x, yy, lineColor);
                }
            }
        }
    }
}
