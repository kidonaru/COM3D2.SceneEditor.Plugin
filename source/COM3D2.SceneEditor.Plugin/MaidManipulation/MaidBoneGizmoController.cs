using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// ボーン回転ギズモ。修飾キーで表示対象グループを切り替える（MultipleMaids 準拠）。
    /// Alt=手首/足首/頭/中心、Alt+Ctrl=肘/膝、Alt+Shift=肩/腿/鎖骨。
    /// 指の個別編集中は同じ修飾キーで指関節へ切り替える（先端/中間/根本関節）。
    ///
    /// ギズモの実体はカメラごとの GizmoRenderer が持つ TransformGizmo で、
    /// ここは「どのボーンに出すか」を供給するだけ。
    /// ゲーム側 GizmoRender は掴み判定が Camera.main のスクリーン座標に固定されており
    /// SceneView で操作できないため、自前ギズモへ寄せている
    /// </summary>
    public class MaidBoneGizmoController
    {
        public enum BoneGroup
        {
            Tip,    // 手首/足首/頭/中心（Alt）
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

        /// <summary>指の個別編集中のグループ表示名。体のグループと修飾キーの割り当ては同じ</summary>
        private static readonly Dictionary<BoneGroup, string> FingerGroupDisplayNames
            = new Dictionary<BoneGroup, string>
        {
            { BoneGroup.Tip, "指の先端関節" },
            { BoneGroup.Mid, "指の中間関節" },
            { BoneGroup.Root, "指の根本関節" },
        };

        /// <summary>
        /// 指ギズモの体のギズモに対する表示倍率。指は関節間が数 cm しかなく、
        /// 体と同じ大きさでは隣の指のギズモと重なって掴み分けられない
        /// </summary>
        private const float FingerGizmoSizeRatio = 0.5f;

        private const int HandDigitCount = 5;
        private const int FootDigitCount = 3;

        private const string HeadBoneName = "Bip01 Head";

        /// <summary>中心ボーン。骨盤ドラッグ点と同じ位置にあり、Shift ドラッグの移動対象でもある</summary>
        private const string CenterBoneName = "Bip01";

        private static readonly Dictionary<BoneGroup, string[]> BoneNames
            = new Dictionary<BoneGroup, string[]>
        {
            // 中心は全身の向きを決めるため、最もよく使う Alt グループに同居させる
            { BoneGroup.Tip, new[] { "Bip01 L Hand", "Bip01 R Hand", "Bip01 L Foot", "Bip01 R Foot",
                HeadBoneName, CenterBoneName } },
            { BoneGroup.Mid, new[] { "Bip01 L Forearm", "Bip01 R Forearm", "Bip01 L Calf", "Bip01 R Calf" } },
            // 鎖骨は肩と同時に整えることが多いため Root グループに同居させる
            { BoneGroup.Root, new[] { "Bip01 L UpperArm", "Bip01 R UpperArm", "Bip01 L Thigh", "Bip01 R Thigh",
                "Bip01 L Clavicle", "Bip01 R Clavicle" } },
        };

        private Maid _maid = null;

        /// <summary>グループごとの対象ボーン。SetTarget で 1 回だけ解決する</summary>
        private readonly Dictionary<BoneGroup, List<Transform>> _bones
            = new Dictionary<BoneGroup, List<Transform>>();

        /// <summary>指の個別編集中に使うグループごとの指関節。SetTarget で 1 回だけ解決する</summary>
        private readonly Dictionary<BoneGroup, List<Transform>> _fingerBones
            = new Dictionary<BoneGroup, List<Transform>>();

        private Transform _headBone = null;

        /// <summary>いま表示しているグループ。非表示なら null</summary>
        private BoneGroup? _visibleGroup = null;

        /// <summary>表示中のグループが指関節か（指の個別編集中か）</summary>
        private bool _isFingerMode = false;

        /// <summary>ギズモに渡す一覧。毎フレームの確保を避けるため使い回す</summary>
        private readonly List<Transform> _visibleBones = new List<Transform>();

        /// <summary>
        /// GizmoRenderer からの供給フックを繋ぐ。
        /// マネージャの Init から 1 回だけ呼ぶ
        /// </summary>
        public void RegisterGizmoHooks()
        {
            GizmoRenderer.boneGizmoTargetsProvider = GetVisibleBones;
            GizmoRenderer.onBoneGizmoDragBegin = OnDragBegin;
            GizmoRenderer.boneGizmoSizeRatioProvider = GetSizeRatio;
        }

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

            var bones = maid.body0.m_Bones.transform;
            foreach (var pair in BoneNames)
            {
                _bones[pair.Key] = FindBones(bones, pair.Value);
            }
            foreach (BoneGroup group in System.Enum.GetValues(typeof(BoneGroup)))
            {
                _fingerBones[group] = FindBones(bones, GetFingerBoneNames(group));
            }

            _headBone = CMT.SearchObjName(bones, HeadBoneName, false);
        }

        /// <summary>ボーン名の一覧を解決する。見つからない部位はスキップする</summary>
        private static List<Transform> FindBones(Transform bones, IList<string> boneNames)
        {
            var list = new List<Transform>();
            foreach (var boneName in boneNames)
            {
                var bone = CMT.SearchObjName(bones, boneName, false);
                if (bone == null)
                {
                    MTEUtils.LogWarning("ボーンが見つかりません: {0}", boneName);
                    continue;
                }
                list.Add(bone);
            }
            return list;
        }

        /// <summary>
        /// 指の個別編集中にグループへ出す指関節のボーン名。
        /// 手指は根本 "Finger0" → 中間 "Finger01" → 先端 "Finger02"、
        /// 足指は根本 "Toe0" → 先端 "Toe01" の 2 関節なので中間グループには出さない
        /// </summary>
        public static List<string> GetFingerBoneNames(BoneGroup group)
        {
            string handSuffix;
            string footSuffix;
            switch (group)
            {
                case BoneGroup.Tip:
                    handSuffix = "2";
                    footSuffix = "1";
                    break;
                case BoneGroup.Mid:
                    handSuffix = "1";
                    // 足指は 2 関節のため中間グループには出さない
                    footSuffix = null;
                    break;
                case BoneGroup.Root:
                default:
                    handSuffix = "";
                    footSuffix = "";
                    break;
            }

            var names = new List<string>();
            foreach (var prefix in new[] { "Bip01 R ", "Bip01 L " })
            {
                for (var digit = 0; digit < HandDigitCount; digit++)
                {
                    names.Add(prefix + "Finger" + digit + handSuffix);
                }
                if (footSuffix == null)
                {
                    continue;
                }
                for (var digit = 0; digit < FootDigitCount; digit++)
                {
                    names.Add(prefix + "Toe" + digit + footSuffix);
                }
            }
            return names;
        }

        /// <summary>
        /// ギズモを掴んで操作中のボーン名。掴んでいなければ null。
        /// ポーズタブがスライダー表示の自動追従に使う
        /// </summary>
        public string grabbedBoneName
        {
            get
            {
                var bone = GrabbedBone();
                return bone != null ? bone.name : null;
            }
        }

        /// <summary>
        /// 毎フレーム呼ぶ。修飾キーの状態で表示グループを切り替える。
        /// isFingerMode（指の個別編集中）なら体のボーンの代わりに指関節へ出す
        /// </summary>
        public void Update(bool enabled, bool isFingerMode)
        {
            _visibleGroup = enabled ? GetVisibleGroup() : null;
            _isFingerMode = isFingerMode;
            StopHeadToCamWhileGrabbingHead();
        }

        /// <summary>
        /// 表示中グループのボーン。GizmoRenderer から毎フレーム呼ばれる。
        /// 対象が無いフレームは空リストを返す (null を返しても同じ扱いだが、
        /// 呼び出し側の分岐を減らすため常にリストを返す)。
        ///
        /// 返すのは使い回しの共有バッファで、次の呼び出しで中身が入れ替わる。
        /// SceneView と GameView の GizmoRenderer が同じフレームに順番に呼ぶため、
        /// 呼び出し側はその場で中身を自分の配列へ写し取ること。参照を保持してはいけない
        /// </summary>
        private IList<Transform> GetVisibleBones()
        {
            _visibleBones.Clear();

            var groups = _isFingerMode ? _fingerBones : _bones;
            List<Transform> bones;
            if (!_visibleGroup.HasValue || !groups.TryGetValue(_visibleGroup.Value, out bones))
            {
                return _visibleBones;
            }

            foreach (var bone in bones)
            {
                if (bone != null)
                {
                    _visibleBones.Add(bone);
                }
            }
            return _visibleBones;
        }

        /// <summary>
        /// ギズモを掴んだ瞬間にモーションを止めて履歴を残す。
        /// 動いたままだとボーンが毎フレーム上書きされて操作できない
        /// </summary>
        private void OnDragBegin(Transform bone)
        {
            if (_maid == null || bone == null || !_visibleGroup.HasValue)
            {
                return;
            }

            var displayNames = _isFingerMode ? FingerGroupDisplayNames : GroupDisplayNames;
            MaidMotionState.StopMotion(_maid);
            HistoryManager.instance.BeforeEdit(_maid, HistoryScope.Pose,
                "ギズモ操作: " + displayNames[_visibleGroup.Value], new[] { bone });
        }

        /// <summary>表示中のボーンギズモの大きさ（体のギズモ比）。GizmoRenderer から毎フレーム呼ばれる</summary>
        private float GetSizeRatio()
        {
            return _isFingerMode ? FingerGizmoSizeRatio : 1f;
        }

        /// <summary>
        /// いずれかのビューで掴んでいるボーン。掴んでいなければ null。
        /// SceneView と GameView のどちらで掴んでもよい
        /// </summary>
        private static Transform GrabbedBone()
        {
            var sceneView = SceneViewManager.instance.gizmoRenderer;
            if (sceneView != null && sceneView.draggingBoneTarget != null)
            {
                return sceneView.draggingBoneTarget;
            }

            var gameView = GameViewManager.instance.gizmoRenderer;
            if (gameView != null && gameView.draggingBoneTarget != null)
            {
                return gameView.draggingBoneTarget;
            }

            return null;
        }

        /// <summary>
        /// 頭のギズモを掴んでいる間は顔追従を切る。
        /// TBody.MoveHeadAndEye が trsHead.localRotation を毎フレーム上書きするため、
        /// 切らないと回した端から追従先へ引き戻される。
        /// boHeadToCam を倒すだけだと HeadToCamPer がフェードし終えるまで Slerp が効き続けるので、
        /// 追従の割合そのものも 0 にする。
        /// 離した後も追従は切ったままにする。戻すと回した頭が即カメラ向きへ引かれて操作が無に帰すため。
        /// 追従の再開は表情ウィンドウ視線タブの「メイド目線」を選び直すか、
        /// タイムラインの再生 (UpdateHeadLook) で行う（頭のドラッグ点 MaidFaceDragPoint も同じ扱い）
        /// </summary>
        private void StopHeadToCamWhileGrabbingHead()
        {
            if (_maid == null || _maid.body0 == null || _headBone == null)
            {
                return;
            }

            if (GrabbedBone() != _headBone)
            {
                return;
            }

            _maid.body0.boHeadToCam = false;
            _maid.body0.HeadToCamPer = 0f;
        }

        /// <summary>修飾キーの組から表示するグループを決める。Alt 非押下なら非表示</summary>
        private static BoneGroup? GetVisibleGroup()
        {
            return ResolveGroup(
                Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt),
                Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl),
                Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
        }

        /// <summary>
        /// 修飾キーの組み合わせから表示グループを決める純関数。
        /// Input に触らないので単体テストできる
        /// </summary>
        public static BoneGroup? ResolveGroup(bool alt, bool ctrl, bool shift)
        {
            if (!alt)
            {
                return null;
            }

            if (ctrl)
            {
                return BoneGroup.Mid;
            }
            if (shift)
            {
                return BoneGroup.Root;
            }
            return BoneGroup.Tip;
        }

        public void Destroy()
        {
            _bones.Clear();
            _fingerBones.Clear();
            _visibleBones.Clear();
            _visibleGroup = null;
            _isFingerMode = false;
            _headBone = null;
            _maid = null;
        }
    }
}
