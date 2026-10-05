using System;
using System.Collections.Generic;
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

        /// <summary>強度スライダーの上限（メイン・追加ライト共通）</summary>
        public const float MaxIntensity = 20f;

        /// <summary>追加ライトの回転のリセット既定値（StudioLightManager.AddLight の生成時と同じ無回転）</summary>
        public static readonly Vector3 DefaultAdditionalRotation = Vector3.zero;

        // 追加ライトの影のリセット既定値（メインライトの初期値に合わせる）
        public const float DefaultAdditionalShadowStrength = 0.098f;
        public const float DefaultAdditionalShadowBias = 0.01f;

        /// <summary>座標行（Inspector の座標行と同じ形式）のドラッグ感度</summary>
        public const float PositionDragSensitivity = 0.01f;

        private static readonly int TypeButtonWidth = 70;

        /// <summary>
        /// ライトごとの回転スライダーの前回表示値 (キーは Transform のインスタンス ID)。
        /// 実体の eulerAngles だけでは縦回転 90 度超を表せないため、表示の連続性をここで保つ
        /// </summary>
        private static readonly Dictionary<int, Vector3> LastRotationAngles = new Dictionary<int, Vector3>();

        /// <summary>コピー / ペーストボタンの幅（MaterialPropertyRowsDrawer と同じ）</summary>
        private const float ClipboardButtonWidth = 60f;

        /// <summary>追従の入切トグルの幅</summary>
        private const float FollowToggleWidth = 20f;

        /// <summary>追従メイドのコンボの幅</summary>
        private const float FollowComboWidth = 120f;

        /// <summary>照射対象のコンボの幅</summary>
        private const float LightTargetComboWidth = 120f;

        /// <summary>輪郭画像のコンボの幅</summary>
        private const float CookieImageComboWidth = 160f;

        /// <summary>輪郭画像の再読込ボタンの幅</summary>
        private const float CookieReloadButtonWidth = 60f;

        /// <summary>照射対象のコンボ。開閉状態を持つため追従コンボと同じくインスタンスごとに分ける</summary>
        private readonly GUIComboBox<LightTargetMode> _lightTargetComboBox =
            new GUIComboBox<LightTargetMode>
            {
                items = Enum.GetValues(typeof(LightTargetMode)).Cast<LightTargetMode>().ToList(),
                getName = (mode, _) => GetLightTargetName(mode),
                contentSize = new Vector2(120, 100),
            };

        /// <summary>追従先メイドのコンボ</summary>
        private readonly GUIComboBox<MTEP.MaidCache> _followMaidComboBox =
            new GUIComboBox<MTEP.MaidCache>
            {
                getName = (maidCache, _) => maidCache == null ? "未選択" : maidCache.fullName,
                contentSize = new Vector2(150, 300),
                showArrow = false,
            };

        /// <summary>輪郭画像のコンボ。輪郭画像フォルダ (Config/SceneEditor/LightCookie) からの相対パスを並べる</summary>
        private readonly GUIComboBox<string> _cookieImageComboBox =
            new GUIComboBox<string>
            {
                getName = (name, _) => name,
                contentSize = new Vector2(220, 300),
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

            DrawClipboardRow(view, rowHeight,
                () => LightClipboard.CopyMain(light),
                () => LightClipboard.PasteMain(lightMain, light));

            // 既定の横回転 180 度はスライダー範囲の両端どちらでも同じ向きになる。
            // 正規化表示 (-180, 180] と符号を揃えるため -180 側を既定値にする
            DrawRotationSliders(view, labelWidth,
                light.transform,
                new Vector3(DefaultMainRotation.x, DefaultMainRotation.y - 360f),
                lightMain.SetRotation);

            DrawAxisSlider(view, labelWidth, "強度", light.intensity, 0f, MaxIntensity, 0.01f,
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

            DrawClipboardRow(view, rowHeight,
                () => LightClipboard.Copy(light, followLight),
                () => LightClipboard.Paste(light, followLight));

            view.BeginHorizontal();
            {
                view.DrawLabel("種別", labelWidth, rowHeight);
                DrawLightTypeButton(view, rowHeight, light, LightType.Point, "ポイント");
                DrawLightTypeButton(view, rowHeight, light, LightType.Spot, "スポット");
                DrawLightTypeButton(view, rowHeight, light, LightType.Directional, "平行");
            }
            view.EndLayout();

            DrawLightTargetRow(view, labelWidth, rowHeight, light);

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
                    light.transform,
                    DefaultAdditionalRotation,
                    value => light.transform.eulerAngles = value);
            }

            DrawAxisSlider(view, labelWidth, "強度", light.intensity, 0f, MaxIntensity, 0.01f,
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
                DrawCookieRows(view, labelWidth, rowHeight, light);
            }

            DrawShadowTypeRow(view, labelWidth, rowHeight, light);

            // 濃さと距離は影を落とすときだけ意味を持つ
            if (light.shadows != LightShadows.None)
            {
                // キャラの影はキャラを照らさない「背景のみ」のときだけ意味を持つ
                if (LightTarget.FromCullingMask(light.cullingMask) == LightTargetMode.Background)
                {
                    DrawCharacterShadowRow(view, rowHeight, light);
                }
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

        /// <summary>コピー / ペーストの 2 ボタン。ペーストはクリップボードが空なら押せない</summary>
        private static void DrawClipboardRow(
            GUIView view, float rowHeight, Action onCopy, Action onPaste)
        {
            view.BeginHorizontal();
            {
                if (view.DrawButton("コピー", ClipboardButtonWidth, rowHeight))
                {
                    onCopy();
                }

                if (view.DrawButton("ペースト", ClipboardButtonWidth, rowHeight,
                        enabled: LightClipboard.hasData))
                {
                    RecordLightEdit("ペースト");
                    onPaste();
                }
            }
            view.EndLayout();
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
        public static MTEP.MaidFollowLight FindFollowLight(Light light)
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

        /// <summary>
        /// ライトの向き（縦回転・横回転・ロール）。
        /// 縦回転を ±180 度まで連続して動かせるよう、前回の表示値に近い表現で表示する
        /// </summary>
        private static void DrawRotationSliders(
            GUIView view, float labelWidth,
            Transform transform, Vector3 defaultRotation, Action<Vector3> onChanged)
        {
            var id = transform.GetInstanceID();
            Vector3 prevAngles;
            if (!LastRotationAngles.TryGetValue(id, out prevAngles))
            {
                prevAngles = AngleUtils.NormalizeAngles(transform.eulerAngles);
            }

            // 前回値のままの向きなら前回値を表示する。縦回転 ±90 度ちょうどでは横回転とロールが
            // 1 軸に縮退し、Unity の分解が前回値と別の組み合わせを返すため
            var angles = Quaternion.Angle(Quaternion.Euler(prevAngles), transform.rotation) < 0.01f
                ? prevAngles
                : AngleUtils.GetContinuousEulerAngles(transform.eulerAngles, prevAngles);
            LastRotationAngles[id] = angles;

            Action<Vector3> apply = value =>
            {
                LastRotationAngles[id] = value;
                onChanged(value);
            };

            DrawAxisSlider(view, labelWidth, "縦回転", angles.x, -180f, 180f, 0.1f, defaultRotation.x,
                value => apply(new Vector3(value, angles.y, angles.z)));
            DrawAxisSlider(view, labelWidth, "横回転", angles.y, -180f, 180f, 0.1f, defaultRotation.y,
                value => apply(new Vector3(angles.x, value, angles.z)));
            DrawAxisSlider(view, labelWidth, "ロール", angles.z, -180f, 180f, 0.1f, defaultRotation.z,
                value => apply(new Vector3(angles.x, angles.y, value)));
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

        /// <summary>
        /// 照射対象のドロップダウン。
        /// CharaDirectionalLight と同じ cullingMask の切替で、キャラ用/背景用のライトを分ける
        /// </summary>
        private void DrawLightTargetRow(GUIView view, float labelWidth, float rowHeight, Light light)
        {
            view.BeginHorizontal();
            {
                view.DrawLabel("対象", labelWidth, rowHeight);

                _lightTargetComboBox.buttonSize = new Vector2(LightTargetComboWidth, rowHeight);
                // 履歴の復元等で外から変わるため、描画のたびに実体から選択位置を取り直す
                _lightTargetComboBox.currentItem = LightTarget.FromCullingMask(light.cullingMask);
                _lightTargetComboBox.onSelected = (mode, _) =>
                {
                    RecordLightEdit("対象");
                    light.cullingMask = LightTarget.ToCullingMask(
                        mode, LightTarget.HasCharacterShadow(light.cullingMask));
                };
                _lightTargetComboBox.DrawButton(view);
            }
            view.EndLayout();
        }

        /// <summary>
        /// 影の種類。追加ライトは影なしで生成されるので、影を落とすにはここで種類を選ぶ。
        /// 影を落とす灯が増えるほどシャドウマップの描画が増えて重くなる
        /// </summary>
        private static void DrawShadowTypeRow(GUIView view, float labelWidth, float rowHeight, Light light)
        {
            view.BeginHorizontal();
            {
                view.DrawLabel("影", labelWidth, rowHeight);
                DrawShadowTypeButton(view, rowHeight, light, LightShadows.None, "なし");
                DrawShadowTypeButton(view, rowHeight, light, LightShadows.Hard, "ハード");
                DrawShadowTypeButton(view, rowHeight, light, LightShadows.Soft, "ソフト");
            }
            view.EndLayout();
        }

        private static void DrawShadowTypeButton(
            GUIView view, float rowHeight, Light light, LightShadows shadows, string label)
        {
            var isCurrent = light.shadows == shadows;
            if (view.DrawButton(label, TypeButtonWidth, rowHeight, true,
                isCurrent ? Color.cyan : Color.white) && !isCurrent)
            {
                RecordLightEdit("影");
                SetShadows(light, shadows);
            }
        }

        /// <summary>
        /// 「背景のみ」のライトでもキャラの影を背景へ落とすか。キャラ自身は照らさないまま、影専用の複製で影だけを戻す。
        /// 影用レイヤーが確保できなかった環境では操作できない
        /// </summary>
        private static void DrawCharacterShadowRow(GUIView view, float rowHeight, Light light)
        {
            var isAvailable = CharacterShadowLayer.isAvailable;
            // トグルはツールチップを持たないので、描く前に同じ矩形を取って登録する
            var rect = view.GetDrawRect(-1, rowHeight);
            view.DrawToggle("キャラの影", LightTarget.HasCharacterShadow(light.cullingMask), -1, rowHeight, isAvailable,
                value =>
                {
                    RecordLightEdit("キャラの影");
                    SetCharacterShadow(light, value);
                });
            if (!isAvailable)
            {
                TooltipDrawer.RegisterIfHovered(rect, "影の複製に使える空きレイヤーが無いため使えません");
            }
        }

        /// <summary>
        /// スポットの輪郭 (cookie)。角度を広げると内蔵の輪郭はぼけ幅も広がるため、
        /// 硬さの指定か画像で縁を決められるようにする
        /// </summary>
        private void DrawCookieRows(GUIView view, float labelWidth, float rowHeight, Light light)
        {
            var cookie = LightCookie.Get(light);

            view.BeginHorizontal();
            {
                view.DrawLabel("輪郭", labelWidth, rowHeight);
                DrawCookieModeButton(view, rowHeight, light, cookie, LightCookieMode.Default, "既定");
                DrawCookieModeButton(view, rowHeight, light, cookie, LightCookieMode.Generated, "硬さ");
                DrawCookieModeButton(view, rowHeight, light, cookie, LightCookieMode.Image, "画像");
            }
            view.EndLayout();

            if (cookie.mode == LightCookieMode.Generated)
            {
                DrawAxisSlider(view, labelWidth, "硬さ", cookie.hardness, 0f, 1f, 0.01f,
                    LightCookieData.DefaultHardness,
                    value =>
                    {
                        cookie.hardness = value;
                        SetCookie(light, cookie);
                    });
            }
            else if (cookie.mode == LightCookieMode.Image)
            {
                DrawCookieImageRow(view, labelWidth, rowHeight, light, cookie);
            }
        }

        private static void DrawCookieModeButton(
            GUIView view, float rowHeight, Light light, LightCookieData cookie, LightCookieMode mode, string label)
        {
            var isCurrent = cookie.mode == mode;
            if (view.DrawButton(label, TypeButtonWidth, rowHeight, true,
                isCurrent ? Color.cyan : Color.white) && !isCurrent)
            {
                RecordLightEdit("輪郭");
                cookie.mode = mode;
                SetCookie(light, cookie);
            }
        }

        private void DrawCookieImageRow(
            GUIView view, float labelWidth, float rowHeight, Light light, LightCookieData cookie)
        {
            var names = LightCookieTextures.GetImageNames();

            view.BeginHorizontal();
            {
                view.DrawLabel("画像", labelWidth, rowHeight);

                _cookieImageComboBox.items = names;
                _cookieImageComboBox.buttonSize = new Vector2(CookieImageComboWidth, rowHeight);
                // 履歴の復元等で外から変わるため、描画のたびに実体から選択位置を取り直す
                string fallbackName;
                _cookieImageComboBox.currentIndex =
                    ResolveCookieImageSelection(names, cookie.image, out fallbackName);
                _cookieImageComboBox.defaultName = fallbackName;
                _cookieImageComboBox.onSelected = (name, _) =>
                {
                    RecordLightEdit("輪郭画像");
                    cookie.image = name;
                    SetCookie(light, cookie);
                };
                _cookieImageComboBox.DrawButton(view);

                if (view.DrawButton("再読込", CookieReloadButtonWidth, rowHeight))
                {
                    LightCookieTextures.Reload();
                    lightManager.ReapplyCookies();
                }
            }
            view.EndLayout();

            if (names.Count == 0)
            {
                view.DrawLabel("PNG を置いてください: " + LightCookieTextures.directory, -1, rowHeight);
            }
        }

        /// <summary>
        /// 輪郭画像コンボの選択位置を返す。XML 由来の値は大文字小文字や区切り文字が一覧と違いうるので、
        /// テクスチャのキャッシュと同じくゆるく照合する。
        /// fallbackName は一覧に無いときだけ表示する名前で、見つかったときは null
        /// (GUIComboBox は defaultName が null でないと選択中の項目より優先して表示するため)
        /// </summary>
        public static int ResolveCookieImageSelection(List<string> names, string image, out string fallbackName)
        {
            var normalized = NormalizeImagePath(image);
            var index = names.FindIndex(
                name => string.Equals(NormalizeImagePath(name), normalized, StringComparison.OrdinalIgnoreCase));

            if (index >= 0)
            {
                fallbackName = null;
            }
            else
            {
                fallbackName = string.IsNullOrEmpty(image) ? "未選択" : image + " (見つかりません)";
            }
            return index;
        }

        private static string NormalizeImagePath(string path)
        {
            return (path ?? "").Replace('/', '\\');
        }

        /// <summary>
        /// 輪郭を反映し、タイムラインのライト定義へ即時に同期させる
        /// (定期収集は 30 フレーム間隔なので、直後の保存で古い値が書かれないように)
        /// </summary>
        private static void SetCookie(Light light, LightCookieData cookie)
        {
            LightCookie.Set(light, cookie);
            MTEP.StudioLightManager.instance.LateUpdate(true);
        }

        /// <summary>影の種類を反映し、輪郭と同じくタイムラインのライト定義へ即時に同期させる</summary>
        private static void SetShadows(Light light, LightShadows shadows)
        {
            light.shadows = shadows;
            MTEP.StudioLightManager.instance.LateUpdate(true);
        }

        /// <summary>キャラの影を反映し、輪郭と同じくタイムラインのライト定義へ即時に同期させる</summary>
        private static void SetCharacterShadow(Light light, bool value)
        {
            light.cullingMask = LightTarget.WithCharacterShadow(light.cullingMask, value);
            MTEP.StudioLightManager.instance.LateUpdate(true);
        }

        private static string GetLightTargetName(LightTargetMode mode)
        {
            switch (mode)
            {
                case LightTargetMode.Character:
                    return "キャラのみ";
                case LightTargetMode.Background:
                    return "背景のみ";
                default:
                    return "全て";
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
