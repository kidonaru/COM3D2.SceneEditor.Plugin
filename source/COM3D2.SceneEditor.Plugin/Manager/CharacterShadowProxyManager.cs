using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 「背景のみ」のライトでキャラの影を背景へ落とすための、影専用の複製を管理する。
    /// ライトの cullingMask から外したキャラはそのライトの影も落とさない (Unity 標準パイプラインの挙動) ため、
    /// メッシュ・骨・マテリアルを共有する ShadowsOnly の複製を影用レイヤーに置いて、影だけを戻す。
    /// 骨を共有するのでモーションにそのまま追従し、体型や表情は TMorph がメッシュを直接書き換えるので共有で追従する。
    /// 複製はメイドの階層の外に置く。ゲームや他の機能が GetComponentsInChildren で集める対象に混ざらないようにするため
    /// </summary>
    public class CharacterShadowProxyManager : ManagerBase
    {
        private const string RootName = "SceneEditor CharacterShadowProxies";

        // 着替え以外で Renderer が増える変化 (部位の付け外し等) は参照の比較では拾えないため、
        // 一定フレームごとに複製元の顔ぶれを取り直して比べる
        private const int SourceCheckInterval = 60;

        private class Proxy
        {
            public Renderer source;
            public Renderer renderer;
            public SkinnedMeshRenderer sourceSkinned;
            public SkinnedMeshRenderer proxySkinned;
            public MeshFilter sourceFilter;
            public Mesh mesh;
            public Material[] materials;
            public int blendShapeCount;
        }

        private class Entry
        {
            public bool isDirty = true;
            public readonly List<Proxy> proxies = new List<Proxy>();
        }

        private static CharacterShadowProxyManager _instance;
        public static CharacterShadowProxyManager instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new CharacterShadowProxyManager();
                }
                return _instance;
            }
        }

        private readonly Dictionary<Maid, Entry> _entries = new Dictionary<Maid, Entry>();
        private readonly List<Maid> _characters = new List<Maid>();
        private readonly List<Maid> _absentCharacters = new List<Maid>();
        private readonly List<Renderer> _sources = new List<Renderer>();
        // onPreCull で止めた複製 (onPostRender で戻す)
        private readonly List<Renderer> _hiddenForCamera = new List<Renderer>();
        private GameObject _root;
        private bool _isCameraHooked = false;
        private int _lastSourceCheckFrame = -1;

        /// <summary>今ある複製の数 (実機検証用)</summary>
        public int proxyCount
        {
            get
            {
                var count = 0;
                foreach (var entry in _entries.Values)
                {
                    count += entry.proxies.Count;
                }
                return count;
            }
        }

        /// <summary>ライトの適用 (タイムライン・UI) が済んだ後に判定するため LateUpdate で行う</summary>
        public override void Init()
        {
            // 影用レイヤーを先に決めておく。未解決の間に書いたマスクは影ビットを持てず、解決後に読み違えるため
            CharacterShadowLayer.Resolve();
        }

        public override void LateUpdate()
        {
            if (!HasCasterLight())
            {
                DestroyAll();
                return;
            }

            if (_root == null)
            {
                _root = new GameObject(RootName);
                _root.hideFlags = HideFlags.HideAndDontSave;
            }
            HookCamera();

            var frame = Time.frameCount;
            var checkSources = _lastSourceCheckFrame < 0 || frame - _lastSourceCheckFrame >= SourceCheckInterval;
            if (checkSources)
            {
                _lastSourceCheckFrame = frame;
            }

            CollectCharacters(_characters);
            RemoveAbsentEntries();

            foreach (var maid in _characters)
            {
                Entry entry;
                if (!_entries.TryGetValue(maid, out entry))
                {
                    entry = new Entry();
                    _entries.Add(maid, entry);
                }

                // 着替え中はスロットが破棄・再生成されるので、破棄済みの骨やメッシュを指す複製を残さない。
                // 完了 (IsAllProcPropBusy が false に戻る) 後に作り直す
                if (maid.IsAllProcPropBusy)
                {
                    DestroyProxies(entry);
                    entry.isDirty = true;
                    continue;
                }

                if (entry.isDirty || IsStale(entry)
                    || (checkSources && (HasMaterialChanged(entry) || HasSourceChanged(maid, entry))))
                {
                    Rebuild(maid, entry);
                }
                SyncProxies(entry);
            }
        }

        public override void OnPluginDisable()
        {
            DestroyAll();
        }

        public override void OnChangedSceneLevel(Scene scene, LoadSceneMode sceneMode)
        {
            DestroyAll();
        }

        private static bool HasCasterLight()
        {
            var shadowMask = CharacterShadowLayer.mask;
            if (shadowMask == 0)
            {
                return false;
            }

            var characterMask = LightTarget.CharacterMask;
            foreach (var light in StudioLightManager.instance.lights)
            {
                if (light != null && CharacterShadowProxyRules.IsCasterLight(
                    light.enabled && light.gameObject.activeInHierarchy,
                    light.shadows, light.cullingMask, characterMask, shadowMask))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>影を落とすキャラ (メイドと男)。非アクティブのキャラ (呼んでいない男など) は含めない</summary>
        private static void CollectCharacters(List<Maid> results)
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
                AddIfPresent(results, characterMgr.GetMaid(i));
            }
            for (var i = 0; i < characterMgr.GetManCount(); i++)
            {
                AddIfPresent(results, characterMgr.GetMan(i));
            }
        }

        private static void AddIfPresent(List<Maid> results, Maid maid)
        {
            if (maid != null && maid.body0 != null && maid.gameObject.activeInHierarchy)
            {
                results.Add(maid);
            }
        }

        private void RemoveAbsentEntries()
        {
            _absentCharacters.Clear();
            foreach (var maid in _entries.Keys)
            {
                if (maid == null || !_characters.Contains(maid))
                {
                    _absentCharacters.Add(maid);
                }
            }

            foreach (var maid in _absentCharacters)
            {
                DestroyProxies(_entries[maid]);
                _entries.Remove(maid);
            }
        }

        /// <summary>複製元の破棄とメッシュの差し替えを毎フレーム拾う</summary>
        private static bool IsStale(Entry entry)
        {
            foreach (var proxy in entry.proxies)
            {
                if (proxy.source == null || proxy.renderer == null)
                {
                    return true;
                }

                var mesh = proxy.sourceSkinned != null
                    ? proxy.sourceSkinned.sharedMesh
                    : proxy.sourceFilter != null ? proxy.sourceFilter.sharedMesh : null;
                if (mesh != proxy.mesh)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// マテリアルの差し替え (MaterialShaderManager のシェーダー変更を含む) を拾う。
        /// sharedMaterials は呼ぶたびに配列を確保するので、毎フレームではなく複製元の顔ぶれの確認と同じ間隔で比べる
        /// </summary>
        private static bool HasMaterialChanged(Entry entry)
        {
            foreach (var proxy in entry.proxies)
            {
                if (!SameMaterials(proxy.source.sharedMaterials, proxy.materials))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool SameMaterials(Material[] a, Material[] b)
        {
            if (a.Length != b.Length)
            {
                return false;
            }
            for (var i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }
            return true;
        }

        private bool HasSourceChanged(Maid maid, Entry entry)
        {
            CollectSources(maid, _sources);
            if (_sources.Count != entry.proxies.Count)
            {
                return true;
            }
            for (var i = 0; i < _sources.Count; i++)
            {
                if (_sources[i] != entry.proxies[i].source)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 複製元の Renderer。非アクティブの部位も含める (表示の切り替えは描画ごとの判定で追従する)。
        /// MeshRenderer も、見つかった場合は Transform を毎フレーム写して扱う
        /// </summary>
        private static void CollectSources(Maid maid, List<Renderer> results)
        {
            results.Clear();
            var characterMask = LightTarget.CharacterMask;
            foreach (var renderer in maid.gameObject.GetComponentsInChildren<Renderer>(true))
            {
                if (!CharacterShadowProxyRules.ShouldProxy(
                    renderer.gameObject.layer, renderer.shadowCastingMode, characterMask))
                {
                    continue;
                }
                if (renderer is SkinnedMeshRenderer
                    || (renderer is MeshRenderer && renderer.GetComponent<MeshFilter>() != null))
                {
                    results.Add(renderer);
                }
            }
        }

        private void Rebuild(Maid maid, Entry entry)
        {
            DestroyProxies(entry);
            CollectSources(maid, _sources);
            foreach (var source in _sources)
            {
                entry.proxies.Add(CreateProxy(source));
            }
            entry.isDirty = false;
        }

        private Proxy CreateProxy(Renderer source)
        {
            var go = new GameObject(source.name);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.layer = CharacterShadowLayer.layer;
            go.transform.SetParent(_root.transform, false);

            var proxy = new Proxy { source = source };
            var skinned = source as SkinnedMeshRenderer;
            if (skinned != null)
            {
                var proxySkinned = go.AddComponent<SkinnedMeshRenderer>();
                proxySkinned.sharedMesh = skinned.sharedMesh;
                proxySkinned.bones = skinned.bones;
                // メイドの rootBone は空 (描画範囲の基準が自身の Transform)。複製で空のままだと基準が複製のルートになってずれるため、
                // 元の基準の Transform を指定し、localBounds を写す
                proxySkinned.rootBone = skinned.rootBone != null ? skinned.rootBone : skinned.transform;
                proxySkinned.localBounds = skinned.localBounds;
                proxySkinned.quality = skinned.quality;
                proxySkinned.updateWhenOffscreen = skinned.updateWhenOffscreen;

                proxy.sourceSkinned = skinned;
                proxy.proxySkinned = proxySkinned;
                proxy.mesh = skinned.sharedMesh;
                proxy.blendShapeCount = skinned.sharedMesh != null ? skinned.sharedMesh.blendShapeCount : 0;
                proxy.renderer = proxySkinned;
            }
            else
            {
                var filter = source.GetComponent<MeshFilter>();
                go.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                proxy.sourceFilter = filter;
                proxy.mesh = filter.sharedMesh;
                proxy.renderer = go.AddComponent<MeshRenderer>();
            }

            // アルファテストの切り抜きを影に反映させるため、マテリアルも共有する
            proxy.materials = source.sharedMaterials;
            proxy.renderer.sharedMaterials = proxy.materials;
            proxy.renderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            proxy.renderer.receiveShadows = false;
            return proxy;
        }

        private static void SyncProxies(Entry entry)
        {
            foreach (var proxy in entry.proxies)
            {
                if (proxy.proxySkinned != null)
                {
                    proxy.proxySkinned.localBounds = proxy.sourceSkinned.localBounds;
                    // ブレンドシェイプを使うメッシュに備えて重みを写す
                    for (var i = 0; i < proxy.blendShapeCount; i++)
                    {
                        proxy.proxySkinned.SetBlendShapeWeight(i, proxy.sourceSkinned.GetBlendShapeWeight(i));
                    }
                }
                else
                {
                    var sourceTransform = proxy.source.transform;
                    var proxyTransform = proxy.renderer.transform;
                    proxyTransform.position = sourceTransform.position;
                    proxyTransform.rotation = sourceTransform.rotation;
                    // 複製のルートは原点・等倍なので、ワールドの拡縮をそのまま写せる
                    proxyTransform.localScale = sourceTransform.lossyScale;
                }
            }
        }

        private void HookCamera()
        {
            if (_isCameraHooked)
            {
                return;
            }
            Camera.onPreCull += OnPreCull;
            Camera.onPostRender += OnPostRender;
            _isCameraHooked = true;
        }

        private void UnhookCamera()
        {
            if (!_isCameraHooked)
            {
                return;
            }
            Camera.onPreCull -= OnPreCull;
            Camera.onPostRender -= OnPostRender;
            _isCameraHooked = false;
            RestoreHidden();
        }

        /// <summary>
        /// 元がこのカメラに描かれないなら、このカメラの描画中は複製の影も止める。影の計算はカメラごとに行われる。
        /// Camera.onPreCull はカメラのコンポーネントの OnPreCull (ViewCullingFilter・PostEffects の MaidHideEffect) より
        /// 後に呼ばれるため、それらが無効にした Renderer や書き換えた cullingMask をここで読める
        /// </summary>
        private void OnPreCull(Camera camera)
        {
            // 前のカメラの onPostRender が来なかったときの保険
            RestoreHidden();

            var cameraMask = camera.cullingMask;
            if (!CharacterShadowProxyRules.ShouldUseCamera(cameraMask, CharacterShadowLayer.mask))
            {
                return;
            }

            foreach (var entry in _entries.Values)
            {
                foreach (var proxy in entry.proxies)
                {
                    var renderer = proxy.renderer;
                    if (renderer == null || !renderer.enabled)
                    {
                        continue;
                    }

                    // 判定を Unity 非依存に保つため、破棄済み (Unity の null) の判定を畳んでから渡す
                    var source = proxy.source;
                    var isAlive = source != null;
                    if (CharacterShadowProxyRules.ShouldHideForCamera(
                        isAlive,
                        isAlive && source.enabled,
                        isAlive && source.gameObject.activeInHierarchy,
                        isAlive ? source.gameObject.layer : 0,
                        cameraMask))
                    {
                        renderer.enabled = false;
                        _hiddenForCamera.Add(renderer);
                    }
                }
            }
        }

        private void OnPostRender(Camera camera)
        {
            RestoreHidden();
        }

        private void RestoreHidden()
        {
            for (var i = 0; i < _hiddenForCamera.Count; i++)
            {
                var renderer = _hiddenForCamera[i];
                if (renderer != null)
                {
                    renderer.enabled = true;
                }
            }
            _hiddenForCamera.Clear();
        }

        private static void DestroyProxies(Entry entry)
        {
            foreach (var proxy in entry.proxies)
            {
                if (proxy.renderer != null)
                {
                    Object.Destroy(proxy.renderer.gameObject);
                }
            }
            entry.proxies.Clear();
        }

        /// <summary>条件を満たすライトが無くなったら複製をすべて捨て、この機能を使わない間のコストを無くす</summary>
        private void DestroyAll()
        {
            UnhookCamera();
            foreach (var entry in _entries.Values)
            {
                DestroyProxies(entry);
            }
            _entries.Clear();
            if (_root != null)
            {
                Object.Destroy(_root);
                _root = null;
            }
        }
    }
}
