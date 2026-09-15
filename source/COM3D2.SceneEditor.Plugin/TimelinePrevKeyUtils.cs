using COM3D2.MotionTimelineEditor.Plugin;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 直前キーの値を引くための共通処理。
    /// ライブ演出系の行描画が、キーフレーム間で角度が飛ばないよう基準値に使う
    /// </summary>
    public static class TimelinePrevKeyUtils
    {
        private static MTEP.TimelineManager timelineManager => MTEP.TimelineManager.instance;

        /// <summary>対象レイヤーの直前キーを引く。レイヤー未追加なら null</summary>
        public static BoneData GetPrevBone<T>(string boneName) where T : TimelineLayerBase
        {
            var layer = timelineManager.GetLayer<T>();
            return layer == null ? null : layer.GetPrevBone(timelineManager.currentFrameNo, boneName);
        }

        /// <summary>
        /// 回転欄はキーフレーム間の角度連続性を保つため直前キーの角度を基準にする。
        /// レイヤー未追加時は初期値へフォールバックする (レイヤー側 GetPrevBone と同じ挙動)
        /// </summary>
        public static Vector3 GetPrevEulerAngles<T>(
            string boneName, Vector3 initialEulerAngles, bool sub = false)
            where T : TimelineLayerBase
        {
            var prevBone = GetPrevBone<T>(boneName);
            if (prevBone == null)
            {
                return initialEulerAngles;
            }

            return sub ? prevBone.transform.subEulerAngles : prevBone.transform.eulerAngles;
        }
    }
}
