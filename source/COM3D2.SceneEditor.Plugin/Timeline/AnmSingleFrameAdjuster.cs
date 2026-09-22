using System.Collections.Generic;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>anm へ書き出すキー 1 つ分の入力</summary>
    public struct AnmKeySource
    {
        public int frameNo;
        public float time;
        /// <summary>そのボーンの全キー中での添字 (合成キーは -1)。
        /// Setup は添字 1 以下のキーの対では前区間を延ばさないため、その判定に使う</summary>
        public int boneIndex;
        /// <summary>出力範囲の端を埋めるために合成したキー (実キーとのペアは潰さない)</summary>
        public bool isSynthetic;
        /// <summary>そのボーンの全キー中で最後の実キー。
        /// 出力範囲が activeTrack で切り出されていても、最後の区間かどうかはここで判定する</summary>
        public bool isLast;
    }

    /// <summary>anm へ書き出すキー 1 つ分の潰し結果</summary>
    public struct AnmKeyTiming
    {
        /// <summary>値とタンジェントを取る入力キーの添字。
        /// 潰れて値として現れないキーは出力されず、保持区間では同じキーが 2 回出力される</summary>
        public int source;
        public float time;
        /// <summary>直前区間をステップにする (inTangent を Infinity にする)</summary>
        public bool stepIn;
        /// <summary>直後区間をステップにする (outTangent を Infinity にする)</summary>
        public bool stepOut;
    }

    /// <summary>
    /// anm 出力向けの 1 フレーム調整。PlayDataBase.Setup と同じ手順で区間表を作り、
    /// その再生結果を AnimationCurve のキー列で再現する。
    /// 区間を潰す判定は PlayDataBase.Setup / TimelineLayerBase.UpdateTangent /
    /// TimelineCurveEditor.BuildSegmentFrames / KeyFrameTangentDrawer.IsSingleFrameInterval と揃えること
    /// </summary>
    public static class AnmSingleFrameAdjuster
    {
        /// <summary>同時刻で値が切り替わるときに手前のキーをずらす幅の、フレーム長に対する比。
        /// AnimationCurve は同時刻キーを持てない</summary>
        private const float EpsilonRatio = 0.01f;

        private struct Anchor
        {
            public int source;
            public float time;
            /// <summary>区間の始端 (手前との間は補間しない)</summary>
            public bool isSegmentStart;
        }

        public static void Adjust(
            IList<AnmKeySource> keys,
            SingleFrameType type,
            float frameSeconds,
            List<AnmKeyTiming> result)
        {
            result.Clear();

            if (type == SingleFrameType.None || keys.Count < 2)
            {
                for (var i = 0; i < keys.Count; i++)
                {
                    result.Add(new AnmKeyTiming { source = i, time = keys[i].time });
                }
                return;
            }

            var segmentCount = keys.Count - 1;
            var stTimes = new float[segmentCount];
            var edTimes = new float[segmentCount];
            BuildSegments(keys, type, stTimes, edTimes);

            var epsilon = frameSeconds * EpsilonRatio;

            // 最初の有効区間より前に先頭キーがあれば、そこから保持する (Delay で先頭の対が潰れた場合)
            var firstSegment = 0;
            while (firstSegment < segmentCount && stTimes[firstSegment] >= edTimes[firstSegment])
            {
                firstSegment++;
            }
            if (firstSegment < segmentCount && keys[0].time < stTimes[firstSegment])
            {
                Append(result, new Anchor { source = 0, time = keys[0].time }, epsilon);
            }

            for (var i = 0; i < segmentCount; i++)
            {
                // 長さ 0 の区間は後続の区間に追い越されるので値として現れない
                if (stTimes[i] >= edTimes[i])
                {
                    continue;
                }
                Append(result, new Anchor { source = i, time = stTimes[i], isSegmentStart = true }, epsilon);
                Append(result, new Anchor { source = i + 1, time = edTimes[i] }, epsilon);
            }

            // 窓の末尾の区間が潰れた場合は、後続区間の始端として末尾キーを置く
            Append(result, new Anchor
            {
                source = keys.Count - 1,
                time = edTimes[segmentCount - 1],
                isSegmentStart = true,
            }, epsilon);
        }

        /// <summary>PlayDataBase.Setup と同じ規則で各区間 (キー i → i+1) の始端・終端時刻を書き換える</summary>
        private static void BuildSegments(
            IList<AnmKeySource> keys,
            SingleFrameType type,
            float[] stTimes,
            float[] edTimes)
        {
            var segmentCount = stTimes.Length;
            for (var i = 0; i < segmentCount; i++)
            {
                stTimes[i] = keys[i].time;
                edTimes[i] = keys[i + 1].time;
            }

            for (var i = 0; i < segmentCount; i++)
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
                    // Setup は区間添字 (= A のボーン内添字) が 1 より大きいときだけ前区間を延ばす
                    if (i > 0 && a.boneIndex > 1)
                    {
                        edTimes[i - 1] = edTimes[i];
                    }
                    stTimes[i] = edTimes[i];
                }
                else
                {
                    if (i + 1 < segmentCount)
                    {
                        stTimes[i + 1] = stTimes[i];
                    }
                    edTimes[i] = stTimes[i];
                }
            }
        }

        private static void Append(List<AnmKeyTiming> result, Anchor anchor, float epsilon)
        {
            var next = new AnmKeyTiming { source = anchor.source, time = anchor.time };
            if (result.Count == 0)
            {
                result.Add(next);
                return;
            }

            var lastIndex = result.Count - 1;
            var last = result[lastIndex];

            if (anchor.time <= last.time)
            {
                // 区間の終端と次区間の始端が同じキーなら 1 つにまとめる
                if (anchor.source == last.source)
                {
                    return;
                }

                // 同時刻で値が切り替わるので、手前のキーを ε だけ前へずらして瞬間切替にする
                last.time = anchor.time - epsilon;
                if (lastIndex > 0 && last.time <= result[lastIndex - 1].time)
                {
                    last.time = (result[lastIndex - 1].time + anchor.time) * 0.5f;
                }
                last.stepOut = true;
                next.stepIn = true;
                next.time = anchor.time;
            }
            else if (anchor.isSegmentStart)
            {
                // 区間の間の空白は手前の値を保持する
                last.stepOut = true;
                next.stepIn = true;
            }

            result[lastIndex] = last;
            result.Add(next);
        }
    }
}
