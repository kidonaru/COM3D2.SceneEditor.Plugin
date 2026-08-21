using System.Collections.Generic;
using System.Linq;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>ボーンツリーの 1 ノード。表示順は親から子へ深さ優先で辿る</summary>
    public class SlotBoneNode
    {
        public Transform transform;
        public string name;
        public SlotBoneNode parent;
        public List<SlotBoneNode> children = new List<SlotBoneNode>();
        public int depth;
    }

    /// <summary>スロット単位のボーン列挙とツリー構築</summary>
    public static class SlotBoneManager
    {
        /// <summary>アイテムが載っているスロット名の一覧（TBody.SlotID の名前）</summary>
        public static List<string> GetLoadedSlotNames(Maid maid)
        {
            var result = new List<string>();
            var body = GetLoadedBody(maid);
            if (body == null)
            {
                return result;
            }

            // SlotID.end は番兵で goSlot に実体が無い。goSlot[int] は例外を握らないため上限を切る
            var count = Mathf.Min((int)TBody.SlotID.end, body.goSlot.Count);
            for (var i = 0; i < count; i++)
            {
                var slot = body.GetSlot(i);
                if (slot != null && slot.obj != null)
                {
                    result.Add(((TBody.SlotID)i).ToString());
                }
            }
            return result;
        }

        /// <summary>スロットのルート GameObject。未装着なら null</summary>
        public static GameObject GetSlotObject(Maid maid, string slotName)
        {
            var slot = GetSlot(maid, slotName);
            return slot != null ? slot.obj : null;
        }

        /// <summary>装着中のモデルファイル名。着替え検出のキーに使う</summary>
        public static string GetSlotItemFileName(Maid maid, string slotName)
        {
            var slot = GetSlot(maid, slotName);
            return slot != null ? slot.m_strModelFileName : null;
        }

        /// <summary>末端のダミーボーン (_nub) は編集対象にならないため表示しない</summary>
        public static bool IsVisibleBone(string boneName)
        {
            return !string.IsNullOrEmpty(boneName) && !boneName.EndsWith("_nub");
        }

        /// <summary>スロット配下の全ボーンから親子ツリーを組み、ルートノードの一覧を返す</summary>
        public static List<SlotBoneNode> BuildBoneTree(GameObject slotObj)
        {
            var roots = new List<SlotBoneNode>();
            var bones = CollectBones(slotObj);
            if (bones.Count == 0)
            {
                return roots;
            }

            // ボーン名はスロット内で一意である前提。重複時は警告して先勝ちで扱う
            var dupNames = bones.GroupBy(b => b.name)
                .Where(g => g.Count() > 1).Select(g => g.Key).ToArray();
            if (dupNames.Length > 0)
            {
                MTEUtils.LogWarning("同名ボーンを検出しました（先勝ちで扱います）: {0}",
                    string.Join(", ", dupNames));
            }

            var nodeMap = new Dictionary<Transform, SlotBoneNode>();
            foreach (var bone in bones)
            {
                nodeMap[bone] = new SlotBoneNode { transform = bone, name = bone.name };
            }

            var boneSet = new HashSet<Transform>(bones);
            foreach (var bone in bones)
            {
                // 中間に非ボーンの Transform が挟まる構成があるため祖先方向に探索する
                var p = bone.parent;
                while (p != null && !boneSet.Contains(p))
                {
                    p = p.parent;
                }

                var node = nodeMap[bone];
                if (p != null)
                {
                    node.parent = nodeMap[p];
                    node.parent.children.Add(node);
                }
                else
                {
                    roots.Add(node);
                }
            }

            SetDepth(roots, 0);
            return roots;
        }

        /// <summary>スロット配下の全 SkinnedMeshRenderer のボーンを結合して返す</summary>
        public static List<Transform> CollectBones(GameObject slotObj)
        {
            if (slotObj == null)
            {
                return new List<Transform>();
            }

            // スロットは複数 SMR で構成されうるため全結合で列挙する。
            // _SCL_ はスケール反映用の複製ボーン (親が実ボーン) で、これを残すと
            // 実ボーンが集合に入らず親子探索が Bip01 まで飛んでツリーが平坦化するため、
            // 親の実ボーンへ読み替える
            return slotObj.GetComponentsInChildren<SkinnedMeshRenderer>()
                .Where(s => s.bones != null)
                .SelectMany(s => s.bones)
                .Where(b => b != null)
                .Select(b => b.name.EndsWith("_SCL_") && b.parent != null ? b.parent : b)
                .Where(b => IsVisibleBone(b.name))
                .Distinct()
                .ToList();
        }

        /// <summary>スロット内のボーンを名前で引く。同名は先勝ち（BuildBoneTree 側で警告済み）</summary>
        public static Transform FindBone(GameObject slotObj, string boneName)
        {
            if (string.IsNullOrEmpty(boneName))
            {
                return null;
            }
            return CollectBones(slotObj).FirstOrDefault(b => b.name == boneName);
        }

        /// <summary>
        /// 構築済みのボーンツリーから名前でボーンを引く。見つからなければ null。
        /// 同名がある場合は親から子への深さ優先で最初に見つかったもの
        /// (重複自体は BuildBoneTree 側で警告済み)
        /// </summary>
        public static Transform FindBoneInTree(List<SlotBoneNode> nodes, string boneName)
        {
            foreach (var node in nodes)
            {
                if (node.transform != null && node.name == boneName)
                {
                    return node.transform;
                }

                var found = FindBoneInTree(node.children, boneName);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }

        private static TBody GetLoadedBody(Maid maid)
        {
            if (maid == null || maid.body0 == null || !maid.body0.isLoadedBody)
            {
                return null;
            }
            return maid.body0;
        }

        private static TBodySkin GetSlot(Maid maid, string slotName)
        {
            var body = GetLoadedBody(maid);
            if (body == null || string.IsNullOrEmpty(slotName) || !TBody.hashSlotName.ContainsKey(slotName))
            {
                return null;
            }
            return body.GetSlot(slotName);
        }

        private static void SetDepth(List<SlotBoneNode> nodes, int depth)
        {
            foreach (var node in nodes)
            {
                node.depth = depth;
                SetDepth(node.children, depth + 1);
            }
        }
    }
}
