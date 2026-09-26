using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// デカール投影の箱の計算。Unity のネイティブ呼び出しを含めず単体テストできるようにする。
    /// 投影箱は root ローカルで X ±aspect.x/2・Y ±aspect.y/2・Z ±0.5 の箱で、
    /// root の拡縮・回転がそのまま箱に効く
    /// </summary>
    public static class PngDecalProjection
    {
        public const float DefaultFadeAngle = 80f;
        public const float MinFadeAngle = 1f;
        public const float MaxFadeAngle = 90f;

        /// <summary>Projector の範囲を箱より広げる量 (m)。箱の境界に接する面がカリングで欠けないようにする</summary>
        public const float CullMargin = 0.01f;

        /// <summary>拡縮の絶対値の下限。0 除算で Infinity にしないため</summary>
        public const float MinScale = 0.0001f;

        /// <summary>fadeAngle に対してフェードが始まる角度の比</summary>
        private const float FadeStartRatio = 2f / 3f;

        /// <summary>Projector と子 (PngDecal) に設定する値一式</summary>
        public struct ProjectorFrame
        {
            public float orthographicSize;
            public float aspectRatio;
            public float nearClipPlane;
            public float farClipPlane;
            /// <summary>子の root ローカル位置。箱の表側の面より少し手前</summary>
            public Vector3 localPosition;
            /// <summary>root の拡縮を打ち消す子の localScale。Projector をワールド単位で扱うため</summary>
            public Vector3 localScale;
        }

        /// <summary>長辺を 1、短辺を比率にした画像の縦横。PNG 板の Quad と同じ規則</summary>
        public static Vector2 GetAspect(int width, int height)
        {
            if (width <= 0 || height <= 0)
            {
                return Vector2.one;
            }

            float w = width;
            float h = height;
            var ratio = Mathf.Min(w, h) / Mathf.Max(w, h);
            return w >= h ? new Vector2(1f, ratio) : new Vector2(ratio, 1f);
        }

        /// <summary>
        /// 投影箱を覆う Projector の設定を求める。
        /// Projector は子に置き、root の拡縮を子の localScale で打ち消してワールド単位で設定する。
        /// 位置は root ローカル (root の拡縮で伸びる) のため、ワールドでの距離を拡縮で割り戻す。
        /// 負の拡縮でも「拡縮 × ローカル位置」の符号が打ち消し合い、常に表側 (root の +Z) に立つ
        /// </summary>
        public static ProjectorFrame ComputeFrame(Vector2 aspect, Vector3 rootScale)
        {
            var sx = SafeScale(rootScale.x);
            var sy = SafeScale(rootScale.y);
            var sz = SafeScale(rootScale.z);

            var halfWidth = aspect.x * Mathf.Abs(sx) * 0.5f + CullMargin;
            var halfHeight = aspect.y * Mathf.Abs(sy) * 0.5f + CullMargin;
            var depth = Mathf.Abs(sz);

            return new ProjectorFrame
            {
                orthographicSize = halfHeight,
                aspectRatio = halfWidth / halfHeight,
                nearClipPlane = CullMargin * 0.5f,
                farClipPlane = depth + CullMargin * 2f,
                localPosition = new Vector3(0f, 0f, (depth * 0.5f + CullMargin) / sz),
                localScale = new Vector3(1f / sx, 1f / sy, 1f / sz),
            };
        }

        /// <summary>ワールド座標 → 投影箱の座標 (各軸 -0.5〜0.5) の行列</summary>
        public static Matrix4x4 ComputeDecalMatrix(Matrix4x4 rootWorldToLocal, Vector2 aspect)
        {
            var normalize = Matrix4x4.identity;
            normalize.m00 = 1f / Mathf.Max(aspect.x, MinScale);
            normalize.m11 = 1f / Mathf.Max(aspect.y, MinScale);
            return normalize * rootWorldToLocal;
        }

        /// <summary>
        /// 投影箱の 8 頂点をワールド座標で返す。
        /// 並びは PluginUtils.GetBoundsCorners と同じ (bit0=X, bit1=Y, bit2=Z) で、同じ辺の表を使える
        /// </summary>
        public static void GetBoxCorners(Matrix4x4 rootLocalToWorld, Vector2 aspect, Vector3[] corners)
        {
            var hx = aspect.x * 0.5f;
            var hy = aspect.y * 0.5f;
            const float hz = 0.5f;
            for (var i = 0; i < 8; i++)
            {
                var local = new Vector3(
                    (i & 1) == 0 ? -hx : hx,
                    (i & 2) == 0 ? -hy : hy,
                    (i & 4) == 0 ? -hz : hz);
                corners[i] = rootLocalToWorld.MultiplyPoint3x4(local);
            }
        }

        /// <summary>0 度では smoothstep の両端が一致するため下限を設ける</summary>
        public static float ClampFadeAngle(float fadeAngle)
        {
            return Mathf.Clamp(fadeAngle, MinFadeAngle, MaxFadeAngle);
        }

        /// <summary>
        /// 角度フェードの cos 範囲。面の法線と投影元の向きのなす角が
        /// fadeAngle 以上で不透明度 0 (x)、fadeAngle × 2/3 以下で 1 (y)
        /// </summary>
        public static Vector2 GetFadeCosRange(float fadeAngle)
        {
            var angle = ClampFadeAngle(fadeAngle) * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(angle), Mathf.Cos(angle * FadeStartRatio));
        }

        private static float SafeScale(float scale)
        {
            if (Mathf.Abs(scale) >= MinScale)
            {
                return scale;
            }
            return scale < 0f ? -MinScale : MinScale;
        }
    }
}
