using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using RootMotion.FinalIK;
using UnityEngine;
using UnityEngine.Events;
// SE 側とは Config / MaidManager など同名の型が多いため、別名で読み込んで衝突を避ける
using SEP = COM3D2.SceneEditor.Plugin;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    using AttachPoint = PhotoTransTargetObject.AttachPoint;

    public enum MaidPointType
    {
        Head,
        Chest,
        Crotch,
        Hip,
        Bip01,
    }

    public enum LookAtTargetType
    {
        None,
        Camera,
        Maid,
        Model,
    }

    public partial class MaidCache
    {
        public int slotNo = 0;
        public Maid maid = null;
        public MaidInfo info = null;
        public string annName = "";
        public long anmId = 0;
        public AnimationState animationState = null;
        public int anmStartFrameNo = 0;
        public int anmEndFrameNo = 0;
        public CacheBoneDataArray cacheBoneData;
        public IKManager ikManager = null;
        public ExtendBoneCache extendBoneCache = null;
        public MaidPropCache maidPropCache = null;
        public List<MaidSlotStat> slotStats = new List<MaidSlotStat>(32);
        public Dictionary<TBody.SlotID, MaidSlotStat> slotStatMap = new Dictionary<TBody.SlotID, MaidSlotStat>(32);
        public Dictionary<string, ModelMaterial> materialMap = new Dictionary<string, ModelMaterial>(32);
        public List<string> materialNames = new List<string>(32);
        public List<AnimationLayerInfo> animationLayerInfos = new List<AnimationLayerInfo>(10);

        public static readonly int MinLayerIndex = 2;
        public static readonly int MaxLayerIndex = 8;

        public static event UnityAction<int, Maid> onMaidChanged;
        public static event UnityAction<int, string> onAnmChanged;

        public static readonly Dictionary<string, IKHoldType> ikHoldTypeMap = Enum.GetValues(typeof(IKHoldType))
                .Cast<IKHoldType>()
                .Where(t => t != IKHoldType.Max)
                .ToDictionary(t => t.ToString(), t => t);

        public Animation animation
        {
            get => maid != null ? maid.GetAnimation() : null;
        }

        public float anmSpeed
        {
            get => animationState != null ? animationState.speed : 0;
            set
            {
                if (animationState != null)
                {
                    animationState.speed = value;
                }
            }
        }

        public float _motionSliderRate = 0f;
        public float motionSliderRate
        {
            get => _motionSliderRate;
            set
            {
                //MTEUtils.LogDebug("Update motionSliderRate slot={0} rate={1}", slotNo, value);
                _motionSliderRate = value;

                if (animationState != null)
                {
                    var isAnmEnabled = animationState.enabled;
                    var maxNum = animationState.length;
                    var current = Mathf.Clamp01(value) * maxNum;
                    animationState.time = current;

                    if (!isAnmEnabled)
                    {
                        animationState.enabled = true;
                        animation.Sample();
                        animationState.enabled = false;
                    }
                }
            }
        }

        public int playingFrameNo
        {
            get => (int) Mathf.Round(playingFrameNoFloat);
            set => playingFrameNoFloat = value;
        }

        public float playingFrameNoFloat
        {
            get
            {
                if (!isAnmSyncing)
                {
                    return timelineManager.currentFrameNo;
                }

                var rate = motionSliderRate;
                var anmLength = anmEndFrameNo - anmStartFrameNo;
                return anmStartFrameNo + rate * anmLength;
            }
            set
            {
                var anmLength = anmEndFrameNo - anmStartFrameNo;
                if (anmLength > 0)
                {
                    var rate = Mathf.Clamp01((float) (value - anmStartFrameNo) / anmLength);
                    motionSliderRate = rate;
                }
            }
        }

        // アニメーションと同期しているか
        public bool isAnmSyncing
        {
            get => timelineManager.IsValidData() && anmId == TimelineLayerBase.TimelineAnmId;
        }

        // アニメーションが有効か
        public bool isAnmEnabled
        {
            get
            {
                if (animationState != null)
                {
                    return animationState.enabled;
                }
                return false;
            }
            set
            {
                if (animationState != null)
                {
                    animationState.enabled = value;
                }
            }
        }

        // タイムラインアニメーションを再生中か
        public bool isAnmPlaying
        {
            get => isAnmEnabled && isAnmSyncing && anmSpeed > 0f;
            set
            {
                if (value)
                {
                    anmSpeed = timelineManager.anmSpeed;
                    isAnmEnabled = true;
                }
                else
                {
                    anmSpeed = 0f;
                }
            }
        }

        public Transform trsEyeL
        {
            get => maid != null ? maid.body0.trsEyeL : null;
        }

        public Transform trsEyeR
        {
            get => maid != null ? maid.body0.trsEyeR : null;
        }

        public Vector3 eyesPosL
        {
            get
            {
                if (maid != null)
                {
                    var initPos = info.initEyesPosL;
                    return trsEyeL.localPosition - initPos;
                }
                return Vector3.zero;
            }
            set
            {
                if (maid != null)
                {
                    var initPos = info.initEyesPosL;
                    trsEyeL.localPosition = value + initPos;
                }
            }
        }

        public Vector3 eyesPosR
        {
            get
            {
                if (maid != null)
                {
                    var initPos = info.initEyesPosR;
                    return trsEyeR.localPosition - initPos;
                }
                return Vector3.zero;
            }
            set
            {
                if (maid != null)
                {
                    var initPos = info.initEyesPosR;
                    trsEyeR.localPosition = value + initPos;
                }
            }
        }

        public Vector3 eyesScaL
        {
            get
            {
                if (maid != null)
                {
                    var initSca = info.initEyesScaL;
                    return trsEyeL.localScale - initSca;
                }
                return Vector3.zero;
            }
            set
            {
                if (maid != null)
                {
                    var initSca = info.initEyesScaL;
                    trsEyeL.localScale = value + initSca;
                }
            }
        }

        public Vector3 eyesScaR
        {
            get
            {
                if (maid != null)
                {
                    var initSca = info.initEyesScaR;
                    return trsEyeR.localScale - initSca;
                }
                return Vector3.zero;
            }
            set
            {
                if (maid != null)
                {
                    var initSca = info.initEyesScaR;
                    trsEyeR.localScale = value + initSca;
                }
            }
        }

        private LookAtTargetType _lookAtTargetType;
        public LookAtTargetType lookAtTargetType
        {
            get => _lookAtTargetType;
            set
            {
                _lookAtTargetType = value;
                UpdateLookAtTarget();
            }
        }

        private int _lookAtTargetIndex;
        public int lookAtTargetIndex
        {
            get => _lookAtTargetIndex;
            set
            {
                _lookAtTargetIndex = value;
                UpdateLookAtTarget();
            }
        }

        private MaidPointType _lookAtMaidPointType;
        public MaidPointType lookAtMaidPointType
        {
            get => _lookAtMaidPointType;
            set
            {
                _lookAtMaidPointType = value;
                UpdateLookAtTarget();
            }
        }

        public Vector3 eyeEulerAngle;

        public string oneShotVoiceName = string.Empty;
        public float oneShotVoiceStartTime = 0f;
        public float oneShotVoiceLength = 0f;
        public float voiceFadeTime = 0.1f;
        public float voicePitch = 1f;
        public string loopVoiceName = string.Empty;

        private static FieldInfo fieldLimbControlList = null;

        public List<LimbControl> limbControlList
        {
            get
            {
                if (ikManager == null)
                {
                    return new List<LimbControl>();
                }
                if (fieldLimbControlList == null)
                {
                    fieldLimbControlList = typeof(IKManager).GetField("limb_control_list_", BindingFlags.NonPublic | BindingFlags.Instance);
                    MTEUtils.AssertNull(fieldLimbControlList != null, "fieldLimbControlList is null");
                }
                return (List<LimbControl>) fieldLimbControlList.GetValue(ikManager);
            }
        }

        public string fullName
        {
            get => maid != null ? maid.status.fullNameJpStyle : "";
        }

        private static TimelineManager timelineManager => TimelineManager.instance;
        private static TimelineData timeline => timelineManager.timeline;
        private static StudioHackBase studioHack => StudioHackManager.instance.studioHack;
        private static MaidManager maidManager => MaidManager.instance;
        private static StudioModelManager modelManager => StudioModelManager.instance;

        public MaidCache(int slotNo)
        {
            this.slotNo = slotNo;

            for (int i = 0; i <= MaxLayerIndex; i++)
            {
                animationLayerInfos.Add(new AnimationLayerInfo(i));
            }
        }

        public static IKHoldType GetIKHoldType(string holdName)
        {
            IKHoldType holdType;
            if (ikHoldTypeMap.TryGetValue(holdName, out holdType))
            {
                return holdType;
            }
            return IKHoldType.Max;
        }

        public Vector3 GetInitialPosition(IKManager.BoneType boneType)
        {
            return info.GetInitialPosition(boneType);
        }

        public void Reset()
        {
            maid = null;
            info = null;
            cacheBoneData = null;
            ikManager = null;
            extendBoneCache = null;
            maidPropCache = null;
            _blendShapeCache.Clear();
            slotStats.Clear();
            slotStatMap.Clear();
            materialMap.Clear();
            materialNames.Clear();
            lookAtTargetType = LookAtTargetType.None;
            lookAtTargetIndex = 0;
            lookAtMaidPointType = MaidPointType.Head;
            eyeEulerAngle = Vector3.zero;
            oneShotVoiceName = string.Empty;
            oneShotVoiceStartTime = 0f;
            oneShotVoiceLength = 0f;
            voiceFadeTime = 0.1f;
            voicePitch = 1f;
            loopVoiceName = string.Empty;

            ResetAnm();
        }

        public void ResetAnm()
        {
            annName = "";
            anmId = 0;
            animationState = null;

            foreach (var info in animationLayerInfos)
            {
                info.Reset();
            }
        }

        public void ResetEyes()
        {
            eyesPosL = Vector3.zero;
            eyesPosR = Vector3.zero;
            eyesScaL = Vector3.zero;
            eyesScaR = Vector3.zero;
        }

        public void Update(Maid maid)
        {
            if (maid != this.maid)
            {
                OnMaidChanged(maid);
            }

            if (maid == null || animation == null)
            {
                return;
            }

            // アニメ名更新
            var anmName = maid.body0.LastAnimeFN;
            if (this.annName != anmName)
            {
                OnAnmChanged(anmName);
            }

            var animationState = this.animationState;
            if (animationState != null && animationState.enabled && animationState.length > 0f)
            {
                float value = animationState.time;
                if (animationState.length < animationState.time)
                {
                    if (animationState.wrapMode == WrapMode.ClampForever)
                    {
                        value = animationState.length;
                    }
                    else
                    {
                        value = animationState.time - animationState.length * (float)((int)(animationState.time / animationState.length));
                    }
                }
                _motionSliderRate = value / animationState.length;
            }

            UpdateEyeEulerAngle();
            UpdateVoice();
        }

        public void PlayAnm(long id, byte[] anmData)
        {
            if (anmData == null)
            {
                return;
            }
            if (maid == null)
            {
                return;
            }

            GameMain.Instance.ScriptMgr.StopMotionScript();

            UpdateMuneYure();
            UpdateHeadLook();

            maid.body0.CrossFade(id.ToString(), anmData, false, false, false, 0f, 1f);
            maid.SetAutoTwistAll(true);

            var animation = maid.GetAnimation();
            if (animation != null)
            {
                animation.wrapMode = timeline.isLoopAnm ? WrapMode.Loop : WrapMode.ClampForever;
            }
        }

        public void UpdateMuneYure()
        {
            if (maid != null && !maid.boMAN)
            {
                maid.body0.MuneYureL((float)((!timeline.useMuneKeyL) ? 1 : 0));
                maid.body0.MuneYureR((float)((!timeline.useMuneKeyR) ? 1 : 0));
                maid.body0.jbMuneL.enabled = !timeline.useMuneKeyL;
                maid.body0.jbMuneR.enabled = !timeline.useMuneKeyR;
            }
        }

        public void UpdateHeadLook()
        {
            if (maid == null || timeline == null)
            {
                return;
            }

            // Maid.EyeToCamera は目線種別のフラグと同時に trsLookTarget をカメラへ
            // 書き換えてしまう。向け先の所有者は SE の MaidLookController なのでフラグだけ写す
            SEP.MaidLookBridge.ApplyEyeMoveType(maid, timeline.eyeMoveType);
            maid.LockHeadAndEye(false);

            if (timeline.useHeadKey)
            {
                var trsEyeL = maid.body0.trsEyeL;
                var trsEyeR = maid.body0.trsEyeR;
                trsEyeL.localRotation = maid.body0.quaDefEyeL;
                trsEyeR.localRotation = maid.body0.quaDefEyeR;
            }

            UpdateLookAtTarget();
        }

        /// <summary>
        /// 注視先を SE の MaidLookController へ反映する。
        /// trsLookTarget を直接書かず、向け先の所有者をコントローラに一本化する
        /// </summary>
        public void UpdateLookAtTarget()
        {
            if (maid == null || timeline == null)
            {
                return;
            }

            var lookAtTarget = GetLookAtTarget();
            var lookMode = SEP.MaidLookBridge.ResolveLookMode(
                timeline.useHeadKey, lookAtTargetType, lookAtTarget != null);
            if (lookMode == null)
            {
                return;
            }

            SEP.MaidLookBridge.ApplyLookMode(maid, lookMode.Value, lookAtTarget);

            // 固定化中に向け先が無いときだけ、瞳回転を TBody に上書きされないよう固定する。
            // 視線そらしはロック中は動かないため、そらし指定なら TBody の演出を優先する
            maid.LockHeadAndEye(
                lookMode.Value == SEP.MaidLookMode.無し
                && !SEP.MaidLookBridge.IsEyeSorashi(timeline.eyeMoveType));
        }

        public void UpdateEyeEulerAngle()
        {
            if (maid == null || timeline == null || !timeline.useHeadKey)
            {
                return;
            }

            // 視線そらし中の瞳は TBody の演出が動かす。固定値で上書きすると演出が潰れる
            if (SEP.MaidLookBridge.IsEyeSorashi(timeline.eyeMoveType))
            {
                return;
            }

            // 向け先の実体は MaidLookController が決めるため、TBody の現在値を見る
            var lookAtTarget = maid.body0.trsLookTarget;
            if (lookAtTarget == null)
            {
                maid.body0.SetEyeEulerAngle(eyeEulerAngle);

                this.trsEyeL.localRotation = maid.body0.quaDefEyeL * Quaternion.Euler(0f, -eyeEulerAngle.x * 0.2f + maid.body0.m_editYorime, -eyeEulerAngle.z * 0.1f);
                this.trsEyeR.localRotation = maid.body0.quaDefEyeR * Quaternion.Euler(0f, eyeEulerAngle.x * 0.2f + maid.body0.m_editYorime, eyeEulerAngle.z * 0.1f);
            }
            else
            {
                eyeEulerAngle = maid.body0.GetEyeEulerAngle();
            }
        }

        public Transform GetAttachPointTransform(AttachPoint point)
        {
            if (ikManager == null || point == AttachPoint.Null)
            {
                return null;
            }

            var boneType = BoneUtils.GetBoneType(point);
            var bone = ikManager.GetBone(boneType);
            return bone != null ? bone.transform : null;
        }

        private Dictionary<string, MaidBlendShape> _blendShapeCache =
                new Dictionary<string, MaidBlendShape>();

        /// <summary>着替えで TMorph が差し替わった後に呼び、シェイプキーのキャッシュを破棄する</summary>
        public void ClearBlendShapeCache()
        {
            _blendShapeCache.Clear();
        }

        public MaidBlendShape GetBlendShape(string shapeKey)
        {
            MaidBlendShape blendShape;
            if (_blendShapeCache.TryGetValue(shapeKey, out blendShape))
            {
                return blendShape;
            }

            blendShape = GetBlendShapeInternal(shapeKey);
            // ボディ未ロード時の null を覚えると、ロード後もキャッシュ破棄まで null を返し続けるため
            if (blendShape != null)
            {
                _blendShapeCache[shapeKey] = blendShape;
            }
            return blendShape;
        }

        private MaidBlendShape GetBlendShapeInternal(string shapeKey)
        {
            if (maid == null || maid.body0 == null)
            {
                return null;
            }

            var blendShape = new MaidBlendShape();

            // COM3D2.5 の goSlot は直接列挙できないため、インデックス走査で両バージョンに対応する
            var slotCount = Mathf.Min((int) TBody.SlotID.end, maid.body0.goSlot.Count);
            for (var i = 0; i < slotCount; i++)
            {
                var slot = maid.body0.GetSlot(i);
                if (slot == null || slot.morph == null || slot.morph.hash.Count == 0)
                {
                    continue;
                }

                if (slot.morph.Contains(shapeKey))
                {
                    blendShape.entities.Add(new MaidBlendShape.Entity
                    {
                        morph = slot.morph,
                        shapeKeyIndex = (int) slot.morph.hash[shapeKey],
                    });
                }
            }

            return blendShape;
        }

        public void SetBlendShapeValue(string shapeKey, float value)
        {
            var blendShape = GetBlendShape(shapeKey);
            if (blendShape != null)
            {
                blendShape.weight = value;
            }
        }

        public float GetBlendShapeValue(string shapeKey)
        {
            var blendShape = GetBlendShape(shapeKey);
            if (blendShape != null)
            {
                return blendShape.weight;
            }
            return 0f;
        }

        public void FixBlendValues(IEnumerable<string> shapeKeys)
        {
            if (maid == null || maid.body0 == null)
            {
                return;
            }

            var morphs = new HashSet<TMorph>();

            foreach (var shapeKey in shapeKeys)
            {
                var blendShape = GetBlendShape(shapeKey);
                if (blendShape == null)
                {
                    continue;
                }
                foreach (var entity in blendShape.entities)
                {
                    morphs.Add(entity.morph);
                }
            }

            foreach (var morph in morphs)
            {
                if (morph != null)
                {
                    morph.FixBlendValues();
                }
            }
        }

        public MaidSlotStat GetSlotStat(TBody.SlotID slotId)
        {
            return slotStatMap.GetOrDefault(slotId);
        }

        public string GetBonePath(string boneName)
        {
            var bonePath = BoneUtils.ConvertToBonePath(boneName);
            if (!string.IsNullOrEmpty(bonePath))
            {
                return bonePath;
            }

            if (extendBoneCache != null)
            {
                var entity = extendBoneCache.GetEntity(boneName);
                if (entity != null)
                {
                    return entity.bonePath;
                }
            }
            return "";
        }

        public Transform GetBoneTransform(string boneName)
        {
            if (BoneUtils.IsDefaultBoneName(boneName))
            {
                var bonePath = BoneUtils.ConvertToBonePath(boneName);
                var boneData = cacheBoneData.GetBoneData(bonePath);
                if (boneData != null)
                {
                    return boneData.transform;
                }
                return null;
            }

            if (extendBoneCache != null)
            {
                var entity = extendBoneCache.GetEntity(boneName);
                if (entity != null)
                {
                    return entity.transform;
                }
            }

            return null;
        }

        public Vector3 GetInitialPosition(string boneName)
        {
            if (BoneUtils.IsDefaultBoneName(boneName))
            {
                var boneType = BoneUtils.GetBoneTypeByName(boneName);
                return BoneUtils.GetInitialPosition(boneType);
            }

            if (extendBoneCache != null)
            {
                var entity = extendBoneCache.GetEntity(boneName);
                if (entity != null)
                {
                    return entity.initialPosition;
                }
            }

            return Vector3.zero;
        }

        public Vector3 GetInitialEulerAngles(string boneName)
        {
            if (BoneUtils.IsDefaultBoneName(boneName))
            {
                var boneType = BoneUtils.GetBoneTypeByName(boneName);
                return BoneUtils.GetInitialEulerAngles(boneType);
            }

            if (extendBoneCache != null)
            {
                var entity = extendBoneCache.GetEntity(boneName);
                if (entity != null)
                {
                    return entity.initialRotation.eulerAngles;
                }
            }

            return Vector3.zero;
        }

        public bool IsYureSlot(string slotName)
        {
            return extendBoneCache != null && extendBoneCache.IsYureSlot(slotName);
        }

        // PartsEdit 連携は未移植のため、揺れ状態の取得・設定は無効化している
        public bool GetYureState(string slotName)
        {
            return false;
        }

        public void SetYureState(string slotName, bool state)
        {
        }

        public void UpdateMaterials()
        {
            materialMap.Clear();
            materialNames.Clear();

            foreach (var stat in slotStats)
            {
                foreach (var material in stat.materials)
                {
                    materialMap[material.name] = material;
                    materialNames.Add(material.name);
                }
            }
        }

        public ModelMaterial GetMaterial(string name)
        {
            ModelMaterial material;
            if (materialMap.TryGetValue(name, out material))
            {
                return material;
            }
            return null;
        }

        public Transform GetPointTransform(MaidPointType type)
        {
            if (maid == null || maid.body0 == null)
            {
                return null;
            }

            Transform result = null;
            switch (type)
            {
                case MaidPointType.Head:
                    result = maid.body0.trsHead;
                    break;
                case MaidPointType.Chest:
                    result = maid.body0.Spine1a;
                    break;
                case MaidPointType.Crotch:
                    result = maid.body0.Pelvis;
                    break;
                case MaidPointType.Hip:
                    result = maid.body0.Hip_R;
                    break;
                case MaidPointType.Bip01:
                    result = maid.body0.trBip;
                    break;
            }

            return result;
        }

        public static readonly List<string> MaidPointTypeNames = new List<string>
        {
            "顔", "胸", "股", "尻", "中心",
        };

        public static string GetMaidPointTypeName(MaidPointType type)
        {
            return MaidPointTypeNames[(int) type];
        }

        public Transform GetLookAtTarget()
        {
            var maid = this.maid;
            if (maid == null)
            {
                return null;
            }

            switch (lookAtTargetType)
            {
                case LookAtTargetType.Camera:
                    return PluginUtils.MainCamera.transform;
                case LookAtTargetType.Maid:
                {
                    var maidCache = maidManager.GetMaidCache(lookAtTargetIndex);
                    if (maidCache != null)
                    {
                        return maidCache.GetPointTransform(lookAtMaidPointType);
                    }
                    break;
                }
                case LookAtTargetType.Model:
                {
                    var model = modelManager.GetModel(lookAtTargetIndex);
                    if (model != null)
                    {
                        return model.transform;
                    }
                    break;
                }
            }
            return null;
        }

        public TBodySkin GetSlot(TBody.SlotID slotId)
        {
            return maid != null ? maid.body0.goSlot[(int)slotId] : null;
        }

        public TBodySkin GetSlot(DressSlotID slotId)
        {
            if (DressUtils.IsShiftSlotId(slotId))
            {
                return null;
            }
            var bodySlotId = DressUtils.GetBodySlotId(slotId);
            return GetSlot(bodySlotId);
        }

        public MaidSlotStat GetSlotStat(DressSlotID slotId)
        {
            if (DressUtils.IsShiftSlotId(slotId))
            {
                return null;
            }
            var bodySlotId = DressUtils.GetBodySlotId(slotId);
            return GetSlotStat(bodySlotId);
        }

        public void PlayOneShotVoice()
        {
            PlayVoice(
                oneShotVoiceName,
                oneShotVoiceStartTime,
                oneShotVoiceLength,
                false);
            UpdateVoice();
        }

        private string _playingVoiceName = "";
        private float _voiceStopTime = 0f;

        public void PlayVoice(
            string fileName,
            float startTime,
            float length,
            bool isLoop)
        {
            if (maid == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(fileName))
            {
                maid.AudioMan.Stop(voiceFadeTime);
                return;
            }

            var fixedFileName = EndsWith(fileName, ".ogg");
            maid.AudioMan.LoadPlay(fixedFileName, voiceFadeTime, false, isLoop);

            if (maid.AudioMan.audiosource != null)
            {
                maid.AudioMan.audiosource.pitch = voicePitch;

                if (startTime > 0f)
                {
                    maid.AudioMan.audiosource.time = startTime;
                }
            }

            _playingVoiceName = fixedFileName;
            _voiceStopTime = length > 0f ? startTime + length : 0f;
        }

        public void UpdateVoice()
        {
            if (maid == null)
            {
                return;
            }

            if (_voiceStopTime > 0f)
            {
                if (maid.AudioMan.FileName != _playingVoiceName)
                {
                    _voiceStopTime = 0f;
                }
                else if (maid.AudioMan.isPlay() && maid.AudioMan.audiosource.time >= _voiceStopTime)
                {
                    maid.AudioMan.Stop(voiceFadeTime);
                    _voiceStopTime = 0f;
                }
            }

            if (!string.IsNullOrEmpty(loopVoiceName))
            {
                if (!maid.AudioMan.isPlay())
                {
                    PlayVoice(loopVoiceName, 0f, 0f, true);
                }
            }
        }

        public static string EndsWith(string value, string extension)
        {
            if (!string.IsNullOrEmpty(value) && !value.EndsWith(extension, StringComparison.CurrentCultureIgnoreCase))
            {
                value += extension;
            }
            return value;
        }

        private void OnMaidChanged(Maid maid)
        {
            MTEUtils.LogDebug("Maid changed: " + (maid != null ? maid.name : "null"));

            Reset();

            this.maid = maid;
            if (maid == null)
            {
                return;
            }

            cacheBoneData = maid.gameObject.GetComponent<CacheBoneDataArray>();
            if (cacheBoneData == null)
            {
                cacheBoneData = maid.gameObject.AddComponent<CacheBoneDataArray>();
                cacheBoneData.CreateCache(maid.body0.GetBone("Bip01"));
            }
            ikManager = PoseEditWindow.GetMaidIKManager(maid);

            info = MaidInfo.GetOrCreate(maid, ikManager);

            extendBoneCache = maid.gameObject.GetComponent<ExtendBoneCache>();
            if (extendBoneCache == null)
            {
                var anmRoot = cacheBoneData.GetBoneData("Bip01").transform.parent;
                extendBoneCache = maid.gameObject.AddComponent<ExtendBoneCache>();
                extendBoneCache.Init(maid, anmRoot);
            }

            maidPropCache = maid.gameObject.GetComponent<MaidPropCache>();
            if (maidPropCache == null)
            {
                maidPropCache = maid.gameObject.AddComponent<MaidPropCache>();
                maidPropCache.Init(maid);
            }

            foreach (var pair in DressUtils.DressSlotJpNameMap)
            {
                var dressSlotId = pair.Key;
                var slotJpName = pair.Value;
                if (DressUtils.IsShiftSlotId(dressSlotId))
                {
                    continue;
                }

                var slotId = DressUtils.GetBodySlotId(dressSlotId);
                var slot = GetSlot(dressSlotId);
                if (slot == null)
                {
                    continue;
                }

                var stat = new MaidSlotStat(slot, slotJpName);

                slotStats.Add(stat);
                slotStatMap[slotId] = stat;
            }

            UpdateMaterials();

            onMaidChanged?.Invoke(slotNo, maid);
        }

        public AnimationLayerInfo GetAnimationLayerInfo(int layer)
        {
            return animationLayerInfos.GetOrDefault(layer);
        }

        public void ApplyAnimationLayerInfo(AnimationLayerInfo info, float t)
        {
            animationLayerInfos[info.layer] = info;

            if (animation == null)
            {
                return;
            }

            if (info.state == null || info.state.name != info.anmTag)
            {
                if (string.IsNullOrEmpty(info.anmName))
                {
                    if (info.state != null)
                    {
                        info.state.enabled = false;
                        info.state = null;
                    }
                }
                else if (GameUty.IsExistFile(info.anmName))
                {
                    info.state = maid.body0.CrossFadeLayer(
                        info.anmName,
                        GameUty.FileSystem,
                        info.layer,
                        false,
                        info.loop,
                        false,
                        0f,
                        info.weight);
                }
                else
                {
                    // マイポーズを検索
                    var path = MTEUtils.CombinePaths(PhotoModePoseSave.folder_path, info.anmName);
                    if (File.Exists(path))
                    {
                        info.state = maid.body0.CrossFadeLayerByFullPath(
                            path,
                            info.layer,
                            false,
                            info.loop,
                            false,
                            0f,
                            info.weight);
                    }
                }
            }

            if (info.state == null)
            {
                return;
            }

            info.state.wrapMode = info.loop ? WrapMode.Loop : WrapMode.Once;
            info.state.weight = info.weight;

            info.state.time = t;
            info.state.speed = 0f;
        }

        private void OnAnmChanged(string anmName)
        {
            MTEUtils.LogDebug("Animation changed: " + anmName);

            ResetAnm();

            this.annName = anmName;
            if (string.IsNullOrEmpty(annName))
            {
                return;
            }

            animationState = animation[annName.ToLower()];
            long.TryParse(annName, out anmId);

            onAnmChanged?.Invoke(slotNo, anmName);
        }
    }
}