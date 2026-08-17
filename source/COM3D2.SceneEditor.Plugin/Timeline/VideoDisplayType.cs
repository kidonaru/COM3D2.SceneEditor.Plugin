namespace COM3D2.MotionTimelineEditor.Plugin
{
    // MTE の MovieManager.cs から enum のみ移植。
    // 動画再生機能自体は未移植だが、タイムライン XML の互換維持のため定義を残す
    public enum VideoDisplayType
    {
        GUI,
        Mesh,
        Backmost,
        Frontmost,
    }
}
