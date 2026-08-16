using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// スタジオモードの重力ウィンドウ相当。
    /// 髪・スカートの揺れものにかかる力の向きをカテゴリ単位で編集する
    /// </summary>
    public class MaidGravityWindow : MaidWindowBase
    {
        public static readonly int WINDOW_ID = 8903380;

        private static readonly int TAB_WIDTH = 80;

        /// <summary>選択中のカテゴリ。カテゴリ定義が変わっても壊れないよう id で保持する</summary>
        private string _selectedCategoryId = null;

        protected override int windowId => WINDOW_ID;
        protected override string windowTitle => "重力";

        private static MaidGravityWindow _instance = null;
        public static MaidGravityWindow instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new MaidGravityWindow();
                }
                return _instance;
            }
        }

        private MaidGravityWindow()
        {
        }

        private static MaidGravityController gravityController
            => maidManager.gravityController;

        protected override void LoadPlacement(out int x, out int y, out int width, out int height)
        {
            x = config.maidGravityPosX;
            y = config.maidGravityPosY;
            width = config.maidGravityWidth;
            height = config.maidGravityHeight;
        }

        protected override void StorePlacement(int x, int y, int width, int height)
        {
            config.maidGravityPosX = x;
            config.maidGravityPosY = y;
            config.maidGravityWidth = width;
            config.maidGravityHeight = height;
        }

        public override bool savedVisible
        {
            get => config.maidGravityVisible;
            set => config.maidGravityVisible = value;
        }

        protected override void DrawMaidContent(Maid target)
        {
            if (target == null)
            {
                return;
            }
            if (target.body0 == null || !target.body0.isLoadedBody)
            {
                view.DrawLabel("ボディの読み込みを待っています", -1, ROW_HEIGHT);
                return;
            }

            var category = DrawCategoryTabs();

            // 最後の要素なので高さ -1（残り全部）でウィンドウの伸縮に追従させる
            view.BeginScrollView(-1, -1, GUIView.AutoScrollViewRect, false, true);

            DrawCategory(target, category);

            view.EndScrollView();
        }

        /// <summary>
        /// カテゴリをタブとして描き、選択中のカテゴリを返す。
        /// カテゴリはデータ定義（enum ではない）なので DrawInnerTabs は使わず自前で並べる。
        /// カテゴリは少数固定の前提で、DrawTabs のような折り返しは行わない
        /// </summary>
        private GravityCategory DrawCategoryTabs()
        {
            var categories = MaidGravityController.categories;

            // 初回表示、または定義から消えた id が残っている場合は先頭へ戻す。
            // 描画より先に確定させないと 1 フレームどのタブも点灯しない
            var selected = MaidGravityController.FindCategory(_selectedCategoryId);
            if (selected == null)
            {
                selected = categories[0];
                _selectedCategoryId = selected.id;
            }

            view.BeginHorizontal();
            {
                foreach (var item in categories)
                {
                    var color = item == selected ? GUIView.option.accentColor : Color.white;
                    if (view.DrawButton(item.name, TAB_WIDTH, ROW_HEIGHT, true, color))
                    {
                        _selectedCategoryId = item.id;
                        selected = item;
                    }
                }
            }
            view.EndLayout();

            return selected;
        }

        private void DrawCategory(Maid target, GravityCategory category)
        {
            if (!gravityController.IsValid(target, category))
            {
                // 着ていない・揺れものを持たない衣装では力の掛け先が無い
                view.DrawLabel("対象の揺れものがありません", -1, ROW_HEIGHT);
                return;
            }

            view.BeginHorizontal();
            {
                view.DrawToggle("有効", gravityController.GetEnabled(target, category),
                    80, ROW_HEIGHT, true,
                    value =>
                    {
                        RecordEdit(target, category, "有効");
                        gravityController.SetEnabled(target, category, value);
                    });

                if (view.DrawButton("リセット", 80, ROW_HEIGHT))
                {
                    RecordEdit(target, category, "リセット");
                    gravityController.SetOffset(target, category, Vector3.zero);
                }
            }
            view.EndLayout();

            var offset = gravityController.GetOffset(target, category);
            DrawAxisSlider(target, category, "X", offset.x,
                value =>
                {
                    var current = gravityController.GetOffset(target, category);
                    current.x = value;
                    gravityController.SetOffset(target, category, current);
                });
            DrawAxisSlider(target, category, "Y", offset.y,
                value =>
                {
                    var current = gravityController.GetOffset(target, category);
                    current.y = value;
                    gravityController.SetOffset(target, category, current);
                });
            DrawAxisSlider(target, category, "Z", offset.z,
                value =>
                {
                    var current = gravityController.GetOffset(target, category);
                    current.z = value;
                    gravityController.SetOffset(target, category, current);
                });
        }

        /// <summary>共通書式のスライダー 1 行（LightWindow と同形式）</summary>
        private void DrawAxisSlider(
            Maid target, GravityCategory category, string label, float value,
            System.Action<float> onChanged)
        {
            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = label,
                labelWidth = 20,
                width = -1,
                min = -1f,
                max = 1f,
                step = 0.01f,
                defaultValue = 0f,
                value = value,
                onChanged = newValue =>
                {
                    RecordEdit(target, category, label);
                    onChanged(newValue);
                },
            });
        }

        /// <summary>重力操作を履歴へ記録する。ドラッグ中の連続変更は 1 件に集約される</summary>
        private static void RecordEdit(Maid target, GravityCategory category, string label)
        {
            HistoryManager.instance.BeforeEdit(target, HistoryScope.Gravity,
                "重力: " + category.name + " " + label);
        }
    }
}
