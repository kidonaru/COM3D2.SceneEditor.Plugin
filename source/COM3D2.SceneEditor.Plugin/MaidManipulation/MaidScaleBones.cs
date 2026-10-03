using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>メイドスケールで拡縮できる骨 1 本分 (骨名とメニュー表示名)</summary>
    public class MaidScaleBone
    {
        public readonly string boneName;
        public readonly string displayName;

        public MaidScaleBone(string boneName, string displayName)
        {
            this.boneName = boneName;
            this.displayName = displayName;
        }
    }

    /// <summary>
    /// メイドスケール (嘘パース用の腕の拡縮) の対象骨と倍率の範囲。
    /// 並びはタイムラインのメニューと履歴スナップショットの並びを兼ねる
    /// </summary>
    public static class MaidScaleBones
    {
        public const float MinScale = 0.1f;
        public const float MaxScale = 3f;
        public const float DefaultScale = 1f;

        public static readonly List<MaidScaleBone> bones = new List<MaidScaleBone>
        {
            new MaidScaleBone("Bip01 L UpperArm", "左上腕"),
            new MaidScaleBone("Bip01 L Forearm", "左前腕"),
            new MaidScaleBone("Bip01 L Hand", "左手"),
            new MaidScaleBone("Bip01 R UpperArm", "右上腕"),
            new MaidScaleBone("Bip01 R Forearm", "右前腕"),
            new MaidScaleBone("Bip01 R Hand", "右手"),
        };

        /// <summary>骨名から対象骨を引く。対象外なら null</summary>
        public static MaidScaleBone Find(string boneName)
        {
            if (string.IsNullOrEmpty(boneName))
            {
                return null;
            }
            foreach (var bone in bones)
            {
                if (bone.boneName == boneName)
                {
                    return bone;
                }
            }
            return null;
        }

        /// <summary>範囲へ丸める。NaN (手で書き換えたプリセットなど) は元の大きさにする</summary>
        public static float Clamp(float scale)
        {
            if (float.IsNaN(scale))
            {
                return DefaultScale;
            }
            return Mathf.Clamp(scale, MinScale, MaxScale);
        }

        /// <summary>範囲へ丸めた倍率が元の大きさ (1) か。状態を持つか・キーを適用するかの判定をそろえる</summary>
        public static bool IsDefault(float scale)
        {
            return Mathf.Approximately(Clamp(scale), DefaultScale);
        }
    }
}
