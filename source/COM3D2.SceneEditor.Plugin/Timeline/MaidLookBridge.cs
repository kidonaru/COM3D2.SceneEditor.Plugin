using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// タイムラインの視線状態を SE の <see cref="MaidLookController"/> へ流し込むアダプタ。
    ///
    /// TBody.trsLookTarget の書き手は MaidLookController に一本化する。
    /// MTE 側 (MaidCache) はキーフレームの指定 (注視先の種別・対象) だけを持ち、
    /// 実際の向け先はここを通して SE のコントローラへ委譲する。
    ///
    /// Maid.EyeToCamera は目線種別のフラグ設定と同時に trsLookTarget をカメラへ
    /// 書き換えてしまうため使わない。フラグだけを <see cref="ApplyEyeMoveType"/> で写す
    /// </summary>
    public static class MaidLookBridge
    {
        /// <summary>フェード時間 0 指定時の追従速度。Maid.EyeToCamera の下限 (0.001 秒) に合わせる</summary>
        private const float HEAD_TO_CAM_FADE_SPEED = 1f / 0.001f;

        private static MaidLookController lookController
            => MaidManipulateManager.instance.lookController;

        /// <summary>
        /// タイムライン設定から SE の向け先モードを決める。
        /// null は「SE 側の向け先を変更しない」を意味する。
        ///
        /// 注視先が無い (種別 None または対象が未解決) ときは、顔向きキーが
        /// 効くよう「方向指定」にする。ただし視線そらし中は TBody の演出が
        /// trsLookTarget == null を要求するため「無し」へ倒す
        /// </summary>
        /// <param name="useHeadKey">タイムラインの「視線をキー化」</param>
        /// <param name="targetType">タイムラインの注視先種別</param>
        /// <param name="hasTarget">注視先の Transform が解決できたか</param>
        /// <param name="isEyeSorashi">目線種別が視線そらしか</param>
        public static MaidLookMode? ResolveLookMode(
            bool useHeadKey,
            MTEP.LookAtTargetType targetType,
            bool hasTarget,
            bool isEyeSorashi)
        {
            // キー化が無効ならタイムラインは向け先を持たない。SE の設定が正
            if (!useHeadKey)
            {
                return null;
            }

            switch (targetType)
            {
                case MTEP.LookAtTargetType.Camera:
                    return MaidLookMode.カメラ;
                case MTEP.LookAtTargetType.Maid:
                case MTEP.LookAtTargetType.Model:
                    if (hasTarget)
                    {
                        return MaidLookMode.オブジェクト;
                    }
                    return ResolveNoTargetMode(isEyeSorashi);
                default:
                    return ResolveNoTargetMode(isEyeSorashi);
            }
        }

        /// <summary>注視先が無い (種別 None または対象が未解決) ときの向け先</summary>
        private static MaidLookMode ResolveNoTargetMode(bool isEyeSorashi)
        {
            return isEyeSorashi ? MaidLookMode.無し : MaidLookMode.方向指定;
        }

        /// <summary>
        /// 視線をそらす目線種別か。
        /// そらし演出は TBody が trsLookTarget == null かつ boLockHeadAndEye == false の
        /// ときにだけ動かすため、瞳の固定と排他になる
        /// </summary>
        public static bool IsEyeSorashi(Maid.EyeMoveType eyeMoveType)
        {
            return eyeMoveType == Maid.EyeMoveType.顔をそらす
                || eyeMoveType == Maid.EyeMoveType.目だけそらす;
        }

        /// <summary>目線種別で顔を向けるか</summary>
        public static bool IsHeadToCam(Maid.EyeMoveType eyeMoveType)
        {
            switch (eyeMoveType)
            {
                case Maid.EyeMoveType.顔を向ける:
                case Maid.EyeMoveType.顔だけ動かす:
                case Maid.EyeMoveType.顔をそらす:
                case Maid.EyeMoveType.目と顔を向ける:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>目線種別で瞳を向けるか</summary>
        public static bool IsEyeToCam(Maid.EyeMoveType eyeMoveType)
        {
            switch (eyeMoveType)
            {
                case Maid.EyeMoveType.顔を向ける:
                case Maid.EyeMoveType.顔をそらす:
                case Maid.EyeMoveType.目と顔を向ける:
                case Maid.EyeMoveType.目だけ向ける:
                case Maid.EyeMoveType.目だけそらす:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// 目線種別のフラグ (顔/瞳の追従・視線そらし) を TBody へ写す。
        /// Maid.EyeToCamera と同じ組み合わせを設定するが、trsLookTarget は触らない
        /// </summary>
        public static void ApplyEyeMoveType(Maid maid, Maid.EyeMoveType eyeMoveType)
        {
            if (maid == null || maid.body0 == null)
            {
                return;
            }

            var body = maid.body0;
            body.boHeadToCam = IsHeadToCam(eyeMoveType);
            body.boEyeToCam = IsEyeToCam(eyeMoveType);
            body.boEyeSorashi = IsEyeSorashi(eyeMoveType);
            body.HeadToCamFadeSpeed = HEAD_TO_CAM_FADE_SPEED;
        }

        /// <summary>
        /// 向け先モードを SE のコントローラへ反映する。
        /// オブジェクトモード以外では既存の注視対象を残す
        /// (タイムライン側の都合で SE が覚えている対象を消さないため)。
        /// 方向指定のときだけ顔向きキーの指定値で lookX/lookY を駆動し、
        /// それ以外では SE が覚えている顔向きを残す
        /// </summary>
        /// <param name="lookDirection">顔向きキーの指定値 (lookX/lookY、-1〜1)</param>
        public static void ApplyLookMode(
            Maid maid, MaidLookMode mode, Transform target, Vector2 lookDirection)
        {
            if (maid == null)
            {
                return;
            }

            var controller = lookController;
            var isDirection = mode == MaidLookMode.方向指定;
            controller.SetState(
                maid,
                mode,
                isDirection ? lookDirection.x : controller.GetLookX(maid),
                isDirection ? lookDirection.y : controller.GetLookY(maid),
                mode == MaidLookMode.オブジェクト ? target : controller.GetTarget(maid));
        }
    }
}
