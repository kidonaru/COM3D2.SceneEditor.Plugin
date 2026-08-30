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

        private Texture2D _tangentTex;
        private Texture2D[] _presetTextures;
        private HashSet<MTEP.TangentPair> _cachedTangents = new HashSet<MTEP.TangentPair>();
        private readonly HashSet<MTEP.TangentPair> _workTangents = new HashSet<MTEP.TangentPair>();
        private MTEP.TangentValueType _tangentValueType = MTEP.TangentValueType.すべて;

        private readonly GUIComboBox<MTEP.TangentValueType> _valueTypeComboBox =
            new GUIComboBox<MTEP.TangentValueType>
            {
                items = Enum.GetValues(typeof(MTEP.TangentValueType))
                    .Cast<MTEP.TangentValueType>().ToList(),
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

        public void Draw(GUIView view)
        {
            if (!CollectTangents())
            {
                return;
            }

            EnsureTextures();
            UpdateCurveTextureIfNeeded();

            view.DrawHorizontalLine(Color.gray);
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

            view.DrawTexture(_tangentTex);

            DrawPresets(view);
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
            _valueTypeComboBox.onSelected = (type, index) =>
            {
                _tangentValueType = type;
            };
            _valueTypeComboBox.currentIndex = (int)_tangentValueType;
            _valueTypeComboBox.DrawButton(subView);

            KeyFrameTangentLogic.GetUniformTangents(
                _workTangents, out var outTangent, out var inTangent);

            var newOutTangent = outTangent;
            var newInTangent = inTangent;
            var diffOutTangent = 0f;
            var diffInTangent = 0f;

            // MTE 原典と同じく「コンボ展開中は無効化。値が 1 件でもあれば編集可 (NaN=混在でも差分編集は許可)」
            var hasAnyTangent = _workTangents.Count > 0;

            subView.DrawLabel("OutTangent", 100, RowHeight);
            subView.SetEnabled(subView.focusedComboBox == null && hasAnyTangent);
            subView.DrawFloatSelect(
                "", 0.1f, 0f, null, outTangent,
                value => newOutTangent = value,
                value => diffOutTangent = value);

            subView.SetEnabled(subView.focusedComboBox == null);
            subView.DrawLabel("InTangent", 100, RowHeight);
            subView.SetEnabled(subView.focusedComboBox == null && hasAnyTangent);
            subView.DrawFloatSelect(
                "", 0.1f, 0f, null, inTangent,
                value => newInTangent = value,
                value => diffInTangent = value);

            subView.SetEnabled(subView.focusedComboBox == null);

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
