using System;
using System.Collections.Generic;
using System.IO;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// PNG/JPG 画像を板としてシーンに配置するウィンドウ。
    /// 「画像」タブが Config\PngPlacement と PhotoModeData\Texture の一覧、
    /// 「配置済み」タブが配置した板の一覧。
    /// パラメータの編集は選択して Inspector で行う
    /// </summary>
    public class PngPlacementWindow : EditorSubWindow
    {
        public static readonly int WINDOW_ID = 8903381;

        protected override int windowId => WINDOW_ID;
        protected override string windowTitle => "PNG配置";

        private static readonly int ROW_HEIGHT = 20;
        private static readonly int LABEL_WIDTH = 70;
        private static readonly float TILE_WIDTH = 96f;
        private static readonly float TILE_HEIGHT = 96f;

        private static readonly string[] IMAGE_PATTERNS = { "*.png", "*.jpg" };

        private static readonly int TAB_WIDTH = 100;
        /// <summary>フォルダ行の右端に並べる「開く」「更新」ボタン 1 つ分の幅</summary>
        private static readonly int FOLDER_BUTTON_WIDTH = 50;

        private enum PngTab
        {
            画像,
            配置済み,
        }

        private const string ROOT_NAME = "PNG";

        // 出所をルートへ平坦に展開するため、同名フォルダ・同名画像が並びうる。
        // 写真フォルダ側にだけ印を付けて見分けられるようにする
        private const string PHOTO_TAG = "写真";
        private static readonly Color PHOTO_TAG_COLOR = new Color(0.2f, 0.4f, 0.8f);
        private const string DECAL_LABEL_PREFIX = "[デカール] ";

        /// <summary>フォルダをたどる深さの上限。壊れた階層で暴走させないための保険</summary>
        private const int MAX_DIR_DEPTH = 16;

        /// <summary>
        /// 画像タイル 1 枚分。クリックで配置する。
        /// サムネイルはマネージャのテクスチャキャッシュと共有するため、
        /// 基底の thum setter (旧値を Destroy する) を通さず getter で直接返す
        /// </summary>
        private class PngTileContent : TileViewContentBase
        {
            public string source;
            public string relativePath;

            public override Texture2D thum =>
                PngPlacementManager.instance.GetTexture(source, relativePath);
        }

        /// <summary>
        /// 配置済み 1 枚分のタイル。サムネイルは配置に使ったテクスチャを共有する
        /// </summary>
        private class PlacedTileContent : TileViewContentBase
        {
            public PngObjectData data;
            /// <summary>name に反映済みの表示タイプ。変わったときだけ表示名を作り直す</summary>
            public PngDisplayType labeledType;

            public override Texture2D thum =>
                PngPlacementManager.instance.GetTexture(data.source, data.relativePath);
        }

        /// <summary>
        /// フォルダタイル 1 つ分。同名フォルダでも出所ごとに別ノードになるため、
        /// 実フォルダは常に 1 つに定まる
        /// </summary>
        private class PngDirContent : TileViewContentBase
        {
            /// <summary>対応する実フォルダの絶対パス。「開く」と更新後の復帰に使う</summary>
            public string path;
        }

        /// <summary>
        /// 画像一覧の最上位。2 つの出所の中身を階層ごとここへ展開する
        /// (出所フォルダを挟まず、直下の画像とサブフォルダをそのまま並べる)。
        /// 中身は BuildFileList で必ず組み立て直される
        /// </summary>
        private PngDirContent _tileRoot = CreateDir(ROOT_NAME, null);
        /// <summary>
        /// タイルビューに表示中のフォルダ。フォルダタイルのクリックで潜る。
        /// フォルダノードは CreateDir でしか作らないため、中身は常に PngDirContent
        /// </summary>
        private ITileViewContent _currentDir = null;

        /// <summary>配置済み一覧。要素は PngPlacementManager の一覧に追従させる</summary>
        private readonly TempTileViewContent _placedRoot = new TempTileViewContent
        {
            name = "配置済み",
            isDir = true,
            children = new List<ITileViewContent>(),
        };

        /// <summary>
        /// 検索中の表示用。子の parent を書き換えない TempTileViewContent を使い、
        /// 元の階層構造を壊さずに平坦な一覧を作る
        /// </summary>
        private readonly TempTileViewContent _searchRoot = new TempTileViewContent
        {
            name = "検索結果",
            isDir = true,
            children = new List<ITileViewContent>(),
        };

        private PngTab _tab = PngTab.画像;

        private string _searchText = "";
        private bool isSearching => !string.IsNullOrEmpty(_searchText);

        private bool _fileListDirty = true;

        private readonly GUIView _view = new GUIView();

        private static PngPlacementWindow _instance = null;
        public static PngPlacementWindow instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new PngPlacementWindow();
                }
                return _instance;
            }
        }

        private PngPlacementWindow()
        {
        }

        private static PngDirContent CreateDir(string name, string path)
        {
            return new PngDirContent
            {
                name = name,
                path = path,
                isDir = true,
                children = new List<ITileViewContent>(),
            };
        }


        private static PngPlacementManager pngManager => PngPlacementManager.instance;

        protected override void LoadPlacement(out int x, out int y, out int width, out int height)
        {
            x = config.pngPlacementPosX;
            y = config.pngPlacementPosY;
            width = config.pngPlacementWidth;
            height = config.pngPlacementHeight;
        }

        protected override void StorePlacement(int x, int y, int width, int height)
        {
            config.pngPlacementPosX = x;
            config.pngPlacementPosY = y;
            config.pngPlacementWidth = width;
            config.pngPlacementHeight = height;
        }

        public override bool savedVisible
        {
            get => config.pngPlacementVisible;
            set => config.pngPlacementVisible = value;
        }

        public override bool TryFocusTimelineLayer(Type layerType)
        {
            return layerType == typeof(MTEP.PngPlacementTimelineLayer);
        }

        /// <summary>開くたびに画像一覧を取り直す。開いている間に追加されたファイルを拾うため</summary>
        protected override void OnShowChanged(bool visible)
        {
            if (visible)
            {
                _fileListDirty = true;
            }
        }

        protected override void DrawContent()
        {
            _view.Init(ToLocalRect(contentRect));

            DrawTabs();

            try
            {
                // 画像タブ（配置）も配置済みタブも PNG 配置レイヤーへ記録される
                TimelineLayerGate.Begin(_view, typeof(MTEP.PngPlacementTimelineLayer), ROW_HEIGHT);

                if (_tab == PngTab.配置済み)
                {
                    DrawPlacedTiles();
                }
                else
                {
                    DrawImageTab();
                }
            }
            finally
            {
                TimelineLayerGate.End(_view);
            }
        }

        private void DrawTabs()
        {
            var tab = _view.DrawTabs(_tab, TAB_WIDTH, ROW_HEIGHT);
            // DrawTabs 末尾の AddSpace(5) が縦レイアウトでは「スペース5px + margin」になるため、
            // SettingWindow と同じく通常の行間に合わせて詰める
            _view.currentPos.y -= 5 + GUIView.defaultMargin;

            if (tab != _tab)
            {
                _tab = tab;
                // タイルビューは _view を共用するため、前のタブの
                // スクロール位置を引き継がないよう戻す
                _view.scrollPosition = Vector2.zero;
            }
        }

        private void DrawImageTab()
        {
            if (_fileListDirty)
            {
                BuildFileList();
                _fileListDirty = false;
            }

            DrawSearchRow();
            DrawFolderRow();
            DrawTiles();
        }

        private void DrawSearchRow()
        {
            _view.DrawTextField("検索", LABEL_WIDTH, _searchText, -1, ROW_HEIGHT,
                value =>
                {
                    if (value != _searchText)
                    {
                        // 階層は作り直さない。検索を消したとき元のフォルダへ戻れるようにする
                        _searchText = value;
                        BuildSearchList();
                    }
                });
        }

        /// <summary>上位フォルダへ戻る / フォルダを開く / 一覧更新 + 表示中フォルダ名</summary>
        private void DrawFolderRow()
        {
            _view.BeginHorizontal();
            {
                // ルートでは戻り先が無く、検索中は一覧が階層を表していないため無効化する
                if (_view.DrawButton("<", 20, ROW_HEIGHT, !isSearching && _currentDir != _tileRoot))
                {
                    _currentDir = _currentDir.parent;
                }

                _view.DrawLabel(_currentDir.name, -1, ROW_HEIGHT);

                // currentPos は padding の内側基準なので、右端も padding の内側で合わせる
                _view.currentPos.x = _view.viewRect.width - _view.padding.x * 2
                    - (FOLDER_BUTTON_WIDTH * 2 + _view.margin);

                if (_view.DrawButton("開く", FOLDER_BUTTON_WIDTH, ROW_HEIGHT))
                {
                    OpenCurrentDirectory();
                }

                if (_view.DrawButton("更新", FOLDER_BUTTON_WIDTH, ROW_HEIGHT))
                {
                    ReloadFileList();
                }
            }
            _view.EndLayout();
        }

        /// <summary>画像タイル。クリックで配置し、フォルダタイルはクリックで潜る</summary>
        private void DrawTiles()
        {
            _view.DrawHorizontalLine(Color.gray);
            _view.AddSpace(5);

            var root = isSearching ? (ITileViewContent)_searchRoot : _currentDir;
            if (root.children.Count == 0)
            {
                DrawEmptyMessage();
                return;
            }

            _view.DrawTileView(root, -1, -1, TILE_WIDTH, TILE_HEIGHT, OnTileSelected);
        }

        /// <summary>
        /// 配置済みタイル。クリックで選択し、パラメータの編集は Inspector で行う。
        /// x ボタンで削除する
        /// </summary>
        private void DrawPlacedTiles()
        {
            _view.DrawHorizontalLine(Color.gray);
            _view.AddSpace(5);

            SyncPlacedList();

            if (_placedRoot.children.Count == 0)
            {
                _view.DrawLabel("配置中の画像はありません", -1, ROW_HEIGHT,
                    textColor: Color.gray);
                return;
            }

            _view.DrawTileView(_placedRoot, -1, -1, TILE_WIDTH, TILE_HEIGHT,
                content =>
                {
                    var placed = content as PlacedTileContent;
                    if (placed != null)
                    {
                        SelectPng(placed.data);
                    }
                },
                null,
                content =>
                {
                    var placed = content as PlacedTileContent;
                    if (placed != null)
                    {
                        RemovePng(placed.data);
                    }
                });
        }

        /// <summary>
        /// 配置済みタイルをマネージャの一覧へ追従させる。
        /// 選択枠はウィンドウ内の状態ではなく実際の選択object に合わせる
        /// (Hierarchy 等から選び直しても表示がずれないようにするため)
        /// </summary>
        private void SyncPlacedList()
        {
            var pngObjects = pngManager.pngObjects;
            var children = _placedRoot.children;

            if (!IsPlacedListSynced(pngObjects, children))
            {
                _placedRoot.RemoveAllChildren();
                foreach (var data in pngObjects)
                {
                    _placedRoot.AddChild(new PlacedTileContent
                    {
                        name = GetPlacedLabel(data),
                        labeledType = data.displayType,
                        data = data,
                        canDelete = true,
                    });
                }
            }

            var selectedObject = SelectionManager.instance.selectedObject;
            foreach (PlacedTileContent content in children)
            {
                content.isSelected = content.data.rootObject == selectedObject;
                if (content.labeledType != content.data.displayType)
                {
                    content.labeledType = content.data.displayType;
                    content.name = GetPlacedLabel(content.data);
                }
            }
        }

        private static string GetPlacedLabel(PngObjectData data)
        {
            return data.displayType == PngDisplayType.Decal
                ? DECAL_LABEL_PREFIX + data.name
                : data.name;
        }

        private static bool IsPlacedListSynced(
            List<PngObjectData> pngObjects, List<ITileViewContent> children)
        {
            if (children.Count != pngObjects.Count)
            {
                return false;
            }

            for (var i = 0; i < children.Count; i++)
            {
                if (((PlacedTileContent)children[i]).data != pngObjects[i])
                {
                    return false;
                }
            }
            return true;
        }

        private void DrawEmptyMessage()
        {
            if (isSearching)
            {
                _view.DrawLabel("一致する画像がありません", -1, ROW_HEIGHT,
                    textColor: Color.yellow);
                return;
            }

            _view.DrawLabel("画像がありません", -1, ROW_HEIGHT, textColor: Color.yellow);
            _view.DrawLabel("Config\\PngPlacement または PhotoModeData\\Texture に",
                -1, ROW_HEIGHT, textColor: Color.gray);
            _view.DrawLabel("PNG/JPG を配置してください", -1, ROW_HEIGHT,
                textColor: Color.gray);
        }

        /// <summary>
        /// 画像一覧を作り直す。2 つの出所の中身をルートへ展開し、
        /// サブフォルダは実フォルダの階層をそのまま写す。
        /// サムネイルは配置と同じテクスチャキャッシュを使う
        /// </summary>
        private void BuildFileList()
        {
            // ルートは 2 つの出所を合わせた仮想フォルダ。「開く」ではプラグイン側の出所を開く
            _tileRoot = CreateDir(ROOT_NAME,
                PngPlacementManager.GetSourceDirectory(PngPlacementManager.SOURCE_CONFIG));
            AddSource(PngPlacementManager.SOURCE_CONFIG);
            AddSource(PngPlacementManager.SOURCE_PHOTO);
            _currentDir = _tileRoot;

            BuildSearchList();
        }

        /// <summary>一覧を取り直し、表示中のフォルダがまだあればそこへ戻す</summary>
        private void ReloadFileList()
        {
            var currentPath = ((PngDirContent)_currentDir).path;
            BuildFileList();

            var dir = FindDir(_tileRoot, currentPath);
            if (dir != null)
            {
                _currentDir = dir;
            }
        }

        private static PngDirContent FindDir(PngDirContent node, string path)
        {
            if (string.Equals(node.path, path, StringComparison.OrdinalIgnoreCase))
            {
                return node;
            }

            foreach (var child in node.children)
            {
                var childDir = child as PngDirContent;
                if (childDir == null)
                {
                    continue;
                }

                var found = FindDir(childDir, path);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }

        /// <summary>
        /// 表示中のフォルダをエクスプローラーで開く。
        /// ルートの出所フォルダは未作成のことがあり、画像を置く先として作ってから開く
        /// </summary>
        private void OpenCurrentDirectory()
        {
            var path = ((PngDirContent)_currentDir).path;
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            if (_currentDir == _tileRoot && !Directory.Exists(path))
            {
                try
                {
                    Directory.CreateDirectory(path);
                }
                catch (Exception e)
                {
                    MTEUtils.LogWarning("フォルダを作成できません: {0} ({1})", path, e.Message);
                    return;
                }
            }

            MTEUtils.OpenDirectory(path);
        }

        private void AddSource(string source)
        {
            var dir = PngPlacementManager.GetSourceDirectory(source);
            if (dir == null || !Directory.Exists(dir))
            {
                return;
            }

            AddDirectory(_tileRoot, dir, dir, source, 0);
        }

        /// <summary>
        /// 1 フォルダ分を再帰的に写し、配下の画像枚数を返す。
        /// 空のサブフォルダは畳んで出さない。
        /// 読めないフォルダは飛ばして残りの列挙を続ける (一覧を丸ごと失わせない)
        /// </summary>
        private static int AddDirectory(
            TileViewContentBase node, string rootDir, string currentDir, string source,
            int depth)
        {
            var count = 0;

            try
            {
                foreach (var pattern in IMAGE_PATTERNS)
                {
                    foreach (var path in Directory.GetFiles(currentDir, pattern))
                    {
                        var tile = new PngTileContent
                        {
                            name = Path.GetFileNameWithoutExtension(path),
                            source = source,
                            relativePath = path.Substring(rootDir.Length).TrimStart('\\', '/'),
                        };
                        ApplySourceTag(tile, source);
                        node.AddChild(tile);
                        count++;
                    }
                }

                if (depth >= MAX_DIR_DEPTH)
                {
                    MTEUtils.LogWarning("フォルダが深すぎるため打ち切ります: {0}", currentDir);
                    return count;
                }

                foreach (var subDir in Directory.GetDirectories(currentDir))
                {
                    // ジャンクション・シンボリックリンクは自分の祖先を指しうる。
                    // 辿ると再帰が終わらずスタックを食い潰すため入らない
                    if (IsReparsePoint(subDir))
                    {
                        continue;
                    }

                    var child = CreateDir(Path.GetFileName(subDir), subDir);
                    ApplySourceTag(child, source);
                    var childCount = AddDirectory(child, rootDir, subDir, source, depth + 1);
                    if (childCount > 0)
                    {
                        node.AddChild(child);
                        count += childCount;
                    }
                }
            }
            catch (UnauthorizedAccessException e)
            {
                MTEUtils.LogWarning("フォルダを読み込めません: {0} ({1})", currentDir, e.Message);
            }
            catch (IOException e)
            {
                MTEUtils.LogWarning("フォルダを読み込めません: {0} ({1})", currentDir, e.Message);
            }

            return count;
        }

        /// <summary>写真フォルダ由来のタイルに印を付ける。Config 側は無印</summary>
        private static void ApplySourceTag(TileViewContentBase content, string source)
        {
            if (source == PngPlacementManager.SOURCE_PHOTO)
            {
                content.tag = PHOTO_TAG;
                content.tagColor = PHOTO_TAG_COLOR;
            }
        }

        /// <summary>属性を読めないフォルダは辿らせない (壊れたリンク等)</summary>
        private static bool IsReparsePoint(string path)
        {
            try
            {
                return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
            }
            catch (UnauthorizedAccessException)
            {
                return true;
            }
            catch (IOException)
            {
                return true;
            }
        }

        /// <summary>検索結果を作り直す。全階層のファイルから名前で絞る</summary>
        private void BuildSearchList()
        {
            _searchRoot.RemoveAllChildren();
            if (!isSearching)
            {
                return;
            }

            var files = new List<ITileViewContent>();
            _tileRoot.GetAllFiles(files);

            foreach (var file in files)
            {
                if (file.name.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _searchRoot.AddChild(file);
                }
            }
        }

        private void OnTileSelected(ITileViewContent content)
        {
            if (content.isDir)
            {
                _currentDir = content;
                return;
            }

            var tile = content as PngTileContent;
            if (tile == null)
            {
                return;
            }

            HistoryManager.instance.BeforeEdit(null, HistoryScope.PngPlacement,
                "PNG配置: " + tile.name);
            var data = pngManager.AddPng(tile.source, tile.relativePath);
            if (data != null)
            {
                SelectPng(data);
            }
        }

        /// <summary>
        /// Inspector・ギズモで動かせるよう選択も切り替える。
        /// 新規配置・一覧からの再選択のどちらも画像が画面外にあることが多いので
        /// SceneView も対象へ寄せる
        /// </summary>
        private static void SelectPng(PngObjectData data)
        {
            SelectionManager.instance.Select(data.rootObject, true, true);
        }

        private void RemovePng(PngObjectData data)
        {
            HistoryManager.instance.BeforeEdit(null, HistoryScope.PngPlacement,
                "PNG削除: " + data.name);

            // 消した配置物を Inspector に残さない
            if (SelectionManager.instance.selectedObject == data.rootObject)
            {
                SelectionManager.instance.Select(null);
            }
            pngManager.RemovePng(data);
        }
    }
}
