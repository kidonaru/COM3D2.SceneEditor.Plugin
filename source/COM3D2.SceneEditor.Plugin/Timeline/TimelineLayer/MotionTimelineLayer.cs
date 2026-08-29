using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using UnityEngine;
using COM3D2.SceneEditor.Plugin;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    public class PoseTimeLineRow
    {
        public float time;
        public int poseType;
        public string animation;
        public float fadeTime;
        public float speed;
        public Vector3 position;
        public Vector3 rotation;
        public Maid.EyeMoveType eyeMoveType;
        public string option;
    }

    [TimelineLayerDesc("メイドアニメ", 0)]
    public class MotionTimelineLayer : TimelineLayerBase
    {
        public override Type layerType => typeof(MotionTimelineLayer);
        public override string layerName => nameof(MotionTimelineLayer);

        public override bool hasSlotNo => true;
        public override bool isMotionLayer => true;

        public static string GroundingBoneName = "Grounding";
        public static string GroundingDisplayName = "接地";

        /// <summary>IK 固定・接地の実体は SE 側が持つ (maidManager は MTE 側なので混同しないこと)</summary>
        private static MaidIKHoldController ikHoldController
            => MaidManipulateManager.instance.ikHoldController;

        protected override EditTargetStore trackedStore
        {
            get
            {
                var maid = this.maid;
                return maid != null
                    ? BoneEditManager.instance.FindMaidBoneTrackedStore(maid)
                    : null;
            }
        }

        private readonly List<string> _trackedCandidateNames = new List<string>();

        /// <summary>
        /// 拡張ボーンの候補。MTE の ExtendBoneCache が拾えたボーンだけが対象になる。
        /// 返り値は次回呼び出しで書き換わる使い捨てビューなので保持しないこと
        /// </summary>
        protected override List<string> trackedCandidateNames
        {
            get
            {
                _trackedCandidateNames.Clear();
                var cache = maidCache != null ? maidCache.extendBoneCache : null;
                if (cache != null)
                {
                    _trackedCandidateNames.AddRange(cache.entities.Keys);
                }
                return _trackedCandidateNames;
            }
        }

        protected override string trackedHistoryPrefix => "拡張ボーン";

        private readonly List<string> _allBoneNamesCache = new List<string>();

        /// <summary>
        /// 体ボーン・拡張ボーン・IK・接地・指ブレンドの全対象名。
        /// 返り値は次回呼び出しで書き換わる使い捨てビューなので保持しないこと
        /// </summary>
        public override List<string> allBoneNames
        {
            get
            {
                _allBoneNamesCache.Clear();
                _allBoneNamesCache.AddRange(BoneUtils.saveBoneNames);
                // 拡張ボーンはボーンウィンドウのチェック (∪ 既存キーフレーム記載) が対象集合
                _allBoneNamesCache.AddRange(trackedBoneNames);
                _allBoneNamesCache.AddRange(MaidCache.ikHoldTypeMap.Keys);
                _allBoneNamesCache.Add(GroundingBoneName);
                _allBoneNamesCache.AddRange(FingerBlendBoneNames);
                return _allBoneNamesCache;
            }
        }

        private MotionTimelineLayer(int slotNo) : base(slotNo)
        {
        }

        public static MotionTimelineLayer Create(int slotNo)
        {
            return new MotionTimelineLayer(slotNo);
        }

        public override void Init()
        {
            base.Init();

            if (maidCache == null)
            {
                return;
            }

            // IK 固定・接地の状態は SE の MaidIKHoldController が持つ (タイムライン所有ではない)。
            // 読み込み時のリセットは IK ウィンドウの設定を消すことになるため行わない

            foreach (var frame in keyFrames)
            {
                foreach (var extendBoneName in trackedBoneNames)
                {
                    var bone = frame.GetBone(extendBoneName);
                    if (bone == null)
                    {
                        continue;
                    }

                    var transform = bone.transform;
                    if (transform.position.x == float.MinValue)
                    {
                        MTEUtils.LogDebug("ボーンの初期位置を設定 boneName={0}", extendBoneName);
                        transform.position = maidCache.GetInitialPosition(extendBoneName);
                    }
                }
            }
        }

        protected override void InitMenuItems()
        {
            _allMenuItems.Clear();

            var setMenuItemMap = new Dictionary<BoneSetMenuType, BoneSetMenuItem>(12);

            foreach (var pair in BoneUtils.BoneTypeToSetMenuTypeMap)
            {
                var boneType = pair.Key;
                var boneSetType = pair.Value;

                var boneName = BoneUtils.GetBoneName(boneType);
                var displayName = BoneUtils.GetBoneJpName(boneType);
                var menuItem = new MaidBoneMenuItem(boneName, displayName);

                if (boneSetType == BoneSetMenuType.None)
                {
                    _allMenuItems.Add(menuItem);
                    continue;
                }

                BoneSetMenuItem setMenuItem;
                if (!setMenuItemMap.TryGetValue(boneSetType, out setMenuItem))
                {
                    var boneSetName = boneSetType.ToString();
                    var displaySetName = BoneUtils.GetBoneSetMenuJpName(boneSetType);
                    setMenuItem = new BoneSetMenuItem(boneSetName, displaySetName);
                    setMenuItemMap[boneSetType] = setMenuItem;
                    _allMenuItems.Add(setMenuItem);
                }

                setMenuItem.AddChild(menuItem);
            }

            var slotMenuItemMap = new Dictionary<string, BoneSetMenuItem>(12);

            foreach (var extendBoneName in trackedBoneNames)
            {
                if (maidCache == null)
                {
                    break;
                }
                var entity = maidCache.extendBoneCache.GetEntity(extendBoneName);
                if (entity == null)
                {
                    continue;
                }

                var slotName = entity.slotName;
                var boneName = entity.boneName;

                var menuItem = new ExtendBoneMenuItem(extendBoneName, boneName);

                BoneSetMenuItem setMenuItem;
                if (!slotMenuItemMap.TryGetValue(slotName, out setMenuItem))
                {
                    setMenuItem = new BoneSetMenuItem(slotName, slotName);
                    slotMenuItemMap[slotName] = setMenuItem;
                    _allMenuItems.Add(setMenuItem);
                }

                setMenuItem.AddChild(menuItem);
            }

            // 保存互換のため、タイムライン側の拡張ボーン一覧は追跡集合から作り直す。
            // ソース・オブ・トゥルースはボーンウィンドウのチェック側
            var extendBoneNames = timeline.GetExtendBoneNames(slotNo);
            extendBoneNames.Clear();
            foreach (var extendBoneName in trackedBoneNames)
            {
                extendBoneNames.Add(extendBoneName);
            }

            var ikSetMenuItem = new BoneSetMenuItem("IK", "IK");
            allMenuItems.Add(ikSetMenuItem);

            foreach (var boneName in MaidCache.ikHoldTypeMap.Keys)
            {
                MaidIKHoldType holdType;
                if (!MaidIKHoldController.TryParseHoldType(boneName, out holdType))
                {
                    continue;
                }

                var menuItem = new BoneMenuItem(
                    boneName, MaidIKHoldController.GetHoldTypeName(holdType));
                ikSetMenuItem.AddChild(menuItem);
            }

            var groundingMenuItem = new BoneMenuItem(GroundingBoneName, GroundingDisplayName);
            allMenuItems.Add(groundingMenuItem);

            var fingerBlendSetMenuItem = new BoneSetMenuItem("FingerBlend", "指ブレンド");
            allMenuItems.Add(fingerBlendSetMenuItem);

            foreach (var boneName in FingerBlendBoneNames)
            {
                var blendType = ConvertToFingerBlendType(boneName);
                var blendName = FingerBrendNames[(int)blendType];
                var menuItem = new BoneMenuItem(boneName, blendName);
                fingerBlendSetMenuItem.AddChild(menuItem);
            }
        }

        public override bool IsValidData()
        {
            errorMessage = "";

            var firstFrame = this.firstFrame;
            if (firstFrame == null || firstFrame.frameNo != 0)
            {
                errorMessage = "0フレーム目にキーフレームが必要です";
                return false;
            }

            return true;
        }

        public override void Update()
        {
            base.Update();
        }

        public override void LateUpdate()
        {
            base.LateUpdate();

            if (!studioHackManager.isPoseEditing)
            {
                ApplyPlayData();
            }
        }

        protected override void ApplyPlayData()
        {
            var maid = this.maid;
            if (maid == null || maid.body0 == null || !maid.body0.isLoadedBody)
            {
                return;
            }

            ApplyPlayDataByType(TransformType.ExtendBone);
            ApplyPlayDataByType(TransformType.IKHold);
            ApplyPlayDataByType(TransformType.Grounding);
            ApplyPlayDataByType(TransformType.FingerBlend);
        }

        protected override void ApplyMotion(MotionData motion, float t, bool indexUpdated, MotionPlayData playData)
        {
            switch (motion.start.type)
            {
                case TransformType.ExtendBone:
                    ApplyExtendBoneMotion(motion, t);
                    break;
                case TransformType.IKHold:
                    ApplyIKHoldMotion(motion, t);
                    break;
                case TransformType.Grounding:
                    ApplyGroundingMotion(motion);
                    break;
                case TransformType.FingerBlend:
                    ApplyFingerBlendMotion(motion);
                    break;
            }
        }

        private void ApplyExtendBoneMotion(MotionData motion, float t)
        {
            if (maidCache == null)
            {
                return;
            }

            var boneName = motion.name;
            var bone = maidCache.GetBoneTransform(boneName);
            if (bone == null)
            {
                return;
            }

            var start = motion.start;
            var end = motion.end;

            var t0 = motion.stFrame * timeline.frameDuration;
            var t1 = motion.edFrame * timeline.frameDuration;

            bone.localPosition = PluginUtils.HermiteVector3(
                t0,
                t1,
                start.positionValues,
                end.positionValues,
                t);

            bone.localScale = PluginUtils.HermiteVector3(
                t0,
                t1,
                start.scaleValues,
                end.scaleValues,
                t);
        }

        private void ApplyIKHoldMotion(MotionData motion, float t)
        {
            var maid = this.maid;
            if (maid == null)
            {
                return;
            }

            MaidIKHoldType holdType;
            if (!MaidIKHoldController.TryParseHoldType(motion.name, out holdType))
            {
                return;
            }

            var start = motion.start as TransformDataIKHold;
            var end = motion.end as TransformDataIKHold;

            var t0 = motion.stFrame * timeline.frameDuration;
            var t1 = motion.edFrame * timeline.frameDuration;

            ikHoldController.SetAnime(maid, holdType, start.isAnime);
            ikHoldController.SetHold(maid, holdType, start.isHold);
            ikHoldController.SetTargetPosition(maid, holdType, PluginUtils.HermiteVector3(
                t0,
                t1,
                start.positionValues,
                end.positionValues,
                t));
        }

        private void ApplyGroundingMotion(MotionData motion)
        {
            var maid = this.maid;
            if (maid == null)
            {
                return;
            }

            var start = motion.start as TransformDataGrounding;
            var holdParams = ikHoldController.GetParams(maid);

            holdParams.isGroundingFootL = start.isGroundingFootL;
            holdParams.isGroundingFootR = start.isGroundingFootR;
            holdParams.floorHeight = start.floorHeight;
            holdParams.footBaseOffset = start.footBaseOffset;
            holdParams.footStretchHeight = start.footStretchHeight;
            holdParams.footStretchAngle = start.footStretchAngle;
            holdParams.footGroundAngle = start.footGroundAngle;
        }

        private void ApplyFingerBlendMotion(MotionData motion)
        {
            // 指ブレンドはポーズ編集中のみ反映
            if (!studioHackManager.isPoseEditing || !timeline.fingerBlendEnabled)
            {
                return;
            }

            var blendType = ConvertToFingerBlendType(motion.name);
            var trans = motion.start as TransformDataFingerBlend;
            trans.ApplyUnit(GetFingerBlendUnit(blendType));
        }

        public override void OnMaidChanged(Maid maid)
        {
            // 候補 (extendBoneCache) ごと入れ替わるため、追跡集合のキャッシュを捨てる
            InvalidateTrackedBoneNames();
            InitMenuItems();
        }

        public override void UpdateFrame(FrameData frame, bool initialEdit, bool force)
        {
            var cacheBoneData = maidManager.cacheBoneData;
            if (cacheBoneData == null)
            {
                MTEUtils.LogError("ボーンデータが取得できませんでした");
                return;
            }

            var rootBone = cacheBoneData.GetBoneData("Bip01");
            if (rootBone == null)
            {
                MTEUtils.LogError("中心ボーンが取得できませんでした");
                return;
            }

            // 編集モード中の移動は中心ボーンに反映  
            if (timelineManager.initialEditFrame != null)
            {
                var targetPosition = rootBone.transform.position;
                var targetRotation = rootBone.transform.rotation;

                maid.transform.position = timelineManager.initialEditPosition;
                maid.transform.rotation = timelineManager.initialEditRotation;

                rootBone.transform.position = targetPosition;
                rootBone.transform.rotation = targetRotation;
            }

            var maidCache = this.maidCache;

            foreach (var name in BoneUtils.saveBoneNames)
            {
                var transform = maidCache.GetBoneTransform(name);
                if (transform == null)
                {
                    MTEUtils.LogDebug("UpdateFrame: ボーンがないのでスキップしました name={0}", name);
                    continue;
                }

                ITransformData trans;
                if (name == "Bip01")
                {
                    trans = CreateTransformData<TransformDataRoot>(name);
                }
                else
                {
                    trans = CreateTransformData<TransformDataRotation>(name);
                }

                if (trans.hasPosition)
                {
                    trans.position = transform.localPosition;
                }
                if (trans.hasRotation)
                {
                    trans.rotation = transform.localRotation;
                }
                if (trans.hasScale)
                {
                    trans.scale = transform.localScale;
                }

                var bone = frame.CreateBone(trans);
                frame.UpdateBone(bone);
            }

            foreach (var name in trackedBoneNames)
            {
                var transform = maidCache.GetBoneTransform(name);
                if (transform == null)
                {
                    MTEUtils.LogDebug("UpdateFrame: ボーンがないのでスキップしました name={0}", name);
                    continue;
                }

                var trans = CreateTransformData<TransformDataExtendBone>(name);
                trans.position = transform.localPosition;
                trans.rotation = transform.localRotation;
                trans.scale = transform.localScale;

                var bone = frame.CreateBone(trans);
                frame.UpdateBone(bone);
            }

            foreach (var name in MaidCache.ikHoldTypeMap.Keys)
            {
                MaidIKHoldType holdType;
                if (!MaidIKHoldController.TryParseHoldType(name, out holdType))
                {
                    continue;
                }

                var trans = CreateTransformData<TransformDataIKHold>(name);
                trans.position = ikHoldController.GetTargetPosition(maid, holdType);
                trans.isHold = ikHoldController.GetHold(maid, holdType);
                trans.isAnime = ikHoldController.GetAnime(maid, holdType);

                var bone = frame.CreateBone(trans);
                frame.UpdateBone(bone);
            }

            {
                var name = GroundingBoneName;
                var holdParams = ikHoldController.GetParams(maid);

                var trans = CreateTransformData<TransformDataGrounding>(name);
                trans.isGroundingFootL = holdParams.isGroundingFootL;
                trans.isGroundingFootR = holdParams.isGroundingFootR;
                trans.floorHeight = holdParams.floorHeight;
                trans.footBaseOffset = holdParams.footBaseOffset;
                trans.footStretchHeight = holdParams.footStretchHeight;
                trans.footStretchAngle = holdParams.footStretchAngle;
                trans.footGroundAngle = holdParams.footGroundAngle;

                var bone = frame.CreateBone(trans);
                frame.UpdateBone(bone);
            }

            foreach (var name in FingerBlendBoneNames)
            {
                var unit = GetFingerBlendUnit(ConvertToFingerBlendType(name));
                if (unit == null)
                {
                    continue;
                }

                var trans = CreateTransformData<TransformDataFingerBlend>(name);
                trans.UpdateFromUnit(unit);

                var bone = frame.CreateBone(trans);
                frame.UpdateBone(bone);
            }
        }

        public override void ApplyAnm(long id, byte[] anmData)
        {
            MTEUtils.LogDebug("ApplyAnm: id={0}", id);
            if (anmData == null)
            {
                return;
            }

            var maid = this.maid;
            if (maid == null)
            {
                MTEUtils.LogError("メイドが配置されていません");
                return;
            }

            float playingFrameNoFloat = defaultLayer.playingFrameNoFloat;
            var isAnmPlaying = this.isAnmPlaying;
            if (isAnmPlaying)
            {
                playingFrameNoFloat += 0.01f; // モーション再生中は再生位置に差分がないと反映されない
            }

            MTEUtils.LogDebug("playingFrameNoFloat={0}", playingFrameNoFloat);

            maidCache.PlayAnm(id, anmData);
            studioHack.OnMotionUpdated(maid);
            maidManager.OnMotionUpdated(maid);

            this.isAnmPlaying = isAnmPlaying;
            maidCache.playingFrameNoFloat = playingFrameNoFloat;

            var stopwatch = new StopwatchDebug();
            ApplyPlayData();
            stopwatch.ProcessEnd("  ApplyPlayData: " + layerName);
        }

        public override void ApplyCurrentFrame(bool motionUpdate)
        {
            if (anmId != TimelineAnmId || motionUpdate)
            {
                CreateAndApplyAnm();
            }
            else
            {
                maidCache.playingFrameNo = timelineManager.currentFrameNo;
                var stopwatch = new StopwatchDebug();
                ApplyPlayData();
                stopwatch.ProcessEnd("  ApplyPlayData: " + layerName);
            }
        }

        public override void OutputAnm()
        {
            try
            {
                var anmData = GetAnmBinary(true);
                if (anmData == null)
                {
                    MTEUtils.LogError("モーションの出力に失敗しました");
                    return;
                }
                var anmPath = this.anmPath;
                var anmFileName = this.anmFileName;

                bool isExist = File.Exists(anmPath);
                File.WriteAllBytes(anmPath, anmData);

                studioHack.OnUpdateMyPose(anmPath, isExist);
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
                MTEUtils.ShowDialog("モーションの出力に失敗しました");
            }
        }

        List<float> _timesCache = new List<float>(128);
        List<ValueData[]> _valuesListCache = new List<ValueData[]>(128);

        protected override byte[] GetAnmBinaryInternal(bool forOutput, int startFrameNo, int endFrameNo)
        {
            if (maidCache == null)
            {
                return null;
            }

            var startSecond = timeline.GetFrameTimeSeconds(startFrameNo);
            var endSecond = timeline.GetFrameTimeSeconds(endFrameNo);

            int _startFrameNo = startFrameNo;
            int _endFrameNo = endFrameNo;
            Action<BinaryWriter, List<BoneData>> write_bones = delegate (
                BinaryWriter w,
                List<BoneData> bones)
            {
                if (bones.Count == 0)
                {
                    return;
                }

                var firstBone = bones[0];
                var name = firstBone.name;
                var path = maidCache.GetBonePath(name);
                if (string.IsNullOrEmpty(path))
                {
                    MTEUtils.LogWarning("ボーンがないのでスキップしました boneName={0}", name);
                    return;
                }

                w.Write((byte)1);
                w.Write(path);

                _timesCache.Clear();
                _valuesListCache.Clear();

                BoneData prevBone = null;

                foreach (var bone in bones)
                {
                    if (bone.frameNo < _startFrameNo)
                    {
                        prevBone = bone;
                        continue;
                    }

                    // 開始フレームにキーフレームがない場合は前のキーフレームを使う
                    if (_timesCache.Count == 0 && bone.frameNo != _startFrameNo && prevBone != null)
                    {
                        _timesCache.Add(0f);
                        _valuesListCache.Add(prevBone.transform.values);
                    }

                    if (bone.frameNo > _endFrameNo)
                    {
                        // 終了フレームにキーフレームがない場合は後のキーフレームを使う
                        if (prevBone != null && prevBone.frameNo != _endFrameNo)
                        {
                            _timesCache.Add(endSecond - startSecond);
                            _valuesListCache.Add(bone.transform.values);
                        }
                        break;
                    }

                    _timesCache.Add(timeline.GetFrameTimeSeconds(bone.frameNo) - startSecond);
                    _valuesListCache.Add(bone.transform.values);
                    prevBone = bone;
                }

                // anmフォーマットのチャンネル107以降はマテリアルUV(_MainTex_ST等)に割り当てられているため、
                // スケール値は書き出さない (回転4 + 位置3 の最大7チャンネルまで)
                var channelCount = Mathf.Min(firstBone.transform.valueCount, 7);
                for (int i = 0; i < channelCount; i++)
                {
                    w.Write((byte)(100 + i));
                    w.Write(_timesCache.Count);
                    for (int j = 0; j < _timesCache.Count; j++)
                    {
                        w.Write(_timesCache[j]);
                        w.Write(_valuesListCache[j][i].value);
                        w.Write(_valuesListCache[j][i].inTangent.value);
                        w.Write(_valuesListCache[j][i].outTangent.value);
                    }
                }
            };

            MemoryStream memoryStream = new MemoryStream();
            BinaryWriter binaryWriter = new BinaryWriter(memoryStream);
            binaryWriter.Write("CM3D2_ANIM");
            binaryWriter.Write(1001);

            foreach (var bones in _timelineBonesMap.Values)
            {
                if (bones.Count == 0)
                {
                    continue;
                }

                switch (bones[0].transform.type)
                {
                    case TransformType.Root:
                    case TransformType.Rotation:
                    case TransformType.ExtendBone:
                        write_bones(binaryWriter, bones);
                        break;
                }
            }

            binaryWriter.Write((byte)0);
            binaryWriter.Write((byte)(useMuneKeyL ? 1u : 0u));
            binaryWriter.Write((byte)(useMuneKeyR ? 1u : 0u));
            binaryWriter.Close();
            memoryStream.Close();
            byte[] result = memoryStream.ToArray();
            memoryStream.Dispose();

            if (!forOutput && maidCache != null)
            {
                maidCache.anmStartFrameNo = startFrameNo;
                maidCache.anmEndFrameNo = endFrameNo;
            }

            return result;
        }

        public void OutputPoseCsv(
            List<PoseTimeLineRow> rows,
            string filePath)
        {
            var offsetTime = timeline.startOffsetTime;

            var builder = new StringBuilder();
            builder.Append("time,poseType,animation,fadeTime,speed,posX,posY,posZ,rotX,rotY,rotZ,eyeMoveType,option\r\n");

            Action<PoseTimeLineRow, bool> appendRow = (row, isFirst) =>
            {
                var time = row.time;

                if (!isFirst)
                {
                    time += offsetTime;
                }

                builder.Append(time.ToString("0.000") + ",");
                builder.Append(row.poseType + ",");
                builder.Append(row.animation + ",");
                builder.Append(row.fadeTime.ToString("0.000") + ",");
                builder.Append(row.speed.ToString("0.000") + ",");
                builder.Append(row.position.x.ToString("0.000") + ",");
                builder.Append(row.position.y.ToString("0.000") + ",");
                builder.Append(row.position.z.ToString("0.000") + ",");
                builder.Append(row.rotation.x.ToString("0.000") + ",");
                builder.Append(row.rotation.y.ToString("0.000") + ",");
                builder.Append(row.rotation.z.ToString("0.000") + ",");
                builder.Append((int) row.eyeMoveType + ",");
                builder.Append(row.option);
                builder.Append("\r\n");
            };

            if (rows.Count > 0 && offsetTime > 0f)
            {
                appendRow(rows.First(), true);
            }

            foreach (var row in rows)
            {
                appendRow(row, false);
            }

            using (var streamWriter = new StreamWriter(filePath, false))
            {
                streamWriter.Write(builder.ToString());
            }
        }

        // DCM 連携は未移植のため出力しない
        public override void OutputDCM(XElement songElement)
        {
        }

        public override void DrawWindow(GUIView view)
        {
            // ボーン・IK・指の編集 UI は SE の各ウィンドウに委譲する (レイヤー UI 非接続方針)。
            // キー書き込み (UpdateFrame) はいずれもライブ状態を読むため、
            // 個別ウィンドウでの編集がそのままキー化される
            view.DrawLabel("ボーンの編集はボーンウィンドウ・Inspectorで行ってください", -1, 20);
            view.DrawLabel("IK固定・接地はIKウィンドウで行ってください", -1, 20);
            view.DrawLabel("指の編集は指ウィンドウで行ってください", -1, 20);
        }

        public static readonly string[] FingerBlendBoneNames = new string[]
        {
            "ArmFingerBlendR",
            "ArmFingerBlendL",
            "LegFingerBlendR",
            "LegFingerBlendL",
        };

        public static readonly Dictionary<string, WindowPartsFingerBlend.Type> FingerBlendBoneTypeMap =
            new Dictionary<string, WindowPartsFingerBlend.Type>
            {
                { "ArmFingerBlendR", WindowPartsFingerBlend.Type.RightArm },
                { "ArmFingerBlendL", WindowPartsFingerBlend.Type.LeftArm },
                { "LegFingerBlendR", WindowPartsFingerBlend.Type.RightLeg },
                { "LegFingerBlendL", WindowPartsFingerBlend.Type.LeftLeg },
            };

        private static readonly string[] FingerBrendNames = new string[]
        {
            "右手",
            "左手",
            "右足",
            "左足",
        };

        private static WindowPartsFingerBlend.Type ConvertToFingerBlendType(string boneName)
        {
            WindowPartsFingerBlend.Type type;
            if (FingerBlendBoneTypeMap.TryGetValue(boneName, out type))
            {
                return type;
            }

            MTEUtils.LogError("ConvertToFingerBlendType: 不明なボーン名です boneName={0}", boneName);
            return WindowPartsFingerBlend.Type.RightArm;
        }

        private static bool IsFingerBlendBone(string boneName)
        {
            return FingerBlendBoneTypeMap.ContainsKey(boneName);
        }

        /// <summary>
        /// SE の指ブレンドユニット。指ボーンの書き手はゲームの FingerBlend ではなく
        /// SE の MaidFingerBlendController に一本化する。
        /// コントローラは編集対象 (アクティブメイド。TimelineSelectionBridge で MTE の選択と同期)
        /// のユニットしか持たないため、このレイヤーのメイドが対象でないときは null を返す
        /// </summary>
        private FingerBlendUnit GetFingerBlendUnit(WindowPartsFingerBlend.Type type)
        {
            var controller = MaidManipulateManager.instance.fingerBlendController;
            if (controller.maid == null || controller.maid != maid)
            {
                return null;
            }

            // 列挙順は WindowPartsFingerBlend.Type と FingerBlendType で一致している
            return controller.GetUnit((FingerBlendType)type);
        }

        public override SingleFrameType GetSingleFrameType(TransformType transformType)
        {
            switch (transformType)
            {
                case TransformType.Grounding:
                case TransformType.FingerBlend:
                    return SingleFrameType.None;
            }

            return base.GetSingleFrameType(transformType);
        }

        private static Dictionary<string, TransformType> _transformTypeCache = new Dictionary<string, TransformType>();

        public override TransformType GetTransformType(string name)
        {
            TransformType type;
            if (_transformTypeCache.TryGetValue(name, out type))
            {
                return type;
            }

            type = GetTransformTypeInternal(name);
            _transformTypeCache[name] = type;
            return type;
        }

        private TransformType GetTransformTypeInternal(string name)
        {
            var holdtype = MaidCache.GetIKHoldType(name);
            if (holdtype != IKHoldType.Max)
            {
                return TransformType.IKHold;
            }
            else if (name == GroundingBoneName)
            {
                return TransformType.Grounding;
            }
            else if (name == "Bip01")
            {
                return TransformType.Root;
            }
            else if (BoneUtils.IsDefaultBoneName(name))
            {
                return TransformType.Rotation;
            }
            else if (IsFingerBlendBone(name))
            {
                return TransformType.FingerBlend;
            }
            else
            {
                return TransformType.ExtendBone;
            }
        }
    }
}