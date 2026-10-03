using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// メイド 1 体分のメイドスケール倍率。未設定の骨は 1 (元の大きさ)。
    /// 倍率 1 は保持しないので、全骨が元の大きさなら isDefault になる
    /// </summary>
    public class MaidScaleState
    {
        private readonly Dictionary<string, float> _scales = new Dictionary<string, float>();

        public bool isDefault => _scales.Count == 0;

        /// <summary>倍率が 1 でない骨だけ</summary>
        public IEnumerable<KeyValuePair<string, float>> nonDefaultScales => _scales;

        public float Get(string boneName)
        {
            float value;
            if (boneName == null || !_scales.TryGetValue(boneName, out value))
            {
                return MaidScaleBones.DefaultScale;
            }
            return value;
        }

        /// <summary>範囲へ丸めて保持する。対象外の骨名は無視する</summary>
        public void Set(string boneName, float scale)
        {
            if (MaidScaleBones.Find(boneName) == null)
            {
                return;
            }

            if (MaidScaleBones.IsDefault(scale))
            {
                _scales.Remove(boneName);
                return;
            }
            _scales[boneName] = MaidScaleBones.Clamp(scale);
        }

        /// <summary>
        /// 退避値へ戻してよいか。今の値が自分の書いた値のままのときだけ戻す。
        /// 体型スライダー (BoneMorph_.Blend) などが途中で書き直した値を古い退避値で潰さないため。
        /// Vector3 の == は近似比較 (差の 2 乗が 1e-10 未満) なので、それより小さな書き直しは区別しない
        /// </summary>
        public static bool ShouldRestore(Vector3 current, Vector3 written)
        {
            return current == written;
        }
    }
}
