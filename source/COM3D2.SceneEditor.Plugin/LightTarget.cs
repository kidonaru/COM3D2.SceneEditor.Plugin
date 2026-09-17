using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>追加ライトが照らす対象。値はタイムラインキー・プリセットにそのまま保存される</summary>
    public enum LightTargetMode
    {
        All = 0,
        Character = 1,
        Background = 2,
    }

    /// <summary>
    /// 照射対象モードと Light.cullingMask の相互変換。
    /// ゲーム側はメイドを Charactor / Face、男を Man レイヤーに置くため、この 3 つをキャラ用とみなす
    /// （CharaDirectionalLight が Charactor だけを照らすのと同じ仕組み）
    /// </summary>
    public static class LightTarget
    {
        private static readonly string[] CharacterLayerNames = { "Charactor", "Face", "Man" };

        private static int _characterMask = 0;

        /// <summary>キャラ用レイヤーのマスク。レイヤー名の解決は初回だけ行う</summary>
        public static int CharacterMask
        {
            get
            {
                if (_characterMask == 0)
                {
                    foreach (var layerName in CharacterLayerNames)
                    {
                        var layer = LayerMask.NameToLayer(layerName);
                        if (layer >= 0)
                        {
                            _characterMask |= 1 << layer;
                        }
                    }
                }
                return _characterMask;
            }
        }

        public static int ToCullingMask(LightTargetMode mode) => ToCullingMask(mode, CharacterMask);

        public static LightTargetMode FromCullingMask(int cullingMask)
            => FromCullingMask(cullingMask, CharacterMask);

        /// <summary>characterMask を注入する版（テスト用）</summary>
        public static int ToCullingMask(LightTargetMode mode, int characterMask)
        {
            switch (mode)
            {
                case LightTargetMode.Character:
                    return characterMask;
                case LightTargetMode.Background:
                    return ~characterMask;
                default:
                    return -1;
            }
        }

        /// <summary>
        /// 実マスクからモードへ逆引きする。
        /// 本プラグイン以外が書いたマスクは判別できないため「全て」として扱う
        /// </summary>
        public static LightTargetMode FromCullingMask(int cullingMask, int characterMask)
        {
            if (cullingMask == characterMask)
            {
                return LightTargetMode.Character;
            }
            if (cullingMask == ~characterMask)
            {
                return LightTargetMode.Background;
            }
            return LightTargetMode.All;
        }

        /// <summary>XML 等から読んだ整数を有効なモードへ丸める。範囲外は「全て」</summary>
        public static LightTargetMode ClampMode(int value)
        {
            return value == (int)LightTargetMode.Character || value == (int)LightTargetMode.Background
                ? (LightTargetMode)value
                : LightTargetMode.All;
        }
    }
}
