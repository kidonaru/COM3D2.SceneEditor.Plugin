using System.Collections.Generic;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>身長補正の基準身長 (スロット番号 → ゲーム内身長 cm)。未設定のスロットは補正しない</summary>
    public class CameraHeightBases
    {
        private readonly Dictionary<int, int> _heights = new Dictionary<int, int>();

        public IEnumerable<int> slotNos => _heights.Keys;

        public int Get(int slotNo)
        {
            int height;
            return _heights.TryGetValue(slotNo, out height) ? height : 0;
        }

        /// <summary>0 以下は設定しない (未設定と同じ扱い)</summary>
        public void Set(int slotNo, int height)
        {
            if (height > 0)
            {
                _heights[slotNo] = height;
            }
        }

        /// <summary>
        /// 未設定のスロットに限り取り込む。設定済みを上書きすると、
        /// 別のメイドで開いて基準メイドを選び直したりキーを打ったりしただけで作者の基準が失われる
        /// </summary>
        public void SetIfUnset(int slotNo, int height)
        {
            if (Get(slotNo) <= 0)
            {
                Set(slotNo, height);
            }
        }

        public void Clear()
        {
            _heights.Clear();
        }

        /// <summary>other の設定済みスロットだけを上書きで取り込む。other に無いスロットは残す</summary>
        public void MergeFrom(CameraHeightBases other)
        {
            // 自分自身を通すと、列挙中の辞書へ書き込んで例外になる
            if (other == null || ReferenceEquals(other, this))
            {
                return;
            }

            foreach (var pair in other._heights)
            {
                _heights[pair.Key] = pair.Value;
            }
        }
    }
}
