using System;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 瞳の位置・サイズ (EyesPosL/R・EyesScaL/R) の行描画。
    /// 表情ウィンドウの視線タブと TimelineItemInspector (瞳レイヤーの項目表示) で共有する。
    /// 値の読み書きは EyesTimelineLayer の静的メソッドを通し、レイヤーのキーと同じ換算にする。
    /// 書き込み先はどのスナップショットにも含まれない MaidCache のため履歴は記録しない
    /// (レイヤー側の UI も記録していない)。
    /// ドラッグ状態とテクスチャを持つため、描画するビューごとにインスタンスを分ける
    /// </summary>
    public class EyesPosRowDrawer
    {
        /// <summary>位置図の一辺 (px)。旧レイヤー編集ウィンドウと同じ大きさ</summary>
        private const int ImageSize = 150;
        private const float RowHeight = 20f;

        /// <summary>この Drawer が扱う項目。位置図に続けてスライダーを並べる順でもある</summary>
        public static readonly MTEP.MotionEyesType[] AllEyesTypes = new MTEP.MotionEyesType[]
        {
            MTEP.MotionEyesType.EyesPosL,
            MTEP.MotionEyesType.EyesPosR,
            MTEP.MotionEyesType.EyesScaL,
            MTEP.MotionEyesType.EyesScaR,
        };

        /// <summary>位置図でドラッグして動かす項目 (サイズは図では扱わない)</summary>
        private static readonly MTEP.MotionEyesType[] DraggableEyesTypes = new MTEP.MotionEyesType[]
        {
            MTEP.MotionEyesType.EyesPosL,
            MTEP.MotionEyesType.EyesPosR,
        };

        private static MTEP.Config config => MTEP.ConfigManager.instance.config;

        private static MTEP.StudioHackManager studioHackManager =>
            MTEP.StudioHackManager.instance;

        private Texture2D _eyesPositionTex = null;
        private Texture2D _eyesTex = null;
        private bool _isEyesDragging = false;

        /// <summary>この Drawer が扱う項目か (Inspector の項目種別の振り分けに使う)</summary>
        public static bool IsEyesPosType(MTEP.MotionEyesType eyesType)
        {
            return Array.IndexOf(AllEyesTypes, eyesType) >= 0;
        }

        private void InitTexture()
        {
            if (_eyesPositionTex == null)
            {
                _eyesPositionTex = new Texture2D(ImageSize, ImageSize);
                TextureUtils.ClearTexture(_eyesPositionTex, config.curveBgColor);

                var color1 = config.curveLineColor;
                var color2 = new Color(color1.r, color1.g, color1.b, color1.a * 0.5f);

                // 10x10のグリッドの描画
                for (var i = 1; i < 10; i++)
                {
                    var x = i * (ImageSize / 10);
                    TextureUtils.DrawLineTexture(_eyesPositionTex, x, 0, x, ImageSize, color2);
                    TextureUtils.DrawLineTexture(_eyesPositionTex, 0, x, ImageSize, x, color2);
                }

                // 円の描画
                TextureUtils.DrawCircleLineTexture(
                    _eyesPositionTex,
                    ImageSize / 2 - 0.5f,
                    64,
                    config.curveLineColor);
            }

            if (_eyesTex == null)
            {
                _eyesTex = TextureUtils.CreateCircleTexture(
                    config.frameWidth,
                    Color.white);
            }
        }

        /// <summary>
        /// 左右の瞳位置をドラッグで動かす位置図。旧レイヤー編集ウィンドウから移設。
        /// 図の右側に初期化ボタンを並べ、図の下端まで描画位置を進めてから戻る
        /// </summary>
        public void DrawEyesPosImage(GUIView view, MTEP.MaidCache maidCache)
        {
            if (maidCache == null)
            {
                return;
            }

            InitTexture();

            var basePos = view.currentPos;

            DrawEyesImage(view, maidCache, basePos);

            view.currentPos = basePos;
            view.currentPos.x += ImageSize + 10;

            if (view.DrawButton("初期化", 60, RowHeight))
            {
                foreach (var eyesType in AllEyesTypes)
                {
                    MTEP.EyesTimelineLayer.ApplyEyes(maidCache, eyesType, 0, 0);
                }
            }

            view.currentPos = basePos;
            view.currentPos.y += ImageSize;
        }

        /// <summary>瞳 1 つ分のスライダー 2 本 (位置は水平/垂直、サイズは幅/高さ)</summary>
        public void DrawEyesSliderRows(
            GUIView view, MTEP.MaidCache maidCache, MTEP.MotionEyesType eyesType)
        {
            if (maidCache == null)
            {
                return;
            }

            string[] names;
            switch (eyesType)
            {
                case MTEP.MotionEyesType.EyesPosL:
                case MTEP.MotionEyesType.EyesPosR:
                    names = new string[] { "水平", "垂直" };
                    break;
                case MTEP.MotionEyesType.EyesScaL:
                case MTEP.MotionEyesType.EyesScaR:
                    names = new string[] { "幅", "高さ" };
                    break;
                default:
                    return;
            }

            var eyesValue = MTEP.EyesTimelineLayer.GetEyesValue(maidCache, eyesType);
            var horizon = eyesValue.x;
            var vertical = eyesValue.y;
            var updateTransform = false;

            updateTransform |= DrawEyesSlider(view, names[0], horizon, x => horizon = x);
            updateTransform |= DrawEyesSlider(view, names[1], vertical, y => vertical = y);

            if (updateTransform)
            {
                MTEP.EyesTimelineLayer.ApplyEyes(maidCache, eyesType, horizon, vertical);
            }
        }

        /// <summary>項目名 (レイヤーの表示名) 付きでスライダーを描く</summary>
        public void DrawLabeledEyesSliderRows(
            GUIView view, MTEP.MaidCache maidCache, MTEP.MotionEyesType eyesType)
        {
            view.DrawLabel(
                MTEP.EyesTimelineLayer.EyesDisplayNameMap[eyesType.ToString()], 100, RowHeight);
            DrawEyesSliderRows(view, maidCache, eyesType);
        }

        private static bool DrawEyesSlider(
            GUIView view, string label, float value, Action<float> onChanged)
        {
            return view.DrawSliderValue(new GUIView.SliderOption
            {
                label = label,
                labelWidth = 30,
                min = -1f,
                max = 1f,
                step = 0.01f,
                defaultValue = 0f,
                value = value,
                onChanged = onChanged,
            });
        }

        /// <summary>位置図とその上の瞳マーカー。左瞳は図の座標と符号が逆になる</summary>
        private void DrawEyesImage(GUIView view, MTEP.MaidCache maidCache, Vector2 basePos)
        {
            var half = ImageSize / 2;

            view.DrawTexture(
                _eyesPositionTex,
                ImageSize,
                ImageSize,
                studioHackManager.isPoseEditing ? Color.white : Color.gray,
                EventType.MouseDrag,
                pos =>
                {
                    _isEyesDragging = true;

                    var horizon = (pos.x - half) / (float)half;
                    var vertical = (pos.y - half) / (float)half;

                    foreach (var eyesType in DraggableEyesTypes)
                    {
                        if (eyesType == MTEP.MotionEyesType.EyesPosL)
                        {
                            MTEP.EyesTimelineLayer.ApplyEyes(
                                maidCache, eyesType, -horizon, -vertical);
                        }
                        else
                        {
                            MTEP.EyesTimelineLayer.ApplyEyes(
                                maidCache, eyesType, horizon, vertical);
                        }
                    }
                });

            if (_isEyesDragging && !Input.GetMouseButton(0))
            {
                _isEyesDragging = false;
            }

            var halfEyesSize = _eyesTex.width / 2;

            foreach (var eyesType in DraggableEyesTypes)
            {
                var eyesValue = MTEP.EyesTimelineLayer.GetEyesValue(maidCache, eyesType);
                var horizon = eyesValue.x;
                var vertical = eyesValue.y;

                if (eyesType == MTEP.MotionEyesType.EyesPosL)
                {
                    horizon = -horizon;
                    vertical = -vertical;
                }

                var pos = new Vector2(half + horizon * half, half + vertical * half);
                pos.x = Mathf.Clamp(pos.x, 0, ImageSize);
                pos.y = Mathf.Clamp(pos.y, 0, ImageSize);

                view.currentPos = basePos + pos - new Vector2(halfEyesSize, halfEyesSize);
                view.DrawTexture(_eyesTex);
            }
        }
    }
}
