using System;
using System.Linq;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// ライト 1 灯分のパラメータ行。
    /// LightWindow と TimelineItemInspector (ライトレイヤーの項目表示) で共有する。
    /// メインライトは LightMain 経由でしか正しく編集できないため、追加ライトとは別の行を持つ。
    ///
    /// 追従メイドのコンボボックスの開閉状態を持つため、
    /// ライトごと・描画するビューごとにインスタンスを分ける
    /// </summary>
    public class LightRowDrawer
    {
        private static StudioLightManager lightManager => StudioLightManager.instance;

        // メインライトのリセット既定値（LightMain.Reset と同じ）
        public static readonly Vector3 DefaultMainRotation = new Vector3(40f, 180f, 18f);
        public const float DefaultMainIntensity = 0.95f;
        public const float DefaultMainShadowStrength = 0.098f;
        public const float DefaultMainShadowBias = 0.01f;

        /// <summary>追加ライトの回転のリセット既定値（StudioLightManager.AddLight の生成時と同じ無回転）</summary>
        public static readonly Vector3 DefaultAdditionalRotation = Vector3.zero;

        // 追加した平行光源の影のリセット既定値（メインライトの初期値に合わせる）
        public const float DefaultAdditionalShadowStrength = 0.098f;
        public const float DefaultAdditionalShadowBias = 0.01f;

        /// <summary>座標行（Inspector の座標行と同じ形式）のドラッグ感度</summary>
        public const float PositionDragSensitivity = 0.01f;

        private static readonly int TypeButtonWidth = 70;

        /// <summary>追従の入切トグルの幅</summary>
        private const float FollowToggleWidth = 20f;

        /// <summary>追従メイドのコンボの幅</summary>
        private const float FollowComboWidth = 120f;

        /// <summary>追従先メイドのコンボ</summary>
        private readonly GUIComboBox<MTEP.MaidCache> _followMaidComboBox =
            new GUIComboBox<MTEP.MaidCache>
            {
                getName = (maidCache, _) => maidCache == null ? "未選択" : maidCache.fullName,
                contentSize = new Vector2(150, 300),
                showArrow = false,
            };

        /// <summary>メインライトの Light。シーンによっては取得できず null になる</summary>
        public static Light MainLightComponent
        {
            get
            {
                var lightMain = lightManager.mainLight;
                return lightMain != null ? lightMain.GetComponent<Light>() : null;
            }
        }

        /// <summary>メインライトのパラメータ（回転・強度・影の濃さ・色・リセット）</summary>
        /// <param name="colorLabelPrefix">
        /// 色行のラベル (= ピッカーの同定キー) の接頭辞。
        /// 複数ライトを並べる呼び出し側がキーを一意にするために使う。null なら付けない
        /// </param>
        public void DrawMainLightParams(
            GUIView view, Light light, float labelWidth, float rowHeight,
            string colorLabelPrefix = null)
        {
            var lightMain = lightManager.mainLight;

            // 既定の横回転 180 度はスライダー範囲の両端どちらでも同じ向きになる。
            // 正規化表示 (-180, 180] と符号を揃えるため -180 側を既定値にする
            DrawRotationSliders(view, labelWidth,
                light.transform.eulerAngles,
                new Vector3(DefaultMainRotation.x, DefaultMainRotation.y - 360f),
                lightMain.SetRotation);

            DrawAxisSlider(view, labelWidth, "強度", light.intensity, 0f, 5f, 0.01f,
                DefaultMainIntensity, value => lightMain.SetIntensity(value));
            DrawAxisSlider(view, labelWidth, "影の濃さ", light.shadowStrength, 0f, 1f, 0.01f,
                DefaultMainShadowStrength, value => lightMain.SetShadowStrength(value));
            // shadowBias に LightMain の API は無いため Light へ直接書く（LightMain.Reset と同じ扱い）
            DrawAxisSlider(view, labelWidth, "影の距離", light.shadowBias, 0f, 1f, 0.01f,
                DefaultMainShadowBias, value => light.shadowBias = value);

            // ColorPickerWindow はラベル文字列で編集対象を識別するため、
            // 追加ライト側の色行とラベルを重複させないこと
            DrawColorRow(view, colorLabelPrefix, "メイン色", light, Color.white);

            if (view.DrawButton("リセット", 100, rowHeight))
            {
                RecordLightEdit("リセット");
                lightMain.Reset();
            }
        }

        /// <summary>
        /// 追加ライトのパラメータ
        /// （種別・有効・位置/オフセット・回転・強度・範囲・スポット角度・影・色・メイド追従）
        /// </summary>
        /// <param name="colorLabelPrefix">DrawMainLightParams と同じ</param>
        /// <param name="followLight">
        /// メイド追従の状態を持つコンポーネント。タイムライン側の StudioLightStat が持つ実体で、
        /// ライトレイヤーがキー化するのと同じものを編集する。
        /// null なら Light から逆引きする (StudioLightStat を持たない呼び出し側のため)
        /// </param>
        public void DrawAdditionalLightParams(
            GUIView view, Light light, float labelWidth, float rowHeight,
            string colorLabelPrefix = null, MTEP.MaidFollowLight followLight = null)
        {
            if (followLight == null)
            {
                followLight = FindFollowLight(light);
            }

            view.BeginHorizontal();
            {
                view.DrawLabel("種別", labelWidth, rowHeight);
                DrawLightTypeButton(view, rowHeight, light, LightType.Point, "ポイント");
                DrawLightTypeButton(view, rowHeight, light, LightType.Spot, "スポット");
                DrawLightTypeButton(view, rowHeight, light, LightType.Directional, "平行");
            }
            view.EndLayout();

            view.DrawToggle("有効", light.enabled, -1, rowHeight,
                value =>
                {
                    RecordLightEdit("有効");
                    light.enabled = value;
                });

            // 平行光源は位置を持たない。追従中は位置がメイド基準のオフセットになる
            // （StudioLightStat.position と同じ切り替え）
            if (light.type != LightType.Directional)
            {
                if (followLight != null && followLight.isFollow)
                {
                    DrawVector3Row(view, labelWidth, rowHeight, "オフセット", followLight.offset,
                        value =>
                        {
                            RecordLightEdit("オフセット");
                            followLight.offset = value;
                        },
                        () =>
                        {
                            RecordLightEdit("オフセット");
                            followLight.offset = Vector3.zero;
                        });
                }
                else
                {
                    var lightTransform = light.transform;
                    DrawVector3Row(view, labelWidth, rowHeight, "位置",
                        lightTransform.localPosition,
                        value =>
                        {
                            RecordLightEdit("位置");
                            lightTransform.localPosition = value;
                        },
                        () =>
                        {
                            RecordLightEdit("位置");
                            lightTransform.localPosition =
                                StudioLightManager.DefaultPosition;
                        });
                }
            }

            // ポイントライトは全方位へ照らすため向きを持たない
            if (light.type != LightType.Point)
            {
                DrawRotationSliders(view, labelWidth,
                    light.transform.eulerAngles,
                    DefaultAdditionalRotation,
                    value => light.transform.eulerAngles = value);
            }

            DrawAxisSlider(view, labelWidth, "強度", light.intensity, 0f, 5f, 0.01f,
                StudioLightManager.DefaultIntensity, value => light.intensity = value);

            // 平行光源は位置・減衰を持たないため範囲は編集させない
            if (light.type != LightType.Directional)
            {
                DrawAxisSlider(view, labelWidth, "範囲", light.range, 0f, 30f, 0.01f,
                    StudioLightManager.DefaultRange, value => light.range = value);
            }

            if (light.type == LightType.Spot)
            {
                DrawAxisSlider(view, labelWidth, "角度", light.spotAngle, 1f, 179f, 0.1f,
                    StudioLightManager.DefaultSpotAngle, value => light.spotAngle = value);
            }

            // 影は平行光源だけが落とす（ライトレイヤーの表示条件に合わせる）
            if (light.type == LightType.Directional)
            {
                DrawAxisSlider(view, labelWidth, "影の濃さ", light.shadowStrength, 0f, 1f, 0.01f,
                    DefaultAdditionalShadowStrength, value => light.shadowStrength = value);
                DrawAxisSlider(view, labelWidth, "影の距離", light.shadowBias, 0f, 1f, 0.01f,
                    DefaultAdditionalShadowBias, value => light.shadowBias = value);
            }

            DrawColorRow(view, colorLabelPrefix, "追加色", light, Color.white);

            // 平行光源は位置を持たないため追従させない
            if (light.type != LightType.Directional && followLight != null)
            {
                DrawFollowMaidRow(view, labelWidth, rowHeight, followLight);
            }
        }

        /// <summary>
        /// メイド追従の切替と追従先の選択。
        /// メインライトはゲーム側の恒久オブジェクトのため追従対象にしない
        /// （StudioLightManager.RemoveMainLightFollow の方針に合わせ、追加ライトでのみ描く）
        /// </summary>
        private void DrawFollowMaidRow(
            GUIView view, float labelWidth, float rowHeight, MTEP.MaidFollowLight followLight)
        {
            view.BeginHorizontal();
            {
                view.DrawLabel("追従メイド", labelWidth, rowHeight);

                _followMaidComboBox.buttonSize = new Vector2(FollowComboWidth, rowHeight);

                view.DrawToggle("", followLight.maidSlotNo >= 0, FollowToggleWidth, rowHeight,
                    value =>
                    {
                        RecordLightEdit("追従メイド");
                        followLight.maidSlotNo =
                            value ? Mathf.Max(0, _followMaidComboBox.currentIndex) : -1;
                    });

                _followMaidComboBox.items = MTEP.MaidManager.instance.maidCaches;
                _followMaidComboBox.onSelected = (maidCache, index) =>
                {
                    RecordLightEdit("追従メイド");
                    followLight.maidSlotNo = index;
                };
                _followMaidComboBox.DrawButton(view);
            }
            view.EndLayout();
        }

        /// <summary>
        /// タイムライン側が持つ追従コンポーネント。
        /// ライト一覧はタイムライン側で遅延収集されるため、未収集なら null を返す
        /// </summary>
        private static MTEP.MaidFollowLight FindFollowLight(Light light)
        {
            var stat = MTEP.StudioLightManager.instance.lights
                .FirstOrDefault(s => s != null && s.light == light);
            return stat != null ? stat.followLight : null;
        }

        /// <summary>ラベル + XYZ（ドラッグラベル + 数値入力）+ リセットボタンの 1 行</summary>
        private static void DrawVector3Row(
            GUIView view, float labelWidth, float rowHeight,
            string label, Vector3 value, Action<Vector3> onChanged, Action onReset)
        {
            Vector3RowDrawer.Draw(view, label, PositionDragSensitivity, labelWidth, rowHeight,
                value, onChanged, onReset);
        }

        /// <summary>ライトの向き（縦回転・横回転・ロール）</summary>
        private static void DrawRotationSliders(
            GUIView view, float labelWidth,
            Vector3 eulerAngles, Vector3 defaultRotation, Action<Vector3> onChanged)
        {
            var pitch = AngleUtils.NormalizeAngle(eulerAngles.x);
            var yaw = AngleUtils.NormalizeAngle(eulerAngles.y);
            var roll = AngleUtils.NormalizeAngle(eulerAngles.z);

            DrawAxisSlider(view, labelWidth, "縦回転", pitch, -90f, 90f, 0.1f, defaultRotation.x,
                value => onChanged(new Vector3(value, yaw, roll)));
            DrawAxisSlider(view, labelWidth, "横回転", yaw, -180f, 180f, 0.1f, defaultRotation.y,
                value => onChanged(new Vector3(pitch, value, roll)));
            DrawAxisSlider(view, labelWidth, "ロール", roll, -180f, 180f, 0.1f, defaultRotation.z,
                value => onChanged(new Vector3(pitch, yaw, value)));
        }

        /// <summary>種別切替ボタン 1 つ。選択中はアクセント色で示す</summary>
        private static void DrawLightTypeButton(
            GUIView view, float rowHeight, Light light, LightType type, string label)
        {
            var isCurrent = light.type == type;
            if (view.DrawButton(label, TypeButtonWidth, rowHeight, true,
                isCurrent ? Color.cyan : Color.white) && !isCurrent)
            {
                RecordLightEdit("種別");
                lightManager.SetLightType(light, type);
            }
        }

        /// <summary>ライトの色を DrawColor（ColorPickerWindow 連携）で編集する 1 行</summary>
        private static void DrawColorRow(
            GUIView view, string labelPrefix, string label, Light light, Color resetColor)
        {
            var colorLabel = labelPrefix == null ? label : labelPrefix + "/" + label;
            var fieldCache = view.GetColorFieldCache(colorLabel, false);
            view.DrawColor(fieldCache, light.color, resetColor,
                value =>
                {
                    RecordLightEdit(label);
                    light.color = value;
                });
        }

        /// <summary>共通書式のスライダー 1 行（CameraWindow と同形式）</summary>
        private static void DrawAxisSlider(
            GUIView view, float labelWidth,
            string label, float value, float min, float max, float step,
            float defaultValue, Action<float> onChanged)
        {
            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = label,
                labelWidth = labelWidth,
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
        public static void RecordLightEdit(string label)
        {
            HistoryManager.instance.BeforeEdit(null, HistoryScope.Light, "ライト: " + label);
        }
    }
}
