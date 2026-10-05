using UnityEngine;
using UnityEngine.Rendering;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>キャラの影の複製 (CharacterShadowProxyManager) の条件判定</summary>
    public static class CharacterShadowProxyRules
    {
        /// <summary>
        /// 複製を必要とするライトか。「背景のみ」でキャラを照らさないライトだけが対象で、
        /// ほかの照射対象ではキャラ本体が影を落とすので複製は要らない
        /// </summary>
        public static bool IsCasterLight(
            bool isLightActive, LightShadows shadows, int cullingMask, int characterMask, int shadowMask)
        {
            return isLightActive
                && shadows != LightShadows.None
                && LightTarget.HasCharacterShadow(cullingMask, shadowMask)
                && LightTarget.FromCullingMask(cullingMask, characterMask, shadowMask) == LightTargetMode.Background;
        }

        /// <summary>複製する部位か。顔 (Face7) のように元から影を落とさない部位は複製しない</summary>
        public static bool ShouldProxy(int layer, ShadowCastingMode castingMode, int characterMask)
        {
            return (characterMask & (1 << layer)) != 0 && castingMode != ShadowCastingMode.Off;
        }

        /// <summary>
        /// このカメラの描画で表示判定をするか。複製の影はカメラが影用レイヤーを描くときにしか落ちないので、
        /// 描かないカメラ (オーバーレイ用など) は判定を省く
        /// </summary>
        public static bool ShouldUseCamera(int cameraMask, int shadowMask)
        {
            return shadowMask != 0 && (cameraMask & shadowMask) != 0;
        }

        /// <summary>
        /// このカメラの描画中、複製に影を落とさせないか。元がこのカメラに描かれないなら、その影も消す。
        /// ViewCullingFilter のメイド非表示は元の Renderer を無効にし、PostEffects のメイド非表示は
        /// カメラの cullingMask からキャラ用レイヤーを外すので、どちらもここで拾える
        /// </summary>
        public static bool ShouldHideForCamera(
            bool isSourceAlive, bool isSourceEnabled, bool isSourceActive, int sourceLayer, int cameraMask)
        {
            if (!isSourceAlive || !isSourceEnabled || !isSourceActive)
            {
                return true;
            }
            return (cameraMask & (1 << sourceLayer)) == 0;
        }
    }
}
