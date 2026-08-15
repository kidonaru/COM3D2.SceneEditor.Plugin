using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// ドラッグ点（IK 終端・頭部・上体・骨盤・胸）の生成・破棄。
    /// GizmoRender を使うギズモ系と違い、透明なコライダ + Unity のマウスメッセージで操作する
    /// </summary>
    public class MaidDragPointController
    {
        /// <summary>四肢の IK チェーン 1 本ぶんの定義。手足の点と肘/膝の点がこのチェーンを共有する</summary>
        private struct IKChainDef
        {
            public string rootBone;   // UpperArm / Thigh
            public string midBone;    // Forearm / Calf
            public string tipBone;    // Hand / Foot

            public IKChainDef(string rootBone, string midBone, string tipBone)
            {
                this.rootBone = rootBone;
                this.midBone = midBone;
                this.tipBone = tipBone;
            }
        }

        private static readonly IKChainDef[] IKChainDefs =
        {
            new IKChainDef("Bip01 L UpperArm", "Bip01 L Forearm", "Bip01 L Hand"),
            new IKChainDef("Bip01 R UpperArm", "Bip01 R Forearm", "Bip01 R Hand"),
            new IKChainDef("Bip01 L Thigh", "Bip01 L Calf", "Bip01 L Foot"),
            new IKChainDef("Bip01 R Thigh", "Bip01 R Calf", "Bip01 R Foot"),
        };

        /// <summary>
        /// 上体の 4 ボーンと配分重み（MM の MouseDrag3 ido==2/5 の係数）。
        /// Spine1 はひねり対象外なので twistWeight=0
        /// </summary>
        private static readonly string[] SpineBoneNames =
            { "Bip01 Spine1a", "Bip01 Spine1", "Bip01 Spine0a", "Bip01 Spine" };
        private static readonly float[] SpineTiltWeights = { 0.03f, 0.1f, 0.09f, 0.07f };
        private static readonly float[] SpineTwistWeights = { 0.084f, 0f, 0.156f, 0.156f };

        /// <summary>肩のボーン対（回転の起点 Clavicle と IK 終端 UpperArm）</summary>
        private static readonly string[][] ShoulderBonePairs =
        {
            new[] { "Bip01 L Clavicle", "Bip01 L UpperArm" },
            new[] { "Bip01 R Clavicle", "Bip01 R UpperArm" },
        };

        /// <summary>胸のボーン対（回転の起点 Mune_* と IK 終端 Mune_*_sub）</summary>
        private static readonly string[][] MuneBonePairs =
        {
            new[] { "Mune_L", "Mune_L_sub" },
            new[] { "Mune_R", "Mune_R_sub" },
        };

        /// <summary>
        /// ドラッグ点を置くレイヤー。MM はレイヤー 8 を使うが、COM3D2.5 の 8 は NGUI で
        /// CameraMain の cullingMask に含まれない。Unity のマウスメッセージは
        /// cullingMask &amp; eventMask でレイキャストするため、8 だとどのカメラからも当たらない。
        /// Default(0) は CameraMain の両マスクに入っており、Renderer は切ってあるので描画もされない
        /// </summary>
        private const int DragPointLayer = 0;

        // 円が大きいと体を覆って邪魔になるため、MM の当たり判定サイズ（0.12〜0.2）ではなく
        // 上体の点（0.04）を基準に揃える
        private const float IKDragPointScale = 0.04f;

        private const float FaceDragPointScale = 0.04f;

        private const float SpineDragPointScale = 0.04f;
        private const float PelvisDragPointScale = 0.04f;
        private const float MuneDragPointScale = 0.04f;

        private readonly List<GameObject> _dragPoints = new List<GameObject>();

        /// <summary>四肢・肩・胸の IK チェーン。ドラッグ点と一緒に破棄する</summary>
        private readonly List<MaidIKChain> _chains = new List<MaidIKChain>();

        /// <summary>ドラッグ点を張り付けているメイド。変わったときだけ作り直す</summary>
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

            CreateIKDragPoints(maid);
            CreateShoulderDragPoints(maid);
            CreateFaceDragPoint(maid);
            CreateSpineDragPoints(maid);
            CreatePelvisDragPoint(maid);
            CreateMuneDragPoints(maid);

            // 着替え等でボディが作り直されると揺れものが既定値に戻るため、
            // ドラッグ点を作り直すこのタイミングで保持している状態を焼き直す
            MaidManipulateManager.instance.muneYureController.Reapply(maid);

            // ボディが作り直されると trsLookTarget がカメラへ戻るため、視線も焼き直す
            MaidManipulateManager.instance.lookController.Apply(maid);
        }

        private void CreateIKDragPoints(Maid maid)
        {
            var bones = maid.body0.m_Bones.transform;

            foreach (var def in IKChainDefs)
            {
                var root = CMT.SearchObjName(bones, def.rootBone, false);
                var mid = CMT.SearchObjName(bones, def.midBone, false);
                var tip = CMT.SearchObjName(bones, def.tipBone, false);
                if (root == null || mid == null || tip == null)
                {
                    MTEUtils.LogWarning("IK ボーンが見つかりません: {0}", def.tipBone);
                    continue;
                }

                var chain = new MaidIKChain(root, mid, tip);
                _chains.Add(chain);

                CreateIKDragPoint(maid, chain, MaidIKChainPoint.Tip, tip);
                CreateIKDragPoint(maid, chain, MaidIKChainPoint.Joint, mid);
            }
        }

        /// <summary>
        /// 肩のドラッグ点。Clavicle から UpperArm までの 2 ボーンチェーンとして解く
        /// （MM の gClavicleL/R 相当）。鎖骨が回って肩が上下前後に動く。
        /// 肘/膝と違い 2 ボーンなので鎖骨は target の方を向くだけで、逆に折れる自由度が無い。
        /// このため可動域制限は付けていない。首と同じ RotationLimitAngle も使えない:
        /// 実測で素の姿勢が localRotation=identity から 93 度ずれており（首は 17.8 度）、
        /// ゲームと同じ identity 中心の円錐では普通のポーズが即クランプされてしまう
        /// </summary>
        private void CreateShoulderDragPoints(Maid maid)
        {
            var bones = maid.body0.m_Bones.transform;

            foreach (var pair in ShoulderBonePairs)
            {
                var clavicle = CMT.SearchObjName(bones, pair[0], false);
                var upperArm = CMT.SearchObjName(bones, pair[1], false);
                if (clavicle == null || upperArm == null)
                {
                    MTEUtils.LogWarning("肩のボーンが見つかりません: {0}", pair[0]);
                    continue;
                }

                var chain = new MaidIKChain(clavicle, null, upperArm);
                _chains.Add(chain);

                CreateIKDragPoint(maid, chain, MaidIKChainPoint.Tip, upperArm);
            }
        }

        /// <summary>チェーン上の 1 点を作る。GameObject 名は追従先のボーン名で一意になる</summary>
        private void CreateIKDragPoint(Maid maid, MaidIKChain chain,
            MaidIKChainPoint pointType, Transform followBone)
        {
            var go = CreateDragPointObject("MIE_IKDragPoint_" + followBone.name, IKDragPointScale);

            var point = go.AddComponent<MaidIKDragPoint>();
            point.maid = maid;
            point.chain = chain;
            point.pointType = pointType;
            point.followBone = followBone;

            _dragPoints.Add(go);
        }

        private void CreateFaceDragPoint(Maid maid)
        {
            var bones = maid.body0.m_Bones.transform;

            var neck = CMT.SearchObjName(bones, "Bip01 Neck", false);
            var head = CMT.SearchObjName(bones, "Bip01 Head", false);
            var headNub = CMT.SearchObjName(bones, "Bip01 HeadNub", false);
            if (neck == null || head == null || headNub == null)
            {
                MTEUtils.LogWarning("頭部のボーンが見つかりません");
                return;
            }

            var go = CreateDragPointObject("MIE_FaceDragPoint", FaceDragPointScale);

            var point = go.AddComponent<MaidFaceDragPoint>();
            point.maid = maid;
            point.neckBone = neck;
            point.headBone = head;
            point.headNubBone = headNub;

            _dragPoints.Add(go);
        }

        /// <summary>
        /// 上体のドラッグ点。4 ボーンそれぞれに点を置き、どれを掴んでも
        /// 4 ボーンへ重み配分して曲げる（MM の gSpine* と同じ構成）
        /// </summary>
        private void CreateSpineDragPoints(Maid maid)
        {
            var bones = maid.body0.m_Bones.transform;

            var spineBones = new Transform[SpineBoneNames.Length];
            for (var i = 0; i < SpineBoneNames.Length; i++)
            {
                spineBones[i] = CMT.SearchObjName(bones, SpineBoneNames[i], false);
                if (spineBones[i] == null)
                {
                    MTEUtils.LogWarning("上体のボーンが見つかりません: {0}", SpineBoneNames[i]);
                    return;
                }
            }

            var entries = new MaidBoneRotateDragPoint.Entry[spineBones.Length];
            for (var i = 0; i < spineBones.Length; i++)
            {
                entries[i] = new MaidBoneRotateDragPoint.Entry(
                    spineBones[i], SpineTiltWeights[i], SpineTwistWeights[i]);
            }

            // 4 点すべてが同じ entries を共有し、どの点を掴んでも全ボーンが連動して曲がる
            foreach (var bone in spineBones)
            {
                var go = CreateDragPointObject("MIE_SpineDragPoint_" + bone.name, SpineDragPointScale);

                var point = go.AddComponent<MaidBoneRotateDragPoint>();
                point.maid = maid;
                point.entries = entries;
                point.followBone = bone;
                point.pitchDivisor = 1f;
                point.yawDivisor = -1.5f;
                point.twistDivisor = 1.5f;

                _dragPoints.Add(go);
            }
        }

        private void CreatePelvisDragPoint(Maid maid)
        {
            var bones = maid.body0.m_Bones.transform;

            var pelvis = CMT.SearchObjName(bones, "Bip01 Pelvis", false);
            if (pelvis == null)
            {
                MTEUtils.LogWarning("骨盤のボーンが見つかりません");
                return;
            }

            var go = CreateDragPointObject("MIE_PelvisDragPoint", PelvisDragPointScale);

            var point = go.AddComponent<MaidBoneRotateDragPoint>();
            point.maid = maid;
            point.entries = new[] { new MaidBoneRotateDragPoint.Entry(pelvis, 1f, 1f) };
            point.followBone = pelvis;
            // MM の MouseDrag3 ido==3/6 と同じ感度。横方向は上体と逆符号（+x/6）
            point.pitchDivisor = 4f;
            point.yawDivisor = 6f;
            point.twistDivisor = 3f;

            _dragPoints.Add(go);
        }

        /// <summary>
        /// 胸のドラッグ点。Mune_* から Mune_*_sub までの 2 ボーンチェーンとして解く
        /// （MM の gIKMuneL/R 相当）。点は根本と先端の中点に置く
        /// </summary>
        private void CreateMuneDragPoints(Maid maid)
        {
            var bones = maid.body0.m_Bones.transform;

            foreach (var pair in MuneBonePairs)
            {
                var mune = CMT.SearchObjName(bones, pair[0], false);
                var sub = CMT.SearchObjName(bones, pair[1], false);
                if (mune == null || sub == null)
                {
                    // CRC 新ボディ等でボーン構成が異なる場合は警告を残してスキップする
                    MTEUtils.LogWarning("胸のボーンが見つかりません: {0}", pair[0]);
                    continue;
                }

                var chain = new MaidIKChain(mune, null, sub);
                _chains.Add(chain);

                var go = CreateDragPointObject("MIE_MuneDragPoint_" + pair[0], MuneDragPointScale);

                var point = go.AddComponent<MaidIKDragPoint>();
                point.maid = maid;
                point.chain = chain;
                point.pointType = MaidIKChainPoint.Tip;
                point.followBone = sub;
                point.followBoneSub = mune;
                point.isMune = true;
                // スライダー自動追従には回転の起点（Mune_*）を報告する
                point.sliderBoneName = mune.name;

                _dragPoints.Add(go);
            }
        }

        /// <summary>当たり判定だけを持つ透明な球を作る</summary>
        private static GameObject CreateDragPointObject(string name, float scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.GetComponent<Renderer>().enabled = false;
            go.transform.localScale = new Vector3(scale, scale, scale);
            go.layer = DragPointLayer;

            // 掴める範囲が分かるよう輪郭だけ描く（球のメッシュは出さない）
            go.AddComponent<MaidDragPointRing>();
            return go;
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

            foreach (var chain in _chains)
            {
                chain.Destroy();
            }
            _chains.Clear();

            _maid = null;
        }
    }
}
