using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 正規化タンジェント曲線 (t は 0..1) をテクスチャへ CPU 描画するヘルパー。
    /// Inspector の補間曲線 (KeyFrameTangentDrawer) と
    /// タイムラインカーブエディタのプリセットサムネ (TimelineCurveEditor) で共有する
    /// </summary>
    public static class TangentCurveTexture
    {
        /// <summary>基準線 (値 0/1 と区間の始点/終点で囲む枠) の色</summary>
        private static readonly Color GuideFrameColor = new Color(1f, 1f, 1f, 0.35f);
        /// <summary>線形補間 (勾配 1) を示す対角線の色</summary>
        private static readonly Color GuideLinearColor = new Color(1f, 1f, 1f, 0.25f);
        /// <summary>対角線の破線パターン (この px 数ごとに描画と空白を切り替える)</summary>
        private const int GuideDashLength = 4;

        /// <summary>プリセットサムネの線幅 (40px 程度では 1px だと細くて形が読めない)</summary>
        private const int PresetLineWidth = 3;
        /// <summary>プリセットサムネの内側余白 (小さいので控えめに)</summary>
        private const int PresetPadding = 3;

        /// <summary>
        /// 曲線を読む基準線を描く。内側領域の枠 (下辺=値 0 / 上辺=値 1 /
        /// 左辺=区間の始点 / 右辺=終点) と、線形補間を示す対角線 (破線)。
        /// 曲線より先に描いて背面に置く。
        /// GPU への転送をまとめるため Apply は呼び出し側で行う
        /// </summary>
        public static void DrawGuideLines(Texture2D texture, int padding)
        {
            var size = texture.width;
            // 曲線と同じ写像で端を求め、枠と曲線がずれないようにする
            var min = KeyFrameTangentLogic.ValueToTextureY(0f, size, padding);
            var max = KeyFrameTangentLogic.ValueToTextureY(1f, size, padding);

            // 対角線は内側領域が正方形であることを使い、x と y に同じ値を使う。
            // 曲線と紛れないよう GuideDashLength px ごとに描画と空白を切り替える。
            // 枠より先に描いて、重なる角は枠の色を残す
            for (var i = min; i <= max; i++)
            {
                var dashSegment = (i - min) / GuideDashLength;
                if (dashSegment % 2 == 0)
                {
                    texture.SetPixel(i, i, GuideLinearColor);
                }
            }

            for (var i = min; i <= max; i++)
            {
                texture.SetPixel(i, min, GuideFrameColor);
                texture.SetPixel(i, max, GuideFrameColor);
                texture.SetPixel(min, i, GuideFrameColor);
                texture.SetPixel(max, i, GuideFrameColor);
            }
        }

        /// <summary>
        /// 正規化タンジェント形状 (t は 0..1) の Hermite 曲線をテクスチャへ描く
        /// (MTE KeyFrameUI.UpdateTangentTexture の移植)。
        /// padding px は四辺の余白として空け、内側領域だけに描く。
        /// 複数の曲線を重ねられるよう Apply は呼び出し側で行う
        /// </summary>
        public static void DrawCurve(
            Texture2D texture,
            float outTangent,
            float inTangent,
            Color lineColor,
            int lineWidth,
            int padding)
        {
            var size = texture.width;
            var halfLineWidth = lineWidth / 2;
            var innerWidth = size - padding * 2;
            // 最終列で t=1 に到達させ、曲線の終端を in ハンドルの原点へ合わせる
            var lastX = Mathf.Max(1, innerWidth - 1);

            for (var x = 0; x < innerWidth; x++)
            {
                var t = x / (float)lastX;
                var value = MTEP.PluginUtils.HermiteSimplified(outTangent, inTangent, t);
                var y = KeyFrameTangentLogic.ValueToTextureY(value, size, padding) - halfLineWidth;

                for (var i = 0; i < lineWidth; i++)
                {
                    var yy = Mathf.Clamp(y + i, padding, size - 1 - padding);
                    texture.SetPixel(padding + x, yy, lineColor);
                }
            }
        }

        /// <summary>
        /// プリセットサムネを作る。添字は TangentType の値と一致する
        /// (自動補間 (Smooth) は固定形状を持たないため除く)
        /// </summary>
        public static Texture2D[] CreatePresetTextures(int size, Color bgColor, Color lineColor)
        {
            var textures = new Texture2D[(int)MTEP.TangentType.Smooth];
            for (var i = 0; i < textures.Length; i++)
            {
                var texture = new Texture2D(size, size);
                textures[i] = texture;
                TextureUtils.ClearTexture(texture, bgColor);
                var pair = MTEP.TangentPair.GetDefault((MTEP.TangentType)i);
                DrawCurve(
                    texture, pair.outTangent, pair.inTangent, lineColor,
                    lineWidth: PresetLineWidth, padding: PresetPadding);
                texture.Apply();
            }
            return textures;
        }
    }
}
