using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>スコープに対応するスナップショットを生成する</summary>
    public static class SnapshotFactory
    {
        public static IStateSnapshot Capture(
            Maid maid, HistoryScope scope, IEnumerable<Transform> targetBones)
        {
            switch (scope)
            {
                case HistoryScope.Pose:
                    return PoseSnapshot.Capture(maid, targetBones);
                case HistoryScope.Face:
                    return FaceSnapshot.Capture(maid);
                case HistoryScope.Undress:
                    return UndressSnapshot.Capture(maid);
                case HistoryScope.Object:
                    return ObjectSnapshot.Capture(targetBones);
                case HistoryScope.IK:
                    return IKSnapshot.Capture(maid);
                case HistoryScope.Background:
                    return BackgroundSnapshot.Capture();
                case HistoryScope.Light:
                    return LightSnapshot.Capture();
                case HistoryScope.Camera:
                    return CameraSnapshot.Capture();
                case HistoryScope.Placement:
                    return PlacementSnapshot.Capture();
                case HistoryScope.Gravity:
                    return GravitySnapshot.Capture(maid);
                case HistoryScope.PngPlacement:
                    return PngPlacementSnapshot.Capture();
            }

            MTEUtils.LogError("未対応の履歴スコープです: {0}", scope);
            return null;
        }
    }
}
