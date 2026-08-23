using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using COM3D2.MotionTimelineEditor;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>タイル一覧の 1 項目。ファイルならタイムライン XML、フォルダなら下位階層</summary>
    public class TimelineLoadItem : TileViewContentBase
    {
        /// <summary>XML (ファイル) またはフォルダの絶対パス</summary>
        public string path;
    }

    /// <summary>
    /// タイムライン XML の一覧をタイルビュー用のツリーとして構築する。
    /// 一覧の作法は ScenePresetManager に合わせている (サムネは XML と同名の PNG)。
    /// ツリー構築の処理は ScenePresetManager とほぼ同じだが、移植元 MTE の
    /// TimelineLoadManager との対応を追えるようにするため、共通化せず据え置いている
    /// </summary>
    public static class TimelineLoadManager
    {
        public static string rootPath => MTEP.PluginUtils.TimelineDirPath;

        // ルートパスの解決が GameMain 経由になるため、静的初期化子では作らない。
        // 静的コンストラクタで例外が出ると TypeInitializationException がキャッシュされ、
        // そのゲームセッション中ずっとこのクラスが使えなくなる
        /// <summary>タイルビュー用のツリールート。初回の Reload で作られる</summary>
        public static TimelineLoadItem rootItem { get; private set; }

        /// <summary>表示中のフォルダ。UI のフォルダ移動で書き換える</summary>
        public static TimelineLoadItem currentDirItem { get; set; }

        public static string currentDirPath
            => currentDirItem != null ? currentDirItem.path : rootPath;

        private static bool _loaded = false;

        /// <summary>初回参照時だけ一覧を読み込む。以後は保存時と「更新」ボタンで作り直す</summary>
        public static TimelineLoadItem GetOrLoadCurrentDirItem()
        {
            if (!_loaded)
            {
                Reload();
            }
            return currentDirItem;
        }

        /// <summary>一覧を作り直す。GUI から呼ばれるため例外は握って空のまま返す</summary>
        public static void Reload()
        {
            _loaded = true;

            // 作り直しで表示中フォルダの実体が入れ替わるため、相対パスで控えて後から解決し直す
            // (初回は currentDirItem が無く、ルート相当の空文字になる)
            var currentRelativeDir = GetRelativePath(rootPath, currentDirPath);

            ClearThumbnails(rootItem);
            rootItem = CreateRootItem();
            currentDirItem = rootItem;

            try
            {
                if (Directory.Exists(rootPath))
                {
                    var visitedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        GetCanonicalPath(rootPath),
                    };
                    SearchItems(rootItem, visitedDirs);
                }

                currentDirItem = FindDirItem(rootItem, currentRelativeDir) ?? rootItem;
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
            }
        }

        /// <summary>
        /// タイムラインのロードに渡すディレクトリ名 (ルートからの相対パス、区切りは \)。
        /// ルート直下なら空文字
        /// </summary>
        public static string GetRelativeDirectoryName(TimelineLoadItem item)
        {
            return item == null ? "" : GetRelativeDirectoryName(rootPath, item.path);
        }

        /// <summary>ルートとファイルパスから、そのファイルが属するフォルダの相対パスを返す</summary>
        public static string GetRelativeDirectoryName(string rootPath, string filePath)
        {
            var dirPath = Path.GetDirectoryName(filePath) ?? "";
            return GetRelativePath(rootPath, dirPath);
        }

        /// <summary>rootPath から見た相対パス。範囲外・同一なら空文字</summary>
        private static string GetRelativePath(string rootPath, string path)
        {
            var normalizedRoot = rootPath.TrimEnd(Path.DirectorySeparatorChar);

            // 区切り文字まで含めて比較する。接頭辞が同じ兄弟フォルダ
            // (例: ルートが ...\Timeline のとき ...\TimelineBackup) を配下と誤判定しないため
            if (!path.StartsWith(normalizedRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
            {
                return "";
            }

            return path.Substring(normalizedRoot.Length)
                .Trim(Path.DirectorySeparatorChar);
        }

        private static TimelineLoadItem CreateRootItem()
        {
            return new TimelineLoadItem
            {
                name = "Timeline",
                path = rootPath,
                isDir = true,
                children = new List<ITileViewContent>(16),
            };
        }

        /// <summary>1 フォルダ分の読み込み。読めないフォルダがあっても一覧全体は諦めない</summary>
        private static void SearchItems(TimelineLoadItem dirItem, HashSet<string> visitedDirs)
        {
            try
            {
                SearchItemsCore(dirItem, visitedDirs);
            }
            catch (Exception e)
            {
                MTEUtils.LogWarning("タイムラインフォルダを読み込めませんでした: {0}", dirItem.path);
                MTEUtils.LogException(e);
            }
        }

        /// <summary>ファイルを先、フォルダを後に並べる (ScenePresetManager と同じ構成)</summary>
        private static void SearchItemsCore(TimelineLoadItem dirItem, HashSet<string> visitedDirs)
        {
            var xmlPaths = Directory.GetFiles(dirItem.path, "*.xml")
                .OrderBy(path => path, new NaturalStringComparer());
            foreach (var xmlPath in xmlPaths)
            {
                var item = new TimelineLoadItem
                {
                    name = Path.GetFileNameWithoutExtension(xmlPath),
                    path = xmlPath,
                };

                var thumPath = MTEP.PluginUtils.ConvertThumPath(xmlPath);
                if (File.Exists(thumPath))
                {
                    item.thum = TextureUtils.LoadTexture(thumPath);
                }

                dirItem.AddChild(item);
            }

            var dirPaths = Directory.GetDirectories(dirItem.path)
                .OrderBy(path => path, new NaturalStringComparer());
            foreach (var dirPath in dirPaths)
            {
                var childDirItem = new TimelineLoadItem
                {
                    name = Path.GetFileName(dirPath),
                    path = dirPath,
                    isDir = true,
                    // 空フォルダでもタイルビューが children を走査するため必ず実体を持たせる
                    children = new List<ITileViewContent>(16),
                };
                dirItem.AddChild(childDirItem);

                // ジャンクション等が祖先を指していると無限再帰になり、
                // StackOverflowException は握れずゲームごと落ちるため訪問済みは辿らない
                if (visitedDirs.Add(GetCanonicalPath(dirPath)))
                {
                    SearchItems(childDirItem, visitedDirs);
                }
            }
        }

        /// <summary>循環検出用にフォルダパスを正規化する (大文字小文字は HashSet 側で無視する)</summary>
        private static string GetCanonicalPath(string dirPath)
        {
            return Path.GetFullPath(dirPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        /// <summary>ルートからの相対パスでフォルダ項目を探す。見つからなければ null</summary>
        private static TimelineLoadItem FindDirItem(TimelineLoadItem dirItem, string relativeDir)
        {
            if (string.IsNullOrEmpty(relativeDir))
            {
                return dirItem;
            }

            var current = dirItem;
            foreach (var name in relativeDir.Split(Path.DirectorySeparatorChar))
            {
                TimelineLoadItem next = null;
                foreach (var child in current.children)
                {
                    var childItem = child as TimelineLoadItem;
                    if (childItem != null && childItem.isDir &&
                        string.Equals(childItem.name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        next = childItem;
                        break;
                    }
                }

                if (next == null)
                {
                    return null;
                }
                current = next;
            }

            return current;
        }

        /// <summary>作り直し前に古いサムネテクスチャを破棄する (放置すると GPU メモリが積み上がる)</summary>
        private static void ClearThumbnails(TimelineLoadItem dirItem)
        {
            if (dirItem?.children == null)
            {
                return;
            }

            foreach (var child in dirItem.children)
            {
                var childItem = child as TimelineLoadItem;
                if (childItem == null)
                {
                    continue;
                }

                childItem.thum = null;
                ClearThumbnails(childItem);
            }
        }
    }
}
