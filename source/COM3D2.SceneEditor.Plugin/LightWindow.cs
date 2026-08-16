using COM3D2.MotionTimelineEditor;
using UnityEngine;

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
        private static readonly int TYPE_BUTTON_WIDTH = 70;

        // メインライトのリセット既定値（LightMain.Reset と同じ）
        private static readonly Vector3 DefaultMainRotation = new Vector3(40f, 180f, 18f);
        private const float DefaultMainIntensity = 0.95f;
        private const float DefaultMainShadowStrength = 0.098f;

        /// <summary>追加ライトの回転のリセット既定値（StudioLightManager.AddLight の生成時と同じ無回転）</summary>
        private static readonly Vector3 DefaultAdditionalRotation = Vector3.zero;

        /// <summary>編集中のライト（メイン / 追加）。破棄・削除で null になりうる</summary>
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

        /// <summary>メインライトの Light。シーンによっては取得できず null になる</summary>
        private static Light mainLightComponent
        {
            get
            {
                var lightMain = lightManager.mainLight;
                return lightMain != null ? lightMain.GetComponent<Light>() : null;
            }
        }

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

            // GetComponent を挟むため 1 描画につき 1 回だけ引いて使い回す
            var mainLight = mainLightComponent;

            DrawLightListSection(mainLight);

            if (_selectedLight != null)
            {
                _view.DrawHorizontalLine();
                DrawLightEditSection(_selectedLight, mainLight);
            }

            _view.EndScrollView();
        }

        /// <summary>ライト一覧（メインライト + 追加ライト）と、追加ライトの追加・削除</summary>
        private void DrawLightListSection(Light mainLight)
        {
            _view.DrawLabel("ライト一覧", -1, ROW_HEIGHT);

            _view.BeginHorizontal();
            {
                if (_view.DrawButton("追加", 60, ROW_HEIGHT))
                {
                    RecordLightEdit("追加");
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

            RecordLightEdit("削除");

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
                DrawMainLightParams(light);
            }
            else
            {
                DrawAdditionalLightParams(light);
            }
        }

        /// <summary>メインライトのパラメータ（回転・強度・影の濃さ・色・リセット）</summary>
        private void DrawMainLightParams(Light light)
        {
            var lightMain = lightManager.mainLight;

            // 既定の横回転 180 度はスライダー範囲の両端どちらでも同じ向きになる。
            // 正規化表示 (-180, 180] と符号を揃えるため -180 側を既定値にする
            DrawRotationSliders(
                light.transform.eulerAngles,
                new Vector3(DefaultMainRotation.x, DefaultMainRotation.y - 360f),
                lightMain.SetRotation);

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

        /// <summary>
        /// 追加ライトのパラメータ
        /// （種別・有効・回転・強度・範囲・スポット角度・色）
        /// </summary>
        private void DrawAdditionalLightParams(Light light)
        {
            _view.BeginHorizontal();
            {
                _view.DrawLabel("種別", LABEL_WIDTH, ROW_HEIGHT);
                DrawLightTypeButton(light, LightType.Point, "ポイント");
                DrawLightTypeButton(light, LightType.Spot, "スポット");
                DrawLightTypeButton(light, LightType.Directional, "平行");
            }
            _view.EndLayout();

            _view.DrawToggle("有効", light.enabled, -1, ROW_HEIGHT,
                value =>
                {
                    RecordLightEdit("有効");
                    light.enabled = value;
                });

            // ポイントライトは全方位へ照らすため向きを持たない
            if (light.type != LightType.Point)
            {
                DrawRotationSliders(
                    light.transform.eulerAngles,
                    DefaultAdditionalRotation,
                    value => light.transform.eulerAngles = value);
            }

            DrawAxisSlider("強度", light.intensity, 0f, 5f, 0.01f,
                StudioLightManager.DefaultIntensity, value => light.intensity = value);

            // 平行光源は位置・減衰を持たないため範囲は編集させない
            if (light.type != LightType.Directional)
            {
                DrawAxisSlider("範囲", light.range, 0f, 30f, 0.01f,
                    StudioLightManager.DefaultRange, value => light.range = value);
            }

            if (light.type == LightType.Spot)
            {
                DrawAxisSlider("角度", light.spotAngle, 1f, 179f, 0.1f,
                    StudioLightManager.DefaultSpotAngle, value => light.spotAngle = value);
            }

            DrawColorRow("追加色", light, Color.white);
        }

        /// <summary>ライトの向き（縦回転・横回転）。ロールは扱わず元の値を保つ</summary>
        private void DrawRotationSliders(
            Vector3 eulerAngles, Vector3 defaultRotation, System.Action<Vector3> onChanged)
        {
            var pitch = NormalizeAngle(eulerAngles.x);
            var yaw = NormalizeAngle(eulerAngles.y);

            DrawAxisSlider("縦回転", pitch, -90f, 90f, 0.1f, defaultRotation.x,
                value => onChanged(new Vector3(value, yaw, eulerAngles.z)));
            DrawAxisSlider("横回転", yaw, -180f, 180f, 0.1f, defaultRotation.y,
                value => onChanged(new Vector3(pitch, value, eulerAngles.z)));
        }

        /// <summary>種別切替ボタン 1 つ。選択中はアクセント色で示す</summary>
        private void DrawLightTypeButton(Light light, LightType type, string label)
        {
            var isCurrent = light.type == type;
            if (_view.DrawButton(label, TYPE_BUTTON_WIDTH, ROW_HEIGHT, true,
                isCurrent ? Color.cyan : Color.white) && !isCurrent)
            {
                RecordLightEdit("種別");
                lightManager.SetLightType(light, type);
            }
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
