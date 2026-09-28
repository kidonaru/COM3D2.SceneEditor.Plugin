using System;
using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>Hierarchy の配置物ビューのカテゴリ。定義順がそのまま表示順</summary>
    public enum PlacedObjectCategory
    {
        Maid,
        Model,
        Background,
        Png,
        Light,
        SubCamera,
    }

    /// <summary>
    /// 配置物 1 件の入力。PlacedObjectCollector がゲームの実体から作る。
    /// 木の組み立てと変化検知は target に触れずこの値だけで行うため、テストでは target を null にできる
    /// </summary>
    public class PlacedObjectSource
    {
        public PlacedObjectCategory category;
        /// <summary>対象 GameObject の GetInstanceID。行の ID (展開状態・スクロール予約) にも使う</summary>
        public int id;
        public string label;
        /// <summary>親にする配置物の id。NoParent または一覧に無い id ならカテゴリ直下に置く</summary>
        public int parentId = PlacedObjectTree.NoParent;
        public GameObject target;
    }

    /// <summary>配置物ビューの 1 行。カテゴリ見出しも同じ型で表す</summary>
    public class PlacedObjectNode
    {
        public readonly int id;
        public readonly string label;
        /// <summary>行を選んだときに選択する GameObject。カテゴリ見出しは null</summary>
        public readonly GameObject target;
        public readonly bool isCategory;
        public readonly List<PlacedObjectNode> children = new List<PlacedObjectNode>();
        public PlacedObjectNode parent { get; private set; }

        public PlacedObjectNode(int id, string label, GameObject target, bool isCategory)
        {
            this.id = id;
            this.label = label;
            this.target = target;
            this.isCategory = isCategory;
        }

        public void AddChild(PlacedObjectNode child)
        {
            child.parent = this;
            children.Add(child);
        }
    }

    /// <summary>
    /// 配置物の平坦な一覧から、カテゴリ見出しを根とする木を組み立てる。
    /// アタッチしたモデル (親 = メイド) や背景モデルの入れ子は parentId で表し、カテゴリをまたいで付けられる。
    /// Unity の実体に触れないので、組み立て・変化検知・祖先の引き当てをテストで固定できる
    /// </summary>
    public class PlacedObjectTree
    {
        public const int NoParent = 0;

        // カテゴリ見出しの ID。GetInstanceID は正負どちらも取るが int.MinValue 付近は実際には使われないため、
        // ここに寄せて配置物の ID との衝突を避ける
        private const int CategoryIdBase = int.MinValue;

        private static readonly int CategoryCount = Enum.GetValues(typeof(PlacedObjectCategory)).Length;

        private readonly List<PlacedObjectNode> _roots = new List<PlacedObjectNode>();
        private readonly Dictionary<int, PlacedObjectNode> _nodeMap = new Dictionary<int, PlacedObjectNode>();

        /// <summary>カテゴリ見出しの並び (中身のあるカテゴリだけ)</summary>
        public List<PlacedObjectNode> roots => _roots;

        private PlacedObjectTree()
        {
        }

        public static int GetCategoryId(PlacedObjectCategory category)
        {
            return CategoryIdBase + (int)category;
        }

        public static string GetCategoryName(PlacedObjectCategory category)
        {
            switch (category)
            {
                case PlacedObjectCategory.Maid: return "メイド";
                case PlacedObjectCategory.Model: return "モデル";
                case PlacedObjectCategory.Background: return "背景";
                case PlacedObjectCategory.Png: return "PNG";
                case PlacedObjectCategory.Light: return "ライト";
                case PlacedObjectCategory.SubCamera: return "サブカメラ";
                default: return category.ToString();
            }
        }

        public static PlacedObjectTree Build(IList<PlacedObjectSource> sources)
        {
            var tree = new PlacedObjectTree();
            if (sources == null)
            {
                return tree;
            }

            // 同じ ID が二度来たら (同じ GameObject を複数経路で提供された等) 最初の 1 件だけ採る。
            // GUITreeView は ID で展開状態を持つため、重複を残すと片方の開閉がもう片方にも効いてしまう
            var accepted = new List<PlacedObjectSource>(sources.Count);
            foreach (var source in sources)
            {
                if (source == null || tree._nodeMap.ContainsKey(source.id))
                {
                    continue;
                }
                tree._nodeMap[source.id] = new PlacedObjectNode(source.id, source.label ?? "", source.target, false);
                accepted.Add(source);
            }

            // 親へ付ける。親が一覧に無い・自分自身・付けると循環するものはカテゴリ直下へ置き、行を失わない
            var categoryItems = new List<PlacedObjectNode>[CategoryCount];
            for (var i = 0; i < CategoryCount; i++)
            {
                categoryItems[i] = new List<PlacedObjectNode>();
            }

            foreach (var source in accepted)
            {
                var node = tree._nodeMap[source.id];
                PlacedObjectNode parent;
                if (source.parentId != NoParent &&
                    tree._nodeMap.TryGetValue(source.parentId, out parent) &&
                    !IsSelfOrAncestor(node, parent))
                {
                    parent.AddChild(node);
                }
                else
                {
                    categoryItems[(int)source.category].Add(node);
                }
            }

            for (var i = 0; i < CategoryCount; i++)
            {
                var items = categoryItems[i];
                if (items.Count == 0)
                {
                    continue;
                }

                var category = (PlacedObjectCategory)i;
                var header = new PlacedObjectNode(
                    GetCategoryId(category), GetCategoryName(category) + " (" + items.Count + ")", null, true);
                foreach (var item in items)
                {
                    header.AddChild(item);
                }
                tree._roots.Add(header);
                tree._nodeMap[header.id] = header;
            }

            return tree;
        }

        /// <summary>candidate から親をたどって node に行き着くか (= candidate の子にすると循環する)</summary>
        private static bool IsSelfOrAncestor(PlacedObjectNode node, PlacedObjectNode candidate)
        {
            for (var p = candidate; p != null; p = p.parent)
            {
                if (p == node)
                {
                    return true;
                }
            }
            return false;
        }

        public bool Contains(int id)
        {
            return _nodeMap.ContainsKey(id);
        }

        /// <summary>id の行を出すために展開する祖先の ID をルート側から並べる。一覧に無ければ空</summary>
        public List<int> GetAncestorIds(int id)
        {
            var result = new List<int>();
            PlacedObjectNode node;
            if (!_nodeMap.TryGetValue(id, out node))
            {
                return result;
            }

            for (var p = node.parent; p != null; p = p.parent)
            {
                result.Add(p.id);
            }
            result.Reverse();
            return result;
        }

        /// <summary>
        /// 前回集めた一覧と同じか。1 項目でも違えば組み直す。
        /// target は Unity の == (ネイティブ呼び出し) を避けて参照で比べる。
        /// 破棄された GameObject は id が変わらないが、行は GUITreeView の isAlive で消える
        /// </summary>
        public static bool SameSources(IList<PlacedObjectSource> a, IList<PlacedObjectSource> b)
        {
            var countA = a != null ? a.Count : 0;
            var countB = b != null ? b.Count : 0;
            if (countA != countB)
            {
                return false;
            }

            for (var i = 0; i < countA; i++)
            {
                var x = a[i];
                var y = b[i];
                if (ReferenceEquals(x, y))
                {
                    continue;
                }
                if (x == null || y == null ||
                    x.category != y.category ||
                    x.id != y.id ||
                    x.parentId != y.parentId ||
                    !string.Equals(x.label, y.label, StringComparison.Ordinal) ||
                    !ReferenceEquals(x.target, y.target))
                {
                    return false;
                }
            }
            return true;
        }
    }
}
