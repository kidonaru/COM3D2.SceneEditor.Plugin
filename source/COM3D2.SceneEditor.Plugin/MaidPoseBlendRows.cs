using System;
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// モーションウィンドウの適用先タブと、レイヤーを選んでいるときの値の行
    /// (再生時間 / 重み / 速度 / ループ / 時間上書き)。
    /// 名前・前後送り・再生・削除の行は送り先一覧の状態を持つ MaidPoseWindow 側が描く。
    /// 値の書き込みは全て MaidAnimationBlendController 経由で、履歴は HistoryScope.Pose に積む
    /// </summary>
    public static class MaidPoseBlendRows
    {
        /// <summary>適用先タブの先頭は「ベース」(レイヤー 0 = 従来のベースモーション)</summary>
        public const int BaseLayer = 0;

        /// <summary>適用先タブ 1 つぶんの幅と間隔。「ベース」が収まる幅に合わせる</summary>
        private const float TAB_WIDTH = 44f;
        private const float TAB_MARGIN = 2f;

        /// <summary>載っている層の目印。番号だけだとどこに何があるか分からないため添える</summary>
        private const string LOADED_MARK = "*";

        // 描画は IMGUI で 1 フレームに複数回走るため、作業用リストは使い回す
        // (一覧側の _motions 等と同じくガベージを出さない方針に揃える)
        private static readonly List<string> _nameBuffer = new List<string>();
        private static readonly List<string> _labelBuffer = new List<string>();

        /// <summary>
        /// 適用先タブの見出し。先頭が「ベース」で、以降がレイヤー番号。
        /// アニメが載っている層には目印を付ける
        /// (選択中の層しか中身を出さないので、タブ側で載り具合が分かるようにする)
        /// </summary>
        public static List<string> BuildTargetLabels(
            IList<string> anmNamesByLayer, int minLayer, int maxLayer)
        {
            return BuildTargetLabels(anmNamesByLayer, minLayer, maxLayer, new List<string>());
        }

        /// <summary>見出しの書き出し先を渡す版。描画からは使い回しのバッファを渡す</summary>
        private static List<string> BuildTargetLabels(
            IList<string> anmNamesByLayer, int minLayer, int maxLayer, List<string> result)
        {
            result.Clear();
            result.Add("ベース");
            for (var layer = minLayer; layer <= maxLayer; layer++)
            {
                var name = layer < anmNamesByLayer.Count ? anmNamesByLayer[layer] : null;
                result.Add(string.IsNullOrEmpty(name)
                    ? layer.ToString()
                    : layer + LOADED_MARK);
            }
            return result;
        }

        /// <summary>適用先タブの並び順 (index) とレイヤー番号の相互変換</summary>
        public static int ToTabIndex(int targetLayer, int minLayer)
        {
            return targetLayer == BaseLayer ? 0 : targetLayer - minLayer + 1;
        }

        public static int ToTargetLayer(int tabIndex, int minLayer)
        {
            return tabIndex == 0 ? BaseLayer : tabIndex - 1 + minLayer;
        }

        /// <summary>
        /// 適用先タブを描く。レイヤー情報が取れないときは false を返すので、
        /// 呼び出し側はベース扱いで続けること
        /// </summary>
        public static bool DrawTargetTabs(GUIView view, Maid maid, int targetLayer,
            Action<int> setTargetLayer, float rowHeight, float labelWidth)
        {
            var infos = MaidAnimationBlendController.GetLayerInfos(maid);
            if (infos == null)
            {
                return false;
            }

            var minLayer = MaidAnimationBlendController.MinLayer;

            _nameBuffer.Clear();
            foreach (var info in infos)
            {
                _nameBuffer.Add(info.anmName);
            }

            view.BeginHorizontal();
            {
                view.DrawLabel("適用先", labelWidth, rowHeight, style: GUIView.gsLabelRight);

                var labels = BuildTargetLabels(
                    _nameBuffer, minLayer, MaidAnimationBlendController.MaxLayer, _labelBuffer);
                var currentIndex = Mathf.Clamp(
                    ToTabIndex(targetLayer, minLayer), 0, labels.Count - 1);
                var newIndex = view.DrawTabs(labels, currentIndex, TAB_WIDTH, rowHeight, TAB_MARGIN);
                if (newIndex != currentIndex)
                {
                    setTargetLayer(ToTargetLayer(newIndex, minLayer));
                }
            }
            view.EndLayout();
            return true;
        }

        /// <summary>
        /// レイヤーの値の行。アニメが載っていない段では何も描かない
        /// (名前と 削除 の行は呼び出し側が描く)
        /// </summary>
        public static void DrawLayerValues(GUIView view, Maid maid, AnimationLayerInfo info,
            float rowHeight, float labelWidth)
        {
            if (info == null || info.state == null || string.IsNullOrEmpty(info.anmName))
            {
                return;
            }

            var layer = info.layer;
            var length = Mathf.Max(info.state.length, 0.01f);

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

            // 2 つ並べるぶん、スライダーは行幅を等分する
            var halfWidth = (view.viewRect.width - view.padding.x * 2 - view.margin) * 0.5f;

            view.BeginHorizontal();
            {
                view.DrawSliderValue(new GUIView.SliderOption
                {
                    label = "重み",
                    labelWidth = labelWidth,
                    width = halfWidth,
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

                view.DrawSliderValue(new GUIView.SliderOption
                {
                    label = "速度",
                    labelWidth = labelWidth,
                    width = halfWidth,
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
            }
            view.EndLayout();

            view.BeginHorizontal();
            {
                view.DrawToggle("ループ", info.loop, halfWidth, rowHeight, value =>
                {
                    HistoryManager.instance.BeforeEdit(maid, HistoryScope.Pose,
                        "ブレンドループ", () => PoseSnapshot.GetAllBodyBones(maid));
                    MaidAnimationBlendController.SetLoop(maid, layer, value);
                });

                // タイムライン再生時だけ効く値なので、実 AnimationState は触らない
                view.DrawToggle("時間上書き", info.overrideTime, halfWidth, rowHeight, value =>
                {
                    HistoryManager.instance.BeforeEdit(maid, HistoryScope.Pose,
                        "ブレンド時間上書き", () => PoseSnapshot.GetAllBodyBones(maid));
                    MaidAnimationBlendController.SetOverrideTime(maid, layer, value);
                });
            }
            view.EndLayout();

            view.EndAutoEditMode();
        }

    }
}
