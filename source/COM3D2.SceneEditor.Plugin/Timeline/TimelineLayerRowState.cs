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
    /// タイムラインの複数レイヤー表示状態 (表示集合・折りたたみ集合) と行リスト構築。
    /// セッション内のみのビュー状態で、永続化しない。
    /// Unity 非依存のジェネリックにしてユニットテスト可能にしている
    /// </summary>
    public class TimelineLayerRowState<TLayer, TItem>
        where TLayer : class
        where TItem : class
    {
        private readonly HashSet<TLayer> _visibleLayers = new HashSet<TLayer>();
        private readonly HashSet<TLayer> _collapsedLayers = new HashSet<TLayer>();
        private readonly List<TItem> _itemBuffer = new List<TItem>(128);

        /// <summary>アクティブレイヤーは常に表示対象</summary>
        public bool IsVisible(TLayer layer, TLayer currentLayer)
        {
            return layer == currentLayer || _visibleLayers.Contains(layer);
        }

        public void ToggleVisible(TLayer layer, TLayer currentLayer)
        {
            // アクティブレイヤーは非表示にできない
            if (layer == currentLayer)
            {
                _visibleLayers.Add(layer);
                return;
            }

            if (!_visibleLayers.Remove(layer))
            {
                _visibleLayers.Add(layer);
            }
        }

        public bool IsCollapsed(TLayer layer)
        {
            return _collapsedLayers.Contains(layer);
        }

        public void ToggleCollapsed(TLayer layer)
        {
            if (!_collapsedLayers.Remove(layer))
            {
                _collapsedLayers.Add(layer);
            }
        }

        /// <summary>
        /// 全レイヤーの表示を一括で切り替える。
        /// 一括非表示にしてもアクティブレイヤーは IsVisible の判定で表示扱いのまま残る
        /// </summary>
        public void SetAllVisible(IList<TLayer> layers, bool visible)
        {
            _visibleLayers.Clear();
            if (!visible)
            {
                return;
            }

            for (var i = 0; i < layers.Count; i++)
            {
                _visibleLayers.Add(layers[i]);
            }
        }

        public bool AreAllVisible(IList<TLayer> layers, TLayer currentLayer)
        {
            for (var i = 0; i < layers.Count; i++)
            {
                if (!IsVisible(layers[i], currentLayer))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// 折りたたみを一括で切り替える。画面に出ている行に対する操作なので、
        /// 非表示レイヤーは対象外にしてそれぞれの状態を保つ
        /// </summary>
        public void SetAllCollapsed(IList<TLayer> layers, TLayer currentLayer, bool collapsed)
        {
            for (var i = 0; i < layers.Count; i++)
            {
                var layer = layers[i];
                if (!IsVisible(layer, currentLayer))
                {
                    continue;
                }

                if (collapsed)
                {
                    _collapsedLayers.Add(layer);
                }
                else
                {
                    _collapsedLayers.Remove(layer);
                }
            }
        }

        public bool AreAllCollapsed(IList<TLayer> layers, TLayer currentLayer)
        {
            for (var i = 0; i < layers.Count; i++)
            {
                var layer = layers[i];
                if (IsVisible(layer, currentLayer) && !IsCollapsed(layer))
                {
                    return false;
                }
            }
            return true;
        }

        public void Reset()
        {
            _visibleLayers.Clear();
            _collapsedLayers.Clear();
        }

        /// <summary>タイムライン再構築等で消えたレイヤー参照を掃除する</summary>
        public void Prune(IList<TLayer> aliveLayers)
        {
            _visibleLayers.RemoveWhere(layer => !aliveLayers.Contains(layer));
            _collapsedLayers.RemoveWhere(layer => !aliveLayers.Contains(layer));
        }

        /// <summary>
        /// 表示レイヤーごとに「カテゴリ行 + (展開中なら) アイテム行」を
        /// layers の並び順で result へ組み立てる
        /// </summary>
        public void BuildRows(
            IList<TLayer> layers,
            TLayer currentLayer,
            Action<TLayer, List<TItem>> collectItems,
            List<LayerRow<TLayer, TItem>> result)
        {
            result.Clear();

            for (var i = 0; i < layers.Count; i++)
            {
                var layer = layers[i];
                if (!IsVisible(layer, currentLayer))
                {
                    continue;
                }

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
