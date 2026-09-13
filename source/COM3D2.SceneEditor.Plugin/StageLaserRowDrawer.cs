using COM3D2.MotionTimelineEditor;
using COM3D2.MotionTimelineEditor.Plugin;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// ステージレーザーのパラメータ行。コントローラー (一括設定) 単位とレーザー単位で内容が違う。
    /// LiveEffectWindow のレーザータブと TimelineItemInspector
    /// (ステージレーザーレイヤーの項目表示) で共有する。
    ///
    /// 書き込み先はレイヤーがキー化するのと同じ StageLaserManager の実体で、
    /// どのスナップショットにも含まれないため履歴は記録しない (ウィンドウ側も記録していない)。
    /// 色欄の編集状態を持つため、対象ごと・描画するビューごとにインスタンスを分ける
    /// </summary>
    public class StageLaserRowDrawer
    {
        /// <summary>カスタム値のラベル幅 (「ﾉｲｽﾞｻｲｽﾞ」等が収まる幅。KeyFrameInspector と揃える)</summary>
        private const float CustomLabelWidth = 100f;

        /// <summary>カスタム値のスライダー幅 (-1 でウィンドウ幅いっぱい。KeyFrameInspector と揃える)</summary>
        private const float CustomSliderWidth = -1f;

        /// <summary>Transform 行のラベル幅 (「最小位置」等が収まる幅)</summary>
        private const float StageLaserTransformLabelWidth = 60f;

        // ラベル (= カラーピッカーの同定キー) は対象ごとに変えるため、描画時に設定する
        private readonly ColorFieldCache _color1FieldCache = new ColorFieldCache("", true);
        private readonly ColorFieldCache _color2FieldCache = new ColorFieldCache("", true);


        /// <summary>色欄のラベルを対象ごとに一意にする (ColorPickerWindow はラベルで対象を識別する)</summary>
        private void SetColorLabels(string colorLabelPrefix)
        {
            _color1FieldCache.label = colorLabelPrefix + "/色1";
            _color2FieldCache.label = colorLabelPrefix + "/色2";
        }

        /// <summary>コントローラー (グループ一括設定) のパラメータ行</summary>
        public void DrawControllerRows(
            GUIView view, StageLaserController controller, string colorLabelPrefix)
        {
            SetColorLabels(colorLabelPrefix);

            var updateTransform = false;
            var defaultTrans = TransformDataStageLaserController.defaultTrans;

            {
                var initialPosition = defaultTrans.initialPosition;
                var transformCache = view.GetTransformCache(null);
                transformCache.position = controller.position;

                updateTransform |= TimelineLayerBase.DrawPosition(
                    view,
                    transformCache,
                    TimelineLayerBase.TransformEditType.全て,
                    initialPosition,
                    label: "位置",
                    labelWidth: StageLaserTransformLabelWidth);

                if (updateTransform)
                {
                    controller.position = transformCache.position;
                }
            }

            {
                var initialEulerAngles = defaultTrans.initialEulerAngles;
                var transformCache = view.GetTransformCache(null);
                transformCache.eulerAngles = controller.eulerAngles;

                updateTransform |= TimelineLayerBase.DrawEulerAngles(
                    view,
                    transformCache,
                    TimelineLayerBase.TransformEditType.全て,
                    TimelinePrevKeyUtils.GetPrevEulerAngles<StageLaserTimelineLayer>(controller.name, initialEulerAngles),
                    initialEulerAngles,
                    label: "角度",
                    labelWidth: StageLaserTransformLabelWidth);

                if (updateTransform)
                {
                    controller.eulerAngles = transformCache.eulerAngles;
                }
            }

            updateTransform |= view.DrawToggle("一括表示設定", controller.autoVisible, 200, 20, newValue =>
            {
                controller.autoVisible = newValue;
            });

            if (controller.autoVisible)
            {
                updateTransform |= view.DrawToggle("表示", controller.visible, 120, 20, newValue =>
                {
                    controller.visible = newValue;
                });
            }

            updateTransform |= view.DrawToggle("一括角度設定", controller.autoRotation, 200, 20, newValue =>
            {
                controller.autoRotation = newValue;
            });

            if (controller.autoRotation)
            {
                var initialEulerAngles = defaultTrans.initialRotationMin;
                var transformCache = view.GetTransformCache(null);
                transformCache.eulerAngles = controller.rotationMin;

                var prevBone = TimelinePrevKeyUtils.GetPrevBone<StageLaserTimelineLayer>(controller.name);
                var prevTransform = prevBone != null ? prevBone.transform as TransformDataStageLaserController : null;
                var prevAngles = prevTransform != null ? prevTransform.rotationMin : initialEulerAngles;

                updateTransform |= TimelineLayerBase.DrawEulerAngles(
                    view,
                    transformCache,
                    TimelineLayerBase.TransformEditType.全て,
                    prevAngles,
                    initialEulerAngles,
                    label: "最小角度",
                    labelWidth: StageLaserTransformLabelWidth);

                if (updateTransform)
                {
                    controller.rotationMin = transformCache.eulerAngles;
                }

                initialEulerAngles = defaultTrans.initialRotationMax;
                transformCache = view.GetTransformCache(null);
                transformCache.eulerAngles = controller.rotationMax;

                prevAngles = prevTransform != null ? prevTransform.rotationMax : initialEulerAngles;

                updateTransform |= TimelineLayerBase.DrawEulerAngles(
                    view,
                    transformCache,
                    TimelineLayerBase.TransformEditType.全て,
                    prevAngles,
                    initialEulerAngles,
                    label: "最大角度",
                    labelWidth: StageLaserTransformLabelWidth);

                if (updateTransform)
                {
                    controller.rotationMax = transformCache.eulerAngles;
                }
            }

            updateTransform |= view.DrawToggle("一括色設定", controller.autoColor, 200, 20, newValue =>
            {
                controller.autoColor = newValue;
            });

            if (controller.autoColor)
            {
                view.DrawLabel("中心色", 200, 20);

                updateTransform |= view.DrawColor(
                    _color1FieldCache,
                    controller.color1,
                    defaultTrans.GetDefaultColorValue(TransformDataBase.ColorKey.Main),
                    c => controller.color1 = c);

                view.DrawLabel("錯乱色", 200, 20);

                updateTransform |= view.DrawColor(
                    _color2FieldCache,
                    controller.color2,
                    defaultTrans.GetDefaultColorValue(TransformDataBase.ColorKey.Sub),
                    c => controller.color2 = c);
            }

            updateTransform |= view.DrawToggle("一括レーザー情報設定", controller.autoLaserInfo, 200, 20, newValue =>
            {
                controller.autoLaserInfo = newValue;
            });

            if (controller.autoLaserInfo)
            {
                var laserInfo = controller.laserInfo;

                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.intensityInfo,
                    laserInfo.intensity,
                    x => laserInfo.intensity = x,
                    labelWidth: CustomLabelWidth,
                    sliderWidth: CustomSliderWidth);

                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.laserRangeInfo,
                    laserInfo.laserRange,
                    x => laserInfo.laserRange = x,
                    labelWidth: CustomLabelWidth,
                    sliderWidth: CustomSliderWidth);

                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.laserWidthInfo,
                    laserInfo.laserWidth,
                    x => laserInfo.laserWidth = x,
                    labelWidth: CustomLabelWidth,
                    sliderWidth: CustomSliderWidth);

                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.falloffExpInfo,
                    laserInfo.falloffExp,
                    x => laserInfo.falloffExp = x,
                    labelWidth: CustomLabelWidth,
                    sliderWidth: CustomSliderWidth);

                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.noiseStrengthInfo,
                    laserInfo.noiseStrength,
                    x => laserInfo.noiseStrength = x,
                    labelWidth: CustomLabelWidth,
                    sliderWidth: CustomSliderWidth);

                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.noiseScaleInfo,
                    laserInfo.noiseScale,
                    x => laserInfo.noiseScale = x,
                    labelWidth: CustomLabelWidth,
                    sliderWidth: CustomSliderWidth);
                
                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.coreRadiusInfo,
                    laserInfo.coreRadius,
                    x => laserInfo.coreRadius = x,
                    labelWidth: CustomLabelWidth,
                    sliderWidth: CustomSliderWidth);
                
                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.offsetRangeInfo,
                    laserInfo.offsetRange,
                    x => laserInfo.offsetRange = x,
                    labelWidth: CustomLabelWidth,
                    sliderWidth: CustomSliderWidth);
                
                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.glowWidthInfo,
                    laserInfo.glowWidth,
                    x => laserInfo.glowWidth = x,
                    labelWidth: CustomLabelWidth,
                    sliderWidth: CustomSliderWidth);
                
                updateTransform |= view.DrawCustomValueInt(
                    defaultTrans.segmentRangeInfo,
                    laserInfo.segmentRange,
                    x => laserInfo.segmentRange = x,
                    labelWidth: CustomLabelWidth,
                    sliderWidth: CustomSliderWidth);

                updateTransform |= view.DrawCustomValueBool(
                    defaultTrans.zTestInfo,
                    laserInfo.zTest,
                    x => laserInfo.zTest = x);
            }

            if (updateTransform)
            {
                controller.UpdateLasers();
            }
        }

        /// <summary>レーザー 1 本のパラメータ行</summary>
        public void DrawLaserRows(
            GUIView view, StageLaserController controller, StageLaser laser,
            string colorLabelPrefix)
        {
            SetColorLabels(colorLabelPrefix);

            view.DrawLabel(laser.displayName, 200, 20);

            if (!controller.autoVisible)
            {
                view.DrawToggle("表示", laser.visible, 120, 20, newValue =>
                {
                    laser.visible = newValue;
                });
            }

            var transformCache = view.GetTransformCache();
            var defaultTrans = TransformDataStageLaser.defaultTrans;
            transformCache.eulerAngles = laser.eulerAngles;
            var initialPosition = defaultTrans.initialPosition;
            var initialEulerAngles = defaultTrans.initialEulerAngles;
            var initialScale = Vector3.one;
            var updateTransform = false;
            var editType = TimelineLayerBase.TransformEditType.全て;

            if (!controller.autoRotation)
            {
                updateTransform |= TimelineLayerBase.DrawEulerAngles(view, transformCache, editType,
                    TimelinePrevKeyUtils.GetPrevEulerAngles<StageLaserTimelineLayer>(laser.name, initialEulerAngles), initialEulerAngles);
            }

            if (updateTransform)
            {
                laser.eulerAngles = transformCache.eulerAngles;
            }

            if (!controller.autoColor)
            {
                view.DrawLabel("中心色", 200, 20);

                updateTransform |= view.DrawColor(
                    _color1FieldCache,
                    laser.color1,
                    Color.white,
                    c => laser.color1 = c);

                view.DrawLabel("錯乱色", 200, 20);

                updateTransform |= view.DrawColor(
                    _color2FieldCache,
                    laser.color2,
                    Color.white,
                    c => laser.color2 = c);
            }

            if (!controller.autoLaserInfo)
            {
                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.intensityInfo,
                    laser.intensity,
                    x => laser.intensity = x,
                    labelWidth: CustomLabelWidth,
                    sliderWidth: CustomSliderWidth);

                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.laserRangeInfo,
                    laser.laserRange,
                    x => laser.laserRange = x,
                    labelWidth: CustomLabelWidth,
                    sliderWidth: CustomSliderWidth);
                
                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.laserWidthInfo,
                    laser.laserWidth,
                    x => laser.laserWidth = x,
                    labelWidth: CustomLabelWidth,
                    sliderWidth: CustomSliderWidth);

                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.falloffExpInfo,
                    laser.falloffExp,
                    x => laser.falloffExp = x,
                    labelWidth: CustomLabelWidth,
                    sliderWidth: CustomSliderWidth);

                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.noiseStrengthInfo,
                    laser.noiseStrength,
                    x => laser.noiseStrength = x,
                    labelWidth: CustomLabelWidth,
                    sliderWidth: CustomSliderWidth);

                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.noiseScaleInfo,
                    laser.noiseScale,
                    x => laser.noiseScale = x,
                    labelWidth: CustomLabelWidth,
                    sliderWidth: CustomSliderWidth);
                
                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.coreRadiusInfo,
                    laser.coreRadius,
                    x => laser.coreRadius = x,
                    labelWidth: CustomLabelWidth,
                    sliderWidth: CustomSliderWidth);
                
                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.offsetRangeInfo,
                    laser.offsetRange,
                    x => laser.offsetRange = x,
                    labelWidth: CustomLabelWidth,
                    sliderWidth: CustomSliderWidth);
                
                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.glowWidthInfo,
                    laser.glowWidth,
                    x => laser.glowWidth = x,
                    labelWidth: CustomLabelWidth,
                    sliderWidth: CustomSliderWidth);
                
                updateTransform |= view.DrawCustomValueInt(
                    defaultTrans.segmentRangeInfo,
                    laser.segmentRange,
                    x => laser.segmentRange = x,
                    labelWidth: CustomLabelWidth,
                    sliderWidth: CustomSliderWidth);

                updateTransform |= view.DrawCustomValueBool(
                    defaultTrans.zTestInfo,
                    laser.zTest,
                    x => laser.zTest = x);
            }
        }
    }
}
