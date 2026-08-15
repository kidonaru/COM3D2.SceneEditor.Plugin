using System.Collections.Generic;
using RootMotion.FinalIK;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>ドラッグ点がチェーンのどこを掴んでいるか</summary>
    public enum MaidIKChainPoint
    {
        /// <summary>手首/足首/胸の先端</summary>
        Tip,
        /// <summary>肘/膝</summary>
        Joint,
    }

    /// <summary>
    /// 四肢 1 本ぶんの IK チェーン。ゲーム本体のポーズ編集（LimbControl）と同じく
    /// FinalIK の FABRIK を 1 つ持ち、掴んだ点に応じてチェーンを組み替えて解く。
    /// TBody.IKCMO と違い IK データ（AIKCtrl / IKCtrlData）に依存しないので、
    /// 2.0 と 2.5 で分岐が要らず、2.5 のブレンド固着（target が原点へ差し替わる）も起きない
    /// </summary>
    public class MaidIKChain
    {
        private readonly Transform _rootBone;   // UpperArm / Thigh / Mune_*
        private readonly Transform _midBone;    // Forearm / Calf（胸は null）
        private readonly Transform _tipBone;    // Hand / Foot / Mune_*_sub

        /// <summary>
        /// ボーン階層の最上位。チェーンの根元（_rootBone）とは別物で、
        /// solver の基準として渡す（LimbControl も階層の最上位を渡している）
        /// </summary>
        private readonly Transform _hierarchyRoot;

        /// <summary>
        /// 肘/膝のヒンジ制限。値はゲーム側 IKManager.AddLimitComponen と同じ
        /// （腕・脚とも軸は Z、0〜170 度）
        /// </summary>
        private static readonly Vector3 JointHingeAxis = new Vector3(0f, 0f, 1f);
        private const float JointHingeMin = 0f;
        private const float JointHingeMax = 170f;

        private FABRIK _fabrik;

        /// <summary>ボーン単位で共有するヒンジと参照カウント</summary>
        private class SharedHinge
        {
            public RotationLimitHinge hinge;
            public int refCount;
        }

        /// <summary>
        /// 自前で付けたヒンジのボーン単位の共有台帳。ドラッグ用と IK 固定用など
        /// 複数のチェーンが同じ肘/膝ボーンに作られるため、インスタンス所有にすると
        /// 先に Destroy された側がヒンジごと消し、残った側の可動域制限が失われる
        /// </summary>
        private static readonly Dictionary<Transform, SharedHinge> _sharedHinges
            = new Dictionary<Transform, SharedHinge>();

        /// <summary>共有ヒンジの参照を持っているか（Destroy 時に参照カウントを返す）</summary>
        private bool _hasHingeRef;

        /// <summary>Solve 用の使い回しターゲット。ドラッグ用の外部 target とは別に持つ</summary>
        private Transform _solveTarget;

        /// <summary>
        /// チェーンを構成するボーン (root/mid/tip、胸は mid なし)。操作履歴の記録対象用。
        /// root/tip はコンストラクタで非 null が保証される
        /// </summary>
        public IEnumerable<Transform> bones
        {
            get
            {
                yield return _rootBone;
                if (_midBone != null)
                {
                    yield return _midBone;
                }
                yield return _tipBone;
            }
        }

        public MaidIKChain(Transform rootBone, Transform midBone, Transform tipBone)
        {
            _rootBone = rootBone;
            _midBone = midBone;
            _tipBone = tipBone;
            _hierarchyRoot = rootBone.root;

            AddJointHinge();

            _fabrik = rootBone.gameObject.AddComponent<FABRIK>();

            // ゲーム側 LimbControl と同じ設定。fixTransforms を切らないと
            // 解く前に毎フレーム元の姿勢へ戻され、モーション停止中の手付けが効かない
            _fabrik.fixTransforms = false;
            _fabrik.solver.maxIterations = 1;
            _fabrik.solver.Initiate(_hierarchyRoot);
        }

        /// <summary>
        /// 肘/膝に可動域制限を付ける。IKSolverFABRIK は useRotationLimits が既定 true で、
        /// チェーン上のボーンに付いた RotationLimit を拾って解に反映する。
        /// これが無いと肘/膝が逆方向にも自由に折れて姿勢が崩れる
        /// </summary>
        private void AddJointHinge()
        {
            // 胸は中間関節が無いので制限しない
            if (_midBone == null)
            {
                return;
            }

            // 別のチェーンが既に付けたヒンジなら参照カウントだけ増やして共有する
            SharedHinge shared;
            if (_sharedHinges.TryGetValue(_midBone, out shared))
            {
                shared.refCount++;
                _hasHingeRef = true;
                return;
            }

            // スタジオを開いた後などゲーム側 IKManager が既に付けている場合は触らない
            if (_midBone.GetComponent<RotationLimit>() != null)
            {
                return;
            }

            // RotationLimit は AddComponent 時の Awake で localRotation を可動域の基準として
            // 取り込む。基準をボーンのゼロ回転に揃えるため、ゲーム側 IKManager と同じく
            // 一時的に identity にしてから付ける
            var localRotation = _midBone.localRotation;
            _midBone.localRotation = Quaternion.identity;

            RotationLimitHinge hinge;
            // 復元を落とすとボーンが identity のまま残りポーズが壊れるので finally で戻す
            try
            {
                hinge = _midBone.gameObject.AddComponent<RotationLimitHinge>();
                hinge.axis = JointHingeAxis;
                hinge.min = JointHingeMin;
                hinge.max = JointHingeMax;
            }
            finally
            {
                _midBone.localRotation = localRotation;
            }

            // 有効のままだと RotationLimit 自身の LateUpdate が非ドラッグ時にもポーズを
            // 書き換えてしまう。solver は無効なコンポーネントも参照するので支障は無い
            hinge.enabled = false;

            _sharedHinges.Add(_midBone, new SharedHinge { hinge = hinge, refCount = 1 });
            _hasHingeRef = true;
        }

        /// <summary>
        /// 掴んだ点に応じてチェーンを張り、target を差す。
        /// jointLock（Ctrl）では根元のボーンをチェーンから外すので肘/膝が動かない
        /// </summary>
        public void BeginDrag(MaidIKChainPoint point, bool jointLock, Transform target)
        {
            if (_fabrik == null)
            {
                return;
            }

            var solver = _fabrik.solver;

            // 張り替え前に空にする（LimbControl と同じ手順。積み残しを防ぐ）
            solver.SetChain(new Transform[0], _hierarchyRoot);

            // 肘/膝を掴んだ場合。先端は動かさないので jointLock は見ない
            if (point == MaidIKChainPoint.Joint)
            {
                solver.SetChain(new[] { _rootBone, _midBone }, _hierarchyRoot);
            }
            // 胸。中間関節が無いので固定する対象も無い
            else if (_midBone == null)
            {
                solver.SetChain(new[] { _rootBone, _tipBone }, _hierarchyRoot);
            }
            // Ctrl 固定中。根元を外すので肘/膝から先だけが動く
            else if (jointLock)
            {
                solver.SetChain(new[] { _midBone, _tipBone }, _hierarchyRoot);
            }
            // 通常の手足。四肢全体で解く
            else
            {
                solver.SetChain(new[] { _rootBone, _midBone, _tipBone }, _hierarchyRoot);
            }

            solver.target = target;
        }

        /// <summary>チェーンを空にして解くのを止める</summary>
        public void EndDrag()
        {
            if (_fabrik == null)
            {
                return;
            }

            _fabrik.solver.SetChain(new Transform[0], _hierarchyRoot);
        }

        /// <summary>
        /// IK 固定用に 1 フレームだけ解く。チェーンを張って targetPosition へ解き、
        /// 直後にチェーンを空へ戻すので、ドラッグ用の状態（BeginDrag/EndDrag）とは干渉しない。
        /// 反復回数は MTE の IKHoldEntity と同じ 4 回
        /// </summary>
        public void Solve(MaidIKChainPoint point, Vector3 targetPosition)
        {
            if (_fabrik == null)
            {
                return;
            }

            if (_solveTarget == null)
            {
                _solveTarget = new GameObject("MIE_IKHoldTarget_" + _tipBone.name).transform;
            }
            _solveTarget.position = targetPosition;

            var solver = _fabrik.solver;
            solver.SetChain(new Transform[0], _hierarchyRoot);

            if (point == MaidIKChainPoint.Joint)
            {
                solver.SetChain(new[] { _rootBone, _midBone }, _hierarchyRoot);
            }
            else if (_midBone == null)
            {
                solver.SetChain(new[] { _rootBone, _tipBone }, _hierarchyRoot);
            }
            else
            {
                solver.SetChain(new[] { _rootBone, _midBone, _tipBone }, _hierarchyRoot);
            }

            solver.target = _solveTarget;
            solver.maxIterations = 4;
            solver.Update();

            // 解き終わったら元へ戻す。ドラッグ側は maxIterations=1 の毎フレーム自動更新前提
            solver.maxIterations = 1;
            solver.target = null;
            solver.SetChain(new Transform[0], _hierarchyRoot);
        }

        public void Destroy()
        {
            if (_fabrik != null)
            {
                Object.Destroy(_fabrik);
                _fabrik = null;
            }

            // ボーンが破棄済みでも台帳のエントリは残るため、Unity の null 判定ではなく
            // 参照そのもので引いてカウントを返し、最後の利用者だけがヒンジを消す
            if (_hasHingeRef && !ReferenceEquals(_midBone, null))
            {
                SharedHinge shared;
                if (_sharedHinges.TryGetValue(_midBone, out shared) && --shared.refCount <= 0)
                {
                    if (shared.hinge != null)
                    {
                        Object.Destroy(shared.hinge);
                    }
                    _sharedHinges.Remove(_midBone);
                }
                _hasHingeRef = false;
            }

            if (_solveTarget != null)
            {
                Object.Destroy(_solveTarget.gameObject);
                _solveTarget = null;
            }
        }
    }
}
