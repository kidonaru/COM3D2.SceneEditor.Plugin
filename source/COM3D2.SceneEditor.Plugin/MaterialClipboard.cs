using System.Collections.Generic;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// マテリアル設定 (シェーダー / 色 / 数値プロパティ) のコピー & ペースト用クリップボード。
    /// 別マテリアルへ設定を写すためのもので、プロセス内でのみ保持する。
    /// 貼り付け先のシェーダーを先に揃え、それでも持たないプロパティは読み飛ばす
    /// </summary>
    public static class MaterialClipboard
    {
        private static readonly Dictionary<MTEP.ModelMaterial.ColorPropertyType, Color> _colors
            = new Dictionary<MTEP.ModelMaterial.ColorPropertyType, Color>();

        private static readonly Dictionary<MTEP.ModelMaterial.ValuePropertyType, float> _values
            = new Dictionary<MTEP.ModelMaterial.ValuePropertyType, float>();

        private static Shader _shader;

        public static bool hasData => _shader != null || _colors.Count > 0 || _values.Count > 0;

        public static void Copy(MTEP.ModelMaterial material)
        {
            _colors.Clear();
            _values.Clear();
            _shader = material.material != null ? material.material.shader : null;

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

        /// <returns>色・数値を 1 つでも適用したら true (シェーダーの差し替えは数えない)</returns>
        public static bool Paste(MTEP.ModelMaterial material)
        {
            var applied = false;

            // 貼り付け元のシェーダーにしか無いプロパティへ書けるよう、先にシェーダーを揃える。
            // シェーダーは追跡の対象外なので applied (= 呼び出し側の追跡チェック) には数えない
            if (_shader != null && material.material != null && material.material.shader != _shader)
            {
                material.ChangeShader(_shader);
            }

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
