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

        /// <summary>
        /// スロット全体の揺れ状態。いずれかの物理が動いていれば true。
        /// PartsEditWithStudio の bYure (YureUtil.GetYureState) と同じ判定に合わせてあり、
        /// プリセットの相互運用に使う
        /// </summary>
        public static bool GetSlotYureState(Maid maid, string slotName)
        {
            var slot = GetSlot(maid, slotName);
            if (slot == null || slot.obj == null)
            {
                return false;
            }

            // m_bEnable はチェーンが 1 本も無くても Init() で true になるため、
            // チェーンの有無を先に確認する (CaptureSnapshot と同じ判定基準)
            if (HasBoneHairChains(slot) && HairEnableField != null
                && (bool)HairEnableField.GetValue(slot.bonehair))
            {
                return true;
            }

            // PartsEdit に合わせてスロット直下のコンポーネントだけを見る (子孫は対象外)
            var dynamicBone = slot.obj.GetComponent<DynamicBone>();
            if (dynamicBone != null && dynamicBone.enabled)
            {
                return true;
            }

            return slot.bonehair3 != null && SkirtBoneField != null
                && SkirtBoneField.GetValue(slot.bonehair3) != null;
        }

        /// <summary>
        /// スロット全体の揺れ物理をまとめて切り替える。GetSlotYureState の対になる操作で、
        /// PartsEdit プリセットの bYure 復元に使う
        /// </summary>
        public static void SetSlotYureState(Maid maid, string slotName, bool state)
        {
            var slot = GetSlot(maid, slotName);
            if (slot == null || slot.obj == null)
            {
                return;
            }

            if (HasBoneHairChains(slot) && HairEnableField != null)
            {
                HairEnableField.SetValue(slot.bonehair, state);
            }

            // 判定側は PartsEdit に合わせてスロット直下だけを見るが、切り替えは
            // 取り残しが出ないよう子孫の DynamicBone まで対象にする
            foreach (var dynamicBone in slot.obj.GetComponentsInChildren<DynamicBone>())
            {
                SetDynamicBoneEnabled(dynamicBone, state);
            }

            SetSkirtYureState(slot, state);
        }

        /// <summary>
        /// スロット 1 つ分の揺れ物理の状態スナップショット。シーンプリセットの保存・復元に使う。
        /// 物理の単位はゲーム実装に合わせる: TBoneHair_ はスロットで 1 bit、
        /// DynamicBone はコンポーネント毎 (ルートボーン名で同定)、DynamicSkirtBone はスロットで 1 bit
        /// </summary>
        public class SlotYureSnapshot
        {
            /// <summary>対象の物理が無いことを表す</summary>
            public const int None = -1;

            public int boneHair = None;
            public int skirt = None;
            public List<DynamicBoneEntry> dynamicBones = new List<DynamicBoneEntry>();

            public class DynamicBoneEntry
            {
                public string rootBoneName;
                public bool enabled;
            }

            public bool isEmpty =>
                boneHair == None && skirt == None && dynamicBones.Count == 0;
        }

        /// <summary>スロットの揺れ物理の状態を控える。対象の物理が無ければ null</summary>
        public static SlotYureSnapshot CaptureSnapshot(Maid maid, string slotName)
        {
            var slot = GetSlot(maid, slotName);
            if (slot == null || slot.obj == null)
            {
                return null;
            }

            var snapshot = new SlotYureSnapshot();

            if (HasBoneHairChains(slot) && HairEnableField != null)
            {
                snapshot.boneHair = (bool)HairEnableField.GetValue(slot.bonehair) ? 1 : 0;
            }

            foreach (var dynamicBone in slot.obj.GetComponentsInChildren<DynamicBone>())
            {
                if (dynamicBone.m_Root == null)
                {
                    continue;
                }
                snapshot.dynamicBones.Add(new SlotYureSnapshot.DynamicBoneEntry
                {
                    rootBoneName = dynamicBone.m_Root.name,
                    enabled = dynamicBone.enabled,
                });
            }

            // スカート物理の有効判定は GetYureState と同じく BoneHair3 側の参照で行う。
            // 複数コンポーネント構成でも取り逃さないよう、参照先が列挙の中にあるかで見る
            var skirtBones = slot.obj.GetComponentsInChildren<DynamicSkirtBone>();
            if (skirtBones.Length > 0 && slot.bonehair3 != null && SkirtBoneField != null)
            {
                snapshot.skirt =
                    FindReferencedSkirtBone(slot, skirtBones) != null ? 1 : 0;
            }

            return snapshot.isEmpty ? null : snapshot;
        }

        /// <summary>
        /// 控えた揺れ物理の状態を書き戻す。構成が変わっていて見つからない物理は飛ばす
        /// </summary>
        public static void ApplySnapshot(Maid maid, string slotName, SlotYureSnapshot snapshot)
        {
            var slot = GetSlot(maid, slotName);
            if (slot == null || slot.obj == null || snapshot == null)
            {
                return;
            }

            if (snapshot.boneHair != SlotYureSnapshot.None
                && slot.bonehair != null && HairEnableField != null)
            {
                HairEnableField.SetValue(slot.bonehair, snapshot.boneHair != 0);
            }

            if (snapshot.dynamicBones.Count > 0)
            {
                var dynamicBones = slot.obj.GetComponentsInChildren<DynamicBone>();
                foreach (var entry in snapshot.dynamicBones)
                {
                    foreach (var dynamicBone in dynamicBones)
                    {
                        if (dynamicBone.m_Root != null
                            && dynamicBone.m_Root.name == entry.rootBoneName)
                        {
                            SetDynamicBoneEnabled(dynamicBone, entry.enabled);
                        }
                    }
                }
            }

            if (snapshot.skirt != SlotYureSnapshot.None)
            {
                SetSkirtYureState(slot, snapshot.skirt != 0);
            }
        }

        /// <summary>
        /// スロットのスカート物理を切り替える。参照中のコンポーネントを優先し、
        /// 参照が外れていれば先頭を使う (BoneHair3 が持てる参照は 1 つの単一参照モデル)
        /// </summary>
        private static void SetSkirtYureState(TBodySkin slot, bool state)
        {
            var skirtBones = slot.obj.GetComponentsInChildren<DynamicSkirtBone>();
            if (skirtBones.Length == 0 || slot.bonehair3 == null || SkirtBoneField == null)
            {
                return;
            }

            var target = FindReferencedSkirtBone(slot, skirtBones) ?? skirtBones[0];
            target.enabled = state;
            SkirtBoneField.SetValue(slot.bonehair3, state ? target : null);
        }

        /// <summary>BoneHair3 が現在参照しているスカート物理。参照が無い/外れていれば null</summary>
        private static DynamicSkirtBone FindReferencedSkirtBone(
            TBodySkin slot, DynamicSkirtBone[] skirtBones)
        {
            var current = SkirtBoneField != null && slot.bonehair3 != null
                ? SkirtBoneField.GetValue(slot.bonehair3) : null;
            foreach (var skirtBone in skirtBones)
            {
                if (ReferenceEquals(current, skirtBone))
                {
                    return skirtBone;
                }
            }
            return null;
        }

        /// <summary>TBoneHair_ が駆動対象のチェーンを持つか (無いスロットでは m_bEnable に意味が無い)</summary>
        private static bool HasBoneHairChains(TBodySkin slot)
        {
            var bonehair = slot.bonehair;
            if (bonehair == null)
            {
                return false;
            }
            var hairList = HairListField != null
                ? HairListField.GetValue(bonehair) as List<THair1> : null;
            return (hairList != null && hairList.Count > 0) || bonehair.boSkirt;
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
