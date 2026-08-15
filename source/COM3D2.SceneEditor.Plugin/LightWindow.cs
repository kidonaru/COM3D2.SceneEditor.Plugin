using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// メインライトと追加ライトを編集するウィンドウ。
    /// メインライトは LightMain を直接操作し、追加ライトは StudioLightManager が実体を持つ。
    /// 位置・回転の編集は既存方針どおり Inspector に寄せる（一覧から選択連動）
    /// </summary>
    public class LightWindow : EditorSubWindow
    {
        public static readonly int WINDOW_ID = 8903368;

        protected override int windowId => WINDOW_ID;
        protected override string windowTitle => "ライト";

        private static readonly int ROW_HEIGHT = 20;
        private static readonly int LABEL_WIDTH = 70;

        // メインライトのリセット既定値（LightMain.Reset と同じ）
        private static readonly Vector3 DefaultMainRotation = new Vector3(40f, 180f, 18f);
        private const float DefaultMainIntensity = 0.95f;
        private const float DefaultMainShadowStrength = 0.098f;

        /// <summary>選択中の追加ライト。破棄・削除で null になりうる</summary>
        private Light _selectedLight = null;

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
            _view.Init(ToLocalRect(contentRect));

            _view.DrawHorizontalLine(Color.gray);
            _view.AddSpace(5);

            _view.BeginScrollView(-1, -1, GUIView.AutoScrollViewRect, false, true);

            DrawMainLightSection();
            _view.DrawHorizontalLine();
            DrawAdditionalLightSection();

            _view.EndScrollView();
        }

        /// <summary>メインライト（回転・色・強度・影の濃さ・リセット）</summary>
        private void DrawMainLightSection()
        {
            _view.DrawLabel("メインライト", -1, ROW_HEIGHT);

            var lightMain = lightManager.mainLight;
            var light = lightMain != null ? lightMain.GetComponent<Light>() : null;
            if (light == null)
            {
                _view.DrawLabel("メインライトが見つかりません", -1, ROW_HEIGHT,
                    textColor: Color.yellow);
                return;
            }

            var eulerAngles = light.transform.eulerAngles;
            var pitch = NormalizeAngle(eulerAngles.x);
            var yaw = NormalizeAngle(eulerAngles.y);

            DrawAxisSlider("縦回転", pitch, -90f, 90f, 0.1f, DefaultMainRotation.x,
                value => lightMain.SetRotation(new Vector3(value, yaw, eulerAngles.z)));
            // 既定の 180 度はスライダー範囲の両端どちらでも同じ向きになる。
            // 正規化表示 (-180, 180] と符号を揃えるため -180 側を既定値にする
            DrawAxisSlider("横回転", yaw, -180f, 180f, 0.1f, DefaultMainRotation.y - 360f,
                value => lightMain.SetRotation(new Vector3(pitch, value, eulerAngles.z)));

            DrawAxisSlider("強度", light.intensity, 0f, 5f, 0.01f, DefaultMainIntensity,
                value => lightMain.SetIntensity(value));
            DrawAxisSlider("影の濃さ", light.shadowStrength, 0f, 1f, 0.01f,
                DefaultMainShadowStrength, value => lightMain.SetShadowStrength(value));

            // ColorPickerWindow はラベル文字列で編集対象を識別するため、
            // 追加ライト側の色行とラベルを重複させないこと
            DrawColorRow("メイン色", light, Color.white);

            if (_view.DrawButton("リセット", 100, ROW_HEIGHT))
            {
                RecordLightEdit("リセット");
                lightMain.Reset();
            }
        }

        /// <summary>追加ライトの一覧・追加・削除と、選択中ライトのパラメータ編集</summary>
        private void DrawAdditionalLightSection()
        {
            _view.DrawLabel("追加ライト", -1, ROW_HEIGHT);

            _view.BeginHorizontal();
            {
                if (_view.DrawButton("追加", 60, ROW_HEIGHT))
                {
                    RecordLightEdit("追加");
                    _selectedLight = lightManager.AddLight();
                    SelectionManager.instance.Select(_selectedLight.gameObject);
                }
                if (_view.DrawButton("削除", 60, ROW_HEIGHT, _selectedLight != null))
                {
                    RecordLightEdit("削除");

                    // 消したライトを Inspector に残さない
                    if (SelectionManager.instance.selectedObject == _selectedLight.gameObject)
                    {
                        SelectionManager.instance.Select(null);
                    }
                    lightManager.RemoveLight(_selectedLight);
                    _selectedLight = null;
                }
            }
            _view.EndLayout();

            foreach (var light in lightManager.lights)
            {
                if (light == null)
                {
                    continue;
                }

                var isSelected = light == _selectedLight;
                if (_view.DrawButton(light.gameObject.name, -1, ROW_HEIGHT, true,
                    isSelected ? Color.cyan : Color.white))
                {
                    // 選択と同時に Inspector で位置・回転を編集できるようにする
                    _selectedLight = light;
                    SelectionManager.instance.Select(light.gameObject);
                }
            }

            if (_selectedLight == null)
            {
                return;
            }

            _view.DrawHorizontalLine();
            DrawSelectedLightParams(_selectedLight);
        }

        /// <summary>選択中の追加ライトのパラメータ（種別・有効・色・強度・範囲・スポット角度）</summary>
        private void DrawSelectedLightParams(Light light)
        {
            _view.BeginHorizontal();
            {
                _view.DrawLabel("種別", LABEL_WIDTH, ROW_HEIGHT);
                if (_view.DrawButton("ポイント", 80, ROW_HEIGHT, true,
                    light.type == LightType.Point ? Color.cyan : Color.white))
                {
                    RecordLightEdit("種別");
                    lightManager.SetLightType(light, LightType.Point);
                }
                if (_view.DrawButton("スポット", 80, ROW_HEIGHT, true,
                    light.type == LightType.Spot ? Color.cyan : Color.white))
                {
                    RecordLightEdit("種別");
                    lightManager.SetLightType(light, LightType.Spot);
                }
            }
            _view.EndLayout();

            _view.DrawToggle("有効", light.enabled, -1, ROW_HEIGHT,
                value =>
                {
                    RecordLightEdit("有効");
                    light.enabled = value;
                });

            DrawAxisSlider("強度", light.intensity, 0f, 5f, 0.01f,
                StudioLightManager.DefaultIntensity, value => light.intensity = value);
            DrawAxisSlider("範囲", light.range, 0f, 30f, 0.01f,
                StudioLightManager.DefaultRange, value => light.range = value);

            if (light.type == LightType.Spot)
            {
                DrawAxisSlider("角度", light.spotAngle, 1f, 179f, 0.1f,
                    StudioLightManager.DefaultSpotAngle, value => light.spotAngle = value);
            }

            DrawColorRow("追加色", light, Color.white);
        }

        /// <summary>ライトの色を DrawColor（ColorPickerWindow 連携）で編集する 1 行</summary>
        private void DrawColorRow(string label, Light light, Color resetColor)
        {
            var fieldCache = _view.GetColorFieldCache(label, false);
            _view.DrawColor(fieldCache, light.color, resetColor,
                value =>
                {
                    RecordLightEdit(label);
                    light.color = value;
                });
        }

        /// <summary>共通書式のスライダー 1 行（CameraWindow と同形式）</summary>
        private void DrawAxisSlider(
            string label, float value, float min, float max, float step,
            float defaultValue, System.Action<float> onChanged)
        {
            _view.DrawSliderValue(new GUIView.SliderOption
            {
                label = label,
                labelWidth = LABEL_WIDTH,
                width = -1,
                min = min,
                max = max,
                step = step,
                defaultValue = defaultValue,
                value = value,
                onChanged = newValue =>
                {
                    RecordLightEdit(label);
                    onChanged(newValue);
                },
            });
        }

        /// <summary>ライト操作を履歴へ記録する。ドラッグ中の連続変更は 1 件に集約される</summary>
        private static void RecordLightEdit(string label)
        {
            HistoryManager.instance.BeforeEdit(null, HistoryScope.Light, "ライト: " + label);
        }

        /// <summary>角度を (-180, 180] へ正規化する</summary>
        private static float NormalizeAngle(float angle)
        {
            angle = Mathf.Repeat(angle, 360f);
            return angle > 180f ? angle - 360f : angle;
        }
    }
}
