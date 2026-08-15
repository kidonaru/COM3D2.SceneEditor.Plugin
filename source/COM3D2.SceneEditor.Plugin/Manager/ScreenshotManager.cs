using System;
using System.Collections.Generic;
using System.IO;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// スクリーンショットの撮影。
    /// メインカメラを一時 RenderTexture へ描画するため、プラグイン UI や NGUI は写らず、
    /// 撮影中だけギズモ・骨格線・ドラッグ点の描画を止めることでゲーム画面だけを保存する
    /// </summary>
    public static class ScreenshotManager
    {
        /// <summary>解像度倍率の上限。これ以上はメモリと撮影時間が現実的でない</summary>
        public static readonly int MAX_SCALE = 4;

        private static Config config => ConfigManager.instance.config;

        /// <summary>ゲーム本体の F9 撮影と同じ、ゲームルート直下の ScreenShot フォルダ</summary>
        public static string screenshotFolderPath
            => Path.Combine(UTY.gameProjectPath, "ScreenShot");

        /// <summary>現在の設定で撮影した場合の出力解像度。設定ウィンドウの表示にも使う</summary>
        public static void GetCaptureSize(out int width, out int height)
        {
            var scale = Mathf.Clamp(config.screenshotScale, 1, MAX_SCALE);
            width = Screen.width * scale;
            height = Screen.height * scale;

            // GPU の上限を超えると RenderTexture の確保に失敗するため縮める。
            // 縦横を個別にクランプすると長辺だけが縮んで絵が歪むので、
            // はみ出した分の比率を両辺へ等しくかける
            var limit = SystemInfo.maxTextureSize;
            var longest = Mathf.Max(width, height);
            if (longest > limit)
            {
                var ratio = (float)limit / longest;
                width = Mathf.Max(Mathf.RoundToInt(width * ratio), 1);
                height = Mathf.Max(Mathf.RoundToInt(height * ratio), 1);
            }
        }

        /// <summary>
        /// スクリーンショットを撮影して保存し、保存先パスを返す (失敗時は null)。
        /// メインカメラを一時 RT へ描画するため、最大化中でも使え、
        /// 画面より大きい解像度 (config.screenshotScale 倍) でも撮れる
        /// </summary>
        public static string Capture()
        {
            var mainCamera = GameMain.Instance.MainCamera;
            var camera = mainCamera != null ? mainCamera.camera : null;
            if (camera == null)
            {
                MTEUtils.LogWarning("メインカメラが見つからないため撮影できません");
                ToastManager.Show("メインカメラが見つからないため撮影できません", ToastType.Error);
                return null;
            }

            string filePath;
            try
            {
                Directory.CreateDirectory(screenshotFolderPath);
                filePath = CreateFilePath();
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
                ToastManager.Show("保存先フォルダを作成できませんでした", ToastType.Error);
                return null;
            }

            var savedTargetTexture = camera.targetTexture;
            var savedActive = RenderTexture.active;
            var savedClearFlags = camera.clearFlags;
            var savedBackgroundColor = camera.backgroundColor;
            RenderTexture renderTexture = null;
            // 途中で例外が起きても enabled=false にした分だけは確実に復元できるよう、
            // リストは先に作って HideOverlays には詰めてもらう
            var hiddenOverlays = new List<Behaviour>();
            Texture2D texture = null;
            try
            {
                int captureWidth, captureHeight;
                GetCaptureSize(out captureWidth, out captureHeight);
                renderTexture = RenderTexture.GetTemporary(captureWidth, captureHeight, 24);
                HideOverlays(hiddenOverlays);

                camera.targetTexture = renderTexture;

                var bgColor = BackgroundUtils.bgColor;
                texture = bgColor.a < 1f
                    ? CaptureTransparent(camera, renderTexture, bgColor)
                    : CaptureOpaque(camera, renderTexture);

                // UTY.SaveImage(Texture2D) は内部で Blit して ReadPixels し直すため、
                // 読み込み済みのピクセルをそのまま書き出して GPU リードバックの往復を避ける
                File.WriteAllBytes(filePath, texture.EncodeToPNG());
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
                ToastManager.Show("スクリーンショットの保存に失敗しました", ToastType.Error);
                return null;
            }
            finally
            {
                RestoreOverlays(hiddenOverlays);
                camera.targetTexture = savedTargetTexture;
                camera.clearFlags = savedClearFlags;
                camera.backgroundColor = savedBackgroundColor;
                RenderTexture.active = savedActive;
                if (renderTexture != null)
                {
                    RenderTexture.ReleaseTemporary(renderTexture);
                }
                if (texture != null)
                {
                    UnityEngine.Object.Destroy(texture);
                }
            }

            MTEUtils.Log("スクリーンショットを保存しました: {0}", filePath);
            ToastManager.Show(
                "スクリーンショットを保存しました\n" + Path.GetFileName(filePath),
                ToastType.Success);
            return filePath;
        }

        /// <summary>不透明な撮影。カメラをそのまま 1 回描画して読み出す</summary>
        private static Texture2D CaptureOpaque(Camera camera, RenderTexture renderTexture)
        {
            camera.Render();

            RenderTexture.active = renderTexture;
            var texture = new Texture2D(renderTexture.width, renderTexture.height,
                TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
            texture.Apply();
            return texture;
        }

        /// <summary>
        /// 背景を透過させた撮影。
        /// シェーダがアルファチャンネルへ何を書くかは当てにできないため、黒背景と白背景で
        /// 2 回描き、その差分から前景の被覆率を求める。
        /// 被覆率を c、前景色を F とすると 黒背景 = c*F、白背景 = c*F + (1-c) なので、
        /// 両者の差がそのまま「背景の透け量」(1-c) になる
        /// </summary>
        private static Texture2D CaptureTransparent(
            Camera camera, RenderTexture renderTexture, Color bgColor)
        {
            var onBlack = RenderAndRead(camera, renderTexture, Color.black);
            var onWhite = RenderAndRead(camera, renderTexture, Color.white);

            // 高解像度 (最大 4 倍) では 1 本で数百 MB になるため、結果は onBlack へ上書きして
            // 巨大な配列を 3 本同時に抱えないようにする (同じ添字を読んでから書くので安全)
            var pixels = onBlack;
            for (var i = 0; i < pixels.Length; i++)
            {
                var fore = onBlack[i];
                var back = onWhite[i];

                // 黒背景では背景の寄与が 0 なので、onBlack がそのまま乗算済みの前景色になる
                var transparency = ((back.r - fore.r) + (back.g - fore.g) + (back.b - fore.b)) / 3f;
                var coverage = Mathf.Clamp01(1f - transparency);

                // 前景の隙間から見える分だけ、指定された背景色をそのアルファで敷く
                var backAlpha = (1f - coverage) * bgColor.a;
                var alpha = Mathf.Clamp01(coverage + backAlpha);
                if (alpha <= 0f)
                {
                    pixels[i] = Color.clear;
                    continue;
                }

                // PNG はストレートアルファなので、乗算済みの色をアルファで割り戻す
                pixels[i] = new Color(
                    Mathf.Clamp01((fore.r + backAlpha * bgColor.r) / alpha),
                    Mathf.Clamp01((fore.g + backAlpha * bgColor.g) / alpha),
                    Mathf.Clamp01((fore.b + backAlpha * bgColor.b) / alpha),
                    alpha);
            }

            // 出力用テクスチャの確保前に、もう使わない白背景分を GC 対象にする
            onWhite = null;

            var texture = new Texture2D(renderTexture.width, renderTexture.height,
                TextureFormat.RGBA32, false);
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        /// <summary>クリア色を指定してカメラを 1 回描画し、ピクセルを読み出す</summary>
        private static Color[] RenderAndRead(
            Camera camera, RenderTexture renderTexture, Color clearColor)
        {
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = clearColor;
            camera.Render();

            RenderTexture.active = renderTexture;
            var texture = new Texture2D(renderTexture.width, renderTexture.height,
                TextureFormat.RGB24, false);
            try
            {
                texture.ReadPixels(
                    new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
                texture.Apply();
                return texture.GetPixels();
            }
            finally
            {
                UnityEngine.Object.Destroy(texture);
            }
        }

        /// <summary>ゲーム本体の命名 (img + タイムスタンプ) に合わせる。同秒連写は連番で回避する</summary>
        private static string CreateFilePath()
        {
            var baseName = "img" + DateTime.Now.ToString("yyyyMMddHHmmss");
            var filePath = Path.Combine(screenshotFolderPath, baseName + ".png");
            for (var i = 1; File.Exists(filePath); i++)
            {
                filePath = Path.Combine(screenshotFolderPath, baseName + "_" + i + ".png");
            }
            return filePath;
        }

        /// <summary>
        /// 撮影に写したくないプラグインの描画要素 (ギズモ・骨格線・グリッド・ドラッグ点の円) を
        /// 一時的に無効化し、復元用のリストを返す。
        /// GizmoRenderer は OnPostRender、MaidDragPointRing は OnRenderObject で描くため、
        /// コンポーネントを無効化すれば手動の camera.Render() でも描かれない
        /// </summary>
        private static void HideOverlays(List<Behaviour> hidden)
        {
            // 撮影対象はメインカメラのため、そこに紐づく GameViewManager 側だけを止める
            // (SceneViewManager のギズモ・骨格線は別カメラなので写らない)
            var gameViewManager = GameViewManager.instance;
            HideOverlay(hidden, gameViewManager.gizmoRenderer);
            HideOverlay(hidden, gameViewManager.boneLineRenderer);
            HideOverlay(hidden, gameViewManager.gridRenderer);

            foreach (var ring in UnityEngine.Object.FindObjectsOfType<MaidDragPointRing>())
            {
                HideOverlay(hidden, ring);
            }

            // ボーン編集の回転ギズモは Alt 押下中だけ出る。Alt を押したままメニューを
            // クリックされると写り込むため、これも止める
            foreach (var gizmo in UnityEngine.Object.FindObjectsOfType<ModelGizmoRender>())
            {
                HideOverlay(hidden, gizmo);
            }
        }

        private static void HideOverlay(List<Behaviour> hidden, Behaviour behaviour)
        {
            if (behaviour != null && behaviour.enabled)
            {
                behaviour.enabled = false;
                hidden.Add(behaviour);
            }
        }

        private static void RestoreOverlays(List<Behaviour> hidden)
        {
            foreach (var behaviour in hidden)
            {
                if (behaviour != null)
                {
                    behaviour.enabled = true;
                }
            }
        }
    }
}
