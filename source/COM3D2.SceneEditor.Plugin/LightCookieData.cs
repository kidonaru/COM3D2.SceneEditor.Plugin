using System;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>スポットライトの輪郭 (cookie) の決め方。数値は XML に保存するので変えないこと</summary>
    public enum LightCookieMode
    {
        // Unity 内蔵の減衰 (cookie なし)
        Default = 0,
        // 硬さから円形の cookie を生成する
        Generated = 1,
        // PNG 配置と同じ Config/PngPlacement の PNG を使う
        Image = 2,
    }

    /// <summary>
    /// 追加ライト 1 灯分の輪郭設定。シーンプリセット・Undo・クリップボードでは一式を受け渡す。
    /// タイムラインでは種類と画像をライト定義、硬さをライトキー (index 19) で持つ
    /// </summary>
    public struct LightCookieData : IEquatable<LightCookieData>
    {
        public const float DefaultHardness = 0.8f;

        public LightCookieMode mode;
        /// <summary>0 で中心から滑らかに減衰、1 で縁だけぼかす</summary>
        public float hardness;
        /// <summary>PNG 配置の画像フォルダ (Config/PngPlacement) からの相対パス</summary>
        public string image;

        public static LightCookieData Default => new LightCookieData
        {
            mode = LightCookieMode.Default,
            hardness = DefaultHardness,
            image = "",
        };

        /// <summary>XML など外部入力由来の値を扱える範囲へ丸める</summary>
        public LightCookieData Normalized()
        {
            return new LightCookieData
            {
                mode = Enum.IsDefined(typeof(LightCookieMode), mode) ? mode : LightCookieMode.Default,
                hardness = float.IsNaN(hardness) ? DefaultHardness : Math.Max(0f, Math.Min(1f, hardness)),
                image = image ?? "",
            };
        }

        /// <summary>
        /// タイムラインのライト定義として同じか。硬さはライトキーで補間されるので比べない
        /// (比べると再生中に毎回定義の同期が走る)
        /// </summary>
        public bool EqualsIgnoringHardness(LightCookieData other)
        {
            return mode == other.mode
                && string.Equals(image ?? "", other.image ?? "", StringComparison.Ordinal);
        }

        public bool Equals(LightCookieData other)
        {
            return mode == other.mode
                && hardness == other.hardness
                && string.Equals(image ?? "", other.image ?? "", StringComparison.Ordinal);
        }

        public override bool Equals(object obj) => obj is LightCookieData other && Equals(other);

        public override int GetHashCode()
        {
            return ((int)mode * 397) ^ hardness.GetHashCode() ^ (image ?? "").GetHashCode();
        }
    }
}
