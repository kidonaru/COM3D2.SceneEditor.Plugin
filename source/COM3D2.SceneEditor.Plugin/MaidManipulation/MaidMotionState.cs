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

        /// <summary>
        /// このプラグインから適用したモーション / マイポーズの記録。
        /// クリップ名からの逆引きでは表示名やスクリプト経由エントリを特定できないため、
        /// 適用時に何を当てたかをメイドごとに控える。
        /// プラグイン外でモーションが差し替わった場合は古い記録が残るが、
        /// resume / reset と同じく「モーションはこのプラグインが変える」前提に揃える
        /// </summary>
        public sealed class AppliedMotionInfo
        {
            /// <summary>適用した PhotoMotionData.id。マイポーズ適用時は 0</summary>
            public long motionId;
            /// <summary>PhotoMotionData.direct_file。Mod の id 変化時の引き当てに使う</summary>
            public string motionFile;
            /// <summary>「再生中」ラベルに出す表示名</summary>
            public string displayName;
            /// <summary>マイポーズの保存フォルダからの相対パス。モーション適用時は null</summary>
            public string myPosePath;
        }

        /// <summary>メイドごとの適用中モーションの記録</summary>
        private static readonly Dictionary<Maid, AppliedMotionInfo> _appliedMotions
            = new Dictionary<Maid, AppliedMotionInfo>();

        /// <summary>リセットで戻したときに復元する記録。_resetClipNames と対で更新する</summary>
        private static readonly Dictionary<Maid, AppliedMotionInfo> _resetAppliedMotions
            = new Dictionary<Maid, AppliedMotionInfo>();

        /// <summary>適用中モーションの記録。無ければ null</summary>
        public static AppliedMotionInfo GetAppliedMotion(Maid maid)
        {
            if (maid == null)
            {
                return null;
            }
            AppliedMotionInfo info;
            _appliedMotions.TryGetValue(maid, out info);
            return info;
        }

        /// <summary>モーション適用の記録。PhotoMotionUtils.Apply から呼ぶ</summary>
        public static void RecordAppliedMotion(Maid maid, PhotoMotionData data)
        {
            if (maid == null || data == null)
            {
                return;
            }
            _appliedMotions[maid] = new AppliedMotionInfo
            {
                motionId = data.id,
                motionFile = data.direct_file,
                displayName = data.name,
            };
        }

        /// <summary>マイポーズ適用の記録。relativePath は保存フォルダからの相対パス</summary>
        public static void RecordAppliedMyPose(Maid maid, string relativePath)
        {
            if (maid == null || string.IsNullOrEmpty(relativePath))
            {
                return;
            }
            _appliedMotions[maid] = new AppliedMotionInfo
            {
                displayName = System.IO.Path.GetFileName(relativePath),
                myPosePath = relativePath,
            };
        }

        /// <summary>記録を差し替える (履歴の復元用)。null で消去</summary>
        public static void SetAppliedMotion(Maid maid, AppliedMotionInfo info)
        {
            if (maid == null)
            {
                return;
            }
            if (info == null)
            {
                _appliedMotions.Remove(maid);
            }
            else
            {
                _appliedMotions[maid] = info;
            }
        }

        private static Animation GetAnimation(Maid maid)
        {
            if (maid == null || maid.body0 == null || maid.body0.m_Bones == null)
            {
                return null;
            }
            return maid.body0.m_Bones.GetComponent<Animation>();
        }

        /// <summary>
        /// 再生中のモーションを現在フレームで停止し、リセット用・再開用のクリップ名を控える。
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
            AnimationState playingState = null;
            foreach (AnimationState state in anim)
            {
                if (anim.IsPlaying(state.name))
                {
                    playingClipName = state.name;
                    playingState = state;
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
                    CaptureResetApplied(maid);
                }
            }
            // 止めた瞬間のポーズを保つ。anim.Stop() は再生位置を 0 へ巻き戻すため、
            // 止める直前の位置を控えて反映し直す
            var stoppedTime = playingState != null ? GetWrappedTime(playingState) : 0f;
            anim.Stop();
            SampleWhileStopped(anim, playingState, stoppedTime);

            // 停止直後のポーズをボーンスライダーの基準として記録する
            MaidBoneSliderController.CaptureBasePose(maid);
        }

        /// <summary>
        /// 停止したままクリップの指定フレームをポーズへ反映する。
        /// Sample には有効化が要るため、一時的に有効化して戻す
        /// (スタジオモードの MotionWindow と同じ流儀)。
        /// 呼び出し元が停止済みであることを保証すること
        /// </summary>
        private static void SampleWhileStopped(Animation anim, AnimationState state, float time)
        {
            if (state == null)
            {
                return;
            }

            state.enabled = true;
            state.weight = 1f;
            state.time = time;
            anim.Sample();
            state.enabled = false;
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

        /// <summary>
        /// リセットの復帰先を、クリップ名と適用記録の対で今適用したクリップ自身に差し替える。
        /// 停止状態で読み込んだポーズ/アニメ用。StopMotion が控えた「読込前のモーション」は
        /// 常駐枠の中身が差し替わると名前だけ残って別のアニメを指すため、ここで上書きする。
        /// 適用記録 (SetAppliedMotion / RecordAppliedMotion / RecordAppliedMyPose) を
        /// 書いた後に呼ぶこと
        /// </summary>
        public static void SetResetTarget(Maid maid, string clipName)
        {
            if (maid == null || string.IsNullOrEmpty(clipName))
            {
                return;
            }

            _resetClipNames[maid] = clipName;
            CaptureResetApplied(maid);
        }

        /// <summary>
        /// リセットで戻したときにハイライト・表示名も一緒に戻せるよう、今の適用記録を対で控える。
        /// スクリプト経由エントリはクリップ名から再解決できないため、ここで保存するしかない
        /// </summary>
        private static void CaptureResetApplied(Maid maid)
        {
            AppliedMotionInfo applied;
            _appliedMotions.TryGetValue(maid, out applied);
            _resetAppliedMotions[maid] = applied;
        }

        /// <summary>
        /// 編集したポーズを破棄して復帰先へ戻す。
        /// 停止中は停止したまま復帰先の現在フレームを反映し、
        /// 再生中は復帰先のクリップを再生し直す。
        /// 復帰先のクリップが失われている場合、停止中はポーズを変えず、
        /// 再生中は現在のモーションを流し直す (復帰先が不定なので適用記録も破棄する)
        /// </summary>
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
                var hasResetTarget = _resetClipNames.TryGetValue(maid, out clipName)
                    && anim.GetClip(clipName) != null;

                if (!anim.isPlaying)
                {
                    if (!hasResetTarget)
                    {
                        // 復帰先のクリップが失われている。停止中に勝手な再生へ
                        // 切り替えないよう何もしない。記録を消すと IsMotionStopped が
                        // false になり、実際は停止中なのに IK 固定まで効かなくなるため残す
                        // (リセットボタンは有効なままだが、押しても何も起きないだけ)
                        return;
                    }

                    ResetPoseWhileStopped(maid, anim, clipName);
                    return;
                }

                if (hasResetTarget)
                {
                    anim.Play(clipName);
                    _resetClipNames.Remove(maid);
                    // 戻したモーションの適用記録も対で戻す (スクリプト経由エントリの
                    // ハイライトはクリップ名から再現できないため)
                    AppliedMotionInfo resetApplied;
                    _resetAppliedMotions.TryGetValue(maid, out resetApplied);
                    SetAppliedMotion(maid, resetApplied);
                    _resetAppliedMotions.Remove(maid);
                    // 戻した先が「今当たっているアニメ」になるので再開先も揃える。
                    // 放置すると、戻したモーションが自然停止した後の再生で
                    // 破棄したはずのポーズが流れてしまう
                    _resumeClipNames[maid] = clipName;
                    MaidBoneSliderController.ClearBasePose(maid);
                    return;
                }

                // 再生中で復帰先のクリップ名が取れていない場合は現在のモーションを流し直す。
                // 復帰先が不定なので適用記録も破棄する
                anim.Rewind();
                anim.Play();
                _resetClipNames.Remove(maid);
                _resumeClipNames.Remove(maid);
                _appliedMotions.Remove(maid);
                _resetAppliedMotions.Remove(maid);
                MaidBoneSliderController.ClearBasePose(maid);
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
            }
        }

        /// <summary>
        /// 停止したまま復帰先クリップの現在フレームを反映する。
        /// 再生に切り替えるとボーンスライダーの基準が定まらず、そのまま編集を続けられないため。
        /// 停止状態は維持するので、崩すたびに何度でも押して戻せる
        /// </summary>
        private static void ResetPoseWhileStopped(Maid maid, Animation anim, string clipName)
        {
            var state = anim[clipName];
            if (state == null)
            {
                return;
            }

            // 再生位置は動かさず、今のフレームのポーズへ戻す
            SampleWhileStopped(anim, state, state.time);

            // 戻した先が「今当たっているアニメ」になるので再開先も揃える
            _resumeClipNames[maid] = clipName;

            // 復帰先の記録は消さない (停止中の判定と、繰り返しリセットに使う)。
            // 表示名・ハイライトだけ復帰先のものへ戻す
            AppliedMotionInfo resetApplied;
            _resetAppliedMotions.TryGetValue(maid, out resetApplied);
            SetAppliedMotion(maid, resetApplied);

            // 戻した後のポーズをボーンスライダーの基準に取り直す
            MaidBoneSliderController.CaptureBasePose(maid);
        }

        /// <summary>
        /// メイド解除時の全記録の破棄。ストックの Maid は使い回されるため、
        /// 適用記録を残すと次のキャラへ表示名・ハイライトが持ち越されてしまう
        /// </summary>
        public static void Release(Maid maid)
        {
            Discard(maid);
            if (maid != null)
            {
                _appliedMotions.Remove(maid);
            }
        }

        /// <summary>停止状態を破棄する。ポーズは戻さず、リセット対象から外すだけ</summary>
        public static void Discard(Maid maid)
        {
            if (maid != null)
            {
                _resetClipNames.Remove(maid);
                _resumeClipNames.Remove(maid);
                // 当たっているアニメ自体は変わらないため _appliedMotions は保持する
                _resetAppliedMotions.Remove(maid);
                MaidBoneSliderController.ClearBasePose(maid);
            }
        }

        public static void Clear()
        {
            _resetClipNames.Clear();
            _resumeClipNames.Clear();
            _appliedMotions.Clear();
            _resetAppliedMotions.Clear();
            MaidBoneSliderController.Clear();
        }
    }
}
