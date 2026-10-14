using System.Collections.Generic;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// メインカメラの構図スナップショット (メイド追従設定・身長補正を含む)。
    /// SceneView カメラは「見え方」であり履歴の対象にしない
    /// </summary>
    public class CameraSnapshot : IStateSnapshot
    {
        private ScenePresetCamera _state;

        public static CameraSnapshot Capture()
        {
            return new CameraSnapshot { _state = CaptureState() };
        }

        /// <summary>現在のメインカメラの構図を記録する。取れなければ null</summary>
        public static ScenePresetCamera CaptureState()
        {
            var mainCamera = GameMain.Instance != null ? GameMain.Instance.MainCamera : null;
            var camera = mainCamera != null ? mainCamera.camera : null;
            if (camera == null)
            {
                return null;
            }

            var aroundAngle = mainCamera.GetAroundAngle();
            var state = new ScenePresetCamera
            {
                targetPos = mainCamera.GetTargetPos(),
                yaw = aroundAngle.x,
                pitch = aroundAngle.y,
                // 手ブレ適用中は揺れ込みの値になるため、揺れ前のロールを記録する
                roll = CameraShakeManager.instance.GetCleanRotationZ(camera),
                distance = mainCamera.GetDistance(),
                fov = camera.fieldOfView,
                // 揺れは Transform ではなく CameraShakeManager のライブ値が真の値
                shakeParams = CameraShakeManager.instance.shakeParams,
            };

            // 追従中は注視点の代わりにオフセットを記録する (CameraTimelineLayer.UpdateFrame と同じ)
            var follow = MTEP.MaidFollowMainCamera.instance;
            if (follow != null)
            {
                state.maidSlotNo = follow.state.maidSlotNo;
                state.maidPointType = (int)follow.state.maidPointType;
                state.followRotation = follow.state.followRotation;

                if (follow.isFollow)
                {
                    state.targetPos = follow.state.offset;
                    if (follow.state.followRotation)
                    {
                        state.yaw = follow.state.yawOffset;
                    }
                }
            }

            var heightManager = CameraHeightManager.instance;
            var slotNo = heightManager.state.liveSlotNo;
            state.heightSlotNo = slotNo;
            state.heightBase = heightManager.bases.Get(slotNo);

            // 再生中の補間 (CameraHeightState の slotA/B/weight) で逆補正すると、
            // 補間を持たないプリセットを同じメイドで読んだときに画面の位置へ戻らない
            if (CanCorrectHeight(state))
            {
                float rootY;
                var scale = GetPresetScale(state, out rootY);
                var pos = state.targetPos;
                pos.y = MTEP.CameraHeightCorrection.Uncorrect(pos.y, rootY, scale);
                state.targetPos = pos;
            }
            return state;
        }

        /// <summary>追従中のオフセットは追従点がもともと身長に合わせて動くため、補正しない</summary>
        private static bool CanCorrectHeight(ScenePresetCamera state)
        {
            return !state.hasFollow && state.hasHeight;
        }

        private static float GetPresetScale(ScenePresetCamera state, out float rootY)
        {
            // タイムラインの基準身長で補正すると、タイムラインの有無や設定で保存時と構図が変わる
            return CameraHeightManager.GetSlotScale(state.heightSlotNo, state.heightBase, out rootY);
        }

        /// <param name="applyHeight">
        /// false なら身長補正をかけずに注視点を置き、身長補正の設定も戻さない。
        /// メイドの呼出が終わる前に適用し、補正だけを後で ApplyHeight でかけるときに使う
        /// </param>
        /// <param name="isPreset">プリセットの適用なら true。履歴の Undo/Redo は false</param>
        public static void ApplyState(ScenePresetCamera state, bool applyHeight = true, bool isPreset = false)
        {
            if (state == null)
            {
                return;
            }

            var mainCamera = GameMain.Instance != null ? GameMain.Instance.MainCamera : null;
            var camera = mainCamera != null ? mainCamera.camera : null;
            if (camera == null)
            {
                return;
            }

            // 追従設定はカメラより先に復元する。追従付きデータの targetPos / yaw はオフセットなので、
            // 追従先がまだロード中 (シーンプリセットはメイド呼び出し完了前にカメラを適用する) でも
            // オフセットだけは控えておき、ロード完了後の MaidFollowMainCamera.LateUpdate に反映を任せる。
            // プリセットは追従状態込みの完全な断面として扱うため、旧形式 (未追従) の適用は追従解除になる
            var follow = MTEP.MaidFollowMainCamera.instance;
            var hasFollow = follow != null && state.hasFollow;
            var hasYawOffset = hasFollow && state.followRotation;
            if (follow != null)
            {
                follow.state.maidSlotNo = state.maidSlotNo;
                follow.state.maidPointType = MTEP.MaidFollowState.ToMaidPointType(state.maidPointType);
                follow.state.followRotation = state.followRotation;
                if (hasFollow)
                {
                    follow.state.offset = state.targetPos;
                }
                if (hasYawOffset)
                {
                    follow.state.yawOffset = state.yaw;
                }
            }

            // オフセット扱いの値は世界座標として書かない (現在の注視点・ヨーを保つ)
            var aroundAngle = mainCamera.GetAroundAngle();
            if (!hasFollow)
            {
                mainCamera.SetTargetPos(applyHeight ? CorrectTargetPos(state) : state.targetPos);
            }
            mainCamera.SetAroundAngle(new Vector2(hasYawOffset ? aroundAngle.x : state.yaw, state.pitch));
            mainCamera.SetDistance(state.distance);
            camera.fieldOfView = state.fov;

            // ロールはオービットモデル外なので、旋回角を確定させた後に Transform へ書く。
            // 手ブレ適用中でも復元値に揺れ分が混ざらないよう CameraShakeManager を経由する
            CameraShakeManager.instance.SetCleanRotationZ(camera, state.roll);

            // 揺れ自体は CameraShakeManager の LateUpdate が反映する (ここでは値を戻すだけ)
            CameraShakeManager.instance.shakeParams = state.shakeParams;

            if (applyHeight)
            {
                RestoreHeightSetting(state, isPreset);
            }

            if (hasFollow)
            {
                // ロード済みなら即座に追従点基準へ置き直す (未ロードなら何もしない)
                follow.Apply();
            }
        }

        /// <summary>
        /// 身長補正の設定を戻す。プリセットは断面として扱うため、補正を持たない旧形式は基準メイドなしになる
        /// </summary>
        /// <param name="isPreset">プリセットの適用なら true。タイムライン未読込のときだけ基準身長を上書きする</param>
        private static void RestoreHeightSetting(ScenePresetCamera state, bool isPreset)
        {
            var heightManager = CameraHeightManager.instance;
            heightManager.state.SetLive(state.heightSlotNo);

            // いないスロットへ入れると、後でそのスロットにメイドを呼んでも「設定済み」で取り込まれない
            if (state.heightBase <= 0
                || MTEP.MaidManager.instance.GetMaidCache(state.heightSlotNo) == null)
            {
                return;
            }

            // 履歴の Undo/Redo で上書きすると、基準身長を手入力した後に無関係なカメラ操作を戻しただけで記録時の値へ戻る。
            // タイムライン読込中に上書きすると、設定済みの基準身長を変えるのは「取得」と手入力だけという作者の基準を守る規則が崩れる
            if (isPreset && MTEP.TimelineManager.instance.timeline == null)
            {
                heightManager.bases.Set(state.heightSlotNo, state.heightBase);
            }
            else
            {
                heightManager.bases.SetIfUnset(state.heightSlotNo, state.heightBase);
            }
        }

        private static Vector3 CorrectTargetPos(ScenePresetCamera state)
        {
            var pos = state.targetPos;
            if (!CanCorrectHeight(state))
            {
                return pos;
            }

            float rootY;
            var scale = GetPresetScale(state, out rootY);
            pos.y = MTEP.CameraHeightCorrection.Correct(pos.y, rootY, scale);
            return pos;
        }

        /// <summary>
        /// ApplyState(state, false) で飛ばした身長補正 (設定の復元と注視点の補正) をかける。
        /// 補正前の値 (state.targetPos) から計算するため、何度呼んでも二重にはかからない
        /// </summary>
        public static void ApplyHeight(ScenePresetCamera state)
        {
            if (state == null)
            {
                return;
            }

            RestoreHeightSetting(state, isPreset: true);
            if (!CanCorrectHeight(state))
            {
                return;
            }

            var mainCamera = GameMain.Instance != null ? GameMain.Instance.MainCamera : null;
            if (mainCamera == null)
            {
                return;
            }
            mainCamera.SetTargetPos(CorrectTargetPos(state));
        }

        public void AddBones(IEnumerable<Transform> targetBones)
        {
        }

        public IStateSnapshot CaptureCurrent() => Capture();

        public void Apply(Maid maid) => ApplyState(_state);

        public bool Approximately(IStateSnapshot other)
        {
            var o = other as CameraSnapshot;
            if (o == null || _state == null || o._state == null)
            {
                return false;
            }

            return _state.targetPos == o._state.targetPos
                && Mathf.Approximately(_state.yaw, o._state.yaw)
                && Mathf.Approximately(_state.pitch, o._state.pitch)
                && Mathf.Approximately(_state.roll, o._state.roll)
                && Mathf.Approximately(_state.distance, o._state.distance)
                && Mathf.Approximately(_state.fov, o._state.fov)
                && _state.maidSlotNo == o._state.maidSlotNo
                && _state.maidPointType == o._state.maidPointType
                && _state.followRotation == o._state.followRotation
                // 基準身長 (heightBase) は Undo の対象外なので比べない
                && _state.heightSlotNo == o._state.heightSlotNo
                && ApproximatelyVector3(
                    _state.shakePositionAmplitude, o._state.shakePositionAmplitude)
                && ApproximatelyVector3(
                    _state.shakeRotationAmplitude, o._state.shakeRotationAmplitude)
                && Mathf.Approximately(_state.shakeFrequencyScale, o._state.shakeFrequencyScale)
                && _state.shakeSeed == o._state.shakeSeed;
        }

        /// <summary>
        /// 成分ごとの近似比較。Vector3 の == は Mathf.Approximately と許容誤差が違うため、
        /// 揺れの判定基準をスカラー値と揃える
        /// </summary>
        private static bool ApproximatelyVector3(Vector3 a, Vector3 b)
        {
            return Mathf.Approximately(a.x, b.x)
                && Mathf.Approximately(a.y, b.y)
                && Mathf.Approximately(a.z, b.z);
        }

        public bool CanApply(Maid maid) => _state != null;
    }
}
