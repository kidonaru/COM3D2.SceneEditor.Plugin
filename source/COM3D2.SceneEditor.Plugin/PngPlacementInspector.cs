using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// Inspector に PNG 配置固有のパラメータを描く。
    /// 位置・回転・拡縮は Inspector 共通の Transform 行が担うため、その続きに足す。
    /// 表示タイプ (板 / デカール) で固有欄を出し分ける
    /// </summary>
    public static class PngPlacementInspector
    {
        private const float LABEL_WIDTH = 70f;
        private const float ROW_HEIGHT = 20f;

        // 表示順の増減量 (< > と << >>)
        private const int RENDER_QUEUE_STEP = 10;
        private const int RENDER_QUEUE_BIG_STEP = 100;

        private const float TAB_WIDTH = 80f;
        private const float BLEND_TAB_WIDTH = 60f;
        private const float FADE_ANGLE_STEP = 1f;

        // 見出し列版の DrawTabs を選ばせるため明示する (4 引数だと enum 版の DrawTabs<T> に解決される)
        private const float TAB_MARGIN = 0f;

        // PngDisplayType / PngDecalBlendMode の値順に並べる
        private static readonly string[] DisplayTypeLabels = { "板", "デカール" };
        private static readonly string[] BlendModeLabels = { "通常", "乗算", "加算" };

        private static PngPlacementManager pngManager => PngPlacementManager.instance;

        /// <summary>選択中が PNG 配置なら固有パラメータを描く。描いたら true</summary>
        /// <param name="colorLabelPrefix">
        /// 色行のラベル (= ピッカーの同定キー) の接頭辞。
        /// 複数の PNG を並べる呼び出し側がキーを一意にするために使う。null なら付けない
        /// </param>
        public static bool Draw(GUIView view, GameObject go, string colorLabelPrefix = null)
        {
            var data = pngManager.FindByRoot(go);
            if (data == null)
            {
                return false;
            }

            view.DrawHorizontalLine(Color.gray);

            var displayIndex = view.DrawTabs(
                DisplayTypeLabels, (int)data.displayType, TAB_WIDTH, ROW_HEIGHT, TAB_MARGIN);
            if (displayIndex != (int)data.displayType)
            {
                RecordPngEdit("表示タイプ");
                pngManager.SetDisplayType(data, (PngDisplayType)displayIndex);
            }
            var isDecal = data.displayType == PngDisplayType.Decal;

            view.BeginHorizontal();
            {
                view.DrawToggle("表示", data.visible, 60, ROW_HEIGHT, value =>
                {
                    RecordPngEdit("表示切替");
                    pngManager.SetVisible(data, value);
                });

                // デカールの root を回すと投影方向が変わるため、ビルボードは板専用
                if (!isDecal)
                {
                    view.DrawToggle("ビルボード", data.billboard, 100, ROW_HEIGHT, value =>
                    {
                        RecordPngEdit("ビルボード切替");
                        pngManager.SetBillboard(data, value);
                    });
                }
            }
            view.EndLayout();

            // ColorPickerWindow はラベル文字列で編集対象を識別するため、
            // 他ウィンドウの色行とラベルを重複させないこと
            var colorLabel = colorLabelPrefix == null ? "PNG色" : colorLabelPrefix + "/PNG色";
            var fieldCache = view.GetColorFieldCache(colorLabel, true);
            view.DrawColor(fieldCache, data.color, Color.white, value =>
            {
                RecordPngEdit("色");
                pngManager.SetColor(data, value, data.brightness);
            });

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "明るさ",
                labelWidth = LABEL_WIDTH,
                width = -1,
                min = 0f,
                max = 2f,
                step = 0.01f,
                defaultValue = 1f,
                value = data.brightness,
                onChanged = value =>
                {
                    RecordPngEdit("明るさ");
                    pngManager.SetColor(data, data.color, value);
                },
            });

            if (isDecal)
            {
                DrawDecalRows(view, data);
            }
            else
            {
                DrawRenderQueueRow(view, data);
            }

            return true;
        }

        /// <summary>表示順は板の描画順で、デカールの描画順はシェーダー側で固定のため板専用</summary>
        private static void DrawRenderQueueRow(GUIView view, PngObjectData data)
        {
            view.DrawIntSelect("表示順", RENDER_QUEUE_STEP, RENDER_QUEUE_BIG_STEP,
                () =>
                {
                    RecordPngEdit("表示順");
                    pngManager.SetRenderQueue(data, PngPlacementManager.DefaultRenderQueue);
                },
                data.renderQueue,
                value =>
                {
                    RecordPngEdit("表示順");
                    pngManager.SetRenderQueue(data, value);
                },
                diff =>
                {
                    RecordPngEdit("表示順");
                    pngManager.SetRenderQueue(data, data.renderQueue + diff);
                });
        }

        private static void DrawDecalRows(GUIView view, PngObjectData data)
        {
            view.BeginHorizontal();
            {
                view.DrawLabel("ブレンド", LABEL_WIDTH, ROW_HEIGHT);
                var blendIndex = view.DrawTabs(
                    BlendModeLabels, (int)data.decalBlendMode, BLEND_TAB_WIDTH, ROW_HEIGHT, TAB_MARGIN);
                if (blendIndex != (int)data.decalBlendMode)
                {
                    RecordPngEdit("ブレンド");
                    pngManager.SetDecalBlendMode(data, (PngDecalBlendMode)blendIndex);
                }
            }
            view.EndLayout();

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "角度フェード",
                labelWidth = LABEL_WIDTH,
                width = -1,
                min = PngDecalProjection.MinFadeAngle,
                max = PngDecalProjection.MaxFadeAngle,
                step = FADE_ANGLE_STEP,
                defaultValue = PngDecalProjection.DefaultFadeAngle,
                value = data.decalFadeAngle,
                onChanged = value =>
                {
                    RecordPngEdit("角度フェード");
                    pngManager.SetDecalFadeAngle(data, value);
                },
            });

            view.DrawToggle("メイドにも投影", data.decalProjectOnMaids, 140, ROW_HEIGHT, value =>
            {
                RecordPngEdit("メイドにも投影");
                pngManager.SetDecalProjectOnMaids(data, value);
            });
        }

        /// <summary>PNG 操作を履歴へ記録する。ドラッグ中の連続変更は 1 件に集約される</summary>
        private static void RecordPngEdit(string label)
        {
            HistoryManager.instance.BeforeEdit(null, HistoryScope.PngPlacement,
                "PNG: " + label);
        }
    }
}
