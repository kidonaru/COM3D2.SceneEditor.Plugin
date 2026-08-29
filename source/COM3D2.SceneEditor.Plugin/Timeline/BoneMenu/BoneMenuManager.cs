using System.Collections.Generic;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    public class BoneMenuManager : ManagerBase
    {
        private List<IBoneMenuItem> easyMenuItems = null;

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
            if (easyMenuItems == null)
            {
                easyMenuItems = new List<IBoneMenuItem>
                {
                    new EasyMenuItem()
                };
            }
        }

        public void UnselectAll()
        {
            foreach (var setMenuItem in allMenuItems)
            {
                setMenuItem.isSelectedMenu = false;
            }
        }

        private List<IBoneMenuItem> _visibleItems = new List<IBoneMenuItem>(128);

        public List<IBoneMenuItem> GetVisibleItems()
        {
            if (config.isEasyEdit)
            {
                return easyMenuItems;
            }

            _visibleItems.Clear();
            CollectVisibleItems(allMenuItems, _visibleItems);
            return _visibleItems;
        }

        /// <summary>
        /// 指定レイヤーの可視メニュー項目を result へ追記する。
        /// 複数レイヤー表示の行リスト構築用 (isEasyEdit の分岐は呼び出し側が行う)
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
            if (config.isEasyEdit)
            {
                return easyMenuItems;
            }

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