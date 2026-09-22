using System.Collections.Generic;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>anm へ書き出すキー 1 つ分の入力</summary>
    public struct AnmKeySource
    {
        public int frameNo;
        public float time;
        /// <summary>出力範囲の端を埋めるために合成したキー (実キーとのペアは潰さない)</summary>
        public bool isSynthetic;
        /// <summary>そのボーンの全キー中で最後の実キー。
        /// 出力範囲が activeTrack で切り出されていても、最後の区間かどうかはここで判定する</summary>
        public bool isLast;
    }

    /// <summary>anm へ書き出すキー 1 つ分の潰し結果</summary>
    public struct AnmKeyTiming
    {
        public float time;
        /// <summary>直前区間をステップにする (inTangent を Infinity にする)</summary>
        public bool stepIn;
        /// <summary>直後区間をステップにする (outTangent を Infinity にする)</summary>
        public bool stepOut;
    }

    /// <summary>
    /// anm 出力向けの 1 フレーム調整。PlayDataBase.Setup と同じ規則で
    /// 1 フレーム差のキー対を潰し、区間をステップ化する時刻を算出する。
    /// Unity の AnimationCurve は同時刻キーを持てないため、潰した側は ε だけ手前へずらす
    /// </summary>
    public static class AnmSingleFrameAdjuster
    {
        /// <summary>ε のフレーム長に対する比</summary>
        public const float EpsilonRatio = 0.01f;

        public static AnmKeyTiming[] Adjust(IList<AnmKeySource> keys, SingleFrameType type, float epsilon)
        {
            var result = new AnmKeyTiming[keys.Count];
            for (var i = 0; i < keys.Count; i++)
            {
                result[i].time = keys[i].time;
            }

            if (type == SingleFrameType.None || keys.Count < 2)
            {
                return result;
            }

            for (var i = 0; i < keys.Count - 1; i++)
            {
                var a = keys[i];
                var b = keys[i + 1];
                // 最後の区間 (B がボーン全体の最後のキー) は Setup の走査対象外なので潰さない
                if (a.isSynthetic || b.isSynthetic || b.isLast || b.frameNo - a.frameNo != 1)
                {
                    continue;
                }

                if (type == SingleFrameType.Delay)
                {
                    // A への補間を B の位置まで延長し、B で瞬間切替
                    result[i].time = b.time - epsilon;
                }
                else
                {
                    // B を A の位置へ前倒しし、A は直前で瞬間切替
                    result[i + 1].time = result[i].time;
                    result[i].time = result[i].time - epsilon;
                }
                result[i].stepOut = true;
                result[i + 1].stepIn = true;
            }

            // 連鎖で同時刻・逆順になったキーを末尾から ε 刻みで押し戻す
            for (var i = keys.Count - 2; i >= 0; i--)
            {
                if (result[i].time >= result[i + 1].time)
                {
                    result[i].time = result[i + 1].time - epsilon;
                }
            }

            return result;
        }
    }
}
