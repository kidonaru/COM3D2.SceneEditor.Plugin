using System.Collections.Generic;
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

            SEP.MaidFaceMorphController.AdjustClosedEye(morph);
            morph.FixBlendValues_Face();
        }

        /// <summary>
        /// 強制上書きキーの値でまばたきを上書きする
        /// (ON なら目閉じ値が毎フレーム上書きされるのを防ぎ、OFF ならゲーム側へ返す)。
        /// boMabataki の書き換えは SE 側コントローラへ委譲し、ユーザー設定は解除時に復元される
        /// </summary>
        public void SetMabatakiOverride(Maid maid, bool forceOverride)
        {
            SEP.MaidFaceMorphController.SetMabatakiOverride(maid, forceOverride);

            if (forceOverride)
            {
                var morph = GetFaceMorph(maid);
                if (morph != null)
                {
                    // 進行中のまばたきの目閉じ量が残らないようにする
                    morph.EyeMabataki = 0f;
                }
            }
        }

        /// <summary>まばたきの上書きを解除し、ユーザー設定へ戻す</summary>
        public void ClearMabatakiOverride(Maid maid)
        {
            SEP.MaidFaceMorphController.ClearMabatakiOverride(maid);
        }

        /// <summary>まばたきの上書きを解除するが、現在の実効値は維持する</summary>
        public void CommitMabatakiOverride(Maid maid)
        {
            SEP.MaidFaceMorphController.CommitMabatakiOverride(maid);
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
    }
}
