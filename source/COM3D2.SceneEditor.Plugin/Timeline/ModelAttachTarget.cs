using System;
using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>アタッチ先の親のワールド姿勢。親を削除したときのキー変換に使う</summary>
    public struct ModelAttachPose
    {
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 lossyScale;

        public static ModelAttachPose From(Transform transform)
        {
            return new ModelAttachPose
            {
                position = transform.position,
                rotation = transform.rotation,
                lossyScale = transform.lossyScale,
            };
        }
    }

    /// <summary>
    /// アタッチ先として別のモデルを指す取り決め。
    /// モデルキーの index 12 (アタッチ先スロット) に ModelSlotNo を入れ、アタッチ先は文字列値でモデル名を参照する。
    /// 参照は今は "{モデル名}" (原点) だけだが、モデル内のボーンへ広げるときは "{モデル名}/{ボーン名}" にする。
    /// テストから呼ぶため Unity のネイティブ関数を使わない
    /// </summary>
    public static class ModelAttachTarget
    {
        /// <summary>アタッチ先がモデルであることを表すスロット番号。旧 SE・MTE は負数を「アタッチなし」と読む</summary>
        public const int ModelSlotNo = -2;

        /// <summary>循環したデータでも止まるよう、親をたどる深さの上限</summary>
        private const int MaxDepth = 64;

        // SortParentsFirst 専用の作業用コレクション (メインスレッドのみ)
        private static readonly List<KeyValuePair<string, int>> _depths = new List<KeyValuePair<string, int>>();
        private static readonly Dictionary<string, int> _depthMemo = new Dictionary<string, int>();

        public static bool IsModelTarget(int slotNo, string reference)
        {
            return slotNo == ModelSlotNo && !string.IsNullOrEmpty(reference);
        }

        /// <summary>
        /// 参照をモデル名とボーン名 (原点なら空) に分ける。
        /// モデル名自体に "/" を含みうるため、参照全体 → 先頭から "/" ごとに区切った接頭辞の順で実在するモデル名を探す
        /// </summary>
        public static bool TryResolveReference(
            string reference, Func<string, bool> modelExists, out string modelName, out string boneName)
        {
            modelName = null;
            boneName = "";
            if (string.IsNullOrEmpty(reference))
            {
                return false;
            }

            if (modelExists(reference))
            {
                modelName = reference;
                return true;
            }

            for (var i = reference.IndexOf('/'); i > 0; i = reference.IndexOf('/', i + 1))
            {
                var candidate = reference.Substring(0, i);
                if (modelExists(candidate))
                {
                    modelName = candidate;
                    boneName = reference.Substring(i + 1);
                    return true;
                }
            }
            return false;
        }

        /// <summary>childName を parentName へ付けると循環するか (自分自身・自分の子孫へ付ける場合)</summary>
        public static bool WouldCreateCycle(string childName, string parentName, Func<string, string> getParentModelName)
        {
            var current = parentName;
            for (var depth = 0; depth < MaxDepth && !string.IsNullOrEmpty(current); depth++)
            {
                if (current == childName)
                {
                    return true;
                }
                current = getParentModelName(current);
            }
            return false;
        }

        /// <summary>
        /// 親が子より先に来る順へ並べて result へ入れる (result はクリアする)。
        /// 同じ深さの中では元の順を保つ。循環している分は途中で打ち切って並べる
        /// </summary>
        public static void SortParentsFirst(
            List<string> names, Func<string, string> getParentModelName, List<string> result)
        {
            SortParentsFirst(names, getParentModelName, null, result);
        }

        /// <summary>
        /// 親を 2 系統 (ワールド補間の始点側と終点側) 持てる版。
        /// どちらの親よりも後に来るよう、深い方に合わせる
        /// </summary>
        public static void SortParentsFirst(
            List<string> names,
            Func<string, string> getParentModelName,
            Func<string, string> getSecondParentModelName,
            List<string> result)
        {
            result.Clear();
            // 毎フレーム呼ばれるので作業用のコレクションを使い回す (メインスレッド専用)
            var depths = _depths;
            depths.Clear();
            _depthMemo.Clear();
            var maxDepth = 0;
            foreach (var name in names)
            {
                var depth = GetDepth(name, getParentModelName, getSecondParentModelName, 0);
                depths.Add(new KeyValuePair<string, int>(name, depth));
                maxDepth = Math.Max(maxDepth, depth);
            }

            for (var depth = 0; depth <= maxDepth; depth++)
            {
                foreach (var pair in depths)
                {
                    if (pair.Value == depth)
                    {
                        result.Add(pair.Key);
                    }
                }
            }
        }

        /// <summary>
        /// 親をたどった深さ (親なしは 0)。親が 2 系統あれば深い方。
        /// 求めた値は _depthMemo に控え、2 系統で枝分かれしても同じモデルを何度もたどらない。
        /// 循環は、たどっている途中のモデルへ戻ったところ (控えの仮値 0) で打ち切る
        /// </summary>
        private static int GetDepth(
            string name, Func<string, string> getParent, Func<string, string> getSecondParent, int level)
        {
            int depth;
            if (_depthMemo.TryGetValue(name, out depth))
            {
                return depth;
            }
            if (level >= MaxDepth)
            {
                return 0;
            }

            _depthMemo[name] = 0;
            depth = GetParentDepth(getParent(name), getParent, getSecondParent, level);
            if (getSecondParent != null)
            {
                depth = Math.Max(depth, GetParentDepth(getSecondParent(name), getParent, getSecondParent, level));
            }
            _depthMemo[name] = depth;
            return depth;
        }

        private static int GetParentDepth(
            string parent, Func<string, string> getParent, Func<string, string> getSecondParent, int level)
        {
            return string.IsNullOrEmpty(parent) ? 0 : 1 + GetDepth(parent, getParent, getSecondParent, level + 1);
        }

        /// <summary>
        /// 親基準のローカル値を、配置ルート基準のローカル値へ変換する。
        /// 拡縮は各軸の見た目の倍率で割り戻す近似 (回転を含む非一様拡縮のせん断は表せない)
        /// </summary>
        public static void ConvertToRoot(
            ModelAttachPose parent, ModelAttachPose root,
            ref Vector3 position, ref Quaternion rotation, ref Vector3 scale)
        {
            var world = parent.position + parent.rotation * Vector3.Scale(parent.lossyScale, position);
            var inverseRoot = Conjugate(root.rotation);
            position = Divide(inverseRoot * (world - root.position), root.lossyScale);
            rotation = inverseRoot * parent.rotation * rotation;
            scale = Divide(Vector3.Scale(scale, parent.lossyScale), root.lossyScale);
        }

        /// <summary>単位クォータニオンの逆。Quaternion.Inverse はネイティブ呼び出しのため使わない</summary>
        private static Quaternion Conjugate(Quaternion q)
        {
            return new Quaternion(-q.x, -q.y, -q.z, q.w);
        }

        private static Vector3 Divide(Vector3 value, Vector3 divisor)
        {
            return new Vector3(
                divisor.x != 0f ? value.x / divisor.x : value.x,
                divisor.y != 0f ? value.y / divisor.y : value.y,
                divisor.z != 0f ? value.z / divisor.z : value.z);
        }
    }
}
