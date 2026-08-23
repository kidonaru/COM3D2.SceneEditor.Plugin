using System;
using System.Collections.Generic;
using System.Linq;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 選択中キーフレームの詳細表示・編集 (MTE の KeyFrameUI を Inspector 内へ移植したもの)。
    /// 位置/回転/拡縮/色などの Transform 値、レイヤー固有のカスタム値、
    /// 補間曲線 (Tangent) と Easing を編集できる。
    /// 編集は選択中の全キーフレームへ適用し、ApplyCurrentFrame で即時反映する
    /// </summary>
    public class KeyFrameInspector
    {
        private Texture2D tangentTex = null;
        private MTEP.TangentValueType tangentValueType = MTEP.TangentValueType.すべて;
        private HashSet<MTEP.TangentPair> cachedTangents = new HashSet<MTEP.TangentPair>();
        private Texture2D[] tangentPresetTextures = null;

        private Texture2D easingTex = null;
        private HashSet<int> cachedEasings = new HashSet<int>();
        private readonly GUIComboBox<int> easingComboBox = new GUIComboBox<int>
        {
            items = Enumerable.Range(0, (int)MTEP.MoveEasingType.Max).ToList(),
            getName = (type, index) => ((MTEP.MoveEasingType)type).ToString(),
        };

        private readonly GUIComboBox<MTEP.TangentValueType> _tangentValueTypeComboBox
            = new GUIComboBox<MTEP.TangentValueType>
        {
            items = Enum.GetValues(typeof(MTEP.TangentValueType))
                .Cast<MTEP.TangentValueType>().ToList(),
            getName = (type, index) => type.ToString(),
            buttonSize = new Vector2(50, 20),
        };

        private static MTEP.Config config => MTEP.ConfigManager.instance.config;
        private static MTEP.TimelineManager timelineManager => MTEP.TimelineManager.instance;
        private static MTEP.ITimelineLayer currentLayer => timelineManager.currentLayer;
        private static HashSet<MTEP.BoneData> selectedBones => timelineManager.selectedBones;

        private static KeyFrameInspector _instance = null;
        public static KeyFrameInspector instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new KeyFrameInspector();
                }
                return _instance;
            }
        }

        private KeyFrameInspector()
        {
        }

        /// <summary>キーフレーム選択中で Inspector に表示すべき状態か</summary>
        public static bool ShouldDraw()
        {
            return timelineManager.timeline != null
                && currentLayer != null
                && timelineManager.HasSelected();
        }

        /// <summary>曲線表示用テクスチャの遅延生成 (MTE KeyFrameUI.InitWindow 相当)</summary>
        private void InitTextures()
        {
            if (tangentPresetTextures == null)
            {
                MTEUtils.LogDebug("補間曲線プリセット画像を生成します");

                tangentPresetTextures = new Texture2D[(int)MTEP.TangentType.Smooth];

                for (var i = 0; i < tangentPresetTextures.Length; i++)
                {
                    var tangentType = (MTEP.TangentType)i;
                    var texture = new Texture2D(40, 40);
                    var tangentPair = MTEP.TangentPair.GetDefault(tangentType);

                    tangentPresetTextures[i] = texture;
                    TextureUtils.ClearTexture(texture, config.curveBgColor);

                    UpdateTangentTexture(
                        texture,
                        tangentPair.outTangent,
                        tangentPair.inTangent,
                        config.curveLineColor,
                        3
                    );
                }
            }

            if (tangentTex == null)
            {
                tangentTex = new Texture2D(150, 150);
                TextureUtils.ClearTexture(tangentTex, config.curveBgColor);
            }

            if (easingTex == null)
            {
                easingTex = new Texture2D(150, 150);
                TextureUtils.ClearTexture(easingTex, config.curveBgColor);
            }
        }

        public void Draw(GUIView view)
        {
            if (!ShouldDraw())
            {
                view.DrawLabel("キーフレームが選択されていません", -1, 20);
                return;
            }

            InitTextures();

            view.DrawLabel(
                string.Format("キーフレーム詳細 ({0}個選択中)", selectedBones.Count), -1, 20);

            DrawTransform(view);
            DrawCustomValues(view);
            DrawStrValues(view);

            if (view.DrawButton("初期化", 80, 20))
            {
                foreach (var selectedBone in selectedBones)
                {
                    selectedBone.transform.Reset();
                }

                MTEUtils.LogDebug("初期化します");
                currentLayer.ApplyCurrentFrame(true);
            }

            view.DrawHorizontalLine(Color.gray);

            DrawTangent(view);
            DrawEasing(view);
        }

        private void DrawTransform(GUIView view)
        {
            DrawVector3(
                view,
                new string[] { "X", "Y", "Z" },
                0.01f,
                0.1f,
                transform => transform.initialPosition,
                transform => transform.hasPosition,
                transform => transform.position,
                (transform, pos) => transform.position = pos
            );
            DrawVector3(
                view,
                new string[] { "RX", "RY", "RZ" },
                1f,
                10f,
                transform => transform.initialEulerAngles,
                transform => transform.hasRotation || transform.hasEulerAngles,
                transform => transform.normalizedEulerAngles,
                (transform, angle) => transform.eulerAngles = angle
            );
            DrawVector3(
                view,
                new string[] { "SX", "SY", "SZ" },
                0.01f,
                0.1f,
                transform => transform.initialScale,
                transform => transform.hasScale,
                transform => transform.scale,
                (transform, scale) => transform.scale = scale
            );
            DrawVector3(
                view,
                new string[] { "R", "G", "B" },
                0.01f,
                0.1f,
                transform => transform.initialColor.ToVector3(),
                transform => transform.hasColor,
                transform => transform.color.ToVector3(),
                (transform, color) => transform.color = color.ToColor()
            );
            DrawBoolValue(
                view,
                "表示",
                transform => transform.hasVisible,
                transform => transform.visible,
                (transform, visible) => transform.visible = visible
            );
        }

        private void DrawCustomValues(GUIView view)
        {
            var firstBone = selectedBones.First();
            var customKeys = firstBone.transform.GetCustomValueInfoMap().Keys;

            foreach (var customKey in customKeys)
            {
                DrawValue(
                    view,
                    firstBone.transform.GetCustomValueName(customKey),
                    0.01f,
                    0.1f,
                    transform => transform.GetDefaultCustomValue(customKey),
                    transform => transform.HasCustomValue(customKey),
                    transform => transform.GetCustomValue(customKey).value,
                    (transform, newValue) => transform.GetCustomValue(customKey).value = newValue
                );
            }
        }

        private void DrawStrValues(GUIView view)
        {
            var firstBone = selectedBones.First();
            var customKeys = firstBone.transform.GetStrValueInfoMap().Keys;

            foreach (var customKey in customKeys)
            {
                DrawStrValue(
                    view,
                    firstBone.transform.GetStrValueName(customKey),
                    transform => transform.HasStrValue(customKey),
                    transform => transform.GetStrValue(customKey),
                    (transform, newValue) => transform.SetStrValue(customKey, newValue)
                );
            }
        }

        private void DrawVector3(
            GUIView view,
            string[] labels,
            float addedValue1,
            float addedValue2,
            Func<MTEP.ITransformData, Vector3> getResetValue,
            Func<MTEP.ITransformData, bool> hasValue,
            Func<MTEP.ITransformData, Vector3> getValue,
            Action<MTEP.ITransformData, Vector3> setValue)
        {
            var values = Vector3.zero;
            var boneCount = 0;

            foreach (var bone in selectedBones)
            {
                var transform = bone.transform;
                int nanCount = 0;

                if (hasValue(transform))
                {
                    var pos = getValue(transform);
                    if (boneCount == 0)
                    {
                        values = pos;
                    }
                    else
                    {
                        for (var i = 0; i < 3; i++)
                        {
                            if (pos[i] != values[i])
                            {
                                values[i] = float.NaN;
                                nanCount++;
                            }
                        }
                    }

                    boneCount++;
                    if (boneCount >= config.detailTransformCount || nanCount == 3)
                    {
                        break;
                    }
                }
            }

            if (boneCount == 0)
            {
                return;
            }

            var diffValues = Vector3.zero;
            var newValues = values;
            int resetIndex = -1;

            for (var i = 0; i < 3; i++)
            {
                var index = i;
                view.DrawFloatSelect(
                    labels[i],
                    addedValue1,
                    addedValue2,
                    () => resetIndex = index,
                    values[i],
                    newValue => newValues[index] = newValue,
                    diffValue => diffValues[index] = diffValue
                );
            }

            // リセット
            if (resetIndex >= 0)
            {
                foreach (var selectedBone in selectedBones)
                {
                    var transform = selectedBone.transform;
                    if (hasValue(transform))
                    {
                        var pos = getValue(transform);
                        pos[resetIndex] = getResetValue(transform)[resetIndex];
                        setValue(transform, pos);
                    }
                }

                MTEUtils.LogDebug("リセットします");
                currentLayer.ApplyCurrentFrame(true);
            }

            // 差分の適用
            for (var i = 0; i < 3; i++)
            {
                var diffValue = diffValues[i];
                if (diffValue == 0f)
                {
                    continue;
                }

                foreach (var selectedBone in selectedBones)
                {
                    var transform = selectedBone.transform;
                    if (hasValue(transform))
                    {
                        var pos = getValue(transform);
                        pos[i] += diffValue;
                        setValue(transform, pos);
                    }
                }

                MTEUtils.LogDebug("差分を適用します：" + diffValue);
                currentLayer.ApplyCurrentFrame(true);
            }

            // 新値の適用
            for (var i = 0; i < 3; i++)
            {
                var newValue = newValues[i];
                if (float.IsNaN(newValue) || newValue == values[i])
                {
                    continue;
                }

                foreach (var selectedBone in selectedBones)
                {
                    var transform = selectedBone.transform;
                    if (hasValue(transform))
                    {
                        var pos = getValue(transform);
                        pos[i] = newValue;
                        setValue(transform, pos);
                    }
                }

                MTEUtils.LogDebug("新値を適用します：" + newValue);
                currentLayer.ApplyCurrentFrame(true);
            }
        }

        private void DrawValue(
            GUIView view,
            string label,
            float addedValue1,
            float addedValue2,
            Func<MTEP.ITransformData, float> getResetValue,
            Func<MTEP.ITransformData, bool> hasValue,
            Func<MTEP.ITransformData, float> getValue,
            Action<MTEP.ITransformData, float> setValue)
        {
            var value = 0f;
            var boneCount = 0;

            foreach (var bone in selectedBones)
            {
                var transform = bone.transform;
                var isNan = false;

                if (hasValue(transform))
                {
                    var _value = getValue(transform);
                    if (boneCount == 0)
                    {
                        value = _value;
                    }
                    else
                    {
                        if (_value != value)
                        {
                            value = float.NaN;
                            isNan = true;
                        }
                    }

                    boneCount++;
                    if (boneCount >= config.detailTransformCount || isNan)
                    {
                        break;
                    }
                }
            }

            if (boneCount == 0)
            {
                return;
            }

            var diffValue = 0f;
            var newValue = value;
            bool isReset = false;

            view.DrawFloatSelect(
                label,
                addedValue1,
                addedValue2,
                () => isReset = true,
                value,
                _newValue => newValue = _newValue,
                _diffValue => diffValue = _diffValue
            );

            // リセット
            if (isReset)
            {
                foreach (var selectedBone in selectedBones)
                {
                    var transform = selectedBone.transform;
                    if (hasValue(transform))
                    {
                        setValue(transform, getResetValue(transform));
                    }
                }

                MTEUtils.LogDebug("リセットします");
                currentLayer.ApplyCurrentFrame(true);
            }

            // 差分の適用
            if (diffValue != 0f)
            {
                foreach (var selectedBone in selectedBones)
                {
                    var transform = selectedBone.transform;
                    if (hasValue(transform))
                    {
                        var _value = getValue(transform);
                        _value += diffValue;
                        setValue(transform, _value);
                    }
                }

                MTEUtils.LogDebug("差分を適用します：" + diffValue);
                currentLayer.ApplyCurrentFrame(true);
            }

            // 新値の適用
            if (!float.IsNaN(newValue) && newValue != value)
            {
                foreach (var selectedBone in selectedBones)
                {
                    var transform = selectedBone.transform;
                    if (hasValue(transform))
                    {
                        setValue(transform, newValue);
                    }
                }

                MTEUtils.LogDebug("新値を適用します：" + newValue);
                currentLayer.ApplyCurrentFrame(true);
            }
        }

        private void DrawBoolValue(
            GUIView view,
            string label,
            Func<MTEP.ITransformData, bool> hasValue,
            Func<MTEP.ITransformData, bool> getValue,
            Action<MTEP.ITransformData, bool> setValue)
        {
            bool value = false;
            var boneCount = 0;

            foreach (var bone in selectedBones)
            {
                var transform = bone.transform;
                var isMixed = false;

                if (hasValue(transform))
                {
                    var _value = getValue(transform);
                    if (boneCount == 0)
                    {
                        value = _value;
                    }
                    else
                    {
                        if (_value != value)
                        {
                            value = false;
                            isMixed = true;
                        }
                    }

                    boneCount++;
                    if (boneCount >= config.detailTransformCount || isMixed)
                    {
                        break;
                    }
                }
            }

            if (boneCount == 0)
            {
                return;
            }

            var newValue = value;

            view.DrawToggle(
                label,
                value,
                100,
                20,
                _newValue => newValue = _newValue);

            // 新値の適用
            if (newValue != value)
            {
                foreach (var selectedBone in selectedBones)
                {
                    var transform = selectedBone.transform;
                    if (hasValue(transform))
                    {
                        setValue(transform, newValue);
                    }
                }

                MTEUtils.LogDebug("新値を適用します：" + newValue);
                currentLayer.ApplyCurrentFrame(true);
            }
        }

        private void DrawStrValue(
            GUIView view,
            string label,
            Func<MTEP.ITransformData, bool> hasValue,
            Func<MTEP.ITransformData, string> getValue,
            Action<MTEP.ITransformData, string> setValue)
        {
            var value = "";
            var boneCount = 0;

            foreach (var bone in selectedBones)
            {
                var transform = bone.transform;
                var isMixed = false;

                if (hasValue(transform))
                {
                    var _value = getValue(transform);
                    if (boneCount == 0)
                    {
                        value = _value;
                    }
                    else
                    {
                        if (_value != value)
                        {
                            value = "";
                            isMixed = true;
                        }
                    }

                    boneCount++;
                    if (boneCount >= config.detailTransformCount || isMixed)
                    {
                        break;
                    }
                }
            }

            if (boneCount == 0)
            {
                return;
            }

            var newValue = value;

            view.DrawTextField(
                label,
                0,
                value,
                -1,
                20,
                 _newValue => newValue = _newValue);

            // 新値の適用
            if (newValue != value)
            {
                foreach (var selectedBone in selectedBones)
                {
                    var transform = selectedBone.transform;
                    if (hasValue(transform))
                    {
                        setValue(transform, newValue);
                    }
                }

                MTEUtils.LogDebug("新値を適用します：" + newValue);
                currentLayer.ApplyCurrentFrame(true);
            }
        }

        private void DrawTangent(GUIView view)
        {
            var tangents = new HashSet<MTEP.TangentPair>();

            bool hasTangent = false;

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
                var outTangentDataList = prevBone.transform.GetOutTangentDataList(tangentValueType);
                var inTangentDataList = bone.transform.GetInTangentDataList(tangentValueType);

                for (var i = 0; i < outTangentDataList.Length && i < inTangentDataList.Length; i++)
                {
                    var outTangentData = outTangentDataList[i];
                    var inTangentData = inTangentDataList[i];

                    tangents.Add(new MTEP.TangentPair
                    {
                        outTangent = outTangentData.normalizedValue,
                        inTangent = inTangentData.normalizedValue,
                        isSmooth = outTangentData.isSmooth && inTangentData.isSmooth,
                    });

                    if (tangents.Count >= config.detailTangentCount)
                    {
                        break;
                    }
                }

                if (tangents.Count >= config.detailTangentCount)
                {
                    break;
                }
            }

            if (!hasTangent)
            {
                return;
            }

            bool isUpdated = false;

            if (cachedTangents.Count != tangents.Count)
            {
                isUpdated = true;
            }
            else
            {
                foreach (var tangent in cachedTangents)
                {
                    if (!tangents.Contains(tangent))
                    {
                        isUpdated = true;
                        break;
                    }
                }
            }

            if (isUpdated)
            {
                MTEUtils.LogDebug("補間曲線画像を更新します：" + tangents.Count);

                cachedTangents = tangents;

                TextureUtils.ClearTexture(tangentTex, config.curveBgColor);

                foreach (var tangent in tangents)
                {
                    var color = tangent.isSmooth ? config.curveLineSmoothColor : config.curveLineColor;
                    UpdateTangentTexture(
                        tangentTex,
                        tangent.outTangent,
                        tangent.inTangent,
                        color,
                        1
                    );
                }
            }

            Action<Action<MTEP.TangentData>> foreachOutTangent = (callback) =>
            {
                foreach (var prevBone in currentLayer.GetPrevBones(selectedBones))
                {
                    var outTangentDataList = prevBone.transform.GetOutTangentDataList(tangentValueType);
                    foreach (var outTangentData in outTangentDataList)
                    {
                        callback(outTangentData);
                    }
                }
            };

            Action<Action<MTEP.TangentData>> foreachInTangent = (callback) =>
            {
                foreach (var selectedBone in selectedBones)
                {
                    var inTangentDataList = selectedBone.transform.GetInTangentDataList(tangentValueType);
                    foreach (var inTangentData in inTangentDataList)
                    {
                        callback(inTangentData);
                    }
                }
            };

            view.AddSpace(10);

            view.DrawLabel("補間曲線", 100, 20);

            {
                // 曲線テクスチャの右側に種別コンボと Tangent 編集欄を並べる
                var subView = new GUIView(
                    tangentTex.width + 10,
                    view.currentPos.y,
                    Mathf.Max(100f, view.viewRect.width - tangentTex.width - 10),
                    200);
                subView.parent = view;

                _tangentValueTypeComboBox.onSelected = (type, index) =>
                {
                    tangentValueType = type;
                };
                _tangentValueTypeComboBox.currentIndex = (int)tangentValueType;
                _tangentValueTypeComboBox.DrawButton(subView);

                float outTangent = float.NaN;
                float inTangent = float.NaN;
                bool includeOutTangent = false;
                bool includeInTangent = false;

                foreach (var tangent in tangents)
                {
                    if (!includeOutTangent)
                    {
                        outTangent = tangent.outTangent;
                        includeOutTangent = true;
                    }
                    else if (outTangent != tangent.outTangent)
                    {
                        outTangent = float.NaN;
                    }

                    if (!includeInTangent)
                    {
                        inTangent = tangent.inTangent;
                        includeInTangent = true;
                    }
                    else if (inTangent != tangent.inTangent)
                    {
                        inTangent = float.NaN;
                    }
                }

                var diffOutTangent = 0f;
                var diffInTangent = 0f;
                var newOutTangent = outTangent;
                var newInTangent = inTangent;

                subView.DrawLabel("OutTangent", 100, 20);

                // SE 版 GUIView には IsComboBoxFocused がないため focusedComboBox 判定に置き換え
                subView.SetEnabled(subView.focusedComboBox == null && includeOutTangent);

                subView.DrawFloatSelect(
                    "",
                    0.1f,
                    0f,
                    null,
                    outTangent,
                    value => newOutTangent = value,
                    value => diffOutTangent = value
                );

                subView.SetEnabled(subView.focusedComboBox == null);

                subView.DrawLabel("InTangent", 100, 20);

                subView.SetEnabled(subView.focusedComboBox == null && includeInTangent);

                subView.DrawFloatSelect(
                    "",
                    0.1f,
                    0f,
                    null,
                    inTangent,
                    value => newInTangent = value,
                    value => diffInTangent = value
                );

                subView.SetEnabled(subView.focusedComboBox == null);

                // 新値の適用
                if (!float.IsNaN(newOutTangent) && newOutTangent != outTangent)
                {
                    foreachOutTangent((outTangentData) =>
                    {
                        outTangentData.normalizedValue = newOutTangent;
                        outTangentData.isSmooth = false;
                    });

                    MTEUtils.LogDebug("OutTangentを適用します：" + newOutTangent);
                    currentLayer.ApplyCurrentFrame(true);
                }

                if (!float.IsNaN(newInTangent) && newInTangent != inTangent)
                {
                    foreachInTangent((inTangentData) =>
                    {
                        inTangentData.normalizedValue = newInTangent;
                        inTangentData.isSmooth = false;
                    });

                    MTEUtils.LogDebug("InTangentを適用します：" + newInTangent);
                    currentLayer.ApplyCurrentFrame(true);
                }

                // 差分の適用
                if (diffOutTangent != 0f)
                {
                    foreachOutTangent((outTangentData) =>
                    {
                        outTangentData.normalizedValue += diffOutTangent;
                        outTangentData.isSmooth = false;
                    });

                    MTEUtils.LogDebug("OutTangentの差分を適用します：" + diffOutTangent);
                    currentLayer.ApplyCurrentFrame(true);
                }

                if (diffInTangent != 0f)
                {
                    foreachInTangent((inTangentData) =>
                    {
                        inTangentData.normalizedValue += diffInTangent;
                        inTangentData.isSmooth = false;
                    });

                    MTEUtils.LogDebug("InTangentの差分を適用します：" + diffInTangent);
                    currentLayer.ApplyCurrentFrame(true);
                }

                var isSmooth = tangents.All(tangent => tangent.isSmooth);
                subView.DrawToggle("自動補間", isSmooth, 100, 20, newIsSmooth =>
                {
                    foreachOutTangent((outTangentData) =>
                    {
                        outTangentData.isSmooth = newIsSmooth;
                    });

                    foreachInTangent((inTangentData) =>
                    {
                        inTangentData.isSmooth = newIsSmooth;
                    });

                    MTEUtils.LogDebug("自動補間を適用します：" + newIsSmooth);
                    currentLayer.ApplyCurrentFrame(true);
                });
            }

            view.DrawTexture(tangentTex);

            view.DrawLabel("プリセット反映", 100, 20);

            view.BeginHorizontal();

            for (int i = 0; i < tangentPresetTextures.Length; ++i)
            {
                var texture = tangentPresetTextures[i];
                var tangentType = (MTEP.TangentType)i;

                view.DrawTexture(
                    texture,
                    texture.width,
                    texture.height,
                    Color.white,
                    EventType.MouseDown,
                    _ =>
                {
                    if (selectedBones.Count == 0)
                    {
                        return;
                    }

                    var tangentPair = MTEP.TangentPair.GetDefault(tangentType);

                    foreachOutTangent((outTangentData) =>
                    {
                        outTangentData.normalizedValue = tangentPair.outTangent;
                        outTangentData.isSmooth = tangentPair.isSmooth;
                    });

                    foreachInTangent((inTangentData) =>
                    {
                        inTangentData.normalizedValue = tangentPair.inTangent;
                        inTangentData.isSmooth = tangentPair.isSmooth;
                    });

                    MTEUtils.LogDebug("プリセットを適用します：" + tangentType);
                    currentLayer.ApplyCurrentFrame(true);
                });
            }

            view.EndLayout();
        }

        private void DrawEasing(GUIView view)
        {
            var easings = new HashSet<int>();

            foreach (var bone in selectedBones)
            {
                if (!bone.transform.hasEasing)
                {
                    continue;
                }

                easings.Add(bone.transform.easing);
            }

            bool isUpdated = false;

            if (cachedEasings.Count != easings.Count)
            {
                isUpdated = true;
            }
            else
            {
                foreach (var cachedEasing in cachedEasings)
                {
                    if (!easings.Contains(cachedEasing))
                    {
                        isUpdated = true;
                        break;
                    }
                }
            }

            if (isUpdated)
            {
                MTEUtils.LogDebug("補間曲線画像を更新します：" + easings.Count);

                cachedEasings = easings;

                TextureUtils.ClearTexture(easingTex, config.curveBgColor);

                foreach (var easing in easings)
                {
                    UpdateEasingTexture(
                        easingTex,
                        easing,
                        config.curveLineColor,
                        1
                    );
                }
            }

            if (cachedEasings.Count == 0)
            {
                return;
            }

            view.AddSpace(10);

            Action<int> updateEasing = easing =>
            {
                var max = (int)MTEP.MoveEasingType.Max;
                easing = (easing + max) % max;

                foreach (var bone in selectedBones)
                {
                    bone.transform.easing = easing;
                }

                MTEUtils.LogDebug("Easingを適用します: " + easing);
                currentLayer.ApplyCurrentFrame(true);
            };

            {
                int easing = -1;
                bool includeEasing = false;

                foreach (var _easing in easings)
                {
                    if (!includeEasing)
                    {
                        easing = _easing;
                        includeEasing = true;
                    }
                    else if (easing != _easing)
                    {
                        easing = -1;
                    }
                }

                easingComboBox.currentIndex = easing;
                easingComboBox.onSelected = (_easing, index) =>
                {
                    updateEasing(_easing);
                };
                easingComboBox.DrawButton("補間曲線", view);
            }

            view.DrawTexture(easingTex);
        }

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

            for (int x = 0; x < width; x++)
            {
                float t = x / (float)width;
                int y = (int)(MTEP.PluginUtils.HermiteSimplified(outTangent, inTangent, t) * height);

                y -= halfLineWidth;
                for (int i = 0; i < lineWidth; i++)
                {
                    var yy = Mathf.Clamp(y + i, 0, height - 1);
                    texture.SetPixel(x, yy, lineColor);
                }
            }

            texture.Apply();
        }

        private static void UpdateEasingTexture(
            Texture2D texture,
            int easing,
            Color lineColor,
            int lineWidth)
        {
            var width = texture.width;
            var height = texture.height;
            var halfLineWidth = lineWidth / 2;

            for (int x = 0; x < width; x++)
            {
                float t = x / (float)width;
                int y = (int)(currentLayer.CalcEasingValue(t, easing) * height);

                y -= halfLineWidth;
                for (int i = 0; i < lineWidth; i++)
                {
                    var yy = Mathf.Clamp(y + i, 0, height - 1);
                    texture.SetPixel(x, yy, lineColor);
                }
            }

            texture.Apply();
        }
    }
}
