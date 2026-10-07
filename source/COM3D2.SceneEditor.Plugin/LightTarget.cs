using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>追加ライトが照らす対象。値はタイムラインキー・プリセットにそのまま保存される</summary>
    public enum LightTargetMode
    {
        All = 0,
        Character = 1,
        Background = 2,
        /// <summary>メイドの顔 (head スロット) だけ。照らしている間は MaidFaceRendererManager が顔を Face レイヤーへ移す</summary>
        Face = 3,
        /// <summary>キャラのうち顔以外 (男は全身)</summary>
        Body = 4,
    }

    /// <summary>
    /// 照射対象モードと Light.cullingMask の相互変換。
    /// ゲーム側はメイドを Charactor / Face、男を Man レイヤーに置くため、この 3 つをキャラ用とみなす
    /// （CharaDirectionalLight が Charactor だけを照らすのと同じ仕組み）。
    /// 顔のみ／体のみは Face レイヤーで顔と体を分ける。ゲームは通常顔も Charactor に置くため、
    /// そのモードのライトがある間だけ MaidFaceRendererManager が顔を Face へ移す。
    /// 影用レイヤーのビット (CharacterShadowLayer) で、背景のみのライトでもキャラの影を落とすかを表す
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

        private static int _faceMask = -1;

        /// <summary>Face レイヤーのビット。レイヤーが無ければ 0</summary>
        public static int FaceMask
        {
            get
            {
                if (_faceMask < 0)
                {
                    var layer = LayerMask.NameToLayer("Face");
                    _faceMask = layer >= 0 ? 1 << layer : 0;
                }
                return _faceMask;
            }
        }

        /// <summary>
        /// 照射対象とキャラの影の設定から cullingMask を組み立てる。
        /// 影ビットは照射対象と独立に持つ。照射対象はタイムラインのキーで切り替わるため、
        /// 「背景のみ」以外でも残しておかないと、背景のみへ戻したときに設定が失われる
        /// </summary>
        public static int ToCullingMask(LightTargetMode mode, bool characterShadow)
            => ToCullingMask(mode, characterShadow, CharacterMask, FaceMask, CharacterShadowLayer.mask);

        public static LightTargetMode FromCullingMask(int cullingMask)
            => FromCullingMask(cullingMask, CharacterMask, FaceMask, CharacterShadowLayer.mask);

        public static bool HasCharacterShadow(int cullingMask)
            => HasCharacterShadow(cullingMask, CharacterShadowLayer.mask);

        /// <summary>照射対象は変えずに、キャラの影の設定だけを書き換える</summary>
        public static int WithCharacterShadow(int cullingMask, bool characterShadow)
            => WithCharacterShadow(cullingMask, characterShadow, CharacterShadowLayer.mask);

        /// <summary>characterMask・faceMask・影ビットを注入する版（テスト用）</summary>
        public static int ToCullingMask(
            LightTargetMode mode, bool characterShadow, int characterMask, int faceMask, int shadowMask)
        {
            int cullingMask;
            switch (mode)
            {
                case LightTargetMode.Character:
                    cullingMask = characterMask;
                    break;
                case LightTargetMode.Background:
                    cullingMask = ~characterMask;
                    break;
                case LightTargetMode.Face:
                    cullingMask = faceMask;
                    break;
                case LightTargetMode.Body:
                    cullingMask = characterMask & ~faceMask;
                    break;
                default:
                    cullingMask = -1;
                    break;
            }
            return WithCharacterShadow(cullingMask, characterShadow, shadowMask);
        }

        /// <summary>
        /// 実マスクからモードへ逆引きする。影ビットを立てた状態にそろえてから比べる。
        /// 本プラグイン以外が書いたマスクは判別できないため「全て」として扱う
        /// </summary>
        public static LightTargetMode FromCullingMask(int cullingMask, int characterMask, int faceMask, int shadowMask)
        {
            var normalized = cullingMask | shadowMask;
            if (normalized == (characterMask | shadowMask))
            {
                return LightTargetMode.Character;
            }
            // 影用レイヤーはキャラ用でないレイヤーから選ぶので、~characterMask は影ビットを含む
            if (normalized == ~characterMask)
            {
                return LightTargetMode.Background;
            }
            if (normalized == (faceMask | shadowMask))
            {
                return LightTargetMode.Face;
            }
            if (normalized == ((characterMask & ~faceMask) | shadowMask))
            {
                return LightTargetMode.Body;
            }
            return LightTargetMode.All;
        }

        /// <summary>
        /// 照らさないキャラの影を影専用の複製で落とせるモードか。
        /// 「キャラのみ」「全て」はキャラ全体を照らすので、本体がそのまま影を落とす
        /// </summary>
        public static bool UsesCharacterShadow(LightTargetMode mode)
        {
            return mode == LightTargetMode.Background || mode == LightTargetMode.Face || mode == LightTargetMode.Body;
        }

        /// <summary>顔を Face レイヤーへ移さないと照らし分けられないモードか</summary>
        public static bool UsesFaceLayer(LightTargetMode mode)
        {
            return mode == LightTargetMode.Face || mode == LightTargetMode.Body;
        }

        /// <summary>影ビットが立っているか。影用レイヤーが無い (shadowMask == 0) ときは常に false</summary>
        public static bool HasCharacterShadow(int cullingMask, int shadowMask)
        {
            return shadowMask != 0 && (cullingMask & shadowMask) == shadowMask;
        }

        public static int WithCharacterShadow(int cullingMask, bool characterShadow, int shadowMask)
        {
            return characterShadow ? cullingMask | shadowMask : cullingMask & ~shadowMask;
        }

        /// <summary>XML 等から読んだ整数を有効なモードへ丸める。範囲外は「全て」</summary>
        public static LightTargetMode ClampMode(int value)
        {
            return value >= (int)LightTargetMode.All && value <= (int)LightTargetMode.Body
                ? (LightTargetMode)value
                : LightTargetMode.All;
        }
    }
}
