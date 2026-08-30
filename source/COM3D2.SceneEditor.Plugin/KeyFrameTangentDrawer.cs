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

        private Texture2D _tangentTex;
        private Texture2D[] _presetTextures;
        private HashSet<MTEP.TangentPair> _cachedTangents = new HashSet<MTEP.TangentPair>();
        private readonly HashSet<MTEP.TangentPair> _workTangents = new HashSet<MTEP.TangentPair>();
        private MTEP.TangentValueType _tangentValueType = MTEP.TangentValueType.すべて;

        /// <summary>ドラッグ中のハンドル (true=Out / false=In)。null ならドラッグしていない</summary>
        private bool? _draggingIsOut = null;

        /// <summary>選択キーフレームが実際に持つ値種別だけを入れたコンボ候補</summary>
        private readonly List<MTEP.TangentValueType> _availableValueTypes
            = new List<MTEP.TangentValueType>();

        // 候補は選択内容で変わるため、items は毎フレーム差し替える
        private readonly GUIComboBox<MTEP.TangentValueType> _valueTypeComboBox =
            new GUIComboBox<MTEP.TangentValueType>
            {
                getName = (type, index) => type.ToString(),
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
            UpdateAvailableValueTypes();

            if (!CollectTangents())
            {
                return false;
            }

            EnsureTextures();
            UpdateCurveTextureIfNeeded();

            view.DrawLabel("補間曲線", 100, RowHeight);

            // 数値編集列は親レイアウトに参加しない独立 GUIView としてテクスチャの右に置く。
            // BeginSubView/EndSubView は EndSubView が親の NextElement を呼び縦の高さを
            // 二重消費するため使わない。行送りは最後の view.DrawTexture の 1 回に任せる
            // 矩形は親の GetDrawRect で求める (親のビュー位置と padding が加算される)。
            // 求めた時点で絶対座標なので、subView 側は BeginSubView と同じく padding を 0 にする
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
                var outTangents = prevBone.transform.GetOutTangentDataList(_tangentValueType);
                var inTangents = bone.transform.GetInTangentDataList(_tangentValueType);

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
                    texture, pair.outTangent, pair.inTangent, config.curveLineColor, PresetLineWidth);
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
            foreach (var tangent in _workTangents)
            {
                var color = tangent.isSmooth ? config.curveLineSmoothColor : config.curveLineColor;
                UpdateTangentTexture(_tangentTex, tangent.outTangent, tangent.inTangent, color, 1);
            }
        }

        private void DrawTangentFields(GUIView subView)
        {
            // 候補と enum 値の添字は一致しないので IndexOf で対応させる
            _valueTypeComboBox.items = _availableValueTypes;
            _valueTypeComboBox.currentIndex =
                Mathf.Max(0, _availableValueTypes.IndexOf(_tangentValueType));
            _valueTypeComboBox.onSelected = (type, index) =>
            {
                _tangentValueType = type;
            };
            _valueTypeComboBox.DrawButton(subView);

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
            if (!float.IsNaN(newOutTangent) && newOutTangent != outTangent)
            {
                ForEachOutTangent(data =>
                {
                    data.normalizedValue = newOutTangent;
                    data.isSmooth = false;
                });
                ApplyAndRecord("タンジェント: Out 変更");
            }

            if (!float.IsNaN(newInTangent) && newInTangent != inTangent)
            {
                ForEachInTangent(data =>
                {
                    data.normalizedValue = newInTangent;
                    data.isSmooth = false;
                });
                ApplyAndRecord("タンジェント: In 変更");
            }

            // 差分の適用 (混在時でも相対変更できる)
            if (diffOutTangent != 0f)
            {
                ForEachOutTangent(data =>
                {
                    data.normalizedValue += diffOutTangent;
                    data.isSmooth = false;
                });
                ApplyAndRecord("タンジェント: Out 変更");
            }

            if (diffInTangent != 0f)
            {
                ForEachInTangent(data =>
                {
                    data.normalizedValue += diffInTangent;
                    data.isSmooth = false;
                });
                ApplyAndRecord("タンジェント: In 変更");
            }

            var isSmooth = _workTangents.Count > 0 && _workTangents.All(tangent => tangent.isSmooth);
            subView.DrawToggle("自動補間", isSmooth, 100, RowHeight, newIsSmooth =>
            {
                ForEachOutTangent(data => data.isSmooth = newIsSmooth);
                ForEachInTangent(data => data.isSmooth = newIsSmooth);
                ApplyAndRecord("タンジェント: 自動補間");
            });
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
            var origin = isOut
                ? new Vector2(0f, CurveTexSize)
                : new Vector2(CurveTexSize, 0f);
            var handlePos = KeyFrameTangentLogic.GetHandlePos(
                isOut, normalizedValue, CurveTexSize, HandleLength);
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
                        isOut, mouse, CurveTexSize, out var newValue))
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
                    isOut, isOut ? outTangent : inTangent, CurveTexSize, HandleLength);
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
                foreach (var data in prevBone.transform.GetOutTangentDataList(_tangentValueType))
                {
                    callback(data);
                }
            }
        }

        /// <summary>
        /// コンボ候補を選択キーフレームが実際に持つ値種別だけに絞る。
        /// 「すべて」は常に候補に残し、選択中の種別が消えたらそこへ戻す
        /// (TimelineCurveEditor.UpdateValueTypeFilter と同じ流儀)
        /// </summary>
        private void UpdateAvailableValueTypes()
        {
            _availableValueTypes.Clear();
            _availableValueTypes.Add(MTEP.TangentValueType.すべて);

            foreach (MTEP.TangentValueType valueType in
                Enum.GetValues(typeof(MTEP.TangentValueType)))
            {
                if (valueType != MTEP.TangentValueType.すべて && HasValueType(valueType))
                {
                    _availableValueTypes.Add(valueType);
                }
            }

            if (!_availableValueTypes.Contains(_tangentValueType))
            {
                _tangentValueType = MTEP.TangentValueType.すべて;
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

        /// <summary>選択キーフレーム側 (区間終点) の in タンジェントを走査する</summary>
        private void ForEachInTangent(Action<MTEP.TangentData> callback)
        {
            foreach (var bone in selectedBones)
            {
                if (!bone.transform.hasTangent)
                {
                    continue;
                }
                foreach (var data in bone.transform.GetInTangentDataList(_tangentValueType))
                {
                    callback(data);
                }
            }
        }

        private void ApplyAndRecord(string description)
        {
            MTEUtils.LogDebug(description);
            currentLayer.ApplyCurrentFrame(true);
            // ドラッグ中は毎フレーム呼ばれるため、履歴はマウスを離すまで集約させる
            timelineManager.RequestHistory(description);
        }

        /// <summary>
        /// 正規化タンジェント形状 (t は 0..1) の Hermite 曲線をテクスチャへ描く
        /// (MTE KeyFrameUI.UpdateTangentTexture の移植)
        /// </summary>
        private static void UpdateTangentTexture(
            Texture2D texture,
            float outTangent,
            float inTangent,
            Color lineColor,
            int lineWidth)
        {
            var width = texture.width;
            var height = texture.height;
            var halfLineWidth = lineWidth / 2;

            for (var x = 0; x < width; x++)
            {
                var t = x / (float)width;
                var y = (int)(MTEP.PluginUtils.HermiteSimplified(outTangent, inTangent, t) * height);

                y -= halfLineWidth;
                for (var i = 0; i < lineWidth; i++)
                {
                    var yy = Mathf.Clamp(y + i, 0, height - 1);
                    texture.SetPixel(x, yy, lineColor);
                }
            }

            texture.Apply();
        }
    }
}
