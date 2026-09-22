using System.Collections.Generic;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// 1 フレーム調整で区間 (隣り合うキーの間) が潰れるかの判定。
    /// 潰れた区間は補間されず値が瞬間的に切り替わる
    /// </summary>
    public static class SingleFrameInterval
    {
        /// <param name="isLastInterval">終端キーがボーン全体の最後のキーか。
        /// 最後の区間は Setup の走査対象外なので潰れない</param>
        public static bool IsCollapsed(
            SingleFrameType type,
            int stFrameNo,
            int edFrameNo,
            bool isLastInterval)
        {
            return type != SingleFrameType.None
                && !isLastInterval
                && edFrameNo - stFrameNo == 1;
        }

        /// <summary>キー列の区間 intervalIndex (キー intervalIndex → intervalIndex + 1) が潰れるか</summary>
        public static bool IsCollapsed(SingleFrameType type, IList<int> frameNos, int intervalIndex)
        {
            if (intervalIndex < 0 || intervalIndex + 1 >= frameNos.Count)
            {
                return false;
            }
            return IsCollapsed(
                type,
                frameNos[intervalIndex],
                frameNos[intervalIndex + 1],
                intervalIndex + 1 == frameNos.Count - 1);
        }
    }
}
