namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 固定 IK の目標取り直しを行ってよいフレームかの判定。
    /// ゲーム側の LateUpdate (Maid.OffsetUpdate / TBody.AutoTwist) はプラグインの LateUpdate より
    /// 後に走り、ポーズが変わったフレームでは体の高さオフセットや前腕のスケールがまだ確定していない。
    /// 確定前の位置を目標にすると翌フレームに腕脚が引き戻され、触っていないボーンが動くため、
    /// 要求されたフレームでは取り直さず翌フレーム以降に行う。
    /// Unity 型に依存しない純粋ロジックにしてテストから呼べるようにしている
    /// </summary>
    public static class MaidIKHoldResetGate
    {
        /// <summary>取り直し要求が無い状態のフレーム値</summary>
        public const int NoRequest = -1;

        public static bool ShouldCapture(int requestedFrame, int currentFrame)
        {
            return requestedFrame != NoRequest && currentFrame > requestedFrame;
        }
    }
}
