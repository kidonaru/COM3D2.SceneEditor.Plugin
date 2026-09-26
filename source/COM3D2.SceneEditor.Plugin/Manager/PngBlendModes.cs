using UnityEngine.Rendering;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// PNG 配置 (板・デカール共通) のブレンド方式。値はシーンプリセットとタイムライン XML に保存され、
    /// シェーダー SE/PngBoard・SE/Decal の _BlendMode 判定とも対応する
    /// </summary>
    public enum PngBlendMode
    {
        Normal = 0,
        Multiply = 1,
        Additive = 2,
        /// <summary>下地の色を読んで合成する。GrabPass を持つ専用シェーダーへ差し替えて描く</summary>
        Overlay = 3,
    }

    /// <summary>ブレンド方式からマテリアルへ渡す値を決める純粋関数群</summary>
    public static class PngBlendModes
    {
        public static void GetBlendFactors(PngBlendMode mode, out BlendMode src, out BlendMode dst)
        {
            switch (mode)
            {
                case PngBlendMode.Multiply:
                    // シェーダーが透明部を白 (変化なし) へ寄せた色を出す
                    src = BlendMode.DstColor;
                    dst = BlendMode.Zero;
                    break;
                case PngBlendMode.Additive:
                    src = BlendMode.SrcAlpha;
                    dst = BlendMode.One;
                    break;
                default:
                    // 通常と、シェーダー内で下地と合成済みのオーバーレイ
                    src = BlendMode.SrcAlpha;
                    dst = BlendMode.OneMinusSrcAlpha;
                    break;
            }
        }

        /// <summary>下地を GrabPass で読むシェーダーが要るか</summary>
        public static bool UsesGrab(PngBlendMode mode)
        {
            return mode == PngBlendMode.Overlay;
        }

        /// <summary>
        /// 実際に描くブレンド方式。オーバーレイ用シェーダーが無いときは通常で描く
        /// (設定値そのものは変えず、保存もオーバーレイのまま)
        /// </summary>
        public static PngBlendMode ResolveRenderMode(PngBlendMode mode, bool hasGrabShader)
        {
            return UsesGrab(mode) && !hasGrabShader ? PngBlendMode.Normal : mode;
        }
    }
}
