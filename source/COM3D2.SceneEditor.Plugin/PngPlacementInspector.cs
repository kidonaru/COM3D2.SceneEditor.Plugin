using System.Collections.Generic;
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

        private const float DISPLAY_TYPE_COMBO_WIDTH = 100f;
        private const float BLEND_TAB_WIDTH = 60f;
        private const float FADE_ANGLE_STEP = 1f;

        // 見出し列版の DrawTabs を選ばせるため明示する (4 引数だと enum 版の DrawTabs<T> に解決される)
        private const float TAB_MARGIN = 0f;

        // PngDisplayType / PngDecalBlendMode の値順に並べる
        private static readonly List<PngDisplayType> DisplayTypes =
            new List<PngDisplayType> { PngDisplayType.Board, PngDisplayType.Decal };
        private static readonly string[] DisplayTypeLabels = { "板", "デカール" };
        private static readonly string[] BlendModeLabels = { "通常", "乗算", "加算" };

        /// <summary>
        /// 表示タイプのコンボ。開閉状態と選択時の対象を持つため、
        /// 複数の PNG を並べる呼び出し側 (タイムラインの項目 Inspector) に備えて配置物ごとに持つ
        /// </summary>
        private static readonly Dictionary<PngObjectData, GUIComboBox<PngDisplayType>> DisplayTypeComboBoxes =
            new Dictionary<PngObjectData, GUIComboBox<PngDisplayType>>();

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

            var displayTypeComboBox = GetDisplayTypeComboBox(data);
            displayTypeComboBox.currentIndex = (int)data.displayType;
            displayTypeComboBox.DrawButton("表示タイプ", view);
            // デカールを作れず板で見せているときは板の欄を出す
            var isDecal = data.isDecalShown;

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

        private static GUIComboBox<PngDisplayType> GetDisplayTypeComboBox(PngObjectData data)
        {
            GUIComboBox<PngDisplayType> comboBox;
            if (DisplayTypeComboBoxes.TryGetValue(data, out comboBox))
            {
                return comboBox;
            }

            PruneDisplayTypeComboBoxes();
            comboBox = new GUIComboBox<PngDisplayType>
            {
                items = DisplayTypes,
                getName = (type, _) => DisplayTypeLabels[(int)type],
                labelWidth = LABEL_WIDTH,
                buttonSize = new Vector2(DISPLAY_TYPE_COMBO_WIDTH, ROW_HEIGHT),
                contentSize = new Vector2(
                    DISPLAY_TYPE_COMBO_WIDTH, GUIView.GetPopupHeight(DisplayTypes.Count)),
                onSelected = (type, _) =>
                {
                    // ポップアップを開いたまま配置物が消えた場合、削除済みの配置物へ書き込まない
                    if (type == data.displayType || pngManager.FindByRoot(data.rootObject) != data)
                    {
                        return;
                    }
                    RecordPngEdit("表示タイプ");
                    pngManager.SetDisplayType(data, type);
                },
            };
            DisplayTypeComboBoxes.Add(data, comboBox);
            return comboBox;
        }

        /// <summary>削除済みの配置物のコンボを捨てる。新しいコンボを作るときだけ走らせる</summary>
        private static void PruneDisplayTypeComboBoxes()
        {
            var removed = new List<PngObjectData>();
            foreach (var data in DisplayTypeComboBoxes.Keys)
            {
                if (data.rootObject == null)
                {
                    removed.Add(data);
                }
            }
            foreach (var data in removed)
            {
                DisplayTypeComboBoxes.Remove(data);
            }
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
                label = "フェード角",
                labelWidth = LABEL_WIDTH,
                width = -1,
                min = PngDecalProjection.MinFadeAngle,
                max = PngDecalProjection.MaxFadeAngle,
                step = FADE_ANGLE_STEP,
                defaultValue = PngDecalProjection.DefaultFadeAngle,
                value = data.decalFadeAngle,
                onChanged = value =>
                {
                    RecordPngEdit("フェード角");
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
