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
    /// 位置/回転/拡縮/色などの Transform 値とレイヤー固有のカスタム値を編集できる
    /// (補間曲線の編集は TimelineCurveEditor が担当する)。
    /// 編集は選択中の全キーフレームへ適用し、ApplyCurrentFrame で即時反映する
    /// </summary>
    public class KeyFrameInspector
    {
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

        public void Draw(GUIView view)
        {
            if (!ShouldDraw())
            {
                view.DrawLabel("キーフレームが選択されていません", -1, 20);
                return;
            }

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
    }
}
