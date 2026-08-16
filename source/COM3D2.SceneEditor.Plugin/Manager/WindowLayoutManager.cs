using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 名前付きウィンドウレイアウトの保存・反映とファイル管理。
    /// レイアウトは Config\WindowLayout に <名前>.xml で保存する
    /// (フォルダ・検証・エラー処理の流儀は ScenePresetManager と同じ)
    /// </summary>
    public static class WindowLayoutManager
    {
        /// <summary>レイアウト名の最大長。シーンプリセットと同じ制限に合わせる</summary>
        private const int MAX_LAYOUT_NAME_LENGTH = 250;

        /// <summary>同梱の既定レイアウト名。初回起動時にこの名前のレイアウトを適用する</summary>
        private const string DEFAULT_LAYOUT_NAME = "デフォルト";

        public static string layoutFolderPath
            => Path.Combine(PluginUtils.PluginDataPath, "WindowLayout");

        private static readonly XmlSerializer _serializer = new XmlSerializer(typeof(WindowLayoutData));

        /// <summary>レイアウト名一覧のキャッシュ。null なら次回取得時に走査する</summary>
        private static List<string> _layoutNames = null;

        private static Config config => ConfigManager.instance.config;

        /// <summary>保存済みレイアウト名の一覧。メニュー表示のたびに呼ばれるためキャッシュする</summary>
        public static List<string> GetLayoutNames()
        {
            if (_layoutNames == null)
            {
                _layoutNames = new List<string>();
                try
                {
                    if (Directory.Exists(layoutFolderPath))
                    {
                        var xmlPaths = Directory.GetFiles(layoutFolderPath, "*.xml")
                            .OrderBy(path => path, new NaturalStringComparer());
                        foreach (var xmlPath in xmlPaths)
                        {
                            _layoutNames.Add(Path.GetFileNameWithoutExtension(xmlPath));
                        }
                    }
                }
                catch (Exception e)
                {
                    MTEUtils.LogException(e);
                }
            }
            return _layoutNames;
        }

        public static bool Exists(string layoutName)
        {
            return File.Exists(GetLayoutFilePath(layoutName));
        }

        /// <summary>レイアウト名の検証。問題があればエラーメッセージ、なければ null を返す</summary>
        public static string ValidateLayoutName(string layoutName)
        {
            if (string.IsNullOrEmpty(layoutName))
            {
                return "レイアウト名を入力してください";
            }
            if (layoutName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                return "レイアウト名に使用できない文字が含まれています";
            }
            if (layoutName.Length > MAX_LAYOUT_NAME_LENGTH)
            {
                return "レイアウト名が長すぎます（" + MAX_LAYOUT_NAME_LENGTH + "文字まで）";
            }
            return null;
        }

        private static string GetLayoutFilePath(string layoutName)
        {
            return Path.Combine(layoutFolderPath, layoutName + ".xml");
        }

        /// <summary>現在のウィンドウ配置をレイアウトとして保存する。同名は上書き</summary>
        public static void SaveLayout(string layoutName)
        {
            try
            {
                var data = Capture();

                if (!Directory.Exists(layoutFolderPath))
                {
                    Directory.CreateDirectory(layoutFolderPath);
                }

                using (var stream = File.Create(GetLayoutFilePath(layoutName)))
                {
                    _serializer.Serialize(stream, data);
                }

                // メニューの一覧へ即時反映させる
                _layoutNames = null;

                MTEUtils.Log("レイアウトを保存しました: {0}", layoutName);
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
                DialogPopupWindow.ShowDialog("レイアウトの保存に失敗しました");
            }
        }

        /// <summary>レイアウトを読み込んで現在のウィンドウ配置へ反映する</summary>
        public static void ApplyLayout(string layoutName)
        {
            try
            {
                WindowLayoutData data;
                using (var stream = File.OpenRead(GetLayoutFilePath(layoutName)))
                {
                    data = (WindowLayoutData)_serializer.Deserialize(stream);
                }

                // 未来バージョンのレイアウトは不完全に復元されるおそれがあるため適用しない
                if (data.version > WindowLayoutData.CurrentVersion)
                {
                    MTEUtils.LogError(
                        "レイアウトのバージョンが未対応です: {0} (version={1})", layoutName, data.version);
                    DialogPopupWindow.ShowDialog(
                        "レイアウトのバージョンが新しいため適用できません\nプラグインを更新してください");
                    return;
                }

                Apply(data);
                MTEUtils.Log("レイアウトを適用しました: {0}", layoutName);
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
                DialogPopupWindow.ShowDialog("レイアウトの適用に失敗しました");
            }
        }

        /// <summary>
        /// 既定レイアウトを適用する。
        /// ユーザーが削除・改名している場合もあるため、無ければ何もしない
        /// </summary>
        public static void ApplyDefaultLayout()
        {
            if (!Exists(DEFAULT_LAYOUT_NAME))
            {
                MTEUtils.Log("既定レイアウトが見つからないため適用をスキップします: {0}", DEFAULT_LAYOUT_NAME);
                return;
            }

            ApplyLayout(DEFAULT_LAYOUT_NAME);
        }

        /// <summary>現在のウィンドウ配置からレイアウトデータを組み立てる</summary>
        private static WindowLayoutData Capture()
        {
            // グループ構成の文字列化は既存の SaveGroups を流用するため、先に config へ反映する
            TabGroupManager.instance.SaveGroups();
            WindowConnectManager.instance.SaveGroups();

            var gameView = GameViewWindow.instance;
            var menuBar = MenuBarWindow.instance;

            var data = new WindowLayoutData
            {
                version = WindowLayoutData.CurrentVersion,
                screenWidth = Screen.width,
                screenHeight = Screen.height,
                tabGroups = new List<string>(config.tabGroups),
                connectGroups = new List<string>(config.connectGroups),
                gameViewMaximized = GameViewManager.instance.isMaximized,
                // GameView の表示可否は gameViewMaximized 経由で復元するため visible は使わない
                gameView = new WindowLayoutEntry
                {
                    windowId = GameViewWindow.WINDOW_ID,
                    x = (int)gameView.windowRect.x,
                    y = (int)gameView.windowRect.y,
                    width = gameView.viewPixelWidth,
                    height = gameView.viewPixelHeight,
                },
                menuBarPosX = (int)menuBar.windowRect.x,
                menuBarPosY = (int)menuBar.windowRect.y,
            };

            foreach (var window in WindowManager.instance.windows)
            {
                var subWindow = window as EditorSubWindow;
                if (subWindow == null)
                {
                    continue;
                }
                data.windows.Add(new WindowLayoutEntry
                {
                    windowId = subWindow.tabWindowId,
                    x = (int)subWindow.windowRect.x,
                    y = (int)subWindow.windowRect.y,
                    width = subWindow.contentPixelWidth,
                    height = subWindow.contentPixelHeight,
                    visible = subWindow.isShowWnd,
                });
            }

            return data;
        }

        /// <summary>
        /// レイアウトデータを現在の配置へ反映する。
        /// 再グルーピングは未所属・表示中が前提のため、
        /// グループ解体 → 配置・表示適用 → グループ復元の順で行う
        /// </summary>
        private static void Apply(WindowLayoutData data)
        {
            TabGroupManager.instance.DissolveAllGroups();
            WindowConnectManager.instance.DisconnectAll();

            foreach (var entry in data.windows)
            {
                var subWindow = FindSubWindow(entry.windowId);
                if (subWindow == null)
                {
                    // 別バージョンで保存されたレイアウト等。該当ウィンドウだけ諦めて続行する
                    continue;
                }
                subWindow.isShowWnd = entry.visible;
                subWindow.ApplyPlacement(
                    entry.x, entry.y, entry.width, entry.height,
                    data.screenWidth, data.screenHeight);
            }

            if (data.gameView != null)
            {
                GameViewWindow.instance.ApplyPlacement(
                    data.gameView.x, data.gameView.y,
                    data.gameView.width, data.gameView.height,
                    data.screenWidth, data.screenHeight);
            }

            var gameViewManager = GameViewManager.instance;
            if (gameViewManager.isMaximized != data.gameViewMaximized)
            {
                gameViewManager.SetMaximized(data.gameViewMaximized);
            }

            if (data.menuBarPosX >= 0)
            {
                MenuBarWindow.instance.ApplyPosition(
                    data.menuBarPosX, data.menuBarPosY,
                    data.screenWidth, data.screenHeight);
            }

            // グループ復元は既存の RestoreGroups (config 参照) を流用する
            config.tabGroups.Clear();
            if (data.tabGroups != null)
            {
                config.tabGroups.AddRange(data.tabGroups);
            }
            config.connectGroups.Clear();
            if (data.connectGroups != null)
            {
                config.connectGroups.AddRange(data.connectGroups);
            }
            TabGroupManager.instance.RestoreGroups();
            WindowConnectManager.instance.RestoreGroups();
            WindowConnectManager.instance.ClampGroups();

            // 外部窓の配置はゲストが持つためレイアウトでは動かない。グループ復元で
            // 拾えなかったぶんを、同じ位置の窓への自動再ドッキングに任せる
            DockingHost.OnLayoutApplied();

            config.dirty = true;
        }

        private static EditorSubWindow FindSubWindow(int windowId)
        {
            foreach (var window in WindowManager.instance.windows)
            {
                var subWindow = window as EditorSubWindow;
                if (subWindow != null && subWindow.tabWindowId == windowId)
                {
                    return subWindow;
                }
            }
            return null;
        }
    }
}
