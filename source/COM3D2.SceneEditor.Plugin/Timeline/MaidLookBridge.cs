using System.Collections.Generic;
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
        /// 向け先の選択肢。キーの注視先種別に写せる値だけを出す。
        /// 「無し」は None が方向指定と衝突するため出さず、そらし演出は目線種別で表す。
        /// 「オブジェクト」は任意 Transform の同定情報が int 3 値のキーに収まらないため出さない
        /// </summary>
        private static readonly MaidLookMode[] SelectableModes =
        {
            MaidLookMode.カメラ, MaidLookMode.マウス, MaidLookMode.メイド,
            MaidLookMode.モデル, MaidLookMode.方向指定,
        };

        /// <summary>
        /// 向け先の選択肢。コンボボックスへ渡す List を呼び出し側が持ち回るため、毎回複製して返す
        /// </summary>
        public static List<MaidLookMode> GetSelectableModes()
        {
            return new List<MaidLookMode>(SelectableModes);
        }

        /// <summary>キーの注視先種別を統合列挙へ写す (UI 表示用)</summary>
        public static MaidLookMode ToLookMode(MTEP.LookAtTargetType targetType)
        {
            switch (targetType)
            {
                case MTEP.LookAtTargetType.Camera:
                    return MaidLookMode.カメラ;
                case MTEP.LookAtTargetType.Mouse:
                    return MaidLookMode.マウス;
                case MTEP.LookAtTargetType.Maid:
                    return MaidLookMode.メイド;
                case MTEP.LookAtTargetType.Model:
                    return MaidLookMode.モデル;
                default:
                    return MaidLookMode.方向指定;
            }
        }

        /// <summary>
        /// 統合列挙をキーの注視先種別へ写す。
        /// キー化できない値 (任意オブジェクト・無し) は、顔向きキーで駆動する
        /// None へ丸める (選択肢には出さないが、外部から渡っても壊れないようにする)
        /// </summary>
        public static MTEP.LookAtTargetType ToTargetType(MaidLookMode mode)
        {
            switch (mode)
            {
                case MaidLookMode.カメラ:
                    return MTEP.LookAtTargetType.Camera;
                case MaidLookMode.マウス:
                    return MTEP.LookAtTargetType.Mouse;
                case MaidLookMode.メイド:
                    return MTEP.LookAtTargetType.Maid;
                case MaidLookMode.モデル:
                    return MTEP.LookAtTargetType.Model;
                default:
                    return MTEP.LookAtTargetType.None;
            }
        }

        /// <summary>
        /// タイムラインの注視先から SE の向け先モードを決める。
        ///
        /// 注視先が無い (種別 None または対象が未解決) ときは、顔向きキーが
        /// 効くよう「方向指定」にする。ただし視線そらし中は TBody の演出が
        /// trsLookTarget == null を要求するため「無し」へ倒す。
        /// カメラとマウスは対象の同定が要らないため、常にそのまま写す
        /// </summary>
        /// <param name="targetType">タイムラインの注視先種別</param>
        /// <param name="hasTarget">注視先の Transform が解決できたか</param>
        /// <param name="isEyeSorashi">目線種別が視線そらしか</param>
        public static MaidLookMode ResolveLookMode(
            MTEP.LookAtTargetType targetType,
            bool hasTarget,
            bool isEyeSorashi)
        {
            switch (targetType)
            {
                case MTEP.LookAtTargetType.Camera:
                    return MaidLookMode.カメラ;
                case MTEP.LookAtTargetType.Mouse:
                    return MaidLookMode.マウス;
                case MTEP.LookAtTargetType.Maid:
                    // メイド注視は SE 側にも同じ概念があるためそのまま写す
                    if (hasTarget)
                    {
                        return MaidLookMode.メイド;
                    }
                    return ResolveNoTargetMode(isEyeSorashi);
                case MTEP.LookAtTargetType.Model:
                    // モデル注視は SE 側にも同じ概念があるためそのまま写す
                    if (hasTarget)
                    {
                        return MaidLookMode.モデル;
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
        /// モードに関係しない指定 (注視対象・メイド指定・モデル指定・顔向き) は
        /// SE が覚えている値を残す (タイムライン側の都合で消さないため)。
        /// 方向指定のときだけ顔向きキーの指定値で lookX/lookY を駆動する
        /// </summary>
        /// <param name="lookDirection">顔向きキーの指定値 (lookX/lookY、-1〜1)</param>
        /// <param name="targetMaid">注視先がメイドのときの対象</param>
        /// <param name="maidPointType">注視先がメイドのときの部位</param>
        /// <param name="targetModelName">注視先がモデルのときの対象のモデル名</param>
        public static void ApplyLookMode(
            Maid maid, MaidLookMode mode, Transform target, Vector2 lookDirection,
            Maid targetMaid, MTEP.MaidPointType maidPointType, string targetModelName)
        {
            if (maid == null)
            {
                return;
            }

            var controller = lookController;
            var isDirection = mode == MaidLookMode.方向指定;
            var isMaid = mode == MaidLookMode.メイド;
            var isModel = mode == MaidLookMode.モデル;
            controller.SetState(
                maid,
                mode,
                isDirection ? lookDirection.x : controller.GetLookX(maid),
                isDirection ? lookDirection.y : controller.GetLookY(maid),
                mode == MaidLookMode.オブジェクト ? target : controller.GetTarget(maid),
                isMaid ? targetMaid : controller.GetTargetMaid(maid),
                isMaid ? maidPointType : controller.GetMaidPointType(maid),
                isModel ? targetModelName : controller.GetTargetModelName(maid));
        }
    }
}
