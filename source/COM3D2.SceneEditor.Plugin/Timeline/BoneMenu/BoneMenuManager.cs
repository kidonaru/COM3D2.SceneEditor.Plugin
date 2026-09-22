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
        /// 指定レイヤーの全メニュー項目の選択を切り替える (レイヤー名クリック用)。
        /// 単体項目のクリックと同じく、全選択済みなら解除、そうでなければ全選択する
        /// </summary>
        public void SelectLayerMenuItems(ITimelineLayer layer, bool isMultiSelect)
        {
            var menuItems = layer.allMenuItems;
            if (menuItems.Count == 0)
            {
                return;
            }

            var prevSelected = IsLayerMenuSelected(layer);

            if (!isMultiSelect)
            {
                UnselectAll();
            }

            foreach (var menuItem in menuItems)
            {
                menuItem.isSelectedMenu = !prevSelected;
            }
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