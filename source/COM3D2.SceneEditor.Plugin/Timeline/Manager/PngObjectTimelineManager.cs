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

        private static SE.PngPlacementManager seManager => SE.PngPlacementManager.instance;

        public List<TimelinePngObjectEntry> pngObjects = new List<TimelinePngObjectEntry>();
        public List<string> pngObjectNames = new List<string>();
        private Dictionary<string, TimelinePngObjectEntry> _entryMap
            = new Dictionary<string, TimelinePngObjectEntry>();

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
            var seObjects = seManager.pngObjects;
            if (!IsChanged(seObjects))
            {
                return;
            }

            var oldEntries = pngObjects;
            pngObjects = new List<TimelinePngObjectEntry>(seObjects.Count);
            pngObjectNames.Clear();
            _entryMap.Clear();

            var groupCounter = new Dictionary<string, int>();
            foreach (var data in seObjects)
            {
                var imageName = Path.GetFileNameWithoutExtension(data.relativePath ?? "");
                int group;
                groupCounter.TryGetValue(imageName, out group);
                groupCounter[imageName] = group + 1;

                var entry = new TimelinePngObjectEntry
                {
                    imageName = imageName,
                    group = group,
                    name = imageName + PluginUtils.GetGroupSuffix(group),
                    data = data,
                };
                pngObjects.Add(entry);
                pngObjectNames.Add(entry.name);
                _entryMap[entry.name] = entry;
            }

            foreach (var entry in pngObjects)
            {
                if (!oldEntries.Any(e => e.name == entry.name))
                {
                    onObjectAdded?.Invoke(entry);
                }
            }
            foreach (var entry in oldEntries)
            {
                if (!_entryMap.ContainsKey(entry.name))
                {
                    onObjectRemoved?.Invoke(entry);
                }
            }
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

            foreach (var data in pngObjectDatas)
            {
                var name = data.name;
                if (_entryMap.ContainsKey(name))
                {
                    continue;
                }

                string source, relativePath;
                if (!FindImage(data.imageName, out source, out relativePath))
                {
                    MTEUtils.LogWarning(
                        "PNG 画像が見つかりません: {0} (UserData\\PngPlacement または PhotoModeData\\Texture へ配置してください)",
                        data.imageName);
                    continue;
                }

                var created = seManager.AddPng(source, relativePath);
                if (created == null)
                {
                    MTEUtils.LogWarning("PNG の生成に失敗しました: {0}", data.imageName);
                }
            }

            RebuildIfChanged();
        }

        private static bool FindImage(string imageName, out string source, out string relativePath)
        {
            foreach (var src in new[] { SE.PngPlacementManager.SOURCE_CONFIG, SE.PngPlacementManager.SOURCE_PHOTO })
            {
                var dir = SE.PngPlacementManager.GetSourceDirectory(src);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                {
                    continue;
                }
                foreach (var file in Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories))
                {
                    if (Path.GetFileNameWithoutExtension(file) == imageName)
                    {
                        source = src;
                        relativePath = file.Substring(dir.Length).TrimStart('\\', '/');
                        return true;
                    }
                }
            }
            source = null;
            relativePath = null;
            return false;
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
        }
    }
}
