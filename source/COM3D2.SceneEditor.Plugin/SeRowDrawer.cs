using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 効果音の再生パラメータの行 (SE 名 / 再生間隔 / ループ / 再生・初期化)。
    /// SoundWindow の効果音タブと TimelineItemInspector (効果音レイヤーの項目表示) で共有する。
    ///
    /// 書き込み先は効果音レイヤーがキー化するのと同じ TimelineSeManager で、
    /// どのスナップショットにも含まれないため履歴は記録しない (ウィンドウ側も記録していない)。
    /// コンボボックスの開閉状態と一覧のキャッシュを持つため、描画するビューごとに
    /// インスタンスを分ける
    /// </summary>
    public class SeRowDrawer
    {
        private static MTEP.TimelineSeManager seManager => MTEP.TimelineSeManager.instance;
        private static MTEP.Config timelineConfig => MTEP.ConfigManager.instance.config;

        /// <summary>再生間隔スライダーのラベル幅</summary>
        private const float IntervalLabelWidth = 60f;

        /// <summary>タイムライン固有の追加 SE と公式 SE を連結したコンボボックス用の一覧</summary>
        private readonly List<string> _seNames = new List<string>();

        private readonly GUIComboBox<string> _seNameComboBox = new GUIComboBox<string>
        {
            getName = (seName, index) => seName,
        };

        /// <summary>_seNames を構築した時点のタイムライン。切替の検出用に実体で持つ</summary>
        private MTEP.TimelineData _seNamesTimeline = null;

        /// <summary>_seNames を構築した時点の世代。InvalidateSeNames のたびに進む</summary>
        private int _seNamesGeneration = -1;

        /// <summary>
        /// 追加 SE 一覧の世代。ドロワーはウィンドウ側と Inspector 側に別々に存在するため、
        /// 全インスタンスへ「引き直し」を伝えられるよう静的に持つ
        /// </summary>
        private static int _seNamesGenerationSource = 0;

        /// <summary>
        /// 追加 SE を増減したときに呼ぶ。件数の比較だけでは、追加と削除が同数で
        /// 相殺されたときに古い一覧が残ってしまう
        /// </summary>
        public static void InvalidateSeNames()
        {
            _seNamesGenerationSource++;
        }

        private void UpdateSeNames(MTEP.TimelineData timeline)
        {
            _seNamesTimeline = timeline;
            _seNamesGeneration = _seNamesGenerationSource;
            _seNames.Clear();
            _seNames.AddRange(timeline.additionalSeNames);
            _seNames.AddRange(seManager.seNames);
        }

        public void Draw(GUIView view, MTEP.TimelineData timeline, float rowHeight)
        {
            var updated = false;

            // 追加 SE はタイムラインごとの内容。切替時・増減時・公式 SE の読み込み後に引き直す
            if (_seNamesTimeline != timeline ||
                _seNamesGeneration != _seNamesGenerationSource ||
                _seNames.Count != timeline.additionalSeNames.Count + seManager.seNames.Count)
            {
                UpdateSeNames(timeline);
            }

            _seNameComboBox.items = _seNames;
            if (_seNameComboBox.currentItem != seManager.currentSeName)
            {
                _seNameComboBox.currentIndex = _seNameComboBox.items.IndexOf(seManager.currentSeName);
            }
            _seNameComboBox.onSelected = (seName, _) =>
            {
                seManager.currentSeName = seName;
                updated = true;
            };

            _seNameComboBox.DrawButton("SE名", view);

            updated |= view.DrawSliderValue(
                new GUIView.SliderOption
                {
                    label = "再生間隔",
                    labelWidth = IntervalLabelWidth,
                    min = 0f,
                    max = timelineConfig.voiceMaxLength,
                    step = 0.01f,
                    defaultValue = 0f,
                    value = seManager.currentInterval,
                    onChanged = value => seManager.currentInterval = value,
                });

            view.DrawToggle("ループ", seManager.currentIsLoop, 80, rowHeight, newValue =>
            {
                seManager.currentIsLoop = newValue;
                updated = true;
            });

            view.BeginHorizontal();
            {
                if (view.DrawButton("再生", 100, rowHeight))
                {
                    updated = true;
                }

                if (view.DrawButton("初期化", 100, rowHeight))
                {
                    seManager.PlaySe("", 0f, false);
                }
            }
            view.EndLayout();

            if (updated)
            {
                seManager.StopSe();
                seManager.PlaySe(seManager.currentSeName, seManager.currentInterval, seManager.currentIsLoop);
            }
        }
    }
}
