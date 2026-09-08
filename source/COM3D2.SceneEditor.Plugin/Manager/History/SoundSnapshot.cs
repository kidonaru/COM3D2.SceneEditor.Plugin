namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>BGM ファイル設定のスナップショット。ゲーム BGM の再生状態は含まない (再生操作は履歴に載せない)</summary>
    public class SoundSnapshot : PresetDtoSnapshot<ScenePresetSound>
    {
        public static SoundSnapshot Capture() => (SoundSnapshot)new SoundSnapshot().Init();

        protected override ScenePresetSound CaptureState() => MteEffectsSnapshot.CaptureBgmSettings();
        protected override void ApplyState(ScenePresetSound state) => MteEffectsSnapshot.ApplyBgmSettings(state);
        protected override PresetDtoSnapshot<ScenePresetSound> CreateEmpty() => new SoundSnapshot();
    }
}
