using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    public class FrameData
    {
        public ITimelineLayer parentLayer { get; set; }

        public int frameNo { get; set; }

        public Dictionary<string, BoneData>.ValueCollection bones
        {
            get => _boneMap.Values;
        }

        public Dictionary<string, BoneData>.KeyCollection boneNames
        {
            get => _boneMap.Keys;
        }

        private Dictionary<string, BoneData> _boneMap = null;

        public bool isFullBone
        {
            get => _boneMap.Count == parentLayer.allBoneNames.Count;
        }

        private static TimelineManager timelineManager => TimelineManager.instance;

        public FrameData(ITimelineLayer parentLayer)
        {
            this.parentLayer = parentLayer;
            _boneMap = new Dictionary<string, BoneData>(BoneUtils.saveBoneNames.Count);
        }

        public FrameData(ITimelineLayer parentLayer, int frameNo) : this(parentLayer)
        {
            this.frameNo = frameNo;
        }

        public BoneData CreateBone(ITransformData transform)
        {
            return new BoneData(this, transform);
        }

        public BoneData CreateBone(BoneXml xml)
        {
            var bone = new BoneData(this);
            bone.FromXml(xml);
            return bone;
        }

        public T GetOrCreateTransformData<T>(string name)
            where T : class, ITransformData, new()
        {
            var bone = GetBone(name);
            if (bone != null)
            {
                return bone.transform as T;
            }

            var trans = TimelineManager.CreateTransform<T>(name);
            bone = CreateBone(trans);
            _boneMap[name] = bone;
            return trans;
        }

        public BoneData GetBone(string name)
        {
            BoneData bone;
            if (_boneMap.TryGetValue(name, out bone))
            {
                return bone;
            }
            return null;
        }

        public bool HasBone(BoneData bone)
        {
            return bone != null && _boneMap.ContainsKey(bone.name);
        }

        public bool HasAnyBones(IEnumerable<BoneData> bones)
        {
            return bones.Any(HasBone);
        }

        public BoneData GetOrCreateBone(TransformType transformType, string name)
        {
            BoneData bone;
            if (!_boneMap.TryGetValue(name, out bone))
            {
                var trans = timelineManager.CreateTransform(transformType, name);
                bone = CreateBone(trans);
                _boneMap[name] = bone;
            }
            return bone;
        }

        public void SetBone(BoneData bone)
        {
            if (bone == null)
            {
                return;
            }

            var name = bone.name;
            bone.parentFrame = this;
            _boneMap[name] = bone;
        }

        public void SetBones(IEnumerable<BoneData> bones)
        {
            foreach (var bone in bones)
            {
                SetBone(bone);
            }
        }

        public void UpdateBone(BoneData bone)
        {
            if (bone == null)
            {
                return;
            }

            var targetBone = GetOrCreateBone(bone.transform.type, bone.name);
            targetBone.transform.FromTransformData(bone.transform);
        }

        public void UpdateBones(IEnumerable<BoneData> bones)
        {
            foreach (var bone in bones)
            {
                UpdateBone(bone);
            }
        }

        public void RemoveBone(BoneData bone)
        {
            if (bone != null)
            {
                _boneMap.Remove(bone.name);
                bone.parentFrame = null;
            }
        }

        public void RemoveBone(string boneName)
        {
            if (_boneMap.TryGetValue(boneName, out var bone))
            {
                RemoveBone(bone);
            }
        }

        public void RemoveBones(IEnumerable<BoneData> bones)
        {
            foreach (var bone in bones)
            {
                RemoveBone(bone);
            }
        }

        public void ClearBones()
        {
            _boneMap.Clear();
        }

        public BoneData FindBone(Func<BoneData, bool> match)
        {
            return bones.FirstOrDefault(match);
        }

        public bool HasBones()
        {
            return _boneMap.Count > 0;
        }

        public List<BoneData> GetDiffBones(FrameData sourceFrame)
        {
            var diffBones = new List<BoneData>(_boneMap.Count);
            foreach (var pair in _boneMap)
            {
                var name = pair.Key;
                var bone = pair.Value;

                if (bone.transform.isHidden)
                {
                    continue;
                }

                var sourceBone = sourceFrame.GetBone(name);
                if (sourceBone == null || !bone.transform.Equals(sourceBone.transform))
                {
                    diffBones.Add(bone);
                }
            }
            return diffBones;
        }

        public List<BoneData> GetFilterBones(IEnumerable<string> boneNames)
        {
            var diffBones = new List<BoneData>(_boneMap.Count);
            var boneNamesSet = new HashSet<string>(boneNames);
            foreach (var pair in _boneMap)
            {
                var name = pair.Key;
                var bone = pair.Value;

                if (bone.transform.isHidden)
                {
                    continue;
                }

                if (boneNamesSet.Contains(name))
                {
                    diffBones.Add(bone);
                }
            }
            return diffBones;
        }

        public void Flip()
        {
            var newBones = new List<BoneData>(_boneMap.Count);

            foreach (var bone in bones)
            {
                var boneType = bone.boneType;
                if (PoseFlipUtils.IsNotFlipType(boneType))
                {
                    newBones.Add(bone);
                    continue;
                }

                var transform = bone.transform;
                boneType = PoseFlipUtils.GetFlippedBoneType(boneType);

                var newTransform = timelineManager.CreateTransform(
                    transform.type, BoneUtils.GetBoneName(boneType));

                // 例外規則を持たないボーンは、オイラー角を経由せずクォータニオンの鏡像で反転する
                // （オイラー規則との等価性と、そうする理由は PoseFlipUtils.FlipRotation を参照）
                if (transform.hasRotation && !PoseFlipUtils.HasEulerFlipRule(boneType))
                {
                    newTransform.rotation = PoseFlipUtils.FlipRotation(transform.rotation);
                }
                else
                {
                    var eulerAngles = transform.eulerAngles;
                    var newEulerAngles = PoseFlipUtils.FlipEulerAngles(boneType, eulerAngles);
                    MTEUtils.LogDebug("Flip Bone：" + boneType + " " + eulerAngles + " -> " + newEulerAngles);
                    newTransform.eulerAngles = newEulerAngles;
                }

                if (boneType == IKManager.BoneType.Root)
                {
                    var localPosition = transform.position;
                    localPosition.x = -localPosition.x;
                    newTransform.position = localPosition;
                }

                newBones.Add(CreateBone(newTransform));
            }

            ClearBones();
            SetBones(newBones);
        }

        public void FromFrameData(FrameData sourceFrame)
        {
            ClearBones();

            foreach (var bone in sourceFrame.bones)
            {
                var newBone = GetOrCreateBone(bone.transform.type, bone.transform.name);
                newBone.transform.FromTransformData(bone.transform);
                SetBone(newBone);
            }
        }

        public void FromXml(FrameXml xml)
        {
            frameNo = xml.frameNo;

            ClearBones();
            foreach (var boneXml in xml.bones)
            {
                var bone = CreateBone(boneXml);
                SetBone(bone);
            }
        }

        public FrameXml ToXml()
        {
            var xml = new FrameXml();
            xml.frameNo = frameNo;
            xml.bones = new List<BoneXml>(bones.Count);
            foreach (var bone in bones)
            {
                xml.bones.Add(bone.ToXml());
            }
            return xml;
        }
    }
}