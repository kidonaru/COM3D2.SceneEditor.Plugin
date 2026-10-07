using System.Collections.Generic;
using UnityEngine.Rendering;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>顔 (head スロット) の Renderer に書く値の決め方 (MaidFaceRendererManager の判定部分)</summary>
    public static class FaceRendererRules
    {
        /// <summary>
        /// 書くレイヤー。Charactor にある顔だけを Face へ移す。
        /// VR の一人称モードでゲームが Face へ移した顔や、他プラグインが変えたレイヤーは元のまま
        /// </summary>
        public static int ResolveLayer(int originalLayer, bool moveToFace, int charactorLayer, int faceLayer)
        {
            return moveToFace && originalLayer == charactorLayer ? faceLayer : originalLayer;
        }

        public static bool ResolveReceive(bool original, IDictionary<string, bool> overrides)
        {
            bool value;
            return overrides.TryGetValue(FaceShadowItems.Receive, out value) ? value : original;
        }

        public static ShadowCastingMode ResolveCast(ShadowCastingMode original, IDictionary<string, bool> overrides)
        {
            bool value;
            if (!overrides.TryGetValue(FaceShadowItems.Cast, out value))
            {
                return original;
            }
            return value ? ShadowCastingMode.On : ShadowCastingMode.Off;
        }

        /// <summary>このライトのために顔を Face レイヤーへ移す必要があるか</summary>
        public static bool NeedsFaceLayer(bool isLightActive, LightTargetMode mode)
        {
            return isLightActive && LightTarget.UsesFaceLayer(mode);
        }

        /// <summary>
        /// 顔を移している間、Face ビットを足すライトか。Charactor を照らすのに Face を照らさないライト
        /// (ゲームの CharaDirectionalLight など) は、移した顔を照らさなくなるため
        /// </summary>
        public static bool NeedsLightCompensation(int cullingMask, int charactorMask, int faceMask)
        {
            return faceMask != 0 && (cullingMask & charactorMask) != 0 && (cullingMask & faceMask) == 0;
        }

        /// <summary>上書きも移動も無く、Renderer をゲームの値へ戻してよいか</summary>
        public static bool IsDefault(IDictionary<string, bool> overrides, bool moveToFace)
        {
            return overrides.Count == 0 && !moveToFace;
        }

        /// <summary>
        /// 書き込むか。決めた値が前回書いた値から変わったときだけ書く。
        /// 毎フレーム実体と比べて書くと、他者 (VR の LayerCheck・他プラグイン) が後から変えた値を取り消し続ける
        /// </summary>
        public static bool ShouldWrite<T>(T desired, T lastWritten)
        {
            return !EqualityComparer<T>.Default.Equals(desired, lastWritten);
        }

        /// <summary>戻す値。今の値が前回書いた値のままなら元の値、他者が書き換えていればその値を残す</summary>
        public static T ResolveRestore<T>(T current, T lastWritten, T original)
        {
            return EqualityComparer<T>.Default.Equals(current, lastWritten) ? original : current;
        }
    }
}
