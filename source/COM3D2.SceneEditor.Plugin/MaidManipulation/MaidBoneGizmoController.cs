using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// ボーン回転ギズモ。修飾キーで表示対象グループを切り替える（MultipleMaids 準拠）。
    /// Alt=手首/足首/頭、Alt+Ctrl=肘/膝、Alt+Shift=肩/腿/鎖骨。
    /// ギズモはボーンの GameObject に直接付け、GizmoRender がボーン transform を回す
    /// </summary>
    public class MaidBoneGizmoController
    {
        /// <summary>MM のボーンギズモと同じ縮尺</summary>
        private const float BoneGizmoScale = 0.25f;

        private const float BoneGizmoSelectedThick = 0.25f;

        private enum BoneGroup
        {
            Tip,    // 手首/足首/頭（Alt）
            Mid,    // 肘/膝（Alt+Ctrl）
            Root,   // 肩/腿/鎖骨（Alt+Shift）
        }

        private const string HeadBoneName = "Bip01 Head";

        private static readonly Dictionary<BoneGroup, string[]> BoneNames
            = new Dictionary<BoneGroup, string[]>
        {
            { BoneGroup.Tip, new[] { "Bip01 L Hand", "Bip01 R Hand", "Bip01 L Foot", "Bip01 R Foot",
                HeadBoneName } },
            { BoneGroup.Mid, new[] { "Bip01 L Forearm", "Bip01 R Forearm", "Bip01 L Calf", "Bip01 R Calf" } },
            // 鎖骨は肩と同時に整えることが多いため Root グループに同居させる
            { BoneGroup.Root, new[] { "Bip01 L UpperArm", "Bip01 R UpperArm", "Bip01 L Thigh", "Bip01 R Thigh",
                "Bip01 L Clavicle", "Bip01 R Clavicle" } },
        };

        private Maid _maid = null;
        private readonly Dictionary<BoneGroup, List<ModelGizmoRender>> _gizmos
            = new Dictionary<BoneGroup, List<ModelGizmoRender>>();

        /// <summary>頭のギズモ。掴んでいる間だけ顔追従を切るために覚えておく</summary>
        private ModelGizmoRender _headGizmo = null;

        public void SetTarget(Maid maid)
        {
            if (_maid == maid)
            {
                return;
            }

            Destroy();

            if (maid == null || maid.body0 == null || !maid.body0.isLoadedBody
                || maid.body0.m_Bones == null)
            {
                return;
            }

            _maid = maid;

            foreach (var pair in BoneNames)
            {
                var list = new List<ModelGizmoRender>();
                foreach (var boneName in pair.Value)
                {
                    var bone = CMT.SearchObjName(maid.body0.m_Bones.transform, boneName, false);
                    if (bone == null)
                    {
                        // ボーンが見つからない部位はスキップする
                        MTEUtils.LogWarning("ボーンが見つかりません: {0}", boneName);
                        continue;
                    }

                    var gizmo = bone.gameObject.AddComponent<ModelGizmoRender>();
                    gizmo.eRotate = true;
                    gizmo.offsetScale = BoneGizmoScale;
                    gizmo.lineRSelectedThick = BoneGizmoSelectedThick;
                    gizmo.Visible = false;
                    list.Add(gizmo);

                    if (boneName == HeadBoneName)
                    {
                        _headGizmo = gizmo;
                    }
                }
                _gizmos[pair.Key] = list;
            }
        }

        /// <summary>
        /// ギズモを掴んで操作中のボーン名。掴んでいなければ null。
        /// ポーズタブがスライダー表示の自動追従に使う
        /// </summary>
        public string grabbedBoneName
        {
            get
            {
                foreach (var pair in _gizmos)
                {
                    foreach (var gizmo in pair.Value)
                    {
                        if (gizmo != null && gizmo.Visible && GizmoRenderHack.IsGrabbed(gizmo))
                        {
                            return gizmo.transform.name;
                        }
                    }
                }
                return null;
            }
        }

        /// <summary>毎フレーム呼ぶ。修飾キーの状態で表示グループを切り替える</summary>
        public void Update(bool enabled)
        {
            var visibleGroup = enabled ? GetVisibleGroup() : null;

            foreach (var pair in _gizmos)
            {
                var visible = visibleGroup.HasValue && pair.Key == visibleGroup.Value;
                foreach (var gizmo in pair.Value)
                {
                    if (gizmo != null && gizmo.Visible != visible)
                    {
                        gizmo.Visible = visible;
                    }
                }
            }

            // ギズモを掴んだフレームでモーションを止める（動いたままだとボーンが上書きされる）
            if (visibleGroup.HasValue && Input.GetMouseButtonDown(0) && _maid != null)
            {
                MaidMotionState.StopMotion(_maid);
            }

            StopHeadToCamWhileGrabbingHead();
        }

        /// <summary>
        /// 頭のギズモを掴んでいる間は顔追従を切る。
        /// TBody.MoveHeadAndEye が trsHead.localRotation を毎フレーム上書きするため、
        /// 切らないと回した端から追従先へ引き戻される。
        /// boHeadToCam を倒すだけだと HeadToCamPer がフェードし終えるまで Slerp が効き続けるので、
        /// 追従の割合そのものも 0 にする。
        /// 離した後も追従は切ったままにする。戻すと回した頭が即カメラ向きへ引かれて操作が無に帰すため。
        /// 追従の再開は「顔をカメラへ」トグル（MaidOperationWindow）でユーザーが選ぶ
        /// （顔向きドラッグ MaidFaceDragPoint も同じ扱い）
        /// </summary>
        private void StopHeadToCamWhileGrabbingHead()
        {
            if (_maid == null || _maid.body0 == null || _headGizmo == null)
            {
                return;
            }

            if (!GizmoRenderHack.IsGrabbed(_headGizmo))
            {
                return;
            }

            _maid.body0.boHeadToCam = false;
            _maid.body0.HeadToCamPer = 0f;
        }

        /// <summary>修飾キーの組から表示するグループを決める。Alt 非押下なら非表示</summary>
        private static BoneGroup? GetVisibleGroup()
        {
            if (!Input.GetKey(KeyCode.LeftAlt) && !Input.GetKey(KeyCode.RightAlt))
            {
                return null;
            }

            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
            {
                return BoneGroup.Mid;
            }
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
            {
                return BoneGroup.Root;
            }
            return BoneGroup.Tip;
        }

        public void Destroy()
        {
            foreach (var pair in _gizmos)
            {
                foreach (var gizmo in pair.Value)
                {
                    if (gizmo != null)
                    {
                        Object.Destroy(gizmo);
                    }
                }
            }
            _gizmos.Clear();
            _headGizmo = null;
            _maid = null;
        }
    }
}
