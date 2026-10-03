using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// メイドスケール (腕の骨の均一拡縮) の状態を持ち、スキニングへ反映する。
    /// メッシュは TBodySkin ごとの複製骨にスキニングされ、本体の骨のスケールはコピーされないため、
    /// 全スロットの複製骨 (SkinnedMeshRenderer.bones) へ直接掛ける。
    /// 掛けるのは TBody.LateUpdate の直後 (MaidScaleLateUpdatePatch)。
    /// それより前だと MOD (MaidVoicePitch の ForeArmFix) が前腕を書き戻し、
    /// 描画直前 (Camera.onPreCull) ではスキニングの計算に間に合わない。
    /// 次フレームの Update と TBody.LateUpdate の直前で元へ戻す。
    /// GUI を描く間だけは外して掛け直す (SuspendApplied / ResumeApplied)。
    /// 本体の骨には触らないので、IK・ハンドル・アタッチ位置は拡縮の影響を受けない
    /// </summary>
    public class MaidScaleController
    {
        /// <summary>1 回分の書き込み記録。戻すときに使う</summary>
        private struct AppliedBone
        {
            public Transform bone;
            public Vector3 original;
            public Vector3 written;
        }

        /// <summary>メイド 1 体分の状態と複製骨のキャッシュ</summary>
        private class Entry
        {
            public readonly MaidScaleState state = new MaidScaleState();

            /// <summary>骨名 → 全スロットの複製骨</summary>
            public readonly Dictionary<string, List<Transform>> bones
                = new Dictionary<string, List<Transform>>();

            /// <summary>キャッシュを作った時点のスロット obj の並び。変わったら作り直す</summary>
            public readonly List<GameObject> slotObjects = new List<GameObject>();

            public bool isCacheValid;

            /// <summary>前回掛けた分。次に掛ける前か Update で戻す</summary>
            public readonly List<AppliedBone> applied = new List<AppliedBone>();

            /// <summary>GUI の間だけ外している。ResumeApplied で掛け直す</summary>
            public bool isSuspended;
        }

        private readonly Dictionary<Maid, Entry> _entries = new Dictionary<Maid, Entry>();

        /// <summary>キャッシュの検証で毎回作らないよう使い回す</summary>
        private readonly List<GameObject> _slotObjectBuffer = new List<GameObject>();

        private readonly List<Maid> _deadMaids = new List<Maid>();

        public bool HasState(Maid maid)
        {
            return maid != null && _entries.ContainsKey(maid);
        }

        /// <summary>記録が無いメイドは倍率 1 として扱う</summary>
        public float GetScale(Maid maid, string boneName)
        {
            Entry entry;
            if (maid == null || !_entries.TryGetValue(maid, out entry))
            {
                return MaidScaleBones.DefaultScale;
            }
            return entry.state.Get(boneName);
        }

        public void SetScale(Maid maid, string boneName, float scale)
        {
            if (maid == null || MaidScaleBones.Find(boneName) == null)
            {
                return;
            }

            Entry entry;
            if (!_entries.TryGetValue(maid, out entry))
            {
                // 元の大きさを書くだけなら状態を作らない (常駐コストを増やさない)
                if (MaidScaleBones.IsDefault(scale))
                {
                    return;
                }
                entry = new Entry();
                _entries[maid] = entry;
            }

            entry.state.Set(boneName, scale);

            // 全骨が元の大きさに戻ったら状態ごと捨てる (HasState を「拡縮中か」に揃える)
            if (entry.state.isDefault)
            {
                Restore(entry);
                _entries.Remove(maid);
            }
        }

        /// <summary>
        /// 全メイドの前回分を戻す。プラグインの Update から毎フレーム呼び、
        /// ゲームのロジックにはなるべく拡縮していない骨を見せる。
        /// 解除済みのメイドの状態もここで捨てる
        /// </summary>
        public void RestoreApplied()
        {
            if (_entries.Count == 0)
            {
                return;
            }

            _deadMaids.Clear();
            foreach (var pair in _entries)
            {
                Restore(pair.Value);
                if (pair.Key == null || pair.Key.body0 == null)
                {
                    _deadMaids.Add(pair.Key);
                }
            }
            foreach (var maid in _deadMaids)
            {
                _entries.Remove(maid);
            }
        }

        /// <summary>
        /// GUI が骨を読み書きする間だけ、掛けた分を外す。ResumeApplied と対で呼ぶ。
        /// ボーンウィンドウに倍率込みのスケールを表示・記録・焼き込みさせないため
        /// </summary>
        public void SuspendApplied()
        {
            foreach (var entry in _entries.Values)
            {
                if (entry.applied.Count == 0)
                {
                    continue;
                }
                Restore(entry);
                entry.isSuspended = true;
            }
        }

        /// <summary>
        /// SuspendApplied で外した分を掛け直す。GUI が書き換えた値はその値を元として掛ける。
        /// OnGUI の後に描くフレーム末の撮影 (camera.Render) にも倍率を写すため。
        /// GUI の中で状態ごと捨てたメイドは _entries に無いので掛け直さない
        /// </summary>
        public void ResumeApplied()
        {
            foreach (var entry in _entries.Values)
            {
                if (!entry.isSuspended)
                {
                    continue;
                }
                entry.isSuspended = false;
                ApplyScales(entry);
            }
        }

        /// <summary>メイド解除時。ストックの Maid は使い回されるので状態を持ち越さない</summary>
        public void Release(Maid maid)
        {
            Entry entry;
            if (maid == null || !_entries.TryGetValue(maid, out entry))
            {
                return;
            }
            Restore(entry);
            _entries.Remove(maid);
        }

        public void Destroy()
        {
            foreach (var entry in _entries.Values)
            {
                Restore(entry);
            }
            _entries.Clear();
        }

        /// <summary>TBody.LateUpdate の直前。前フレームに掛けた分が残っていれば戻す</summary>
        public void OnBodyLateUpdateBegin(TBody body)
        {
            var entry = FindEntry(body);
            if (entry != null)
            {
                Restore(entry);
            }
        }

        /// <summary>
        /// TBody.LateUpdate の直後。CopyTrans と MOD の書き込みが済み、
        /// スキニングの計算より前なので、ここで掛けた値が描画に効く
        /// </summary>
        public void OnBodyLateUpdateEnd(TBody body)
        {
            var entry = FindEntry(body);
            if (entry == null)
            {
                return;
            }

            // Update が回らないフレームでも積み上げないよう、前回分を必ず戻してから掛ける
            Restore(entry);

            // 着替え・ボディ読込中は複製骨が破棄・再生成されるので触らない
            if (!body.isLoadedBody || body.maid.IsAllProcPropBusy)
            {
                entry.isCacheValid = false;
                return;
            }

            RefreshBonesIfNeeded(body, entry);
            ApplyScales(entry);
        }

        /// <summary>キャッシュ済みの複製骨へ、今の値 × 倍率を書き、戻すための記録を残す</summary>
        private static void ApplyScales(Entry entry)
        {
            foreach (var scalePair in entry.state.nonDefaultScales)
            {
                List<Transform> bones;
                if (!entry.bones.TryGetValue(scalePair.Key, out bones))
                {
                    continue;
                }
                foreach (var bone in bones)
                {
                    if (bone == null)
                    {
                        continue;
                    }
                    var original = bone.localScale;
                    var written = original * scalePair.Value;
                    bone.localScale = written;
                    entry.applied.Add(new AppliedBone
                    {
                        bone = bone,
                        original = original,
                        written = written,
                    });
                }
            }
        }

        private Entry FindEntry(TBody body)
        {
            if (_entries.Count == 0 || body == null || body.maid == null)
            {
                return null;
            }
            Entry entry;
            return _entries.TryGetValue(body.maid, out entry) ? entry : null;
        }

        /// <summary>自分が書いた値のまま残っている骨だけを戻し、破棄済みの骨は飛ばす</summary>
        private static void Restore(Entry entry)
        {
            var applied = entry.applied;
            for (var i = 0; i < applied.Count; i++)
            {
                var record = applied[i];
                if (record.bone != null
                    && MaidScaleState.ShouldRestore(record.bone.localScale, record.written))
                {
                    record.bone.localScale = record.original;
                }
            }
            applied.Clear();
        }

        private void RefreshBonesIfNeeded(TBody body, Entry entry)
        {
            CollectSlotObjects(body, _slotObjectBuffer);
            if (entry.isCacheValid && IsSameSlotObjects(entry.slotObjects, _slotObjectBuffer))
            {
                return;
            }

            entry.slotObjects.Clear();
            entry.slotObjects.AddRange(_slotObjectBuffer);
            RebuildBones(entry);
            entry.isCacheValid = true;
        }

        /// <summary>全スロットの obj を集める。2.5 はサブスロット (goSlot[i, j]) も含める</summary>
        private static void CollectSlotObjects(TBody body, List<GameObject> result)
        {
            result.Clear();
            // SlotID.end は番兵で goSlot に実体が無い。goSlot[int] は例外を握らないため上限を切る
            var count = Mathf.Min((int)TBody.SlotID.end, body.goSlot.Count);
            for (var i = 0; i < count; i++)
            {
#if COM3D25
                var childCount = body.goSlot.CountChildren(i);
                for (var j = 0; j < childCount; j++)
                {
                    var slot = body.goSlot[i, j];
                    result.Add(slot != null ? slot.obj : null);
                }
#else
                var slot = body.GetSlot(i);
                result.Add(slot != null ? slot.obj : null);
#endif
            }
        }

        private static bool IsSameSlotObjects(List<GameObject> a, List<GameObject> b)
        {
            if (a.Count != b.Count)
            {
                return false;
            }
            for (var i = 0; i < a.Count; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// 各スロットの SkinnedMeshRenderer が実際に参照している骨と、そのスロット obj までの祖先から対象骨を集める。
        /// CRC ボディは上腕・前腕を直接参照せず子のツイスト骨 (UpperTwist* / ForeTwist*) だけを参照するため、
        /// 祖先までたどらないと袖だけが拡縮される。
        /// 名前で子孫を探すと、別スロットの骨や持ち物の中の同名ノードを拾うおそれがある。
        /// スロット obj 配下にない骨 (本体の骨を直接参照している場合) は書かない
        /// </summary>
        private static void RebuildBones(Entry entry)
        {
            entry.bones.Clear();

            // 一度たどった骨から上は同じ経路になるので、ここで打ち切る
            var visited = new HashSet<Transform>();

            foreach (var slotObject in entry.slotObjects)
            {
                if (slotObject == null)
                {
                    continue;
                }
                var slotRoot = slotObject.transform;
                foreach (var renderer in slotObject.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var bones = renderer.bones;
                    if (bones == null)
                    {
                        continue;
                    }
                    foreach (var bone in bones)
                    {
                        if (bone == null || !bone.IsChildOf(slotRoot))
                        {
                            continue;
                        }

                        for (var t = bone; t != null && t != slotRoot && visited.Add(t); t = t.parent)
                        {
                            if (MaidScaleBones.Find(t.name) != null)
                            {
                                AddBone(entry, t);
                            }
                        }
                    }
                }
            }
        }

        private static void AddBone(Entry entry, Transform bone)
        {
            List<Transform> list;
            if (!entry.bones.TryGetValue(bone.name, out list))
            {
                list = new List<Transform>();
                entry.bones[bone.name] = list;
            }
            list.Add(bone);
        }
    }
}
