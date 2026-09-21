using System;
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using COM3D2.MotionTimelineEditor.Plugin;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// キーフレーム詳細の一括編集モード。
    /// 選択キーフレームを TransformType ごとにまとめ、グループ内で値が揃っていればその値、
    /// 混在していれば空欄 (数値は NaN) で描く。編集はグループ内の全キーフレームへ反映する
    /// (混在時の表現がない bool トグルは OFF、色は先頭キーフレームの値で描く)
    /// </summary>
    public class KeyFrameBatchDrawer
    {
        private static MTEP.TimelineManager timelineManager => MTEP.TimelineManager.instance;

        private const float RowHeight = 20f;
        private const float TransformLabelWidth = 50f;
        private const float FoldMarkWidth = 16f;
        private const float HeaderButtonWidth = 44f;
        private const float MinHeaderLabelWidth = 40f;
        private const float StrLabelWidth = 70f;
        private const float CustomLabelWidth = 100f;
        private const float CustomSliderWidth = -1f;

        private const float PositionSensitivity = 0.01f;
        private const float RotationSensitivity = 1f;
        private const float ScaleSensitivity = 0.01f;

        /// <summary>1 グループ分の描画対象 (型と所属キーフレーム)</summary>
        private class Group
        {
            public MTEP.TransformType type;
            public readonly List<MTEP.BoneData> bones = new List<MTEP.BoneData>();
        }

        /// <summary>折りたたみ中のグループ (セッション中のみ有効)</summary>
        private readonly HashSet<MTEP.TransformType> _collapsedTypes = new HashSet<MTEP.TransformType>();

        /// <summary>毎フレームの確保を避けるためのグループバッファ (順序は選択の並び順)</summary>
        private readonly List<Group> _groups = new List<Group>();
        private readonly Dictionary<MTEP.TransformType, Group> _groupMap =
            new Dictionary<MTEP.TransformType, Group>();

        /// <summary>追従メイド / 追従点のコンボ (グループごとに実体を分ける)</summary>
        private readonly MaidFollowCustomValueDrawer _followValueDrawer =
            new MaidFollowCustomValueDrawer();

        /// <summary>Apply でレイヤーを重複なく反映するためのバッファ</summary>
        private readonly HashSet<MTEP.ITimelineLayer> _applyLayers = new HashSet<MTEP.ITimelineLayer>();

        /// <summary>描画ループ終了後に開閉を切り替えるグループ</summary>
        private MTEP.TransformType? _pendingToggleType = null;

        /// <summary>
        /// 並び順が決まった選択キーフレームをグループ描画する。
        /// 削除は selectedBones を変更するため、呼び出し側へ委ねる
        /// (渡すリストは次回 Draw で使い回すので、呼び出し側でコピーすること)
        /// </summary>
        public void Draw(GUIView view, List<MTEP.BoneData> sortedBones, Action<List<MTEP.BoneData>> requestDelete)
        {
            BuildGroups(sortedBones);

            foreach (var group in _groups)
            {
                DrawGroupBlock(view, group, requestDelete);
            }

            ProcessPendingToggle();
            EndFrame();
        }

        /// <summary>選択や開閉で描かれなくなった行のコンボを捨てる (描画しないフレームも呼ぶ)</summary>
        public void EndFrame()
        {
            _followValueDrawer.EndFrame();
        }

        public void Clear()
        {
            _collapsedTypes.Clear();
            _followValueDrawer.Clear();
        }

        private void BuildGroups(List<MTEP.BoneData> sortedBones)
        {
            _groups.Clear();
            _groupMap.Clear();

            foreach (var bone in sortedBones)
            {
                var type = bone.transform.type;
                Group group;
                if (!_groupMap.TryGetValue(type, out group))
                {
                    group = new Group { type = type };
                    _groupMap[type] = group;
                    _groups.Add(group);
                }
                group.bones.Add(bone);
            }
        }

        private void DrawGroupBlock(GUIView view, Group group, Action<List<MTEP.BoneData>> requestDelete)
        {
            view.DrawHorizontalLine(Color.gray);

            var expanded = !_collapsedTypes.Contains(group.type);
            DrawGroupHeader(view, group, expanded, requestDelete);

            if (!expanded)
            {
                return;
            }

            DrawTransform(view, group);
            DrawColorRows(view, group);
            DrawCustomValues(view, group);
            DrawStrValues(view, group);
        }

        /// <summary>開閉マーク + 型名(件数) + 初期化 / 削除ボタンの 1 行</summary>
        private void DrawGroupHeader(
            GUIView view, Group group, bool expanded, Action<List<MTEP.BoneData>> requestDelete)
        {
            var available = view.viewRect.width - view.padding.x * 2;
            var labelWidth = available
                - FoldMarkWidth - HeaderButtonWidth * 2 - view.margin * 4;
            labelWidth = Mathf.Max(labelWidth, MinHeaderLabelWidth);

            view.BeginHorizontal();
            {
                Action toggle = () => _pendingToggleType = group.type;

                view.DrawLabel(expanded ? "▼" : "▶", FoldMarkWidth, RowHeight,
                    onClickAction: toggle);
                view.DrawLabel(
                    string.Format("{0} ({1}個)", group.type, group.bones.Count),
                    labelWidth, RowHeight, onClickAction: toggle);

                if (view.DrawButton("初期化", HeaderButtonWidth, RowHeight))
                {
                    foreach (var bone in group.bones)
                    {
                        bone.transform.Reset();
                    }
                    MTEUtils.LogDebug("キーフレームを一括初期化します：" + group.type);
                    Apply(group);
                }

                if (view.DrawButton("削除", HeaderButtonWidth, RowHeight))
                {
                    requestDelete(group.bones);
                }
            }
            view.EndLayout();
        }

        /// <summary>
        /// 開閉の反映を描画ループの外へ遅延させる
        /// (同じ Unity フレームの途中で描画要素数が変わると FieldCache の採番がずれる)
        /// </summary>
        private void ProcessPendingToggle()
        {
            if (_pendingToggleType == null)
            {
                return;
            }
            var type = _pendingToggleType.Value;
            _pendingToggleType = null;

            if (!_collapsedTypes.Remove(type))
            {
                _collapsedTypes.Add(type);
            }
        }

        /// <summary>
        /// 色ラベルを型ごとに一意化する。ColorPickerWindow はラベル文字列で編集対象を同定するため、
        /// 個別表示と同じ色名のままだと編集ウィンドウが取り違える
        /// (数値欄の FloatFieldCache は描画順で採番されるので一意化は不要)
        /// </summary>
        private static string ColorLabel(Group group, string name)
        {
            return string.Format("{0} [一括 {1}]", name, group.type);
        }

        private void DrawTransform(GUIView view, Group group)
        {
            var first = group.bones[0].transform;

            if (first.hasPosition)
            {
                DrawVector3Row(view, group, "位置", PositionSensitivity,
                    t => t.position,
                    (t, v) => t.position = v,
                    t => t.position = t.initialPosition);
            }

            if (first.hasRotation || first.hasEulerAngles)
            {
                DrawVector3Row(view, group, "回転", RotationSensitivity,
                    t => t.normalizedEulerAngles,
                    (t, v) => t.eulerAngles = v,
                    t => t.eulerAngles = t.initialEulerAngles);
            }

            if (first.hasScale)
            {
                DrawVector3Row(view, group, "拡縮", ScaleSensitivity,
                    t => t.scale,
                    (t, v) => t.scale = v,
                    t => t.scale = t.initialScale);
            }
        }

        /// <summary>
        /// 軸ごとに値が揃っていればその値、混在なら NaN で空欄にする。
        /// 混在軸はドラッグで NaN が返るため、その軸だけは反映を見送る
        /// </summary>
        private void DrawVector3Row(
            GUIView view,
            Group group,
            string label,
            float sensitivity,
            Func<MTEP.ITransformData, Vector3> getValue,
            Action<MTEP.ITransformData, Vector3> setValue,
            Action<MTEP.ITransformData> resetValue)
        {
            var uniform = getValue(group.bones[0].transform);
            for (var i = 1; i < group.bones.Count; i++)
            {
                var value = getValue(group.bones[i].transform);
                for (var axis = 0; axis < 3; axis++)
                {
                    if (uniform[axis] != value[axis])
                    {
                        uniform[axis] = float.NaN;
                    }
                }
            }

            view.DrawVector3Row(new GUIView.Vector3RowOption
            {
                label = label,
                labelWidth = TransformLabelWidth,
                height = RowHeight,
                dragSensitivity = sensitivity,
                value = uniform,
                onChangedAxis = (newValue, axis) =>
                {
                    var axisValue = newValue[axis];
                    if (float.IsNaN(axisValue))
                    {
                        return;
                    }
                    foreach (var bone in group.bones)
                    {
                        var current = getValue(bone.transform);
                        current[axis] = axisValue;
                        setValue(bone.transform, current);
                    }
                    Apply(group);
                },
                onReset = () =>
                {
                    foreach (var bone in group.bones)
                    {
                        resetValue(bone.transform);
                    }
                    Apply(group);
                },
            });
        }

        /// <summary>グループ内の全キーフレームが持つ色だけ描く。混在時は先頭の色を表示する</summary>
        private void DrawColorRows(GUIView view, Group group)
        {
            var first = group.bones[0].transform;
            foreach (var pair in first.GetColorValueInfoMap())
            {
                var colorKey = pair.Key;
                if (!AllHave(group, t => t.HasColorValue(colorKey)))
                {
                    continue;
                }

                var info = pair.Value;
                var fieldCache = view.GetColorFieldCache(ColorLabel(group, info.name), info.hasAlpha);
                view.DrawColor(fieldCache, first.GetColorValue(colorKey), info.defaultValue, newValue =>
                {
                    foreach (var bone in group.bones)
                    {
                        bone.transform.SetColorValue(colorKey, newValue);
                    }
                    Apply(group);
                });
            }
        }

        private void DrawCustomValues(GUIView view, Group group)
        {
            var first = group.bones[0].transform;
            foreach (var pair in first.GetCustomValueInfoMap())
            {
                var customKey = pair.Key;
                if (!AllHave(group, t => t.HasCustomValue(customKey)))
                {
                    continue;
                }
                DrawCustomValueRow(view, group, customKey, pair.Value);
            }

            if (first.hasVisible)
            {
                var visible = UniformFloat(group, t => t.visible ? 1f : 0f);
                view.DrawToggle("表示", visible == 1f, -1, RowHeight, newValue =>
                {
                    foreach (var bone in group.bones)
                    {
                        bone.transform.visible = newValue;
                    }
                    Apply(group);
                });
            }
        }

        private void DrawCustomValueRow(
            GUIView view,
            Group group,
            string customKey,
            MTEP.CustomValueInfo info)
        {
            var value = UniformFloat(group, t => t.GetCustomValue(customKey).value);

            Action<float> setAll = newValue =>
            {
                foreach (var bone in group.bones)
                {
                    bone.transform.GetCustomValue(customKey).value = newValue;
                }
                Apply(group);
            };

            if (MaidFollowCustomValueDrawer.IsComboValue(info))
            {
                // owner はフレームをまたいで同じ必要があるが Group は毎フレーム作り直すため、
                // グループを一意にする型を渡す
                _followValueDrawer.Draw(
                    view, group.type, customKey, info, value, CustomLabelWidth, RowHeight,
                    setAll);
                return;
            }

            switch (info.type)
            {
                case MTEP.CustomValueType.BoolValue:
                    view.DrawCustomValueBool(info, value == 1f, newValue => setAll(newValue ? 1f : 0f));
                    break;

                case MTEP.CustomValueType.IntValue:
                    // DrawCustomValueInt は int 値しか受けず混在 (NaN) を表せないため、スライダーを直接描く
                    view.DrawSliderValue(new GUIView.SliderOption
                    {
                        label = info.name,
                        labelWidth = CustomLabelWidth,
                        width = CustomSliderWidth,
                        fieldType = FloatFieldType.Int,
                        min = info.min,
                        max = info.max,
                        step = info.step,
                        defaultValue = info.defaultValue,
                        value = value,
                        onChanged = newValue => setAll(Mathf.Round(newValue)),
                    });
                    break;

                default:
                    view.DrawCustomValueFloat(info, value, setAll,
                        () =>
                        {
                            foreach (var bone in group.bones)
                            {
                                bone.transform.GetCustomValue(customKey).value =
                                    bone.transform.GetDefaultCustomValue(customKey);
                            }
                            Apply(group);
                        },
                        labelWidth: CustomLabelWidth,
                        sliderWidth: CustomSliderWidth);
                    break;
            }
        }

        private void DrawStrValues(GUIView view, Group group)
        {
            var first = group.bones[0].transform;
            foreach (var pair in first.GetStrValueInfoMap())
            {
                var strKey = pair.Key;
                if (!AllHave(group, t => t.HasStrValue(strKey)))
                {
                    continue;
                }

                var uniform = first.GetStrValue(strKey);
                var isMixed = false;
                for (var i = 1; i < group.bones.Count; i++)
                {
                    if (group.bones[i].transform.GetStrValue(strKey) != uniform)
                    {
                        isMixed = true;
                        uniform = "";
                        break;
                    }
                }

                view.DrawTextField(
                    first.GetStrValueName(strKey),
                    StrLabelWidth,
                    uniform,
                    -1,
                    RowHeight,
                    newValue =>
                    {
                        // 混在時の空欄は目印なので、空文字への一括変更は通す
                        if (!isMixed && newValue == uniform)
                        {
                            return;
                        }
                        foreach (var bone in group.bones)
                        {
                            bone.transform.SetStrValue(strKey, newValue);
                        }
                        Apply(group);
                    });
            }
        }

        private static bool AllHave(Group group, Func<MTEP.ITransformData, bool> predicate)
        {
            foreach (var bone in group.bones)
            {
                if (!predicate(bone.transform))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>グループ内で揃っていればその値、混在なら NaN</summary>
        private static float UniformFloat(Group group, Func<MTEP.ITransformData, float> getValue)
        {
            var result = getValue(group.bones[0].transform);
            for (var i = 1; i < group.bones.Count; i++)
            {
                if (getValue(group.bones[i].transform) != result)
                {
                    return float.NaN;
                }
            }
            return result;
        }

        /// <summary>グループの所属レイヤーへ重複なく反映し、履歴を要求する (選択は複数レイヤーにまたがりうる)</summary>
        private void Apply(Group group)
        {
            MTEUtils.LogDebug("キーフレームを一括更新します：" + group.type);
            // ドラッグ中は毎フレーム呼ばれるため、履歴はマウスを離すまで集約させる
            timelineManager.RequestHistory("キーフレーム一括編集: " + group.type);
            _applyLayers.Clear();
            foreach (var bone in group.bones)
            {
                if (_applyLayers.Add(bone.parentLayer))
                {
                    bone.parentLayer.ApplyCurrentFrame(true);
                }
            }
            _applyLayers.Clear();
        }
    }
}
