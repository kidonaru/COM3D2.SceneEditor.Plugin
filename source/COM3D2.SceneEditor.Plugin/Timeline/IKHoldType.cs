namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// IK 固定の対象箇所。
    /// 固定の実体は SE の MaidIKHoldController が持ち、この enum は
    /// キーフレームのボーン名テーブル (TimelineXml の isHoldList・旧データ移行、
    /// MotionTimelineLayer の TransformType 判定) としてのみ使う。
    /// メンバ名は MaidIKHoldType と一致させること (キーのボーン名がメンバ名そのもののため)
    /// </summary>
    public enum IKHoldType
    {
        Arm_R_Joint,
        Arm_R_Tip,
        Arm_L_Joint,
        Arm_L_Tip,
        Foot_R_Joint,
        Foot_R_Tip,
        Foot_L_Joint,
        Foot_L_Tip,
        Max,
    }
}
