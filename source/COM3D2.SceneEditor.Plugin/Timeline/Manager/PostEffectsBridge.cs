namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// PostEffects.Plugin の導入判定。実体は MTEUtils の PostEffectsClient にあり、
    /// ここはタイムライン層から呼びやすくするための別名。
    /// PostEffects.Plugin のロードが SceneEditor より後になるため、
    /// false のまま確定させず毎回問い合わせる (接続できた時点で true に変わる)
    /// </summary>
    public static class PostEffectsBridge
    {
        public static bool isAvailable => PostEffectsClient.isAvailable;
    }
}
