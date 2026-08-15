using System;
using COM3D2.MotionTimelineEditor;
using MyRoomCustom;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 背景 (BgMgr / PhotoBGData) 周りの共通処理。
    /// 背景ウィンドウとシーンプリセットの双方から使う
    /// </summary>
    public static class BackgroundUtils
    {
        /// <summary>ゲーム起動時のカメラ背景色。背景色のリセット先に使う (初回アクセス時に控える)</summary>
        private static Color? _defaultBgColor = null;

        private static Camera mainCamera
        {
            get
            {
                var gameMain = GameMain.Instance;
                var camMain = gameMain != null ? gameMain.MainCamera : null;
                return camMain != null ? camMain.camera : null;
            }
        }

        /// <summary>
        /// 背景を消しているときに見える色 (メインカメラのクリア色)。
        /// 公式のフォトモード (BGWindow) も同じく camera.backgroundColor を直接書き換えている。
        /// アルファは撮影時の透過度としても使う
        /// </summary>
        public static Color bgColor
        {
            get
            {
                var camera = mainCamera;
                if (camera == null)
                {
                    return Color.black;
                }

                CacheDefaultBgColor(camera);
                return camera.backgroundColor;
            }
            set
            {
                var camera = mainCamera;
                if (camera == null)
                {
                    return;
                }

                CacheDefaultBgColor(camera);

                // Skybox クリアのままだと backgroundColor が画面に出ないため単色クリアへ寄せる
                if (camera.clearFlags != CameraClearFlags.SolidColor)
                {
                    camera.clearFlags = CameraClearFlags.SolidColor;
                }
                camera.backgroundColor = value;
            }
        }

        /// <summary>背景色のリセット先。メインカメラ未取得のうちは黒</summary>
        public static Color defaultBgColor => _defaultBgColor ?? Color.black;

        private static void CacheDefaultBgColor(Camera camera)
        {
            if (_defaultBgColor == null)
            {
                _defaultBgColor = camera.backgroundColor;
            }
        }

        /// <summary>
        /// マイルーム背景時に BgMgr.GetBGName() が返す名前の接頭辞。
        /// BgMgr.ChangeBgMyRoom が "マイルーム:" + guid を設定する
        /// </summary>
        private const string MyRoomBgNamePrefix = "マイルーム:";

        /// <summary>
        /// 背景一覧を用意する。フォトモード未経由だと未構築のため初回にここで構築する。
        /// 使える一覧が得られたかを返す
        /// </summary>
        public static bool EnsureBgDataLoaded()
        {
            if (PhotoBGData.data == null)
            {
                PhotoBGData.Create();
            }
            return PhotoBGData.data != null && PhotoBGData.data.Count > 0;
        }

        /// <summary>
        /// 背景一覧を作り直す。マイルーム背景は PhotoBGData.Create の中で
        /// セーブデータから組み直されるため、これを呼ばないと新規保存分が一覧に出ない。
        /// 公式のフォトモードも BGWindow.Awake で毎回作り直している
        /// </summary>
        public static void ReloadBgData()
        {
            try
            {
                PhotoBGData.Create();
            }
            catch (Exception e)
            {
                // ファイルシステム未初期化などで失敗しても描画は続行する
                // (一覧は空表示になり、開き直せば作り直される)
                MTEUtils.LogException(e);
            }
        }

        /// <summary>
        /// BgMgr.GetBGName() の値を PhotoBGData の id へ変換する。特定できない場合は null。
        /// マイルームは名前から guid を直接取れるため、一覧に載っていなくても id を返せる
        /// </summary>
        public static string GetBgId(string bgName)
        {
            if (string.IsNullOrEmpty(bgName))
            {
                return null;
            }

            if (bgName.StartsWith(MyRoomBgNamePrefix))
            {
                return bgName.Substring(MyRoomBgNamePrefix.Length);
            }

            if (PhotoBGData.data == null)
            {
                return null;
            }
            foreach (var bgData in PhotoBGData.data)
            {
                if (bgData.create_prefab_name == bgName)
                {
                    return bgData.id;
                }
            }
            return null;
        }

        /// <summary>bgData が bgName（BgMgr.GetBGName() の値）の指す背景かどうか</summary>
        public static bool IsCurrentBg(PhotoBGData bgData, string bgName)
        {
            if (bgData == null || string.IsNullOrEmpty(bgName))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(bgData.create_prefab_name))
            {
                return bgData.create_prefab_name == bgName;
            }
            // create_prefab_name が空なのはマイルーム背景で、id がセーブデータの guid
            return bgName == MyRoomBgNamePrefix + bgData.id;
        }

        /// <summary>
        /// id を指定して背景を適用する。適用できたかを返す。
        /// 一覧に無いマイルーム（一覧構築後に保存されたもの）はセーブデータを直接引いて適用する
        /// </summary>
        public static bool ApplyById(string bgId)
        {
            if (string.IsNullOrEmpty(bgId))
            {
                return false;
            }

            EnsureBgDataLoaded();

            var bgData = PhotoBGData.data != null ? PhotoBGData.Get(bgId) : null;
            if (bgData != null)
            {
                bgData.Apply();
                return true;
            }

            if (CreativeRoomManager.IsExistSaveData(bgId))
            {
                GameMain.Instance.BgMgr.ChangeBgMyRoom(bgId);
                return true;
            }

            return false;
        }
    }
}
