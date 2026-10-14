namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// カメラの身長補正のライブ値 (UI で選ぶ基準メイド) と、再生で決まった補間の状態。
    /// 補正するのは slotA と slotB の補正結果を weight で補間した値
    /// </summary>
    public class CameraHeightState
    {
        public const int NoSlot = -1;

        public int liveSlotNo { get; private set; } = NoSlot;
        public int slotA { get; private set; } = NoSlot;
        public int slotB { get; private set; } = NoSlot;
        public float weight { get; private set; }

        /// <summary>基準メイドを選び、補間 (slotA / slotB / weight) もその値へ戻す</summary>
        public void SetLive(int slotNo)
        {
            liveSlotNo = slotNo;
            slotA = slotNo;
            slotB = slotNo;
            weight = 0f;
        }

        /// <summary>
        /// タイムラインの切り替え時に呼ぶ。基準メイドはシーンの設定なので残し、前のタイムラインのキーの補間だけを消す
        /// </summary>
        public void ResetBlend()
        {
            SetLive(liveSlotNo);
        }

        /// <summary>
        /// キーの適用を始める。補間をライブ値へ戻す。
        /// 続く ApplyKeys が呼ばれなければ (キーが無い・最初のキーより前)、ライブ値で補正する
        /// </summary>
        public void BeginApply()
        {
            slotA = liveSlotNo;
            slotB = liveSlotNo;
            weight = 0f;
        }

        /// <summary>t は区間の進み具合 (0〜1、範囲外は収める)</summary>
        public void ApplyKeys(int startSlotNo, int endSlotNo, float t)
        {
            slotA = startSlotNo;
            slotB = endSlotNo;
            weight = t < 0f ? 0f : (t > 1f ? 1f : t);
            liveSlotNo = weight >= 1f ? endSlotNo : startSlotNo;
        }
    }
}
