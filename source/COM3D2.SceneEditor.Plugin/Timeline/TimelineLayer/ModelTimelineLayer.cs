using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    using AttachPoint = PhotoTransTargetObject.AttachPoint;

    [TimelineLayerDesc("モデル", 21, TimelineLayerCategory.Model, CanRestoreOnRemove = false)]
    public class ModelTimelineLayer : ModelTimelineLayerBase
    {
        public override Type layerType => typeof(ModelTimelineLayer);
        public override string layerName => nameof(ModelTimelineLayer);

        public override List<string> allBoneNames => modelManager.modelNames;

        private ModelTimelineLayer(int slotNo) : base(slotNo)
        {
        }

        public static ModelTimelineLayer Create(int slotNo)
        {
            return new ModelTimelineLayer(0);
        }

        public override void Init()
        {
            base.Init();

            StudioModelManager.onModelAdded += OnModelAdded;
            StudioModelManager.onModelRemoved += OnModelRemoved;
        }

        protected override void InitMenuItems()
        {
            allMenuItems.Clear();

            foreach (var model in modelManager.models)
            {
                var menuItem = new BoneMenuItem(model.name, model.displayName);
                allMenuItems.Add(menuItem);
            }
        }

        public override void ResetOnRemove()
        {
            timeline.models.Clear();
            modelManager.SetupModels(timeline.models);
        }

        public override void Dispose()
        {
            base.Dispose();

            StudioModelManager.onModelAdded -= OnModelAdded;
            StudioModelManager.onModelRemoved -= OnModelRemoved;
        }

        public override bool IsValidData()
        {
            errorMessage = "";
            return true;
        }

        public override void Update()
        {
            base.Update();
        }

        public override void LateUpdate()
        {
            base.LateUpdate();

            if (!SceneEditorHack.isPoseEditing)
            {
                ApplyPlayData();
            }
        }

        // 毎フレーム使うので作業用のコレクションとデリゲートを使い回す
        private readonly Dictionary<string, bool> _indexUpdatedMap = new Dictionary<string, bool>();
        private readonly List<string> _playNames = new List<string>();
        private readonly List<string> _applyOrder = new List<string>();
        private Func<string, string> _getPlayingParentName;
        private Func<string, string> _getWorldLerpEndParentName;
        private Func<string, bool> _hasPlayData;

        /// <summary>
        /// 親モデルを子より先に適用する。ワールド補間は始点・終点の親の今フレームの姿勢を読むため、
        /// 辞書順のままだと子が 1 フレーム前の親を基準にしてしまう
        /// </summary>
        protected override void ApplyPlayData()
        {
            var maid = this.maid;
            if (maid == null || maid.body0 == null || !maid.body0.isLoadedBody)
            {
                return;
            }

            var playingFrameNoFloat = this.playingFrameNoFloat;

            _indexUpdatedMap.Clear();
            _playNames.Clear();
            foreach (var pair in _playDataMap)
            {
                _indexUpdatedMap[pair.Key] = pair.Value.Update(playingFrameNoFloat);
                _playNames.Add(pair.Key);
            }

            if (_getPlayingParentName == null)
            {
                _getPlayingParentName = GetPlayingParentName;
                _getWorldLerpEndParentName = GetWorldLerpEndParentName;
                _hasPlayData = _playDataMap.ContainsKey;
            }
            ModelAttachTarget.SortParentsFirst(
                _playNames, _getPlayingParentName, _getWorldLerpEndParentName, _applyOrder);

            foreach (var name in _applyOrder)
            {
                var playData = _playDataMap[name];
                var current = playData.current;
                if (current != null)
                {
                    ApplyMotion(current, playData.lerpFrame, _indexUpdatedMap[name], playData);
                }
            }
        }

        /// <summary>再生位置で有効なキーのアタッチ先モデル名 (ApplyMotion と同じく区間の 99% までは始点)</summary>
        private string GetPlayingParentName(string name)
        {
            MotionPlayData playData;
            if (!_playDataMap.TryGetValue(name, out playData) || playData.current == null)
            {
                return null;
            }

            var key = playData.lerpFrame < StepEndThreshold ? playData.current.start : playData.current.end;
            return GetKeyParentName(key as TransformDataModel);
        }

        /// <summary>ワールド補間の区間なら終点キーのアタッチ先モデル名 (ApplyMotionWorldLerp が終点の親も読むため)</summary>
        private string GetWorldLerpEndParentName(string name)
        {
            MotionPlayData playData;
            if (!_playDataMap.TryGetValue(name, out playData) || playData.current == null)
            {
                return null;
            }

            var end = playData.current.end as TransformDataModel;
            return end != null && end.worldLerp ? GetKeyParentName(end) : null;
        }

        private string GetKeyParentName(TransformDataModel key)
        {
            if (key == null || !key.isAttachedToModel)
            {
                return null;
            }
            string parentName, boneName;
            return ModelAttachTarget.TryResolveReference(
                key.attachModelName, _hasPlayData, out parentName, out boneName) ? parentName : null;
        }

        protected override void ApplyMotion(MotionData motion, float t, bool indexUpdated, MotionPlayData playData)
        {
            var model = modelManager.GetModel(motion.name);
            if (model == null || model.transform == null)
            {
                return;
            }

            var start = motion.start as TransformDataModel;
            var end = motion.end as TransformDataModel;
            if (start == null || end == null)
            {
                return;
            }

            var attachKey = t < StepEndThreshold ? start : end;
            var attachChanged = modelManager.ApplyAttach(
                model, attachKey.attachPoint, attachKey.attachMaidSlotNo, attachKey.attachModelName);

            // 付け替えはローカル位置・回転・拡縮を変える (メイドへは 0 に戻し、モデルへはワールド姿勢を保つ) ので、区間頭と同じく入れ直す
            if (indexUpdated || attachChanged)
            {
                ApplyMotionInit(motion, model);
            }

            // 同値区間は ApplyMotionInit が入れた start の値のままでよい (MotionData.isConstant)
            if (motion.isConstant)
            {
                return;
            }

            // 終点側へ付け替えた後もワールド座標で書くので、区間の最後まで続けても親に依らず連続する
            if (end.worldLerp)
            {
                if (ApplyMotionWorldLerp(motion, t, model, start, end))
                {
                    return;
                }
            }

            ApplyMotionUpdateTangent(motion, t, model);
        }

        private void ApplyMotionInit(MotionData motion, StudioModelStat model)
        {
            var transform = model.transform;
            var start = motion.start;

            transform.localPosition = start.position;
            transform.localRotation = start.rotation;
            transform.localScale = start.scale;

            modelManager.SetModelVisible(model, start.visible && modelManager.Visible);
            model.visible = start.visible;
        }

        private void ApplyMotionUpdateTangent(MotionData motion, float t, StudioModelStat model)
        {
            var transform = model.transform;
            var start = motion.start;
            var end = motion.end;
            var t0 = motion.stFrame * timeline.frameDuration;
            var t1 = motion.edFrame * timeline.frameDuration;

            transform.localPosition = PluginUtils.HermiteVector3(
                t0,
                t1,
                start.positionValues,
                end.positionValues,
                t);

            transform.localRotation = PluginUtils.HermiteQuaternion(
                t0,
                t1,
                start.rotationValues,
                end.rotationValues,
                t);

            transform.localScale = PluginUtils.HermiteVector3(
                t0,
                t1,
                start.scaleValues,
                end.scaleValues,
                t);
        }

        /// <summary>
        /// 始点キー (始点の親基準) と終点キー (そのフレームの終点の親基準) のワールド姿勢を線形補間する。
        /// タンジェントはローカル座標系の傾きなので使わない。親が解決できなければ false (ローカル補間へ戻す)
        /// </summary>
        private bool ApplyMotionWorldLerp(
            MotionData motion,
            float t,
            StudioModelStat model,
            TransformDataModel start,
            TransformDataModel end)
        {
            var startParent = modelManager.GetAttachParent(
                model, start.attachPoint, start.attachMaidSlotNo, start.attachModelName);
            var endParent = modelManager.GetAttachParent(
                model, end.attachPoint, end.attachMaidSlotNo, end.attachModelName);
            if (startParent == null || endParent == null)
            {
                return false;
            }

            var transform = model.transform;
            transform.position = Vector3.Lerp(
                startParent.TransformPoint(start.position),
                endParent.TransformPoint(end.position),
                t);
            transform.rotation = Quaternion.Slerp(
                startParent.rotation * start.rotation,
                endParent.rotation * end.rotation,
                t);

            var t0 = motion.stFrame * timeline.frameDuration;
            var t1 = motion.edFrame * timeline.frameDuration;
            transform.localScale = PluginUtils.HermiteVector3(
                t0,
                t1,
                start.scaleValues,
                end.scaleValues,
                t);
            return true;
        }

        public void OnModelAdded(StudioModelStat model)
        {
            InitMenuItems();
            AddFirstBones(new List<string> { model.name });
            ApplyCurrentFrame(true);
        }

        public void OnModelRemoved(StudioModelStat model)
        {
            InitMenuItems();
            DetachKeysFromRemovedParent(model.name);
            RemoveAllBones(new List<string> { model.name });
            ApplyCurrentFrame(true);
        }

        /// <summary>
        /// 削除された親を指すキーを、親の最後の姿勢で配置ルート基準の値へ直してアタッチなしにする (ワールド位置を保つ)。
        /// 親が動いていた場合も削除時点の姿勢を全キーに使う近似。タンジェントは変換しない
        /// </summary>
        private void DetachKeysFromRemovedParent(string parentName)
        {
            ModelAttachPose parentPose;
            var hasPose = modelManager.TryGetAttachParentPose(parentName, out parentPose);

            foreach (var keyFrame in keyFrames)
            {
                foreach (var bone in keyFrame.bones)
                {
                    var trans = bone.transform as TransformDataModel;
                    if (trans == null || !trans.isAttachedToModel)
                    {
                        continue;
                    }

                    string referencedName, boneName;
                    ModelAttachTarget.TryResolveReference(
                        trans.attachModelName, name => name == parentName, out referencedName, out boneName);
                    if (referencedName != parentName)
                    {
                        continue;
                    }

                    var child = modelManager.GetModel(bone.name);
                    var root = child != null
                        ? modelManager.GetAttachParent(child, AttachPoint.Null, -1, "")
                        : null;
                    if (hasPose && root != null)
                    {
                        var position = trans.position;
                        var rotation = trans.rotation;
                        var scale = trans.scale;
                        ModelAttachTarget.ConvertToRoot(
                            parentPose, ModelAttachPose.From(root), ref position, ref rotation, ref scale);
                        trans.position = position;
                        trans.rotation = rotation;
                        trans.scale = scale;
                    }
                    else if (!hasPose)
                    {
                        MTEUtils.LogWarning(
                            "削除されたアタッチ先の姿勢が分からないため、位置を変換せずアタッチなしにします: {0}", bone.name);
                    }
                    else
                    {
                        MTEUtils.LogWarning(
                            "配置ルートが見つからないため、位置を変換せずアタッチなしにします: {0}", bone.name);
                    }
                    trans.SetUnattached();
                }
            }

            modelManager.ForgetAttachParentPose(parentName);
        }

        public override void OnCopyModel(StudioModelStat sourceModel, StudioModelStat newModel)
        {
            var sourceModelName = sourceModel.name;
            var newModelName = newModel.name;
            foreach (var keyFrame in keyFrames)
            {
                var sourceBone = keyFrame.GetBone(sourceModelName);
                if (sourceBone == null)
                {
                    continue;
                }

                var newBone = keyFrame.GetOrCreateBone(sourceBone.transform.type, newModelName);
                newBone.transform.FromTransformData(sourceBone.transform);
            }
        }

        public override void UpdateFrame(FrameData frame, bool initialEdit, bool force)
        {
            // 呼び出し元は一時フレームを渡すことが多いので、引き継ぎ元は登録済みキーから引く
            var existingFrame = GetFrame(frame.frameNo);

            foreach (var model in modelManager.models)
            {
                var modelName = model.name;

                var trans = CreateTransformData<TransformDataModel>(modelName);
                trans.position = model.transform.localPosition;
                trans.rotation = model.transform.localRotation;
                trans.scale = model.transform.localScale;
                trans.visible = model.visible;
                if (model.isAttachedToModel)
                {
                    trans.SetAttachedToModel(model.attachModelName);
                }
                else if (TransformDataModel.IsAttached(model.attachPoint, model.attachMaidSlotNo))
                {
                    trans.attachMaidSlotNo = model.attachMaidSlotNo;
                    trans.attachPoint = model.attachPoint;
                }
                else
                {
                    trans.SetUnattached();
                }

                var existingBone = existingFrame != null ? existingFrame.GetBone(modelName) : null;
                trans.InheritKeySettings(existingBone != null ? existingBone.transform as TransformDataModel : null);

                var bone = frame.CreateBone(trans);
                frame.UpdateBone(bone);
            }
        }

        // DCM 連携は未移植のため出力しない
        public override void OutputDCM(XElement songElement)
        {
        }

        public override TransformType GetTransformType(string name)
        {
            return TransformType.Model;
        }
    }
}