using System;
using COM3D2.MotionTimelineEditor;
using HarmonyLib;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// GameView モード中のカメラ操作入力を本プラグインが仲裁する。
    /// 対象は CameraMain.SetControl (回転・移動の可否) と Input.ResetInputAxes (ホイール拡縮潰し) の 2 つ。
    ///
    /// InputRemapper が GameView 内で Input.mousePosition を RT 座標へ書き換えるため、
    /// スクリーン座標前提で「自ウィンドウ上か」を判定する他プラグイン (例: ModItemExplorer) は、
    /// カーソルが GameView 内にあるだけで自ウィンドウ上と誤判定し、SetControl(false) を
    /// 毎フレーム呼び続けることがある。毎フレーム SetControl(true) で書き戻す方式は
    /// UltimateOrbitCamera が自身の Update で mouseControl を読む都合上、
    /// スクリプト実行順に依存して不安定なので、呼び出し自体を prefix で抑止する。
    ///
    /// 抑止するのはカーソルが GameView 描画領域内 (リサイズつかみ範囲・他プラグインの
    /// ウィンドウ上を除く) にある間の「有効 → 無効」への変更だけ。
    /// 無効化済みの状態 (フェード等でゲーム本体が先に無効化したケース) や
    /// 有効へ戻す呼び出しはそのまま通す。
    ///
    /// 既知の副作用: 呼び出し元は区別できないため、カーソルが GameView 内にある瞬間に
    /// ゲーム本体が正当に行う「有効 → 無効」への変更 (ADV のカメラロック等) も抑止される。
    /// 実害はその間ドラッグでカメラが動く程度に留まる
    /// </summary>
    public static class CameraControlArbiter
    {
        // 本プラグイン自身の SetControl 呼び出し中フラグ (仲裁を素通しさせる)
        private static bool _applying = false;

        // フックに失敗した場合は仲裁なし (従来挙動) へフォールバックする
        public static bool isEnabled { get; private set; }

        public static void Patch(Harmony harmony)
        {
            // 呼び出し側にも二重パッチのガードがあるが、Harmony の例外になるため保険を残す
            if (isEnabled)
            {
                return;
            }

            try
            {
                var original = AccessTools.Method(typeof(CameraMain), nameof(CameraMain.SetControl));
                if (original == null)
                {
                    throw new MissingMethodException(typeof(CameraMain).FullName, nameof(CameraMain.SetControl));
                }

                var prefix = new HarmonyMethod(AccessTools.Method(typeof(CameraControlArbiter), nameof(SetControlPrefix)));
                harmony.Patch(original, prefix: prefix);
                isEnabled = true;
                MTEUtils.Log("CameraMain.SetControl のフックに成功しました");
            }
            catch (Exception e)
            {
                // 失敗しても GameView 自体は動作する。他プラグインとの競合時にカメラ操作が奪われるだけ
                MTEUtils.LogError("CameraMain.SetControl のフックに失敗しました。GameView内のカメラ操作が他プラグインに無効化される場合があります");
                MTEUtils.LogException(e);
            }

            try
            {
                var original = AccessTools.Method(typeof(UnityEngine.Input), nameof(UnityEngine.Input.ResetInputAxes));
                if (original == null)
                {
                    throw new MissingMethodException(typeof(UnityEngine.Input).FullName, nameof(UnityEngine.Input.ResetInputAxes));
                }

                var prefix = new HarmonyMethod(AccessTools.Method(typeof(CameraControlArbiter), nameof(ResetInputAxesPrefix)));
                harmony.Patch(original, prefix: prefix);
                MTEUtils.Log("Input.ResetInputAxes のフックに成功しました");
            }
            catch (Exception e)
            {
                // externメソッドのパッチはランタイム依存。失敗時は GameView 内のホイール拡縮だけが他プラグインに潰されうる
                MTEUtils.LogError("Input.ResetInputAxes のフックに失敗しました。GameView内のホイール拡縮が他プラグインに無効化される場合があります");
                MTEUtils.LogException(e);
            }
        }

        /// <summary>
        /// 本プラグイン自身の意図的な変更。仲裁をバイパスして必ず適用する
        /// </summary>
        public static void SetControl(CameraMain camera, bool enable)
        {
            _applying = true;
            try
            {
                camera.SetControl(enable);
            }
            finally
            {
                _applying = false;
            }
        }

        private static bool SetControlPrefix(CameraMain __instance, bool f_bEnable)
        {
            if (_applying || f_bEnable || !GameViewManager.instance.isWindowMode)
            {
                return true;
            }

            // 仲裁対象は GameView が RT へ逃がしているメインカメラだけ。
            // CameraMain は派生 (OvrCamera 等) や複数インスタンスがありうるため他は素通しする
            var gameMain = GameMain.Instance;
            if (gameMain == null || !ReferenceEquals(__instance, gameMain.MainCamera))
            {
                return true;
            }

            // 既に無効なら状態は変わらないので通す (他プラグインの「自分が無効化した」判定を壊さない)
            if (!__instance.GetControl())
            {
                return true;
            }

            // カーソルが GameView 描画領域内にある間だけ無効化を抑止する。
            // 判定は InputRemapper の座標変換条件と揃える
            return !InputRemapper.IsGameViewActiveAt(InputRemapper.rawGuiPosition);
        }

        /// <summary>
        /// 他プラグイン (ModItemExplorer 等) は自ウィンドウ上のホイール操作を
        /// Input.ResetInputAxes で潰すが、座標誤読により GameView 内でも誤発動して
        /// ホイール拡縮が効かなくなるため、GameView 内では呼び出しを抑止する。
        /// SceneView のホイールズームも同じ理由で守る
        /// </summary>
        private static bool ResetInputAxesPrefix()
        {
            if (!GameViewManager.instance.isWindowMode)
            {
                return true;
            }

            var guiPos = InputRemapper.rawGuiPosition;
            return !InputRemapper.IsGameViewActiveAt(guiPos) &&
                !SceneViewWindow.instance.IsSceneViewActiveAt(guiPos);
        }
    }
}
