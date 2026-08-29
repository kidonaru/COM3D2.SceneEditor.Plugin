using System.Collections.Generic;
using UnityEngine;
using SEP = COM3D2.SceneEditor.Plugin;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// 表情モーフ (TMorph) への適用を担うマネージャ。
    /// DCM 本体の MaidFaceManager から再生に必要な部分だけを移植したもので、
    /// 表情プリセット読み込み・ランダム表情・視線制御は持ち込んでいない
    /// </summary>
    public class TimelineFaceManager : ManagerBase
    {
        private static TimelineFaceManager _instance;
        public static TimelineFaceManager instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new TimelineFaceManager();
                }
                return _instance;
            }
        }

        private TimelineFaceManager()
        {
        }

        public float GetMorphValue(Maid maid, string morphName)
        {
            var morph = GetFaceMorph(maid);
            return morph != null ? SEP.MaidFaceMorphController.GetMorphValueByName(morph, morphName) : 0f;
        }

        /// <summary>
        /// モーフ値をまとめて適用する。
        /// FixBlendValues_Face は重いので、1 回の呼び出しにつき 1 回だけ実行する
        /// </summary>
        public void SetMorphValue(Maid maid, Dictionary<string, float> morphMap)
        {
            if (morphMap == null || morphMap.Count == 0)
            {
                return;
            }

            var morph = GetFaceMorph(maid);
            if (morph == null)
            {
                return;
            }

            foreach (var pair in morphMap)
            {
                SetBlendValues(morph, pair.Key, pair.Value);
            }

            AdjustClosedEye(maid, morph);
            morph.FixBlendValues_Face();
        }

        /// <summary>まばたきを止める (タイムラインの目閉じ値が毎フレーム上書きされるのを防ぐ)</summary>
        public void SetMabatakiOff(Maid maid)
        {
            var morph = GetFaceMorph(maid);
            if (morph == null)
            {
                return;
            }

            maid.boMabataki = false;
            morph.EyeMabataki = 0f;
        }

        private static TMorph GetFaceMorph(Maid maid)
        {
            return SEP.MaidFaceMorphController.GetFaceMorph(maid);
        }

        /// <summary>-1 は「このフレームでは触らない」を表す番兵</summary>
        private void SetBlendValues(TMorph morph, string morphName, float value)
        {
            if (value == -1f)
            {
                return;
            }

            SEP.MaidFaceMorphController.SetMorphValueByName(morph, morphName, value);
        }

        /// <summary>
        /// 目閉じ系モーフの合計が 1 を超えないよう配分し直す。
        /// 超過したまま適用すると瞼が破綻するためゲーム側と同じ補正を行う
        /// </summary>
        private void AdjustClosedEye(Maid maid, TMorph morph)
        {
            var adjusted = false;

            var close = GetAdjustValue(maid, morph, "eyeclose");
            var close2 = GetAdjustValue(maid, morph, "eyeclose2");
            var winkL1 = GetAdjustValue(maid, morph, "eyeclose5");
            var winkL2 = GetAdjustValue(maid, morph, "eyeclose6");
            var winkR1 = GetAdjustValue(maid, morph, "eyeclose7");
            var winkR2 = GetAdjustValue(maid, morph, "eyeclose8");

            if (1f < winkL1 + winkL2)
            {
                if (winkL1 < winkL2)
                {
                    winkL1 = GetLimitValue(winkL2);
                }
                else
                {
                    winkL2 = GetLimitValue(winkL1);
                }
                adjusted = true;
            }

            if (1f < winkR1 + winkR2)
            {
                if (winkR1 < winkR2)
                {
                    winkR1 = GetLimitValue(winkR2);
                }
                else
                {
                    winkR2 = GetLimitValue(winkR1);
                }
                adjusted = true;
            }

            var winkTotal = Mathf.Max(winkL1 + winkL2, winkR1 + winkR2);

            if (1f < close + close2 + winkTotal)
            {
                var closeTotal = close + close2;
                // ウィンク単体が 1 を超える XML では winkTotal が 1 を超えたまま残り、
                // 目閉じが両方 0 だと 0 除算で NaN が TMorph へ流れる (移植元にある穴)
                if (closeTotal > 0f)
                {
                    var rest = 1f - winkTotal;
                    close = rest * close / closeTotal;
                    close2 = rest * close2 / closeTotal;
                    adjusted = true;
                }
            }

            if (!adjusted)
            {
                return;
            }

            SetBlendValues(morph, "eyeclose", close);
            SetBlendValues(morph, "eyeclose2", close2);
            SetBlendValues(morph, "eyeclose5", winkL1);
            SetBlendValues(morph, "eyeclose6", winkL2);
            SetBlendValues(morph, "eyeclose7", winkR1);
            SetBlendValues(morph, "eyeclose8", winkR2);
        }

        /// <summary>
        /// 目閉じ補正に使う現在値 (TMorph の生の値)。
        /// ウィンク系モーフを持たない顔では補正しないよう 0 を返す
        /// </summary>
        private float GetAdjustValue(Maid maid, TMorph morph, string morphName)
        {
            if (SEP.MaidFaceMorphController.ResolveMorphIndex(morph, "eyeclose5") < 0)
            {
                return 0f;
            }

            var index = SEP.MaidFaceMorphController.ResolveMorphIndex(morph, morphName);
            return index < 0 ? 0f : morph.GetBlendValues(index);
        }

        private static float GetLimitValue(float value)
        {
            return Mathf.Max(1f - value, 0f);
        }
    }
}
