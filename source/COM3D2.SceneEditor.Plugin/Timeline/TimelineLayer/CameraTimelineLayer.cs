using System;
using System.Collections.Generic;
using System.Xml.Linq;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    using SE = SceneEditor.Plugin;

    [TimelineLayerDesc("カメラ", 20, TimelineLayerCategory.Camera)]
    public class CameraTimelineLayer : TimelineLayerBase
    {
        public override Type layerType => typeof(CameraTimelineLayer);
        public override string layerName => nameof(CameraTimelineLayer);

        public override bool isCameraLayer => true;

        private static Camera camera => PluginUtils.MainCamera;
        private static MaidFollowMainCamera mainFollow => MaidFollowMainCamera.instance;

        public static string CameraBoneName = "camera";
        public static string CameraDisplayName = "カメラ";
        public static string ShakeBoneName = "shake";
        public static string ShakeDisplayName = "手ブレ";

        private List<string> _allBoneNames = new List<string> { CameraBoneName, ShakeBoneName };
        public override List<string> allBoneNames => _allBoneNames;

        private CameraTimelineLayer(int slotNo) : base(slotNo)
        {
        }

        public static CameraTimelineLayer Create(int slotNo)
        {
            return new CameraTimelineLayer(0);
        }

        public override void Init()
        {
            base.Init();
        }

        protected override void InitMenuItems()
        {
            allMenuItems.Clear();

            allMenuItems.Add(new BoneMenuItem(CameraBoneName, CameraDisplayName));
            allMenuItems.Add(new BoneMenuItem(ShakeBoneName, ShakeDisplayName));
        }

        public override bool IsValidData()
        {
            errorMessage = "";
            return true;
        }

        public override void Update()
        {
            base.Update();

            // 手ブレの位相はキーの有無に関わらず再生位置に従わせる。
            // キーを適用したフレームだけ渡すと、最初のキーより手前へシークしたときに
            // 実時間へ切り替わって位相が飛び、動画出力も再現しなくなる
            SE.CameraShakeManager.instance.SetTimelineSeconds(playingTime);

            if (!SceneEditorHack.isPoseEditing)
            {
                ApplyPlayData();
            }
        }

        protected override void ApplyPlayData()
        {
            if (!isCurrent && !config.isCameraSync)
            {
                return;
            }

            // 基底の ApplyPlayData を呼ばなくなるため、同じメイド未ロードのガードを引き継ぐ
            var maid = this.maid;
            if (maid == null || maid.body0 == null || !maid.body0.isLoadedBody)
            {
                return;
            }

            // 手ブレ → カメラの順に適用する (依存の向きを明示するため順序を固定する)
            ApplyPlayDataByType(TransformType.CameraShake);
            ApplyPlayDataByType(TransformType.Camera);
        }

        protected override void ApplyMotion(MotionData motion, float t, bool indexUpdated, MotionPlayData playData)
        {
            if (motion.start.type == TransformType.CameraShake)
            {
                ApplyShakeMotion(motion, t);
                return;
            }

            Vector3 position, eulerAngles;
            float distance, viewAngle;

            var start = motion.start as TransformDataCamera;
            var end = motion.end as TransformDataCamera;

            var t0 = motion.stFrame * timeline.frameDuration;
            var t1 = motion.edFrame * timeline.frameDuration;

            position = PluginUtils.HermiteVector3(
                t0,
                t1,
                start.positionValues,
                end.positionValues,
                t);

            eulerAngles = PluginUtils.HermiteVector3(
                t0,
                t1,
                start.eulerAnglesValues,
                end.eulerAnglesValues,
                t);

            var tempScale = PluginUtils.HermiteVector3(
                t0,
                t1,
                start.scaleValues,
                end.scaleValues,
                t);

            distance = tempScale.x;
            viewAngle = tempScale.y;

            if (config.isFixedFoV && !isCurrent && SceneEditorHack.isPoseEditing)
            {
                viewAngle = 35;
            }

            var uoCamera = GetUOCamera();
            uoCamera.SetDistance(distance);
            uoCamera.SetAroundAngle(new Vector2(eulerAngles.y, eulerAngles.x));
            camera.SetRotationZ(eulerAngles.z);
            camera.fieldOfView = viewAngle;

            // 追従設定は補間せず区間の始点の値を使う (サブカメラレイヤーと同じ)。
            // 追従中は position がオフセット、向き反映中は yaw がヨーオフセットになり、
            // 実際の注視点は MaidFollowMainCamera が LateUpdate で書く
            var follow = mainFollow;
            if (follow == null)
            {
                uoCamera.SetTargetPos(position);
            }
            else
            {
                follow.state.maidSlotNo = start.maidSlotNo;
                follow.state.maidPointType = start.maidPointType;
                follow.state.followRotation = start.followRotation;

                if (follow.isFollow)
                {
                    follow.state.offset = position;
                    follow.state.yawOffset = eulerAngles.y;
                    follow.Apply();
                }
                else
                {
                    uoCamera.SetTargetPos(position);
                }
            }

            if (config.isFixedFocus && !isCurrent && SceneEditorHack.isPoseEditing)
            {
                var currentMaidCache = maidManager.maidCache;
                if (currentMaidCache != null)
                {
                    var target = currentMaidCache.GetPointTransform(MaidPointType.Head);
                    uoCamera.MoveTarget(target.position);
                }
            }

            //MTEUtils.LogDebug("ApplyMotion: position={0}, rotation={1}, distance={2}, viewAngle={3}", position, rotation, distance, viewAngle);
        }

        /// <summary>
        /// 補間済みの揺れパラメータをライブ値へ書き戻す。
        /// ノイズの算出と Transform への適用は CameraShakeManager が行う
        /// (適用は描画直前なので、ゲームロジックからは揺れが見えない)。
        /// キーが無いフレームは何もしないので、ライブ値の振幅がそのまま使われる
        /// (経過秒は Update が毎フレーム渡しているので位相は連続したまま)
        /// </summary>
        private void ApplyShakeMotion(MotionData motion, float t)
        {
            var scratch = LerpScratch<TransformDataCameraShake>(motion, t);
            var shakeParams = scratch.shakeParams;
            shakeParams.seed = ResolveShakeSeed(motion.start as TransformDataCameraShake);

            SE.CameraShakeManager.instance.shakeParams = shakeParams;
        }

        /// <summary>
        /// 使用するシード。補間すると毎フレーム値が変わり、ハッシュのカオス性で周波数・位相が
        /// 不連続にジャンプして波形が壊れるため、区間の始点の値をそのまま使う
        /// (追従設定を補間しないのと同じ方針)
        /// </summary>
        public static int ResolveShakeSeed(TransformDataCameraShake start)
        {
            return start != null ? start.shakeParams.seed : 0;
        }

        public static UltimateOrbitCamera GetUOCamera()
        {
            return camera.GetComponent<UltimateOrbitCamera>();
        }

        public override void UpdateFrame(FrameData frame, bool initialEdit, bool force)
        {
            var uoCamera = GetUOCamera();
            var target = uoCamera.target;
            var angle = uoCamera.GetAroundAngle();
            // 手ブレ適用中は揺れ込みの値になるため、揺れ前のロールを読む
            var rotZ = SE.CameraShakeManager.instance.GetCleanRotationZ(camera);

            var trans = CreateTransformData<TransformDataCamera>(CameraBoneName);
            trans.position = target.position;
            trans.eulerAngles = new Vector3(angle.y, angle.x, rotZ);
            trans.scale = new Vector3(uoCamera.distance, camera.fieldOfView, 0);

            // 追従中は注視点の代わりにオフセットを保存する (ApplyMotion と対称)
            var follow = mainFollow;
            if (follow != null)
            {
                trans.maidSlotNo = follow.state.maidSlotNo;
                trans.maidPointType = follow.state.maidPointType;
                trans.followRotation = follow.state.followRotation;

                if (follow.isFollow)
                {
                    trans.position = follow.state.offset;
                    if (follow.state.followRotation)
                    {
                        trans.eulerAngles = new Vector3(angle.y, follow.state.yawOffset, rotZ);
                    }
                }
            }

            var bone = frame.CreateBone(trans);
            frame.UpdateBone(bone);

            var shakeTrans = CreateTransformData<TransformDataCameraShake>(ShakeBoneName);
            shakeTrans.shakeParams = SE.CameraShakeManager.instance.shakeParams;

            var shakeBone = frame.CreateBone(shakeTrans);
            frame.UpdateBone(shakeBone);

            //MTEUtils.LogDebug("UpdateFromCurrentPose: position={0}, rotation={1}", _cameraManager.CurrentPosition,_cameraManager.CurrentRotation);
        }

        // DCM 連携は未移植のため出力しない
        public override void OutputDCM(XElement songElement)
        {
        }

        public override TransformType GetTransformType(string name)
        {
            if (name == ShakeBoneName)
            {
                return TransformType.CameraShake;
            }
            return TransformType.Camera;
        }
    }
}