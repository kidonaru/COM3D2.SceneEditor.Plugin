using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>色帯 1 区間ぶんの描画内容。isLast の区間はタイムライン末尾まで伸ばす</summary>
    public struct ColorLaneSegment
    {
        public int startFrameNo;
        public int endFrameNo;
        public bool isLast;
        public Color fromColor;
        public Color toColor;
    }

    /// <summary>
    /// 項目ごとのキー列 (トラック) から、タイムラインの 1 行に出す色帯の区間を組み立てる (純粋ロジック、単体テスト対象)。
    /// 折りたたんだ行では複数の項目を 1 本へ合成する。毎フレーム呼ばれるため作業用のリストは使い回す
    /// </summary>
    public class ColorLaneAggregator
    {
        private struct Key
        {
            public int frameNo;
            public Color color;
            public bool hidden;
        }

        private class Track
        {
            public LaneColorSource source;
            public bool isStep;
            public readonly List<Key> keys = new List<Key>();
            /// <summary>BuildSegments 中の、区間開始フレーム以前で最後のキーの添字</summary>
            public int cursor;
        }

        private readonly List<Track> _trackPool = new List<Track>();
        private int _trackCount;
        private readonly List<Track> _selectedTracks = new List<Track>();
        private readonly List<int> _frameNos = new List<int>();

        public int trackCount => _trackCount;

        public void Clear()
        {
            _trackCount = 0;
        }

        /// <summary>トラックを追加して添字を返す</summary>
        public int AddTrack(LaneColorSource source, bool isStep)
        {
            if (_trackCount == _trackPool.Count)
            {
                _trackPool.Add(new Track());
            }

            var track = _trackPool[_trackCount];
            track.source = source;
            track.isStep = isStep;
            track.keys.Clear();
            return _trackCount++;
        }

        /// <summary>キーはフレーム番号の昇順で追加すること。hidden のキーから次のキーまでは帯に加えない</summary>
        public void AddKey(int trackIndex, int frameNo, Color color, bool hidden)
        {
            _trackPool[trackIndex].keys.Add(new Key
            {
                frameNo = frameNo,
                color = color,
                hidden = hidden,
            });
        }

        /// <summary>
        /// 合成した区間を result へ組み立てる。種類が混ざると読めなくなるため合成する種類を 1 つに絞る:
        /// Color のトラックがあれば先頭の 1 本だけ (全区間非表示でも優先する)、
        /// 無ければ ValueAlpha のトラック、それも無ければ ON/OFF のトラックを合成する。
        /// 合成は区間の両端で最も不透明な色を取る (区間内は両端を結ぶ近似)
        /// </summary>
        public void BuildSegments(List<ColorLaneSegment> result)
        {
            result.Clear();
            SelectTracks();
            if (_selectedTracks.Count == 0)
            {
                return;
            }

            _frameNos.Clear();
            foreach (var track in _selectedTracks)
            {
                track.cursor = -1;
                foreach (var key in track.keys)
                {
                    _frameNos.Add(key.frameNo);
                }
            }
            _frameNos.Sort();

            var lastIndex = _frameNos.Count - 1;
            for (var i = 0; i <= lastIndex; i++)
            {
                var startFrameNo = _frameNos[i];
                if (i < lastIndex && _frameNos[i + 1] == startFrameNo)
                {
                    continue;
                }

                var isLast = i == lastIndex;
                var endFrameNo = isLast ? startFrameNo : _frameNos[i + 1];

                Color fromColor, toColor;
                if (TryCombine(startFrameNo, endFrameNo, isLast, out fromColor, out toColor))
                {
                    result.Add(new ColorLaneSegment
                    {
                        startFrameNo = startFrameNo,
                        endFrameNo = endFrameNo,
                        isLast = isLast,
                        fromColor = fromColor,
                        toColor = toColor,
                    });
                }
            }
        }

        private void SelectTracks()
        {
            _selectedTracks.Clear();

            var hasValueAlpha = false;
            for (var i = 0; i < _trackCount; i++)
            {
                var track = _trackPool[i];
                if (track.keys.Count == 0)
                {
                    continue;
                }
                if (track.source == LaneColorSource.Color)
                {
                    _selectedTracks.Add(track);
                    return;
                }
                hasValueAlpha |= track.source == LaneColorSource.ValueAlpha;
            }

            for (var i = 0; i < _trackCount; i++)
            {
                var track = _trackPool[i];
                if (track.keys.Count > 0 && (!hasValueAlpha || track.source == LaneColorSource.ValueAlpha))
                {
                    _selectedTracks.Add(track);
                }
            }
        }

        /// <summary>区間 [start, end] の両端で各トラックの色を求め、それぞれ最も不透明な色を取る。寄与が無ければ false</summary>
        private bool TryCombine(int startFrameNo, int endFrameNo, bool isLast, out Color fromColor, out Color toColor)
        {
            fromColor = Color.clear;
            toColor = Color.clear;
            var hasContribution = false;

            foreach (var track in _selectedTracks)
            {
                var keys = track.keys;
                while (track.cursor + 1 < keys.Count && keys[track.cursor + 1].frameNo <= startFrameNo)
                {
                    track.cursor++;
                }
                if (track.cursor < 0)
                {
                    continue;
                }

                var key = keys[track.cursor];
                if (key.hidden)
                {
                    continue;
                }

                var trackFrom = key.color;
                var trackTo = key.color;
                var hasNext = track.cursor + 1 < keys.Count;
                if (!track.isStep && !isLast && hasNext)
                {
                    var next = keys[track.cursor + 1];
                    var span = (float)(next.frameNo - key.frameNo);
                    trackFrom = Color.Lerp(key.color, next.color, (startFrameNo - key.frameNo) / span);
                    trackTo = Color.Lerp(key.color, next.color, (endFrameNo - key.frameNo) / span);
                }

                if (!hasContribution || trackFrom.a > fromColor.a)
                {
                    fromColor = trackFrom;
                }
                if (!hasContribution || trackTo.a > toColor.a)
                {
                    toColor = trackTo;
                }
                hasContribution = true;
            }

            return hasContribution;
        }
    }
}
