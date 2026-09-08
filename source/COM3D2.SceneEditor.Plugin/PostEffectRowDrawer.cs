using System;
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using COM3D2.MotionTimelineEditor.Plugin;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// ポストエフェクト 1 つ分のパラメータ行 (被写界深度 / パラフィン / 距離フォグ / リムライト / GTToneMap / ブルーム)。
    ///
    /// 委譲先の個別ウィンドウが無いため、コピー先への複製とトーンカーブのプレビューも含めここで描く。
    /// 書き込み先は PostEffectManager のデータで、値の範囲・既定値は TransformData の Info を使う。
    /// 書き込み先はどのスナップショットにも含まれないため履歴は記録しない。
    ///
    /// 色欄とコンボボックスの状態を持つため、エフェクトごと・描画するビューごとに
    /// インスタンスを分ける
    /// </summary>
    public class PostEffectRowDrawer
    {
        private static MTEP.PostEffectManager postEffectManager => MTEP.PostEffectManager.instance;
        private static MTEP.TimelineData timeline => MTEP.TimelineManager.instance.timeline;
        private static MTEP.Config config => MTEP.ConfigManager.instance.config;

        // ラベル (= カラーピッカーの同定キー) は対象ごとに変えるため、描画時に設定する
        private readonly ColorFieldCache _color1FieldCache = new ColorFieldCache("", true);
        private readonly ColorFieldCache _color2FieldCache = new ColorFieldCache("", true);

        // ブルームは色が 5 つあり色1/色2 のペア用キャッシュでは足りないため、専用に持つ
        private readonly ColorFieldCache _bloomThresholdColorFieldCache = new ColorFieldCache("", false);
        private readonly ColorFieldCache[] _bloomFlareColorFieldCaches =
        {
            new ColorFieldCache("", true),
            new ColorFieldCache("", true),
            new ColorFieldCache("", true),
            new ColorFieldCache("", true),
        };

        private static readonly string[] BloomFlareColorLabels = { "A", "B", "C", "D" };

        private readonly GUIComboBox<MTEP.MaidCache> _maidComboBox = new GUIComboBox<MTEP.MaidCache>
        {
            getName = (maidCache, _) => maidCache == null ? "未選択" : maidCache.fullName,
            buttonSize = new Vector2(120, 20),
            contentSize = new Vector2(150, 300),
            showArrow = false,
        };

        private readonly GUIComboBox<string> _copyToComboBox = new GUIComboBox<string>
        {
            getName = (name, index) => name,
        };

        /// <summary>コピー先コンボの選択肢。エフェクト数が変わったときだけ作り直す</summary>
        private readonly List<string> _copyToNames = new List<string>();

        /// <summary>GTToneMap のトーンカーブ表示用。描画済みのデータと一致する間は使い回す</summary>
        private Texture2D _gtToneMapTexture;
        private MTEP.GTToneMapData _gtToneMapTextureData;

        /// <summary>
        /// 同種の別インデックスへ設定をコピーする行。
        /// コンボは項目ごとの Drawer が持つため、エフェクトごとに選択状態が残る
        /// </summary>
        private void DrawCopyRow(
            GUIView view, int count, int sourceIndex,
            Func<int, string> getJpName, Action<int> copyTo)
        {
            if (_copyToNames.Count != count)
            {
                _copyToNames.Clear();
                for (var i = 0; i < count; i++)
                {
                    _copyToNames.Add(getJpName(i));
                }
            }

            _copyToComboBox.items = _copyToNames;
            _copyToComboBox.DrawButton("コピー先", view);

            if (view.DrawButton("コピー", 60, 20))
            {
                var copyToIndex = _copyToComboBox.currentIndex;
                if (copyToIndex != -1 && copyToIndex != sourceIndex)
                {
                    copyTo(copyToIndex);
                }
            }

            view.SetEnabled(view.focusedComboBox == null);
        }

        /// <summary>色欄のラベルを対象ごとに一意にする (ColorPickerWindow はラベルで対象を識別する)</summary>
        public void SetColorLabels(string colorLabelPrefix)
        {
            _color1FieldCache.label = colorLabelPrefix + "/色1";
            _color2FieldCache.label = colorLabelPrefix + "/色2";
            _bloomThresholdColorFieldCache.label = colorLabelPrefix + "/しきい値色";
            for (var i = 0; i < _bloomFlareColorFieldCaches.Length; i++)
            {
                _bloomFlareColorFieldCaches[i].label = colorLabelPrefix + "/ﾌﾚｱ色" + BloomFlareColorLabels[i];
            }
        }

        /// <summary>色1 / 色2 の行</summary>
        private bool DrawColorPair(
            GUIView view, Color color1, Color color2,
            Color initialColor, Color initialSubColor, Action<Color, Color> onChanged)
        {
            var updated = false;

            updated |= view.DrawColor(_color1FieldCache, color1, initialColor,
                newValue => onChanged(newValue, color2));

            updated |= view.DrawColor(_color2FieldCache, color2, initialSubColor,
                newValue => onChanged(color1, newValue));

            return updated;
        }

        /// <summary>被写界深度</summary>
        public void DrawDepthOfFieldRows(GUIView view)
        {
            var depthOfField = postEffectManager.GetDepthOfFieldData();
            var updateTransform = false;
            var defaultTrans = TransformDataDepthOfField.defaultTrans;

            view.DrawToggle("有効化", depthOfField.enabled, 80, 20, newValue =>
            {
                depthOfField.enabled = newValue;
                updateTransform = true;
            });

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.focalLengthInfo,
                depthOfField.focalLength,
                newValue => depthOfField.focalLength = newValue);

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.focalSizeInfo,
                depthOfField.focalSize,
                newValue => depthOfField.focalSize = newValue);
            
            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.apertureInfo,
                depthOfField.aperture,
                newValue => depthOfField.aperture = newValue);
            
            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.maxBlurSizeInfo,
                depthOfField.maxBlurSize,
                newValue => depthOfField.maxBlurSize = newValue);

            view.BeginHorizontal();
            {
                view.DrawLabel("追従メイド", 70, 20);

                view.DrawToggle("", depthOfField.maidSlotNo >= 0, 20, 20, newValue =>
                {
                    depthOfField.maidSlotNo = newValue ? _maidComboBox.currentIndex : -1;
                    updateTransform = true;
                });

                _maidComboBox.items = MTEP.MaidManager.instance.maidCaches;
                _maidComboBox.onSelected = (maidCache, index) =>
                {
                    depthOfField.maidSlotNo = index;
                    updateTransform = true;
                };
                _maidComboBox.DrawButton(view);
            }
            view.EndLayout();

            view.SetEnabled(view.focusedComboBox == null);

            if (updateTransform)
            {
                postEffectManager.ApplyDepthOfField(depthOfField);
            }
        }

        /// <summary>パラフィン (index 番目)</summary>
        public void DrawParaffinRows(GUIView view, int index)
        {
            var paraffin = postEffectManager.GetParaffinData(index);
            var updateTransform = false;
            var defaultTrans = TransformDataParaffin.defaultTrans;

            updateTransform = view.DrawToggle("有効化", paraffin.enabled, 80, 20, newValue =>
            {
                paraffin.enabled = newValue;
            });

            updateTransform |= DrawColorPair(view,
                paraffin.color1, paraffin.color2,
                defaultTrans.initialColor, defaultTrans.initialSubColor,
                (color1, color2) =>
                {
                    paraffin.color1 = color1;
                    paraffin.color2 = color2;
                });

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.centerPositionXInfo,
                paraffin.centerPosition.x,
                newValue => paraffin.centerPosition.x = newValue);
            
            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.centerPositionYInfo,
                paraffin.centerPosition.y,
                newValue => paraffin.centerPosition.y = newValue);

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.radiusFarInfo,
                paraffin.radiusFar,
                newValue => paraffin.radiusFar = newValue);

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.radiusNearInfo,
                paraffin.radiusNear,
                newValue => paraffin.radiusNear = newValue);

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.radiusScaleXInfo,
                paraffin.radiusScale.x,
                newValue => paraffin.radiusScale.x = newValue);

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.radiusScaleYInfo,
                paraffin.radiusScale.y,
                newValue => paraffin.radiusScale.y = newValue);
            updateTransform |= view.DrawCustomValueInt(
                defaultTrans.maskModeInfo,
                paraffin.maskMode,
                newValue => paraffin.maskMode = newValue);

            view.DrawLabel("ブレンドモード", 100, 20);

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.useNormalInfo,
                paraffin.useNormal,
                newValue => paraffin.useNormal = newValue);

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.useAddInfo,
                paraffin.useAdd,
                newValue => paraffin.useAdd = newValue);

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.useMultiplyInfo,
                paraffin.useMultiply,
                newValue => paraffin.useMultiply = newValue);

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.useOverlayInfo,
                paraffin.useOverlay,
                newValue => paraffin.useOverlay = newValue);

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.useSubstructInfo,
                paraffin.useSubstruct,
                newValue => paraffin.useSubstruct = newValue);

            view.DrawHorizontalLine(Color.gray);

            DrawCopyRow(view, timeline.paraffinCount, index,
                PostEffectTimelineLayer.GetParaffinJpName,
                copyToIndex => postEffectManager.ApplyParaffin(copyToIndex, paraffin));

            if (updateTransform)
            {
                postEffectManager.ApplyParaffin(index, paraffin);
            }
        }

        /// <summary>距離フォグ (index 番目)</summary>
        public void DrawDistanceFogRows(GUIView view, int index)
        {
            var distanceFog = postEffectManager.GetDistanceFogData(index);
            var updateTransform = false;
            var defaultTrans = TransformDataDistanceFog.defaultTrans;

            updateTransform = view.DrawToggle("有効化", distanceFog.enabled, 80, 20, newValue =>
            {
                distanceFog.enabled = newValue;
            });

            updateTransform |= DrawColorPair(view,
                distanceFog.color1, distanceFog.color2,
                defaultTrans.initialColor, defaultTrans.initialSubColor,
                (color1, color2) =>
                {
                    distanceFog.color1 = color1;
                    distanceFog.color2 = color2;
                });

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.fogStartInfo,
                distanceFog.fogStart,
                newValue => distanceFog.fogStart = newValue);
            
            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.fogEndInfo,
                distanceFog.fogEnd,
                newValue => distanceFog.fogEnd = newValue);
            
            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.fogExpInfo,
                distanceFog.fogExp,
                newValue => distanceFog.fogExp = newValue);

            view.DrawLabel("ブレンドモード", 100, 20);

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.useNormalInfo,
                distanceFog.useNormal,
                newValue => distanceFog.useNormal = newValue);

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.useAddInfo,
                distanceFog.useAdd,
                newValue => distanceFog.useAdd = newValue);

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.useMultiplyInfo,
                distanceFog.useMultiply,
                newValue => distanceFog.useMultiply = newValue);

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.useOverlayInfo,
                distanceFog.useOverlay,
                newValue => distanceFog.useOverlay = newValue);

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.useSubstructInfo,
                distanceFog.useSubstruct,
                newValue => distanceFog.useSubstruct = newValue);

            view.DrawHorizontalLine(Color.gray);

            DrawCopyRow(view, timeline.distanceFogCount, index,
                PostEffectTimelineLayer.GetDistanceFogJpName,
                copyToIndex => postEffectManager.ApplyDistanceFog(copyToIndex, distanceFog));

            if (updateTransform)
            {
                postEffectManager.ApplyDistanceFog(index, distanceFog);
            }
        }

        /// <summary>リムライト (index 番目)</summary>
        /// <param name="rimlightName">キー名 (メニュー項目名)。直前キーの角度を引くのに使う</param>
        public void DrawRimlightRows(GUIView view, int index, string rimlightName)
        {
            var rimlight = postEffectManager.GetRimlightData(index);
            var updateTransform = false;
            var defaultTrans = TransformDataRimlight.defaultTrans;

            updateTransform = view.DrawToggle("有効化", rimlight.enabled, 80, 20, newValue =>
            {
                rimlight.enabled = newValue;
            });

            updateTransform |= DrawColorPair(view,
                rimlight.color1, rimlight.color2,
                defaultTrans.initialColor, defaultTrans.initialSubColor,
                (color1, color2) =>
                {
                    rimlight.color1 = color1;
                    rimlight.color2 = color2;
                });

            var initialEulerAngles = defaultTrans.initialEulerAngles;
            var transformCache = view.GetTransformCache(null);
            transformCache.eulerAngles = rimlight.rotation;

            view.DrawLabel("ライト方向", 200, 20);

            // レイヤー UI はここへ表示名 (日本語) を渡しており直前キーを引けていないが、
            // 新規に書く側で同じ状態を作る理由が無いため、キー名 (ボーン名) を渡す
            updateTransform |= TimelineLayerBase.DrawEulerAngles(
                view,
                transformCache,
                TimelineLayerBase.TransformEditType.全て,
                TimelinePrevKeyUtils.GetPrevEulerAngles<PostEffectTimelineLayer>(
                    rimlightName, initialEulerAngles),
                initialEulerAngles);

            updateTransform |= view.DrawCustomValueBool(
                defaultTrans.isWorldSpaceInfo,
                rimlight.isWorldSpace,
                newValue => rimlight.isWorldSpace = newValue);

            updateTransform |= view.DrawCustomValueInt(
                defaultTrans.maskModeInfo,
                rimlight.maskMode,
                newValue => rimlight.maskMode = newValue);

            updateTransform |= view.DrawCustomValueBool(
                defaultTrans.excludeFaceInfo,
                rimlight.excludeFace,
                newValue => rimlight.excludeFace = newValue);

            // 顔を除外しているときだけ、髪へ戻すかを選べる (実体側 UI と同じ条件)
            if (rimlight.excludeFace)
            {
                updateTransform |= view.DrawCustomValueBool(
                    defaultTrans.applyHairInfo,
                    rimlight.applyHair,
                    newValue => rimlight.applyHair = newValue);
            }

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.lightAreaInfo,
                rimlight.lightArea,
                newValue => rimlight.lightArea = newValue);

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.fadeRangeInfo,
                rimlight.fadeRange,
                newValue => rimlight.fadeRange = newValue);

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.fadeExpInfo,
                rimlight.fadeExp,
                newValue => rimlight.fadeExp = newValue);

            view.DrawLabel("ブレンドモード", 100, 20);

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.useNormalInfo,
                rimlight.useNormal,
                newValue => rimlight.useNormal = newValue);

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.useAddInfo,
                rimlight.useAdd,
                newValue => rimlight.useAdd = newValue);

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.useMultiplyInfo,
                rimlight.useMultiply,
                newValue => rimlight.useMultiply = newValue);

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.useOverlayInfo,
                rimlight.useOverlay,
                newValue => rimlight.useOverlay = newValue);

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.useSubstructInfo,
                rimlight.useSubstruct,
                newValue => rimlight.useSubstruct = newValue);

            view.DrawHorizontalLine(Color.gray);

            DrawCopyRow(view, timeline.rimlightCount, index,
                PostEffectTimelineLayer.GetRimlightJpName,
                copyToIndex => postEffectManager.ApplyRimlight(copyToIndex, rimlight));

            if (updateTransform)
            {
                rimlight.rotation = transformCache.eulerAngles;
                postEffectManager.ApplyRimlight(index, rimlight);
            }
        }

        /// <summary>GTToneMap</summary>
        public void DrawGTToneMapRows(GUIView view)
        {
            var data = postEffectManager.GetGTToneMapData();
            var updateTransform = false;
            var defaultTrans = TransformDataGTToneMap.defaultTrans;

            view.DrawToggle("有効化", data.enabled, 80, 20, newValue =>
            {
                data.enabled = newValue;
                updateTransform = true;
            });

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.maxBrightnessInfo,
                data.maxBrightness,
                newValue => data.maxBrightness = newValue);

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.contrastInfo,
                data.contrast,
                newValue => data.contrast = newValue);

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.linearStartInfo,
                data.linearStart,
                newValue => data.linearStart = newValue);

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.linearLengthInfo,
                data.linearLength,
                newValue => data.linearLength = newValue);

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.blackTightnessInfo,
                data.blackTightness,
                newValue => data.blackTightness = newValue);

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.blackOffsetInfo,
                data.blackOffset,
                newValue => data.blackOffset = newValue);

            if (updateTransform)
            {
                postEffectManager.ApplyGTToneMap(data);
            }

            view.DrawHorizontalLine(Color.gray);

            DrawGTToneMapCurve(view, data);
        }

        /// <summary>ブルーム</summary>
        public void DrawBloomRows(GUIView view)
        {
            var bloom = postEffectManager.GetBloomData();
            var updateTransform = false;
            var defaultTrans = TransformDataBloom.defaultTrans;

            updateTransform = view.DrawToggle("有効化", bloom.enabled, 80, 20, newValue =>
            {
                bloom.enabled = newValue;
            });

            updateTransform |= view.DrawCustomValueBool(
                defaultTrans.gameEffectDisabledInfo,
                bloom.gameEffectDisabled,
                newValue => bloom.gameEffectDisabled = newValue);

            updateTransform |= view.DrawCustomValueInt(
                defaultTrans.hdrInfo,
                bloom.hdr,
                newValue => bloom.hdr = newValue);

            // 0=Screen / 1=Add
            updateTransform |= view.DrawCustomValueBool(
                defaultTrans.screenBlendModeInfo,
                bloom.screenBlendMode != 0,
                newValue => bloom.screenBlendMode = newValue ? 1 : 0);

            updateTransform |= view.DrawCustomValueBool(
                defaultTrans.highQualityInfo,
                bloom.highQuality,
                newValue => bloom.highQuality = newValue);

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.intensityInfo,
                bloom.intensity,
                newValue => bloom.intensity = newValue);

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.thresholdInfo,
                bloom.threshold,
                newValue => bloom.threshold = newValue);

            updateTransform |= view.DrawColor(
                _bloomThresholdColorFieldCache,
                bloom.thresholdColor,
                defaultTrans.initialColor,
                newValue => bloom.thresholdColor = newValue);

            updateTransform |= view.DrawCustomValueInt(
                defaultTrans.blurIterationsInfo,
                bloom.blurIterations,
                newValue => bloom.blurIterations = newValue);

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.blurSpreadInfo,
                bloom.blurSpread,
                newValue => bloom.blurSpread = newValue);

            view.DrawHorizontalLine(Color.gray);
            view.DrawLabel("キャラと背景の分離", 200, 20);

            updateTransform |= view.DrawCustomValueBool(
                defaultTrans.separationEnabledInfo,
                bloom.separationEnabled,
                newValue => bloom.separationEnabled = newValue);

            // 分離が無効なうちは以降の値が効かないので出さない (実体側 UI と同じ条件)
            if (bloom.separationEnabled)
            {
                updateTransform |= view.DrawCustomValueBool(
                    defaultTrans.separationCharactersEnabledInfo,
                    bloom.separationCharactersEnabled,
                    newValue => bloom.separationCharactersEnabled = newValue);

                updateTransform |= view.DrawCustomValueBool(
                    defaultTrans.separationBackgroundEnabledInfo,
                    bloom.separationBackgroundEnabled,
                    newValue => bloom.separationBackgroundEnabled = newValue);

                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.separationCharacterIntensityInfo,
                    bloom.separationCharacterIntensity,
                    newValue => bloom.separationCharacterIntensity = newValue);

                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.separationCharacterThresholdInfo,
                    bloom.separationCharacterThreshold,
                    newValue => bloom.separationCharacterThreshold = newValue);

                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.separationCharacterRadiusInfo,
                    bloom.separationCharacterRadius,
                    newValue => bloom.separationCharacterRadius = newValue);
            }

            view.DrawHorizontalLine(Color.gray);
            view.DrawLabel("レンズフレア", 200, 20);

            updateTransform |= view.DrawCustomValueInt(
                defaultTrans.lensFlareModeInfo,
                bloom.lensFlareMode,
                newValue => bloom.lensFlareMode = newValue);

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.lensFlareIntensityInfo,
                bloom.lensFlareIntensity,
                newValue => bloom.lensFlareIntensity = newValue);

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.lensFlareSaturationInfo,
                bloom.lensFlareSaturation,
                newValue => bloom.lensFlareSaturation = newValue);

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.lensFlareThresholdInfo,
                bloom.lensFlareThreshold,
                newValue => bloom.lensFlareThreshold = newValue);

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.flareRotationInfo,
                bloom.flareRotation,
                newValue => bloom.flareRotation = newValue);

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.hollyStretchWidthInfo,
                bloom.hollyStretchWidth,
                newValue => bloom.hollyStretchWidth = newValue);

            updateTransform |= view.DrawCustomValueInt(
                defaultTrans.hollywoodFlareBlurIterationsInfo,
                bloom.hollywoodFlareBlurIterations,
                newValue => bloom.hollywoodFlareBlurIterations = newValue);

            updateTransform |= view.DrawColor(
                _bloomFlareColorFieldCaches[0],
                bloom.flareColorA,
                defaultTrans.initialSubColor,
                newValue => bloom.flareColorA = newValue);

            updateTransform |= view.DrawColor(
                _bloomFlareColorFieldCaches[1],
                bloom.flareColorB,
                TransformDataBloom.InitialFlareColorB,
                newValue => bloom.flareColorB = newValue);

            updateTransform |= view.DrawColor(
                _bloomFlareColorFieldCaches[2],
                bloom.flareColorC,
                TransformDataBloom.InitialFlareColorC,
                newValue => bloom.flareColorC = newValue);

            updateTransform |= view.DrawColor(
                _bloomFlareColorFieldCaches[3],
                bloom.flareColorD,
                TransformDataBloom.InitialFlareColorD,
                newValue => bloom.flareColorD = newValue);

            if (updateTransform)
            {
                postEffectManager.ApplyBloom(bloom);
            }
        }

        /// <summary>トーンカーブのプレビュー</summary>
        private void DrawGTToneMapCurve(GUIView view, MTEP.GTToneMapData data)
        {
            if (_gtToneMapTexture == null)
            {
                _gtToneMapTexture = new Texture2D(150, 150);
                TextureUtils.ClearTexture(_gtToneMapTexture, config.curveBgColor);
            }

            if (!_gtToneMapTextureData.Equals(data))
            {
                _gtToneMapTextureData = data;

                TextureUtils.ClearTexture(_gtToneMapTexture, config.curveBgColor);

                MTEP.GTToneMap.ApplyTexture(
                    _gtToneMapTexture,
                    config.curveLineColor,
                    1,
                    data.maxBrightness,
                    data.contrast,
                    data.linearStart,
                    data.linearLength,
                    data.blackTightness,
                    data.blackOffset);
            }

            view.DrawTexture(_gtToneMapTexture);
        }
    }
}
