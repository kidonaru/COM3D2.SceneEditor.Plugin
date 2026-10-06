using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// ノード表示キー 1 件分。項目名は body のボーン名 (MaidNodeVisibilityNodes) で、表示フラグを持つ。
    /// 補間せず区間開始時に適用する
    /// </summary>
    public class TransformDataNodeVisibility : TransformDataBase
    {
        public enum Index
        {
            IsVisible = 0,
        }

        public override TransformType type => TransformType.NodeVisibility;

        public override int valueCount => 1;

        public TransformDataNodeVisibility()
        {
        }

        public override void Initialize(string name)
        {
            base.Initialize(name);

            // 基底の Initialize は値を 0（非表示）で作るため、表示を既定にする。
            // XML からの読み込みは Initialize 後に値を上書きするため影響しない
            isVisible = true;
        }

        private readonly static Dictionary<string, CustomValueInfo> CustomValueInfoMap = new Dictionary<string, CustomValueInfo>
        {
            {
                "isVisible",
                new CustomValueInfo
                {
                    index = (int)Index.IsVisible,
                    name = "表示",
                    min = 0f,
                    max = 1f,
                    step = 1f,
                    defaultValue = 1f,
                }
            },
        };

        public override Dictionary<string, CustomValueInfo> GetCustomValueInfoMap()
        {
            return CustomValueInfoMap;
        }

        private static readonly LaneColorInfo LaneInfo =
            LaneColorInfo.FromBool(LaneColorInfo.DefaultColor, (int)Index.IsVisible);

        public override LaneColorInfo GetLaneColorInfo()
        {
            return LaneInfo;
        }

        public ValueData isVisibleValue => values[(int)Index.IsVisible];

        public bool isVisible
        {
            get => isVisibleValue.boolValue;
            set => isVisibleValue.boolValue = value;
        }
    }
}
