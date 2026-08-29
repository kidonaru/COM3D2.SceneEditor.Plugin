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

        /// <summary>キー化中に選べる向け先。「無し」は None が方向指定と衝突するため出さない</summary>
        private static readonly MaidLookMode[] KeyedModes =
        {
            MaidLookMode.カメラ, MaidLookMode.メイド, MaidLookMode.方向指定,
        };

        /// <summary>キー化していないときに選べる向け先 (SE の全モード)</summary>
        private static readonly MaidLookMode[] UnkeyedModes =
        {
            MaidLookMode.カメラ, MaidLookMode.マウス, MaidLookMode.方向指定,
            MaidLookMode.メイド, MaidLookMode.オブジェクト, MaidLookMode.無し,
        };

        /// <summary>
        /// 向け先の選択肢。キー化の有無で選べる値だけが変わり、語彙は共通にする。
        /// コンボボックスへ渡す List を呼び出し側が持ち回るため、毎回複製して返す
        /// </summary>
        public static List<MaidLookMode> GetSelectableModes(bool useHeadKey)
        {
            return new List<MaidLookMode>(useHeadKey ? KeyedModes : UnkeyedModes);
        }

        /// <summary>
        /// キーの注視先種別を統合列挙へ写す (UI 表示用)。
        /// モデル注視は選択肢に出していないため方向指定へ丸める
        /// (StudioModelManager 未移植。TimelineLookRowDrawer の除外と揃える)
        /// </summary>
        public static MaidLookMode ToLookMode(MTEP.LookAtTargetType targetType)
        {
            switch (targetType)
            {
                case MTEP.LookAtTargetType.Camera:
                    return MaidLookMode.カメラ;
                case MTEP.LookAtTargetType.Maid:
                    return MaidLookMode.メイド;
                default:
                    return MaidLookMode.方向指定;
            }
        }

        /// <summary>
        /// 統合列挙をキーの注視先種別へ写す。
        /// キー化できない値 (マウス・任意オブジェクト・無し) は、顔向きキーで駆動する
        /// None へ丸める (選択肢には出さないが、外部から渡っても壊れないようにする)
        /// </summary>
        public static MTEP.LookAtTargetType ToTargetType(MaidLookMode mode)
        {
            switch (mode)
            {
                case MaidLookMode.カメラ:
                    return MTEP.LookAtTargetType.Camera;
                case MaidLookMode.メイド:
                    return MTEP.LookAtTargetType.Maid;
                default:
                    return MTEP.LookAtTargetType.None;
            }
        }

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
                    // メイド注視は SE 側にも同じ概念があるためそのまま写す
                    if (hasTarget)
                    {
                        return MaidLookMode.メイド;
                    }
                    return ResolveNoTargetMode(isEyeSorashi);
                case MTEP.LookAtTargetType.Model:
                    // モデルは SE 側に対応する概念が無いため任意オブジェクトとして扱う
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
        /// モードに関係しない指定 (注視対象・メイド指定・顔向き) は
        /// SE が覚えている値を残す (タイムライン側の都合で消さないため)。
        /// 方向指定のときだけ顔向きキーの指定値で lookX/lookY を駆動する
        /// </summary>
        /// <param name="lookDirection">顔向きキーの指定値 (lookX/lookY、-1〜1)</param>
        /// <param name="targetMaid">注視先がメイドのときの対象</param>
        /// <param name="maidPointType">注視先がメイドのときの部位</param>
        public static void ApplyLookMode(
            Maid maid, MaidLookMode mode, Transform target, Vector2 lookDirection,
            Maid targetMaid, MTEP.MaidPointType maidPointType)
        {
            if (maid == null)
            {
                return;
            }

            var controller = lookController;
            var isDirection = mode == MaidLookMode.方向指定;
            var isMaid = mode == MaidLookMode.メイド;
            controller.SetState(
                maid,
                mode,
                isDirection ? lookDirection.x : controller.GetLookX(maid),
                isDirection ? lookDirection.y : controller.GetLookY(maid),
                mode == MaidLookMode.オブジェクト ? target : controller.GetTarget(maid),
                isMaid ? targetMaid : controller.GetTargetMaid(maid),
                isMaid ? maidPointType : controller.GetMaidPointType(maid));
        }
    }
}
