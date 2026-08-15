using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// ボーンを駆動している揺れ物理コンポーネントの束。
    /// 階層探索とリフレクションを毎フレーム走らせないよう、探索結果を保持して使い回す
    /// </summary>
    public class SlotYureTargets
    {
        public TBodySkin slot;
        /// <summary>旧来の髪・旧スカート物理。チェーン単位の切替手段がないためコンポーネント単位で扱う</summary>
        public TBoneHair_ boneHair;
        public List<DynamicBone> dynamicBones = new List<DynamicBone>();
        public DynamicSkirtBone skirtBone;

        public bool isEmpty =>
            boneHair == null && dynamicBones.Count == 0 && skirtBone == null;
    }

    /// <summary>
    /// ボーン単位の揺れもの (物理) の ON/OFF。ボーン編集中に揺れで
    /// ポーズが動くのを止めるために使う。対象ボーンを駆動している
    /// TBoneHair_ / DynamicBone / DynamicSkirtBone だけを切り替える
    /// </summary>
    public static class SlotYureUtil
    {
        // ゲーム側の状態フィールドは private のためリフレクションで触る
        private static readonly FieldInfo HairListField =
            typeof(TBoneHair_).GetField("hair1list", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo HairSkirtListField =
            typeof(TBoneHair_).GetField("SkirtList", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo HairEnableField =
            typeof(TBoneHair_).GetField("m_bEnable", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo SkirtBoneField =
            typeof(BoneHair3).GetField("m_SkirtBone", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo SkirtBoneTrsField =
            typeof(DynamicSkirtBone).GetField("m_aryBoneTrs", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>
        /// ボーンを駆動している揺れ物理を探索する。関連する物理が無ければ null。
        /// 装着物が変わらない限り結果は不変なので、呼び出し側でキャッシュしてよい
        /// </summary>
        public static SlotYureTargets FindTargets(Maid maid, string slotName, Transform bone)
        {
            var slot = GetSlot(maid, slotName);
            if (slot == null || bone == null)
            {
                return null;
            }

            var targets = new SlotYureTargets
            {
                slot = slot,
                boneHair = IsBoneHairRelated(slot, bone) ? slot.bonehair : null,
                skirtBone = FindRelatedSkirtBone(slot, bone),
            };

            // DynamicBone は 1 コンポーネント = 1 チェーンで複数付きうる
            foreach (var dynamicBone in slot.obj.GetComponentsInChildren<DynamicBone>())
            {
                if (dynamicBone.m_Root != null && bone.IsChildOf(dynamicBone.m_Root))
                {
                    targets.dynamicBones.Add(dynamicBone);
                }
            }

            return targets.isEmpty ? null : targets;
        }

        /// <summary>揺れが有効か。関連する物理のいずれかが動いていれば true</summary>
        public static bool GetYureState(SlotYureTargets targets)
        {
            if (targets == null)
            {
                return false;
            }

            if (targets.boneHair != null && HairEnableField != null
                && (bool)HairEnableField.GetValue(targets.boneHair))
            {
                return true;
            }

            foreach (var dynamicBone in targets.dynamicBones)
            {
                if (dynamicBone != null && dynamicBone.enabled)
                {
                    return true;
                }
            }

            // スカート物理は BoneHair3 側の参照が駆動条件のため、
            // 見つけたコンポーネントが実際に参照されているかで判定する
            return targets.skirtBone != null
                && targets.slot.bonehair3 != null && SkirtBoneField != null
                && ReferenceEquals(SkirtBoneField.GetValue(targets.slot.bonehair3), targets.skirtBone);
        }

        /// <summary>関連する揺れ物理だけを有効・無効にする</summary>
        public static void SetYureState(SlotYureTargets targets, bool state)
        {
            if (targets == null)
            {
                return;
            }

            if (targets.boneHair != null && HairEnableField != null)
            {
                HairEnableField.SetValue(targets.boneHair, state);
            }

            foreach (var dynamicBone in targets.dynamicBones)
            {
                if (dynamicBone != null)
                {
                    SetDynamicBoneEnabled(dynamicBone, state);
                }
            }

            if (targets.skirtBone != null)
            {
                targets.skirtBone.enabled = state;
                // スカート物理は BoneHair3 経由で毎フレーム駆動されるため、参照側も切り替える
                if (targets.slot.bonehair3 != null && SkirtBoneField != null)
                {
                    SkirtBoneField.SetValue(targets.slot.bonehair3, state ? targets.skirtBone : null);
                }
            }
        }

        /// <summary>旧来の TBoneHair_ (髪・旧スカート) がこのボーンを駆動しているか</summary>
        private static bool IsBoneHairRelated(TBodySkin slot, Transform bone)
        {
            var bonehair = slot.bonehair;
            if (bonehair == null)
            {
                return false;
            }

            var hairList = HairListField != null
                ? HairListField.GetValue(bonehair) as List<THair1> : null;
            if (hairList != null)
            {
                foreach (var hair in hairList)
                {
                    if (IsChainRoot(hair, bone))
                    {
                        return true;
                    }
                }
            }

            var skirtList = HairSkirtListField != null
                ? HairSkirtListField.GetValue(bonehair) as THair1[] : null;
            if (skirtList != null)
            {
                foreach (var hair in skirtList)
                {
                    if (IsChainRoot(hair, bone))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>bone がチェーンルート (自身含む) の配下か</summary>
        private static bool IsChainRoot(THair1 hair, Transform bone)
        {
            return hair != null && hair.root != null && bone.IsChildOf(hair.root);
        }

        /// <summary>ボーンを駆動している DynamicSkirtBone。管理ボーン配列で判定する</summary>
        private static DynamicSkirtBone FindRelatedSkirtBone(TBodySkin slot, Transform bone)
        {
            if (SkirtBoneTrsField == null)
            {
                return null;
            }

            foreach (var skirtBone in slot.obj.GetComponentsInChildren<DynamicSkirtBone>())
            {
                var boneTrs = SkirtBoneTrsField.GetValue(skirtBone) as Transform[];
                if (boneTrs == null)
                {
                    continue;
                }

                foreach (var t in boneTrs)
                {
                    if (t != null && bone.IsChildOf(t))
                    {
                        return skirtBone;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// DynamicBone は無効化時にパーティクルの Transform を初期姿勢へ戻すため、
        /// 現在の姿勢を退避して切り替え後に復元する (ボーン編集値を消さないための措置)
        /// </summary>
        private static void SetDynamicBoneEnabled(DynamicBone dynamicBone, bool state)
        {
            var poses = new List<KeyValuePair<Transform, TransformPose>>();
            foreach (var particle in dynamicBone.m_Particles)
            {
                if (particle.m_Transform == null)
                {
                    continue;
                }
                poses.Add(new KeyValuePair<Transform, TransformPose>(
                    particle.m_Transform, new TransformPose(particle.m_Transform)));
            }

            dynamicBone.enabled = state;

            foreach (var pair in poses)
            {
                pair.Value.Apply(pair.Key);
            }
        }

        private struct TransformPose
        {
            private readonly Vector3 _localPosition;
            private readonly Quaternion _localRotation;
            private readonly Vector3 _localScale;

            public TransformPose(Transform t)
            {
                _localPosition = t.localPosition;
                _localRotation = t.localRotation;
                _localScale = t.localScale;
            }

            public void Apply(Transform t)
            {
                t.localPosition = _localPosition;
                t.localRotation = _localRotation;
                t.localScale = _localScale;
            }
        }

        /// <summary>アイテムが載っているスロットを引く。未装着・不正名は null</summary>
        private static TBodySkin GetSlot(Maid maid, string slotName)
        {
            if (maid == null || maid.body0 == null || !maid.body0.isLoadedBody
                || string.IsNullOrEmpty(slotName) || !TBody.hashSlotName.ContainsKey(slotName))
            {
                return null;
            }

            var slot = maid.body0.GetSlot(slotName);
            return slot != null && slot.obj != null ? slot : null;
        }
    }
}
