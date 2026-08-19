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
    /// タイルビューに並べる項目 1 件。サムネは Reload 時に読み込む。
    /// path はファイル項目なら本体 XML のパス、フォルダ項目ならフォルダ自身のパス
    /// </summary>
    public class ScenePresetItem : TileViewContentBase
    {
        public string path;

        /// <summary>SceneCapture プリセット由来の項目か。読み込み専用で、適用経路も専用になる</summary>
        public bool isSceneCapture;

        /// <summary>保存先にできないフォルダか（SceneCapture 仮想フォルダとその配下）</summary>
        public bool isReadonlyDir;

        // 自動ロード指定 (ホームアイコン)。実体は Config の自動ロードキー 1 件のみで、
        // ON にすると他の指定は外れる
        public override bool isFavorite
        {
            get => ScenePresetManager.IsAutoLoadTarget(this);
            set => ScenePresetManager.SetAutoLoadTarget(this, value);
        }
    }

    /// <summary>
    /// シーンプリセット（配置・ポーズ・表情・カメラ）の保存/適用とファイル管理。
    /// プリセットは XML + サムネ PNG の同名ペアで Config\ScenePreset に保存する
    /// </summary>
    public static class ScenePresetManager
    {
        /// <summary>プリセット名の最大長。ポーズ保存と同じ制限に合わせる</summary>
        private const int MAX_PRESET_NAME_LENGTH = 250;

        // サムネの保存サイズ (16:9)
        public const int THUM_WIDTH = 240;
        public const int THUM_HEIGHT = 135;

        /// <summary>ポーズ anm サイドカーの拡張子</summary>
        private const string POSE_ANM_EXTENSION = "anm";

        public static string presetFolderPath
            => Path.Combine(PluginUtils.PluginDataPath, "ScenePreset");

        /// <summary>SceneCapture プラグインのプリセットフォルダ。同じ Config 配下に同居している</summary>
        public static string sceneCapturePresetsPath
            => Path.Combine(PluginUtils.UserDataPath, Path.Combine("SceneCapture", "Presets"));

        private static readonly XmlSerializer _serializer = new XmlSerializer(typeof(ScenePresetData));

        /// <summary>タイルビュー用のツリールート。children が最上位のプリセット / フォルダ一覧</summary>
        public static ScenePresetItem rootItem { get; private set; } = CreateRootItem();

        /// <summary>タイルビューに表示中のフォルダ。UI からのフォルダ移動で書き換える</summary>
        public static ScenePresetItem currentDirItem { get; set; } = rootItem;

        /// <summary>表示中フォルダの絶対パス。保存先・「開く」の対象になる</summary>
        public static string currentDirPath
            => currentDirItem != null ? currentDirItem.path : presetFolderPath;

        private static bool _loaded = false;

        private static Config config => ConfigManager.instance.config;

        /// <summary>
        /// 最後に読み込み / 保存したプリセットの、プリセットフォルダからの相対パス（拡張子なし）。
        /// 一覧での枠表示に使う。ゲーム開始時は「未選択」から始めたいため永続化しない
        /// </summary>
        public static string currentPresetKey { get; private set; } = "";

        /// <summary>保存ポップアップの既定名に使うファイル名部分</summary>
        public static string currentPresetName => Path.GetFileName(currentPresetKey);

        /// <summary>一覧のタイルへ選択状態（枠表示）を反映する</summary>
        private static void UpdateSelection(ScenePresetItem dirItem)
        {
            if (dirItem.children == null)
            {
                return;
            }
            foreach (var child in dirItem.children.OfType<ScenePresetItem>())
            {
                if (child.isDir)
                {
                    UpdateSelection(child);
                    continue;
                }
                // SceneCapture はプリセットフォルダ外でキーが空になり、
                // 未選択状態 (currentPresetKey = "") と一致してしまうため対象外にする
                child.isSelected = !child.isSceneCapture
                    && IsSamePresetKey(GetPresetKey(child.path), currentPresetKey);
            }
        }

        private static MaidManipulateManager maidManager => MaidManipulateManager.instance;

        private static CharacterMgr characterMgr => GameMain.Instance.CharacterMgr;

        /// <summary>新規呼出したメイドへの保留適用。ロード完了後に ApplyMaid する</summary>
        private static readonly List<KeyValuePair<Maid, ScenePresetMaid>> _pendingApplies
            = new List<KeyValuePair<Maid, ScenePresetMaid>>();

        /// <summary>
        /// AssignMaids が確定させたスロットと実メイドの対応（適用の完了有無は問わない）。
        /// 視線はロード完了後にまとめて適用するため、そのときに guid で引き直すと
        /// guid 不一致でフォールバック割当されたメイド（他人のプリセット等）を取りこぼす。
        /// そのため割当結果を保持しておき、視線の適用時はこの対応で引く
        /// </summary>
        private static readonly List<KeyValuePair<Maid, ScenePresetMaid>> _resolvedAssignments
            = new List<KeyValuePair<Maid, ScenePresetMaid>>();

        /// <summary>外部プロバイダへの保留適用。全メイドのロード完了後に ApplyExternals する</summary>
        private static ScenePresetData _pendingExternalsData;

        /// <summary>プリセットの適用が完了していない（メイドのロード待ち）か。UI の操作抑止に使う</summary>
        public static bool isLoading => _pendingApplies.Count > 0 || _pendingExternalsData != null;

        // 読込トグル (ロード時に反映するカテゴリ)。特定の適用に対する一時的な絞り込みであり、
        // 前回の OFF が残ったまま適用して要素が欠けるのを避けるため Config には永続化せず、
        // ゲーム起動のたびに全 ON へ戻す。
        // loadBackground は背景・ライト・PNG 配置をまとめた「背景」カテゴリ
        public static bool loadCamera { get; set; } = true;
        public static bool loadMaids { get; set; } = true;
        public static bool loadBackground { get; set; } = true;

        /// <summary>読込を無効化したプロバイダ id。上のトグルと同じくセッション中だけ保持する</summary>
        private static readonly HashSet<string> _loadDisabledProviders = new HashSet<string>();

        /// <summary>プロバイダの状態をロード時に反映するか</summary>
        public static bool IsProviderLoadEnabled(string providerId)
        {
            return !_loadDisabledProviders.Contains(providerId);
        }

        /// <summary>プロバイダの読込可否を記録する。UI のトグルから呼ばれる</summary>
        public static void SetProviderLoadEnabled(string providerId, bool enabled)
        {
            if (enabled)
            {
                _loadDisabledProviders.Remove(providerId);
            }
            else
            {
                _loadDisabledProviders.Add(providerId);
            }
        }

        // 適用可否は「保存されているか (data.saved*)」と「読み込む設定か (load*)」の AND。
        // v15 以前のプリセットは saved* が既定 true で読まれるため、従来どおり読込トグルだけで決まる

        /// <summary>カメラをこのプリセットから適用するか</summary>
        private static bool ShouldApplyCamera(ScenePresetData data)
        {
            return data.savedCamera && loadCamera;
        }

        /// <summary>メイド (呼出・解除・ポーズ・視線・フォーカス) を適用するか</summary>
        private static bool ShouldApplyMaids(ScenePresetData data)
        {
            return data.savedMaids && loadMaids;
        }

        /// <summary>背景カテゴリ (背景・ライト・PNG 配置) を適用するか</summary>
        private static bool ShouldApplyBackground(ScenePresetData data)
        {
            return data.savedBackground && loadBackground;
        }

        private static ScenePresetItem CreateRootItem()
        {
            return new ScenePresetItem
            {
                name = "ScenePreset",
                path = presetFolderPath,
                isDir = true,
                children = new List<ITileViewContent>(16),
            };
        }

        /// <summary>初回参照時だけ一覧を読み込む。以後は保存/削除時に Reload する</summary>
        public static ScenePresetItem GetOrLoadCurrentDirItem()
        {
            if (!_loaded)
            {
                Reload();
            }
            return currentDirItem;
        }

        /// <summary>一覧を再構築する。GUI から呼ばれるため例外は握って空のまま返す</summary>
        public static void Reload()
        {
            // 「更新」ボタンで、後からロードされたプラグインのトグルも出せるようにする
            ScenePresetProviderRegistry.Refresh();

            _loaded = true;

            // 作り直しで表示中フォルダの実体が入れ替わるため、相対パスで控えて後から再解決する。
            // SceneCapture 仮想フォルダは presetFolderPath の外にあるため基準を分ける
            var wasSceneCapture = IsUnderSceneCapture(currentDirPath);
            var currentRelativeDir = wasSceneCapture
                ? GetRelativePathFrom(sceneCapturePresetsPath, currentDirPath)
                : GetRelativePath(currentDirPath);

            // 古いサムネテクスチャを解放してから作り直す
            ClearThumbnails(rootItem);
            rootItem = CreateRootItem();
            currentDirItem = rootItem;

            try
            {
                var visitedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    GetCanonicalPath(presetFolderPath),
                };
                // プリセットフォルダ未作成でも SceneCapture の一覧は出せるようにする
                if (Directory.Exists(presetFolderPath))
                {
                    SearchItems(rootItem, visitedDirs);
                }
                AddSceneCaptureItems(rootItem, visitedDirs);
                UpdateSelection(rootItem);

                // 保存・削除・更新の前後で見ているフォルダを維持する（消えていればルートへ戻す）
                var searchRoot = wasSceneCapture ? FindSceneCaptureRootItem() : rootItem;
                currentDirItem = (searchRoot != null
                    ? FindDirItem(searchRoot, currentRelativeDir) : null) ?? rootItem;
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
            }
        }

        /// <summary>
        /// SceneCapture のプリセットを読み込み専用の仮想フォルダとしてツリーへ追加する。
        /// フォルダが無い環境では何も足さない
        /// </summary>
        private static void AddSceneCaptureItems(
            ScenePresetItem targetRootItem, HashSet<string> visitedDirs)
        {
            if (!Directory.Exists(sceneCapturePresetsPath))
            {
                return;
            }

            var dirItem = new ScenePresetItem
            {
                name = "SceneCapture",
                path = sceneCapturePresetsPath,
                isDir = true,
                isSceneCapture = true,
                isReadonlyDir = true,
                children = new List<ITileViewContent>(16),
            };
            targetRootItem.AddChild(dirItem);

            if (visitedDirs.Add(GetCanonicalPath(sceneCapturePresetsPath)))
            {
                SearchItems(dirItem, visitedDirs);
                MarkSceneCaptureItems(dirItem);
            }
        }

        /// <summary>仮想フォルダ配下の全項目へ SceneCapture / Readonly の属性を伝播する</summary>
        private static void MarkSceneCaptureItems(ScenePresetItem dirItem)
        {
            if (dirItem.children == null)
            {
                return;
            }
            foreach (var child in dirItem.children.OfType<ScenePresetItem>())
            {
                child.isSceneCapture = true;
                if (child.isDir)
                {
                    child.isReadonlyDir = true;
                    MarkSceneCaptureItems(child);
                }
                else
                {
                    // 読み込み専用: 削除ボタンと自動ロード指定を出さない
                    child.canDelete = false;
                    child.canFavorite = false;
                }
            }
        }

        /// <summary>ツリー直下の SceneCapture 仮想フォルダを返す。無ければ null</summary>
        private static ScenePresetItem FindSceneCaptureRootItem()
        {
            return rootItem.children.OfType<ScenePresetItem>()
                .FirstOrDefault(child => child.isDir && child.isSceneCapture);
        }

        /// <summary>
        /// SceneCapture 仮想フォルダ配下のパスか。サイドカー除外と保存抑止の判定に使う。
        /// 同名接頭辞の別フォルダ（例: Presets と PresetsOld）を誤判定しないよう、
        /// ルート自身との一致か、区切り文字付きの前方一致で判定する
        /// </summary>
        private static bool IsUnderSceneCapture(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }
            var root = GetCanonicalPath(sceneCapturePresetsPath);
            var target = GetCanonicalPath(path);
            return target.Equals(root, StringComparison.OrdinalIgnoreCase)
                || target.StartsWith(root + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// フォルダ配下を再帰的に走査してタイル項目を作る。
        /// 権限エラーや壊れたジャンクションで 1 フォルダが読めなくても
        /// 一覧全体が空にならないよう、フォルダ単位で例外を握る
        /// </summary>
        private static void SearchItems(ScenePresetItem dirItem, HashSet<string> visitedDirs)
        {
            try
            {
                SearchItemsCore(dirItem, visitedDirs);
            }
            catch (Exception e)
            {
                MTEUtils.LogWarning("プリセットフォルダを読み込めませんでした: {0}", dirItem.path);
                MTEUtils.LogException(e);
            }
        }

        /// <summary>ファイルを先、フォルダを後に並べる（MTE の TimelineLoadManager と同じ構成）</summary>
        private static void SearchItemsCore(ScenePresetItem dirItem, HashSet<string> visitedDirs)
        {
            var isUnderSceneCapture = IsUnderSceneCapture(dirItem.path);

            var xmlPaths = Directory.GetFiles(dirItem.path, "*.xml")
                .OrderBy(path => path, new NaturalStringComparer());
            foreach (var xmlPath in xmlPaths)
            {
                // サイドカー (<プリセット名>.<キー>.xml) はプリセット本体ではないため一覧に出さない。
                // SceneCapture 側にはサイドカー規約が無く、ドット入りのプリセット名
                // (例: HRK preset v2.0.xml) が普通にあるため除外しない
                if (!isUnderSceneCapture && IsSidecarXmlPath(xmlPath))
                {
                    continue;
                }

                var item = new ScenePresetItem
                {
                    name = Path.GetFileNameWithoutExtension(xmlPath),
                    path = xmlPath,
                    canDelete = true,
                    // 自動ロード指定のホームアイコンを出す (ファイル項目のみ)
                    canFavorite = true,
                };
                var thumPath = GetThumFilePath(xmlPath);
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
                var childDirItem = new ScenePresetItem
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

        /// <summary>循環検出用にフォルダパスを正規化する（大文字小文字は HashSet 側で無視する）</summary>
        private static string GetCanonicalPath(string dirPath)
        {
            return Path.GetFullPath(dirPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        /// <summary>配下のサムネテクスチャをまとめて解放する</summary>
        private static void ClearThumbnails(ScenePresetItem dirItem)
        {
            if (dirItem.children == null)
            {
                return;
            }
            foreach (var child in dirItem.children.OfType<ScenePresetItem>())
            {
                child.thum = null;
                ClearThumbnails(child);
            }
        }

        /// <summary>相対パスに対応するフォルダ項目を探す。見つからなければ null</summary>
        private static ScenePresetItem FindDirItem(ScenePresetItem dirItem, string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath))
            {
                return dirItem;
            }

            var current = dirItem;
            var names = relativePath.Split(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            foreach (var name in names)
            {
                if (name.Length == 0)
                {
                    continue;
                }
                var next = current.children.OfType<ScenePresetItem>()
                    .FirstOrDefault(child => child.isDir && child.name == name);
                if (next == null)
                {
                    return null;
                }
                current = next;
            }
            return current;
        }

        public static bool Exists(string presetName)
        {
            return File.Exists(GetPresetFilePath(presetName));
        }

        /// <summary>プリセット名の検証。問題があればエラーメッセージ、なければ null を返す</summary>
        public static string ValidatePresetName(string presetName)
        {
            if (string.IsNullOrEmpty(presetName))
            {
                return "プリセット名を入力してください";
            }
            if (presetName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                return "プリセット名に使用できない文字が含まれています";
            }
            // 外部サイドカー (<プリセット名>.<プロバイダid>.xml) との判別にドットを使うため禁止
            if (presetName.IndexOf('.') >= 0)
            {
                return "プリセット名にドット (.) は使用できません";
            }
            if (presetName.Length > MAX_PRESET_NAME_LENGTH)
            {
                return "プリセット名が長すぎます（" + MAX_PRESET_NAME_LENGTH + "文字まで）";
            }
            return null;
        }

        /// <summary>
        /// 選択されたカテゴリだけを XML + サムネで保存し、一覧を更新する。
        /// 保存先は一覧で表示中のフォルダ
        /// </summary>
        public static void SavePreset(string presetName, ScenePresetSaveOptions options)
        {
            // UI の抑止をすり抜けても SceneCapture 配下には書き込まない
            if (IsUnderSceneCapture(currentDirPath))
            {
                MTEUtils.LogWarning("SceneCapture フォルダは読み込み専用のため保存できません");
                return;
            }

            try
            {
                var data = Capture(options);
                var xmlPath = GetPresetFilePath(presetName);
                var dirPath = Path.GetDirectoryName(xmlPath);

                if (!Directory.Exists(dirPath))
                {
                    Directory.CreateDirectory(dirPath);
                }

                // 上書き前のサイドカーを控えておき、書き込み成功後に不要分だけ消す。
                // 先に消すと途中失敗時に旧サイドカーだけ失われるため
                var oldData = File.Exists(xmlPath) ? LoadPresetData(xmlPath) : null;

                // ペイロードはサイドカーへ出す。ModItemExplorer 等のプリセットファイルと
                // ファイル単位で相互流用できるようにするため
                WriteSidecars(data, presetName, xmlPath);

                using (var stream = File.Create(xmlPath))
                {
                    _serializer.Serialize(stream, data);
                }

                DeleteStaleSidecars(xmlPath, oldData, data);

                SaveThumbnail(GetThumFilePath(xmlPath));

                MTEUtils.Log("プリセットを保存しました: {0}", GetPresetKey(xmlPath));
                // 保存したプリセットが以後の「読み込み中」扱いになる
                currentPresetKey = GetPresetKey(xmlPath);
                Reload();
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
                DialogPopupWindow.ShowDialog("プリセットの保存に失敗しました");
            }
        }

        /// <summary>プリセットを読み込んで現在のシーンへ適用する</summary>
        public static void LoadPreset(ScenePresetItem item)
        {
            LoadPreset(item, null);
        }

        /// <summary>
        /// プリセットを現在のシーンへ適用する。
        /// preloaded に先出し適用済みのデータを渡すと、再パースと
        /// 背景・カメラ・ライトの再適用を省く
        /// </summary>
        private static void LoadPreset(ScenePresetItem item, ScenePresetData preloaded)
        {
            try
            {
                // SceneCapture プリセットはフォーマットも適用経路も別物のため専用処理へ
                if (item.isSceneCapture)
                {
                    ApplySceneCapturePreset(item);
                    // SceneEditor 形式のキー体系に乗らないため未選択へ戻す
                    currentPresetKey = "";
                    UpdateSelection(rootItem);
                    return;
                }

                var data = preloaded;
                if (data == null)
                {
                    data = LoadPresetData(item.path);
                    ResolveSidecars(data, item.path);
                }
                Apply(data, skipScenery: preloaded != null);
                // 読み込みは一覧を作り直さないため、選択状態だけ更新する
                currentPresetKey = GetPresetKey(item.path);
                UpdateSelection(rootItem);
                MTEUtils.Log("プリセットを適用しました: {0}", item.name);
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
                DialogPopupWindow.ShowDialog("プリセットの読み込みに失敗しました");
            }
        }

        /// <summary>
        /// SceneCapture プリセットを適用する。カメラ・背景・ライトは本体で、
        /// Models / Effects は ApplySceneCaptureXml を実装した外部プロバイダへ委譲する。
        /// メイドには一切触らない
        /// </summary>
        private static void ApplySceneCapturePreset(ScenePresetItem item)
        {
            var converted = SceneCapturePresetLoader.Parse(File.ReadAllText(item.path));

            // シーンの見た目を書き換えるため、既存の履歴は復元先を失う
            HistoryManager.instance.ClearHistory();
            MTEUtils.Log("SceneCapture プリセット適用のため操作履歴をクリアしました");

            if (loadCamera)
            {
                CameraSnapshot.ApplyState(converted.camera);
            }
            if (loadBackground)
            {
                BackgroundSnapshot.ApplyState(converted.background);
                LightSnapshot.ApplyState(converted.light);
            }

            if (converted.hasModels || converted.hasEffects)
            {
                ApplySceneCaptureExternals(converted.rawXml);
            }

            MTEUtils.Log("SceneCapture プリセットを適用しました: {0}", item.name);
        }

        /// <summary>
        /// ApplySceneCaptureXml を実装している全プロバイダへ生 XML を渡す。
        /// どのセクションを読むかはプロバイダの責務。1 件の失敗で他を止めない
        /// </summary>
        private static void ApplySceneCaptureExternals(string rawXml)
        {
            // 保存ポップアップを開かずに読み込んだ場合、遅延ロードされたプラグインを
            // 取りこぼすためここでも走査し直す
            ScenePresetProviderRegistry.Refresh();

            var handled = false;
            // トグルで切られただけの場合に「見つかりません」と誤解させないための区別
            var skippedByOption = false;
            foreach (var provider in ScenePresetProviderRegistry.providers)
            {
                if (provider.applySceneCaptureXml == null)
                {
                    continue;
                }
                if (!IsProviderLoadEnabled(provider.id))
                {
                    skippedByOption = true;
                    continue;
                }
                handled = true;
                try
                {
                    if (!provider.applySceneCaptureXml(rawXml))
                    {
                        MTEUtils.LogWarning(
                            "SceneCapture プリセットの適用に失敗しました: {0}", provider.id);
                    }
                }
                catch (Exception e)
                {
                    MTEUtils.LogError("SceneCapture プリセットの適用に失敗しました: " + provider.id);
                    MTEUtils.LogException(e);
                }
            }

            if (!handled && !skippedByOption)
            {
                MTEUtils.LogWarning(
                    "SceneCapture のモデル・エフェクトを適用できる外部プラグインが見つかりません");
            }
        }

        /// <summary>XML とサムネを削除して一覧を更新する。確認は UI 側の責務</summary>
        public static void DeletePreset(ScenePresetItem item)
        {
            // フォルダは削除対象にしない（UI の x ボタンはファイルにしか出ないが、念のため）
            if (item == null || item.isDir)
            {
                return;
            }

            // UI の抑止をすり抜けても SceneCapture 側のファイルは消さない（SavePreset と対）
            if (item.isSceneCapture || IsUnderSceneCapture(item.path))
            {
                MTEUtils.LogWarning("SceneCapture フォルダは読み込み専用のため削除できません");
                return;
            }

            try
            {
                DeleteSidecars(item.path);
                if (File.Exists(item.path))
                {
                    File.Delete(item.path);
                }
                var thumPath = GetThumFilePath(item.path);
                if (File.Exists(thumPath))
                {
                    File.Delete(thumPath);
                }
                // 消したプリセットを読み込み中のまま残さない
                if (IsSamePresetKey(currentPresetKey, GetPresetKey(item.path)))
                {
                    currentPresetKey = "";
                }
                // 消したプリセットを自動ロード対象のまま残さない
                if (IsSamePresetKey(config.scenePresetAutoLoadKey, GetPresetKey(item.path)))
                {
                    config.scenePresetAutoLoadKey = "";
                    config.dirty = true;
                }
                Reload();
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
                DialogPopupWindow.ShowDialog("プリセットの削除に失敗しました");
            }
        }

        /// <summary>現在のシーン状態から、選択されたカテゴリだけプリセットデータを組み立てる</summary>
        private static ScenePresetData Capture(ScenePresetSaveOptions options)
        {
            var data = new ScenePresetData();

            // 適用時に「保存していない」と「保存した結果が空」を区別できるよう、選択内容を残す
            data.savedCamera = options.saveCamera;
            data.savedMaids = options.saveMaids;
            data.savedBackground = options.saveBackground;

            if (options.saveCamera)
            {
                data.camera = CameraSnapshot.CaptureState();
            }

            if (options.saveMaids)
            {
                foreach (var maid in maidManager.calledMaids)
                {
                    data.maids.Add(CaptureMaid(maid));
                }
            }

            // ライトと PNG 配置は UI 上「背景」カテゴリにまとめている
            if (options.saveBackground)
            {
                data.background = BackgroundSnapshot.CaptureState();
                data.light = LightSnapshot.CaptureState();
                data.pngPlacement = PngPlacementSnapshot.CaptureState();
            }

            CaptureExternals(data, options.enabledProviderIds);
            CaptureModelBoneEdits(data);

            return data;
        }

        /// <summary>
        /// 選択された外部プロバイダの状態を収集する。
        /// プロバイダの例外は 1 件ごとに握り、他カテゴリの保存は続行する
        /// </summary>
        private static void CaptureExternals(ScenePresetData data, List<string> enabledProviderIds)
        {
            if (enabledProviderIds == null)
            {
                return;
            }

            foreach (var providerId in enabledProviderIds)
            {
                var provider = ScenePresetProviderRegistry.GetProvider(providerId);
                if (provider == null)
                {
                    continue;
                }

                try
                {
                    var external = new ScenePresetExternal { id = provider.id };
                    if (provider.isBinary)
                    {
                        external.binaryPayload = provider.captureBinary();
                        // 保存する状態が無い場合は external ごと記録しない
                        if (external.binaryPayload == null || external.binaryPayload.Length == 0)
                        {
                            continue;
                        }
                    }
                    else
                    {
                        external.payload = provider.captureXml();
                        if (string.IsNullOrEmpty(external.payload))
                        {
                            continue;
                        }
                    }
                    data.externals.Add(external);
                }
                catch (Exception e)
                {
                    MTEUtils.LogError("外部プラグインの状態取得に失敗しました: " + provider.id);
                    MTEUtils.LogException(e);
                }
            }
        }

        /// <summary>
        /// 外部プラグイン配置モデルのボーン編集差分を収集する (v18)。
        /// モデルは GameObject 参照でしか識別できないため、ModelProviderHost の
        /// 現在の一覧と突き合わせて GameObject 名 + 提供プラグイン名へ変換して保存する。
        /// 提供元が見つからないモデル (破棄済み・提供解除済み) は保存しない。
        /// モデル自体は外部プロバイダが復元するため、外部プロバイダを 1 つも保存して
        /// いないプリセットにボーン差分だけ残しても復元先が無い。孤立データを避けるため
        /// external が空のときは保存しない
        /// (プロバイダ id と ModelProviderHost の pluginName は別体系のため個別対応は取らない)
        /// </summary>
        private static void CaptureModelBoneEdits(ScenePresetData data)
        {
            if (data.externals.Count == 0)
            {
                return;
            }

            var storeEntries = BoneEditManager.instance.GetModelStoreEntries();
            if (storeEntries.Count == 0)
            {
                return;
            }

            var models = ModelProviderHost.GetModels();
            foreach (var pair in storeEntries)
            {
                var entry = models.Find(m => m.obj == pair.Key);
                if (entry == null)
                {
                    MTEUtils.LogWarning(
                        "提供元が見つからないモデルのボーン編集は保存しません: {0}", pair.Key.name);
                    continue;
                }

                foreach (var edit in pair.Value.GetAllEntries())
                {
                    if (data.modelBoneEdits == null)
                    {
                        data.modelBoneEdits = new List<ScenePresetModelBoneEdit>();
                    }
                    data.modelBoneEdits.Add(ScenePresetModelBoneEdit.FromEntry(
                        pair.Key.name, entry.pluginName, edit));
                }
            }
        }

        private static ScenePresetMaid CaptureMaid(Maid maid)
        {
            var state = new ScenePresetMaid();
            if (maid == null)
            {
                return state;
            }

            state.guid = maid.status != null ? maid.status.guid : null;

            // 退避中は実座標が退避先で埋まっているため、見かけ上の位置（戻り先）を記録する。
            // 座標系は既存の配置系と同じ SetPos/GetPos（ローカル）で統一する
            state.position = maidManager.GetLogicalPosition(maid);
            state.rotation = maid.GetRot();
            state.visible = maidManager.IsVisible(maid);

            // ボディ未ロード中はポーズ・表情を取得できないため配置だけ記録する
            if (maid.body0 == null || !maid.body0.isLoadedBody)
            {
                return state;
            }

            // 1 体の失敗で保存全体が止まらないよう、姿勢の記録はまとめて握りつぶす
            try
            {
                // モーション再生中はポーズを固めず、モーションそのものを記録する (v13)。
                // 再生中の 1 フレームを anm 化しても、適用時には静止ポーズにしかならない
                state.motion = CaptureMotion(maid);
                if (state.motion == null)
                {
                    state.poseAnmBinary = MaidPoseFileManager.CapturePoseBinary(maid);
                }
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
            }

            state.mabataki = MaidFaceMorphController.GetMabataki(maid);

            foreach (FaceMorphCategory category in Enum.GetValues(typeof(FaceMorphCategory)))
            {
                foreach (var def in MaidFaceMorphController.GetAvailableMorphs(maid, category))
                {
                    var value = MaidFaceMorphController.GetMorphValue(maid, def);
                    if (value != 0f)
                    {
                        state.morphs.Add(new ScenePresetMorph { name = def.name, value = value });
                    }
                }
            }

            state.undress = new ScenePresetUndress
            {
                slots = MaidUndressController.CaptureUndressedSlots(maid),
                costumeTypes = MaidUndressController.CaptureCostumeTypes(maid),
            };

            state.gravity = CaptureGravity(maid);

            CaptureIKHold(maid, state);
            CaptureLook(maid, state);

            // ボーン編集差分 (v6)。編集がなければ null のままにして旧バージョン互換の形に保つ
            var boneStore = BoneEditManager.instance.FindStore(maid);
            if (boneStore != null && !boneStore.isEmpty)
            {
                state.boneEdits = boneStore.GetAllEntries()
                    .Select(ScenePresetBoneEdit.FromEntry).ToList();
            }

            // 揺れ物理の状態 (v17)。対象の物理が無いスロットは記録しない
            foreach (var slotName in SlotBoneManager.GetLoadedSlotNames(maid))
            {
                var snapshot = SlotYureUtil.CaptureSnapshot(maid, slotName);
                if (snapshot == null)
                {
                    continue;
                }
                if (state.slotYures == null)
                {
                    state.slotYures = new List<ScenePresetSlotYure>();
                }
                state.slotYures.Add(ScenePresetSlotYure.FromSnapshot(
                    slotName, SlotBoneManager.GetSlotItemFileName(maid, slotName), snapshot));
            }

            return state;
        }

        /// <summary>
        /// 再生中のモーションを記録する。停止中、または一覧に無いモーション
        /// （スクリプト再生やマイポーズ等）が当たっている場合は null を返し、ポーズ記録に任せる
        /// </summary>
        private static ScenePresetMotion CaptureMotion(Maid maid)
        {
            if (!MaidMotionState.IsPlaying(maid))
            {
                return null;
            }

            var data = PhotoMotionUtils.FindByClipName(MaidMotionState.GetCurrentClipName(maid));
            if (data == null)
            {
                return null;
            }

            return new ScenePresetMotion
            {
                id = data.id,
                file = data.direct_file,
                name = data.name,
            };
        }

        /// <summary>
        /// 重力を記録する。既定値（OFF・オフセット 0）のカテゴリも記録し、
        /// 適用時に前のシーンの重力が残らないようにする
        /// </summary>
        private static List<ScenePresetGravity> CaptureGravity(Maid maid)
        {
            var controller = maidManager.gravityController;
            var list = new List<ScenePresetGravity>();
            foreach (var category in MaidGravityController.categories)
            {
                list.Add(new ScenePresetGravity
                {
                    category = category.id,
                    enabled = controller.GetEnabled(maid, category),
                    offset = controller.GetOffset(maid, category),
                });
            }
            return list;
        }

        /// <summary>
        /// IK 固定の状態を記録する。エントリ未作成（一度も固定していない）のメイドは
        /// 全固定 OFF + 既定パラメータとして記録し、適用時に固定が残らないようにする
        /// </summary>
        private static void CaptureIKHold(Maid maid, ScenePresetMaid state)
        {
            var holdParams = maidManager.ikHoldController.GetParamsOrNull(maid)
                ?? MaidIKHoldParams.Default;

            state.ikParams = ScenePresetIKParams.FromParams(holdParams);

            for (var i = 0; i < (int)MaidIKHoldType.Max; i++)
            {
                var type = (MaidIKHoldType)i;
                if (maidManager.ikHoldController.GetHold(maid, type))
                {
                    state.ikHolds.Add(type.ToString());
                }
            }
        }

        /// <summary>
        /// 視線を記録する。方向指定モードの注視点 (頭ボーン配下の face_to_object) は
        /// 復元時に作り直されるため、注視対象としては記録しない
        /// </summary>
        private static void CaptureLook(Maid maid, ScenePresetMaid state)
        {
            var controller = maidManager.lookController;
            var look = new ScenePresetLook
            {
                mode = controller.GetMode(maid).ToString(),
                lookX = controller.GetLookX(maid),
                lookY = controller.GetLookY(maid),
            };

            // 追従トグルは lookController ではなく TBody が持つ (v15)
            var body = maid.body0;
            if (body != null)
            {
                look.headToCam = body.boHeadToCam;
                look.headToCamSpecified = true;
                look.eyeToCam = body.boEyeToCam;
                look.eyeToCamSpecified = true;
            }

            var target = controller.GetTarget(maid);
            if (controller.GetMode(maid) == MaidLookMode.オブジェクト && target != null)
            {
                var ownerMaid = FindOwnerMaid(target);
                if (ownerMaid != null)
                {
                    look.targetMaidGuid = GetGuid(ownerMaid);
                    look.targetBone = target.name;
                }
                else
                {
                    look.targetPath = GetScenePath(target);
                }
            }

            state.look = look;
        }

        /// <summary>
        /// 注視対象がどの呼出済みメイドのボーンかを調べる。無関係なら null。
        /// 復元時は m_trBones から名前で引くため、判定範囲もボーンツリーに合わせる
        /// (メイド配下でもボーン外のもの——装着物等——は階層パスで記録させる)
        /// </summary>
        private static Maid FindOwnerMaid(Transform target)
        {
            foreach (var maid in maidManager.calledMaids)
            {
                if (maid != null && maid.body0 != null && maid.body0.m_trBones != null
                    && target.IsChildOf(maid.body0.m_trBones))
                {
                    return maid;
                }
            }
            return null;
        }

        /// <summary>シーンルートからの階層パス。GameObject.Find に渡せる形にする</summary>
        private static string GetScenePath(Transform target)
        {
            var path = target.name;
            for (var parent = target.parent; parent != null; parent = parent.parent)
            {
                path = parent.name + "/" + path;
            }
            return path;
        }

        /// <summary>
        /// プリセットを現在のシーンへ適用する。
        /// メイドは guid で割り当て、足りない分はストックから呼び出す。
        /// どのスロットにも割り当てられなかった呼出済みメイドは解除する
        /// </summary>
        /// <param name="skipScenery">
        /// 背景・カメラ・ライトを適用済みとして飛ばす。
        /// ChangeBg は毎回オブジェクトを破棄・再生成するため、
        /// 先出し適用済みのまま再適用すると一瞬ちらつく
        /// </param>
        private static void Apply(ScenePresetData data, bool skipScenery)
        {
            // プリセットはシーン全体を書き換えるため、既存の履歴は復元先を失う
            HistoryManager.instance.ClearHistory();
            MTEUtils.Log("シーンプリセット適用のため操作履歴をクリアしました");

            // 前回ロードの保留が残っていると解除済みメイドへの適用や
            // 古い外部状態の反映が起きるため、新しいロードで打ち切る
            _pendingApplies.Clear();
            _pendingExternalsData = null;
            _resolvedAssignments.Clear();

            if (!skipScenery)
            {
                ApplyScenery(data);
            }
            // PNG 配置も背景カテゴリに含める。
            // 未保存のプリセットで ApplyState(null) を呼ぶと既存の配置を全消去してしまうため、
            // ここで確実に弾く
            if (ShouldApplyBackground(data))
            {
                PngPlacementSnapshot.ApplyState(data.pngPlacement);
            }

            // メイドを読み込まないときは呼出も解除も行わず、現在のメイドをそのまま残す
            if (ShouldApplyMaids(data))
            {
                var assignments = AssignMaids(data.maids);
                _resolvedAssignments.AddRange(assignments);

                // メイド未保存（カメラ・背景のみ等）のプリセットと「保存時に 0 体」は
                // XML 上区別できないため、1 体以上保存されている場合だけ解除まで行う
                if (data.maids != null && data.maids.Count > 0)
                {
                    ReleaseUnassignedMaids(assignments);
                }

                foreach (var pair in assignments)
                {
                    // 新規呼出はロード完了までポーズ・位置を適用できないため保留に積む
                    if (maidManager.IsLoading(pair.Key))
                    {
                        _pendingApplies.Add(pair);
                    }
                    else
                    {
                        ApplyMaid(pair.Key, pair.Value);
                    }
                }
            }

            // 外部プラグインはメイドを参照することがあるため、
            // 全メイドのロード完了を待ってから反映する
            if (_pendingApplies.Count > 0)
            {
                _pendingExternalsData = data;
            }
            else
            {
                FinishApply(data);
            }
        }

        /// <summary>
        /// メイドを伴わない情景 (カメラ・背景・ライト) を適用する。
        /// 本適用と先出し適用で順序を揃えるため 1 箇所にまとめている。
        /// 保存されていない、または読込トグルで OFF にされたカテゴリは触らない
        /// </summary>
        private static void ApplyScenery(ScenePresetData data)
        {
            if (ShouldApplyCamera(data))
            {
                CameraSnapshot.ApplyState(data.camera);
            }
            // ライトは背景カテゴリに含めている (UI のトグルを 1 つにまとめているため)
            if (ShouldApplyBackground(data))
            {
                BackgroundSnapshot.ApplyState(data.background);
                LightSnapshot.ApplyState(data.light);
            }
        }

        /// <summary>
        /// どのスロットにも割り当てられなかった呼出済みメイドを解除し、
        /// プリセットのシーン構成を再現する
        /// </summary>
        private static void ReleaseUnassignedMaids(
            List<KeyValuePair<Maid, ScenePresetMaid>> assignments)
        {
            var assigned = new HashSet<Maid>();
            foreach (var pair in assignments)
            {
                assigned.Add(pair.Key);
            }

            // ReleaseMaid は calledMaids を書き換えるためコピーを回す
            foreach (var maid in maidManager.calledMaids.ToArray())
            {
                if (assigned.Contains(maid))
                {
                    continue;
                }
                // 自動解除はユーザー操作と紐付かないため、追跡できるようログを残す
                MTEUtils.Log("プリセットに含まれないメイドを解除します: {0}",
                    maid.status != null ? maid.status.fullNameJpStyle : "(不明)");
                maidManager.ReleaseMaid(maid);
            }

            // 操作対象が解除で空になった場合は割当済みメイドへ引き継ぐ
            if (maidManager.targetMaid == null && assignments.Count > 0)
            {
                maidManager.targetMaid = assignments[0].Key;
            }
        }

        /// <summary>
        /// プリセットの各メイドスロットへ実メイドを割り当てる（1 体は 1 スロットのみ）。
        /// 優先順: 呼出済みの guid 一致 → ストックの guid 一致（呼出）→ 未割当ストックを上から（呼出）。
        /// 呼出枠不足などで割当できないスロットはスキップする
        /// </summary>
        private static List<KeyValuePair<Maid, ScenePresetMaid>> AssignMaids(
            List<ScenePresetMaid> states)
        {
            var result = new List<KeyValuePair<Maid, ScenePresetMaid>>(states.Count);
            var assigned = new HashSet<Maid>();

            // guid 一致を先に確定させ、後続の上から順の充当に guid 持ちを横取りされないようにする
            var fallbackStates = new List<ScenePresetMaid>();
            foreach (var state in states)
            {
                var maid = FindMaidByGuid(state.guid, assigned);
                if (maid != null)
                {
                    assigned.Add(maid);
                    result.Add(new KeyValuePair<Maid, ScenePresetMaid>(maid, state));
                }
                else
                {
                    fallbackStates.Add(state);
                }
            }

            foreach (var state in fallbackStates)
            {
                // guid 不明（旧形式）・不一致の分は、まず呼出済み（ロード中含む）の
                // 未割当メイドを呼出順に充てる。既存メイドを差し置いて新規呼出しない
                var maid = FindUnassignedCalledMaid(assigned);
                if (maid == null)
                {
                    maid = CallNextUnassignedMaid(assigned);
                }
                if (maid == null)
                {
                    // 呼出枠・ストック不足は以降のスロットも失敗するため打ち切る。
                    // 警告は CallMaid 側で出るためここでは積まない
                    break;
                }
                assigned.Add(maid);
                result.Add(new KeyValuePair<Maid, ScenePresetMaid>(maid, state));
            }

            return result;
        }

        /// <summary>
        /// プリセット上の guid を持つスロットへ割り当てられた実メイドを返す。
        /// 保存時の guid と現在のメイドの guid は一致するとは限らないため、
        /// 実メイドの guid ではなくスロット側の guid で引く
        /// </summary>
        private static Maid FindMaidBySlotGuid(string guid)
        {
            if (string.IsNullOrEmpty(guid))
            {
                return null;
            }

            foreach (var pair in _resolvedAssignments)
            {
                if (pair.Value.guid == guid && IsStillCalled(pair.Key))
                {
                    return pair.Key;
                }
            }
            return null;
        }

        /// <summary>
        /// 割当時のメイドが今も呼出済みかを確かめる。
        /// ロード待ちを挟む間にユーザーが解除する余地があり、ストックの Maid は
        /// 使い回されるため、参照を持っているだけでは解除済みかを判別できない
        /// </summary>
        private static bool IsStillCalled(Maid maid)
        {
            return maid != null && maidManager.calledMaids.Contains(maid);
        }

        /// <summary>guid が一致する未割当メイドを呼出済みの中から探す。新たな呼出はしない</summary>
        private static Maid FindCalledMaidByGuid(string guid, HashSet<Maid> assigned)
        {
            if (string.IsNullOrEmpty(guid))
            {
                return null;
            }

            foreach (var maid in maidManager.calledMaids)
            {
                if (!assigned.Contains(maid) && GetGuid(maid) == guid)
                {
                    return maid;
                }
            }
            return null;
        }

        /// <summary>guid が一致する未割当メイドを探す。未呼出でストックに居れば呼び出す</summary>
        private static Maid FindMaidByGuid(string guid, HashSet<Maid> assigned)
        {
            var calledMaid = FindCalledMaidByGuid(guid, assigned);
            if (calledMaid != null)
            {
                return calledMaid;
            }
            if (string.IsNullOrEmpty(guid))
            {
                return null;
            }

            for (var i = 0; i < characterMgr.GetStockMaidCount(); i++)
            {
                var stockMaid = characterMgr.GetStockMaid(i);
                if (stockMaid == null || assigned.Contains(stockMaid) ||
                    GetGuid(stockMaid) != guid)
                {
                    continue;
                }
                return maidManager.CallMaid(i);
            }

            return null;
        }

        /// <summary>呼出済み（ロード中含む）の未割当メイドを呼出順に探す。居なければ null</summary>
        private static Maid FindUnassignedCalledMaid(HashSet<Maid> assigned)
        {
            foreach (var maid in maidManager.calledMaids)
            {
                if (!assigned.Contains(maid))
                {
                    return maid;
                }
            }
            return null;
        }

        /// <summary>未呼出・未割当のストックメイドを上から順に呼び出す。候補が尽きたら null</summary>
        private static Maid CallNextUnassignedMaid(HashSet<Maid> assigned)
        {
            for (var i = 0; i < characterMgr.GetStockMaidCount(); i++)
            {
                var stockMaid = characterMgr.GetStockMaid(i);
                if (stockMaid == null || assigned.Contains(stockMaid))
                {
                    continue;
                }
                // 呼出済み（ロード済み・ロード中含む）の guid 不一致メイドは充当しない。
                // CallMaid はロード済みメイドをそのまま返すため、除外しないと
                // プリセットと無関係な既存メイドをフォールバックで上書きしてしまう
                if (maidManager.calledMaids.Contains(stockMaid) ||
                    maidManager.IsLoading(stockMaid) ||
                    (stockMaid.body0 != null && stockMaid.body0.isLoadedBody))
                {
                    continue;
                }
                var maid = maidManager.CallMaid(i);
                if (maid != null)
                {
                    return maid;
                }
                // 呼出失敗（枠不足など）は以降のストックも見込みが無いため打ち切る
                return null;
            }
            return null;
        }

        private static string GetGuid(Maid maid)
        {
            return maid != null && maid.status != null ? maid.status.guid : null;
        }

        /// <summary>
        /// 保留適用の消化。MaidManipulateManager.Update の UpdateLoadingMaids より後に
        /// 呼ばれ、デフォルト配置の後にプリセットの位置・ポーズを最終値として適用する
        /// </summary>
        public static void UpdatePendingApplies()
        {
            for (var i = _pendingApplies.Count - 1; i >= 0; i--)
            {
                var maid = _pendingApplies[i].Key;
                if (maid == null || maid.body0 == null)
                {
                    _pendingApplies.RemoveAt(i);
                    continue;
                }
                if (maidManager.IsLoading(maid))
                {
                    continue;
                }
                ApplyMaid(maid, _pendingApplies[i].Value);
                _pendingApplies.RemoveAt(i);
            }

            // 全メイドの保留適用が済んだら、待たせていた外部プロバイダを反映する
            if (_pendingApplies.Count == 0 && _pendingExternalsData != null)
            {
                var data = _pendingExternalsData;
                _pendingExternalsData = null;
                FinishApply(data);
            }
        }

        /// <summary>
        /// 全メイドのロード完了後にまとめて行う仕上げ。
        /// 視線は他メイドを参照しうるため、外部プロバイダと同じくここで反映する。
        /// メイドを読み込まないときは AssignMaids ごと飛ばしているため、
        /// 無関係な既存メイドへ視線・フォーカスを当てないよう合わせて飛ばす
        /// </summary>
        private static void FinishApply(ScenePresetData data)
        {
            var applyMaids = ShouldApplyMaids(data);
            if (applyMaids)
            {
                ApplyLooks();
            }
            ApplyExternals(data);
            // 外部プロバイダのモデル復元 (同期) の後でないと GameObject が存在しない
            ApplyModelBoneEdits(data);
            if (applyMaids)
            {
                RequestFocusOnAppliedMaid(data);
            }
            // Maid 参照を適用の間だけ持つ。以降の解除・シーン遷移で寿命が切れるため残さない
            _resolvedAssignments.Clear();
        }

        /// <summary>
        /// AssignMaids が確定させた _resolvedAssignments の各メイドへ視線を復元する。
        /// 旧プリセット (look 無し) のメイドは触らない。
        /// 1 体の失敗で他のメイドが止まらないよう個別に握りつぶす
        /// </summary>
        private static void ApplyLooks()
        {
            foreach (var pair in _resolvedAssignments)
            {
                var maid = pair.Key;
                var look = pair.Value.look;
                if (look == null || !IsStillCalled(maid))
                {
                    continue;
                }

                try
                {
                    ApplyLook(maid, look);
                }
                catch (Exception e)
                {
                    MTEUtils.LogException(e);
                }
            }
        }

        private static void ApplyLook(Maid maid, ScenePresetLook look)
        {
            var mode = MaidLookMode.カメラ;
            try
            {
                mode = (MaidLookMode)Enum.Parse(typeof(MaidLookMode), look.mode);
            }
            catch (Exception)
            {
                // XML は外部入力のため、未知のモード名はカメラとして扱う
                MTEUtils.LogWarning("視線の向け先が不明です: {0}", look.mode);
            }

            var target = mode == MaidLookMode.オブジェクト ? ResolveLookTarget(look) : null;
            if (mode == MaidLookMode.オブジェクト && target == null)
            {
                MTEUtils.LogWarning("注視対象が見つからないため方向指定で復元します: {0}",
                    look.targetPath ?? look.targetBone);
                mode = MaidLookMode.方向指定;
            }

            maidManager.lookController.SetState(maid, mode, look.lookX, look.lookY, target);

            // 追従トグルは lookController の管轄外なので TBody へ直接戻す。
            // ウィンドウのトグルと同じく割合 (HeadToCamPer) は触らず、ゲーム側のフェードに任せる。
            // ここはロード完了後に呼ばれるためボディは揃っている想定だが、
            // 他の TBody アクセスと同じ防御的ガードに揃えておく
            var body = maid.body0;
            if (body == null || !body.isLoadedBody)
            {
                return;
            }
            if (look.headToCamSpecified)
            {
                body.boHeadToCam = look.headToCam;
            }
            if (look.eyeToCamSpecified)
            {
                body.boEyeToCam = look.eyeToCam;
            }
        }

        /// <summary>
        /// 注視対象を引き当てる。メイドは guid で確実に、
        /// それ以外は階層パスでベストエフォートに解決する
        /// </summary>
        private static Transform ResolveLookTarget(ScenePresetLook look)
        {
            if (!string.IsNullOrEmpty(look.targetMaidGuid))
            {
                var owner = FindMaidBySlotGuid(look.targetMaidGuid);
                if (owner == null || owner.body0 == null || owner.body0.m_trBones == null)
                {
                    return null;
                }
                // 第 3 引数 boSMPass は false。このリポジトリの他の呼び出しに合わせる
                return CMT.SearchObjName(owner.body0.m_trBones, look.targetBone, false);
            }

            if (string.IsNullOrEmpty(look.targetPath))
            {
                return null;
            }

            // 先頭の / でルート起点に限定し、同名オブジェクトの取り違えを減らす。
            // GameObject.Find は非アクティブなオブジェクトを見つけられないため、
            // 保存時に非表示だった対象は解決に失敗して方向指定へ落ちる
            var go = GameObject.Find("/" + look.targetPath);
            return go != null ? go.transform : null;
        }

        /// <summary>シーン遷移・プラグイン無効化時に保留適用を破棄する</summary>
        public static void ClearPendingApplies()
        {
            _pendingApplies.Clear();
            _pendingExternalsData = null;
            _resolvedAssignments.Clear();
        }

        /// <summary>
        /// プリセット適用の完了時に、SceneView を操作対象メイドへ寄せるよう予約する。
        /// 呼び出したメイドがプリセットの位置に置かれても見失わないよう、
        /// 新規呼出の有無に関わらず寄せる。
        /// メイド未保存（カメラ・背景のみ）のプリセットは、無関係な既存メイドへ寄ってしまうため対象外。
        /// 退避中（非表示）のメイドも実座標が退避先にあり、寄せると何もない場所を映すため対象外
        /// </summary>
        private static void RequestFocusOnAppliedMaid(ScenePresetData data)
        {
            if (data.maids == null || data.maids.Count == 0)
            {
                return;
            }

            var maid = maidManager.targetMaid;
            if (maid == null || !maidManager.IsVisible(maid))
            {
                return;
            }

            // 実際の寄せは MaidManipulateManager 側でロード完了 + 1 フレーム待ってから行う
            maidManager.RequestFocusOnLoaded(maid);
        }

        /// <summary>
        /// 外部プロバイダのペイロードを適用する。
        /// プロバイダの例外・不在は 1 件ごとに握り、他の復元は続行する
        /// </summary>
        private static void ApplyExternals(ScenePresetData data)
        {
            if (data.externals == null)
            {
                return;
            }

            foreach (var external in data.externals)
            {
                // 読込トグルで OFF にされたプロバイダは、プラグイン未導入の警告も出さずに飛ばす
                if (!IsProviderLoadEnabled(external.id))
                {
                    continue;
                }

                var provider = ScenePresetProviderRegistry.GetProvider(external.id);
                if (provider == null)
                {
                    MTEUtils.LogWarning("外部プラグインが見つからないため復元をスキップします: {0}", external.id);
                    continue;
                }

                try
                {
                    bool applied;
                    if (provider.isBinary)
                    {
                        // サイドカー欠落等で中身が無い場合は適用しない
                        if (external.binaryPayload == null || external.binaryPayload.Length == 0)
                        {
                            continue;
                        }
                        applied = provider.applyBinary(external.binaryPayload);
                    }
                    else
                    {
                        if (string.IsNullOrEmpty(external.payload))
                        {
                            continue;
                        }
                        applied = provider.applyXml(external.payload);
                    }

                    if (!applied)
                    {
                        MTEUtils.LogWarning("外部プラグインの状態復元に失敗しました: {0}", external.id);
                    }
                }
                catch (Exception e)
                {
                    MTEUtils.LogError("外部プラグインの状態復元に失敗しました: " + external.id);
                    MTEUtils.LogException(e);
                }
            }
        }

        private static void ApplyMaid(Maid maid, ScenePresetMaid state)
        {
            if (maid == null || maidManager.IsLoading(maid))
            {
                return;
            }

            var isBodyLoaded = maid.body0 != null && maid.body0.isLoadedBody;

            // 表示状態を先に確定させてから位置・回転を適用する。
            // 退避の出入りは WarpTo で座標・回転を上書きするため、後から適用しないと失われる
            maidManager.SetVisible(maid, state.visible);

            if (state.visible)
            {
                maid.SetPos(state.position);
                maid.SetRot(state.rotation);

                // 瞬間移動で揺れ物が取り残されないよう物理をリセットする（配置プリセットと同じ理由）
                if (isBodyLoaded)
                {
                    maid.body0.WarpInit();
                }
            }
            else
            {
                // 退避中に実座標を動かすと画面に出てしまうため、戻り先だけ書き換える
                // （ApplyPlacement と同じ退避契約）
                maidManager.SetRestorePosition(maid, state.position);
            }

            // ボディ未ロード中はポーズ・表情を復元できないため、配置と表示状態だけ反映する
            // （CaptureMaid 側の制限と対）
            if (!isBodyLoaded)
            {
                return;
            }

            try
            {
                // 非表示中の適用は表示へ戻す際に上書きされるため、表示中だけポーズを復元する
                if (state.visible)
                {
                    if (state.motion != null)
                    {
                        ApplyMotion(maid, state.motion);
                    }
                    else if (state.poseAnmBinary != null)
                    {
                        MaidPoseFileManager.ApplyPoseBinary(
                            maid, state.poseAnmBinary, "scene_preset.anm");
                    }
                }
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
            }

            ApplyFace(maid, state);

            // 1 体の失敗で以降のメイド・externals の適用が止まらないよう個別に握りつぶす
            try
            {
                ApplyUndress(maid, state);
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
            }
            try
            {
                ApplyGravity(maid, state);
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
            }
            try
            {
                ApplyIKHold(maid, state);
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
            }
            try
            {
                ApplyBoneEdits(maid, state);
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
            }
            try
            {
                ApplySlotYures(maid, state);
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
            }
        }

        /// <summary>
        /// 揺れ物理の状態を復元する。旧プリセット (slotYures 無し) では変更しない。
        /// ボーン編集と同様、保存時と違うアイテムのスロットは物理の構成が別物なので飛ばす
        /// </summary>
        private static void ApplySlotYures(Maid maid, ScenePresetMaid state)
        {
            if (state.slotYures == null)
            {
                return;
            }

            foreach (var yure in state.slotYures)
            {
                if (yure == null || !yure.isValid)
                {
                    continue;
                }
                if (SlotBoneManager.GetSlotItemFileName(maid, yure.slot) != yure.item)
                {
                    continue;
                }
                SlotYureUtil.ApplySnapshot(maid, yure.slot, yure.ToSnapshot());
            }
        }

        /// <summary>
        /// 記録したモーションを再生し直す。Mod の削除などで見つからない場合は
        /// ポーズも記録されていないため、現在のポーズのままにする
        /// </summary>
        private static void ApplyMotion(Maid maid, ScenePresetMotion motion)
        {
            var data = PhotoMotionUtils.Find(motion.id, motion.file);
            if (data == null)
            {
                MTEUtils.LogWarning("モーションが見つかりませんでした: {0}",
                    string.IsNullOrEmpty(motion.name) ? motion.file : motion.name);
                return;
            }
            PhotoMotionUtils.Apply(maid, data);
        }

        /// <summary>
        /// スロットボーンの編集差分を復元する。旧プリセット (boneEdits 無し) では変更しない。
        /// RecordEdit は現在値を読むため、書き込みの前後で 2 回呼んで
        /// 「元値 = 適用前」「編集値 = 適用後」になるようにする (リセットで戻せる)
        /// </summary>
        private static void ApplyBoneEdits(Maid maid, ScenePresetMaid state)
        {
            if (state.boneEdits == null || state.boneEdits.Count == 0)
            {
                return;
            }

            var store = BoneEditManager.instance.GetStore(maid);
            foreach (var edit in state.boneEdits)
            {
                if (edit == null || !edit.isValid)
                {
                    continue;
                }

                var slotObj = SlotBoneManager.GetSlotObject(maid, edit.slot);
                if (slotObj == null)
                {
                    continue;
                }

                // 保存時と違うアイテムを装着しているスロットは骨格が別物なので飛ばす
                if (SlotBoneManager.GetSlotItemFileName(maid, edit.slot) != edit.item)
                {
                    continue;
                }

                var bone = SlotBoneManager.FindBone(slotObj, edit.bone);
                if (bone == null)
                {
                    continue;
                }

                // 先に呼んで適用前の値を元値として控える
                store.RecordEdit(edit.slot, edit.item, bone);

                bone.localPosition = new Vector3(edit.pos[0], edit.pos[1], edit.pos[2]);
                bone.localRotation = new Quaternion(edit.rot[0], edit.rot[1], edit.rot[2], edit.rot[3]);
                bone.localScale = new Vector3(edit.scl[0], edit.scl[1], edit.scl[2]);

                store.RecordEdit(edit.slot, edit.item, bone);
            }
        }

        /// <summary>
        /// モデルのボーン編集差分を復元する (v18)。旧プリセット (modelBoneEdits 無し) では変更しない。
        /// GameObject 名 + 提供プラグイン名で現在のモデルへ照合する (同名複数は先勝ち)。
        /// メイドの ApplyBoneEdits と同じく RecordEdit を書き込みの前後で 2 回呼び、
        /// 「元値 = 適用前」「編集値 = 適用後」にする (リセットで戻せる)
        /// </summary>
        private static void ApplyModelBoneEdits(ScenePresetData data)
        {
            if (data.modelBoneEdits == null || data.modelBoneEdits.Count == 0)
            {
                return;
            }

            var models = ModelProviderHost.GetModels();

            // 差分を当てるモデルは既存の編集をリセットしてプリセットの状態で置き換える。
            // プロバイダの読込を OFF にした場合など、モデルが作り直されず残るケースへの対処
            var targets = new Dictionary<GameObject, bool>();
            foreach (var edit in data.modelBoneEdits)
            {
                if (edit == null || !edit.isValid)
                {
                    continue;
                }

                var entry = models.Find(m =>
                    m.obj.name == edit.modelName && m.pluginName == edit.pluginName);
                if (entry == null)
                {
                    MTEUtils.LogWarning(
                        "ボーン編集の対象モデルが見つかりません: {0}", edit.modelName);
                    continue;
                }

                var store = BoneEditManager.instance.GetModelStore(entry.obj);
                if (!targets.ContainsKey(entry.obj))
                {
                    store.ResetSlot(BoneEditManager.ModelSlotKey, entry.obj);
                    targets[entry.obj] = true;
                }

                var bone = SlotBoneManager.FindBone(entry.obj, edit.bone);
                if (bone == null)
                {
                    MTEUtils.LogWarning(
                        "ボーン編集の対象ボーンが見つかりません: {0}/{1}", edit.modelName, edit.bone);
                    continue;
                }

                // 先に呼んで適用前の値を元値として控える
                store.RecordEdit(BoneEditManager.ModelSlotKey, null, bone);

                bone.localPosition = new Vector3(edit.pos[0], edit.pos[1], edit.pos[2]);
                bone.localRotation = new Quaternion(edit.rot[0], edit.rot[1], edit.rot[2], edit.rot[3]);
                bone.localScale = new Vector3(edit.scl[0], edit.scl[1], edit.scl[2]);

                store.RecordEdit(BoneEditManager.ModelSlotKey, null, bone);
            }
        }

        /// <summary>脱衣・めくれ系を復元する。旧プリセット (undress 無し) では変更しない</summary>
        private static void ApplyUndress(Maid maid, ScenePresetMaid state)
        {
            if (state.undress == null)
            {
                return;
            }
            MaidUndressController.ApplyUndressedSlots(maid, state.undress.slots);
            MaidUndressController.ApplyCostumeTypes(maid, state.undress.costumeTypes);
        }

        /// <summary>重力を復元する。旧プリセット (gravity 無し) では変更しない</summary>
        private static void ApplyGravity(Maid maid, ScenePresetMaid state)
        {
            if (state.gravity == null)
            {
                return;
            }

            var controller = maidManager.gravityController;

            // 重力を一度も使っていないメイドに既定値だけを書き戻すと、
            // 何も変わらないのにコンポーネントだけが作られて常駐コストになる。
            // 既定値のみのプリセットでは何もしない
            if (!controller.HasState(maid) && state.gravity.All(IsDefaultGravity))
            {
                return;
            }

            foreach (var entry in state.gravity)
            {
                var category = MaidGravityController.FindCategory(entry.category);
                if (category == null)
                {
                    continue;
                }
                controller.SetOffset(maid, category, entry.offset);
                controller.SetEnabled(maid, category, entry.enabled);
            }
        }

        /// <summary>重力が既定値（無効・オフセット 0）か</summary>
        private static bool IsDefaultGravity(ScenePresetGravity entry)
        {
            return !entry.enabled && entry.offset == Vector3.zero;
        }

        /// <summary>
        /// IK 固定を復元する。旧プリセット (ikParams 無し) では変更しない。
        /// ポーズ復元後に呼ぶことで、固定 ON の箇所は復元したポーズの位置で固定される。
        /// 非表示中はポーズを復元していないため、固定も適用しない
        /// </summary>
        private static void ApplyIKHold(Maid maid, ScenePresetMaid state)
        {
            if (state.ikParams == null || !state.visible)
            {
                return;
            }

            var ik = maidManager.ikHoldController;

            // 接地判定に使うため、パラメータを固定 ON より先に反映する
            state.ikParams.ApplyTo(ik.GetParams(maid));

            for (var i = 0; i < (int)MaidIKHoldType.Max; i++)
            {
                var type = (MaidIKHoldType)i;
                ik.SetHold(maid, type, state.ikHolds.Contains(type.ToString()));
            }
        }

        private static void ApplyFace(Maid maid, ScenePresetMaid state)
        {
            var savedValues = new Dictionary<string, float>(state.morphs.Count);
            foreach (var morph in state.morphs)
            {
                savedValues[morph.name] = morph.value;
            }

            // 未記録のモーフは 0 に戻し、プリセット保存時の表情をそのまま再現する
            foreach (FaceMorphCategory category in Enum.GetValues(typeof(FaceMorphCategory)))
            {
                foreach (var def in MaidFaceMorphController.GetAvailableMorphs(maid, category))
                {
                    float value;
                    if (!savedValues.TryGetValue(def.name, out value))
                    {
                        value = 0f;
                    }
                    MaidFaceMorphController.SetMorphValue(maid, def, value);
                }
            }

            MaidFaceMorphController.SetMabataki(maid, state.mabataki);
        }

        /// <summary>
        /// メインカメラを一時 RenderTexture へ描画してサムネを保存する。
        /// 画面キャプチャと違いプラグイン UI や NGUI が写り込まず、最大化中でも使える
        /// </summary>
        private static void SaveThumbnail(string filePath)
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

                texture.ResizeTexture(THUM_WIDTH, THUM_HEIGHT);
                UTY.SaveImage(texture, filePath);
            }
            finally
            {
                camera.targetTexture = savedTargetTexture;
                RenderTexture.active = savedActive;
                RenderTexture.ReleaseTemporary(renderTexture);
                if (texture != null)
                {
                    UnityEngine.Object.Destroy(texture);
                }
            }
        }

        /// <summary>拡張子以外にドットを含むファイル名はサイドカーとみなす。
        /// プリセット名はドット禁止 (ValidatePresetName) なので誤除外しない</summary>
        private static bool IsSidecarXmlPath(string xmlPath)
        {
            return Path.GetFileNameWithoutExtension(xmlPath).IndexOf('.') >= 0;
        }

        /// <summary>プリセット本体 XML をデシリアライズする</summary>
        private static ScenePresetData LoadPresetData(string xmlPath)
        {
            using (var stream = File.OpenRead(xmlPath))
            {
                return (ScenePresetData)_serializer.Deserialize(stream);
            }
        }

        /// <summary>
        /// サイドカーのファイル名として安全か検証する。
        /// 本体 XML はユーザー間で受け渡されうるため、改ざんされた名前による
        /// パストラバーサル（任意ファイルの読み取り・削除）をここで遮断する
        /// </summary>
        private static bool IsValidSidecarFileName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                return false;
            }
            if (fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                return false;
            }
            return !fileName.Contains("..");
        }

        /// <summary>本体 XML と同じフォルダのサイドカーパスへ解決する</summary>
        private static string GetSidecarPath(string xmlPath, string fileName)
        {
            return Path.Combine(Path.GetDirectoryName(xmlPath), fileName);
        }

        /// <summary>
        /// 命名規約 &lt;プリセット名&gt;.&lt;キー&gt;.&lt;拡張子&gt; でサイドカー名を作る。
        /// キーが衝突する場合（プロバイダ id が "pose0" を名乗る等）は連番で回避する。
        /// 決まったファイル名は usedNames へ登録する
        /// </summary>
        private static string BuildSidecarFileName(
            string presetName, string key, string extension, HashSet<string> usedNames)
        {
            var baseName = presetName + "." + key;
            var fileName = baseName + "." + extension;
            var suffix = 2;
            while (!usedNames.Add(fileName))
            {
                fileName = baseName + "_" + suffix + "." + extension;
                suffix++;
            }
            return fileName;
        }

        /// <summary>data に記録されている全サイドカーのファイル名を列挙する</summary>
        private static IEnumerable<string> EnumerateSidecarFileNames(ScenePresetData data)
        {
            if (data == null)
            {
                yield break;
            }

            if (data.maids != null)
            {
                foreach (var maid in data.maids)
                {
                    if (IsValidSidecarFileName(maid.poseAnmFile))
                    {
                        yield return maid.poseAnmFile;
                    }
                }
            }

            if (data.externals != null)
            {
                foreach (var external in data.externals)
                {
                    if (IsValidSidecarFileName(external.file))
                    {
                        yield return external.file;
                    }
                }
            }
        }

        /// <summary>
        /// サイドカーを書き出し、ファイル名を data へ記録する。
        /// ポーズ anm と外部プロバイダのペイロードを同じ規約で扱う
        /// </summary>
        private static void WriteSidecars(
            ScenePresetData data, string presetName, string xmlPath)
        {
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < data.maids.Count; i++)
            {
                var state = data.maids[i];
                state.poseAnmFile = null;
                if (state.poseAnmBinary == null)
                {
                    continue;
                }

                var fileName = BuildSidecarFileName(
                    presetName, "pose" + i, POSE_ANM_EXTENSION, usedNames);
                File.WriteAllBytes(GetSidecarPath(xmlPath, fileName), state.poseAnmBinary);
                state.poseAnmFile = fileName;
            }

            foreach (var external in data.externals)
            {
                var provider = ScenePresetProviderRegistry.GetProvider(external.id);
                var extension = provider != null
                    ? provider.extension
                    : ScenePresetProviderRegistry.DEFAULT_EXTENSION;

                var fileName = BuildSidecarFileName(
                    presetName, external.id, extension, usedNames);
                var path = GetSidecarPath(xmlPath, fileName);
                if (provider != null && provider.isBinary)
                {
                    File.WriteAllBytes(path, external.binaryPayload);
                }
                else
                {
                    File.WriteAllText(path, external.payload);
                }
                external.file = fileName;
            }
        }

        /// <summary>読み込めるサイドカーのパスを返す。不正・欠落なら警告して null を返す</summary>
        private static string ReadableSidecarPath(string xmlPath, string fileName)
        {
            if (!IsValidSidecarFileName(fileName))
            {
                MTEUtils.LogWarning("不正なサイドカーのファイル名のため読み飛ばします: {0}", fileName);
                return null;
            }

            var path = GetSidecarPath(xmlPath, fileName);
            if (!File.Exists(path))
            {
                MTEUtils.LogWarning("サイドカーが見つかりません: {0}", path);
                return null;
            }
            return path;
        }

        /// <summary>
        /// 記録されたサイドカーの中身を読み込んで data へ載せる。
        /// 欠落・不正なファイル名は警告して読み飛ばし、他の復元は続行する
        /// </summary>
        private static void ResolveSidecars(ScenePresetData data, string xmlPath)
        {
            foreach (var state in data.maids)
            {
                state.poseAnmBinary = null;
                // ポーズ未記録のメイドは属性ごと無いため、警告を出さずに飛ばす
                if (string.IsNullOrEmpty(state.poseAnmFile))
                {
                    continue;
                }

                var path = ReadableSidecarPath(xmlPath, state.poseAnmFile);
                if (path != null)
                {
                    state.poseAnmBinary = File.ReadAllBytes(path);
                }
            }

            if (data.externals == null)
            {
                return;
            }

            foreach (var external in data.externals)
            {
                var provider = ScenePresetProviderRegistry.GetProvider(external.id);
                // プラグイン未導入。ApplyExternals 側で警告するのでここでは読まない
                if (provider == null)
                {
                    continue;
                }

                var path = ReadableSidecarPath(xmlPath, external.file);
                if (path == null)
                {
                    continue;
                }

                if (provider.isBinary)
                {
                    external.binaryPayload = File.ReadAllBytes(path);
                }
                else
                {
                    external.payload = File.ReadAllText(path);
                }
            }
        }

        /// <summary>
        /// 上書き保存後に、旧プリセットにだけ存在したサイドカーを削除する。
        /// 保存成功後に呼ぶことで、途中失敗時に旧サイドカーが失われるのを防ぐ
        /// </summary>
        private static void DeleteStaleSidecars(
            string xmlPath, ScenePresetData oldData, ScenePresetData newData)
        {
            if (oldData == null)
            {
                return;
            }

            try
            {
                var keepNames = new HashSet<string>(
                    EnumerateSidecarFileNames(newData), StringComparer.OrdinalIgnoreCase);

                foreach (var fileName in EnumerateSidecarFileNames(oldData))
                {
                    if (keepNames.Contains(fileName))
                    {
                        continue;
                    }

                    var path = GetSidecarPath(xmlPath, fileName);
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                }
            }
            catch (Exception e)
            {
                // サイドカーの掃除に失敗しても保存自体は成功扱いにする
                MTEUtils.LogException(e);
            }
        }

        /// <summary>プリセット削除時に、本体 XML に記録されたサイドカーも削除する</summary>
        private static void DeleteSidecars(string xmlPath)
        {
            if (!File.Exists(xmlPath))
            {
                return;
            }

            try
            {
                var data = LoadPresetData(xmlPath);
                foreach (var fileName in EnumerateSidecarFileNames(data))
                {
                    var path = GetSidecarPath(xmlPath, fileName);
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                }
            }
            catch (Exception e)
            {
                // サイドカーの掃除に失敗しても本体の削除は続行する
                MTEUtils.LogException(e);
            }
        }

        /// <summary>保存先（表示中フォルダ）の本体 XML パス</summary>
        private static string GetPresetFilePath(string presetName)
        {
            return Path.Combine(currentDirPath, presetName + ".xml");
        }

        /// <summary>本体 XML と対になるサムネのパス</summary>
        private static string GetThumFilePath(string xmlPath)
        {
            return Path.ChangeExtension(xmlPath, ".png");
        }

        /// <summary>プリセットフォルダからの相対パスを返す。配下でなければ空文字</summary>
        private static string GetRelativePath(string fullPath)
        {
            return GetRelativePathFrom(presetFolderPath, fullPath);
        }

        /// <summary>基準フォルダからの相対パスを返す。配下でなければ空文字</summary>
        private static string GetRelativePathFrom(string basePath, string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath) ||
                !fullPath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
            {
                return "";
            }
            return fullPath.Substring(basePath.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        /// <summary>本体 XML のパスを、識別・比較に使う相対パス（拡張子なし）へ変換する</summary>
        private static string GetPresetKey(string xmlPath)
        {
            return Path.ChangeExtension(GetRelativePath(xmlPath), null);
        }

        /// <summary>
        /// プリセットキーの一致判定。ファイルシステム由来のパスなので、
        /// フォルダ名の大文字小文字を変えられても同一と見なす
        /// </summary>
        private static bool IsSamePresetKey(string key, string other)
        {
            return string.Equals(key, other, StringComparison.OrdinalIgnoreCase);
        }

        // ===== SceneDaily 自動ロード =====

        /// <summary>自動ロードの発動対象シーン名</summary>
        private const string AUTO_LOAD_SCENE_NAME = "SceneDaily";

        /// <summary>遷移直後はゲーム側のメイド呼出が始まっていないことがあるため最低限待つ秒数</summary>
        private const float AUTO_LOAD_MIN_WAIT = 0.5f;

        /// <summary>メイドのロード完了をこれ以上待たずに諦める秒数</summary>
        private const float AUTO_LOAD_TIMEOUT = 30f;

        private static bool _autoLoadPending = false;
        private static float _autoLoadRequestTime = 0f;

        /// <summary>セッション (ゲーム起動) 中に一度でも自動ロードしたか。「1回のみ」設定の判定用</summary>
        private static bool _autoLoadDone = false;

        /// <summary>
        /// フェードイン前に先出し適用したプリセットのデータ。
        /// 本適用で再パースせずに使い回し、背景・カメラ・ライトの二重適用を避ける
        /// </summary>
        private static ScenePresetData _autoLoadPreloadedData;

        /// <summary>自動ロード対象のプリセット名。未設定なら空文字。設定 UI の表示用</summary>
        public static string autoLoadName => Path.GetFileName(config.scenePresetAutoLoadKey ?? "");

        public static bool hasAutoLoadTarget
            => !string.IsNullOrEmpty(config.scenePresetAutoLoadKey);

        /// <summary>この項目が自動ロード対象か。タイルのホームアイコン表示に使う</summary>
        public static bool IsAutoLoadTarget(ScenePresetItem item)
        {
            return !item.isDir &&
                IsSamePresetKey(GetPresetKey(item.path), config.scenePresetAutoLoadKey);
        }

        /// <summary>自動ロード対象を切り替える。指定は 1 件のみで、ON は前の指定を上書きする</summary>
        public static void SetAutoLoadTarget(ScenePresetItem item, bool enable)
        {
            // フォルダをキーとして保存しない (IsAutoLoadTarget のガードと対称にする)
            if (item.isDir)
            {
                return;
            }
            var key = enable ? GetPresetKey(item.path) : "";
            SetAutoLoadKey(key);
        }

        /// <summary>自動ロード指定を解除する。設定 UI の解除ボタン用</summary>
        public static void ClearAutoLoadTarget()
        {
            SetAutoLoadKey("");
        }

        private static void SetAutoLoadKey(string key)
        {
            if (IsSamePresetKey(config.scenePresetAutoLoadKey, key))
            {
                return;
            }
            config.scenePresetAutoLoadKey = key;
            config.dirty = true;
        }

        /// <summary>
        /// 自動ロードの予約を破棄する。
        /// 先出し済みデータは本適用に渡らなくなるため、あわせて捨てる
        /// </summary>
        private static void AbortAutoLoad()
        {
            _autoLoadPending = false;
            _autoLoadPreloadedData = null;
        }

        /// <summary>
        /// シーン遷移を受けて自動ロードを予約/破棄する。
        /// プラグイン本体の sceneLoaded から UI の有効状態と無関係に呼ばれる
        /// </summary>
        public static void OnChangedSceneLevel(string sceneName)
        {
            // 別シーンへの遷移で古い予約を持ち越さない
            AbortAutoLoad();

            if (sceneName != AUTO_LOAD_SCENE_NAME || !hasAutoLoadTarget)
            {
                return;
            }
            if (config.scenePresetAutoLoadOnceOnly && _autoLoadDone)
            {
                return;
            }
            _autoLoadPending = true;
            _autoLoadRequestTime = Time.time;
        }

        /// <summary>
        /// フェードイン前に背景・カメラ・ライトだけを先行適用する。
        /// ゲーム側の DailyAPI.SceneStart 直後（画面が黒いうち）に呼ばれる想定で、
        /// メイドを伴う残りの適用は従来どおり UpdateAutoLoad が行う。
        /// 予約が無い場合や対象を解決できない場合は何もしない
        /// </summary>
        public static void PreloadAutoLoadScenery()
        {
            if (!_autoLoadPending || _autoLoadPreloadedData != null)
            {
                return;
            }

            // 一覧の走査・XML の読み込み・適用のいずれで失敗しても、
            // 中途半端な先出し状態を残さず従来タイミングでの適用へ委ねる
            try
            {
                var item = FindItemByKey(config.scenePresetAutoLoadKey);
                if (item == null)
                {
                    // 見つからない旨の警告は UpdateAutoLoad が出すため、ここでは黙って戻る
                    return;
                }

                var data = LoadPresetData(item.path);
                ResolveSidecars(data, item.path);
                ApplyScenery(data);

                _autoLoadPreloadedData = data;
                MTEUtils.Log("シーンプリセットの背景・カメラ・ライトを先行適用しました: {0}", item.name);
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
                // 適用の途中で失敗した場合に本適用で背景・カメラ・ライトを飛ばさないよう、
                // 先出し済み扱いを取り消す
                _autoLoadPreloadedData = null;
            }
        }

        /// <summary>
        /// 予約済みの自動ロードを進める。プラグイン本体から毎フレーム呼ばれる。
        /// ゲーム側のメイドのロード中に適用すると装備・ポーズが上書きされるため、完了を待つ
        /// </summary>
        public static void UpdateAutoLoad()
        {
            if (!_autoLoadPending)
            {
                return;
            }

            var elapsed = Time.time - _autoLoadRequestTime;
            if (elapsed < AUTO_LOAD_MIN_WAIT)
            {
                return;
            }
            if (elapsed > AUTO_LOAD_TIMEOUT)
            {
                AbortAutoLoad();
                MTEUtils.LogWarning(
                    "メイドのロードが完了しないため、プリセットの自動ロードを中止しました");
                return;
            }

            var gameMain = GameMain.Instance;
            if (gameMain == null || gameMain.CharacterMgr == null ||
                gameMain.CharacterMgr.IsBusy())
            {
                return;
            }

            _autoLoadPending = false;

            var item = FindItemByKey(config.scenePresetAutoLoadKey);
            if (item == null)
            {
                AbortAutoLoad();
                MTEUtils.LogWarning("自動ロード対象のプリセットが見つかりません: {0}",
                    config.scenePresetAutoLoadKey);
                return;
            }

            _autoLoadDone = true;
            MTEUtils.Log("シーンプリセットを自動ロードします: {0}", item.name);

            // 先出し済みなら再パースせず使い回し、背景・カメラ・ライトは飛ばす
            var preloaded = _autoLoadPreloadedData;
            _autoLoadPreloadedData = null;
            LoadPreset(item, preloaded);
        }

        /// <summary>キーに一致するプリセット項目を一覧全体から探す。無ければ null</summary>
        private static ScenePresetItem FindItemByKey(string key)
        {
            if (!_loaded)
            {
                Reload();
            }
            return FindItemByKey(rootItem, key);
        }

        private static ScenePresetItem FindItemByKey(ScenePresetItem dirItem, string key)
        {
            if (dirItem.children == null)
            {
                return null;
            }
            foreach (var child in dirItem.children.OfType<ScenePresetItem>())
            {
                if (child.isDir)
                {
                    var found = FindItemByKey(child, key);
                    if (found != null)
                    {
                        return found;
                    }
                }
                else if (IsSamePresetKey(GetPresetKey(child.path), key))
                {
                    return child;
                }
            }
            return null;
        }
    }
}
