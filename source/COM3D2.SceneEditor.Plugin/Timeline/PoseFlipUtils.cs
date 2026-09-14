using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// ポーズの左右反転規則。
    /// 既定はクォータニオンの鏡像（<see cref="FlipRotation"/>）で反転するが、
    /// 鏡像では表せない手書きのオイラー角規則を持つ種別（<see cref="HasEulerFlipRule"/>）だけは
    /// <see cref="FlipEulerAngles"/> を通す
    /// （経緯は docs/timeline-rotation-quaternion-survey.md の 5-2 を参照）
    /// </summary>
    public static class PoseFlipUtils
    {
        private static bool _initialized = false;
        private static HashSet<IKManager.BoneType> _notFlipTypes = null;
        private static Dictionary<IKManager.BoneType, IKManager.BoneType> _swapFlipDic = null;

        private static void Initialize()
        {
            if (_initialized)
            {
                return;
            }
            _initialized = true;

            MTEUtils.LogDebug("ボーンタイプの初期化");

            _notFlipTypes = new HashSet<IKManager.BoneType>
            {
                IKManager.BoneType.TopFixed,
            };
            for (int i = (int) IKManager.BoneType.Mouth; i <= (int) IKManager.BoneType.Nipple_R; i++)
            {
                _notFlipTypes.Add((IKManager.BoneType)i);
            }

            var leftFingerTypes = new List<IKManager.BoneType>(16);
            var rightFingerTypes = new List<IKManager.BoneType>(16);
            var leftToeTypes = new List<IKManager.BoneType>(6);
            var rightToeTypes = new List<IKManager.BoneType>(6);

            for (int i = (int) IKManager.BoneType.Finger0_Root_L; i <= (int) IKManager.BoneType.Finger4_1_L; i++)
            {
                leftFingerTypes.Add((IKManager.BoneType)i);
            }

            for (int i = (int) IKManager.BoneType.Finger0_Root_R; i <= (int) IKManager.BoneType.Finger4_1_R; i++)
            {
                rightFingerTypes.Add((IKManager.BoneType)i);
            }

            for (int i = (int) IKManager.BoneType.Toe0_Root_L; i <= (int) IKManager.BoneType.Toe2_0_L; i++)
            {
                leftToeTypes.Add((IKManager.BoneType)i);
            }

            for (int i = (int) IKManager.BoneType.Toe0_Root_R; i <= (int) IKManager.BoneType.Toe2_0_R; i++)
            {
                rightToeTypes.Add((IKManager.BoneType)i);
            }

            _swapFlipDic = new Dictionary<IKManager.BoneType, IKManager.BoneType>
            {
                { IKManager.BoneType.Clavicle_R, IKManager.BoneType.Clavicle_L },
                { IKManager.BoneType.UpperArm_R, IKManager.BoneType.UpperArm_L },
                { IKManager.BoneType.Forearm_R, IKManager.BoneType.Forearm_L },
                { IKManager.BoneType.Thigh_R, IKManager.BoneType.Thigh_L },
                { IKManager.BoneType.Calf_R, IKManager.BoneType.Calf_L },
                { IKManager.BoneType.Hand_R, IKManager.BoneType.Hand_L },
                { IKManager.BoneType.Foot_R, IKManager.BoneType.Foot_L },
                { IKManager.BoneType.Bust_L, IKManager.BoneType.Bust_R },
            };

            var swapList = _swapFlipDic.ToList();
            foreach (var pair in swapList)
            {
                _swapFlipDic.Add(pair.Value, pair.Key);
            }

            for (int i = 0; i < leftFingerTypes.Count; i++)
            {
                _swapFlipDic.Add(leftFingerTypes[i], rightFingerTypes[i]);
                _swapFlipDic.Add(rightFingerTypes[i], leftFingerTypes[i]);
            }

            for (int i = 0; i < leftToeTypes.Count; i++)
            {
                _swapFlipDic.Add(leftToeTypes[i], rightToeTypes[i]);
                _swapFlipDic.Add(rightToeTypes[i], leftToeTypes[i]);
            }
        }

        /// <summary>反転対象外のボーン種別か</summary>
        public static bool IsNotFlipType(IKManager.BoneType boneType)
        {
            Initialize();
            return _notFlipTypes.Contains(boneType);
        }

        /// <summary>左右を入れ替えたボーン種別を返す。対応が無ければそのまま返す</summary>
        public static IKManager.BoneType GetFlippedBoneType(IKManager.BoneType boneType)
        {
            Initialize();

            IKManager.BoneType flipped;
            if (_swapFlipDic.TryGetValue(boneType, out flipped))
            {
                return flipped;
            }
            return boneType;
        }

        /// <summary>
        /// ボーン種別ごとの例外規則（クォータニオン鏡像では表せない手書きの規則）を持つか。
        /// 持たない種別は <see cref="FlipRotation"/> で反転できる
        /// </summary>
        public static bool HasEulerFlipRule(IKManager.BoneType boneType)
        {
            return boneType == IKManager.BoneType.Root ||
                   boneType == IKManager.BoneType.Pelvis ||
                   boneType == IKManager.BoneType.Spine0 ||
                   boneType == IKManager.BoneType.Bust_L ||
                   boneType == IKManager.BoneType.Bust_R;
        }

        /// <summary>
        /// ボーン種別ごとの反転規則をオイラー角へ適用する。
        /// <paramref name="flippedBoneType"/> は <see cref="GetFlippedBoneType"/> を通した後の種別を渡すこと
        /// </summary>
        public static Vector3 FlipEulerAngles(IKManager.BoneType flippedBoneType, Vector3 eulerAngles)
        {
            var newEulerAngles = eulerAngles;

            if (flippedBoneType == IKManager.BoneType.Root)
            {
                newEulerAngles.y = 180f - (eulerAngles.y - 180f);
                newEulerAngles.z = 270f - (eulerAngles.z - 270f);
            }
            else if (flippedBoneType == IKManager.BoneType.Pelvis)
            {
                newEulerAngles.y = eulerAngles.y + 180f;
                newEulerAngles.z = eulerAngles.z + 180f;
            }
            else if (flippedBoneType == IKManager.BoneType.Spine0)
            {
                newEulerAngles.x = 270f - (eulerAngles.x - 270f);
            }
            else if (flippedBoneType == IKManager.BoneType.Bust_L ||
                     flippedBoneType == IKManager.BoneType.Bust_R)
            {
                newEulerAngles.y = 360f - (eulerAngles.y - 180f);
                newEulerAngles.z = 270f - (eulerAngles.z - 270f);
            }
            else
            {
                newEulerAngles.x = -eulerAngles.x;
                newEulerAngles.y = -eulerAngles.y;
            }

            return newEulerAngles;
        }

        /// <summary>
        /// ローカル XY 平面での鏡像回転（法線 (0,0,1) の鏡像共役 M·R·M）を返す。
        /// <see cref="FlipEulerAngles"/> の「その他」規則（X と Y を符号反転し Z は据え置き）と
        /// オイラー角上で厳密に等価だが、往復変換が無いぶんジンバルロック近傍でも表現が飛ばない
        /// </summary>
        public static Quaternion FlipRotation(Quaternion rotation)
        {
            return new Quaternion(-rotation.x, -rotation.y, rotation.z, rotation.w);
        }
    }
}
