using System;
using System.Collections.Generic;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// タイムラインの 1 行分。menuItem == null ならレイヤーカテゴリ行
    /// </summary>
    public struct LayerRow<TLayer, TItem>
        where TLayer : class
        where TItem : class
    {
        public TLayer layer;
        public TItem menuItem;

        public bool isHeader => menuItem == null;
    }

    /// <summary>
    /// タイムラインの複数レイヤー表示状態 (展開集合) と行リスト構築。
    /// セッション内のみのビュー状態で、永続化しない。
    /// Unity 非依存のジェネリックにしてユニットテスト可能にしている。
    ///
    /// 状態はレイヤー参照ではなくキー文字列で保持する。Undo/Redo はタイムラインを
    /// XML から作り直してレイヤーを別インスタンスにするため、参照で持つと
    /// 表示状態が巻き戻しのたびに失われる
    /// </summary>
    public class TimelineLayerRowState<TLayer, TItem>
        where TLayer : class
        where TItem : class
    {
        private readonly Func<TLayer, string> _keySelector;

        // 折りたたみは既定 ON。畳んだ側ではなく展開した側を持つことで、
        // セッション開始直後や新規レイヤーが自動的に折りたたみ状態になる
        private readonly HashSet<string> _expandedKeys = new HashSet<string>();
        private readonly List<TItem> _itemBuffer = new List<TItem>(128);
        private readonly HashSet<string> _aliveKeyBuffer = new HashSet<string>();
        // キーはレイヤーの生存中不変。毎フレーム全レイヤー分を引く経路 (BuildRows) で
        // 文字列を作り直さないよう、インスタンスごとに一度だけ作って持つ
        private readonly Dictionary<TLayer, string> _keyCache = new Dictionary<TLayer, string>();

        /// <param name="keySelector">
        /// レイヤーをタイムライン再構築をまたいで同定するキー。
        /// 同一キーのレイヤーが同時に 2 つ存在しないものを渡すこと
        /// </param>
        public TimelineLayerRowState(Func<TLayer, string> keySelector)
        {
            if (keySelector == null)
            {
                throw new ArgumentNullException(nameof(keySelector));
            }
            _keySelector = keySelector;
        }

        private string GetKey(TLayer layer)
        {
            string key;
            if (!_keyCache.TryGetValue(layer, out key))
            {
                key = _keySelector(layer);
                _keyCache[layer] = key;
            }
            return key;
        }

        public bool IsCollapsed(TLayer layer)
        {
            return !_expandedKeys.Contains(GetKey(layer));
        }

        public void ToggleCollapsed(TLayer layer)
        {
            var key = GetKey(layer);
            if (!_expandedKeys.Remove(key))
            {
                _expandedKeys.Add(key);
            }
        }

        /// <summary>渡したレイヤーの折りたたみを一括で切り替える (表示中の一覧を渡すこと)</summary>
        public void SetAllCollapsed(IList<TLayer> layers, bool collapsed)
        {
            for (var i = 0; i < layers.Count; i++)
            {
                var key = GetKey(layers[i]);
                if (collapsed)
                {
                    _expandedKeys.Remove(key);
                }
                else
                {
                    _expandedKeys.Add(key);
                }
            }
        }

        public bool AreAllCollapsed(IList<TLayer> layers)
        {
            for (var i = 0; i < layers.Count; i++)
            {
                if (!IsCollapsed(layers[i]))
                {
                    return false;
                }
            }
            return true;
        }

        public void Reset()
        {
            _expandedKeys.Clear();
            _keyCache.Clear();
        }

        /// <summary>
        /// 生存レイヤーに合わせてキーキャッシュを貼り直し、存在しなくなったキーの状態を捨てる。
        /// レイヤーの増減時だけでなく、タイムラインを作り直して
        /// レイヤーが別インスタンスになったときにも呼ぶこと (死んだ参照を溜めないため)
        /// </summary>
        public void Prune(IList<TLayer> aliveLayers)
        {
            _keyCache.Clear();
            _aliveKeyBuffer.Clear();
            for (var i = 0; i < aliveLayers.Count; i++)
            {
                _aliveKeyBuffer.Add(GetKey(aliveLayers[i]));
            }

            _expandedKeys.RemoveWhere(key => !_aliveKeyBuffer.Contains(key));
        }

        /// <summary>
        /// レイヤーごとに「カテゴリ行 + (展開中なら) アイテム行」を layers の並び順で result へ組み立てる
        /// </summary>
        public void BuildRows(
            IList<TLayer> layers,
            Action<TLayer, List<TItem>> collectItems,
            List<LayerRow<TLayer, TItem>> result)
        {
            result.Clear();

            for (var i = 0; i < layers.Count; i++)
            {
                var layer = layers[i];
                result.Add(new LayerRow<TLayer, TItem> { layer = layer });

                if (IsCollapsed(layer))
                {
                    continue;
                }

                _itemBuffer.Clear();
                collectItems(layer, _itemBuffer);
                foreach (var item in _itemBuffer)
                {
                    result.Add(new LayerRow<TLayer, TItem> { layer = layer, menuItem = item });
                }
            }
        }
    }
}
