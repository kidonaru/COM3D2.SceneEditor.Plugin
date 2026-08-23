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
        /// 名前は imageName (relativePath のファイル名) + groupSuffix (同名の出現順) で採番し、
        /// MTE のタイムライン名規則 (imageName + GetGroupSuffix) と揃える
        /// </summary>
        public void RebuildIfChanged()
        {
            var seObjects = sePngManager.pngObjects;
            if (!IsChanged(seObjects))
            {
                return;
            }

            // 削除検出: SE 一覧から消えた実体の対応を破棄する
            var removed = _dataMap.Keys.Where(d => !seObjects.Contains(d)).ToList();
            var removedEntries = removed.Select(d => _dataMap[d]).ToList();
            foreach (var data in removed)
            {
                _dataMap.Remove(data);
            }

            // 追加検出: 既存の名前は維持し、新規実体には同名画像内で空いている最小 group を割り当てる
            var addedEntries = new List<TimelinePngObjectEntry>();
            foreach (var data in seObjects)
            {
                if (_dataMap.ContainsKey(data))
                {
                    continue;
                }
                var imageName = Path.GetFileNameWithoutExtension(data.relativePath ?? "");
                var usedGroups = new HashSet<int>(
                    _dataMap.Values.Where(e => e.imageName == imageName).Select(e => e.group));
                var group = 0;
                while (usedGroups.Contains(group))
                {
                    group++;
                }

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
        /// 見つからない場合は警告してスキップする (キーフレームは XML に保持されたまま)
        /// </summary>
        public void Setup(List<TimelinePngObjectData> pngObjectDatas)
        {
            RebuildIfChanged();

            // ソースディレクトリの走査は 1 回にまとめ、画像名 → (source, relativePath) の辞書で解決する
            Dictionary<string, KeyValuePair<string, string>> imageIndex = null;

            foreach (var data in pngObjectDatas)
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
                    continue;
                }

                var created = sePngManager.AddPng(found.Key, found.Value);
                if (created == null)
                {
                    MTEUtils.LogWarning("PNG の生成に失敗しました: {0}", data.imageName);
                }
            }

            RebuildIfChanged();
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

        /// <summary>タイムライン保存用に配置一覧を書き戻す</summary>
        public void UpdateTimelineData()
        {
            if (timeline == null)
            {
                return;
            }

            timeline.pngObjects.Clear();
            foreach (var entry in pngObjects)
            {
                var data = new TimelinePngObjectData
                {
                    imageName = entry.imageName,
                    group = entry.group,
                    // primitive / squareUV / shaderDisplay は SE では固定 Quad + 標準シェーダー
                    // で代替するため既定値のまま保存する (MTE 互換のためフィールド自体は維持)
                    renderQueue = entry.data != null ? entry.data.renderQueue : SE.PngPlacementManager.DefaultRenderQueue,
                };
                timeline.pngObjects.Add(data);
            }
        }

        public override void OnLoad()
        {
            if (timeline != null)
            {
                Setup(timeline.pngObjects);
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
        }
    }
}
