using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 指関節（手指 5 本×3、足指 3 本×2、左右）の個別ドラッグ点の生成・破棄。
    /// 指ウィンドウの「個別編集」トグルが ON の間だけ表示する。
    /// 各関節の点は自分の関節だけを曲げ、指先（Nub）の点は指 1 本を丸ごとカールさせる。
    /// 根本関節と指先の点は左右ドラッグで開き（スプレッド）も操作できる。
    /// 可動域は指ブレンドテンプレート（open/close/fist）から関節ごとに算出する
    /// </summary>
    public class MaidFingerDragPointController
    {
        /// <summary>手指の関節数（Finger0 → Finger01 → Finger02）</summary>
        private const int HandJointCount = 3;

        /// <summary>足指の関節数（Toe0 → Toe01）</summary>
        private const int FootJointCount = 2;

        private const int HandDigitCount = 5;
        private const int FootDigitCount = 3;

        /// <summary>手指の曲げ軸。指ブレンドテンプレートの実測（open→fist が -Z 回転）</summary>
        private static readonly Vector3 HandBendAxis = new Vector3(0f, 0f, -1f);

        /// <summary>足指の曲げ軸。手指と逆で +Z 回転</summary>
        private static readonly Vector3 FootBendAxis = new Vector3(0f, 0f, 1f);

        /// <summary>
        /// テンプレート範囲へ足す余裕 (度)。テンプレートは「自然な」極値なので、
        /// 表現の幅を残すため少しだけ超えられるようにする
        /// </summary>
        private const float BendMargin = 15f;
        private const float SpreadMargin = 15f;

        // 指は関節間が数 cm しかないため、体のドラッグ点（0.04）より小さくして
        // 隣の関節と当たり判定が重ならないようにする
        private const float FingerDragPointScale = 0.015f;

        private readonly List<GameObject> _dragPoints = new List<GameObject>();

        private Maid _maid = null;

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
            foreach (var isRight in new[] { true, false })
            {
                var prefix = isRight ? "Bip01 R " : "Bip01 L ";

                var handTable = FingerBlendUnit.GetBoneTypeTable(
                    isRight ? FingerBlendType.RightArm : FingerBlendType.LeftArm);
                for (var digit = 0; digit < HandDigitCount; digit++)
                {
                    CreateDigitDragPoints(maid, bones, prefix, "Finger" + digit,
                        HandJointCount, HandBendAxis, handTable[digit]);
                }

                var footTable = FingerBlendUnit.GetBoneTypeTable(
                    isRight ? FingerBlendType.RightLeg : FingerBlendType.LeftLeg);
                for (var digit = 0; digit < FootDigitCount; digit++)
                {
                    // テーブル行 i にはボーン Toe(2-i) が対応する（FingerBlendUnit と同じ逆順対応）
                    CreateDigitDragPoints(maid, bones, prefix, "Toe" + digit,
                        FootJointCount, FootBendAxis, footTable[2 - digit]);
                }
            }
        }

        /// <summary>
        /// 指 1 本ぶんの点を作る。各関節の個別点と、指先（Nub）に全関節をまとめて曲げる
        /// カール点を置く。ボーン名は根本 "Finger0" → 第 2 関節 "Finger01" → 先端 "Finger02"
        /// （足指は "Toe0" → "Toe01"）、指先は "Finger0Nub" 形式。
        /// boneTypes はボーンと同順（根本→先端）のテンプレートキー
        /// </summary>
        private void CreateDigitDragPoints(Maid maid, Transform bones, string prefix,
            string digitName, int jointCount, Vector3 bendAxis, IKManager.BoneType[] boneTypes)
        {
            var joints = new Transform[jointCount];
            for (var joint = 0; joint < jointCount; joint++)
            {
                var boneName = prefix + digitName + (joint == 0 ? "" : joint.ToString());
                joints[joint] = FindBone(bones, boneName);
                if (joints[joint] == null)
                {
                    return;
                }
            }

            var root = joints[0];

            var entries = new MaidFingerDragPoint.Entry[jointCount];
            for (var joint = 0; joint < jointCount; joint++)
            {
                entries[joint] = CreateEntry(joints[joint], 1f, bendAxis, boneTypes[joint]);
            }

            // 各関節の個別点。根本の点だけ左右ドラッグで開きも動かせる
            for (var joint = 0; joint < jointCount; joint++)
            {
                CreateFingerDragPoint(maid, joints[joint], new[] { entries[joint] },
                    bendAxis, joint == 0 ? root : null, boneTypes[0]);
            }

            // 指先のカール点。全関節へ均等配分し、開きは根本へ乗せる
            var nub = FindBone(bones, prefix + digitName + "Nub");
            if (nub == null)
            {
                return;
            }
            CreateFingerDragPoint(maid, nub, entries, bendAxis, root, boneTypes[0]);
        }

        /// <summary>
        /// 1 関節ぶんのエントリを作る。曲げの可動域は open→fist テンプレートの
        /// 相対回転から算出し、open 姿勢を 0 度として [-マージン, fist角+マージン] とする
        /// </summary>
        private static MaidFingerDragPoint.Entry CreateEntry(
            Transform bone, float weight, Vector3 bendAxis, IKManager.BoneType boneType)
        {
            var entry = new MaidFingerDragPoint.Entry
            {
                bone = bone,
                weight = weight,
            };

            Quaternion open, close, fist;
            if (!FingerBlendUnit.TryGetTemplateRotations(boneType, out open, out close, out fist))
            {
                return entry;
            }

            float angle;
            Vector3 axis;
            (Quaternion.Inverse(open) * fist).ToAngleAxis(out angle, out axis);

            // 回転軸が曲げ軸と逆向きなら符号を反転する。親指根本のように軸が斜めの関節でも
            // 全回転量をそのまま可動域として使う（射影すると過剰に制限されるため。
            // 代わりに軸ズレが大きいボーンほど制限は緩くなる）。
            // なお本算出はテンプレート間の相対回転角が 180 度未満であることを前提とする
            var bendMax = Vector3.Dot(axis, bendAxis) >= 0f ? angle : -angle;
            if (bendMax <= 0f)
            {
                // 曲げ軸の想定と合わないテンプレート（異常データ）と、
                // ほぼ動かない関節（回転量 0）はどちらも無制限のまま扱う
                return entry;
            }

            entry.hasLimit = true;
            entry.openRotation = open;
            entry.bendMin = -BendMargin;
            entry.bendMax = bendMax + BendMargin;
            return entry;
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

        private void CreateFingerDragPoint(Maid maid, Transform followBone,
            MaidFingerDragPoint.Entry[] entries, Vector3 bendAxis, Transform spreadBone,
            IKManager.BoneType rootBoneType)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "MIE_FingerDragPoint_" + followBone.name;
            go.GetComponent<Renderer>().enabled = false;
            go.transform.localScale = new Vector3(
                FingerDragPointScale, FingerDragPointScale, FingerDragPointScale);
            go.layer = MaidDragPointController.DragPointLayer;
            go.AddComponent<MaidDragPointRing>();

            var point = go.AddComponent<MaidFingerDragPoint>();
            point.maid = maid;
            point.entries = entries;
            point.bendAxis = bendAxis;
            point.spreadBone = spreadBone;
            point.followBone = followBone;

            if (spreadBone != null)
            {
                ApplySpreadLimit(point, rootBoneType);
            }

            _dragPoints.Add(go);
        }

        /// <summary>
        /// 開きの可動域を根本関節に設定する。close→open テンプレートの相対回転
        /// （ローカル Y まわり、符号は指ごとに異なる）から、open 姿勢を 0 度として
        /// close 側〜open 側の範囲＋マージンとする
        /// </summary>
        private static void ApplySpreadLimit(MaidFingerDragPoint point, IKManager.BoneType boneType)
        {
            Quaternion open, close, fist;
            if (!FingerBlendUnit.TryGetTemplateRotations(boneType, out open, out close, out fist))
            {
                return;
            }

            float angle;
            Vector3 axis;
            (Quaternion.Inverse(close) * open).ToAngleAxis(out angle, out axis);

            // open 基準 (0 度) で close の開き角は逆符号になる
            var closeAngle = Vector3.Dot(axis, Vector3.up) >= 0f ? -angle : angle;

            point.hasSpreadLimit = true;
            point.spreadOpenRotation = open;
            point.spreadMin = Mathf.Min(0f, closeAngle) - SpreadMargin;
            point.spreadMax = Mathf.Max(0f, closeAngle) + SpreadMargin;
        }

        public void Destroy()
        {
            foreach (var go in _dragPoints)
            {
                if (go != null)
                {
                    Object.Destroy(go);
                }
            }
            _dragPoints.Clear();

            _maid = null;
        }
    }
}
