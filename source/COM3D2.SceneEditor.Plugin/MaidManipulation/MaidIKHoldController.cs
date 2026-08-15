using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>IK 固定の対象箇所。並びは MTE の IKHoldType と同じ</summary>
    public enum MaidIKHoldType
    {
        Arm_R_Joint,
        Arm_R_Tip,
        Arm_L_Joint,
        Arm_L_Tip,
        Foot_R_Joint,
        Foot_R_Tip,
        Foot_L_Joint,
        Foot_L_Tip,
        Max,
    }

    /// <summary>メイド 1 人ぶんの接地設定。既定値は MTE と同じ</summary>
    public class MaidIKHoldParams
    {
        /// <summary>スライダーの既定値参照用。値の定義はフィールド初期化子に一本化する</summary>
        public static readonly MaidIKHoldParams Default = new MaidIKHoldParams();

        public bool isGroundingFootL;
        public bool isGroundingFootR;
        public float floorHeight = 0f;
        public float footBaseOffset = 0.05f;
        public float footStretchHeight = 0.1f;
        public float footStretchAngle = 45f;
        public float footGroundAngle = 90f;
    }

    /// <summary>
    /// IK 固定（MTE の IKHoldEntity 相当）。ゲーム側 IKManager には依存せず、
    /// 固定用に自前の MaidIKChain を持って毎フレーム解く。
    /// ドラッグ用チェーン（MaidDragPointController 所有）とは別インスタンスだが、
    /// ドラッグ中の箇所は解かず target の追従記録だけ行うため競合しない
    /// </summary>
    public class MaidIKHoldController
    {
        /// <summary>固定 1 箇所ぶんの状態</summary>
        private class HoldEntity
        {
            public bool isHold;
            public bool resetRequested;
            public Vector3 targetPosition;
        }

        /// <summary>メイド 1 人ぶんの固定状態とチェーン</summary>
        private class MaidEntry
        {
            public readonly HoldEntity[] entities = new HoldEntity[(int)MaidIKHoldType.Max];
            public readonly MaidIKHoldParams holdParams = new MaidIKHoldParams();

            // 四肢 4 本。腕 L/R・脚 L/R の順（ChainDefs と同じ並び）
            public readonly MaidIKChain[] chains = new MaidIKChain[4];
            public readonly Transform[] midBones = new Transform[4];
            public readonly Transform[] tipBones = new Transform[4];

            // ドリフト防止（PositonCorrection 相当）用の初期 localPosition
            public readonly Vector3[] rootLocalPos = new Vector3[4];
            public readonly Vector3[] midLocalPos = new Vector3[4];
            public readonly Vector3[] tipLocalPos = new Vector3[4];
            public readonly Transform[] rootBones = new Transform[4];

            public MaidEntry()
            {
                for (var i = 0; i < entities.Length; i++)
                {
                    entities[i] = new HoldEntity();
                }
            }
        }

        /// <summary>チェーン定義。並びは MaidDragPointController.IKChainDefs と同じ</summary>
        private static readonly string[][] ChainDefs =
        {
            new[] { "Bip01 L UpperArm", "Bip01 L Forearm", "Bip01 L Hand" },
            new[] { "Bip01 R UpperArm", "Bip01 R Forearm", "Bip01 R Hand" },
            new[] { "Bip01 L Thigh", "Bip01 L Calf", "Bip01 L Foot" },
            new[] { "Bip01 R Thigh", "Bip01 R Calf", "Bip01 R Foot" },
        };

        private static readonly string[] HoldTypeNames =
        {
            "肘(右)", "手首(右)", "肘(左)", "手首(左)",
            "膝(右)", "足首(右)", "膝(左)", "足首(左)",
        };

        private readonly Dictionary<Maid, MaidEntry> _entries = new Dictionary<Maid, MaidEntry>();

        public static string GetHoldTypeName(MaidIKHoldType type)
        {
            return HoldTypeNames[(int)type];
        }

        /// <summary>
        /// 固定対象ボーン名 → 固定タイプの逆引き。Inspector の IK 選択表示が
        /// 選択中のドラッグ点に対応する固定トグルを出すために使う
        /// </summary>
        private static readonly Dictionary<string, MaidIKHoldType> HoldTypeByBoneName =
            new Dictionary<string, MaidIKHoldType>
        {
            { "Bip01 R Forearm", MaidIKHoldType.Arm_R_Joint },
            { "Bip01 R Hand", MaidIKHoldType.Arm_R_Tip },
            { "Bip01 L Forearm", MaidIKHoldType.Arm_L_Joint },
            { "Bip01 L Hand", MaidIKHoldType.Arm_L_Tip },
            { "Bip01 R Calf", MaidIKHoldType.Foot_R_Joint },
            { "Bip01 R Foot", MaidIKHoldType.Foot_R_Tip },
            { "Bip01 L Calf", MaidIKHoldType.Foot_L_Joint },
            { "Bip01 L Foot", MaidIKHoldType.Foot_L_Tip },
        };

        /// <summary>ボーン名に対応する固定タイプを引く。肩・胸など固定対象外は false</summary>
        public static bool TryGetHoldType(string boneName, out MaidIKHoldType type)
        {
            return HoldTypeByBoneName.TryGetValue(boneName, out type);
        }

        /// <summary>0=腕L, 1=腕R, 2=脚L, 3=脚R（ChainDefs と同じ並び）</summary>
        private static int GetChainIndex(MaidIKHoldType type)
        {
            switch (type)
            {
                case MaidIKHoldType.Arm_L_Joint:
                case MaidIKHoldType.Arm_L_Tip:
                    return 0;
                case MaidIKHoldType.Arm_R_Joint:
                case MaidIKHoldType.Arm_R_Tip:
                    return 1;
                case MaidIKHoldType.Foot_L_Joint:
                case MaidIKHoldType.Foot_L_Tip:
                    return 2;
                case MaidIKHoldType.Foot_R_Joint:
                case MaidIKHoldType.Foot_R_Tip:
                    return 3;
                default:
                    MTEUtils.LogWarning("不正な IK 固定タイプです: {0}", type);
                    return 3;
            }
        }

        private static bool IsJoint(MaidIKHoldType type)
        {
            return type == MaidIKHoldType.Arm_L_Joint || type == MaidIKHoldType.Arm_R_Joint
                || type == MaidIKHoldType.Foot_L_Joint || type == MaidIKHoldType.Foot_R_Joint;
        }

        private static bool IsAlive(Maid maid)
        {
            return maid != null && maid.body0 != null && maid.body0.isLoadedBody
                && maid.body0.m_Bones != null;
        }

        /// <summary>固定対象ボーンの現在ワールド座標（Joint は肘/膝、Tip は手首/足首）</summary>
        private static Vector3 GetPointPosition(MaidEntry entry, MaidIKHoldType type)
        {
            var index = GetChainIndex(type);
            var bone = IsJoint(type) ? entry.midBones[index] : entry.tipBones[index];
            return bone.position;
        }

        /// <summary>エントリを取得。無ければボーンを引いてチェーンごと作る。作れなければ null</summary>
        private MaidEntry GetOrCreateEntry(Maid maid)
        {
            MaidEntry entry;
            if (_entries.TryGetValue(maid, out entry))
            {
                return entry;
            }

            if (!IsAlive(maid))
            {
                return null;
            }

            entry = new MaidEntry();
            var bones = maid.body0.m_Bones.transform;
            for (var i = 0; i < ChainDefs.Length; i++)
            {
                var root = CMT.SearchObjName(bones, ChainDefs[i][0], false);
                var mid = CMT.SearchObjName(bones, ChainDefs[i][1], false);
                var tip = CMT.SearchObjName(bones, ChainDefs[i][2], false);
                if (root == null || mid == null || tip == null)
                {
                    MTEUtils.LogWarning("IK 固定用のボーンが見つかりません: {0}", ChainDefs[i][2]);
                    return null;
                }

                entry.chains[i] = new MaidIKChain(root, mid, tip);
                entry.rootBones[i] = root;
                entry.midBones[i] = mid;
                entry.tipBones[i] = tip;
                entry.rootLocalPos[i] = root.localPosition;
                entry.midLocalPos[i] = mid.localPosition;
                entry.tipLocalPos[i] = tip.localPosition;
            }

            _entries.Add(maid, entry);
            return entry;
        }

        public bool GetHold(Maid maid, MaidIKHoldType type)
        {
            MaidEntry entry;
            return _entries.TryGetValue(maid, out entry) && entry.entities[(int)type].isHold;
        }

        public void SetHold(Maid maid, MaidIKHoldType type, bool hold)
        {
            var entry = GetOrCreateEntry(maid);
            if (entry == null)
            {
                return;
            }

            var entity = entry.entities[(int)type];
            if (entity.isHold == hold)
            {
                return;
            }
            entity.isHold = hold;
            entity.resetRequested = true;

            // 固定はモーション停止中しか解かないため、ボーンを触っていなくても
            // ON にした時点で停止させてすぐ効くようにする（ボーンドラッグ開始と同じ扱い）。
            // 編集モード外では固定自体が効かない（LateUpdate 参照）ため、モーションだけ
            // 止まる状態にならないよう見送る。停止はモード開始時 (OnEditModeStarted) に行う
            if (hold && MaidManipulateManager.instance.isEditMode)
            {
                MaidMotionState.StopMotion(maid);
            }
        }

        /// <summary>
        /// 編集モード開始時の処理。固定が残っているメイドのモーションを停止し、
        /// ボーンを触らなくても固定が効いている状態から編集を始められるようにする
        /// </summary>
        public void OnEditModeStarted()
        {
            foreach (var pair in _entries)
            {
                var hasHold = false;
                foreach (var entity in pair.Value.entities)
                {
                    if (!entity.isHold)
                    {
                        continue;
                    }

                    // モード外の間に変わったポーズへ固定位置を取り直す。
                    // 残しておくと再開時に古い位置へ引き戻してしまう
                    entity.resetRequested = true;
                    hasHold = true;
                }

                if (hasHold)
                {
                    MaidMotionState.StopMotion(pair.Key);
                }
            }
        }

        /// <summary>
        /// 既存エントリの接地パラメータを返す。エントリが無ければ null。
        /// GetParams と違いエントリ（IK チェーン込み）を新規生成しない読み取り専用アクセサ
        /// </summary>
        public MaidIKHoldParams GetParamsOrNull(Maid maid)
        {
            MaidEntry entry;
            return _entries.TryGetValue(maid, out entry) ? entry.holdParams : null;
        }

        public MaidIKHoldParams GetParams(Maid maid)
        {
            var entry = GetOrCreateEntry(maid);
            // ロード中などエントリを作れない間は使い捨てを返す。編集は保存されないが、
            // UI 側の null チェックを不要にして次フレーム以降の正規エントリに引き継がせる
            return entry != null ? entry.holdParams : new MaidIKHoldParams();
        }

        /// <summary>
        /// 全固定箇所のターゲットを現在のボーン位置から取り直させる。
        /// undo でポーズを書き戻した後、固定が元の位置へ解き直すのを防ぐために使う
        /// </summary>
        public void ResetAllTargetPositions(Maid maid)
        {
            MaidEntry entry;
            if (_entries.TryGetValue(maid, out entry))
            {
                foreach (var entity in entry.entities)
                {
                    entity.resetRequested = true;
                }
            }
        }

        public void ResetTargetPosition(Maid maid, MaidIKHoldType type)
        {
            MaidEntry entry;
            if (_entries.TryGetValue(maid, out entry))
            {
                entry.entities[(int)type].resetRequested = true;
            }
        }

        /// <summary>両足首の現在高さから床の高さを推定する（MTE の「メイドの位置から推定」）</summary>
        public void EstimateFloorHeight(Maid maid)
        {
            MaidEntry entry;
            if (!_entries.TryGetValue(maid, out entry))
            {
                return;
            }

            var footL = entry.tipBones[GetChainIndex(MaidIKHoldType.Foot_L_Tip)];
            var footR = entry.tipBones[GetChainIndex(MaidIKHoldType.Foot_R_Tip)];
            entry.holdParams.floorHeight =
                (footL.position.y + footR.position.y) / 2f - entry.holdParams.footBaseOffset;

            ResetTargetPosition(maid, MaidIKHoldType.Foot_L_Tip);
            ResetTargetPosition(maid, MaidIKHoldType.Foot_R_Tip);
        }

        public void LateUpdate()
        {
            // 消滅したメイドのエントリを片づける
            List<Maid> deadMaids = null;
            foreach (var pair in _entries)
            {
                // 衣装変更・ボディ再ロードでは Maid は生きたままボーン Transform だけが
                // 破棄されるため、チェーンの参照切れも消滅として扱い固定を自動解除する
                if (!IsAlive(pair.Key) || HasDeadBone(pair.Value))
                {
                    (deadMaids ?? (deadMaids = new List<Maid>())).Add(pair.Key);
                }
            }
            if (deadMaids != null)
            {
                foreach (var maid in deadMaids)
                {
                    Release(maid);
                }
            }

            // 固定はポーズ編集用の機能なので、編集モード中だけ効かせる
            // （UI の「※編集モードで有効」表記と揃える）
            if (!MaidManipulateManager.instance.isEditMode)
            {
                return;
            }

            foreach (var pair in _entries)
            {
                UpdateMaid(pair.Key, pair.Value);
            }
        }

        /// <summary>チェーンのボーンが 1 本でも破棄されているか（部分的な再ロード対策）</summary>
        private static bool HasDeadBone(MaidEntry entry)
        {
            for (var i = 0; i < entry.tipBones.Length; i++)
            {
                if (entry.tipBones[i] == null || entry.midBones[i] == null
                    || entry.rootBones[i] == null)
                {
                    return true;
                }
            }
            return false;
        }

        private void UpdateMaid(Maid maid, MaidEntry entry)
        {
            var isMotionStopped = MaidMotionState.IsMotionStopped(maid);

            // 同一チェーンで Joint と Tip を両方固定した場合、列挙順で後の Tip が
            // チェーン全体を解き直すため実質 Tip 固定が勝つ（MTE と同じ挙動）

            for (var i = 0; i < (int)MaidIKHoldType.Max; i++)
            {
                var type = (MaidIKHoldType)i;
                var entity = entry.entities[i];
                if (!entity.isHold)
                {
                    continue;
                }

                // モーション再生中の固定（IK アニメーション）は MTE 側の担当なので、
                // 停止中のポーズ編集時のみ固定する
                if (!isMotionStopped)
                {
                    continue;
                }

                var index = GetChainIndex(type);
                var pointBone = IsJoint(type) ? entry.midBones[index] : entry.tipBones[index];

                // この箇所をドラッグ中は解かず、離した位置を引き継ぐため追従記録だけ行う
                if (MaidDragBoneTracker.draggingBoneName == pointBone.name)
                {
                    entity.targetPosition = GetPointPosition(entry, type);
                    continue;
                }

                if (entity.resetRequested)
                {
                    entity.targetPosition = GetPointPosition(entry, type);
                    entity.resetRequested = false;
                }

                var targetPosition = entity.targetPosition;
                var grounding = IsFootGrounding(entry, type);
                if (grounding)
                {
                    targetPosition.y = entry.holdParams.floorHeight + entry.holdParams.footBaseOffset;
                }

                entry.chains[index].Solve(
                    IsJoint(type) ? MaidIKChainPoint.Joint : MaidIKChainPoint.Tip,
                    targetPosition);

                if (grounding)
                {
                    AdjustFootGrounding(entry, index);
                }

                // FABRIK はボーン位置も動かし得るため、初期 localPosition へ戻して伸縮を防ぐ
                entry.rootBones[index].localPosition = entry.rootLocalPos[index];
                entry.midBones[index].localPosition = entry.midLocalPos[index];
                entry.tipBones[index].localPosition = entry.tipLocalPos[index];
            }
        }

        private static bool IsFootGrounding(MaidEntry entry, MaidIKHoldType type)
        {
            if (type == MaidIKHoldType.Foot_L_Tip)
            {
                return entry.holdParams.isGroundingFootL;
            }
            if (type == MaidIKHoldType.Foot_R_Tip)
            {
                return entry.holdParams.isGroundingFootR;
            }
            return false;
        }

        /// <summary>
        /// 接地時の足首角度補正（MTE の AdjustFootGrounding 移植）。
        /// 接地中は足裏を地面と平行へ、床から浮くほど「伸ばす角度」へ補間する
        /// </summary>
        private static void AdjustFootGrounding(MaidEntry entry, int chainIndex)
        {
            var footBone = entry.tipBones[chainIndex];
            var p = entry.holdParams;

            // 地面と平行になる Z 角度を求める
            float targetAngle;
            {
                var forward = footBone.rotation * Vector3.forward;
                forward.y = 0f;
                forward.Normalize();

                var targetRotation = Quaternion.LookRotation(forward, Vector3.up);
                var localTargetRotation =
                    Quaternion.Inverse(footBone.parent.rotation) * targetRotation;
                targetAngle = localTargetRotation.eulerAngles.z + p.footGroundAngle;
            }

            // 足が床より上にある場合はつま先を伸ばす角度へ寄せる
            var footStretchAngle = p.footStretchAngle;
            var heightDifference = footBone.position.y - p.floorHeight - p.footBaseOffset;
            if (heightDifference > 0f)
            {
                // 360 度差を除いて近い方の角度を採用する
                var diffAngle = (int)(footStretchAngle - targetAngle);
                if (diffAngle > 180)
                {
                    footStretchAngle -= (diffAngle + 180) / 360 * 360;
                }
                else if (diffAngle < -180)
                {
                    footStretchAngle -= (diffAngle - 180) / 360 * 360;
                }

                var heightRate = Mathf.Clamp01(heightDifference / p.footStretchHeight);
                targetAngle = Mathf.Lerp(targetAngle, footStretchAngle, heightRate);
            }

            var footRotation = footBone.localEulerAngles;
            footRotation.z = targetAngle;
            footBone.localEulerAngles = footRotation;
        }

        /// <summary>メイド 1 人ぶんの状態とチェーンを破棄する（解除・消滅時）</summary>
        public void Release(Maid maid)
        {
            MaidEntry entry;
            if (!_entries.TryGetValue(maid, out entry))
            {
                return;
            }

            foreach (var chain in entry.chains)
            {
                if (chain != null)
                {
                    chain.Destroy();
                }
            }
            _entries.Remove(maid);
        }

        public void Destroy()
        {
            foreach (var entry in _entries.Values)
            {
                foreach (var chain in entry.chains)
                {
                    if (chain != null)
                    {
                        chain.Destroy();
                    }
                }
            }
            _entries.Clear();
        }
    }
}
