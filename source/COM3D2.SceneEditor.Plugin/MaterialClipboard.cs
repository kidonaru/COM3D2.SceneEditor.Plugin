using System.Collections.Generic;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// マテリアル設定 (色 / 数値プロパティ) のコピー & ペースト用クリップボード。
    /// 別マテリアルへ設定を写すためのもので、プロセス内でのみ保持する。
    /// 貼り付け先が持たないプロパティは読み飛ばし、シェーダ違いでも壊れないようにする
    /// </summary>
    public static class MaterialClipboard
    {
        private static readonly Dictionary<MTEP.ModelMaterial.ColorPropertyType, Color> _colors
            = new Dictionary<MTEP.ModelMaterial.ColorPropertyType, Color>();

        private static readonly Dictionary<MTEP.ModelMaterial.ValuePropertyType, float> _values
            = new Dictionary<MTEP.ModelMaterial.ValuePropertyType, float>();

        public static bool hasData => _colors.Count > 0 || _values.Count > 0;

        public static void Copy(MTEP.ModelMaterial material)
        {
            _colors.Clear();
            _values.Clear();

            foreach (var type in MTEP.ModelMaterial.ColorPropertyTypes)
            {
                if (material.HasColor(type))
                {
                    _colors[type] = material.GetColor(type);
                }
            }

            foreach (var type in MTEP.ModelMaterial.ValuePropertyTypes)
            {
                if (material.HasValue(type))
                {
                    _values[type] = material.GetValue(type);
                }
            }
        }

        /// <returns>1 つでも適用したら true</returns>
        public static bool Paste(MTEP.ModelMaterial material)
        {
            var applied = false;

            foreach (var pair in _colors)
            {
                if (material.HasColor(pair.Key))
                {
                    material.SetColor(pair.Key, pair.Value);
                    applied = true;
                }
            }

            foreach (var pair in _values)
            {
                if (material.HasValue(pair.Key))
                {
                    material.SetValue(pair.Key, pair.Value);
                    applied = true;
                }
            }

            return applied;
        }
    }
}
