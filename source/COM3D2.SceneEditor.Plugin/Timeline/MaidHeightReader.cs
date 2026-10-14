namespace COM3D2.MotionTimelineEditor.Plugin
{
    public static class MaidHeightReader
    {
        /// <summary>プロフィールに出るゲーム内身長 (cm)。取得できなければ 0</summary>
        public static int GetHeight(Maid maid)
        {
            if (maid == null)
            {
                return 0;
            }
            // status.body.height は SE の体型変更に遅れる。UpdateBodyParam を呼ぶと体重・バスト等の status まで書き換わる
            return CameraHeightCorrection.CalcHeight(
                maid.GetProp(MPN.sintyou).value,
                maid.GetProp(MPN.DouPer).value,
                maid.GetProp(MPN.KubiScl).value,
                maid.GetProp(MPN.HeadY).value);
        }
    }
}
