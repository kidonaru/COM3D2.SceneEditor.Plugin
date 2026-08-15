using System;
using System.Collections;
using System.Reflection;
using COM3D2.MotionTimelineEditor;
using HarmonyLib;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// ゲーム側の Alt+S (UI 無しスクリーンショット) を、プラグイン有効中は
    /// ScreenshotManager.Capture() に差し替える。
    ///
    /// CameraMain.Update の手前で同じキー条件を判定し、先に screen_shot_noui_ を立てて
    /// プラグインの撮影を始める。ゲーム側の分岐は「!screen_shot_noui_」を条件に持つため、
    /// このフラグを先取りするだけで撮影が発生しなくなる。
    ///
    /// 撮影本体である CameraMain.SaveScreenShotNoUI を直接パッチしても Alt+S は捕まらない。
    /// 同メソッドは「イテレータの状態機械を new して返すだけ」の極小メソッドで、
    /// Mono が Update へインライン展開するため detour を通らない
    /// (リフレクション経由の呼び出しでだけ効く状態になる)。
    ///
    /// ギアメニューの「UI無しSS」ボタンやサムネ撮影は Update を経由しないので素通しになる。
    /// VR カメラ (OvrCamera 系) は Update を override していてこのパッチが乗らないため、
    /// VR のスナップショットもゲーム標準のまま
    /// </summary>
    public static class ScreenshotHotkeyPatch
    {
        private const string UPDATE_METHOD_NAME = "Update";

        /// <summary>UI 無し撮影の連写抑止フラグ。2.0 / 2.5 で同名</summary>
        private const string SCREEN_SHOT_NO_UI_FIELD = "screen_shot_noui_";

        // Harmony インスタンスは他のパッチと独立させ、有効・無効判定を他パッチの状態から切り離す
        private static Harmony _harmony = null;

        private static FieldInfo _screenShotNoUIField = null;

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
                _screenShotNoUIField = AccessTools.Field(typeof(CameraMain), SCREEN_SHOT_NO_UI_FIELD);
                if (_screenShotNoUIField == null)
                {
                    throw new Exception(SCREEN_SHOT_NO_UI_FIELD + " が見つかりません");
                }

                var original = AccessTools.Method(typeof(CameraMain), UPDATE_METHOD_NAME);
                if (original == null)
                {
                    throw new Exception("CameraMain." + UPDATE_METHOD_NAME + " が見つかりません");
                }

                var prefix = AccessTools.Method(
                    typeof(ScreenshotHotkeyPatch), nameof(UpdatePrefix));

                _harmony = new Harmony(PluginInfo.PluginFullName + ".ScreenshotHotkey");
                _harmony.Patch(original, prefix: new HarmonyMethod(prefix));
                MTEUtils.Log("CameraMain.Update のフックに成功しました");
            }
            catch (Exception e)
            {
                // 失敗しても Alt+S はゲーム標準の撮影として動く
                MTEUtils.LogError(
                    "CameraMain.Update のフックに失敗しました。Alt+S はゲーム標準の撮影のままです");
                MTEUtils.LogException(e);
                _harmony = null;
                _screenShotNoUIField = null;
            }
        }

        /// <summary>
        /// ゲーム側が Alt+S を処理する直前に割り込む。
        /// 毎フレーム走るため、無効時は真っ先に抜ける。
        /// 例外を外へ出すとゲームのカメラ処理を壊すため握り潰す
        /// </summary>
        private static void UpdatePrefix(CameraMain __instance)
        {
            try
            {
                if (_screenShotNoUIField == null || !SceneEditorPlugin.instance.isEnable)
                {
                    return;
                }

                // 撮影するのは ScreenshotManager がメインカメラなので、
                // フラグを立てる相手も同じインスタンスに限る
                if (!ReferenceEquals(__instance, GameMain.Instance.MainCamera))
                {
                    return;
                }

                if (!IsScreenShotKeyDown())
                {
                    return;
                }

                // 撮影中の連写は元の実装と同じく無視する
                if ((bool)_screenShotNoUIField.GetValue(__instance))
                {
                    return;
                }

                // ゲーム側の分岐が走らないよう、こちらでフラグを立ててから撮影する
                _screenShotNoUIField.SetValue(__instance, true);
                __instance.StartCoroutine(CaptureCoroutine(__instance));
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
            }
        }

        /// <summary>
        /// UI 無し撮影のキー条件。CameraMain.Update の判定と揃えてある。
        /// PrintScreen のキーコードは 2.5 が Print、2.0 が SysReq と異なるため両方拾う
        /// </summary>
        private static bool IsScreenShotKeyDown()
        {
            if (!Input.GetKey(KeyCode.LeftAlt) && !Input.GetKey(KeyCode.RightAlt))
            {
                return false;
            }
            return Input.GetKeyDown(KeyCode.S) ||
                Input.GetKeyDown(KeyCode.Print) ||
                Input.GetKeyDown(KeyCode.SysReq);
        }

        /// <summary>
        /// ゲーム側と同じくフレーム末まで待ってから撮影する。
        /// ScreenshotManager.Capture() は RenderTexture へ描き直すため UI は写らず、
        /// 元処理の UIHide / UIResume は不要。
        /// 連写を止めている screen_shot_noui_ は、撮影の成否によらず必ず戻す
        /// </summary>
        private static IEnumerator CaptureCoroutine(CameraMain cameraMain)
        {
            yield return new WaitForEndOfFrame();

            try
            {
                ScreenshotManager.Capture();
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
            }
            finally
            {
                _screenShotNoUIField.SetValue(cameraMain, false);
            }
        }
    }
}
