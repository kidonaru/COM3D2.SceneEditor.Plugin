namespace COM3D2.MotionTimelineEditor.Plugin
{
    public interface IMotionData
    {
        int stFrame { get; set; }
        int edFrame { get; set; }
        int stFrameInEdit { get; set; }
        int edFrameInEdit { get; set; }
        int stFrameActive { get; }
        int edFrameActive { get; }
    }

    public abstract class MotionDataBase : IMotionData
    {
        public int stFrame { get; set; }
        public int edFrame { get; set; }
        public int stFrameInEdit { get; set; }
        public int edFrameInEdit { get; set; }

        public int stFrameActive
        {
            get => SceneEditorHack.isPoseEditing ? stFrameInEdit : stFrame;
        }

        public int edFrameActive
        {
            get => SceneEditorHack.isPoseEditing ? edFrameInEdit : edFrame;
        }
    }

    public class MotionData : MotionDataBase
    {
        public ITransformData start;
        public ITransformData end;

        public string name => start.name;
        public int frameNo => stFrame;

        /// <summary>
        /// 区間の始点・終点が同値か。true なら区間中ずっと start の値として扱ってよいため、
        /// 再生時の補間計算を省略できる。
        /// 厳密には UpdateTangent の ±0.01 ナッジ (値が変化する隣接区間を持つ境界キー) で
        /// 微小なタンジェントが残り、補間すると 0.01 スケールでわずかに膨らむことがあるが、
        /// 視認できない誤差なので定数として扱う。
        /// タンジェントと同じく再生データ構築時 (BuildPlayData) に一度だけ求める
        /// </summary>
        public readonly bool isConstant;

        private static TimelineData timeline => TimelineManager.instance.timeline;

        public MotionData(BoneData start, BoneData end)
            : this(start.transform, end.transform, start.frameNo, end.frameNo)
        {
        }

        public MotionData(
            ITransformData start,
            ITransformData end,
            int stFrame,
            int edFrame)
        {
            this.start = start;
            this.end = end;
            this.stFrame = stFrame;
            this.edFrame = edFrame;
            this.isConstant = start.IsSameValues(end);
        }

        public MotionData Clone()
        {
            var newStart = start.Clone();
            var newEnd = end.Clone();
            return new MotionData(newStart, newEnd, stFrame, edFrame);
        }
    }
}