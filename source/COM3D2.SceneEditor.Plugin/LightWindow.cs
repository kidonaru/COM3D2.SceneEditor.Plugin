using System;
using System.Linq;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// ライトを編集するウィンドウ。一覧から 1 灯選び、下の編集欄でパラメータを変える。
    /// 一覧にはメインライトと追加ライトが並ぶが、メインライトはゲーム側の実体のため
    /// 削除・種別変更ができず、操作も LightMain 経由で行う。
    /// 追加ライトの実体は StudioLightManager が持つ。
    /// 向きはここで直接編集でき、位置の編集は Inspector に寄せる（一覧から選択連動）
    /// </summary>
    public class LightWindow : EditorSubWindow
    {
        public static readonly int WINDOW_ID = 8903368;

        protected override int windowId => WINDOW_ID;
        protected override string windowTitle => "ライト";

        private static readonly int ROW_HEIGHT = 20;
        private static readonly int LABEL_WIDTH = 70;

        /// <summary>編集中のライト（メイン / 追加）。破棄・削除で null になりうる</summary>
        private Light _selectedLight = null;

        // 編集欄は常に 1 灯ぶんなので、行ドロワーも 1 つで足りる
        private readonly LightRowDrawer _rowDrawer = new LightRowDrawer();

        // コンボのフォーカスはルートビューで共有されるため、内容ビューを子にする
        private readonly GUIView _rootView = new GUIView();
        private readonly GUIView _view = new GUIView();

        private static LightWindow _instance = null;
        public static LightWindow instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new LightWindow();
                }
                return _instance;
            }
        }

        private LightWindow()
        {
        }

        private static StudioLightManager lightManager => StudioLightManager.instance;

        protected override void LoadPlacement(out int x, out int y, out int width, out int height)
        {
            x = config.lightPosX;
            y = config.lightPosY;
            width = config.lightWidth;
            height = config.lightHeight;
        }

        protected override void StorePlacement(int x, int y, int width, int height)
        {
            config.lightPosX = x;
            config.lightPosY = y;
            config.lightWidth = width;
            config.lightHeight = height;
        }

        public override bool savedVisible
        {
            get => config.lightVisible;
            set => config.lightVisible = value;
        }

        protected override void DrawContent()
        {
            _rootView.Init(new Rect(0f, 0f, windowRect.width, windowRect.height));
            _view.parent = _rootView;
            _view.Init(ToLocalRect(contentRect));

            _view.DrawHorizontalLine(Color.gray);
            _view.AddSpace(5);

            _view.BeginScrollView(-1, -1, GUIView.AutoScrollViewRect, false, true);

            // GetComponent を挟むため 1 描画につき 1 回だけ引いて使い回す
            var mainLight = LightRowDrawer.MainLightComponent;

            DrawLightListSection(mainLight);

            if (_selectedLight != null)
            {
                _view.DrawHorizontalLine();
                DrawLightEditSection(_selectedLight, mainLight);
            }

            _view.EndScrollView();

            // ボタン押下で _rootView に登録されたフォーカスをポップアップへ引き渡す
            ComboBoxPopupWindow.instance.ProcessFocus(_rootView, this);
        }

        /// <summary>ライト一覧（メインライト + 追加ライト）と、追加ライトの追加・削除</summary>
        private void DrawLightListSection(Light mainLight)
        {
            _view.DrawLabel("ライト一覧", -1, ROW_HEIGHT);

            _view.BeginHorizontal();
            {
                if (_view.DrawButton("追加", 60, ROW_HEIGHT))
                {
                    LightRowDrawer.RecordLightEdit("追加");
                    SelectLight(lightManager.AddLight(), mainLight);
                }

                // メインライトはゲーム側の実体なので削除させない
                if (_view.DrawButton("削除", 60, ROW_HEIGHT,
                    _selectedLight != null && _selectedLight != mainLight))
                {
                    RemoveSelectedLight();
                }
            }
            _view.EndLayout();

            if (mainLight != null)
            {
                DrawLightRow(mainLight, "メインライト", mainLight);
            }
            else
            {
                _view.DrawLabel("メインライトが見つかりません", -1, ROW_HEIGHT,
                    textColor: Color.yellow);
            }

            foreach (var light in lightManager.lights)
            {
                if (light == null)
                {
                    continue;
                }
                DrawLightRow(light, light.gameObject.name, mainLight);
            }
        }

        /// <summary>一覧の 1 行。クリックで編集対象にする</summary>
        private void DrawLightRow(Light light, string label, Light mainLight)
        {
            var isSelected = light == _selectedLight;
            if (_view.DrawButton(label, -1, ROW_HEIGHT, true,
                isSelected ? Color.cyan : Color.white))
            {
                SelectLight(light, mainLight);
            }
        }

        /// <summary>
        /// 編集対象を切り替える。追加ライトは Inspector・ギズモからも動かせるよう選択に載せるが、
        /// メインライトは LightMain 経由でしか正しく編集できないため載せない
        /// </summary>
        private void SelectLight(Light light, Light mainLight)
        {
            _selectedLight = light;
            SelectionManager.instance.Select(
                light != null && light != mainLight ? light.gameObject : null);
        }

        /// <summary>選択中の追加ライトを削除する</summary>
        private void RemoveSelectedLight()
        {
            // 呼び出し元のボタン活性だけに安全性を委ねない
            if (_selectedLight == null)
            {
                return;
            }

            LightRowDrawer.RecordLightEdit("削除");

            // 消したライトを Inspector に残さない
            if (SelectionManager.instance.selectedObject == _selectedLight.gameObject)
            {
                SelectionManager.instance.Select(null);
            }
            lightManager.RemoveLight(_selectedLight);
            _selectedLight = null;
        }

        /// <summary>選択中ライトの編集欄。メインライトと追加ライトで項目が異なる</summary>
        private void DrawLightEditSection(Light light, Light mainLight)
        {
            _view.DrawLabel("ライト編集", -1, ROW_HEIGHT);

            if (light == mainLight)
            {
                _rowDrawer.DrawMainLightParams(_view, light, LABEL_WIDTH, ROW_HEIGHT);
            }
            else
            {
                _rowDrawer.DrawAdditionalLightParams(_view, light, LABEL_WIDTH, ROW_HEIGHT);
            }
        }
    }
}
