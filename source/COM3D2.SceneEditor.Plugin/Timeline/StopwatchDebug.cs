using System.Diagnostics;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// 処理時間計測のデバッグ用ヘルパー。
    /// COM3D2 (2.0) の Assembly-CSharp にある同名クラスの代替 (COM3D2.5 には存在しないため自前定義)
    /// </summary>
    public class StopwatchDebug
    {
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

        public void ProcessStart()
        {
            _stopwatch.Reset();
            _stopwatch.Start();
        }

        public void ProcessEnd(string processName)
        {
            _stopwatch.Stop();
            MTEUtils.LogDebug("{0}: {1}ms", processName, _stopwatch.ElapsedMilliseconds);
            _stopwatch.Reset();
            _stopwatch.Start();
        }
    }
}
