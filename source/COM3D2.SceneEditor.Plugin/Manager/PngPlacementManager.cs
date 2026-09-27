using System.Collections.Generic;
using System.IO;
using COM3D2.MotionTimelineEditor;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>PNG 配置の表示タイプ。値はシーンプリセットとタイムライン XML に保存される</summary>
    public enum PngDisplayType
    {
        /// <summary>板 (Quad) として置く</summary>
        Board = 0,
        /// <summary>板の奥の面へ画像を投影する</summary>
        Decal = 1,
    }

    /// <summary>
    /// PNG 配置 1 枚分。root（ユーザー操作用 Transform）の子に
    /// アスペクト補正済みの Quad をぶら下げる 2 階層構成
    /// </summary>
    public class PngObjectData
    {
        /// <summary>ユーザーが位置・回転・スケールを操作する親</summary>
        public GameObject rootObject;
        /// <summary>アスペクト補正を持つ Quad（root の子）。補正値へ触らないこと</summary>
        public GameObject quadObject;
        public Material material;

        /// <summary>画像の出所 (PngPlacementManager.SOURCE_*)</summary>
        public string source;
        /// <summary>出所ディレクトリからの相対パス。プリセット保存と再ロードに使う</summary>
        public string relativePath;

        public bool billboard = true;
        public float brightness = PngPlacementManager.DefaultBrightness;
        public Color color = Color.white;
        public int renderQueue;
        public PngBlendMode blendMode = PngBlendMode.Normal;
        /// <summary>彩度。0 でグレースケール、1 で元の色。タイムラインではキーの値</summary>
        public float saturation = PngPlacementManager.DefaultSaturation;
        public bool visible = true;

        /// <summary>画像の縦横 (長辺 1)。板の Quad とデカールの投影箱の大きさに使う</summary>
        public Vector2 aspect = Vector2.one;

        public PngDisplayType displayType = PngDisplayType.Board;
        public float decalFadeAngle = PngDecalProjection.DefaultFadeAngle;
        public bool decalProjectOnMaids;

        /// <summary>デカール投影の子 (PngDecal)。初めてデカールにしたときに生成する</summary>
        public GameObject decalObject;
        public Projector projector;
        public Material decalMaterial;

        /// <summary>
        /// 実際にデカールで表示しているか。シェーダーが無くデカールを作れないときは
        /// 表示タイプがデカールでも板で見せるため、見た目に合わせた分岐はこちらを使う
        /// </summary>
        public bool isDecalShown => decalObject != null && decalObject.activeSelf;

        public string name => rootObject != null ? rootObject.name : "";
        public Transform transform => rootObject != null ? rootObject.transform : null;
    }

    /// <summary>
    /// PNG 配置オブジェクトの実体を管理するマネージャー。
    /// 背景配下には置かず専用ルート配下に生成する (背景切替で消えるのを避けるため)。
    /// 描画は SE 独自の無照明シェーダー (読めなければマイオブジェクトと同じ組込みシェーダー) を使い、
    /// 透過画像は ZWrite を切って描画順の破綻を抑える。
    /// デカール表示では子の Projector が板の奥の面へ画像を投影する
    /// </summary>
    public class PngPlacementManager : ManagerBase
    {
        private const string ROOT_NAME = "SceneEditorPngRoot";

        public const string SOURCE_CONFIG = "config";
        public const string SOURCE_PHOTO = "photo";

        /// <summary>SE の板シェーダーが読めないときの代替 (マイオブジェクトと同じゲーム組込みシェーダー)</summary>
        private const string BUILTIN_BOARD_SHADER_NAME = "CM3D2/Unlit_Texture_Photo_MyObject";
        private const string LAST_RESORT_SHADER_NAME = "Unlit/Transparent";

        private const int ZWRITE_OFF = 0;
        private const int ZWRITE_ON = 1;

        /// <summary>生成時の既定位置。メイドの初期位置と被らない手前に出す</summary>
        public static readonly Vector3 DefaultPosition = new Vector3(0f, 1f, 0f);
        public const int DefaultRenderQueue = 3000;

        public const float DefaultBrightness = 1f;
        public const float MinBrightness = 0f;
        public const float MaxBrightness = 2f;
        public const float BrightnessStep = 0.01f;

        public const float DefaultSaturation = 1f;
        public const float MinSaturation = 0f;
        public const float MaxSaturation = 2f;
        public const float SaturationStep = 0.01f;

        private GameObject _root = null;
        private readonly List<PngObjectData> _pngObjects = new List<PngObjectData>();
        private readonly Dictionary<string, Texture2D> _textureCache =
            new Dictionary<string, Texture2D>();
        /// <summary>透過判定の結果。全ピクセル走査を画像ごとに 1 度で済ませる</summary>
        private readonly Dictionary<string, bool> _alphaCache = new Dictionary<string, bool>();
        private Shader _builtinBoardShader = null;

        // se_bundle 内のシェーダー (Assets/Shaders/<名前>.shader)
        private const string BOARD_SHADER_NAME = "PngBoard";
        private const string BOARD_OVERLAY_SHADER_NAME = "PngBoardOverlay";
        private const string DECAL_SHADER_NAME = "Decal";
        private const string DECAL_OVERLAY_SHADER_NAME = "DecalOverlay";
        private const string DECAL_OBJECT_NAME = "PngDecal";

        /// <summary>「メイドにも投影」が OFF のとき Projector に無視させるレイヤー名</summary>
        private static readonly string[] MaidLayerNames = { "Charactor", "Face", "Man" };

        private static readonly int DecalMatrixId = Shader.PropertyToID("_DecalMatrix");
        private static readonly int DecalNormalId = Shader.PropertyToID("_DecalNormal");
        private static readonly int FadeCosMinId = Shader.PropertyToID("_FadeCosMin");
        private static readonly int FadeCosMaxId = Shader.PropertyToID("_FadeCosMax");
        private static readonly int BlendModeId = Shader.PropertyToID("_BlendMode");
        private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
        private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int SaturationId = Shader.PropertyToID("_Saturation");

        /// <summary>
        /// se_bundle から読んだシェーダー。読めなかった名前は null を入れ、
        /// 失敗を毎回ログへ出さないよう再試行しない
        /// </summary>
        private readonly Dictionary<string, Shader> _bundleShaders = new Dictionary<string, Shader>();
        /// <summary>デカールを最後に更新したフレーム。カメラごとの onPreCull で重ねて更新しないため</summary>
        private int _decalUpdatedFrame = -1;
        private bool _isPreCullHooked = false;
        /// <summary>SceneView カメラの描画中だけ止めている Projector。描画後に戻す</summary>
        private readonly List<Projector> _hiddenProjectors = new List<Projector>();

        /// <summary>
        /// タイムラインの実体データへ保存する設定 (表示順・表示タイプ・ブレンド方式・デカール設定) の変更回数。
        /// タイムライン側は前回値と比べて保存データへ書き戻す。
        /// 色・明るさ・彩度・表示はキー側の値で再生中に毎フレーム変わりうるため数えない
        /// </summary>
        public int entitySettingsRevision { get; private set; }

        /// <summary>次に生成する表示名の番号。削除しても戻さず名前の重複を避ける</summary>
        private int _nextNumber = 1;

        /// <summary>配置一覧。破棄済み要素は Update で除去される</summary>
        public List<PngObjectData> pngObjects => _pngObjects;

        private static PngPlacementManager _instance = null;
        public static PngPlacementManager instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new PngPlacementManager();
                }
                return _instance;
            }
        }

        private PngPlacementManager()
        {
        }

        /// <summary>画像の出所ディレクトリ。photo はセーブデータ側なので都度解決する</summary>
        public static string GetSourceDirectory(string source)
        {
            if (source == SOURCE_PHOTO)
            {
                var gameMain = GameMain.Instance;
                if (gameMain == null || gameMain.SerializeStorageManager == null)
                {
                    return null;
                }
                return Path.Combine(
                    gameMain.SerializeStorageManager.StoreDirectoryPath,
                    "PhotoModeData\\Texture");
            }
            return Path.Combine(PluginUtils.UserDataPath, "PngPlacement");
        }

        /// <summary>キャッシュのキー。出所が違えば同じ相対パスでも別画像になる</summary>
        private static string GetCacheKey(string source, string relativePath)
        {
            return source + ":" + relativePath;
        }

        /// <summary>
        /// 出所ディレクトリ配下の実ファイルパスを解決する。
        /// relativePath はプリセット XML 由来の外部入力なので、絶対パス指定や
        /// ".." による出所外への脱出を弾く。範囲外なら null
        /// </summary>
        public static string ResolveImagePath(string dir, string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath) || Path.IsPathRooted(relativePath))
            {
                return null;
            }

            var rootPath = Path.GetFullPath(dir);
            if (!rootPath.EndsWith("\\") && !rootPath.EndsWith("/"))
            {
                rootPath += Path.DirectorySeparatorChar;
            }

            var fullPath = Path.GetFullPath(Path.Combine(rootPath, relativePath));
            if (!fullPath.StartsWith(rootPath, System.StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            return fullPath;
        }

        /// <summary>
        /// 画像をロードする。同一ファイルの再配置でテクスチャを重複させないよう
        /// キャッシュする。読めない場合は null
        /// </summary>
        public Texture2D GetTexture(string source, string relativePath)
        {
            var key = GetCacheKey(source, relativePath);
            Texture2D cached;
            if (_textureCache.TryGetValue(key, out cached) && cached != null)
            {
                return cached;
            }

            var dir = GetSourceDirectory(source);
            if (dir == null)
            {
                return null;
            }

            var path = ResolveImagePath(dir, relativePath);
            if (path == null)
            {
                MTEUtils.LogWarning("画像のパスが不正です: {0}", relativePath);
                return null;
            }
            if (!File.Exists(path))
            {
                MTEUtils.LogWarning("画像が見つかりません: {0}", path);
                return null;
            }

            // サイズは LoadImage が実画像で上書きするためダミー
            var texture = new Texture2D(2, 2, TextureFormat.ARGB32, false);
            if (!texture.LoadImage(File.ReadAllBytes(path)))
            {
                MTEUtils.LogWarning("画像を読み込めません: {0}", path);
                Object.Destroy(texture);
                return null;
            }
            texture.wrapMode = TextureWrapMode.Clamp;

            _textureCache[key] = texture;
            return texture;
        }

        /// <summary>
        /// PNG を 1 枚配置する。テクスチャが読めない場合とシェーダーが無い場合は null
        /// </summary>
        public PngObjectData AddPng(string source, string relativePath)
        {
            var texture = GetTexture(source, relativePath);
            if (texture == null)
            {
                return null;
            }

            var shader = GetBoardShader();
            if (shader == null)
            {
                MTEUtils.LogWarning("シェーダーが無いため配置できません: {0}", relativePath);
                return null;
            }

            if (_root == null)
            {
                _root = new GameObject(ROOT_NAME);
            }

            var fileName = Path.GetFileNameWithoutExtension(relativePath);
            var rootGo = new GameObject(fileName + " " + _nextNumber++);
            rootGo.transform.SetParent(_root.transform, false);
            rootGo.transform.position = DefaultPosition;

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "PngQuad";
            quad.transform.SetParent(rootGo.transform, false);
            // Quad の表面は -Z 向きだが、ビルボードの LookAt は +Z を対象へ向ける。
            // 180 度回して表面を root の +Z 側（＝カメラ側）に合わせる
            quad.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            var aspect = PngDecalProjection.GetAspect(texture.width, texture.height);
            quad.transform.localScale = new Vector3(aspect.x, aspect.y, 1f);

            var material = new Material(shader);
            material.mainTexture = texture;
            material.renderQueue = DefaultRenderQueue;
            // 透過画像は ZWrite を切る (マイオブジェクトと同じ判定基準)
            material.SetInt("_ZWrite",
                HasAlpha(source, relativePath, texture) ? ZWRITE_OFF : ZWRITE_ON);
            // 板の裏からも見えるよう両面描画にする
            material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            material.SetFloat(SaturationId, DefaultSaturation);
            quad.GetComponent<MeshRenderer>().material = material;

            var data = new PngObjectData
            {
                rootObject = rootGo,
                quadObject = quad,
                material = material,
                source = source,
                relativePath = relativePath,
                renderQueue = DefaultRenderQueue,
                aspect = aspect,
            };
            ApplyBlendMode(data);
            _pngObjects.Add(data);
            return data;
        }

        /// <summary>
        /// 透過画像かどうか。全ピクセル走査は大きな画像だと重いため、
        /// 同じ画像の再配置では走査結果を使い回す
        /// </summary>
        private bool HasAlpha(string source, string relativePath, Texture2D texture)
        {
            var key = GetCacheKey(source, relativePath);
            bool cached;
            if (_alphaCache.TryGetValue(key, out cached))
            {
                return cached;
            }

            var hasAlpha = HasAlphaPixel(texture);
            _alphaCache[key] = hasAlpha;
            return hasAlpha;
        }

        /// <summary>アルファ値 1 未満のピクセルが 1 つでもあるか</summary>
        private static bool HasAlphaPixel(Texture2D texture)
        {
            var pixels = texture.GetPixels32();
            for (var i = 0; i < pixels.Length; i++)
            {
                if (pixels[i].a != byte.MaxValue)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 板のシェーダー。SE/PngBoard が読めなければゲーム組込みシェーダーで描く
        /// (このときブレンド方式と彩度は効かないが、設定値は保持して保存する)
        /// </summary>
        private Shader GetBoardShader()
        {
            var shader = GetBundleShader(BOARD_SHADER_NAME);
            if (shader != null)
            {
                return shader;
            }

            if (_builtinBoardShader == null)
            {
                _builtinBoardShader = Shader.Find(BUILTIN_BOARD_SHADER_NAME);
                if (_builtinBoardShader == null)
                {
                    MTEUtils.LogWarning("シェーダーが見つからないため代替を使います: {0}",
                        BUILTIN_BOARD_SHADER_NAME);
                    _builtinBoardShader = Shader.Find(LAST_RESORT_SHADER_NAME);
                }
            }
            return _builtinBoardShader;
        }

        /// <summary>
        /// 表示タイプに合わせて板とデカールの一方だけを有効にする。
        /// デカールを作れない (シェーダーが無い等) ときは設定値を残したまま板で見せる
        /// </summary>
        private void ApplyDisplayType(PngObjectData data)
        {
            var isDecal = data.displayType == PngDisplayType.Decal;
            if (isDecal && data.decalObject == null && !CreateDecal(data))
            {
                isDecal = false;
            }

            if (data.quadObject != null)
            {
                data.quadObject.SetActive(!isDecal);
            }
            if (data.decalObject != null)
            {
                data.decalObject.SetActive(isDecal);
            }
        }

        private bool CreateDecal(PngObjectData data)
        {
            var shader = GetBundleShader(DECAL_SHADER_NAME);
            if (shader == null || data.rootObject == null)
            {
                return false;
            }

            var go = new GameObject(DECAL_OBJECT_NAME);
            go.transform.SetParent(data.rootObject.transform, false);
            // 板の Quad と同じく 180 度回し、投影方向を root の -Z (板の表から裏) へ向ける
            go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            var material = new Material(shader);
            material.mainTexture = data.material != null ? data.material.mainTexture : null;

            var projector = go.AddComponent<Projector>();
            projector.orthographic = true;
            projector.material = material;

            data.decalObject = go;
            data.projector = projector;
            data.decalMaterial = material;

            material.SetColor(ColorId, GetTintColor(data));
            material.SetFloat(SaturationId, data.saturation);
            ApplyBlendMode(data);
            ApplyDecalFadeAngle(data);
            ApplyDecalIgnoreLayers(data);
            UpdateDecal(data);
            HookPreCull();
            return true;
        }

        /// <summary>se_bundle のシェーダー。読めない・今のグラフィックス API で描けないときは null</summary>
        private Shader GetBundleShader(string shaderName)
        {
            Shader shader;
            if (!_bundleShaders.TryGetValue(shaderName, out shader))
            {
                shader = MTEP.TimelineBundleManager.instance.LoadShader(shaderName);
                if (shader != null && !shader.isSupported)
                {
                    MTEUtils.LogWarning("シェーダーがこの環境で使えないため代替で描きます: {0}", shaderName);
                    shader = null;
                }
                _bundleShaders[shaderName] = shader;
            }
            return shader;
        }

        /// <summary>
        /// ブレンド方式を板とデカールのマテリアルへ適用する。
        /// オーバーレイは GrabPass を持つ専用シェーダーへ差し替える
        /// (GrabPass はシェーダーに書くと常に走るため、通常のシェーダーには入れていない)
        /// </summary>
        private void ApplyBlendMode(PngObjectData data)
        {
            // ゲーム組込みシェーダーで描いている板 (SE シェーダーが読めない) には適用しない
            var boardShader = GetBundleShader(BOARD_SHADER_NAME);
            if (data.material != null && boardShader != null)
            {
                ApplyBlendTo(data.material, boardShader, BOARD_OVERLAY_SHADER_NAME, data.blendMode);
                // シェーダーの差し替えで表示順がシェーダー既定へ戻らないよう、毎回設定し直す
                data.material.renderQueue = data.renderQueue;
            }

            if (data.decalMaterial != null)
            {
                ApplyBlendTo(data.decalMaterial, GetBundleShader(DECAL_SHADER_NAME),
                    DECAL_OVERLAY_SHADER_NAME, data.blendMode);
            }
        }

        private void ApplyBlendTo(
            Material material, Shader baseShader, string overlayShaderName, PngBlendMode mode)
        {
            var overlay = PngBlendModes.UsesGrab(mode) ? GetBundleShader(overlayShaderName) : null;
            var renderMode = PngBlendModes.ResolveRenderMode(mode, overlay != null);
            SetBlendMaterial(material, overlay ?? baseShader, renderMode);
        }

        private static void SetBlendMaterial(Material material, Shader shader, PngBlendMode mode)
        {
            // 同じシェーダーの再代入でもキーワード等の再構築が走るため、変わるときだけ差し替える
            if (shader != null && material.shader != shader)
            {
                material.shader = shader;
            }

            UnityEngine.Rendering.BlendMode src;
            UnityEngine.Rendering.BlendMode dst;
            PngBlendModes.GetBlendFactors(mode, out src, out dst);
            material.SetInt(SrcBlendId, (int)src);
            material.SetInt(DstBlendId, (int)dst);
            material.SetFloat(BlendModeId, (int)mode);
        }

        private static void ApplyDecalFadeAngle(PngObjectData data)
        {
            if (data.decalMaterial == null)
            {
                return;
            }
            var range = PngDecalProjection.GetFadeCosRange(data.decalFadeAngle);
            data.decalMaterial.SetFloat(FadeCosMinId, range.x);
            data.decalMaterial.SetFloat(FadeCosMaxId, range.y);
        }

        private static void ApplyDecalIgnoreLayers(PngObjectData data)
        {
            if (data.projector == null)
            {
                return;
            }
            data.projector.ignoreLayers = data.decalProjectOnMaids ? 0 : GetMaidLayerMask();
        }

        private static int GetMaidLayerMask()
        {
            var mask = 0;
            foreach (var name in MaidLayerNames)
            {
                var layer = LayerMask.NameToLayer(name);
                if (layer >= 0)
                {
                    mask |= 1 << layer;
                }
            }
            return mask;
        }

        /// <summary>
        /// 投影箱を root の Transform へ合わせる。
        /// 専用ルート (SceneEditorPngRoot) は拡縮しないため root の localScale をワールドの拡縮として扱う
        /// </summary>
        private static void UpdateDecal(PngObjectData data)
        {
            var root = data.rootObject.transform;
            var projector = data.projector;
            var isDegenerate = PngDecalProjection.IsDegenerateScale(root.localScale);
            projector.enabled = !isDegenerate;
            if (isDegenerate)
            {
                return;
            }

            var frame = PngDecalProjection.ComputeFrame(data.aspect, root.localScale);

            var decalTransform = data.decalObject.transform;
            decalTransform.localPosition = frame.localPosition;
            decalTransform.localScale = frame.localScale;

            projector.orthographicSize = frame.orthographicSize;
            projector.aspectRatio = frame.aspectRatio;
            projector.nearClipPlane = frame.nearClipPlane;
            projector.farClipPlane = frame.farClipPlane;

            data.decalMaterial.SetMatrix(DecalMatrixId,
                PngDecalProjection.ComputeDecalMatrix(root.worldToLocalMatrix, data.aspect));
            data.decalMaterial.SetVector(DecalNormalId, root.forward);
        }

        private void HookPreCull()
        {
            if (_isPreCullHooked)
            {
                return;
            }
            Camera.onPreCull += OnPreCullDecals;
            Camera.onPostRender += OnPostRenderDecals;
            _isPreCullHooked = true;
        }

        private void UnhookPreCull()
        {
            if (!_isPreCullHooked)
            {
                return;
            }
            Camera.onPreCull -= OnPreCullDecals;
            Camera.onPostRender -= OnPostRenderDecals;
            _isPreCullHooked = false;
            RestoreHiddenProjectors();
        }

        /// <summary>
        /// デカールを Transform へ追従させる。ギズモ・タイムライン再生・Undo によるそのフレームの変更が
        /// 済んだ後、カメラのカリングより前に呼ばれるため投影が 1 フレーム遅れない
        /// </summary>
        private void OnPreCullDecals(Camera camera)
        {
            // Transform は同じフレーム内で変わらないため、2 台目以降のカメラでは更新しない
            if (_decalUpdatedFrame != Time.frameCount)
            {
                _decalUpdatedFrame = Time.frameCount;
                foreach (var data in _pngObjects)
                {
                    if (!data.isDecalShown
                        || data.rootObject == null
                        || !data.rootObject.activeInHierarchy)
                    {
                        continue;
                    }
                    UpdateDecal(data);
                }
            }

            // 更新で Projector が有効へ戻るため、止めるのは更新の後
            if (camera == SceneViewManager.instance.sceneCamera)
            {
                HideOverlayProjectors();
            }
        }

        /// <summary>
        /// オーバーレイのデカールを SceneView カメラの描画中だけ止める。
        /// 下地を取る名前付き GrabPass は全カメラ通算で 1 フレーム 1 回で、先に描く SceneView が取ると
        /// ゲーム画面のデカールが SceneView の下地で合成されてしまうため
        /// </summary>
        private void HideOverlayProjectors()
        {
            foreach (var data in _pngObjects)
            {
                if (data.isDecalShown
                    && data.projector != null
                    && data.projector.enabled
                    && PngBlendModes.UsesGrab(data.blendMode))
                {
                    data.projector.enabled = false;
                    _hiddenProjectors.Add(data.projector);
                }
            }
        }

        private void OnPostRenderDecals(Camera camera)
        {
            RestoreHiddenProjectors();
        }

        private void RestoreHiddenProjectors()
        {
            foreach (var projector in _hiddenProjectors)
            {
                if (projector != null)
                {
                    projector.enabled = true;
                }
            }
            _hiddenProjectors.Clear();
        }

        /// <summary>
        /// 配置物を 1 枚破棄する。破棄済みの root を持つ要素も一覧からは必ず外す
        /// (外し損ねるとプリセット適用の削除ループが進まなくなる)
        /// </summary>
        public void RemovePng(PngObjectData data)
        {
            if (data == null)
            {
                return;
            }

            _pngObjects.Remove(data);
            DestroyResources(data);
        }

        /// <summary>配置物 1 枚が所有する Unity オブジェクトを破棄する</summary>
        private static void DestroyResources(PngObjectData data)
        {
            if (data.material != null)
            {
                Object.Destroy(data.material);
            }
            if (data.decalMaterial != null)
            {
                Object.Destroy(data.decalMaterial);
            }
            if (data.rootObject != null)
            {
                Object.Destroy(data.rootObject);
            }
        }

        /// <summary>ルート GameObject から配置物を引く。PNG 配置でなければ null</summary>
        public PngObjectData FindByRoot(GameObject rootObject)
        {
            if (rootObject == null)
            {
                return null;
            }

            foreach (var data in _pngObjects)
            {
                if (data.rootObject == rootObject)
                {
                    return data;
                }
            }
            return null;
        }

        /// <summary>
        /// go 自身か祖先が配置物のルートなら、その配置物を返す。どれにも属さなければ null。
        /// SceneView クリックでは板の実体 (子の PngQuad) がヒットするため、祖先も含めて判定する
        /// </summary>
        public PngObjectData FindByDescendant(GameObject go)
        {
            for (var transform = go != null ? go.transform : null;
                transform != null;
                transform = transform.parent)
            {
                var data = FindByRoot(transform.gameObject);
                if (data != null)
                {
                    return data;
                }
            }
            return null;
        }

        /// <summary>一覧内の位置を移動する。プリセットの差分適用で順序を合わせるために使う</summary>
        public void MovePng(PngObjectData data, int index)
        {
            if (data == null || !_pngObjects.Remove(data))
            {
                return;
            }
            _pngObjects.Insert(Mathf.Clamp(index, 0, _pngObjects.Count), data);
        }

        public void SetBillboard(PngObjectData data, bool billboard)
        {
            data.billboard = billboard;
        }

        public void SetColor(PngObjectData data, Color color, float brightness)
        {
            data.color = color;
            data.brightness = brightness;
            var tint = GetTintColor(data);
            if (data.material != null)
            {
                data.material.SetColor(ColorId, tint);
            }
            if (data.decalMaterial != null)
            {
                data.decalMaterial.SetColor(ColorId, tint);
            }
        }

        public void SetSaturation(PngObjectData data, float saturation)
        {
            data.saturation = Mathf.Clamp(saturation, MinSaturation, MaxSaturation);
            if (data.material != null)
            {
                data.material.SetFloat(SaturationId, data.saturation);
            }
            if (data.decalMaterial != null)
            {
                data.decalMaterial.SetFloat(SaturationId, data.saturation);
            }
        }

        public void SetRenderQueue(PngObjectData data, int renderQueue)
        {
            if (data.renderQueue != renderQueue)
            {
                entitySettingsRevision++;
            }
            data.renderQueue = renderQueue;
            if (data.material != null)
            {
                data.material.renderQueue = renderQueue;
            }
        }

        public void SetDisplayType(PngObjectData data, PngDisplayType displayType)
        {
            if (data.displayType != displayType)
            {
                entitySettingsRevision++;
            }
            data.displayType = displayType;
            ApplyDisplayType(data);
        }

        public void SetBlendMode(PngObjectData data, PngBlendMode blendMode)
        {
            if (data.blendMode != blendMode)
            {
                entitySettingsRevision++;
            }
            data.blendMode = blendMode;
            ApplyBlendMode(data);
        }

        public void SetDecalFadeAngle(PngObjectData data, float fadeAngle)
        {
            var clamped = PngDecalProjection.ClampFadeAngle(fadeAngle);
            if (!Mathf.Approximately(data.decalFadeAngle, clamped))
            {
                entitySettingsRevision++;
            }
            data.decalFadeAngle = clamped;
            ApplyDecalFadeAngle(data);
        }

        public void SetDecalProjectOnMaids(PngObjectData data, bool projectOnMaids)
        {
            if (data.decalProjectOnMaids != projectOnMaids)
            {
                entitySettingsRevision++;
            }
            data.decalProjectOnMaids = projectOnMaids;
            ApplyDecalIgnoreLayers(data);
        }

        /// <summary>色 × 明るさ。α は明るさの影響を受けない不透明度</summary>
        private static Color GetTintColor(PngObjectData data)
        {
            var c = data.color;
            var b = data.brightness;
            return new Color(c.r * b, c.g * b, c.b * b, c.a);
        }

        public void SetVisible(PngObjectData data, bool visible)
        {
            data.visible = visible;
            if (data.rootObject != null)
            {
                data.rootObject.SetActive(visible);
            }
        }

        public void ClearAll()
        {
            foreach (var data in _pngObjects)
            {
                DestroyResources(data);
            }
            _pngObjects.Clear();
            _nextNumber = 1;
        }

        /// <summary>配置物・ルート・テクスチャキャッシュをすべて破棄する</summary>
        private void ReleaseAll()
        {
            ClearAll();
            UnhookPreCull();

            if (_root != null)
            {
                Object.Destroy(_root);
            }
            _root = null;

            foreach (var texture in _textureCache.Values)
            {
                if (texture != null)
                {
                    Object.Destroy(texture);
                }
            }
            _textureCache.Clear();
            _alphaCache.Clear();
            // シーン切替後にバンドルが読み直されても追従するよう、読んだシェーダーも捨てる
            _bundleShaders.Clear();
        }

        public override void Update()
        {
            // 外部要因 (シーン側の破棄等) で消えた配置物をリストへ残さない。
            // root と一緒に消えないマテリアルはここで破棄する
            _pngObjects.RemoveAll(data =>
            {
                if (data.rootObject != null)
                {
                    return false;
                }
                DestroyResources(data);
                return true;
            });
        }

        public override void LateUpdate()
        {
            // カメラ確定後に向きを合わせる。Y 軸固定のビルボード
            var camera = Camera.main;
            if (camera == null)
            {
                return;
            }

            foreach (var data in _pngObjects)
            {
                if (!data.billboard
                    || data.isDecalShown
                    || data.rootObject == null)
                {
                    continue;
                }
                var pos = camera.transform.position;
                pos.y = data.rootObject.transform.position.y;
                data.rootObject.transform.LookAt(pos);
            }
        }

        public override void OnChangedSceneLevel(Scene scene, LoadSceneMode sceneMode)
        {
            // Additive ロードでは旧シーンの GameObject が破棄されないため明示的に破棄する
            ReleaseAll();
        }

        public override void OnPluginDisable()
        {
            ReleaseAll();
        }
    }
}
