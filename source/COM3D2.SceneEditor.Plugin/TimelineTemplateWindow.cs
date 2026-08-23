using System;
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// キーフレームテンプレートの操作・編集ウィンドウ。
    /// MTE の TimelineTemplateUI (操作 / カテゴリ編集 / テンプレ編集の 3 タブ) を移植。
    /// TimelineWindow のコントロールパネルの「テンプレ」ボタンから開く
    /// </summary>
    public class TimelineTemplateWindow : EditorSubWindow
    {
        public static readonly int WINDOW_ID = 8903388;

        protected override int windowId => WINDOW_ID;
        protected override string windowTitle => "テンプレート";

        private static readonly int ROW_HEIGHT = 20;
        private static readonly int TAB_WIDTH = 70;

        private enum TabType
        {
            操作,
            ｶﾃｺﾞﾘ編集,
            ﾃﾝﾌﾟﾚ編集,
        }

        private TabType _tabType = TabType.操作;

        private readonly GUIView _view = new GUIView();

        /// <summary>カテゴリごとの新規テンプレ名の入力途中の値</summary>
        private readonly Dictionary<string, string> _newTemplateNames = new Dictionary<string, string>();
        private string _newCategoryName = "";

        private static MTEP.TimelineManager timelineManager => MTEP.TimelineManager.instance;
        private static MTEP.TimelineData timeline => timelineManager.timeline;
        private static MTEP.ITimelineLayer currentLayer => timelineManager.currentLayer;
        private static MTEP.TimelineTemplateManager templateManager => MTEP.TimelineTemplateManager.instance;

        private static TimelineTemplateWindow _instance = null;
        public static TimelineTemplateWindow instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new TimelineTemplateWindow();
                }
                return _instance;
            }
        }

        private TimelineTemplateWindow()
        {
        }

        protected override void LoadPlacement(out int x, out int y, out int width, out int height)
        {
            x = config.timelineTemplatePosX;
            y = config.timelineTemplatePosY;
            width = config.timelineTemplateWidth;
            height = config.timelineTemplateHeight;
        }

        protected override void StorePlacement(int x, int y, int width, int height)
        {
            config.timelineTemplatePosX = x;
            config.timelineTemplatePosY = y;
            config.timelineTemplateWidth = width;
            config.timelineTemplateHeight = height;
        }

        public override bool savedVisible
        {
            get => config.timelineTemplateVisible;
            set => config.timelineTemplateVisible = value;
        }

        protected override void DrawContent()
        {
            _view.Init(ToLocalRect(contentRect));

            if (timeline == null)
            {
                _view.DrawLabel("タイムラインが読み込まれていません", -1, ROW_HEIGHT, Color.yellow);
                return;
            }

            _tabType = _view.DrawTabs(_tabType, TAB_WIDTH, ROW_HEIGHT);

            _view.DrawHorizontalLine(Color.gray);

            _view.AddSpace(5);

            _view.BeginScrollView();

            switch (_tabType)
            {
                case TabType.操作:
                    DrawControl(_view);
                    break;
                case TabType.ｶﾃｺﾞﾘ編集:
                    DrawCategory(_view);
                    break;
                case TabType.ﾃﾝﾌﾟﾚ編集:
                    DrawTemplate(_view);
                    break;
            }

            _view.EndScrollView();
        }

        /// <summary>「操作」タブ: テンプレ一覧の適用と新規保存</summary>
        private void DrawControl(GUIView view)
        {
            var info = timelineManager.GetLayerInfo(currentLayer?.layerType);
            view.DrawLabel($"{info?.displayName ?? ""} テンプレ", -1, ROW_HEIGHT);

            view.DrawHorizontalLine();

            var templateLayer = templateManager.GetTemplateLayer();
            if (templateLayer == null)
            {
                view.DrawLabel("テンプレートレイヤーが存在しません。", -1, ROW_HEIGHT);
                return;
            }

            var categories = templateLayer.categories.ToArray();
            foreach (var category in categories)
            {
                view.DrawLabel($"[{category.categoryName}]", 200, ROW_HEIGHT);

                view.BeginHorizontal();

                foreach (var template in category.templates)
                {
                    if (template.nameWidth < 0f)
                    {
                        template.nameWidth = GUIView.CalcWidth(GUIView.gsButton, template.templateName);
                    }

                    // 折り返し: ボタンが右端を超える場合は次の行へ
                    if (view.currentPos.x + template.nameWidth > view.viewRect.width)
                    {
                        view.EndLayout();
                        view.BeginHorizontal();
                    }

                    if (view.DrawButton(template.templateName, template.nameWidth, ROW_HEIGHT))
                    {
                        category.ApplyTemplate(template.templateName);
                    }
                }

                view.EndLayout();

                DrawNewTemplateField(view, category);

                view.DrawHorizontalLine();
            }
        }

        /// <summary>「カテゴリ編集」タブ: カテゴリの追加・改名・並べ替え・削除</summary>
        private void DrawCategory(GUIView view)
        {
            view.BeginHorizontal();
            {
                view.DrawTextField(new GUIView.TextFieldOption
                {
                    label = "カテゴリ名",
                    labelWidth = 70,
                    width = 200,
                    value = _newCategoryName,
                    onChanged = value => _newCategoryName = value,
                    hiddenButton = true,
                });

                if (view.DrawButton("追加", 45, ROW_HEIGHT, !string.IsNullOrEmpty(_newCategoryName)))
                {
                    if (templateManager.AddTemplateCategory(_newCategoryName))
                    {
                        _newCategoryName = "";
                    }
                }
            }
            view.EndLayout();

            view.DrawHorizontalLine();

            var templateLayer = templateManager.GetTemplateLayer();
            if (templateLayer == null)
            {
                view.DrawLabel("テンプレートレイヤーが存在しません。", -1, ROW_HEIGHT);
                return;
            }

            var categories = templateLayer.categories.ToArray();
            foreach (var category in categories)
            {
                view.BeginHorizontal();
                {
                    view.DrawTextField(new GUIView.TextFieldOption
                    {
                        width = 150,
                        value = category.categoryName,
                        onChanged = value =>
                        {
                            category.categoryName = value;
                            category.dirty = true;
                        },
                        disabled = category.categoryName == "Default",
                        hiddenButton = true,
                    });

                    if (view.DrawButton("削除", 45, ROW_HEIGHT, category.categoryName != "Default"))
                    {
                        templateLayer.RemoveCategory(category.categoryName);
                    }

                    if (view.DrawButton("↑", 20, ROW_HEIGHT, templateLayer.CanMoveCategory(category.categoryName, -1)))
                    {
                        templateLayer.MoveCategory(category.categoryName, -1);
                    }

                    if (view.DrawButton("↓", 20, ROW_HEIGHT, templateLayer.CanMoveCategory(category.categoryName, 1)))
                    {
                        templateLayer.MoveCategory(category.categoryName, 1);
                    }
                }
                view.EndLayout();
            }
        }

        /// <summary>「テンプレ編集」タブ: テンプレの改名・削除・ソート・新規保存</summary>
        private void DrawTemplate(GUIView view)
        {
            var templateLayer = templateManager.GetTemplateLayer();
            if (templateLayer == null)
            {
                view.DrawLabel("テンプレートレイヤーが存在しません。", -1, ROW_HEIGHT);
                return;
            }

            foreach (var category in templateLayer.categories)
            {
                view.BeginHorizontal();
                {
                    view.DrawLabel($"[{category.categoryName}]", 180, ROW_HEIGHT);

                    if (view.DrawButton("ソート", 60, ROW_HEIGHT))
                    {
                        category.SortTemplates();
                    }
                }
                view.EndLayout();

                var templates = category.templates.ToArray();
                foreach (var template in templates)
                {
                    view.BeginHorizontal();
                    {
                        view.DrawTextField(new GUIView.TextFieldOption
                        {
                            width = 150,
                            value = template.templateName,
                            onChanged = value =>
                            {
                                template.templateName = value;
                                template.nameWidth = -1f;
                                category.dirty = true;
                            },
                            hiddenButton = true,
                        });

                        if (view.DrawButton("削除", 45, ROW_HEIGHT))
                        {
                            category.RemoveTemplate(template.templateName);
                        }
                    }
                    view.EndLayout();
                }

                DrawNewTemplateField(view, category);

                view.DrawHorizontalLine();
            }
        }

        /// <summary>新規テンプレ名の入力欄と「追加」ボタン (操作タブとテンプレ編集タブで共通)</summary>
        private void DrawNewTemplateField(GUIView view, MTEP.TemplateCategoryXml category)
        {
            if (!_newTemplateNames.ContainsKey(category.categoryName))
            {
                _newTemplateNames[category.categoryName] = "";
            }
            var newTemplateName = _newTemplateNames[category.categoryName];

            view.BeginHorizontal();
            {
                view.DrawTextField(new GUIView.TextFieldOption
                {
                    label = "テンプレ名",
                    labelWidth = 70,
                    width = 200,
                    value = newTemplateName,
                    onChanged = value => _newTemplateNames[category.categoryName] = value,
                    hiddenButton = true,
                });

                if (view.DrawButton("追加", 45, ROW_HEIGHT, !string.IsNullOrEmpty(newTemplateName)))
                {
                    if (category.AddTemplate(newTemplateName))
                    {
                        _newTemplateNames[category.categoryName] = "";
                    }
                }
            }
            view.EndLayout();

            if (newTemplateName.Length > 0)
            {
                var selectedBones = timelineManager.selectedBones;
                if (selectedBones == null || selectedBones.Count == 0)
                {
                    view.DrawLabel("ボーンが選択されていません。", -1, ROW_HEIGHT, Color.green);
                }
                else if (category.HasTemplate(newTemplateName))
                {
                    view.DrawLabel("同名のテンプレートが既に存在します。", -1, ROW_HEIGHT, Color.green);
                }
            }
        }
    }
}
