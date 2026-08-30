using System;
using System.Collections.Generic;
using System.Xml.Linq;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    using SE = SceneEditor.Plugin;

    /// <summary>
    /// PNG 配置のタイムラインレイヤー。
    /// MTE_PngPlacement 版をベースに、外部 PngPlacement.dll ラッパーではなく
    /// SE ネイティブの PngPlacementManager へ接続するアダプタ版 (LightTimelineLayer と同方針)。
    /// SE に対応機能が無い値 (Inversion / StopRotation / FixCamera / Attach / APng 系等) は
    /// XML には保持するが適用しない。billboard は per-frame 値でないため対象外
    /// </summary>
    [TimelineLayerDesc("PNG配置", 35)]
    public class PngPlacementTimelineLayer : TimelineLayerBase
    {
        public override Type layerType => typeof(PngPlacementTimelineLayer);
        public override string layerName => nameof(PngPlacementTimelineLayer);

        public override List<string> allBoneNames => pngTimelineManager.pngObjectNames;

        private static PngObjectTimelineManager pngTimelineManager => PngObjectTimelineManager.instance;
        private static SE.PngPlacementManager sePngManager => SE.PngPlacementManager.instance;

        private PngPlacementTimelineLayer(int slotNo) : base(slotNo)
        {
        }

        public static PngPlacementTimelineLayer Create(int slotNo)
        {
            return new PngPlacementTimelineLayer(0);
        }

        public override void Init()
        {
            base.Init();

            PngObjectTimelineManager.onObjectAdded += OnPngObjectAdded;
            PngObjectTimelineManager.onObjectRemoved += OnPngObjectRemoved;
        }

        protected override void InitMenuItems()
        {
            allMenuItems.Clear();

            foreach (var pngObject in pngTimelineManager.pngObjects)
            {
                var menuItem = new BoneMenuItem(pngObject.name, pngObject.name);
                allMenuItems.Add(menuItem);
            }
        }

        public override void Dispose()
        {
            base.Dispose();

            PngObjectTimelineManager.onObjectAdded -= OnPngObjectAdded;
            PngObjectTimelineManager.onObjectRemoved -= OnPngObjectRemoved;
        }

        public override bool IsValidData()
        {
            errorMessage = "";
            return true;
        }

        public override void LateUpdate()
        {
            base.LateUpdate();

            if (!studioHackManager.isPoseEditing)
            {
                ApplyPlayData();
            }
        }

        protected override void ApplyMotion(MotionData motion, float t, bool indexUpdated, MotionPlayData playData)
        {
            var pngObject = pngTimelineManager.GetPngObject(motion.name);
            if (pngObject == null || pngObject.transform == null)
            {
                return;
            }

            if (indexUpdated)
            {
                ApplyPngObjectInit(motion, t, pngObject);
            }

            ApplyPngObjectUpdate(motion, t, pngObject);
        }

        private void ApplyPngObjectInit(MotionData motion, float t, TimelinePngObjectEntry pngObject)
        {
            var transform = pngObject.transform;
            var start = motion.start as TransformDataPngObject;
            var data = pngObject.data;

            sePngManager.SetVisible(data, start.visible);
            // MTE の brightness は 0-255 の byte、SE は 1.0 基準の倍率のため換算する
            sePngManager.SetColor(data, start.color, start.brightness / 255f);

            transform.localPosition = start.position;
            transform.localEulerAngles = start.eulerAngles;
            ApplyScale(pngObject, start.scalex, start.scalemag, start.scalez);
        }

        private void ApplyPngObjectUpdate(MotionData motion, float t, TimelinePngObjectEntry pngObject)
        {
            var transform = pngObject.transform;
            var start = motion.start as TransformDataPngObject;
            var end = motion.end as TransformDataPngObject;
            var data = pngObject.data;

            var t0 = motion.stFrame * timeline.frameDuration;
            var t1 = motion.edFrame * timeline.frameDuration;

            if (start.position != end.position)
            {
                transform.localPosition = PluginUtils.HermiteVector3(
                    t0, t1, start.positionValues, end.positionValues, t);
            }

            if (start.eulerAngles != end.eulerAngles)
            {
                transform.localEulerAngles = PluginUtils.HermiteVector3(
                    t0, t1, start.eulerAnglesValues, end.eulerAnglesValues, t);
            }

            if (start.scalex != end.scalex || start.scalez != end.scalez)
            {
                var scaleX = PluginUtils.HermiteValue(
                    t0, t1, start.scalexValue, end.scalexValue, t);
                var scaleZ = PluginUtils.HermiteValue(
                    t0, t1, start.scalezValue, end.scalezValue, t);
                ApplyScale(pngObject, scaleX, start.scalemag, scaleZ);
            }

            if (start.color != end.color || start.brightness != end.brightness)
            {
                var color = Color.Lerp(start.color, end.color, t);
                var brightness = Mathf.Lerp(start.brightness, end.brightness, t);
                sePngManager.SetColor(data, color, brightness / 255f);
            }
        }

        // MTE の scalex (基準スケール) × scalemag (倍率) を root のスケールに適用する。
        // アスペクト補正は SE の quadObject 側が担うため Y は X と同値でよい。
        // scalez は板ポリの厚み方向で SE では実質無効だが、値として反映しておく
        private static void ApplyScale(TimelinePngObjectEntry pngObject, float scaleX, int scaleMag, float scaleZ)
        {
            var mag = scaleMag > 0 ? scaleMag : 1;
            var x = scaleX * mag;
            pngObject.transform.localScale = new Vector3(x, x, scaleZ > 0f ? scaleZ : 1f);
        }

        public void OnPngObjectAdded(TimelinePngObjectEntry pngObject)
        {
            InitMenuItems();
            AddFirstBones(new List<string> { pngObject.name });
            ApplyCurrentFrame(true);
        }

        public void OnPngObjectRemoved(TimelinePngObjectEntry pngObject)
        {
            InitMenuItems();
            RemoveAllBones(new List<string> { pngObject.name });
            ApplyCurrentFrame(true);
        }

        public override void UpdateFrame(FrameData frame, bool initialEdit, bool force)
        {
            foreach (var pngObject in pngTimelineManager.pngObjects)
            {
                var data = pngObject.data;
                if (data == null || pngObject.transform == null)
                {
                    continue;
                }

                var trans = frame.GetOrCreateTransformData<TransformDataPngObject>(pngObject.name);
                trans.visible = data.visible;
                trans.position = pngObject.transform.localPosition;
                trans.eulerAngles = pngObject.transform.localEulerAngles;
                trans.color = data.color;
                trans.brightness = (byte) Mathf.Clamp(data.brightness * 255f, 0f, 255f);
                trans.scalex = pngObject.transform.localScale.x;
                trans.scalemag = 1;
                trans.scalez = pngObject.transform.localScale.z;
            }
        }

        // DCM 連携は未移植のため出力しない
        public override void OutputDCM(XElement songElement)
        {
        }

        public override TransformType GetTransformType(string name)
        {
            return TransformType.PngObject;
        }
    }
}
