using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// ボーン回転ギズモ。修飾キーで表示対象グループを切り替える（MultipleMaids 準拠）。
    /// Alt=手首/足首/頭 + 中心（Bip01）の移動、Alt+Ctrl=肘/膝、Alt+Shift=肩/腿/鎖骨。
    /// 回転ギズモはボーンの GameObject に直接付け、GizmoRender がボーン transform を回す。
    /// 中心の移動ギズモだけは代理オブジェクトに付ける（<see cref="CreateCenterMoveGizmo"/> 参照）
    /// </summary>
    public class MaidBoneGizmoController
    {
        /// <summary>MM のボーンギズモと同じ縮尺</summary>
        private const float BoneGizmoScale = 0.25f;

        private const float BoneGizmoSelectedThick = 0.25f;

        /// <summary>中心の移動ギズモの縮尺。回転ギズモより一回り大きくして胴の中で埋もれないようにする</summary>
        private const float CenterMoveGizmoScale = 0.35f;

        private enum BoneGroup
        {
            Tip,    // 手首/足首/頭（Alt）
            Mid,    // 肘/膝（Alt+Ctrl）
            Root,   // 肩/腿/鎖骨（Alt+Shift）
        }

        private static readonly Dictionary<BoneGroup, string> GroupDisplayNames
            = new Dictionary<BoneGroup, string>
        {
            { BoneGroup.Tip, "手首/足首/頭/中心" },
            { BoneGroup.Mid, "肘/膝" },
            { BoneGroup.Root, "肩/腿/鎖骨" },
        };

        private const string HeadBoneName = "Bip01 Head";

        /// <summary>中心ボーン。位置を持つ唯一のポーズボーンで、移動ギズモの対象</summary>
        private const string CenterBoneName = "Bip01";

        /// <summary>中心の移動ギズモを表示する修飾キーのグループ。肩/腿と重ならない Alt 単独に置く</summary>
        private const BoneGroup CenterMoveGizmoGroup = BoneGroup.Tip;

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

        /// <summary>中心ボーン (Bip01)。移動ギズモの書き戻し先</summary>
        private Transform _centerBone = null;

        /// <summary>中心の移動ギズモ。代理 GameObject に付いている</summary>
        private ModelGizmoRender _centerMoveGizmo = null;

        /// <summary>移動ギズモの代理 GameObject。位置だけ Bip01 と同期し、向きは軸空間設定で決める</summary>
        private GameObject _centerMoveProxy = null;

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

            CreateCenterMoveGizmo(maid);
        }

        /// <summary>
        /// 中心 (Bip01) の移動ギズモ。
        /// GizmoRender の移動ハンドルは付けた transform のローカル軸に沿って動くが、Bip01 は
        /// 前方が世界の上を向く姿勢のため、直付けすると軸の色と実際の移動方向が食い違う。
        /// そこで代理 GameObject にギズモを付け、Update で位置を Bip01 と相互に同期する
        /// </summary>
        private void CreateCenterMoveGizmo(Maid maid)
        {
            var centerBone = CMT.SearchObjName(maid.body0.m_Bones.transform, CenterBoneName, false);
            if (centerBone == null)
            {
                MTEUtils.LogWarning("中心のボーンが見つかりません: {0}", CenterBoneName);
                return;
            }

            _centerBone = centerBone;
            _centerMoveProxy = new GameObject("MIE_CenterMoveGizmo");
            SyncCenterMoveProxyToBone();

            var gizmo = _centerMoveProxy.AddComponent<ModelGizmoRender>();
            // Awake が eRotate を立てるため、移動専用にするには生成後に倒す
            gizmo.eAxis = true;
            gizmo.eRotate = false;
            gizmo.offsetScale = CenterMoveGizmoScale;
            gizmo.Visible = false;
            _centerMoveGizmo = gizmo;
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
                if (isCenterMoveGizmoGrabbed)
                {
                    return CenterBoneName;
                }
                return null;
            }
        }

        private bool isCenterMoveGizmoGrabbed
        {
            get
            {
                return _centerMoveGizmo != null && _centerMoveGizmo.Visible
                    && GizmoRenderHack.IsGrabbed(_centerMoveGizmo);
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

            UpdateCenterMoveGizmo(visibleGroup);

            // ギズモを掴んだフレームでモーションを止める（動いたままだとボーンが上書きされる）。
            // 掴んだギズモは描画時 (OnRenderObject) にしか確定しないため、押下時点で
            // 表示中グループのボーンをまとめて履歴へ控えておく
            if (visibleGroup.HasValue && Input.GetMouseButtonDown(0) && _maid != null)
            {
                MaidMotionState.StopMotion(_maid);
                HistoryManager.instance.BeforeEdit(_maid, HistoryScope.Pose,
                    "ギズモ操作: " + GroupDisplayNames[visibleGroup.Value],
                    GetGroupBones(visibleGroup.Value));
            }

            StopHeadToCamWhileGrabbingHead();
        }

        /// <summary>履歴の記録対象。表示中グループの回転ギズモのボーンと、同居する中心ボーン</summary>
        private IEnumerable<Transform> GetGroupBones(BoneGroup group)
        {
            List<ModelGizmoRender> gizmos;
            if (_gizmos.TryGetValue(group, out gizmos))
            {
                foreach (var gizmo in gizmos)
                {
                    if (gizmo != null)
                    {
                        yield return gizmo.transform;
                    }
                }
            }
            if (group == CenterMoveGizmoGroup && _centerBone != null)
            {
                yield return _centerBone;
            }
        }

        /// <summary>
        /// 移動ギズモの表示切替と Bip01 との同期。
        /// 掴んでいる間はギズモが動かした代理の位置をボーンへ書き戻し、
        /// それ以外はボーン側の変化（モーション・他の操作）へ代理を追従させる
        /// </summary>
        private void UpdateCenterMoveGizmo(BoneGroup? visibleGroup)
        {
            if (_centerMoveGizmo == null || _centerBone == null)
            {
                return;
            }

            // 掴み判定は Visible を書き換える前に取る。先に非表示へ倒すと、まだ描画側で
            // ドラッグ継続中でも掴んでいない扱いになり、前フレームの移動量が書き戻されずに消える
            // （Alt を押したまま Ctrl/Shift を足してグループを切り替えたときなど）
            var wasGrabbed = isCenterMoveGizmoGrabbed;
            if (wasGrabbed)
            {
                _centerBone.position = _centerMoveProxy.transform.position;
            }

            var visible = visibleGroup.HasValue && visibleGroup.Value == CenterMoveGizmoGroup;
            if (_centerMoveGizmo.Visible != visible)
            {
                _centerMoveGizmo.Visible = visible;
            }

            if (!wasGrabbed)
            {
                SyncCenterMoveProxyToBone();
            }
        }

        /// <summary>代理を Bip01 の位置へ置き、向きは軸空間設定（Local=メイドの向き / Global=世界軸）に合わせる</summary>
        private void SyncCenterMoveProxyToBone()
        {
            var proxy = _centerMoveProxy.transform;
            proxy.position = _centerBone.position;
            proxy.rotation = GizmoRenderer.useLocalSpace && _maid != null
                ? _maid.transform.rotation
                : Quaternion.identity;
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

            if (_centerMoveProxy != null)
            {
                Object.Destroy(_centerMoveProxy);
            }
            _centerMoveProxy = null;
            _centerMoveGizmo = null;
            _centerBone = null;
            _maid = null;
        }
    }
}
