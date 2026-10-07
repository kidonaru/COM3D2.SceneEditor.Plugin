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
    /// 元の値は Renderer ごとに初めて書くときに退避し、上書きも移動も無くなったら戻す
    /// </summary>
    public class MaidFaceRendererManager : ManagerBase
    {
        private const string HeadSlotName = "head";

        /// <summary>ゲーム側ライトを探し直す間隔 (フレーム)。毎フレームの全走査を避ける</summary>
        private const int LightScanInterval = 60;

        /// <summary>head 配下の Renderer の増減を確かめる間隔 (フレーム)</summary>
        private const int RendererCheckInterval = 60;

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

        private class RendererState
        {
            /// <summary>ゲームの値。他者が値を変えたらその値に置き直す</summary>
            public RendererValues original;
            /// <summary>前回書いた値 (書いていない値は元の値)。他者が変えたかを見分けるために持つ</summary>
            public RendererValues written;
        }

        private class Entry
        {
            public readonly Dictionary<string, bool> overrides = new Dictionary<string, bool>();
            /// <summary>Renderer を集めたときの head スロットの実体。入れ替わったら集め直す</summary>
            public GameObject headObj;
            public readonly List<Renderer> renderers = new List<Renderer>();
            public readonly Dictionary<Renderer, RendererState> states = new Dictionary<Renderer, RendererState>();
            public bool wasBusy;
            /// <summary>head 配下の Renderer の数を最後に確かめたフレーム</summary>
            public int lastRendererCheckFrame = -1;
        }

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

        /// <summary>
        /// 顔の shadowCastingMode を書いたメイド。影の複製 (CharacterShadowProxyManager) は 60 フレームごとにしか
        /// 顔ぶれを取り直さないため、これを見てそのメイドの複製だけをすぐ作り直す
        /// </summary>
        private readonly HashSet<Maid> _castChangedMaids = new HashSet<Maid>();

        /// <summary>Face ビットを足したゲーム側ライトと、Face ビットを足す前の cullingMask</summary>
        private readonly Dictionary<Light, int> _compensatedLights = new Dictionary<Light, int>();
        private int _lastLightScanFrame = -1;

        private int _charactorLayer = -1;
        private int _faceLayer = -1;

        // 毎回の確保を避ける作業用
        private readonly List<Maid> _removeScratch = new List<Maid>();
        private readonly List<Renderer> _rendererScratch = new List<Renderer>();
        private readonly List<Light> _lightScratch = new List<Light>();

        /// <summary>顔を Face レイヤーへ移しているか (実機検証用)</summary>
        public bool isFaceLayerActive { get; private set; }

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

        /// <summary>前回の問い合わせ以降に、このメイドの顔の影落としを書いたか。問い合わせると記録を消す</summary>
        public bool ConsumeCastChanged(Maid maid)
        {
            return _castChangedMaids.Remove(maid);
        }

        public override void LateUpdate()
        {
            var moveToFace = _charactorLayer >= 0 && _faceLayer >= 0 && HasFaceLayerLight();
            isFaceLayerActive = moveToFace;
            if (moveToFace)
            {
                CompensateLights();
            }
            else
            {
                RestoreLights();
            }

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
            RestoreAll(maid, entry);
            _entries.Remove(maid);
        }

        /// <summary>すべてのメイドの顔とゲーム側ライトを元へ戻す</summary>
        public void Destroy()
        {
            foreach (var pair in _entries)
            {
                RestoreAll(pair.Key, pair.Value);
            }
            _entries.Clear();
            RestoreLights();
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
            var isCheckDue = frame - entry.lastRendererCheckFrame >= RendererCheckInterval;
            if (isCheckDue)
            {
                entry.lastRendererCheckFrame = frame;
            }
            if (head != entry.headObj || entry.wasBusy || (isCheckDue && HasRendererCountChanged(entry)))
            {
                entry.headObj = head;
                entry.wasBusy = false;
                CollectRenderers(entry);
            }

            var receiveOverridden = entry.overrides.ContainsKey(FaceShadowItems.Receive);
            var castOverridden = entry.overrides.ContainsKey(FaceShadowItems.Cast);
            foreach (var renderer in entry.renderers)
            {
                if (renderer == null)
                {
                    continue;
                }
                RendererState state;
                if (!entry.states.TryGetValue(renderer, out state))
                {
                    var values = RendererValues.From(renderer);
                    state = new RendererState { original = values, written = values };
                    entry.states.Add(renderer, state);
                }

                // 他者が前回書いた値から変えていたら、その値を元の値として取り込み、取り消さない。
                // レイヤーは常に取り込む (VR の一人称モードが頭を隠すために Face へ移す)
                var layer = renderer.gameObject.layer;
                if (FaceRendererRules.ShouldAdoptExternal(layer, state.written.layer, false))
                {
                    state.original.layer = layer;
                }
                var desiredLayer = FaceRendererRules.ResolveLayer(
                    state.original.layer, moveToFace, _charactorLayer, _faceLayer);
                if (FaceRendererRules.ShouldWrite(desiredLayer, layer))
                {
                    renderer.gameObject.layer = desiredLayer;
                }
                state.written.layer = desiredLayer;

                var receive = renderer.receiveShadows;
                if (FaceRendererRules.ShouldAdoptExternal(receive, state.written.receiveShadows, receiveOverridden))
                {
                    state.original.receiveShadows = receive;
                }
                var desiredReceive = FaceRendererRules.ResolveReceive(state.original.receiveShadows, entry.overrides);
                if (FaceRendererRules.ShouldWrite(desiredReceive, receive))
                {
                    renderer.receiveShadows = desiredReceive;
                }
                state.written.receiveShadows = desiredReceive;

                var cast = renderer.shadowCastingMode;
                if (FaceRendererRules.ShouldAdoptExternal(cast, state.written.castingMode, castOverridden))
                {
                    state.original.castingMode = cast;
                }
                var desiredCast = FaceRendererRules.ResolveCast(state.original.castingMode, entry.overrides);
                if (FaceRendererRules.ShouldWrite(desiredCast, cast))
                {
                    renderer.shadowCastingMode = desiredCast;
                    _castChangedMaids.Add(maid);
                }
                state.written.castingMode = desiredCast;
            }
        }

        private bool HasRendererCountChanged(Entry entry)
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
        /// (着替え中に同じ Renderer の退避を捨てると、SE が書いた値を元の値と取り違える)
        /// </summary>
        private void CollectRenderers(Entry entry)
        {
            entry.renderers.Clear();
            if (entry.headObj != null)
            {
                entry.headObj.GetComponentsInChildren(true, entry.renderers);
            }

            _rendererScratch.Clear();
            foreach (var renderer in entry.states.Keys)
            {
                if (renderer == null || !entry.renderers.Contains(renderer))
                {
                    _rendererScratch.Add(renderer);
                }
            }
            foreach (var renderer in _rendererScratch)
            {
                entry.states.Remove(renderer);
            }
        }

        /// <summary>元の値へ戻す。前回書いた後に他者が書き換えた値は残す</summary>
        private void RestoreAll(Maid maid, Entry entry)
        {
            foreach (var pair in entry.states)
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
                    _castChangedMaids.Add(maid);
                }
            }
            entry.states.Clear();
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
                        RestoreAll(pair.Key, entry);
                        _removeScratch.Add(pair.Key);
                    }
                    continue;
                }

                // 非表示などで一時的にいないメイドは、戻ってきたときのために上書きだけ残す。退避は戻して捨てる
                RestoreAll(pair.Key, entry);
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
        /// ゲームや他プラグインが書き戻しても次のフレームで足し直す
        /// </summary>
        private void CompensateLights()
        {
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

            _lightScratch.Clear();
            _lightScratch.AddRange(_compensatedLights.Keys);
            foreach (var light in _lightScratch)
            {
                if (light != null && (light.cullingMask & faceMask) == 0)
                {
                    // 書き戻された今のマスクを、戻すときの値として覚え直す
                    _compensatedLights[light] = light.cullingMask;
                    light.cullingMask |= faceMask;
                }
            }
        }

        /// <summary>Face ビットを足したライトを戻す。足した後に他者が別のマスクへ変えていたら、その値を残す</summary>
        private void RestoreLights()
        {
            if (_compensatedLights.Count > 0)
            {
                var faceMask = 1 << _faceLayer;
                foreach (var pair in _compensatedLights)
                {
                    if (pair.Key != null && pair.Key.cullingMask == (pair.Value | faceMask))
                    {
                        pair.Key.cullingMask = pair.Value;
                    }
                }
                _compensatedLights.Clear();
            }
            _lastLightScanFrame = -1;
        }

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
