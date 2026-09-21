using System;
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// モーションウィンドウのアニメブレンド区間。適用先レイヤーの選択と、
    /// 載っている層ごとの再生時間 / 重み / 速度 / ループ / 削除の行を描く。
    /// 値の書き込みは全て MaidAnimationBlendController 経由で、履歴は HistoryScope.Pose に積む
    /// </summary>
    public static class MaidPoseBlendRows
    {
        /// <summary>適用先コンボの先頭は「通常」(レイヤー 0 = 従来経路)</summary>
        public const int BaseLayer = 0;

        /// <summary>
        /// 適用先コンボ。GUIComboBox の生成は GUIView の静的初期化 (Unity ネイティブ) を
        /// 引くため、描画時まで遅らせて GetVisibleLayers を pure に保つ
        /// </summary>
        private static GUIComboBox<int> _targetComboBox;

        private static List<int> _targetItems;

        /// <summary>
        /// 描く層の番号。名前が入っている層と、空でも適用先に選ばれている層を昇順で返す
        /// (全 7 段を常に並べると 300px 幅のウィンドウでは一覧が押し出されるため)
        /// </summary>
        public static List<int> GetVisibleLayers(IList<string> anmNamesByLayer, int targetLayer, int minLayer, int maxLayer)
        {
            var result = new List<int>();
            for (var layer = minLayer; layer <= maxLayer; layer++)
            {
                var name = layer < anmNamesByLayer.Count ? anmNamesByLayer[layer] : null;
                if (!string.IsNullOrEmpty(name) || layer == targetLayer)
                {
                    result.Add(layer);
                }
            }
            return result;
        }

        public static void Draw(GUIView view, Maid maid, int targetLayer, Action<int> setTargetLayer,
            float rowHeight, float labelWidth)
        {
            var infos = MaidAnimationBlendController.GetLayerInfos(maid);
            if (infos == null)
            {
                view.DrawLabel("アニメレイヤーの情報がありません", -1, rowHeight, textColor: Color.gray);
                return;
            }

            if (_targetComboBox == null)
            {
                _targetComboBox = new GUIComboBox<int>
                {
                    getName = (layer, _) => layer == BaseLayer ? "通常" : "レイヤー" + layer,
                    buttonSize = new Vector2(110, 20),
                    contentSize = new Vector2(110, 200),
                };
            }

            if (_targetItems == null)
            {
                _targetItems = new List<int> { BaseLayer };
                for (var layer = MaidAnimationBlendController.MinLayer; layer <= MaidAnimationBlendController.MaxLayer; layer++)
                {
                    _targetItems.Add(layer);
                }
            }

            view.BeginHorizontal();
            {
                view.DrawLabel("適用先", labelWidth, rowHeight, style: GUIView.gsLabelRight);
                _targetComboBox.items = _targetItems;
                _targetComboBox.currentIndex = Mathf.Max(0, _targetItems.IndexOf(targetLayer));
                _targetComboBox.onSelected = (layer, _) => setTargetLayer(layer);
                _targetComboBox.DrawButton(view);
            }
            view.EndLayout();

            var names = new List<string>(infos.Count);
            foreach (var info in infos)
            {
                names.Add(info.anmName);
            }

            foreach (var layer in GetVisibleLayers(names, targetLayer,
                MaidAnimationBlendController.MinLayer, MaidAnimationBlendController.MaxLayer))
            {
                DrawLayer(view, maid, infos[layer], rowHeight, labelWidth);
            }
        }

        private static void DrawLayer(GUIView view, Maid maid, AnimationLayerInfo info,
            float rowHeight, float labelWidth)
        {
            var layer = info.layer;
            var hasState = info.state != null && !string.IsNullOrEmpty(info.anmName);
            var length = hasState ? Mathf.Max(info.state.length, 0.01f) : 1f;

            view.DrawHorizontalLine(Color.gray);

            view.BeginHorizontal();
            {
                var title = "レイヤー" + layer + ": " + (hasState
                    ? System.IO.Path.GetFileNameWithoutExtension(info.anmName)
                    : "(未設定)");
                view.DrawLabel(title, -1, rowHeight);

                view.AddRightAlignSpace(30 + 50 + view.margin, rowHeight);

                var playing = MaidAnimationBlendController.IsLayerPlaying(maid, layer);
                // 層の ▶ はベース再生中しか効かない (停止編集を崩さない)
                if (view.DrawButton(playing ? "■" : "▶", 30, rowHeight,
                    enabled: hasState && (playing || MaidMotionState.IsPlaying(maid))))
                {
                    if (playing)
                    {
                        MaidAnimationBlendController.Stop(maid, layer);
                    }
                    else
                    {
                        MaidAnimationBlendController.Play(maid, layer);
                    }
                }

                if (view.DrawButton("削除", 50, rowHeight, enabled: hasState))
                {
                    AutoEditMode.Enter();
                    HistoryManager.instance.BeforeEdit(maid, HistoryScope.Pose,
                        "ブレンド削除: レイヤー" + layer, () => PoseSnapshot.GetAllBodyBones(maid));
                    MaidAnimationBlendController.RemoveLayer(maid, layer);
                }
            }
            view.EndLayout();

            if (!hasState)
            {
                return;
            }

            view.BeginAutoEditMode();

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "再生時間",
                labelWidth = labelWidth,
                width = -1,
                fieldType = FloatFieldType.Float,
                min = 0f,
                max = length,
                step = 0.01f,
                defaultValue = 0f,
                value = info.state.GetPlayingTime(),
                hiddenResetButton = true,
                onChanged = value =>
                {
                    HistoryManager.instance.BeforeEdit(maid, HistoryScope.Pose,
                        "ブレンド再生時間", () => PoseSnapshot.GetAllBodyBones(maid));
                    MaidAnimationBlendController.SetTime(maid, layer, value);
                },
            });

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "重み",
                labelWidth = labelWidth,
                width = -1,
                fieldType = FloatFieldType.Float,
                min = 0f,
                max = 1f,
                step = 0.01f,
                defaultValue = 1f,
                value = info.weight,
                onChanged = value =>
                {
                    HistoryManager.instance.BeforeEdit(maid, HistoryScope.Pose,
                        "ブレンド重み", () => PoseSnapshot.GetAllBodyBones(maid));
                    MaidAnimationBlendController.SetWeight(maid, layer, value);
                },
            });

            view.BeginHorizontal();
            {
                view.DrawSliderValue(new GUIView.SliderOption
                {
                    label = "速度",
                    labelWidth = labelWidth,
                    width = view.viewRect.width - 70 - view.margin,
                    fieldType = FloatFieldType.Float,
                    min = 0f,
                    max = 2f,
                    step = 0.01f,
                    defaultValue = 1f,
                    value = info.speed,
                    onChanged = value =>
                    {
                        HistoryManager.instance.BeforeEdit(maid, HistoryScope.Pose,
                            "ブレンド速度", () => PoseSnapshot.GetAllBodyBones(maid));
                        MaidAnimationBlendController.SetSpeed(maid, layer, value);
                    },
                });

                view.DrawToggle("ループ", info.loop, 70, rowHeight, value =>
                {
                    HistoryManager.instance.BeforeEdit(maid, HistoryScope.Pose,
                        "ブレンドループ", () => PoseSnapshot.GetAllBodyBones(maid));
                    MaidAnimationBlendController.SetLoop(maid, layer, value);
                });
            }
            view.EndLayout();

            view.EndAutoEditMode();
        }
    }
}
