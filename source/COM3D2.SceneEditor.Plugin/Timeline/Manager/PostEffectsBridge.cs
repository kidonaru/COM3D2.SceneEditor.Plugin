// PostEffects.Plugin は MTEUtils 一式を同梱しており、素の参照だと
// COM3D2.MotionTimelineEditor の拡張メソッドが多重定義になる。alias で隔離する
extern alias PostEffectsPlugin;
using System;
using System.Runtime.CompilerServices;
using PEP = PostEffectsPlugin::COM3D25.PostEffects.Plugin;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// COM3D25.PostEffects.Plugin への参照を隔離する唯一のクラス。
    /// 未導入環境では型ロードに失敗するため、PEP の型に触れるコードはすべて
    /// このクラス経由 (または isAvailable ガードの内側) に置くこと。
    /// isAvailable == false のときは他のメンバを呼んではならない。
    /// isAvailable の初回参照は PostEffects 側の初期化を誘発するため、
    /// プラグインロード完了後 (TimelineIntegration.Initialize 以降) に行うこと
    /// </summary>
    public static class PostEffectsBridge
    {
        private static bool? _isAvailable;

        /// <summary>PostEffects.Plugin が導入されているか。初回参照時に型ロードを試す</summary>
        public static bool isAvailable
        {
            get
            {
                if (_isAvailable == null)
                {
                    _isAvailable = CheckAvailable();
                }
                return _isAvailable.Value;
            }
        }

        // 型ロードを別メソッドに隔離し、JIT が isAvailable 自体のコンパイルで
        // PEP の型解決を要求しないようにする
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool CheckAvailable()
        {
            try
            {
                TouchBridge();
                return true;
            }
            catch (Exception e)
            {
                MTEUtils.LogWarning(
                    "PostEffects.Plugin が見つからないため、ポストエフェクトのタイムライン機能を無効化します: {0}",
                    e.GetType().Name);
                return false;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void TouchBridge()
        {
            // 定数参照だけでは型ロードを誘発できないため、静的メンバへ触れる。
            // paraffinCount の get は EffectSettings.instance の生成を伴う
            PEP.TimelineBridge.paraffinCount.GetHashCode();
        }
    }
}
