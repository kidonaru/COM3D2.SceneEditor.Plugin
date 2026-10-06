using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// メイドのノード表示/非表示（ACCEx の「表示ノード選択」相当）の書き込み口。
    /// 上書きの記録と退避は NodeVisibilityState に任せ、ここは dict の収集・反映 (FixVisibleFlag)・着替え追従を担う。
    /// 書き込みは dirty を立てるだけで、FixMaskFlag → FixVisibleFlag は Flush がまとめて 1 回呼ぶ
    /// </summary>
    public static class MaidNodeVisibilityController
    {
        private class Entry
        {
            public readonly NodeVisibilityState state = new NodeVisibilityState();

            /// <summary>前フレームの着替え中フラグ。立ち上がりで退避値へ戻し、立ち下がりで書き直す</summary>
            public bool wasBusy;

            /// <summary>dict を書き換えて FixVisibleFlag がまだのとき</summary>
            public bool dirty;

            /// <summary>
            /// 最後に全上書きを書いたときのスロットの実体。
            /// 同期の AllProcProp（めくれ・ずらし）は busy を立てずにスロットを作り直すので、これの変化で気づく
            /// </summary>
            public List<object> tokens;
        }

        /// <summary>スロットごとの dict と実体（index 0 が body）</summary>
        private class Slots
        {
            public readonly List<Dictionary<string, bool>> dicts;
            public readonly List<object> tokens;

            public Slots(int capacity)
            {
                dicts = new List<Dictionary<string, bool>>(capacity);
                tokens = new List<object>(capacity);
            }
        }

        private static readonly Dictionary<Maid, Entry> _entries = new Dictionary<Maid, Entry>();

        /// <summary>Update でスロットの入れ替わりを調べる作業用。毎フレームの確保を避ける</summary>
        private static readonly List<object> _tokenScratch = new List<object>();

        /// <summary>IsVisible 用に、表示中のスロットの dict をフレーム単位で持つ（一覧の 90 行で走査を繰り返さない）</summary>
        private static readonly List<Dictionary<string, bool>> _activeDicts = new List<Dictionary<string, bool>>();
        private static Maid _activeDictsMaid;
        private static int _activeDictsFrame = -1;

        public static bool HasOverrides(Maid maid)
        {
            var entry = GetEntry(maid);
            return entry != null && entry.state.count > 0;
        }

        public static bool TryGetOverride(Maid maid, string node, out bool visible)
        {
            visible = true;
            var entry = GetEntry(maid);
            return entry != null && entry.state.TryGetOverride(node, out visible);
        }

        /// <summary>実際に表示されているか。ボディ未ロードなら true</summary>
        public static bool IsVisible(Maid maid, string node)
        {
            if (!IsBodyReady(maid))
            {
                return true;
            }
            return NodeVisibilityState.IsVisible(GetActiveDicts(maid), node);
        }

        public static void SetOverride(Maid maid, string node, bool visible)
        {
            var entry = GetOrCreateEntry(maid);
            if (entry == null)
            {
                return;
            }
            var slots = GetWritableSlots(maid, entry);
            entry.state.Set(node, visible, slots?.dicts, slots?.tokens);
            entry.dirty = true;
        }

        public static void ClearOverride(Maid maid, string node)
        {
            var entry = GetEntry(maid);
            if (entry == null)
            {
                return;
            }
            var slots = GetWritableSlots(maid, entry);
            // 上書きの無いノードで FixVisibleFlag を走らせない（タイムラインが区間に入るたびに呼ぶため）
            if (entry.state.Clear(node, slots?.dicts, slots?.tokens))
            {
                entry.dirty = true;
            }
        }

        /// <summary>SE の上書きを全解除して、ゲームが決めた表示へ戻す</summary>
        public static void ClearAll(Maid maid)
        {
            var entry = GetEntry(maid);
            if (entry == null || entry.state.count == 0)
            {
                return;
            }
            var slots = GetWritableSlots(maid, entry);
            entry.state.ClearAll(slots?.dicts, slots?.tokens);
            entry.dirty = true;
        }

        /// <summary>全ノードを表示か非表示で上書きする。表示は衣装の node消去 も打ち消す</summary>
        public static void SetAll(Maid maid, bool visible)
        {
            var entry = GetOrCreateEntry(maid);
            if (entry == null)
            {
                return;
            }
            // dict の収集はノードごとにせず 1 回で済ませる
            var slots = GetWritableSlots(maid, entry);
            foreach (var node in MaidNodeVisibilityNodes.nodes)
            {
                entry.state.Set(node.boneName, visible, slots?.dicts, slots?.tokens);
            }
            entry.dirty = true;
        }

        public static Dictionary<string, bool> GetOverrides(Maid maid)
        {
            var entry = GetEntry(maid);
            return entry != null ? entry.state.CopyOverrides() : new Dictionary<string, bool>();
        }

        /// <summary>
        /// 上書きを丸ごと入れ替える（Undo・プリセット用）。対象外のノード名は捨てる。
        /// 上書きの無いメイドへ空を渡しても状態を作らない
        /// </summary>
        public static void SetOverrides(Maid maid, IDictionary<string, bool> overrides)
        {
            if (overrides.Count == 0 && GetEntry(maid) == null)
            {
                return;
            }
            var entry = GetOrCreateEntry(maid);
            if (entry == null)
            {
                return;
            }
            var slots = GetWritableSlots(maid, entry);
            foreach (var pair in entry.state.CopyOverrides())
            {
                if (!overrides.ContainsKey(pair.Key))
                {
                    entry.state.Clear(pair.Key, slots?.dicts, slots?.tokens);
                    entry.dirty = true;
                }
            }
            foreach (var pair in overrides)
            {
                if (MaidNodeVisibilityNodes.Find(pair.Key) == null)
                {
                    continue;
                }
                bool current;
                if (entry.state.TryGetOverride(pair.Key, out current) && current == pair.Value)
                {
                    continue;
                }
                entry.state.Set(pair.Key, pair.Value, slots?.dicts, slots?.tokens);
                entry.dirty = true;
            }
        }

        /// <summary>書き換えた dict をメッシュへ反映する。着替え中は完了時の ApplyAll に任せる</summary>
        public static void Flush(Maid maid)
        {
            var entry = GetEntry(maid);
            if (entry == null || !entry.dirty || entry.wasBusy || !IsBodyReady(maid))
            {
                return;
            }
            entry.dirty = false;
            maid.body0.FixMaskFlag();
            maid.body0.FixVisibleFlag();
        }

        /// <summary>着替え・ボディ再ロードへの追従と、取り残した反映。MaidManipulateManager.Update から毎フレーム呼ぶ</summary>
        public static void Update()
        {
            if (_entries.Count == 0)
            {
                return;
            }

            List<Maid> deadMaids = null;
            foreach (var pair in _entries)
            {
                var maid = pair.Key;
                var entry = pair.Value;
                if (maid == null || maid.body0 == null)
                {
                    if (deadMaids == null)
                    {
                        deadMaids = new List<Maid>();
                    }
                    deadMaids.Add(maid);
                    continue;
                }

                SyncBusy(maid, entry);
                if (!maid.IsAllProcPropBusy && maid.body0.isLoadedBody)
                {
                    if (entry.wasBusy)
                    {
                        entry.wasBusy = false;
                        Reapply(maid, entry);
                    }
                    else if (entry.state.count > 0 && SlotsReplaced(maid, entry))
                    {
                        Reapply(maid, entry);
                    }
                }

                // Flush は _entries を書き換えないので、列挙の中で呼んでよい
                Flush(maid);
            }

            if (deadMaids != null)
            {
                foreach (var maid in deadMaids)
                {
                    _entries.Remove(maid);
                }
            }
        }

        /// <summary>メイド解除時。上書きを戻して、使い回されるストックの Maid へ持ち越さない</summary>
        public static void Release(Maid maid)
        {
            var entry = GetEntry(maid);
            if (entry == null)
            {
                return;
            }
            ClearAll(maid);
            Flush(maid);
            _entries.Remove(maid);
        }

        public static void Destroy()
        {
            foreach (var maid in new List<Maid>(_entries.Keys))
            {
                if (maid != null)
                {
                    Release(maid);
                }
            }
            _entries.Clear();
        }

        public static bool IsBodyReady(Maid maid)
        {
            return maid != null && maid.body0 != null && maid.body0.isLoadedBody;
        }

        private static Entry GetEntry(Maid maid)
        {
            Entry entry;
            return maid != null && _entries.TryGetValue(maid, out entry) ? entry : null;
        }

        private static Entry GetOrCreateEntry(Maid maid)
        {
            if (maid == null || maid.body0 == null)
            {
                return null;
            }
            var entry = GetEntry(maid);
            if (entry == null)
            {
                entry = new Entry { wasBusy = maid.IsAllProcPropBusy || !maid.body0.isLoadedBody };
                _entries[maid] = entry;
            }
            return entry;
        }

        /// <summary>今のスロットから退避を取り直して全上書きを書き直す。作り直されたスロットの退避値は戻さない</summary>
        private static void Reapply(Maid maid, Entry entry)
        {
            var slots = CollectSlots(maid.body0);
            if (slots == null)
            {
                return;
            }
            entry.state.ApplyAll(slots.dicts, slots.tokens);
            entry.tokens = slots.tokens;
            entry.dirty = true;
        }

        /// <summary>最後に全上書きを書いた後で、スロットの実体が入れ替わったか</summary>
        private static bool SlotsReplaced(Maid maid, Entry entry)
        {
            _tokenScratch.Clear();
            var body = maid.body0;
            var count = GetSlotCount(body);
            for (var i = 0; i < count; i++)
            {
                _tokenScratch.Add(GetToken(body.goSlot[i]));
            }
            return NodeVisibilityState.TokensChanged(entry.tokens, _tokenScratch);
        }

        /// <summary>
        /// 着替えの立ち上がりを処理する。着替えで dict が作り直される前に、SE が書いた値をゲームの値へ戻しておく
        /// （作り直されなかったスロットに SE の値が残り、次の退避に紛れ込むのを防ぐ）。
        /// Update を待たずに書き込み口からも呼び、立ち上がり直後の Release・ClearAll が退避を取り残さないようにする
        /// </summary>
        private static void SyncBusy(Maid maid, Entry entry)
        {
            if (entry.wasBusy || !maid.IsAllProcPropBusy)
            {
                return;
            }
            var slots = CollectSlots(maid.body0);
            if (slots != null)
            {
                entry.state.RestoreAll(slots.dicts, slots.tokens);
            }
            entry.wasBusy = true;
        }

        /// <summary>着替え中・未ロードなら null（記録だけにして、完了時の ApplyAll に書かせる）</summary>
        private static Slots GetWritableSlots(Maid maid, Entry entry)
        {
            SyncBusy(maid, entry);
            if (entry.wasBusy || !IsBodyReady(maid))
            {
                return null;
            }
            return CollectSlots(maid.body0);
        }

        /// <summary>SlotID.end は番兵で goSlot に実体が無い。goSlot[int] は例外を握らないため上限を切る</summary>
        private static int GetSlotCount(TBody body)
        {
            return body == null || body.goSlot == null
                ? 0
                : Mathf.Min((int)TBody.SlotID.end, body.goSlot.Count);
        }

        /// <summary>スロットの実体。参照の比較だけに使い、Unity の == は通さない</summary>
        private static object GetToken(TBodySkin slot)
        {
            return slot != null ? (object)slot.obj : null;
        }

        /// <summary>
        /// スロットごとの m_dicDelNodeBody と実体（index 0 が body）。未装着のスロットは dict が null。
        /// FixVisibleFlag と同じく goSlot[i] だけを見る（2.5 のサブスロットは対象外）
        /// </summary>
        private static Slots CollectSlots(TBody body)
        {
            var count = GetSlotCount(body);
            if (count == 0)
            {
                return null;
            }
            var slots = new Slots(count);
            for (var i = 0; i < count; i++)
            {
                var slot = body.goSlot[i];
                slots.dicts.Add(slot != null ? slot.m_dicDelNodeBody : null);
                slots.tokens.Add(GetToken(slot));
            }
            return slots;
        }

        /// <summary>FixVisibleFlag が見るスロット（boVisible かつ obj あり）の dict。同じフレームの同じメイドなら使い回す</summary>
        private static List<Dictionary<string, bool>> GetActiveDicts(Maid maid)
        {
            var frame = Time.frameCount;
            if (ReferenceEquals(_activeDictsMaid, maid) && _activeDictsFrame == frame)
            {
                return _activeDicts;
            }
            _activeDicts.Clear();
            _activeDictsMaid = maid;
            _activeDictsFrame = frame;

            var body = maid.body0;
            var count = GetSlotCount(body);
            for (var i = 0; i < count; i++)
            {
                var slot = body.goSlot[i];
                if (slot != null && slot.boVisible && slot.obj != null)
                {
                    _activeDicts.Add(slot.m_dicDelNodeBody);
                }
            }
            return _activeDicts;
        }
    }
}
