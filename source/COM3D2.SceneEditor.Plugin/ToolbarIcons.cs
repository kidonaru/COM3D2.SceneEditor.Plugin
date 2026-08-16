using System;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// ツールバーや UI 部品で使うアイコンテクスチャ。
    /// 画像は assets/icons/*.svg を generate.js で 32x32 の PNG へラスタライズしたもの。
    /// 形を変えるときは SVG を直して再生成し、下の base64 を貼り替えること
    /// </summary>
    public static class ToolbarIcons
    {
        public enum Kind
        {
            // 背景表示 (山と太陽)
            Bg,
            // メイド表示 (人物シルエット)
            Maid,
            // ギズモ表示 (3 軸の移動矢印)
            Gizmo,
            // 平行投影 (立方体)
            Ortho,
            // 切り替え (左右逆向きの矢印)
            Change,
            // XYZ連動 (鎖)
            Link,
            // シーンプリセット自動ロード指定 (家の輪郭線)。ON 状態は色を乗算して表す
            Home,
            // 選択対象へフォーカス (四隅のブラケットと中央の点)
            Focus,
        }

        // 32x32 PNG (base64)。添字は Kind と対応させること
        private static readonly string[] PNG_BASE64 =
        {
            "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",
        };

        private static readonly Texture2D[] _textures = new Texture2D[PNG_BASE64.Length];
        // 毎フレーム呼ばれるため、失敗も記録して再デコード・再ログを 1 回に抑える
        private static readonly bool[] _failed = new bool[PNG_BASE64.Length];

        /// <summary>アイコンテクスチャを取得する。読み込めなければ null</summary>
        public static Texture2D GetTexture(Kind kind)
        {
            var index = (int)kind;
            if (_textures[index] == null && !_failed[index])
            {
                _textures[index] = CreateTexture(PNG_BASE64[index]);
                _failed[index] = _textures[index] == null;
            }
            return _textures[index];
        }

        /// <summary>
        /// アイコンに色を乗算したテクスチャを生成する。読み込めなければ null。
        /// 呼び出しごとに新しいテクスチャを作るため、結果は呼び出し側でキャッシュすること
        /// </summary>
        public static Texture2D CreateTintedTexture(Kind kind, Color tint)
        {
            var source = GetTexture(kind);
            if (source == null)
            {
                return null;
            }

            // LoadImage で作ったテクスチャは読み取り可能なので、そのまま画素を取り出せる
            var pixels = source.GetPixels();
            for (var i = 0; i < pixels.Length; i++)
            {
                pixels[i] *= tint;
            }

            var texture = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        private static Texture2D CreateTexture(string base64)
        {
            // 幅・高さと形式は LoadImage が PNG に合わせて作り直す
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);

            try
            {
                if (texture.LoadImage(Convert.FromBase64String(base64)))
                {
                    return texture;
                }
                MTEUtils.LogError("ツールバーアイコンの画像を読み込めませんでした");
            }
            catch (Exception e)
            {
                // 読み込めなくても機能自体は動くので、呼び出し側でテキスト表示へフォールバックする
                MTEUtils.LogError("ツールバーアイコンの画像の展開に失敗しました");
                MTEUtils.LogException(e);
            }

            UnityEngine.Object.Destroy(texture);
            return null;
        }
    }
}
