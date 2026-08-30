using COM3D2.MotionTimelineEditor;
using COM3D2.MotionTimelineEditor.Plugin;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// サイリウムのパラメータ行。コントローラー (グループ) / バー / 持ち手 / エリアで内容が違う。
    /// LiveEffectWindow のサイリウムタブと TimelineItemInspector
    /// (サイリウムレイヤーの項目表示) で共有する。
    ///
    /// 書き込み先はレイヤーがキー化するのと同じ PsylliumManager の実体で、
    /// どのスナップショットにも含まれないため履歴は記録しない (ウィンドウ側も記録していない)。
    /// 色欄の編集状態を持つため、対象ごと・描画するビューごとにインスタンスを分ける。
    ///
    /// パターン・移動回転はウィンドウ側が両手/右手/左手のタブで対象を切り替える構造のため
    /// ここには含めない (ライブ演出ウィンドウで編集する)
    /// </summary>
    public class PsylliumRowDrawer
    {
        // ラベル (= カラーピッカーの同定キー) は対象ごとに変えるため、描画時に設定する
        private readonly ColorFieldCache _color1aFieldCache = new ColorFieldCache("", true);
        private readonly ColorFieldCache _color1bFieldCache = new ColorFieldCache("", true);
        private readonly ColorFieldCache _color1cFieldCache = new ColorFieldCache("", true);
        private readonly ColorFieldCache _color2aFieldCache = new ColorFieldCache("", true);
        private readonly ColorFieldCache _color2bFieldCache = new ColorFieldCache("", true);
        private readonly ColorFieldCache _color2cFieldCache = new ColorFieldCache("", true);

        /// <summary>色欄のラベルを対象ごとに一意にする (ColorPickerWindow はラベルで対象を識別する)</summary>
        private void SetColorLabels(string colorLabelPrefix)
        {
            _color1aFieldCache.label = colorLabelPrefix + "/色1a";
            _color1bFieldCache.label = colorLabelPrefix + "/色1b";
            _color1cFieldCache.label = colorLabelPrefix + "/色1c";
            _color2aFieldCache.label = colorLabelPrefix + "/色2a";
            _color2bFieldCache.label = colorLabelPrefix + "/色2b";
            _color2cFieldCache.label = colorLabelPrefix + "/色2c";
        }

        /// <summary>コントローラー (グループ) のパラメータ行</summary>
        public void DrawControllerRows(GUIView view, PsylliumController controller)
        {
            var updateTransform = false;
            var defaultTrans = TransformDataPsylliumController.defaultTrans;
            var transformCache = view.GetTransformCache(null);

            updateTransform |= view.DrawToggle(controller.displayName, controller.visible, 200, 20, newValue =>
            {
                controller.visible = newValue;
            });

            {
                var initialPosition = defaultTrans.initialPosition;
                transformCache.position = controller.position;

                updateTransform |= TimelineLayerBase.DrawPosition(
                    view,
                    transformCache,
                    TimelineLayerBase.TransformEditType.全て,
                    initialPosition);

                if (updateTransform)
                {
                    controller.position = transformCache.position;
                }
            }

            {
                var initialEulerAngles = defaultTrans.initialEulerAngles;
                transformCache.eulerAngles = controller.eulerAngles;

                updateTransform |= TimelineLayerBase.DrawEulerAngles(
                    view,
                    transformCache,
                    TimelineLayerBase.TransformEditType.全て,
                    TimelinePrevKeyUtils.GetPrevEulerAngles<PsylliumTimelineLayer>(controller.name, initialEulerAngles),
                    initialEulerAngles);

                if (updateTransform)
                {
                    controller.eulerAngles = transformCache.eulerAngles;
                }
            }

            if (updateTransform)
            {
                controller.Refresh();
            }
        }

        /// <summary>バー設定のパラメータ行</summary>
        public void DrawBarConfigRows(
            GUIView view, PsylliumController controller, string colorLabelPrefix)
        {
            SetColorLabels(colorLabelPrefix);

            var barConfig = controller.barConfig;
            var updateTransform = false;
            var defaultTrans = TransformDataPsylliumBar.defaultTrans;

            view.DrawLabel("中心色1", 200, 20);

            updateTransform |= view.DrawColor(
                _color1aFieldCache,
                barConfig.color1a,
                Color.white,
                c => barConfig.color1a = c);

            view.DrawLabel("縁色1", 200, 20);

            updateTransform |= view.DrawColor(
                _color1bFieldCache,
                barConfig.color1b,
                Color.white,
                c => barConfig.color1b = c);

            view.DrawLabel("散乱色1", 200, 20);

            updateTransform |= view.DrawColor(
                _color1cFieldCache,
                barConfig.color1c,
                Color.white,
                c => barConfig.color1c = c);
            
            view.DrawLabel("中心色2", 200, 20);

            updateTransform |= view.DrawColor(
                _color2aFieldCache,
                barConfig.color2a,
                Color.white,
                c => barConfig.color2a = c);

            view.DrawLabel("縁色2", 200, 20);
            
            updateTransform |= view.DrawColor(
                _color2bFieldCache,
                barConfig.color2b,
                Color.white,
                c => barConfig.color2b = c);

            view.DrawLabel("散乱色2", 200, 20);

            updateTransform |= view.DrawColor(
                _color2cFieldCache,
                barConfig.color2c,
                Color.white,
                c => barConfig.color2c = c);

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.baseScaleInfo,
                barConfig.baseScale,
                y => barConfig.baseScale = y);

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.widthInfo,
                barConfig.width,
                y => barConfig.width = y);
            
            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.heightInfo,
                barConfig.height,
                y => barConfig.height = y);
            
            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.positionYInfo,
                barConfig.positionY,
                y => barConfig.positionY = y);
            
            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.radiusInfo,
                barConfig.radius,
                y => barConfig.radius = y);
            
            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.topThresholdInfo,
                barConfig.topThreshold,
                y => barConfig.topThreshold = y);
            
            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.cutoffAlphaInfo,
                barConfig.cutoffAlpha,
                y => barConfig.cutoffAlpha = y);

            if (updateTransform)
            {
                controller.Refresh();
            }
        }

        /// <summary>持ち手設定のパラメータ行</summary>
        public void DrawHandConfigRows(GUIView view, PsylliumController controller)
        {
            var handConfig = controller.handConfig;
            var updateTransform = false;
            var defaultTrans = TransformDataPsylliumHand.defaultTrans;
            var defaultConfig = TransformDataPsylliumHand.defaultConfig;

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.handSpacingInfo,
                handConfig.handSpacing,
                y => handConfig.handSpacing = y);
            
            var transformCache = view.GetTransformCache(null);

            view.DrawLabel("サイリウム間の位置", 200, 20);

            {
                var initialPosition = defaultConfig.barOffsetPosition;
                transformCache.position = handConfig.barOffsetPosition;

                updateTransform |= TimelineLayerBase.DrawPosition(
                    view,
                    transformCache,
                    TimelineLayerBase.TransformEditType.全て,
                    initialPosition);

                if (updateTransform)
                {
                    handConfig.barOffsetPosition = transformCache.position;
                }
            }

            view.DrawLabel("サイリウム間の角度", 200, 20);

            {
                var initialEulerAngles = defaultConfig.barOffsetRotation;
                var prevEulerAngles = Vector3.zero;
                transformCache.eulerAngles = handConfig.barOffsetRotation;

                updateTransform |= TimelineLayerBase.DrawEulerAngles(
                    view,
                    transformCache,
                    TimelineLayerBase.TransformEditType.全て,
                    prevEulerAngles,
                    initialEulerAngles);

                if (updateTransform)
                {
                    handConfig.barOffsetRotation = transformCache.eulerAngles;
                }
            }

            if (updateTransform)
            {
                controller.Refresh();
            }
        }

        /// <summary>エリアのパラメータ行</summary>
        public void DrawAreaRows(GUIView view, PsylliumArea area)
        {
            var areaConfig = area.areaConfig;
            var transformCache = view.GetTransformCache();
            var defaultTrans = TransformDataPsylliumArea.defaultTrans;
            transformCache.position = areaConfig.position;
            transformCache.eulerAngles = areaConfig.rotation;
            var initialPosition = defaultTrans.initialPosition;
            var initialEulerAngles = defaultTrans.initialEulerAngles;
            var initialScale = Vector3.one;
            var updateTransform = false;
            var editType = TimelineLayerBase.TransformEditType.全て;

            updateTransform |= view.DrawToggle(area.displayName, areaConfig.visible, 200, 20, newValue =>
            {
                areaConfig.visible = newValue;
            });

            updateTransform |= TimelineLayerBase.DrawPosition(view, transformCache, editType, initialPosition);
            if (updateTransform)
            {
                areaConfig.position = transformCache.position;
            }

            updateTransform |= TimelineLayerBase.DrawEulerAngles(view, transformCache, editType,
                    TimelinePrevKeyUtils.GetPrevEulerAngles<PsylliumTimelineLayer>(area.name, initialEulerAngles), initialEulerAngles);
            if (updateTransform)
            {
                areaConfig.rotation = transformCache.eulerAngles;
            }

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.sizeXInfo,
                areaConfig.size.x,
                x => areaConfig.size.x = x);

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.sizeYInfo,
                areaConfig.size.y,
                y => areaConfig.size.y = y);

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.seatDistanceXInfo,
                areaConfig.seatDistance.x,
                x => areaConfig.seatDistance.x = x);

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.seatDistanceYInfo,
                areaConfig.seatDistance.y,
                y => areaConfig.seatDistance.y = y);

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.randomPositionRangeXInfo,
                areaConfig.randomPositionRange.x,
                x => areaConfig.randomPositionRange.x = x);

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.randomPositionRangeYInfo,
                areaConfig.randomPositionRange.y,
                y => areaConfig.randomPositionRange.y = y);

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.randomPositionRangeZInfo,
                areaConfig.randomPositionRange.z,
                z => areaConfig.randomPositionRange.z = z);

            view.DrawLabel("バー数の重み", 200, 20);

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.barCountWeight0Info,
                areaConfig.barCountWeight0,
                y => areaConfig.barCountWeight0 = y);
            
            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.barCountWeight1Info,
                areaConfig.barCountWeight1,
                y => areaConfig.barCountWeight1 = y);
            
            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.barCountWeight2Info,
                areaConfig.barCountWeight2,
                y => areaConfig.barCountWeight2 = y);
            
            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.barCountWeight3Info,
                areaConfig.barCountWeight3,
                y => areaConfig.barCountWeight3 = y);

            view.DrawLabel("色の重み", 200, 20);

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.colorWeight1Info,
                areaConfig.colorWeight1,
                y => areaConfig.colorWeight1 = y);
            
            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.colorWeight2Info,
                areaConfig.colorWeight2,
                y => areaConfig.colorWeight2 = y);

            view.DrawLabel("パターンの重み", 200, 20);

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.patternWeight0Info,
                areaConfig.patternWeight0,
                y => areaConfig.patternWeight0 = y);
            
            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.patternWeight1Info,
                areaConfig.patternWeight1,
                y => areaConfig.patternWeight1 = y);
            
            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.patternWeight2Info,
                areaConfig.patternWeight2,
                y => areaConfig.patternWeight2 = y);
            
            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.patternWeight3Info,
                areaConfig.patternWeight3,
                y => areaConfig.patternWeight3 = y);
            
            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.patternWeight4Info,
                areaConfig.patternWeight4,
                y => areaConfig.patternWeight4 = y);
            
            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.patternWeight5Info,
                areaConfig.patternWeight5,
                y => areaConfig.patternWeight5 = y);
            
            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.patternWeight6Info,
                areaConfig.patternWeight6,
                y => areaConfig.patternWeight6 = y);
            
            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.patternWeight7Info,
                areaConfig.patternWeight7,
                y => areaConfig.patternWeight7 = y);
            
            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.patternWeight8Info,
                areaConfig.patternWeight8,
                y => areaConfig.patternWeight8 = y);
            
            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.patternWeight9Info,
                areaConfig.patternWeight9,
                y => areaConfig.patternWeight9 = y);

            updateTransform |= view.DrawCustomValueIntRandom(
                defaultTrans.randomSeedInfo,
                areaConfig.randomSeed,
                y => areaConfig.randomSeed = y);

            if (updateTransform)
            {
                area.Refresh();
            }
        }
    }
}
