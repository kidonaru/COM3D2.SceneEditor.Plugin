using System;
using COM3D2.MotionTimelineEditor;
using HarmonyLib;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// メイドスケールを TBody.LateUpdate の前後で戻す・掛ける。掛ける位置の理由は MaidScaleController 参照。
    /// TBody.OnLateUpdate などのイベントは 2.0 / 2.5 で型と寿命が違うため使わない
    /// </summary>
    public static class MaidScaleLateUpdatePatch
    {
        // Harmony インスタンスは他のパッチと独立させ、有効・無効判定を他パッチの状態から切り離す
        private static Harmony _harmony = null;

        /// <summary>
        /// パッチを適用する。プラグイン初期化から 1 回だけ呼ばれるが、
        /// 二重パッチは Harmony の例外になるため保険を残す
        /// </summary>
        public static void Init()
        {
            if (_harmony != null)
            {
                return;
            }

            try
            {
                var original = AccessTools.Method(typeof(TBody), "LateUpdate");
                if (original == null)
                {
                    throw new Exception("TBody.LateUpdate が見つかりません");
                }

                var prefix = AccessTools.Method(typeof(MaidScaleLateUpdatePatch), nameof(LateUpdatePrefix));
                var postfix = AccessTools.Method(typeof(MaidScaleLateUpdatePatch), nameof(LateUpdatePostfix));

                _harmony = new Harmony(PluginInfo.PluginFullName + ".MaidScale");
                _harmony.Patch(original, prefix: new HarmonyMethod(prefix), postfix: new HarmonyMethod(postfix));
                MTEUtils.LogDebug("TBody.LateUpdate のフックに成功しました");
            }
            catch (Exception e)
            {
                // 失敗してもゲームは通常どおり動く。メイドスケールの見た目だけが効かない
                MTEUtils.LogError("TBody.LateUpdate のフックに失敗しました。メイドスケールは無効です");
                MTEUtils.LogException(e);
                _harmony = null;
            }
        }

        private static MaidScaleController controller
        {
            get
            {
                var manager = MaidManipulateManager.instance;
                return manager != null ? manager.maidScaleController : null;
            }
        }

        /// <summary>毎フレーム全メイド分走るため、例外は握り潰してゲーム側の更新を止めない</summary>
        private static void LateUpdatePrefix(TBody __instance)
        {
            try
            {
                var scaleController = controller;
                if (scaleController != null)
                {
                    scaleController.OnBodyLateUpdateBegin(__instance);
                }
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
            }
        }

        private static void LateUpdatePostfix(TBody __instance)
        {
            try
            {
                // プラグインを無効にしている間は掛けない (戻す処理は prefix と Update で動き続ける)
                if (!ConfigManager.instance.config.pluginEnabled)
                {
                    return;
                }
                var scaleController = controller;
                if (scaleController != null)
                {
                    scaleController.OnBodyLateUpdateEnd(__instance);
                }
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
            }
        }
    }
}
