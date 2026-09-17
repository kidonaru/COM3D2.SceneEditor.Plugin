using System;
using System.Collections.Generic;
using System.Linq;
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
    /// IK 固定。ゲーム側 IKManager には依存せず、
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
            /// <summary>モーション再生中も固定を効かせる（MTE の IK アニメーション相当）</summary>
            public bool isAnime;
            /// <summary>
            /// 目標を現在のボーン位置から取り直す要求を出したフレーム。
            /// 取り直しは翌フレーム以降に行う (MaidIKHoldResetGate 参照)
            /// </summary>
            public int resetRequestedFrame = MaidIKHoldResetGate.NoRequest;
            public Vector3 targetPosition;

            public bool isResetRequested => resetRequestedFrame != MaidIKHoldResetGate.NoRequest;

            public void RequestReset()
            {
                resetRequestedFrame = Time.frameCount;
            }

            public void ClearResetRequest()
            {
                resetRequestedFrame = MaidIKHoldResetGate.NoRequest;
            }
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

        /// <summary>固定の解決で回転を書き換えるボーン名 (四肢チェーンの全ボーン)</summary>
        public static readonly string[] SolvedBoneNames = ChainDefs.SelectMany(chain => chain).ToArray();

        /// <summary>
        /// 固定目標の取り直しが確定した直後に、そのメイドを渡して通知する。
        /// タイムラインはこれを受けて編集開始スナップショットのうち固定が動かすボーンを取り直す
        /// (取り直し前の姿勢を基準にすると、固定で動いた腕脚が触っていないのに差分扱いになる)
        /// </summary>
        public event Action<Maid> onTargetCaptured;

        private readonly Dictionary<Maid, MaidEntry> _entries = new Dictionary<Maid, MaidEntry>();

        public static string GetHoldTypeName(MaidIKHoldType type)
        {
            return HoldTypeNames[(int)type];
        }

        /// <summary>
        /// タイムラインのボーン一覧用の表示名。実ボーン「足首(左)」等と同じ名前が
        /// 同じグループに並んで区別できないため、IK 固定側には「IK」を前置する
        /// </summary>
        public static string GetHoldTypeMenuName(MaidIKHoldType type)
        {
            return "IK" + GetHoldTypeName(type);
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

        /// <summary>enum メンバ名 → 固定タイプ。タイムラインのキーは enum 名でボーンを指す</summary>
        private static readonly Dictionary<string, MaidIKHoldType> HoldTypeByEnumName =
            Enum.GetValues(typeof(MaidIKHoldType))
                .Cast<MaidIKHoldType>()
                .Where(t => t != MaidIKHoldType.Max)
                .ToDictionary(t => t.ToString(), t => t);

        /// <summary>enum メンバ名から固定タイプを引く。未知の名前なら false</summary>
        public static bool TryParseHoldType(string name, out MaidIKHoldType type)
        {
            type = MaidIKHoldType.Max;
            return !string.IsNullOrEmpty(name) && HoldTypeByEnumName.TryGetValue(name, out type);
        }

        /// <summary>IK 固定項目をボーンメニュー上で置く腕/脚グループ</summary>
        public static BoneSetMenuType GetBoneSetMenuType(MaidIKHoldType type)
        {
            switch (type)
            {
                case MaidIKHoldType.Arm_L_Joint:
                case MaidIKHoldType.Arm_L_Tip:
                    return BoneSetMenuType.LeftArm;
                case MaidIKHoldType.Arm_R_Joint:
                case MaidIKHoldType.Arm_R_Tip:
                    return BoneSetMenuType.RightArm;
                case MaidIKHoldType.Foot_L_Joint:
                case MaidIKHoldType.Foot_L_Tip:
                    return BoneSetMenuType.LeftLeg;
                case MaidIKHoldType.Foot_R_Joint:
                case MaidIKHoldType.Foot_R_Tip:
                    return BoneSetMenuType.RightLeg;
                default:
                    return BoneSetMenuType.None;
            }
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
            entity.RequestReset();

            // 固定はモーション停止中しか解かないため、ボーンを触っていなくても
            // ON にした時点で停止させてすぐ効くようにする（ボーンドラッグ開始と同じ扱い）。
            // 編集モード外では固定自体が効かない（LateUpdate 参照）ため、モーションだけ
            // 止まる状態にならないよう見送る。停止はモード開始時 (OnEditModeStarted) に行う
            if (hold && MaidManipulateManager.instance.isEditMode)
            {
                MaidMotionState.StopMotion(maid);
            }
        }

        public bool GetAnime(Maid maid, MaidIKHoldType type)
        {
            MaidEntry entry;
            return _entries.TryGetValue(maid, out entry) && entry.entities[(int)type].isAnime;
        }

        public void SetAnime(Maid maid, MaidIKHoldType type, bool anime)
        {
            var entry = GetOrCreateEntry(maid);
            if (entry == null)
            {
                return;
            }
            entry.entities[(int)type].isAnime = anime;
        }

        /// <summary>固定対象ボーンの現在ワールド座標。エントリを作れなければ Vector3.zero</summary>
        public Vector3 GetPointPosition(Maid maid, MaidIKHoldType type)
        {
            var entry = GetOrCreateEntry(maid);
            return entry != null ? GetPointPosition(entry, type) : Vector3.zero;
        }

        /// <summary>固定点の目標ワールド座標。エントリ未作成なら Vector3.zero</summary>
        public Vector3 GetTargetPosition(Maid maid, MaidIKHoldType type)
        {
            MaidEntry entry;
            return _entries.TryGetValue(maid, out entry)
                ? entry.entities[(int)type].targetPosition
                : Vector3.zero;
        }

        /// <summary>固定点の目標ワールド座標を差し替える（タイムライン再生からの書き戻し用）</summary>
        public void SetTargetPosition(Maid maid, MaidIKHoldType type, Vector3 position)
        {
            var entry = GetOrCreateEntry(maid);
            if (entry == null)
            {
                return;
            }

            var entity = entry.entities[(int)type];
            entity.targetPosition = position;
            // 外から位置を指定した以上、現在のボーン位置で取り直させてはいけない
            entity.ClearResetRequest();
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
                    entity.RequestReset();
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
                    entity.RequestReset();
                }
            }
        }

        /// <summary>左右で対になる固定タイプ。ポーズ反転に合わせて状態を入れ替える</summary>
        private static readonly MaidIKHoldType[,] FlipPairs =
        {
            { MaidIKHoldType.Arm_L_Joint, MaidIKHoldType.Arm_R_Joint },
            { MaidIKHoldType.Arm_L_Tip, MaidIKHoldType.Arm_R_Tip },
            { MaidIKHoldType.Foot_L_Joint, MaidIKHoldType.Foot_R_Joint },
            { MaidIKHoldType.Foot_L_Tip, MaidIKHoldType.Foot_R_Tip },
        };

        /// <summary>
        /// 固定状態を左右反転する (ポーズ反転に追随させる)。
        /// 固定 ON / アニメ指定と足の接地フラグを L↔R で入れ替え、目標位置は反転後の
        /// ボーン位置から取り直させる (古い位置のままだと反転前のポーズへ引き戻される)。
        /// 固定を一度も使っていないメイドはエントリが無いため何もしない。
        /// フラグを直接入れ替えるため SetHold のモーション停止は通らない。
        /// 呼び出し側で停止させておくこと (停止していないと固定が効かない)
        /// </summary>
        public void FlipHolds(Maid maid)
        {
            MaidEntry entry;
            if (maid == null || !_entries.TryGetValue(maid, out entry))
            {
                return;
            }

            for (var i = 0; i < FlipPairs.GetLength(0); i++)
            {
                var left = entry.entities[(int)FlipPairs[i, 0]];
                var right = entry.entities[(int)FlipPairs[i, 1]];

                var isHold = left.isHold;
                left.isHold = right.isHold;
                right.isHold = isHold;

                var isAnime = left.isAnime;
                left.isAnime = right.isAnime;
                right.isAnime = isAnime;
            }

            var holdParams = entry.holdParams;
            var isGroundingFootL = holdParams.isGroundingFootL;
            holdParams.isGroundingFootL = holdParams.isGroundingFootR;
            holdParams.isGroundingFootR = isGroundingFootL;

            ResetAllTargetPositions(maid);
        }

        public void ResetTargetPosition(Maid maid, MaidIKHoldType type)
        {
            MaidEntry entry;
            if (_entries.TryGetValue(maid, out entry))
            {
                entry.entities[(int)type].RequestReset();
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

        /// <summary>
        /// 全メイドの固定を解く。ゲーム側の TBody.LateUpdate (体の高さオフセット・前腕スケール)
        /// はこれより後に走るため、ポーズが変わったフレームの目標取り直しは翌フレームへ回す
        /// (MaidIKHoldResetGate)
        /// </summary>
        public void LateUpdate()
        {
            // 消滅したメイドのエントリを片づける。破棄済みボーンへ触らないよう、解く前に必ず通す
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
            // （UI の「※編集モードで有効」表記と揃える）。
            // ただしアニメ指定（タイムライン再生中の固定）は編集モード外でも効かせる
            var isEditMode = MaidManipulateManager.instance.isEditMode;

            foreach (var pair in _entries)
            {
                if (!isEditMode && !HasAnime(pair.Value))
                {
                    continue;
                }
                UpdateMaid(pair.Key, pair.Value);
            }
        }

        /// <summary>アニメ指定の固定を 1 箇所でも持つか</summary>
        private static bool HasAnime(MaidEntry entry)
        {
            foreach (var entity in entry.entities)
            {
                if (entity.isHold && entity.isAnime)
                {
                    return true;
                }
            }
            return false;
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
            var captured = false;

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

                // 編集モード外はアニメ指定の箇所だけ固定する
                if (!MaidManipulateManager.instance.isEditMode && !entity.isAnime)
                {
                    continue;
                }

                // モーション再生中の固定はアニメ指定のときだけ行う。
                // 指定が無ければ従来どおり停止中のポーズ編集時のみ固定する
                if (!isMotionStopped && !entity.isAnime)
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

                if (entity.isResetRequested)
                {
                    // 要求と同じフレームはゲーム側 LateUpdate (体の高さオフセット・前腕スケール) が
                    // ポーズを確定させる前なので取り直さず、解きもしない。確定前の位置を目標にすると
                    // 翌フレームに腕脚が引き戻され、触っていないボーンが動いてしまう
                    if (!MaidIKHoldResetGate.ShouldCapture(entity.resetRequestedFrame, Time.frameCount))
                    {
                        continue;
                    }
                    entity.targetPosition = GetPointPosition(entry, type);
                    entity.ClearResetRequest();
                    captured = true;
                }

                var targetPosition = entity.targetPosition;
                var grounding = IsFootGrounding(entry, type);
                if (grounding)
                {
                    targetPosition.y = entry.holdParams.floorHeight + entry.holdParams.footBaseOffset;
                }

                // FABRIK はボーン位置も動かし得るため、解く前の localPosition を退避して
                // 解いた後に戻し伸縮を防ぐ。ボディロード直後は体型モーフ適用前で骨長が
                // 一時的に素の値になるため、固定の初期値ではなく毎回その場の値を使う
                var rootBone = entry.rootBones[index];
                var midBone = entry.midBones[index];
                var tipBone = entry.tipBones[index];
                var savedRootLocalPos = rootBone.localPosition;
                var savedMidLocalPos = midBone.localPosition;
                var savedTipLocalPos = tipBone.localPosition;

                entry.chains[index].Solve(
                    IsJoint(type) ? MaidIKChainPoint.Joint : MaidIKChainPoint.Tip,
                    targetPosition);

                if (grounding)
                {
                    AdjustFootGrounding(entry, index);
                }

                rootBone.localPosition = savedRootLocalPos;
                midBone.localPosition = savedMidLocalPos;
                tipBone.localPosition = savedTipLocalPos;
            }

            if (captured)
            {
                onTargetCaptured?.Invoke(maid);
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
                footStretchAngle = AngleUtils.GetFixedAngle(footStretchAngle, targetAngle);

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
