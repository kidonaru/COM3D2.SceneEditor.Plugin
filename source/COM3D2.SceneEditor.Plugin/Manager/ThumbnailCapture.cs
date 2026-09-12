using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// サムネイル用のゲーム画面キャプチャ。
    /// シーンプリセットとタイムラインで共通に使う
    /// </summary>
    public static class ThumbnailCapture
    {
        /// <summary>
        /// メインカメラを一時 RenderTexture へ描画してサムネを保存する。
        /// 画面キャプチャと違いプラグイン UI や NGUI が写り込まず、最大化中でも使える
        /// 撮影自体は画面解像度で行い、保存前に width / height へ縮小する
        /// </summary>
        public static void Save(string filePath, int width, int height)
        {
            var mainCamera = GameMain.Instance.MainCamera;
            var camera = mainCamera != null ? mainCamera.camera : null;
            if (camera == null)
            {
                return;
            }

            var renderTexture = RenderTexture.GetTemporary(Screen.width, Screen.height, 24);
            var savedTargetTexture = camera.targetTexture;
            var savedActive = RenderTexture.active;
            Texture2D texture = null;
            try
            {
                camera.targetTexture = renderTexture;
                camera.Render();

                RenderTexture.active = renderTexture;
                texture = new Texture2D(renderTexture.width, renderTexture.height,
                    TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
                texture.Apply();

                texture.ResizeTexture(width, height);
                UTY.SaveImage(texture, filePath);
            }
            finally
            {
                camera.targetTexture = savedTargetTexture;
                RenderTexture.active = savedActive;
                RenderTexture.ReleaseTemporary(renderTexture);
                if (texture != null)
                {
                    Object.Destroy(texture);
                }
            }
        }
    }
}
