using System;
using COM3D2.MotionTimelineEditor;
using HarmonyLib;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 事務所シーンのロードに割り込み、自動ロード対象プリセットの
    /// 背景・カメラ・ライトをフェードイン前に先出し適用する。
    ///
    /// DailyAPI.SceneStart はゲーム側が背景・カメラ・ライトを設定し終えた直後で、
    /// 画面はまだ FadeOut(0f) の黒。その後 CoCharaLoad がメイドのロード完了を待って
    /// FadeIn するため、ここで差し替えれば通常背景は一度も表示されない。
    /// フェードの進行そのものには手を入れない
    /// </summary>
    public static class ScenePresetAutoLoadPatch
    {
        /// <summary>ゲーム側のシーン開始 API。2.0 / 2.5 で同一シグネチャ</summary>
        private const string DAILY_API_TYPE_NAME =
            "com.workman.cm3d2.scene.dailyEtc.DailyAPI";
        private const string SCENE_START_METHOD_NAME = "SceneStart";
        private const string CALLBACK_TYPE_NAME = "dgOnSceneStartCallBack";

        // Harmony インスタンスは InputRemapper のものを共有せず独立させる。
        // 自動ロードは GameView / 入力まわりの有効状態と無関係に成立させたい機能であり、
        // ID を分けることで unpatch の単位も独立する
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
                var dailyApiType = AccessTools.TypeByName(DAILY_API_TYPE_NAME);
                if (dailyApiType == null)
                {
                    throw new Exception(DAILY_API_TYPE_NAME + " が見つかりません");
                }

                var callbackType = AccessTools.Inner(dailyApiType, CALLBACK_TYPE_NAME);
                if (callbackType == null)
                {
                    throw new Exception(CALLBACK_TYPE_NAME + " が見つかりません");
                }

                var original = AccessTools.Method(
                    dailyApiType,
                    SCENE_START_METHOD_NAME,
                    new[] { typeof(bool), typeof(MonoBehaviour), callbackType });
                if (original == null)
                {
                    throw new Exception(SCENE_START_METHOD_NAME + " が見つかりません");
                }

                var postfix = AccessTools.Method(
                    typeof(ScenePresetAutoLoadPatch), nameof(SceneStartPostfix));

                _harmony = new Harmony(PluginInfo.PluginFullName + ".ScenePresetAutoLoad");
                _harmony.Patch(original, postfix: new HarmonyMethod(postfix));
                MTEUtils.Log("DailyAPI.SceneStart のフックに成功しました");
            }
            catch (Exception e)
            {
                // 失敗してもシーンプリセットの自動ロード自体は従来タイミングで動く
                MTEUtils.LogError(
                    "DailyAPI.SceneStart のフックに失敗しました。プリセットの先出し適用は無効です");
                MTEUtils.LogException(e);
                _harmony = null;
            }
        }

        /// <summary>
        /// ゲーム側のシーン開始直後に呼ばれる。
        /// 例外を外へ出すとゲームのシーン開始処理を壊すため、ここで握り潰す
        /// </summary>
        private static void SceneStartPostfix()
        {
            try
            {
                ScenePresetManager.PreloadAutoLoadScenery();
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
            }
        }
    }
}
