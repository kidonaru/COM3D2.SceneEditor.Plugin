using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    using SE = SceneEditor.Plugin;

    // MTE_PngPlacement の PngAttachPoint (XML 互換のため列挙順を維持)
    public enum PngAttachPoint
    {
        none,
        body,
        head,
        handL,
        handR,
        footL,
        footR,
        muneL,
        muneR,
        body2,
        mata,
        ude1L,
        ude2L,
        ude1R,
        ude2R,
        leg1L,
        leg2L,
        leg1R,
        leg2R
    }

    /// <summary>
    /// タイムライン用の PNG 配置エントリ。
    /// タイムライン側の識別子 (imageName + groupSuffix) と SE 実体 (PngObjectData) の対応を保持する。
    /// SE の AddPng が採番する実体名とタイムライン名は一致しないため、この対応表が唯一の参照経路
    /// </summary>
    public class TimelinePngObjectEntry
    {
        public string name;
        public string imageName;
        public int group;
        public SE.PngObjectData data;

        public Transform transform => data != null ? data.transform : null;
        public bool visible => data != null && data.visible;
    }

    /// <summary>
    /// PngPlacementTimelineLayer 用のマネージャ。
    /// MTE_PngPlacement は外部 PngPlacement.dll のラッパーだが、SE は PNG 配置を自前実装
    /// (SceneEditor.Plugin.PngPlacementManager) しているため、その実体へ委譲するアダプタ版。
    /// SE 側マネージャと名前が紛らわしいため PngObjectTimelineManager に改名している
    /// </summary>
    public class PngObjectTimelineManager : ManagerBase
    {
        private static PngObjectTimelineManager _instance;
        public static PngObjectTimelineManager instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new PngObjectTimelineManager();
                }
                return _instance;
            }
        }

        private static SE.PngPlacementManager sePngManager => SE.PngPlacementManager.instance;

        public List<TimelinePngObjectEntry> pngObjects = new List<TimelinePngObjectEntry>();
        public List<string> pngObjectNames = new List<string>();
        private Dictionary<string, TimelinePngObjectEntry> _entryMap
            = new Dictionary<string, TimelinePngObjectEntry>();
        // SE 実体 → エントリの恒久対応。一度割り当てた名前 (group) は実体が削除されるまで
        // 維持し、SE 側の削除・並べ替えで既存キーフレームの紐付き先がすり替わるのを防ぐ
        private Dictionary<SE.PngObjectData, TimelinePngObjectEntry> _dataMap
            = new Dictionary<SE.PngObjectData, TimelinePngObjectEntry>();

        // 保存データへ書き戻した時点の SE 側の実体設定の改訂番号。
        // 表示順・表示タイプ・ブレンド方式・デカール設定は増減を伴わず変わるため、これで変化を検知する
        private int _syncedSettingsRevision = -1;

        // 読込時に画像が見つからず実体を作れなかった XML の定義。
        // 書き戻しは SE 実体から作り直すため、別に持って保存データへ残す
        private readonly List<TimelinePngObjectData> _unresolved = new List<TimelinePngObjectData>();

        // 次の RebuildIfChanged で実体に割り当てる番号の希望。読込では XML の番号、複製では写したキーの番号。
        // 希望が無い実体は同名画像内で空いている最小の番号になる
        private readonly Dictionary<SE.PngObjectData, int> _requestedGroups
            = new Dictionary<SE.PngObjectData, int>();

        public static event UnityAction<TimelinePngObjectEntry> onObjectAdded;
        public static event UnityAction<TimelinePngObjectEntry> onObjectRemoved;

        private PngObjectTimelineManager()
        {
        }

        public TimelinePngObjectEntry GetPngObject(string name)
        {
            TimelinePngObjectEntry entry;
            if (name != null && _entryMap.TryGetValue(name, out entry))
            {
                return entry;
            }
            return null;
        }

        public override void LateUpdate()
        {
            RebuildIfChanged();
        }

        /// <summary>
        /// SE 側の PNG 配置一覧と同期する。
        /// 名前は imageName (relativePath のファイル名) + groupSuffix で採番する。既存の名前は維持し、
        /// 新規実体には予約した番号か、同名画像内で空いている最小の番号を割り当てる。
        /// MTE のタイムライン名規則 (imageName + GetGroupSuffix) と揃える
        /// </summary>
        public void RebuildIfChanged()
        {
            var seObjects = sePngManager.pngObjects;
            if (!IsChanged(seObjects))
            {
                if (_syncedSettingsRevision != sePngManager.entitySettingsRevision)
                {
                    UpdateTimelineData();
                }
                return;
            }

            // 削除検出: SE 一覧から消えた実体の対応を破棄する
            var removed = _dataMap.Keys.Where(d => !seObjects.Contains(d)).ToList();
            var removedEntries = removed.Select(d => _dataMap[d]).ToList();
            foreach (var data in removed)
            {
                _dataMap.Remove(data);
            }

            // 追加検出: 既存の名前は維持し、新規実体には予約した番号か、同名画像内で空いている最小 group を割り当てる
            var addedEntries = new List<TimelinePngObjectEntry>();
            foreach (var data in seObjects)
            {
                if (_dataMap.ContainsKey(data))
                {
                    continue;
                }
                var imageName = Path.GetFileNameWithoutExtension(data.relativePath ?? "");
                int requestedGroup;
                if (!_requestedGroups.TryGetValue(data, out requestedGroup))
                {
                    requestedGroup = -1;
                }
                var group = AssignGroup(GetUsedGroups(imageName), requestedGroup);

                var entry = new TimelinePngObjectEntry
                {
                    imageName = imageName,
                    group = group,
                    name = imageName + PluginUtils.GetGroupSuffix(group),
                    data = data,
                };
                _dataMap[data] = entry;
                addedEntries.Add(entry);
            }

            // 希望は割り当てた時点で役目を終える。消えた実体の希望も残さない
            _requestedGroups.Clear();

            pngObjects = seObjects.Select(d => _dataMap[d]).ToList();
            pngObjectNames = pngObjects.Select(e => e.name).ToList();
            _entryMap = pngObjects.ToDictionary(e => e.name);

            foreach (var entry in addedEntries)
            {
                onObjectAdded?.Invoke(entry);
            }
            foreach (var entry in removedEntries)
            {
                onObjectRemoved?.Invoke(entry);
            }

            // 配置の増減をタイムライン保存データへ即時反映する
            // (保存フックは無いため、変更検知のこのタイミングで書き戻すのが唯一の経路)
            UpdateTimelineData();
        }

        /// <summary>
        /// 同名画像内の番号を決める。requestedGroup (0 以上) が空いていればそれを、
        /// 希望が無いか使用中なら空いている最小の番号を返す
        /// </summary>
        public static int AssignGroup(ICollection<int> usedGroups, int requestedGroup)
        {
            if (requestedGroup >= 0 && !usedGroups.Contains(requestedGroup))
            {
                return requestedGroup;
            }

            var group = 0;
            while (usedGroups.Contains(group))
            {
                group++;
            }
            return group;
        }

        /// <summary>XML の定義に無い実体名。Undo/Redo の再構築で消す対象</summary>
        public static List<string> GetSurplusNames(
            IEnumerable<string> currentNames, IEnumerable<TimelinePngObjectData> sources)
        {
            var sourceNames = new HashSet<string>(sources.Select(d => d.name));
            return currentNames.Where(name => !sourceNames.Contains(name)).ToList();
        }

        /// <summary>
        /// 同名画像内で使用中の番号。画像が見つからず保留にした定義の番号も含める
        /// (実体に取られると、保留の定義のキーがその実体へ効いてしまうため)
        /// </summary>
        private HashSet<int> GetUsedGroups(string imageName)
        {
            var used = new HashSet<int>(
                _dataMap.Values.Where(e => e.imageName == imageName).Select(e => e.group));
            foreach (var data in _unresolved)
            {
                if (data.imageName == imageName)
                {
                    used.Add(data.group);
                }
            }
            return used;
        }

        /// <summary>
        /// 複製した実体を、元のキーを写した名前で対応表へ載せる。
        /// 番号を予約してキーを写してから RebuildIfChanged を回すので、初期フレームの自動登録は
        /// 写した 0F のキーを上書きしない。履歴は「PNGの複製」1 件。
        /// 元の実体が対応表に無い (タイムライン未読込など) ときは何もせず null を返す
        /// </summary>
        public string RegisterDuplicate(SE.PngObjectData source, SE.PngObjectData created)
        {
            TimelinePngObjectEntry sourceEntry;
            if (timeline == null || source == null || created == null
                || !_dataMap.TryGetValue(source, out sourceEntry)
                || _dataMap.ContainsKey(created))
            {
                return null;
            }

            var group = AssignGroup(GetUsedGroups(sourceEntry.imageName), -1);
            _requestedGroups[created] = group;
            var newName = sourceEntry.imageName + PluginUtils.GetGroupSuffix(group);

            timeline.OnCopyPngObject(sourceEntry.name, newName);
            RebuildIfChanged();

            // 対応表へ載せたときの「初期フレーム登録」より後に呼び、履歴の文言をこちらにする
            timelineManager.RequestHistory("PNGの複製: " + newName);
            return newName;
        }

        private bool IsChanged(List<SE.PngObjectData> seObjects)
        {
            if (seObjects.Count != pngObjects.Count)
            {
                return true;
            }
            for (var i = 0; i < seObjects.Count; i++)
            {
                if (!ReferenceEquals(pngObjects[i].data, seObjects[i]))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// タイムライン読込時に PNG 実体を再生成する。
        /// 画像は SE の既知ソース (config → photo) からファイル名一致で探索し、
        /// 見つからない場合は警告してスキップする (キーフレームは XML に保持されたまま)。
        /// 作った実体には XML の番号を予約し、欠番のある XML でもキーとの対応を崩さない。
        /// removeSurplus なら XML に無い実体を消す (Undo/Redo の再構築用。追加・複製の Undo で実体を残さない)。
        /// 最後に XML の実体設定 (表示順・表示タイプ・ブレンド方式・デカール設定) を SE 実体へ適用する
        /// </summary>
        public void Setup(List<TimelinePngObjectData> pngObjectDatas, bool removeSurplus)
        {
            // 引数は timeline.pngObjects そのもので、途中の RebuildIfChanged → UpdateTimelineData が
            // 同じリストを消して SE 側の状態で書き直しうる。XML の値を失わないよう先に複製する
            var sources = new List<TimelinePngObjectData>(pngObjectDatas);
            _unresolved.Clear();

            RebuildIfChanged();

            if (removeSurplus)
            {
                RemoveSurplus(sources);
            }

            // ソースディレクトリの走査は 1 回にまとめ、画像名 → (source, relativePath) の辞書で解決する
            Dictionary<string, KeyValuePair<string, string>> imageIndex = null;

            foreach (var data in sources)
            {
                var name = data.name;
                if (_entryMap.ContainsKey(name))
                {
                    continue;
                }

                if (imageIndex == null)
                {
                    imageIndex = BuildImageIndex();
                }

                KeyValuePair<string, string> found;
                if (!imageIndex.TryGetValue(data.imageName, out found))
                {
                    MTEUtils.LogWarning(
                        "PNG 画像が見つかりません: {0} (UserData\\PngPlacement または PhotoModeData\\Texture へ配置してください)",
                        data.imageName);
                    _unresolved.Add(data);
                    continue;
                }

                var created = sePngManager.AddPng(found.Key, found.Value);
                if (created == null)
                {
                    MTEUtils.LogWarning("PNG の生成に失敗しました: {0}", data.imageName);
                    _unresolved.Add(data);
                    continue;
                }
                _requestedGroups[created] = data.group;
            }

            RebuildIfChanged();

            // XML の実体設定を、生成した実体と名前が一致した既存の実体へ戻す
            foreach (var data in sources)
            {
                var entry = GetPngObject(data.name);
                if (entry != null && entry.data != null)
                {
                    ApplyEntitySettings(entry.data, data);
                }
            }
        }

        /// <summary>XML に無い実体を消す。消す実体が選択中なら Inspector に残さない</summary>
        private void RemoveSurplus(List<TimelinePngObjectData> sources)
        {
            var surplusNames = GetSurplusNames(pngObjectNames, sources);
            if (surplusNames.Count == 0)
            {
                return;
            }

            var selection = SE.SelectionManager.instance;
            foreach (var name in surplusNames)
            {
                var entry = GetPngObject(name);
                if (entry == null || entry.data == null)
                {
                    continue;
                }
                if (entry.data.rootObject != null && selection.selectedObject == entry.data.rootObject)
                {
                    selection.Select(null);
                }
                sePngManager.RemovePng(entry.data);
            }

            // 消した実体の名前を空けてから XML の定義を作る (同じ名前を取り直せるように)
            RebuildIfChanged();
        }

        private static void ApplyEntitySettings(SE.PngObjectData target, TimelinePngObjectData source)
        {
            // 要素の無い XML は 0 になる。0 以下の表示順は板が背景より先に描かれ消えるため既定のまま残す
            if (source.renderQueue > 0)
            {
                sePngManager.SetRenderQueue(target, source.renderQueue);
            }
            // 範囲外の値は FromXml で既定値へ直してあるため、そのままキャストしてよい
            sePngManager.SetDisplayType(target, (SE.PngDisplayType)source.displayType);
            sePngManager.SetBlendMode(target, (SE.PngBlendMode)source.blendMode);
            sePngManager.SetDecalFadeAngle(target, source.decalFadeAngle);
            sePngManager.SetDecalProjectOnMaids(target, source.decalProjectOnMaids);
        }

        // 画像名 → (source, relativePath)。config を優先するため後勝ちしないよう先着のみ登録する
        private static Dictionary<string, KeyValuePair<string, string>> BuildImageIndex()
        {
            var index = new Dictionary<string, KeyValuePair<string, string>>();
            foreach (var src in new[] { SE.PngPlacementManager.SOURCE_CONFIG, SE.PngPlacementManager.SOURCE_PHOTO })
            {
                var dir = SE.PngPlacementManager.GetSourceDirectory(src);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                {
                    continue;
                }
                foreach (var file in Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories))
                {
                    var imageName = Path.GetFileNameWithoutExtension(file);
                    if (!index.ContainsKey(imageName))
                    {
                        index[imageName] = new KeyValuePair<string, string>(
                            src, file.Substring(dir.Length).TrimStart('\\', '/'));
                    }
                }
            }
            return index;
        }

        /// <summary>タイムライン保存用に配置一覧と実体設定を書き戻す</summary>
        public void UpdateTimelineData()
        {
            _syncedSettingsRevision = sePngManager.entitySettingsRevision;

            if (timeline == null)
            {
                return;
            }

            timeline.pngObjects.Clear();
            foreach (var entry in pngObjects)
            {
                var se = entry.data;
                var data = new TimelinePngObjectData
                {
                    imageName = entry.imageName,
                    group = entry.group,
                    // primitive / squareUV / shaderDisplay は SE では固定 Quad + 標準シェーダー
                    // で代替するため既定値のまま保存する (MTE 互換のためフィールド自体は維持)
                    renderQueue = se != null ? se.renderQueue : SE.PngPlacementManager.DefaultRenderQueue,
                };
                if (se != null)
                {
                    data.displayType = (int)se.displayType;
                    data.blendMode = (int)se.blendMode;
                    data.decalFadeAngle = se.decalFadeAngle;
                    data.decalProjectOnMaids = se.decalProjectOnMaids;
                }
                timeline.pngObjects.Add(data);
            }
            AppendUnresolved(timeline.pngObjects, _unresolved);
        }

        /// <summary>実体の無い定義を、同名の実体定義が無いものだけ保存データへ足す</summary>
        public static void AppendUnresolved(
            List<TimelinePngObjectData> target, List<TimelinePngObjectData> unresolved)
        {
            foreach (var data in unresolved)
            {
                var name = data.name;
                if (!target.Any(d => d.name == name))
                {
                    target.Add(data);
                }
            }
        }

        public override void OnLoad()
        {
            if (timeline != null)
            {
                // 余りを消すのは Undo/Redo の再構築だけ。新規作成・ファイル読込ではシーンの PNG を残す
                Setup(timeline.pngObjects, timelineManager.isRestoringHistory);
            }
        }

        public override void OnPluginDisable()
        {
            Reset();
        }

        public void Reset()
        {
            pngObjects.Clear();
            pngObjectNames.Clear();
            _entryMap.Clear();
            _dataMap.Clear();
            _syncedSettingsRevision = -1;
            _unresolved.Clear();
            _requestedGroups.Clear();
        }
    }
}
