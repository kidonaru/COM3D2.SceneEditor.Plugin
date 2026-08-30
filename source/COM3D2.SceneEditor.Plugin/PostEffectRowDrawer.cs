using System;
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using COM3D2.MotionTimelineEditor.Plugin;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// ポストエフェクト 1 つ分のパラメータ行 (被写界深度 / パラフィン / 距離フォグ / リムライト / GTToneMap)。
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
        private readonly ColorFieldCache _color3FieldCache = new ColorFieldCache("", true);
        private readonly ColorFieldCache _color4FieldCache = new ColorFieldCache("", true);

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
            _color3FieldCache.label = colorLabelPrefix + "/色3";
            _color4FieldCache.label = colorLabelPrefix + "/色4";
        }

        /// <summary>
        /// 色1 / 色2 の行。拡張色が無効なときは色2 を色1 に合わせ、
        /// アルファだけ別スライダー ("A2") で編集する (レイヤー UI と同じ扱い)
        /// </summary>
        private bool DrawColorPair(
            GUIView view, Color color1, Color color2,
            Color initialColor, Color initialSubColor, Action<Color, Color> onChanged)
        {
            var updated = false;

            if (timeline.usePostEffectExtraColor)
            {
                updated |= view.DrawColor(_color1FieldCache, color1, initialColor,
                    newValue => onChanged(newValue, color2));

                updated |= view.DrawColor(_color2FieldCache, color2, initialSubColor,
                    newValue => onChanged(color1, newValue));

                return updated;
            }

            updated |= view.DrawColor(_color1FieldCache, color1, initialColor,
                newValue =>
                {
                    // アルファだけは色2 の値を保つ
                    var subColor = newValue;
                    subColor.a = color2.a;
                    onChanged(newValue, subColor);
                });

            updated |= view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "A2",
                labelWidth = 30,
                min = 0f,
                max = 1f,
                step = 0.01f,
                defaultValue = 0f,
                value = color2.a,
                onChanged = newValue =>
                {
                    var subColor = color2;
                    subColor.a = newValue;
                    onChanged(color1, subColor);
                },
            });

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
            if (paraffin == null)
            {
                return;
            }

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

            if (timeline.usePostEffectExtraBlend)
            {
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
            }
            else
            {
                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.useAddInfo,
                    paraffin.useAdd,
                    newValue => paraffin.useAdd = newValue);
            }

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
            if (distanceFog == null)
            {
                return;
            }

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

            if (timeline.usePostEffectExtraBlend)
            {
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
            }
            else
            {
                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.useNormalInfo,
                    distanceFog.useNormal,
                    newValue => distanceFog.useNormal = newValue);
            }

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
            if (rimlight == null)
            {
                return;
            }

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

            if (timeline.usePostEffectExtraBlend)
            {
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
            }
            else
            {
                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.useAddInfo,
                    rimlight.useAdd,
                    newValue => rimlight.useAdd = newValue);
            }

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
