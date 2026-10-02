using System.Collections.Generic;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    public class BoneMenuManager : ManagerBase
    {
        private static BoneMenuManager _instance = null;
        public static BoneMenuManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new BoneMenuManager();
                }
                return _instance;
            }
        }

        private List<IBoneMenuItem> allMenuItems => currentLayer.allMenuItems;

        private BoneMenuManager()
        {
        }

        public override void Init()
        {
        }

        /// <summary>全レイヤーのメニュー選択を解除する (行の選択ハイライトはレイヤーをまたぐため)</summary>
        public void UnselectAll()
        {
            foreach (var layer in timelineManager.layers)
            {
                foreach (var setMenuItem in layer.allMenuItems)
                {
                    setMenuItem.isSelectedMenu = false;
                }
            }
        }

        /// <summary>
        /// 指定レイヤーが選択状態か (BoneSetMenuItem と同じく、全項目が選択済みなら選択扱い)。
        /// 項目を持たないレイヤーは非選択とする
        /// </summary>
        public bool IsLayerMenuSelected(ITimelineLayer layer)
        {
            var menuItems = layer.allMenuItems;
            if (menuItems.Count == 0)
            {
                return false;
            }

            foreach (var menuItem in menuItems)
            {
                if (!menuItem.isSelectedMenu)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// 折りたたみ中のレイヤー名の行を選択表示にするか。IsMenuHighlighted と同じく、
        /// 畳まれていれば項目が 1 つでも選択されていれば選択扱いにする
        /// </summary>
        public bool IsLayerMenuHighlighted(ITimelineLayer layer, bool isCollapsed)
        {
            if (IsLayerMenuSelected(layer))
            {
                return true;
            }
            return isCollapsed && HasSelectedLeaf(layer.allMenuItems);
        }

        /// <summary>
        /// 指定レイヤーの全メニュー項目の選択を切り替える (レイヤー名クリック用)。
        /// 表示 (IsLayerMenuHighlighted) が選択なら解除、そうでなければ全選択する
        /// </summary>
        public void SelectLayerMenuItems(ITimelineLayer layer, bool isCollapsed, bool isMultiSelect)
        {
            var menuItems = layer.allMenuItems;
            if (menuItems.Count == 0)
            {
                return;
            }

            var prevSelected = IsLayerMenuHighlighted(layer, isCollapsed);

            if (!isMultiSelect)
            {
                UnselectAll();
            }

            foreach (var menuItem in menuItems)
            {
                menuItem.isSelectedMenu = !prevSelected;
            }
        }

        /// <summary>
        /// メニュー行を選択表示にするか。折りたたみ中のグループは子が 1 つでも選択されていれば
        /// 選択扱いにする (隠れた子の選択が見えず解除できなくなるのを防ぐ)。
        /// 描画ループから毎フレーム呼ばれるため、グループの isSelectedMenu (LINQ の All) は使わず
        /// 子を 1 回だけ走査する
        /// </summary>
        public static bool IsMenuHighlighted(IBoneMenuItem menuItem)
        {
            var children = menuItem.children;
            if (!menuItem.isSetMenu || children == null)
            {
                return menuItem.isSelectedMenu;
            }

            // 子が無いグループは isSelectedMenu (All) と同じく選択扱い
            var isAllSelected = true;
            var isAnySelected = false;
            foreach (var child in children)
            {
                if (child.isSelectedMenu)
                {
                    isAnySelected = true;
                }
                else
                {
                    isAllSelected = false;
                }
            }
            return isAllSelected || (isAnySelected && !menuItem.isOpenMenu);
        }

        /// <summary>グループの子を含め、選択中の項目が 1 つでもあるか</summary>
        private static bool HasSelectedLeaf(List<IBoneMenuItem> menuItems)
        {
            foreach (var menuItem in menuItems)
            {
                if (menuItem.children == null)
                {
                    if (menuItem.isSelectedMenu)
                    {
                        return true;
                    }
                    continue;
                }
                foreach (var child in menuItem.children)
                {
                    if (child.isSelectedMenu)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private List<IBoneMenuItem> _visibleItems = new List<IBoneMenuItem>(128);

        public List<IBoneMenuItem> GetVisibleItems()
        {
            _visibleItems.Clear();
            CollectVisibleItems(allMenuItems, _visibleItems);
            return _visibleItems;
        }

        /// <summary>
        /// 指定フレームにレイヤーの全メニュー項目のボーンが揃っているか。
        /// 折りたたみ中のレイヤー行が BoneSetMenuItem.IsFullBones と同じ基準で灰色表示するために使う。
        /// 項目を持たないレイヤーは true (通常色) を返す (IsLayerMenuSelected とは逆)。
        /// 描画ループから毎フレーム呼ばれるため LINQ (デリゲート生成) を避けている
        /// </summary>
        public static bool IsLayerFullBones(ITimelineLayer layer, FrameData frame)
        {
            foreach (var menuItem in layer.allMenuItems)
            {
                if (!menuItem.IsFullBones(frame))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// 指定レイヤーの可視メニュー項目を result へ追記する。
        /// 複数レイヤー表示の行リスト構築用
        /// </summary>
        public void GetVisibleItems(ITimelineLayer layer, List<IBoneMenuItem> result)
        {
            CollectVisibleItems(layer.allMenuItems, result);
        }

        /// <summary>ボーンセットとその子から可視項目だけを result へ追記する</summary>
        private static void CollectVisibleItems(
            List<IBoneMenuItem> source, List<IBoneMenuItem> result)
        {
            foreach (var setMenuItem in source)
            {
                if (setMenuItem.isVisibleMenu)
                {
                    result.Add(setMenuItem);
                }

                if (setMenuItem.children == null)
                {
                    continue;
                }

                foreach (var menuItem in setMenuItem.children)
                {
                    if (menuItem.isVisibleMenu)
                    {
                        result.Add(menuItem);
                    }
                }
            }
        }

        private List<IBoneMenuItem> _selectedItems = new List<IBoneMenuItem>(128);

        public List<IBoneMenuItem> GetSelectedItems()
        {
            _selectedItems.Clear();

            foreach (var setMenuItem in allMenuItems)
            {
                if (setMenuItem.isSelectedMenu)
                {
                    _selectedItems.Add(setMenuItem);
                }

                if (setMenuItem.children == null)
                {
                    continue;
                }

                foreach (var menuItem in setMenuItem.children)
                {
                    if (menuItem.isSelectedMenu)
                    {
                        _selectedItems.Add(menuItem);
                    }
                }
            }
            return _selectedItems;
        }
    }
}