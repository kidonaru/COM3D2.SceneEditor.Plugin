using COM3D2.MotionTimelineEditor;
using COM3D2.MotionTimelineEditor.Plugin;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// ステージライトのパラメータ行。コントローラー (一括設定) 単位とライト単位で内容が違う。
    /// LiveEffectWindow のライトタブと TimelineItemInspector (ステージライトレイヤーの項目表示)
    /// で共有する。
    ///
    /// 書き込み先はレイヤーがキー化するのと同じ StageLightManager の実体で、
    /// どのスナップショットにも含まれないため履歴は記録しない (ウィンドウ側も記録していない)。
    /// 色欄の編集状態を持つため、対象ごと・描画するビューごとにインスタンスを分ける
    /// </summary>
    public class StageLightRowDrawer
    {
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
            GUIView view, StageLightController controller, string colorLabelPrefix)
        {
            SetColorLabels(colorLabelPrefix);

            var updateTransform = false;
            var defaultTrans = TransformDataStageLightController.defaultTrans;

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

            updateTransform |= view.DrawToggle("一括位置設定", controller.autoPosition, 200, 20, newValue =>
            {
                controller.autoPosition = newValue;
            });

            if (controller.autoPosition)
            {
                var initialPosition = defaultTrans.initialPosition;
                var transformCache = view.GetTransformCache(null);
                transformCache.position = controller.positionMin;

                view.DrawLabel("最小位置", 200, 20);

                updateTransform |= TimelineLayerBase.DrawPosition(
                    view,
                    transformCache,
                    TimelineLayerBase.TransformEditType.全て,
                    initialPosition);

                if (updateTransform)
                {
                    controller.positionMin = transformCache.position;
                }

                initialPosition = defaultTrans.initialSubPosition;
                transformCache = view.GetTransformCache(null);
                transformCache.position = controller.positionMax;

                view.DrawLabel("最大位置", 200, 20);

                updateTransform |= TimelineLayerBase.DrawPosition(
                    view,
                    transformCache,
                    TimelineLayerBase.TransformEditType.全て,
                    initialPosition);

                if (updateTransform)
                {
                    controller.positionMax = transformCache.position;
                }
            }

            updateTransform |= view.DrawToggle("一括角度設定", controller.autoRotation, 200, 20, newValue =>
            {
                controller.autoRotation = newValue;
            });

            if (controller.autoRotation)
            {
                var initialEulerAngles = defaultTrans.initialEulerAngles;
                var transformCache = view.GetTransformCache(null);
                transformCache.eulerAngles = controller.rotationMin;

                view.DrawLabel("最小角度", 200, 20);

                updateTransform |= TimelineLayerBase.DrawEulerAngles(
                    view,
                    transformCache,
                    TimelineLayerBase.TransformEditType.全て,
                    TimelinePrevKeyUtils.GetPrevEulerAngles<StageLightTimelineLayer>(controller.name, initialEulerAngles),
                    initialEulerAngles);

                if (updateTransform)
                {
                    controller.rotationMin = transformCache.eulerAngles;
                }

                transformCache = view.GetTransformCache(null);
                transformCache.eulerAngles = controller.rotationMax;

                view.DrawLabel("最大角度", 200, 20);

                updateTransform |= TimelineLayerBase.DrawEulerAngles(
                    view,
                    transformCache,
                    TimelineLayerBase.TransformEditType.全て,
                    TimelinePrevKeyUtils.GetPrevEulerAngles<StageLightTimelineLayer>(controller.name, initialEulerAngles, true),
                    initialEulerAngles);

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
                view.DrawLabel("最小色", 200, 20);

                updateTransform |= view.DrawColor(
                    _color1FieldCache,
                    controller.colorMin,
                    defaultTrans.initialColor,
                    c => controller.colorMin = c);

                view.DrawLabel("最大色", 200, 20);

                updateTransform |= view.DrawColor(
                    _color2FieldCache,
                    controller.colorMax,
                    defaultTrans.initialSubColor,
                    c => controller.colorMax = c);
            }

            updateTransform |= view.DrawToggle("一括ライト情報設定", controller.autoLightInfo, 200, 20, newValue =>
            {
                controller.autoLightInfo = newValue;
            });

            if (controller.autoLightInfo)
            {
                var lightInfo = controller.lightInfo;

                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.spotAngleInfo,
                    lightInfo.spotAngle,
                    x => lightInfo.spotAngle = x);
                
                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.spotRangeInfo,
                    lightInfo.spotRange,
                    x => lightInfo.spotRange = x);
                
                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.rangeMultiplierInfo,
                    lightInfo.rangeMultiplier,
                    x => lightInfo.rangeMultiplier = x);

                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.falloffExpInfo,
                    lightInfo.falloffExp,
                    x => lightInfo.falloffExp = x);

                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.noiseStrengthInfo,
                    lightInfo.noiseStrength,
                    x => lightInfo.noiseStrength = x);

                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.noiseScaleInfo,
                    lightInfo.noiseScale,
                    x => lightInfo.noiseScale = x);
                
                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.coreRadiusInfo,
                    lightInfo.coreRadius,
                    x => lightInfo.coreRadius = x);
                
                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.offsetRangeInfo,
                    lightInfo.offsetRange,
                    x => lightInfo.offsetRange = x);
                
                updateTransform |= view.DrawCustomValueInt(
                    defaultTrans.segmentAngleInfo,
                    lightInfo.segmentAngle,
                    x => lightInfo.segmentAngle = x);
                
                updateTransform |= view.DrawCustomValueInt(
                    defaultTrans.segmentRangeInfo,
                    lightInfo.segmentRange,
                    x => lightInfo.segmentRange = x);

                updateTransform |= view.DrawCustomValueBool(
                    defaultTrans.zTestInfo,
                    lightInfo.zTest,
                    x => lightInfo.zTest = x);
            }

            if (updateTransform)
            {
                controller.UpdateLights();
            }
        }

        /// <summary>ライト 1 灯のパラメータ行</summary>
        public void DrawLightRows(
            GUIView view, StageLightController controller, StageLight light,
            string colorLabelPrefix)
        {
            SetColorLabels(colorLabelPrefix);

            view.DrawLabel(light.displayName, 200, 20);

            if (!controller.autoVisible)
            {
                view.DrawToggle("表示", light.visible, 120, 20, newValue =>
                {
                    light.visible = newValue;
                });
            }

            var transformCache = view.GetTransformCache();
            var defaultTrans = TransformDataStageLight.defaultTrans;
            transformCache.position = light.position;
            transformCache.eulerAngles = light.eulerAngles;
            var initialPosition = defaultTrans.initialPosition;
            var initialEulerAngles = defaultTrans.initialEulerAngles;
            var initialScale = Vector3.one;
            var updateTransform = false;
            var editType = TimelineLayerBase.TransformEditType.全て;

            if (!controller.autoPosition)
            {
                updateTransform |= TimelineLayerBase.DrawPosition(view, transformCache, editType, initialPosition);
            }

            if (!controller.autoRotation)
            {
                updateTransform |= TimelineLayerBase.DrawEulerAngles(view, transformCache, editType,
                    TimelinePrevKeyUtils.GetPrevEulerAngles<StageLightTimelineLayer>(light.name, initialEulerAngles), initialEulerAngles);
            }

            if (updateTransform)
            {
                light.position = transformCache.position;
                light.eulerAngles = transformCache.eulerAngles;
            }

            if (!controller.autoColor)
            {
                updateTransform |= view.DrawColor(
                    _color1FieldCache,
                    light.color,
                    Color.white,
                    c => light.color = c);
            }

            if (!controller.autoLightInfo)
            {
                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.spotAngleInfo,
                    light.spotAngle,
                    x => light.spotAngle = x);
                
                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.spotRangeInfo,
                    light.spotRange,
                    x => light.spotRange = x);
                
                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.rangeMultiplierInfo,
                    light.rangeMultiplier,
                    x => light.rangeMultiplier = x);

                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.falloffExpInfo,
                    light.falloffExp,
                    x => light.falloffExp = x);

                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.noiseStrengthInfo,
                    light.noiseStrength,
                    x => light.noiseStrength = x);

                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.noiseScaleInfo,
                    light.noiseScale,
                    x => light.noiseScale = x);
                
                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.coreRadiusInfo,
                    light.coreRadius,
                    x => light.coreRadius = x);
                
                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.offsetRangeInfo,
                    light.offsetRange,
                    x => light.offsetRange = x);
                
                updateTransform |= view.DrawCustomValueInt(
                    defaultTrans.segmentAngleInfo,
                    light.segmentAngle,
                    x => light.segmentAngle = x);
                
                updateTransform |= view.DrawCustomValueInt(
                    defaultTrans.segmentRangeInfo,
                    light.segmentRange,
                    x => light.segmentRange = x);

                updateTransform |= view.DrawCustomValueBool(
                    defaultTrans.zTestInfo,
                    light.zTest,
                    x => light.zTest = x);
            }
        }
    }
}
