using System;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 「背景のみ」のライトでキャラの影を落とすための影用レイヤー。
    /// ライトの cullingMask から外したオブジェクトはそのライトの影も落とさないため、
    /// 影専用の複製をこのレイヤーに置き、ライトの cullingMask にこのビットを入れるかどうかで影の有無を切り替える。
    /// 複製の影はカメラの cullingMask にも入っていないと落ちないため、メインカメラが描くレイヤーから選ぶ
    /// </summary>
    public static class CharacterShadowLayer
    {
        public const int None = -1;

        private static bool _resolved = false;
        private static int _layer = None;

        /// <summary>影用レイヤーの番号。メインカメラが取れるまでは未解決のまま None を返し、次の呼び出しで再試行する</summary>
        public static int layer
        {
            get
            {
                Resolve();
                return _layer;
            }
        }

        public static int mask => layer == None ? 0 : 1 << layer;

        public static bool isAvailable => layer != None;

        private static void Resolve()
        {
            if (_resolved)
            {
                return;
            }

            var gameMain = GameMain.Instance;
            var cameraMain = gameMain != null ? gameMain.MainCamera : null;
            var camera = cameraMain != null ? cameraMain.camera : null;
            if (camera == null)
            {
                return;
            }

            _resolved = true;
            _layer = Select(LayerMask.LayerToName, camera.cullingMask, LightTarget.CharacterMask);
            if (_layer == None)
            {
                MTEUtils.LogError("キャラの影に使える空きレイヤーが見つからないため、「背景のみ」のライトでキャラの影を落とす機能を無効にします");
            }
            else
            {
                MTEUtils.LogDebug("キャラの影の複製にレイヤー {0} を使います", _layer);
            }
        }

        /// <summary>
        /// 名前の無いレイヤーのうち、カメラに映り、キャラ用でないものの最小番号を返す。該当が無ければ None。
        /// SceneEditor・PostEffects ともに名前の無いレイヤーは使っていない
        /// </summary>
        public static int Select(Func<int, string> layerToName, int cameraMask, int characterMask)
        {
            for (var layer = 0; layer < 32; layer++)
            {
                var bit = 1 << layer;
                if ((cameraMask & bit) == 0 || (characterMask & bit) != 0)
                {
                    continue;
                }
                if (string.IsNullOrEmpty(layerToName(layer)))
                {
                    return layer;
                }
            }
            return None;
        }
    }
}
