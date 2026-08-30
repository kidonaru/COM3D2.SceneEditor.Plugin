using COM3D2.MotionTimelineEditor;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// メイド 1 人分のボイス再生パラメータの行 (開始 / 長さ / Fade / 音程 / ボイス名 / 再生)。
    /// SoundWindow のボイスタブと TimelineItemInspector (ボイスレイヤーの項目表示) で共有する。
    ///
    /// 書き込み先はボイスレイヤーがキー化するのと同じ MaidCache で、
    /// どのスナップショットにも含まれないため履歴は記録しない (ウィンドウ側も記録していない)
    /// </summary>
    public static class VoiceRowDrawer
    {
        private static MTEP.Config timelineConfig => MTEP.ConfigManager.instance.config;

        /// <summary>スライダーのラベル幅 (「開始」「Fade」が収まる幅)</summary>
        private const float SliderLabelWidth = 30f;

        /// <summary>テキスト入力のラベル幅 (「ループボイス」が収まる幅)</summary>
        private const float TextLabelWidth = 75f;

        public static void Draw(GUIView view, MTEP.MaidCache maidCache, float rowHeight)
        {
            var maxLength = timelineConfig.voiceMaxLength;

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "開始",
                labelWidth = SliderLabelWidth,
                min = 0f,
                max = maxLength,
                step = 0.01f,
                defaultValue = 0f,
                value = maidCache.oneShotVoiceStartTime,
                onChanged = value => maidCache.oneShotVoiceStartTime = value,
            });

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "長さ",
                labelWidth = SliderLabelWidth,
                min = 0f,
                max = maxLength,
                step = 0.01f,
                defaultValue = 0f,
                value = maidCache.oneShotVoiceLength,
                onChanged = value => maidCache.oneShotVoiceLength = value,
            });

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "Fade",
                labelWidth = SliderLabelWidth,
                min = 0f,
                max = maxLength,
                step = 0.01f,
                defaultValue = 0.1f,
                value = maidCache.voiceFadeTime,
                onChanged = value => maidCache.voiceFadeTime = value,
            });

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "音程",
                labelWidth = SliderLabelWidth,
                min = 0f,
                max = 2f,
                step = 0.01f,
                defaultValue = 1f,
                value = maidCache.voicePitch,
                onChanged = value => maidCache.voicePitch = value,
            });

            view.DrawTextField(new GUIView.TextFieldOption
            {
                label = "ボイス名",
                labelWidth = TextLabelWidth,
                value = maidCache.oneShotVoiceName,
                onChanged = value => maidCache.oneShotVoiceName = value,
            });

            view.DrawTextField(new GUIView.TextFieldOption
            {
                label = "ループボイス",
                labelWidth = TextLabelWidth,
                value = maidCache.loopVoiceName,
                onChanged = value => maidCache.loopVoiceName = value,
            });

            if (view.DrawButton("再生", 100, rowHeight))
            {
                maidCache.PlayOneShotVoice();
            }
        }
    }
}
