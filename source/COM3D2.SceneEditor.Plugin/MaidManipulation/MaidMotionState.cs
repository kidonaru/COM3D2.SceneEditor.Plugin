using System;
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// ボーン操作時のモーション停止・再開・リセット。
    /// 停止時に再生中クリップ名を控えることで、元のモーションへの復帰（リセット）と
    /// 直前のアニメの再生し直し（再開）を実現する。
    /// 可否判定は IsPlaying / CanPlayMotion / IsMotionStopped で問い合わせる
    /// </summary>
    public static class MaidMotionState
    {
        // 下記 2 つはどちらも「停止した時点で再生されていたクリップ名」を持つが、更新方針が異なる。
        // リセット用は最初の停止のみ記録し、再開用は停止のたびに上書きする

        /// <summary>メイドごとのリセットで戻す先。ボーン編集を始める前のモーション</summary>
        private static readonly Dictionary<Maid, string> _resetClipNames
            = new Dictionary<Maid, string>();

        /// <summary>メイドごとの再生ボタンで再開する先。今当たっているアニメ</summary>
        private static readonly Dictionary<Maid, string> _resumeClipNames
            = new Dictionary<Maid, string>();

        private static Animation GetAnimation(Maid maid)
        {
            if (maid == null || maid.body0 == null || maid.body0.m_Bones == null)
            {
                return null;
            }
            return maid.body0.m_Bones.GetComponent<Animation>();
        }

        /// <summary>
        /// 再生中のモーションを停止し、リセット用・再開用のクリップ名を控える。
        /// ボーンドラッグ開始、ポーズ読込、停止ボタンなど複数箇所から呼ばれる
        /// </summary>
        public static void StopMotion(Maid maid)
        {
            var anim = GetAnimation(maid);
            if (anim == null || !anim.isPlaying)
            {
                return;
            }

            string playingClipName = null;
            foreach (AnimationState state in anim)
            {
                if (anim.IsPlaying(state.name))
                {
                    playingClipName = state.name;
                    break;
                }
            }

            if (playingClipName != null)
            {
                // 再開用は常に最新へ更新する（読み込んだポーズを再生し直せるように）
                _resumeClipNames[maid] = playingClipName;

                // リセット用はポーズ読込による再停止で上書きすると復帰先が
                // 読み込んだポーズになってしまうため初回だけ記録する
                if (!_resetClipNames.ContainsKey(maid))
                {
                    _resetClipNames[maid] = playingClipName;
                }
            }
            anim.Stop();

            // 停止直後のポーズをボーンスライダーの基準として記録する
            MaidBoneSliderController.CaptureBasePose(maid);
        }

        public static bool IsMotionStopped(Maid maid)
        {
            return maid != null && _resetClipNames.ContainsKey(maid);
        }

        /// <summary>
        /// 現在当たっているアニメ名。再生中はそのクリップ名、
        /// 停止中は再開先として控えたクリップ名を返す。どちらもなければ null
        /// </summary>
        public static string GetCurrentClipName(Maid maid)
        {
            var anim = GetAnimation(maid);
            if (anim == null)
            {
                return null;
            }

            if (anim.isPlaying)
            {
                foreach (AnimationState state in anim)
                {
                    if (anim.IsPlaying(state.name))
                    {
                        return state.name;
                    }
                }
            }

            string clipName;
            _resumeClipNames.TryGetValue(maid, out clipName);
            return clipName;
        }

        /// <summary>
        /// 現在当たっているアニメの AnimationState。
        /// 再生位置スライダーの読み書きに使う。クリップが特定できなければ null
        /// </summary>
        public static AnimationState GetCurrentAnimationState(Maid maid)
        {
            var anim = GetAnimation(maid);
            if (anim == null)
            {
                return null;
            }
            var clipName = GetCurrentClipName(maid);
            if (clipName == null || anim.GetClip(clipName) == null)
            {
                return null;
            }
            return anim[clipName];
        }

        /// <summary>
        /// 再生位置(秒)を 0〜length の範囲に折り返して返す。
        /// ループ再生では time が length を超え続けるため、スライダー表示用に丸める
        /// (スタジオモードの MotionWindow.Update と同じ計算)
        /// </summary>
        public static float GetWrappedTime(AnimationState state)
        {
            // 長さ 0 のクリップは折り返し計算がゼロ除算になるため先に返す
            if (state.length <= 0f)
            {
                return 0f;
            }

            var time = state.time;
            if (state.length < time)
            {
                if (state.wrapMode == WrapMode.ClampForever)
                {
                    return state.length;
                }
                return time - state.length * (int)(time / state.length);
            }
            return time;
        }

        /// <summary>
        /// 再生位置(秒)を変更してポーズへ即反映する。
        /// 停止中は Sample のために一時的に有効化し、反映後に停止状態へ戻す
        /// (スタジオモードの MotionWindow.OnChangeMotionSlider と同じ流儀)
        /// </summary>
        public static void SetPlaybackTime(Maid maid, float time)
        {
            var anim = GetAnimation(maid);
            var state = GetCurrentAnimationState(maid);
            if (anim == null || state == null)
            {
                return;
            }

            var wasPlaying = anim.isPlaying;
            state.enabled = true;
            state.weight = 1f;
            state.time = time;
            anim.Sample();

            if (!wasPlaying)
            {
                state.enabled = false;
                // シーク後のポーズをボーンスライダーの基準に取り直す
                MaidBoneSliderController.CaptureBasePose(maid);
            }
        }

        /// <summary>モーションが再生中か。再生中はボーンスライダーの基準が定まらないため操作させない</summary>
        public static bool IsPlaying(Maid maid)
        {
            var anim = GetAnimation(maid);
            return anim != null && anim.isPlaying;
        }

        /// <summary>再生ボタンで再開できるか。停止中かつ再開先のクリップが残っている場合のみ</summary>
        public static bool CanPlayMotion(Maid maid)
        {
            if (maid == null || IsPlaying(maid))
            {
                return false;
            }
            string clipName;
            if (!_resumeClipNames.TryGetValue(maid, out clipName))
            {
                return false;
            }
            var anim = GetAnimation(maid);
            return anim != null && anim.GetClip(clipName) != null;
        }

        /// <summary>
        /// 直近に停止したクリップを再生し直す。
        /// ポーズは動き出すのでボーンスライダーの基準は破棄するが、
        /// リセットの復帰先は残して停止後にまた戻せるようにする
        /// </summary>
        public static void PlayMotion(Maid maid)
        {
            var anim = GetAnimation(maid);
            if (anim == null)
            {
                return;
            }

            string clipName;
            if (!_resumeClipNames.TryGetValue(maid, out clipName)
                || anim.GetClip(clipName) == null)
            {
                return;
            }

            try
            {
                anim.Play(clipName);
                MaidBoneSliderController.ClearBasePose(maid);
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
            }
        }

        /// <summary>
        /// クリップ名と再生位置を指定して再生し直す (履歴の復元用)。
        /// クリップが読み込まれていない場合は何もしない
        /// </summary>
        public static void PlayClip(Maid maid, string clipName, float time)
        {
            var anim = GetAnimation(maid);
            if (anim == null || string.IsNullOrEmpty(clipName) || anim.GetClip(clipName) == null)
            {
                return;
            }

            try
            {
                anim.Play(clipName);
                var state = anim[clipName];
                if (state != null)
                {
                    state.time = time;
                }
                _resumeClipNames[maid] = clipName;
                MaidBoneSliderController.ClearBasePose(maid);
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
            }
        }

        /// <summary>
        /// 停止状態のまま再開先クリップだけ差し替える (履歴の復元用)。
        /// 再生ボタンが undo 前のクリップを掴んだままにならないようにする
        /// </summary>
        public static void SetResumeClip(Maid maid, string clipName)
        {
            if (maid == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(clipName))
            {
                _resumeClipNames.Remove(maid);
            }
            else
            {
                _resumeClipNames[maid] = clipName;
            }
        }

        /// <summary>編集したポーズを破棄し、停止前のモーションを再生し直す</summary>
        public static void ResetPose(Maid maid)
        {
            var anim = GetAnimation(maid);
            if (anim == null)
            {
                return;
            }

            try
            {
                string clipName;
                if (_resetClipNames.TryGetValue(maid, out clipName)
                    && anim.GetClip(clipName) != null)
                {
                    anim.Play(clipName);
                    _resetClipNames.Remove(maid);
                    // 戻した先が「今当たっているアニメ」になるので再開先も揃える。
                    // 放置すると、戻したモーションが自然停止した後の再生で
                    // 破棄したはずのポーズが流れてしまう
                    _resumeClipNames[maid] = clipName;
                    MaidBoneSliderController.ClearBasePose(maid);
                    return;
                }

                // クリップ名が取れていない場合は現在のモーションを流し直す
                anim.Rewind();
                anim.Play();
                _resetClipNames.Remove(maid);
                _resumeClipNames.Remove(maid);
                MaidBoneSliderController.ClearBasePose(maid);
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
            }
        }

        /// <summary>停止状態を破棄する。ポーズは戻さず、リセット対象から外すだけ</summary>
        public static void Discard(Maid maid)
        {
            if (maid != null)
            {
                _resetClipNames.Remove(maid);
                _resumeClipNames.Remove(maid);
                MaidBoneSliderController.ClearBasePose(maid);
            }
        }

        public static void Clear()
        {
            _resetClipNames.Clear();
            _resumeClipNames.Clear();
            MaidBoneSliderController.Clear();
        }
    }
}
