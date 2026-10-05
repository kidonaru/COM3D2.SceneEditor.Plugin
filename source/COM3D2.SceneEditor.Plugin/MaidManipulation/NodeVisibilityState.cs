using System.Collections.Generic;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// メイド 1 人分のノード表示の上書きと、上書き前の値の退避。
    /// ゲーム外テストのため、スロットごとの TBodySkin.m_dicDelNodeBody だけを相手にする
    /// （slots の index 0 が body、null は未装着のスロット）。
    /// 非表示は常に可視の body へ false を書き、脱衣で効果が消えないようにする。
    /// 表示（menu の node消去 の打ち消し）は false になっている全スロットを true にする。
    /// tokens はスロットの実体（TBodySkin.obj）で、退避した後にスロットが作り直されたかの判定に使う（null なら判定しない）
    /// </summary>
    public class NodeVisibilityState
    {
        private struct Backup
        {
            /// <summary>書き換え前にキーがあったか。無かったなら解除時にキーを消す</summary>
            public bool existed;

            public bool value;

            /// <summary>SE が書いた値。戻すのは今の値がこれのときだけ（ゲームが書き直した値は残す）</summary>
            public bool writtenValue;

            /// <summary>退避したときのスロットの実体。作り直されたスロットの値はゲームのものなので戻さない</summary>
            public object token;
        }

        private readonly Dictionary<string, bool> _overrides = new Dictionary<string, bool>();

        /// <summary>ノード → スロット index → 書き換え前の値</summary>
        private readonly Dictionary<string, Dictionary<int, Backup>> _backups
            = new Dictionary<string, Dictionary<int, Backup>>();

        public int count => _overrides.Count;

        public bool TryGetOverride(string node, out bool visible)
        {
            return _overrides.TryGetValue(node, out visible);
        }

        public Dictionary<string, bool> CopyOverrides()
        {
            return new Dictionary<string, bool>(_overrides);
        }

        public void Set(string node, bool visible, IList<Dictionary<string, bool>> slots)
        {
            Set(node, visible, slots, null);
        }

        /// <summary>上書きを記録して dict へ書く。slots が null（着替え中）なら記録だけで、ApplyAll が後で書く</summary>
        public void Set(string node, bool visible, IList<Dictionary<string, bool>> slots, IList<object> tokens)
        {
            if (slots != null)
            {
                // 表示 → 非表示のように向きが変わるとき、前の書き込みを残さない
                RestoreNode(node, slots, tokens);
            }
            _overrides[node] = visible;
            if (slots != null)
            {
                ApplyNode(node, visible, slots, tokens);
            }
        }

        public bool Clear(string node, IList<Dictionary<string, bool>> slots)
        {
            return Clear(node, slots, null);
        }

        /// <summary>上書きを外して書き換え前の値へ戻す。上書きが無かったなら false</summary>
        public bool Clear(string node, IList<Dictionary<string, bool>> slots, IList<object> tokens)
        {
            if (slots != null)
            {
                RestoreNode(node, slots, tokens);
            }
            return _overrides.Remove(node);
        }

        public void ClearAll(IList<Dictionary<string, bool>> slots)
        {
            ClearAll(slots, null);
        }

        public void ClearAll(IList<Dictionary<string, bool>> slots, IList<object> tokens)
        {
            foreach (var node in new List<string>(_overrides.Keys))
            {
                Clear(node, slots, tokens);
            }
        }

        public void RestoreAll(IList<Dictionary<string, bool>> slots)
        {
            RestoreAll(slots, null);
        }

        /// <summary>全ノードを書き換え前の値へ戻す。上書き自体は残す。着替えの開始時に呼ぶ</summary>
        public void RestoreAll(IList<Dictionary<string, bool>> slots, IList<object> tokens)
        {
            foreach (var node in new List<string>(_backups.Keys))
            {
                RestoreNode(node, slots, tokens);
            }
        }

        public void ApplyAll(IList<Dictionary<string, bool>> slots)
        {
            ApplyAll(slots, null);
        }

        /// <summary>今の dict から退避を取り直して全上書きを書く。着替えの完了時に呼ぶ</summary>
        public void ApplyAll(IList<Dictionary<string, bool>> slots, IList<object> tokens)
        {
            foreach (var pair in _overrides)
            {
                RestoreNode(pair.Key, slots, tokens);
                ApplyNode(pair.Key, pair.Value, slots, tokens);
            }
        }

        /// <summary>
        /// 実際に表示されているか。TBody.FixVisibleFlag と同じく、
        /// 有効なスロット（boVisible かつ obj あり）のどれかが false なら非表示
        /// </summary>
        public static bool IsVisible(IEnumerable<Dictionary<string, bool>> activeSlots, string node)
        {
            foreach (var dict in activeSlots)
            {
                bool value;
                if (dict != null && dict.TryGetValue(node, out value) && !value)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>スロットの実体が 1 つでも入れ替わったか（参照で比べる）</summary>
        public static bool TokensChanged(IList<object> previous, IList<object> current)
        {
            if (previous == null || current == null || previous.Count != current.Count)
            {
                return true;
            }
            for (var i = 0; i < current.Count; i++)
            {
                if (!ReferenceEquals(previous[i], current[i]))
                {
                    return true;
                }
            }
            return false;
        }

        private static object GetToken(IList<object> tokens, int index)
        {
            return tokens != null && index < tokens.Count ? tokens[index] : null;
        }

        private void ApplyNode(string node, bool visible, IList<Dictionary<string, bool>> slots, IList<object> tokens)
        {
            var backups = new Dictionary<int, Backup>();
            if (visible)
            {
                for (var i = 0; i < slots.Count; i++)
                {
                    var dict = slots[i];
                    bool value;
                    if (dict != null && dict.TryGetValue(node, out value) && !value)
                    {
                        backups[i] = new Backup
                        {
                            existed = true,
                            value = false,
                            writtenValue = true,
                            token = GetToken(tokens, i),
                        };
                        dict[node] = true;
                    }
                }
            }
            else if (slots.Count > 0 && slots[0] != null)
            {
                var body = slots[0];
                bool value;
                var existed = body.TryGetValue(node, out value);
                backups[0] = new Backup
                {
                    existed = existed,
                    value = value,
                    writtenValue = false,
                    token = GetToken(tokens, 0),
                };
                body[node] = false;
            }
            _backups[node] = backups;
        }

        private void RestoreNode(string node, IList<Dictionary<string, bool>> slots, IList<object> tokens)
        {
            Dictionary<int, Backup> backups;
            if (!_backups.TryGetValue(node, out backups))
            {
                return;
            }
            foreach (var pair in backups)
            {
                if (pair.Key >= slots.Count || slots[pair.Key] == null)
                {
                    continue;
                }
                // 作り直されたスロットは menu が値を決め直しているので、同じ値に見えても戻さない
                if (tokens != null && !ReferenceEquals(GetToken(tokens, pair.Key), pair.Value.token))
                {
                    continue;
                }
                var dict = slots[pair.Key];
                bool current;
                // 着替えで Clear された dict（CRC）や、menu が書き直した値（旧ボディの _ALL_ 上書き）には触らない
                if (!dict.TryGetValue(node, out current) || current != pair.Value.writtenValue)
                {
                    continue;
                }
                if (pair.Value.existed)
                {
                    dict[node] = pair.Value.value;
                }
                else
                {
                    dict.Remove(node);
                }
            }
            _backups.Remove(node);
        }
    }
}
