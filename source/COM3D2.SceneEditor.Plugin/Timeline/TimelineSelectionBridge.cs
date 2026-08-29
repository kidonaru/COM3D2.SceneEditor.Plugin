using System.Collections.Generic;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// タイムラインのメニュー項目選択と SelectionManager の双方向同期。
    /// - 順方向: メニュー選択の変化を検知し、Inspector のボーン/IK 選択を降格して
    ///   項目表示(TimelineItemInspector)が見えるようにする
    /// - 逆方向: SelectionManager の変化をポーリングで検知し、プロバイダの逆引きで
    ///   該当メニュー項目を選択状態にする
    /// ボーン選択の変化はイベントが無いためポーリングで検知する
    /// </summary>
    public class TimelineSelectionBridge : MTEP.ManagerBase
    {
        private static TimelineSelectionBridge _instance = null;
        public static TimelineSelectionBridge instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new TimelineSelectionBridge();
                }
                return _instance;
            }
        }

        private TimelineSelectionBridge()
        {
        }

        // 逆方向検知用の前回値
        private GameObject _lastSelectedObject = null;
        private BoneSliderDef _lastBoneDef = null;

        // 順方向検知用のメニュー選択スナップショット (現在レイヤーの選択集合)
        private MTEP.ITimelineLayer _lastLayer = null;
        private readonly List<MTEP.IBoneMenuItem> _lastSelectedItems =
            new List<MTEP.IBoneMenuItem>(64);

        // 逆方向同期でメニューを書き換えた直後は順方向の降格を 1 回抑止する
        private bool _suppressForwardOnce = false;

        public override void Update()
        {
            if (timelineManager.timeline == null || timelineManager.currentLayer == null)
            {
                return;
            }

            UpdateReverseSync();
            UpdateForwardSync();
        }

        /// <summary>SelectionManager の変化 → メニュー選択</summary>
        private void UpdateReverseSync()
        {
            var selection = SelectionManager.instance;
            var selectedObject = selection.selectedObject;
            var boneDef = selection.selectedBoneDef;

            if (selectedObject == _lastSelectedObject && boneDef == _lastBoneDef)
            {
                return;
            }
            _lastSelectedObject = selectedObject;
            _lastBoneDef = boneDef;

            var layer = timelineManager.currentLayer;
            var provider = TimelineItemInspectorRegistry.Find(layer);
            if (provider == null)
            {
                return;
            }

            var itemName = provider.FindItemName(layer);
            if (itemName == null)
            {
                return;
            }

            var item = FindMenuItem(layer, itemName);
            if (item == null || item.isSelectedMenu)
            {
                return;
            }

            boneMenuManager.UnselectAll();
            // MaidBoneMenuItem のボーン回転表示連動は SceneEditorHack では発火しない
            // (HasBoneRotateVisible が既定 false のため単純フラグとして動く)
            item.isSelectedMenu = true;
            _suppressForwardOnce = true;
        }

        /// <summary>メニュー選択の変化 → Inspector のボーン/IK 選択を降格</summary>
        private void UpdateForwardSync()
        {
            var layer = timelineManager.currentLayer;
            var selectedItems = boneMenuManager.GetSelectedItems();

            if (!MenuSelectionChanged(layer, selectedItems))
            {
                return;
            }

            _lastLayer = layer;
            _lastSelectedItems.Clear();
            _lastSelectedItems.AddRange(selectedItems);

            var suppress = _suppressForwardOnce;
            _suppressForwardOnce = false;
            if (suppress || selectedItems.Count == 0)
            {
                return;
            }

            var selection = SelectionManager.instance;
            if (selection.hasBoneSelection || selection.hasIKSelection)
            {
                // 同一オブジェクトの再選択でボーン/IK 選択だけを解除する
                // (SelectionManager.Select は同値早期 return の前にボーン/IK を解除する)
                selection.Select(selection.selectedObject);
            }
        }

        private bool MenuSelectionChanged(
            MTEP.ITimelineLayer layer, List<MTEP.IBoneMenuItem> selectedItems)
        {
            if (layer != _lastLayer || selectedItems.Count != _lastSelectedItems.Count)
            {
                return true;
            }
            for (var i = 0; i < selectedItems.Count; i++)
            {
                if (!ReferenceEquals(selectedItems[i], _lastSelectedItems[i]))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>現在レイヤーのメニュー項目(セットの子を含む)を名前で探す</summary>
        private static MTEP.IBoneMenuItem FindMenuItem(
            MTEP.ITimelineLayer layer, string itemName)
        {
            foreach (var item in layer.allMenuItems)
            {
                if (!item.isSetMenu && item.name == itemName)
                {
                    return item;
                }
                if (item.children == null)
                {
                    continue;
                }
                foreach (var child in item.children)
                {
                    if (child.name == itemName)
                    {
                        return child;
                    }
                }
            }
            return null;
        }
    }
}
