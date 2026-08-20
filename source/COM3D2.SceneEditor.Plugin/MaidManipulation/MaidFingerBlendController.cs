using System.Collections.Generic;
using System.Xml.Serialization;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>指ブレンドの部位</summary>
    public enum FingerBlendType
    {
        RightArm,
        LeftArm,
        RightLeg,
        LeftLeg,
    }

    /// <summary>指 1 本ぶんのロック状態。ロック中の固定値まで含めて見た目を再現する</summary>
    public class FingerDigitState
    {
        [XmlAttribute]
        public bool isLock;
        [XmlAttribute]
        public float lockOpen;
        [XmlAttribute]
        public float lockFist;
    }

    /// <summary>1 部位ぶんの指ブレンド状態。プリセットの保存/復元に使う</summary>
    public class FingerUnitState
    {
        [XmlAttribute]
        public FingerBlendType type;
        [XmlAttribute]
        public float valueOpen;
        [XmlAttribute]
        public float valueFist;
        public List<FingerDigitState> digits = new List<FingerDigitState>();
    }

    /// <summary>
    /// 1 部位（片手/片足）ぶんの指ブレンド。ゲームの FingerBlend.BaseFinger と同じ
    /// テンプレート補間（閉じ→開きを lerp した後に握りへ lerp）だが、
    /// IKManager を生成せずメイドのボーンを直接回す
    /// </summary>
    public class FingerBlendUnit
    {
        private class Digit
        {
            public bool isLock;
            public float lockOpen;
            public float lockFist;
            public IKManager.BoneType[] boneTypes;
            public Transform[] bones;
        }

        /// <summary>開き具合（0-1）</summary>
        public float valueOpen;

        /// <summary>閉じ（握り）具合（0-1）</summary>
        public float valueFist;

        private readonly Digit[] _digits;

        /// <summary>この部位の種別。プリセットの保存/復元で部位を突き合わせるのに使う</summary>
        public FingerBlendType type { get; private set; }

        public int digitCount => _digits.Length;

        /// <summary>この部位が回す全ボーン。操作履歴の記録対象用</summary>
        public IEnumerable<Transform> bones
        {
            get
            {
                foreach (var digit in _digits)
                {
                    foreach (var bone in digit.bones)
                    {
                        if (bone != null)
                        {
                            yield return bone;
                        }
                    }
                }
            }
        }

        public bool isAllLock
        {
            get
            {
                foreach (var digit in _digits)
                {
                    if (!digit.isLock)
                    {
                        return false;
                    }
                }
                return true;
            }
        }

        // テンプレートはゲームの FingerBlend と同じリソース。キーは IKManager.BoneType
        private static Dictionary<IKManager.BoneType, Quaternion> _openDic;
        private static Dictionary<IKManager.BoneType, Quaternion> _closeDic;
        private static Dictionary<IKManager.BoneType, Quaternion> _fistDic;

        /// <summary>読み込み失敗時も次回また試行できるよう、null 判定で再試行する（ゲームの Awake と同じ）</summary>
        private static void LoadTemplates()
        {
            if (_openDic != null && _closeDic != null && _fistDic != null)
            {
                return;
            }

            _openDic = FingerBlend.ReadFingerBoneDataFromrResource(
                "ScenePhotoMode/binary/finger_template_open");
            _closeDic = FingerBlend.ReadFingerBoneDataFromrResource(
                "ScenePhotoMode/binary/finger_template_close");
            _fistDic = FingerBlend.ReadFingerBoneDataFromrResource(
                "ScenePhotoMode/binary/finger_template_fist1");

            if (_openDic == null || _closeDic == null || _fistDic == null)
            {
                MTEUtils.LogError("指ブレンドのテンプレート読み込みに失敗しました");
            }
        }

        /// <summary>手指 1 本ぶんの BoneType（指 5 本 × 関節 3。右手/左手）</summary>
        private static readonly IKManager.BoneType[][] RightArmBoneTypes =
        {
            new[] { IKManager.BoneType.Finger0_Root_R, IKManager.BoneType.Finger0_0_R, IKManager.BoneType.Finger0_1_R },
            new[] { IKManager.BoneType.Finger1_Root_R, IKManager.BoneType.Finger1_0_R, IKManager.BoneType.Finger1_1_R },
            new[] { IKManager.BoneType.Finger2_Root_R, IKManager.BoneType.Finger2_0_R, IKManager.BoneType.Finger2_1_R },
            new[] { IKManager.BoneType.Finger3_Root_R, IKManager.BoneType.Finger3_0_R, IKManager.BoneType.Finger3_1_R },
            new[] { IKManager.BoneType.Finger4_Root_R, IKManager.BoneType.Finger4_0_R, IKManager.BoneType.Finger4_1_R },
        };

        private static readonly IKManager.BoneType[][] LeftArmBoneTypes =
        {
            new[] { IKManager.BoneType.Finger0_Root_L, IKManager.BoneType.Finger0_0_L, IKManager.BoneType.Finger0_1_L },
            new[] { IKManager.BoneType.Finger1_Root_L, IKManager.BoneType.Finger1_0_L, IKManager.BoneType.Finger1_1_L },
            new[] { IKManager.BoneType.Finger2_Root_L, IKManager.BoneType.Finger2_0_L, IKManager.BoneType.Finger2_1_L },
            new[] { IKManager.BoneType.Finger3_Root_L, IKManager.BoneType.Finger3_0_L, IKManager.BoneType.Finger3_1_L },
            new[] { IKManager.BoneType.Finger4_Root_L, IKManager.BoneType.Finger4_0_L, IKManager.BoneType.Finger4_1_L },
        };

        /// <summary>足指 1 本ぶんの BoneType（指 3 本 × 関節 2。右足/左足）</summary>
        private static readonly IKManager.BoneType[][] RightLegBoneTypes =
        {
            new[] { IKManager.BoneType.Toe0_Root_R, IKManager.BoneType.Toe0_0_R },
            new[] { IKManager.BoneType.Toe1_Root_R, IKManager.BoneType.Toe1_0_R },
            new[] { IKManager.BoneType.Toe2_Root_R, IKManager.BoneType.Toe2_0_R },
        };

        private static readonly IKManager.BoneType[][] LeftLegBoneTypes =
        {
            new[] { IKManager.BoneType.Toe0_Root_L, IKManager.BoneType.Toe0_0_L },
            new[] { IKManager.BoneType.Toe1_Root_L, IKManager.BoneType.Toe1_0_L },
            new[] { IKManager.BoneType.Toe2_Root_L, IKManager.BoneType.Toe2_0_L },
        };

        /// <summary>
        /// テンプレートの回転を返す。指ドラッグ点の可動域算出用
        /// （open↔fist が曲げ、close↔open が開きの自然な範囲を表す）
        /// </summary>
        public static bool TryGetTemplateRotations(IKManager.BoneType boneType,
            out Quaternion open, out Quaternion close, out Quaternion fist)
        {
            LoadTemplates();

            open = close = fist = Quaternion.identity;
            return _openDic != null && _closeDic != null && _fistDic != null
                && _openDic.TryGetValue(boneType, out open)
                && _closeDic.TryGetValue(boneType, out close)
                && _fistDic.TryGetValue(boneType, out fist);
        }

        public static IKManager.BoneType[][] GetBoneTypeTable(FingerBlendType type)
        {
            switch (type)
            {
                case FingerBlendType.RightArm: return RightArmBoneTypes;
                case FingerBlendType.LeftArm: return LeftArmBoneTypes;
                case FingerBlendType.RightLeg: return RightLegBoneTypes;
                default: return LeftLegBoneTypes;
            }
        }

        public FingerBlendUnit(Maid maid, FingerBlendType type)
        {
            LoadTemplates();

            this.type = type;

            var bones = maid.body0.m_Bones.transform;
            var isRight = type == FingerBlendType.RightArm || type == FingerBlendType.RightLeg;
            var isArm = type == FingerBlendType.RightArm || type == FingerBlendType.LeftArm;
            var prefix = isRight ? "Bip01 R " : "Bip01 L ";
            var boneTypeTable = GetBoneTypeTable(type);

            _digits = new Digit[boneTypeTable.Length];
            for (var i = 0; i < _digits.Length; i++)
            {
                var boneTypes = boneTypeTable[i];
                var digit = new Digit
                {
                    boneTypes = boneTypes,
                    bones = new Transform[boneTypes.Length],
                };
                for (var j = 0; j < boneTypes.Length; j++)
                {
                    string boneName;
                    if (isArm)
                    {
                        // Finger0 → Finger01 → Finger02（根本から先端）
                        boneName = prefix + "Finger" + i + (j == 0 ? "" : j.ToString());
                    }
                    else
                    {
                        // ゲームの IKManager と同じく Toe0 の BoneType には Toe2 のボーンを充てる（逆順対応）
                        boneName = prefix + "Toe" + (2 - i) + (j == 0 ? "" : "1");
                    }
                    digit.bones[j] = FindBone(bones, boneName);
                }
                _digits[i] = digit;
            }
        }

        private static Transform FindBone(Transform bones, string boneName)
        {
            var bone = CMT.SearchObjName(bones, boneName, false);
            if (bone == null)
            {
                MTEUtils.LogWarning("指のボーンが見つかりません: {0}", boneName);
            }
            return bone;
        }

        public bool IsLock(int index)
        {
            return _digits[index].isLock;
        }

        /// <summary>ロック時は現在のスライダー値を固定値として覚える（ゲームの LockSingleItem と同じ）</summary>
        public void SetLock(int index, bool isLock)
        {
            var digit = _digits[index];
            digit.isLock = isLock;
            digit.lockOpen = valueOpen;
            digit.lockFist = valueFist;
        }

        public void LockAll(bool isLock)
        {
            for (var i = 0; i < _digits.Length; i++)
            {
                SetLock(i, isLock);
            }
        }

        public void LockReverse()
        {
            for (var i = 0; i < _digits.Length; i++)
            {
                SetLock(i, !_digits[i].isLock);
            }
        }

        /// <summary>スライダーとロックを初期状態へ戻す。反映には別途 Apply を呼ぶ</summary>
        public void Reset()
        {
            valueOpen = 0f;
            valueFist = 0f;
            // ロック解除後の固定値も 0 に揃えるため、値を戻してから解除する
            LockAll(false);
        }

        public void CopyFrom(FingerBlendUnit source)
        {
            valueOpen = source.valueOpen;
            valueFist = source.valueFist;

            var count = Mathf.Min(_digits.Length, source._digits.Length);
            for (var i = 0; i < count; i++)
            {
                _digits[i].isLock = source._digits[i].isLock;
                _digits[i].lockOpen = source._digits[i].lockOpen;
                _digits[i].lockFist = source._digits[i].lockFist;
            }
        }

        /// <summary>現在の状態をプリセット保存用に書き出す</summary>
        public FingerUnitState CaptureState()
        {
            var state = new FingerUnitState
            {
                type = type,
                valueOpen = valueOpen,
                valueFist = valueFist,
            };

            foreach (var digit in _digits)
            {
                state.digits.Add(new FingerDigitState
                {
                    isLock = digit.isLock,
                    lockOpen = digit.lockOpen,
                    lockFist = digit.lockFist,
                });
            }

            return state;
        }

        /// <summary>
        /// 保存された状態を復元する。反映には別途 Apply を呼ぶ。
        /// 手指と足指で指の本数が異なるため、少ない方に合わせて読み込む
        /// </summary>
        public void RestoreState(FingerUnitState state)
        {
            valueOpen = state.valueOpen;
            valueFist = state.valueFist;

            var count = Mathf.Min(_digits.Length, state.digits.Count);
            for (var i = 0; i < count; i++)
            {
                _digits[i].isLock = state.digits[i].isLock;
                _digits[i].lockOpen = state.digits[i].lockOpen;
                _digits[i].lockFist = state.digits[i].lockFist;
            }
        }

        public void Apply()
        {
            if (_openDic == null || _closeDic == null || _fistDic == null)
            {
                return;
            }

            foreach (var digit in _digits)
            {
                var open = digit.isLock ? digit.lockOpen : valueOpen;
                var fist = digit.isLock ? digit.lockFist : valueFist;

                for (var j = 0; j < digit.bones.Length; j++)
                {
                    var bone = digit.bones[j];
                    var boneType = digit.boneTypes[j];

                    Quaternion openRot, closeRot, fistRot;
                    if (bone == null
                        || !_openDic.TryGetValue(boneType, out openRot)
                        || !_closeDic.TryGetValue(boneType, out closeRot)
                        || !_fistDic.TryGetValue(boneType, out fistRot))
                    {
                        continue;
                    }

                    bone.localRotation = Quaternion.Lerp(
                        Quaternion.Lerp(closeRot, openRot, open), fistRot, fist);
                }
            }
        }
    }

    /// <summary>
    /// 対象メイドの指ブレンド 4 部位を束ねる。メイドが変わると値ごと作り直す
    /// </summary>
    public class MaidFingerBlendController
    {
        private Maid _maid = null;
        private FingerBlendUnit[] _units = null;

        public Maid maid => _maid;

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
            _units = new FingerBlendUnit[4];
            _units[(int)FingerBlendType.RightArm] = new FingerBlendUnit(maid, FingerBlendType.RightArm);
            _units[(int)FingerBlendType.LeftArm] = new FingerBlendUnit(maid, FingerBlendType.LeftArm);
            _units[(int)FingerBlendType.RightLeg] = new FingerBlendUnit(maid, FingerBlendType.RightLeg);
            _units[(int)FingerBlendType.LeftLeg] = new FingerBlendUnit(maid, FingerBlendType.LeftLeg);
        }

        public FingerBlendUnit GetUnit(FingerBlendType type)
        {
            return _units != null ? _units[(int)type] : null;
        }

        public void Destroy()
        {
            _maid = null;
            _units = null;
        }
    }
}
