using System.Collections.Generic;
using COM3D2.SceneEditor.Plugin;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// 体型スライダーキー 1 件分。項目名は BodySliderDefs のキーで、値は ModsParam のサブキーの並びの 3 個
    /// (スケールは width / depth / height、位置は x / y / z)。Tangent 補間する。
    /// 既定値と成分の範囲は項目ごとに違うため、カスタム値の定義は項目名から引く
    /// </summary>
    public class TransformDataBodySlider : TransformDataBase
    {
        public override TransformType type => TransformType.BodySlider;

        public override int valueCount => 3;

        public override bool hasTangent => true;

        public override ValueData[] tangentValues => values;

        private static readonly Dictionary<string, CustomValueInfo> EmptyInfoMap
            = new Dictionary<string, CustomValueInfo>();

        /// <summary>項目キー → カスタム値の定義。定義は不変なので作ったものを使い回す</summary>
        private static readonly Dictionary<string, Dictionary<string, CustomValueInfo>> InfoMapCache
            = new Dictionary<string, Dictionary<string, CustomValueInfo>>();

        public TransformDataBodySlider()
        {
        }

        public override void Initialize(string name)
        {
            base.Initialize(name);

            // 基底の Initialize は値を 0 で作るだけなので、項目の既定値 (スケールは 1) にする。
            // XML からの読み込みは Initialize 後に値を上書きするため影響しない
            var item = BodySliderDefs.Find(name);
            if (item != null)
            {
                vector = item.defaultValues;
            }
        }

        public override Dictionary<string, CustomValueInfo> GetCustomValueInfoMap()
        {
            var item = BodySliderDefs.Find(name);
            if (item == null)
            {
                return EmptyInfoMap;
            }

            Dictionary<string, CustomValueInfo> map;
            if (!InfoMapCache.TryGetValue(item.key, out map))
            {
                map = new Dictionary<string, CustomValueInfo>();
                for (var i = 0; i < item.components.Length; i++)
                {
                    var component = item.components[i];
                    map[component.name] = new CustomValueInfo
                    {
                        index = i,
                        name = component.label,
                        min = component.min,
                        max = component.max,
                        step = 0.01f,
                        defaultValue = item.defaultValue,
                    };
                }
                InfoMapCache[item.key] = map;
            }
            return map;
        }

        public Vector3 vector
        {
            get => new Vector3(values[0].value, values[1].value, values[2].value);
            set
            {
                values[0].value = value.x;
                values[1].value = value.y;
                values[2].value = value.z;
            }
        }

        /// <summary>既定値か。適用を省く判定に使う。未知の項目は適用しないので既定扱い</summary>
        public bool isDefault
        {
            get
            {
                var item = BodySliderDefs.Find(name);
                return item == null || item.IsDefault(vector);
            }
        }
    }
}
