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
        public const float TOGGLE_BAR_HEIGHT = 16f;
        private const int MIN_PANE_HEIGHT = 80;
        private const int MAX_PANE_HEIGHT = 400;
        private const float TOGGLE_BUTTON_WIDTH = 80f;

        /// <summary>カーブ折れ線のサンプリング間隔 (px)。線分の太さも兼ねる</summary>
        private const float SAMPLE_STEP = 2f;
        /// <summary>全身ボーン選択時の draw call 急増を避けるための表示上限</summary>
        private const int MAX_CHANNELS = 12;
        private const float KEY_MARKER_SIZE = 6f;

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

        /// <summary>直近の描画で使ったマッピング。ヒットテストは描画済みの座標系に合わせる</summary>
        private MTEP.CurveViewMapping _mapping = null;
        private List<CurveChannel> _channels = new List<CurveChannel>();

        /// <summary>1 本のカーブ = 1 ボーン × 1 値チャンネル</summary>
        private class CurveChannel
        {
            public string boneName;
            /// <summary>単チャンネル型。カスタム値チャンネルでは未使用</summary>
            public MTEP.TangentValueType valueType;
            /// <summary>カスタム値チャンネルのキー名 (通常チャンネルは null)</summary>
            public string customKey;
            public Color color;
            /// <summary>フレーム番号順のキー列</summary>
            public List<int> frameNos = new List<int>();
            public List<MTEP.ValueData> values = new List<MTEP.ValueData>();
            public List<MTEP.BoneData> keyBones = new List<MTEP.BoneData>();
            /// <summary>表示範囲のサンプル値 (SAMPLE_STEP px 刻み)</summary>
            public List<float> samples = new List<float>();
        }

        private static readonly MTEP.TangentValueType[] SingleChannelTypes = {
            MTEP.TangentValueType.X移動, MTEP.TangentValueType.Y移動, MTEP.TangentValueType.Z移動,
            MTEP.TangentValueType.X回転, MTEP.TangentValueType.Y回転, MTEP.TangentValueType.Z回転,
            MTEP.TangentValueType.X拡縮, MTEP.TangentValueType.Y拡縮, MTEP.TangentValueType.Z拡縮,
        };

        private static readonly Color[] ChannelColors = {
            new Color(0.9f, 0.3f, 0.3f),  // X = 赤
            new Color(0.3f, 0.9f, 0.3f),  // Y = 緑
            new Color(0.4f, 0.6f, 1.0f),  // Z = 青
        };

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

            // 開いている間はトグルボタン右のバー領域を上下ドラッグして高さを変更する
            var resizeX = barRect.x + 5 + TOGGLE_BUTTON_WIDTH;
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
                view.DrawLabel("カーブ対象のキーフレームが選択されていません", 400, 20, Color.gray);
                return;
            }

            // 表示中の座標系は Repaint 時に確定させ、他イベントは同じ座標系でヒットテストする
            if (Event.current.type == EventType.Repaint || _mapping == null)
            {
                _mapping = BuildMapping(_channels, paneRect, scrollX, frameWidth);
            }

            if (Event.current.type == EventType.Repaint)
            {
                DrawValueScale(view, paneRect);

                foreach (var channel in _channels)
                {
                    DrawChannelCurve(view, channel, paneRect);
                }

                foreach (var channel in _channels)
                {
                    DrawChannelKeys(view, channel, paneRect, scrollX);
                }

                if (totalChannelCount > _channels.Count)
                {
                    view.currentPos = new Vector2(paneRect.x + 8, paneRect.y + 2);
                    view.DrawLabel(
                        string.Format("表示上限 {0} チャンネル (全 {1})", MAX_CHANNELS, totalChannelCount),
                        260, 18, Color.gray);
                }
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

                for (var i = 0; i < valueTypes.Count; i++)
                {
                    var valueType = valueTypes[i];
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
                        color = ChannelColors[i % ChannelColors.Length],
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
                foreach (var customKey in firstTransform.GetCustomValueInfoMap().Keys)
                {
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

        /// <summary>複合型フィルタを単チャンネル型へ展開する</summary>
        private static List<MTEP.TangentValueType> ExpandValueTypes(MTEP.TangentValueType filter)
        {
            switch (filter)
            {
                case MTEP.TangentValueType.すべて:
                    return SingleChannelTypes.ToList();
                case MTEP.TangentValueType.移動:
                    return SingleChannelTypes.Take(3).ToList();
                case MTEP.TangentValueType.回転:
                    return SingleChannelTypes.Skip(3).Take(3).ToList();
                case MTEP.TangentValueType.拡縮:
                    return SingleChannelTypes.Skip(6).Take(3).ToList();
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
                return channel.values[0].value;
            }
            if (frameNo >= channel.frameNos[count - 1])
            {
                return channel.values[count - 1].value;
            }

            for (var i = 0; i < count - 1; i++)
            {
                if (frameNo <= channel.frameNos[i + 1])
                {
                    return EvaluateSegment(
                        channel.frameNos[i], channel.values[i],
                        channel.frameNos[i + 1], channel.values[i + 1],
                        channel.keyBones[i + 1],
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
            MTEP.BoneData boneB,   // easing はキー B (区間終端) 側が持つ
            float f)
        {
            var dtFrames = frameB - frameA;
            if (dtFrames <= 0) return a.value;
            var t = (f - frameA) / dtFrames;

            if (boneB.transform.hasEasing)
            {
                // Phase A 時点: easing レイヤーは easing 関数で描画する
                var e = MTEP.EasingFunctions.MoveEasing(t, (MTEP.MoveEasingType)boneB.transform.easing);
                return a.value + (b.value - a.value) * e;
            }

            // 再生と同一形状にするため t0/t1 は秒単位で渡す (単位系の原則を参照)
            var frameDuration = timeline.frameDuration;
            return MTEP.PluginUtils.Hermite(
                frameA * frameDuration, frameB * frameDuration,
                a.value, b.value,
                a.outTangent.value, b.inTangent.value, t);
        }

        /// <summary>縦軸の目盛りラベル (上端・中央・下端)</summary>
        private void DrawValueScale(GUIView view, Rect paneRect)
        {
            var labels = new[]
            {
                new KeyValuePair<float, float>(0f, _mapping.valueMax),
                new KeyValuePair<float, float>(paneRect.height * 0.5f, (_mapping.valueMin + _mapping.valueMax) * 0.5f),
                new KeyValuePair<float, float>(paneRect.height - 14f, _mapping.valueMin),
            };

            foreach (var label in labels)
            {
                view.currentPos = new Vector2(paneRect.x + 2, paneRect.y + label.Key);
                view.DrawLabel(label.Value.ToString("F2"), 60, 14, new Color(1f, 1f, 1f, 0.5f));
            }
        }

        /// <summary>サンプル値を 2px 幅の矩形セグメントで折れ線描画する</summary>
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
                // セグメント間の縦ギャップを埋めるため最低 SAMPLE_STEP の高さを与える
                view.DrawTexture(
                    GUIView.texWhite,
                    SAMPLE_STEP,
                    Mathf.Min(bottom - top + SAMPLE_STEP, paneRect.height - top),
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

                var y = _mapping.ValueToY(channel.values[i].value);
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
    }
}
