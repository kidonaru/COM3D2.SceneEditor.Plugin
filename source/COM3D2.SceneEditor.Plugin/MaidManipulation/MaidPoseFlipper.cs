using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 現在のポーズを左右反転する。
    /// 反転規則はタイムラインの反転ペースト (MTEP.PoseFlipUtils) と共通で、
    /// キーフレームではなく実ボーンの localRotation / localPosition へ直接書き戻す。
    /// 対象はタイムラインが保存するボーン (BoneUtils.saveBoneNames) のみで、
    /// 髪やスカート等の拡張ボーンは左右の対応が定義されていないため触らない
    /// </summary>
    public static class MaidPoseFlipper
    {
        /// <summary>反転後の書き戻し内容。読み取りを済ませてからまとめて適用する</summary>
        private struct FlippedBone
        {
            public Transform bone;
            public Quaternion rotation;

            /// <summary>位置も反転するボーン (中心ボーンのみ) か</summary>
            public bool hasPosition;
            public Vector3 position;
        }

        /// <summary>
        /// ポーズを左右反転する。再生中のモーションは呼び出し側で停止しておくこと
        /// (再生したままだと書き戻した値が翌フレームに上書きされる)
        /// </summary>
        public static void Flip(Maid maid)
        {
            var bones = ResolveBones(maid);
            if (bones.Count == 0)
            {
                MTEUtils.LogError("ボーンデータが取得できませんでした");
                return;
            }

            // 左右のボーンを入れ替えるため、全ボーンを読み終えてから書き戻す。
            // 読みながら書くと、先に書き換えた側の値を反対側の元値として拾ってしまう。
            // ループは左右ペアの両方から回るため、書き戻しは 1 ボーンにつき 1 件になる
            var flippedBones = new List<FlippedBone>(bones.Count);

            // 反転先が見つからなかった件数。片側だけ反転した崩れたポーズになるため警告する
            var missingCount = 0;

            foreach (var pair in bones)
            {
                var boneType = BoneUtils.GetBoneTypeByName(pair.Key);
                if (MTEP.PoseFlipUtils.IsNotFlipType(boneType))
                {
                    continue;
                }

                // 未知のボーン名は GetBoneTypeByName が TopFixed を返し、
                // IsNotFlipType 側で弾かれるのでここへは来ない
                var flippedType = MTEP.PoseFlipUtils.GetFlippedBoneType(boneType);

                // CRC ボディ差異等で反転先のボーンが無いことがある
                Transform destBone;
                if (!bones.TryGetValue(BoneUtils.GetBoneName(flippedType), out destBone))
                {
                    missingCount++;
                    continue;
                }

                var sourceBone = pair.Value;
                var flippedBone = new FlippedBone
                {
                    bone = destBone,
                    rotation = FlipRotation(flippedType, sourceBone.localRotation),
                };

                // 中心ボーンだけは左右の移動量も鏡像にする
                if (flippedType == IKManager.BoneType.Root)
                {
                    var localPosition = sourceBone.localPosition;
                    localPosition.x = -localPosition.x;
                    flippedBone.hasPosition = true;
                    flippedBone.position = localPosition;
                }

                flippedBones.Add(flippedBone);
            }

            foreach (var flippedBone in flippedBones)
            {
                flippedBone.bone.localRotation = flippedBone.rotation;
                if (flippedBone.hasPosition)
                {
                    flippedBone.bone.localPosition = flippedBone.position;
                }
            }

            if (missingCount > 0)
            {
                MTEUtils.LogWarning("反転先のボーンが見つかりませんでした: {0} 件", missingCount);
            }

            // 書き戻した後のポーズをボーンスライダーの基準にする
            // (基準が古いままだとスライダーが反転前との差分を表示・適用してしまう)
            MaidBoneSliderController.CaptureBasePose(maid);
        }

        /// <summary>
        /// 回転を反転する。FrameData.Flip と同じく、例外規則を持たないボーンは
        /// オイラー角を経由せずクォータニオンの鏡像で反転する
        /// (等価性とそうする理由は PoseFlipUtils.FlipRotation を参照)
        /// </summary>
        private static Quaternion FlipRotation(IKManager.BoneType flippedBoneType, Quaternion rotation)
        {
            if (!MTEP.PoseFlipUtils.HasEulerFlipRule(flippedBoneType))
            {
                return MTEP.PoseFlipUtils.FlipRotation(rotation);
            }

            var eulerAngles = MTEP.PoseFlipUtils.FlipEulerAngles(flippedBoneType, rotation.eulerAngles);
            return Quaternion.Euler(eulerAngles);
        }

        /// <summary>
        /// 反転対象のボーンを名前で引けるようにする。
        /// タイムラインと同じくパス経由で引き、同名ボーンの取り違えを避ける
        /// </summary>
        private static Dictionary<string, Transform> ResolveBones(Maid maid)
        {
            var boneNames = BoneUtils.saveBoneNames;
            var bones = new Dictionary<string, Transform>(boneNames.Count);
            if (maid == null || maid.body0 == null)
            {
                return bones;
            }

            var cacheBoneData = MaidPoseFileManager.GetOrCreateCacheBoneData(maid);
            foreach (var boneName in boneNames)
            {
                var boneData = cacheBoneData.GetBoneData(BoneUtils.ConvertToBonePath(boneName));
                if (boneData != null && boneData.transform != null)
                {
                    bones[boneName] = boneData.transform;
                }
            }
            return bones;
        }
    }
}
