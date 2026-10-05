using System;
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// スタジオモードの脱衣ウィンドウ相当。
    /// カテゴリ単位の脱衣 (マスク切替) とめくれ・ずらし・はだけの切替を行う。
    /// ノード表示タブでは体の部位 (body のボーン) ごとの表示/非表示を上書きする
    /// </summary>
    public class MaidUndressWindow : MaidWindowBase
    {
        public static readonly int WINDOW_ID = 8903369;

        private static readonly int TAB_WIDTH = 100;

        /// <summary>ノード表示タブの一括操作ボタンの幅</summary>
        private const int BULK_BUTTON_WIDTH = 90;

        /// <summary>ノード一覧の 1 段あたりのインデント幅</summary>
        private const float NODE_INDENT_WIDTH = 12f;

        /// <summary>ウィンドウ内の内部タブ</summary>
        private enum UndressTabType
        {
            脱衣,
            ノード表示,
        }

        private UndressTabType _tabType = UndressTabType.脱衣;

        protected override int windowId => WINDOW_ID;
        protected override string windowTitle => "脱衣";

        private static MaidUndressWindow _instance = null;
        public static MaidUndressWindow instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new MaidUndressWindow();
                }
                return _instance;
            }
        }

        private MaidUndressWindow()
        {
        }

        protected override void LoadPlacement(out int x, out int y, out int width, out int height)
        {
            x = config.maidUndressPosX;
            y = config.maidUndressPosY;
            width = config.maidUndressWidth;
            height = config.maidUndressHeight;
        }

        protected override void StorePlacement(int x, int y, int width, int height)
        {
            config.maidUndressPosX = x;
            config.maidUndressPosY = y;
            config.maidUndressWidth = width;
            config.maidUndressHeight = height;
        }

        public override bool savedVisible
        {
            get => config.maidUndressVisible;
            set => config.maidUndressVisible = value;
        }

        public override bool TryFocusTimelineLayer(Type layerType)
        {
            if (layerType == typeof(MTEP.UndressTimelineLayer))
            {
                _tabType = UndressTabType.脱衣;
                return true;
            }
            if (layerType == typeof(MTEP.NodeVisibilityTimelineLayer))
            {
                _tabType = UndressTabType.ノード表示;
                return true;
            }
            return false;
        }

        protected override void DrawMaidContent(Maid target)
        {
            if (target == null)
            {
                return;
            }
            if (target.body0 == null)
            {
                view.DrawLabel("ボディの読み込みを待っています", -1, ROW_HEIGHT);
                return;
            }

            _tabType = DrawInnerTabs(_tabType, TAB_WIDTH);

            view.DrawHorizontalLine(Color.gray);
            view.AddSpace(5);

            // タブ切替はゲートの対象外にするため、タブを描いた後で判定する
            if (_tabType == UndressTabType.ノード表示)
            {
                TimelineLayerGate.Begin(view, typeof(MTEP.NodeVisibilityTimelineLayer), target, ROW_HEIGHT);
                DrawNodeVisibility(view, target);
                return;
            }

            TimelineLayerGate.Begin(view, typeof(MTEP.UndressTimelineLayer), target, ROW_HEIGHT);

            DrawAllButtons(view, target);
            DrawCategoryList(view, target);
        }

        /// <summary>全脱衣・全着衣の一括操作行</summary>
        private void DrawAllButtons(GUIView view, Maid target)
        {
            view.BeginHorizontal();
            {
                if (view.DrawButton("全脱衣", 90, ROW_HEIGHT))
                {
                    HistoryManager.instance.BeforeEdit(target, HistoryScope.Undress, "全脱衣");
                    MaidUndressController.SetAllUndressed(target, true);
                }
                if (view.DrawButton("全着衣", 90, ROW_HEIGHT))
                {
                    HistoryManager.instance.BeforeEdit(target, HistoryScope.Undress, "全着衣");
                    MaidUndressController.SetAllUndressed(target, false);
                }
            }
            view.EndLayout();

            view.DrawHorizontalLine(Color.gray);
            view.AddSpace(5);
        }

        private void DrawCategoryList(GUIView view, Maid target)
        {
            // 最後の要素なので高さ -1（残り全部）でウィンドウの伸縮に追従させる
            view.BeginScrollView(-1, -1, GUIView.AutoScrollViewRect, false, true);

            // トグル ON = 脱衣中 (対象スロットをマスクで非表示)
            DrawToggleRows(view, MaidUndressController.categories,
                category => MaidUndressController.IsVisible(target, category),
                category =>
                {
                    var enabled = MaidUndressController.IsEnabled(target, category);
                    var undressed = enabled && MaidUndressController.IsUndressed(target, category);
                    view.DrawToggle(category.name, undressed, TOGGLE_WIDTH, ROW_HEIGHT, enabled,
                        value =>
                        {
                            HistoryManager.instance.BeforeEdit(target, HistoryScope.Undress,
                                "脱衣: " + category.name);
                            MaidUndressController.SetUndressed(target, category, value);
                        });
                });

            view.DrawHorizontalLine(Color.gray);

            DrawCostumeChangeList(view, target);

            view.EndScrollView();
        }

        /// <summary>めくれ・ずらし・はだけの切替。排他制御は mekureController 側が行う</summary>
        private void DrawCostumeChangeList(GUIView view, Maid target)
        {
            DrawToggleRows(view, MaidUndressController.CostumeTypes, _ => true, type =>
            {
                var enabled = MaidUndressController.IsCostumeTypeEnabled(target, type);
                var isOn = enabled && target.mekureController.IsEnabledCostumeType(type);
                var typeName = MaidUndressController.GetCostumeTypeName(type);
                view.DrawToggle(typeName, isOn,
                    TOGGLE_WIDTH, ROW_HEIGHT, enabled,
                    value =>
                    {
                        HistoryManager.instance.BeforeEdit(target, HistoryScope.Undress,
                            "脱衣: " + typeName);
                        target.mekureController.SetEnabledCostumeType(type, value);
                    });
            });
        }

        /// <summary>ノード表示タブ。一括操作行と、階層の深さでインデントしたノード一覧</summary>
        private void DrawNodeVisibility(GUIView view, Maid target)
        {
            if (!target.body0.isLoadedBody)
            {
                view.DrawLabel("ボディの読み込みを待っています", -1, ROW_HEIGHT);
                return;
            }

            view.BeginHorizontal();
            {
                if (view.DrawButton("全表示", BULK_BUTTON_WIDTH, ROW_HEIGHT))
                {
                    NodeVisibilityRowDrawer.RecordEdit(target, "全表示");
                    MaidNodeVisibilityController.SetAll(target, true);
                    MaidNodeVisibilityController.Flush(target);
                }
                if (view.DrawButton("全非表示", BULK_BUTTON_WIDTH, ROW_HEIGHT))
                {
                    NodeVisibilityRowDrawer.RecordEdit(target, "全非表示");
                    MaidNodeVisibilityController.SetAll(target, false);
                    MaidNodeVisibilityController.Flush(target);
                }
                if (view.DrawButton("リセット", BULK_BUTTON_WIDTH, ROW_HEIGHT,
                    MaidNodeVisibilityController.HasOverrides(target)))
                {
                    NodeVisibilityRowDrawer.RecordEdit(target, "リセット");
                    MaidNodeVisibilityController.ClearAll(target);
                    MaidNodeVisibilityController.Flush(target);
                }
            }
            view.EndLayout();

            view.DrawHorizontalLine(Color.gray);
            view.AddSpace(5);

            // 最後の要素なので高さ -1（残り全部）でウィンドウの伸縮に追従させる
            view.BeginScrollView(-1, -1, GUIView.AutoScrollViewRect, false, true);
            foreach (var node in MaidNodeVisibilityNodes.nodes)
            {
                NodeVisibilityRowDrawer.Draw(view, target, node, ROW_HEIGHT, node.depth * NODE_INDENT_WIDTH);
            }
            view.EndScrollView();
        }

        private const int TOGGLE_WIDTH = 115;

        /// <summary>ビューの幅に収まる列数でトグルを折り返して描画する</summary>
        private static void DrawToggleRows<T>(
            GUIView view, IEnumerable<T> items, Func<T, bool> isVisible, Action<T> drawItem)
        {
            // n 列の占有幅は n * TOGGLE_WIDTH + (n - 1) * margin
            var availableWidth = view.isScrollViewEnabled
                ? view.scrollViewContentRect.width
                : view.viewRect.width - view.padding.x * 2;
            var columns = Mathf.Max(1,
                (int)((availableWidth + view.margin) / (TOGGLE_WIDTH + view.margin)));

            var drawnInRow = 0;
            foreach (var item in items)
            {
                if (!isVisible(item))
                {
                    continue;
                }
                if (drawnInRow == 0)
                {
                    view.BeginHorizontal();
                }
                drawItem(item);
                if (++drawnInRow >= columns)
                {
                    view.EndLayout();
                    drawnInRow = 0;
                }
            }
            if (drawnInRow > 0)
            {
                view.EndLayout();
            }
        }
    }
}
