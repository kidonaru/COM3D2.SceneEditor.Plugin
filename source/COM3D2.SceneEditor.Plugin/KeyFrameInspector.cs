using System;
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 選択中キーフレームの詳細表示・編集。
    /// 選択キーフレームごとに折りたたみ可能なブロックを縦に並べ、
    /// Transform は標準 Inspector と同じ横並び行、その他のパラメータは
    /// ウィンドウ幅に合わせて折り返すドラッグ可能な数値入力で個別に編集する
    /// (補間曲線の編集は TimelineCurveEditor が担当する)
    /// </summary>
    public class KeyFrameInspector
    {
        private static MTEP.Config config => MTEP.ConfigManager.instance.config;
        private static MTEP.TimelineManager timelineManager => MTEP.TimelineManager.instance;
        private static MTEP.ITimelineLayer currentLayer => timelineManager.currentLayer;
        private static HashSet<MTEP.BoneData> selectedBones => timelineManager.selectedBones;

        private const float RowHeight = 20f;
        /// <summary>Transform 行のラベル幅 (InspectorWindow と揃える)</summary>
        private const float TransformLabelWidth = 50f;
        /// <summary>ブロックヘッダーの開閉マーク幅</summary>
        private const float FoldMarkWidth = 16f;
        /// <summary>ブロックヘッダー右端のボタン幅</summary>
        private const float HeaderButtonWidth = 44f;
        /// <summary>フロー要素 1 個ぶんの目安幅 (ラベル + 数値入力)</summary>
        private const float FlowItemWidth = 130f;
        /// <summary>フロー要素内のラベル幅</summary>
        private const float FlowLabelWidth = 70f;
        /// <summary>ブロック内容の左インデント</summary>
        private const float BlockIndent = 8f;

        // 1px ドラッグあたりの増減量 (InspectorWindow と揃える)
        private const float PositionSensitivity = 0.01f;
        private const float RotationSensitivity = 1f;
        private const float ScaleSensitivity = 0.01f;
        private const float ColorSensitivity = 0.01f;

        /// <summary>
        /// 折りたたみ中のキーフレーム。既定は展開なので「畳んだもの」だけを覚える。
        /// Inspector 外 (タイムラインのショートカット等) からも削除されうるので、
        /// 毎フレーム選択中のものだけへ絞り込んで古い BoneData 参照を残さない
        /// (セッション中のみ有効。config へは永続化しない)
        /// </summary>
        private readonly HashSet<MTEP.BoneData> _collapsedBones = new HashSet<MTEP.BoneData>();

        /// <summary>描画順を安定させるための並べ替えバッファ (毎フレームの確保を避ける)</summary>
        private readonly List<MTEP.BoneData> _sortedBones = new List<MTEP.BoneData>();

        /// <summary>
        /// 削除は selectedBones を書き換えるため、描画ループ中には実行できない。
        /// 描画後にまとめて処理する
        /// </summary>
        private MTEP.BoneData _pendingDeleteBone = null;

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
                _collapsedBones.Clear();
                view.DrawLabel("キーフレームが選択されていません", -1, RowHeight);
                return;
            }

            // Inspector 外の削除経路を通ると選択から外れた BoneData が残るため、ここで掃除する
            _collapsedBones.RemoveWhere(bone => !selectedBones.Contains(bone));

            var maxCount = Mathf.Max(1, config.detailTransformCount);
            var totalCount = selectedBones.Count;

            view.DrawLabel(
                string.Format("キーフレーム詳細 ({0}個選択中)", totalCount), -1, RowHeight);

            // 選択順は HashSet で不定なので、フレーム番号 → 名前で毎回同じ並びにする
            _sortedBones.Clear();
            _sortedBones.AddRange(selectedBones);
            _sortedBones.Sort(CompareBone);

            var drawCount = Mathf.Min(totalCount, maxCount);
            for (var i = 0; i < drawCount; i++)
            {
                DrawBoneBlock(view, _sortedBones[i]);
            }

            if (totalCount > drawCount)
            {
                view.DrawLabel(
                    string.Format("他 {0} 個は非表示", totalCount - drawCount), -1, RowHeight);
            }

            _sortedBones.Clear();

            ProcessPendingDelete();
        }

        private static int CompareBone(MTEP.BoneData a, MTEP.BoneData b)
        {
            var result = a.frameNo.CompareTo(b.frameNo);
            if (result != 0)
            {
                return result;
            }
            return string.CompareOrdinal(a.name, b.name);
        }

        private void DrawBoneBlock(GUIView view, MTEP.BoneData bone)
        {
            view.DrawHorizontalLine(Color.gray);

            var expanded = !_collapsedBones.Contains(bone);
            DrawBlockHeader(view, bone, expanded);

            if (!expanded)
            {
                return;
            }

            var transform = bone.transform;
            DrawTransform(view, bone, transform);
            DrawFlowValues(view, bone, transform);
        }

        /// <summary>開閉マーク + ボーン名(フレーム番号) + 初期化 / 削除ボタンの 1 行</summary>
        private void DrawBlockHeader(GUIView view, MTEP.BoneData bone, bool expanded)
        {
            var available = view.viewRect.width - view.padding.x * 2;
            // 要素は 4 個 (マーク・名前・初期化・削除) なので margin を 4 個ぶん引く
            var labelWidth = available
                - FoldMarkWidth - HeaderButtonWidth * 2 - view.margin * 4;
            labelWidth = Mathf.Max(labelWidth, 40f);

            view.BeginHorizontal();
            {
                Action toggle = () => ToggleCollapsed(bone);

                view.DrawLabel(expanded ? "▼" : "▶", FoldMarkWidth, RowHeight,
                    onClickAction: toggle);
                view.DrawLabel(
                    string.Format("{0} (F{1})", bone.name, bone.frameNo),
                    labelWidth, RowHeight, onClickAction: toggle);

                if (view.DrawButton("初期化", HeaderButtonWidth, RowHeight))
                {
                    bone.transform.Reset();
                    MTEUtils.LogDebug("キーフレームを初期化します：" + bone.name);
                    currentLayer.ApplyCurrentFrame(true);
                }

                if (view.DrawButton("削除", HeaderButtonWidth, RowHeight))
                {
                    _pendingDeleteBone = bone;
                }
            }
            view.EndLayout();
        }

        private void ToggleCollapsed(MTEP.BoneData bone)
        {
            if (!_collapsedBones.Remove(bone))
            {
                _collapsedBones.Add(bone);
            }
        }

        /// <summary>削除は selectedBones を変更するため、描画ループを抜けてから実行する</summary>
        private void ProcessPendingDelete()
        {
            var bone = _pendingDeleteBone;
            if (bone == null)
            {
                return;
            }
            _pendingDeleteBone = null;

            var frame = bone.parentFrame;
            if (frame != null)
            {
                frame.RemoveBone(bone);
            }

            selectedBones.Remove(bone);
            _collapsedBones.Remove(bone);

            currentLayer.CleanFrames();
            currentLayer.ApplyCurrentFrame(true);

            MTEUtils.LogDebug("キーフレームを削除します：" + bone.name);
            timelineManager.RequestHistory("キーフレーム削除");
        }

        /// <summary>位置 / 回転 / 拡縮 / 色 を標準 Inspector と同じ横並び行で描く</summary>
        private void DrawTransform(GUIView view, MTEP.BoneData bone, MTEP.ITransformData transform)
        {
            if (transform.hasPosition)
            {
                DrawVector3Row(view, bone, "位置", PositionSensitivity,
                    transform.position,
                    value => transform.position = value,
                    () => transform.position = transform.initialPosition);
            }

            if (transform.hasRotation || transform.hasEulerAngles)
            {
                DrawVector3Row(view, bone, "回転", RotationSensitivity,
                    transform.normalizedEulerAngles,
                    value => transform.eulerAngles = value,
                    () => transform.eulerAngles = transform.initialEulerAngles);
            }

            if (transform.hasScale)
            {
                DrawVector3Row(view, bone, "拡縮", ScaleSensitivity,
                    transform.scale,
                    value => transform.scale = value,
                    () => transform.scale = transform.initialScale);
            }

            if (transform.hasColor)
            {
                DrawVector3Row(view, bone, "色", ColorSensitivity,
                    transform.color.ToVector3(),
                    value => transform.color = value.ToColor(),
                    () => transform.color = transform.initialColor);
            }
        }

        private void DrawVector3Row(
            GUIView view,
            MTEP.BoneData bone,
            string label,
            float sensitivity,
            Vector3 value,
            Action<Vector3> setValue,
            Action resetValue)
        {
            // GUIView.DrawVector3Row は数値入力の幅を viewRect.width から算出するため、
            // 外側を BeginHorizontal + AddSpace で包むと右端をはみ出す。インデントしないこと
            view.DrawVector3Row(new GUIView.Vector3RowOption
            {
                label = label,
                labelWidth = TransformLabelWidth,
                height = RowHeight,
                dragSensitivity = sensitivity,
                value = value,
                onChanged = newValue =>
                {
                    setValue(newValue);
                    Apply(bone);
                },
                onReset = () =>
                {
                    resetValue();
                    Apply(bone);
                },
            });
        }

        /// <summary>
        /// カスタム値・文字列値・表示トグルを、ウィンドウ幅に入るだけ横に並べて折り返す。
        /// 種類をまたいで詰めると型ごとの見分けがつかなくなるため、
        /// 「カスタム値 → 表示トグル」までを 1 つの流れとし、文字列値は 1 行ずつ別に描く
        /// </summary>
        private void DrawFlowValues(GUIView view, MTEP.BoneData bone, MTEP.ITransformData transform)
        {
            var items = new List<Action>();

            foreach (var pair in transform.GetCustomValueInfoMap())
            {
                var customKey = pair.Key;
                var info = pair.Value;
                if (!transform.HasCustomValue(customKey))
                {
                    continue;
                }

                items.Add(() => DrawCustomValueItem(view, bone, transform, customKey, info));
            }

            if (transform.hasVisible)
            {
                items.Add(() => DrawVisibleItem(view, bone, transform));
            }

            DrawFlow(view, items);

            DrawStrValues(view, bone, transform);
        }

        /// <summary>要素を折り返しながら並べる。1 行ぶんずつ BeginHorizontal で囲む</summary>
        private void DrawFlow(GUIView view, List<Action> items)
        {
            if (items.Count == 0)
            {
                return;
            }

            var available = view.viewRect.width - view.padding.x * 2
                - BlockIndent - view.margin;
            var columnCount = KeyFrameFlowLayout.GetColumnCount(
                available, FlowItemWidth, view.margin);

            for (var i = 0; i < items.Count; i += columnCount)
            {
                view.BeginHorizontal();
                {
                    view.AddSpace(BlockIndent);

                    var end = Mathf.Min(i + columnCount, items.Count);
                    for (var j = i; j < end; j++)
                    {
                        items[j]();
                    }
                }
                view.EndLayout();
            }
        }

        /// <summary>
        /// カスタム値 1 個。bool 相当はトグル、整数相当は int 入力、
        /// それ以外はドラッグ可能な float 入力にする
        /// </summary>
        private void DrawCustomValueItem(
            GUIView view,
            MTEP.BoneData bone,
            MTEP.ITransformData transform,
            string customKey,
            MTEP.CustomValueInfo info)
        {
            var name = transform.GetCustomValueName(customKey);
            var valueData = transform.GetCustomValue(customKey);
            // リセットボタン (20px) はドラッグ入力側が fieldWidth から内側に取る
            var fieldWidth = FlowItemWidth - FlowLabelWidth - view.margin;

            Action reset = () =>
            {
                transform.GetCustomValue(customKey).value = transform.GetDefaultCustomValue(customKey);
                Apply(bone);
            };

            if (info.type == MTEP.CustomValueType.BoolValue)
            {
                view.DrawToggle(name, valueData.value != 0f, FlowItemWidth, RowHeight,
                    newValue =>
                    {
                        transform.GetCustomValue(customKey).value = newValue ? 1f : 0f;
                        Apply(bone);
                    });
                return;
            }

            if (info.type == MTEP.CustomValueType.IntValue)
            {
                view.DrawDragIntField(new GUIView.DragIntFieldOption
                {
                    label = name,
                    labelWidth = FlowLabelWidth,
                    value = Mathf.RoundToInt(valueData.value),
                    minValue = Mathf.RoundToInt(info.min),
                    maxValue = Mathf.RoundToInt(info.max),
                    fieldWidth = fieldWidth,
                    height = RowHeight,
                    // dragSensitivity は既定 (DefaultIntDragSensitivity = 0.5) に任せる。
                    // 1.0 にすると 1px で 1 段変わって細かい調整ができない
                    onChanged = newValue =>
                    {
                        transform.GetCustomValue(customKey).value = newValue;
                        Apply(bone);
                    },
                    onReset = reset,
                });
                return;
            }

            view.DrawDragFloatField(new GUIView.DragFloatFieldOption
            {
                label = name,
                labelWidth = FlowLabelWidth,
                value = valueData.value,
                minValue = info.min,
                maxValue = info.max,
                fieldWidth = fieldWidth,
                height = RowHeight,
                dragSensitivity = info.step > 0f ? info.step : GUIView.DefaultFloatDragSensitivity,
                onChanged = newValue =>
                {
                    transform.GetCustomValue(customKey).value = newValue;
                    Apply(bone);
                },
                onReset = reset,
            });
        }

        private void DrawVisibleItem(GUIView view, MTEP.BoneData bone, MTEP.ITransformData transform)
        {
            view.DrawToggle("表示", transform.visible, FlowItemWidth, RowHeight,
                newValue =>
                {
                    transform.visible = newValue;
                    Apply(bone);
                });
        }

        /// <summary>文字列値は幅が読めないので 1 行ずつ全幅で描く</summary>
        private void DrawStrValues(GUIView view, MTEP.BoneData bone, MTEP.ITransformData transform)
        {
            foreach (var pair in transform.GetStrValueInfoMap())
            {
                var strKey = pair.Key;
                if (!transform.HasStrValue(strKey))
                {
                    continue;
                }

                var value = transform.GetStrValue(strKey);

                view.BeginHorizontal();
                {
                    view.AddSpace(BlockIndent);
                    view.DrawTextField(
                        transform.GetStrValueName(strKey),
                        FlowLabelWidth,
                        value,
                        -1,
                        RowHeight,
                        newValue =>
                        {
                            if (newValue == value)
                            {
                                return;
                            }
                            transform.SetStrValue(strKey, newValue);
                            Apply(bone);
                        });
                }
                view.EndLayout();
            }
        }

        /// <summary>編集を即時反映する</summary>
        private void Apply(MTEP.BoneData bone)
        {
            MTEUtils.LogDebug("キーフレームを更新します：" + bone.name);
            currentLayer.ApplyCurrentFrame(true);
        }
    }
}
