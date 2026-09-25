using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// タイムラインの横ズーム (1 フレームあたりの幅) の計算 (純粋ロジック、単体テスト対象)。
    /// 横軸はドープシートとカーブエディタで共有する
    /// </summary>
    public static class TimelineZoomMath
    {
        /// <summary>
        /// ズームの段階。1px が 1 フレームの最小表現で、
        /// 40px を超えると 1 画面に数十フレームしか入らず実用にならない
        /// </summary>
        public static readonly int[] FrameWidthSteps = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 11, 13, 16, 20, 24, 30, 40 };

        /// <summary>フレーム番号ラベル同士の最小間隔 (px)。ラベル幅 50px が重ならない距離</summary>
        public const float MinLabelSpacing = 50f;

        /// <summary>ラベル間隔の候補 (基本間隔の倍率)。30fps で 1 秒・2 秒・10 秒の区切りになる</summary>
        private static readonly int[] LabelIntervalMultipliers = { 1, 2, 6, 12, 60 };

        /// <summary>
        /// キーの菱形の一辺。フレーム幅によらず、ズーム導入前の既定幅 (11px) と同じ大きさに固定する。
        /// 11px 未満の幅では隣のキーと重なって描かれるが、見やすさと掴みやすさを優先する
        /// </summary>
        public const int KeySize = 11;

        /// <summary>この幅未満ではフレームごとの中心線を引かない (1〜3px では線が背景を埋めてしまう)</summary>
        public const int MinFrameLineWidth = 4;

        /// <summary>
        /// 1 段拡大 (direction &gt; 0) / 縮小 (direction &lt; 0) した幅。
        /// 段にない値は最寄りの段 (同距離なら小さい方) に丸めてから動かし、両端でクランプする
        /// </summary>
        public static int StepFrameWidth(int current, int direction)
        {
            var index = 0;
            var bestDistance = int.MaxValue;
            for (var i = 0; i < FrameWidthSteps.Length; i++)
            {
                var distance = Mathf.Abs(FrameWidthSteps[i] - current);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    index = i;
                }
            }

            if (direction > 0)
            {
                index++;
            }
            else if (direction < 0)
            {
                index--;
            }
            index = Mathf.Clamp(index, 0, FrameWidthSteps.Length - 1);
            return FrameWidthSteps[index];
        }

        /// <summary>
        /// 幅を変えたあとも、カーソル下のフレームが同じ画面位置に留まるスクロール位置。
        /// mouseX はビュー左端からの距離
        /// </summary>
        public static float AnchorScrollX(float scrollX, float mouseX, int oldWidth, int newWidth)
        {
            var scrollX2 = (scrollX + mouseX) * newWidth / oldWidth - mouseX;
            return Mathf.Max(0f, scrollX2);
        }

        /// <summary>フレーム番号ラベルと背景の強調線の間隔 (フレーム数)。ラベル同士が重ならない最小の候補</summary>
        public static int LabelInterval(int frameWidth, int baseInterval)
        {
            var interval = baseInterval;
            foreach (var multiplier in LabelIntervalMultipliers)
            {
                interval = baseInterval * multiplier;
                if (interval * frameWidth >= MinLabelSpacing)
                {
                    return interval;
                }
            }
            // どの候補でも足りないときは最後の候補 (最大間隔) を使う
            return interval;
        }

        /// <summary>Ctrl を押しているか (横ズームの修飾キー)</summary>
        public static bool IsControlHeld()
        {
            return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        }

        /// <summary>
        /// ズーム用のホイール量を読む。回していないか、このフレームで処理済みなら 0。
        /// IMGUI の ScrollWheel イベントは手前のコントロールに消費されて届かないことがあるため、
        /// TabBarDrawer 等と同じく Input の軸を直接読む。届いたイベントは下のコントロール
        /// (スクロールビューの縦スクロール等) へ流さないよう消費する。
        /// OnGUI はイベントごとに走るので、lastFrame で 1 フレーム 1 回に絞る
        /// </summary>
        public static float ConsumeWheel(ref int lastFrame)
        {
            var e = Event.current;
            if (e != null && e.type == EventType.ScrollWheel)
            {
                e.Use();
            }

            var wheel = Input.GetAxis("Mouse ScrollWheel");
            if (wheel == 0f || lastFrame == Time.frameCount)
            {
                return 0f;
            }
            lastFrame = Time.frameCount;
            return wheel;
        }
    }
}
