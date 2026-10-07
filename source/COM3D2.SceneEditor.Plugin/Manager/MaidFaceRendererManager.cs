using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// メイドの顔 (head スロット) の Renderer に、影の上書き (受ける・落とす) と Face レイヤーへの移動を書く。
    /// ゲームは通常顔も Charactor に置くため、「顔のみ／体のみ」の追加ライトがある間だけ顔を Face へ移す。
    /// 元の値は Renderer ごとに初めて書くときに退避し、上書きも移動も無くなったら戻す。
    /// タイムライン (ライトの照射対象・顔の影のキー) と UI の後、影の複製の前に LateUpdate で書く
    /// </summary>
    public class MaidFaceRendererManager : ManagerBase
    {
        private const string HeadSlotName = "head";

        /// <summary>ゲーム側ライトを探し直す間隔 (フレーム)。毎フレームの全走査を避ける</summary>
        private const int LightScanInterval = 60;

        /// <summary>Renderer の 3 値。元の値と、前回書いた値の両方に使う</summary>
        private struct RendererValues
        {
            public int layer;
            public bool receiveShadows;
            public ShadowCastingMode castingMode;

            public static RendererValues From(Renderer renderer)
            {
                return new RendererValues
                {
                    layer = renderer.gameObject.layer,
                    receiveShadows = renderer.receiveShadows,
                    castingMode = renderer.shadowCastingMode,
                };
            }
        }

        private class Original
        {
            /// <summary>初めて触ったときのゲームの値</summary>
            public RendererValues original;
            /// <summary>前回書いた値 (書いていない値は元の値)。他者が変えた値を取り消さないために持つ</summary>
            public RendererValues written;
        }

        /// <summary>head 配下の Renderer の増減を確かめる間隔 (フレーム)。影の複製と同じ</summary>
        private const int RendererCheckInterval = 60;

        private class Entry
        {
            public readonly Dictionary<string, bool> overrides = new Dictionary<string, bool>();
            /// <summary>Renderer を集めたときの head スロットの実体。入れ替わったら集め直す</summary>
            public GameObject headObj;
            public readonly List<Renderer> renderers = new List<Renderer>();
            public readonly Dictionary<Renderer, Original> originals = new Dictionary<Renderer, Original>();
            public bool wasBusy;
            /// <summary>head 配下の Renderer の数を最後に確かめたフレーム</summary>
            public int lastRendererCheckFrame = -1;
        }

        /// <summary>HasRendererCountChanged の作業用。毎回の確保を避ける</summary>
        private static readonly List<Renderer> _rendererScratch = new List<Renderer>();

        private static MaidFaceRendererManager _instance;
        public static MaidFaceRendererManager instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new MaidFaceRendererManager();
                }
                return _instance;
            }
        }

        private readonly Dictionary<Maid, Entry> _entries = new Dictionary<Maid, Entry>();
        private readonly List<Maid> _maids = new List<Maid>();
        private readonly List<Maid> _removeScratch = new List<Maid>();

        /// <summary>Face ビットを足したゲーム側ライトと元の cullingMask</summary>
        private readonly Dictionary<Light, int> _compensatedLights = new Dictionary<Light, int>();
        private int _lastLightScanFrame = -1;

        private int _charactorLayer = -1;
        private int _faceLayer = -1;

        public bool isFaceLayerActive { get; private set; }

        /// <summary>
        /// 顔の shadowCastingMode を書くたびに増える。影の複製 (CharacterShadowProxyManager) は 60 フレームごとにしか
        /// 顔ぶれを取り直さないため、これの変化で即座に作り直す
        /// </summary>
        public int castVersion { get; private set; }

        private MaidFaceRendererManager()
        {
        }

        public override void Init()
        {
            _charactorLayer = LayerMask.NameToLayer("Charactor");
            _faceLayer = LayerMask.NameToLayer("Face");
            if (_charactorLayer < 0 || _faceLayer < 0)
            {
                MTEUtils.LogError("Charactor / Face レイヤーが見つからないため、顔のみ・体のみのライトで顔を照らし分けられません");
            }
        }

        public bool TryGetOverride(Maid maid, string item, out bool enabled)
        {
            enabled = false;
            Entry entry;
            return maid != null && _entries.TryGetValue(maid, out entry) && entry.overrides.TryGetValue(item, out enabled);
        }

        public void SetOverride(Maid maid, string item, bool enabled)
        {
            if (maid == null || FaceShadowItems.Find(item) == null)
            {
                return;
            }
            GetOrCreateEntry(maid).overrides[item] = enabled;
        }

        public void ClearOverride(Maid maid, string item)
        {
            Entry entry;
            if (maid != null && _entries.TryGetValue(maid, out entry))
            {
                entry.overrides.Remove(item);
            }
        }

        public void ClearAll(Maid maid)
        {
            Entry entry;
            if (maid != null && _entries.TryGetValue(maid, out entry))
            {
                entry.overrides.Clear();
            }
        }

        public Dictionary<string, bool> GetOverrides(Maid maid)
        {
            Entry entry;
            return maid != null && _entries.TryGetValue(maid, out entry)
                ? new Dictionary<string, bool>(entry.overrides)
                : new Dictionary<string, bool>();
        }

        public void SetOverrides(Maid maid, IDictionary<string, bool> overrides)
        {
            if (maid == null)
            {
                return;
            }
            var entry = GetOrCreateEntry(maid);
            entry.overrides.Clear();
            foreach (var pair in overrides)
            {
                if (FaceShadowItems.Find(pair.Key) != null)
                {
                    entry.overrides[pair.Key] = pair.Value;
                }
            }
        }

        /// <summary>今の値。上書きがあればその値、無ければ顔の Renderer の値 (読めなければゲームの既定)</summary>
        public bool IsEnabled(Maid maid, string item)
        {
            bool value;
            if (TryGetOverride(maid, item, out value))
            {
                return value;
            }
            var definition = FaceShadowItems.Find(item);
            var fallback = definition != null && definition.gameDefault;
            var head = maid != null && MaidNodeVisibilityController.IsBodyReady(maid)
                ? SlotBoneManager.GetSlotObject(maid, HeadSlotName) : null;
            var renderer = head != null ? head.GetComponentInChildren<Renderer>(true) : null;
            if (renderer == null)
            {
                return fallback;
            }
            return item == FaceShadowItems.Cast
                ? renderer.shadowCastingMode != ShadowCastingMode.Off
                : renderer.receiveShadows;
        }

        public override void LateUpdate()
        {
            var moveToFace = _charactorLayer >= 0 && _faceLayer >= 0 && HasFaceLayerLight();
            isFaceLayerActive = moveToFace;
            UpdateLightCompensation(moveToFace);

            CollectMaids(_maids);
            foreach (var maid in _maids)
            {
                Entry entry;
                _entries.TryGetValue(maid, out entry);
                if (entry == null)
                {
                    if (!moveToFace)
                    {
                        continue;
                    }
                    entry = GetOrCreateEntry(maid);
                }
                Apply(maid, entry, moveToFace);
            }
            RemoveIdleEntries(moveToFace);
        }

        public override void OnPluginDisable()
        {
            Destroy();
        }

        public override void OnChangedSceneLevel(Scene scene, LoadSceneMode sceneMode)
        {
            Destroy();
        }

        /// <summary>メイドがストックへ戻るとき。上書きを外し、顔をゲームの値へ戻す</summary>
        public void Release(Maid maid)
        {
            Entry entry;
            if (maid == null || !_entries.TryGetValue(maid, out entry))
            {
                return;
            }
            RestoreAll(entry);
            _entries.Remove(maid);
        }

        /// <summary>すべてのメイドの顔とゲーム側ライトを元へ戻す</summary>
        public void Destroy()
        {
            foreach (var entry in _entries.Values)
            {
                RestoreAll(entry);
            }
            _entries.Clear();
            UpdateLightCompensation(false);
            isFaceLayerActive = false;
        }

        private Entry GetOrCreateEntry(Maid maid)
        {
            Entry entry;
            if (!_entries.TryGetValue(maid, out entry))
            {
                entry = new Entry();
                _entries.Add(maid, entry);
            }
            return entry;
        }

        private static bool HasFaceLayerLight()
        {
            foreach (var light in StudioLightManager.instance.lights)
            {
                if (light != null && FaceRendererRules.NeedsFaceLayer(
                    light.enabled && light.gameObject.activeInHierarchy,
                    LightTarget.FromCullingMask(light.cullingMask)))
                {
                    return true;
                }
            }
            return false;
        }

        private static void CollectMaids(List<Maid> results)
        {
            results.Clear();
            var gameMain = GameMain.Instance;
            var characterMgr = gameMain != null ? gameMain.CharacterMgr : null;
            if (characterMgr == null)
            {
                return;
            }
            for (var i = 0; i < characterMgr.GetMaidCount(); i++)
            {
                var maid = characterMgr.GetMaid(i);
                if (maid != null && maid.body0 != null && maid.gameObject.activeInHierarchy)
                {
                    results.Add(maid);
                }
            }
        }

        private void Apply(Maid maid, Entry entry, bool moveToFace)
        {
            // 着替え中はスロットが作り直されるので書かない。明けたら集め直して書き直す
            if (maid.IsAllProcPropBusy || !MaidNodeVisibilityController.IsBodyReady(maid))
            {
                entry.wasBusy = true;
                return;
            }

            var head = SlotBoneManager.GetSlotObject(maid, HeadSlotName);
            var frame = Time.frameCount;
            if (head != entry.headObj || entry.wasBusy
                || (frame - entry.lastRendererCheckFrame >= RendererCheckInterval && HasRendererCountChanged(entry)))
            {
                entry.headObj = head;
                entry.wasBusy = false;
                CollectRenderers(entry);
            }
            if (frame - entry.lastRendererCheckFrame >= RendererCheckInterval)
            {
                entry.lastRendererCheckFrame = frame;
            }

            foreach (var renderer in entry.renderers)
            {
                if (renderer == null)
                {
                    continue;
                }
                Original state;
                if (!entry.originals.TryGetValue(renderer, out state))
                {
                    var current = RendererValues.From(renderer);
                    state = new Original { original = current, written = current };
                    entry.originals.Add(renderer, state);
                }

                // 決めた値が前回書いた値から変わったときだけ書く。上書きの無い項目は元の値 = 前回の値なので書かない
                var layer = FaceRendererRules.ResolveLayer(state.original.layer, moveToFace, _charactorLayer, _faceLayer);
                if (FaceRendererRules.ShouldWrite(layer, state.written.layer))
                {
                    renderer.gameObject.layer = layer;
                    state.written.layer = layer;
                }
                var receive = FaceRendererRules.ResolveReceive(state.original.receiveShadows, entry.overrides);
                if (FaceRendererRules.ShouldWrite(receive, state.written.receiveShadows))
                {
                    renderer.receiveShadows = receive;
                    state.written.receiveShadows = receive;
                }
                var cast = FaceRendererRules.ResolveCast(state.original.castingMode, entry.overrides);
                if (FaceRendererRules.ShouldWrite(cast, state.written.castingMode))
                {
                    renderer.shadowCastingMode = cast;
                    state.written.castingMode = cast;
                    castVersion++;
                }
            }
        }

        private static bool HasRendererCountChanged(Entry entry)
        {
            if (entry.headObj == null)
            {
                return false;
            }
            entry.headObj.GetComponentsInChildren(true, _rendererScratch);
            return _rendererScratch.Count != entry.renderers.Count;
        }

        /// <summary>
        /// head スロットの Renderer を集め直す。破棄された Renderer の退避は捨てるが、残っている Renderer の退避は残す
        /// (着替え中に同じ Renderer の退避を捨てると、SE が書いた値を元の値と取り違える)。
        /// 残った Renderer の「前回書いた値」は今の値にそろえる。着替えでゲームが値を戻していても、上書きを 1 回書き直すため
        /// </summary>
        private static void CollectRenderers(Entry entry)
        {
            entry.renderers.Clear();
            if (entry.headObj != null)
            {
                entry.headObj.GetComponentsInChildren(true, entry.renderers);
            }

            var dead = new List<Renderer>();
            foreach (var pair in entry.originals)
            {
                var renderer = pair.Key;
                if (renderer == null || !entry.renderers.Contains(renderer))
                {
                    dead.Add(renderer);
                    continue;
                }
                pair.Value.written = RendererValues.From(renderer);
            }
            foreach (var renderer in dead)
            {
                entry.originals.Remove(renderer);
            }
        }

        /// <summary>元の値へ戻す。前回書いた後に他者が書き換えた値は残す</summary>
        private void RestoreAll(Entry entry)
        {
            foreach (var pair in entry.originals)
            {
                var renderer = pair.Key;
                if (renderer == null)
                {
                    continue;
                }
                var state = pair.Value;
                renderer.gameObject.layer = FaceRendererRules.ResolveRestore(
                    renderer.gameObject.layer, state.written.layer, state.original.layer);
                renderer.receiveShadows = FaceRendererRules.ResolveRestore(
                    renderer.receiveShadows, state.written.receiveShadows, state.original.receiveShadows);
                var cast = FaceRendererRules.ResolveRestore(
                    renderer.shadowCastingMode, state.written.castingMode, state.original.castingMode);
                if (renderer.shadowCastingMode != cast)
                {
                    renderer.shadowCastingMode = cast;
                    castVersion++;
                }
            }
            entry.originals.Clear();
            entry.renderers.Clear();
            entry.headObj = null;
        }

        /// <summary>
        /// 上書きが無く移動もしないエントリは、ゲームの値へ戻して捨てる。
        /// いなくなったメイドのエントリも捨てる (Renderer は破棄済みか、Release 済み)
        /// </summary>
        private void RemoveIdleEntries(bool moveToFace)
        {
            _removeScratch.Clear();
            foreach (var pair in _entries)
            {
                var entry = pair.Value;
                if (_maids.Contains(pair.Key))
                {
                    if (FaceRendererRules.IsDefault(entry.overrides, moveToFace))
                    {
                        RestoreAll(entry);
                        _removeScratch.Add(pair.Key);
                    }
                    continue;
                }

                // 非表示などで一時的にいないメイドは、戻ってきたときのために上書きだけ残す。退避は戻して捨てる
                RestoreAll(entry);
                if (entry.overrides.Count == 0)
                {
                    _removeScratch.Add(pair.Key);
                }
            }
            foreach (var maid in _removeScratch)
            {
                _entries.Remove(maid);
            }
        }

        /// <summary>
        /// 顔を移している間、Charactor を照らすのに Face を照らさないゲーム側ライトへ Face ビットを足す。
        /// 移さなくなったら足したライトだけを戻す
        /// </summary>
        private void UpdateLightCompensation(bool moveToFace)
        {
            if (!moveToFace)
            {
                if (_compensatedLights.Count > 0)
                {
                    var face = 1 << _faceLayer;
                    foreach (var pair in _compensatedLights)
                    {
                        // 足した後にゲームが別のマスクへ変えていたら、その値を残す
                        if (pair.Key != null && pair.Key.cullingMask == (pair.Value | face))
                        {
                            pair.Key.cullingMask = pair.Value;
                        }
                    }
                    _compensatedLights.Clear();
                }
                _lastLightScanFrame = -1;
                return;
            }

            var charactorMask = 1 << _charactorLayer;
            var faceMask = 1 << _faceLayer;
            var frame = Time.frameCount;
            if (_lastLightScanFrame < 0 || frame - _lastLightScanFrame >= LightScanInterval)
            {
                _lastLightScanFrame = frame;
                RemoveDestroyedLights();
                // FindObjectsOfType は Light コンポーネントが無効でも GameObject がアクティブなら拾う
                // (CharaDirectionalLight は普段この状態。実機確認済み)
                var studioLights = StudioLightManager.instance.lights;
                foreach (var light in Object.FindObjectsOfType<Light>())
                {
                    if (studioLights.Contains(light) || _compensatedLights.ContainsKey(light))
                    {
                        continue;
                    }
                    if (FaceRendererRules.NeedsLightCompensation(light.cullingMask, charactorMask, faceMask))
                    {
                        _compensatedLights.Add(light, light.cullingMask);
                    }
                }
            }

            // ゲームが書き戻しても次のフレームで足し直す
            foreach (var light in _compensatedLights.Keys)
            {
                if (light != null && (light.cullingMask & faceMask) == 0)
                {
                    light.cullingMask |= faceMask;
                }
            }
        }

        private readonly List<Light> _lightScratch = new List<Light>();

        private void RemoveDestroyedLights()
        {
            _lightScratch.Clear();
            foreach (var light in _compensatedLights.Keys)
            {
                if (light == null)
                {
                    _lightScratch.Add(light);
                }
            }
            foreach (var light in _lightScratch)
            {
                _compensatedLights.Remove(light);
            }
        }
    }
}
