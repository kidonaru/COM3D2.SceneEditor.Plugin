using System.Collections.Generic;
using System.IO;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace COM3D2.SceneEditor.Plugin
{
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
        public float brightness = 1f;
        public Color color = Color.white;
        public int renderQueue;
        public bool visible = true;

        public string name => rootObject != null ? rootObject.name : "";
        public Transform transform => rootObject != null ? rootObject.transform : null;
    }

    /// <summary>
    /// PNG 配置オブジェクトの実体を管理するマネージャー。
    /// 背景配下には置かず専用ルート配下に生成する (背景切替で消えるのを避けるため)。
    /// 描画はマイオブジェクトと同じ Unlit シェーダーを使い、
    /// 透過画像は ZWrite を切って描画順の破綻を抑える
    /// </summary>
    public class PngPlacementManager : ManagerBase
    {
        private const string ROOT_NAME = "SceneEditorPngRoot";

        public const string SOURCE_CONFIG = "config";
        public const string SOURCE_PHOTO = "photo";

        /// <summary>マイオブジェクトと同じゲーム組込みシェーダー</summary>
        private const string SHADER_NAME = "CM3D2/Unlit_Texture_Photo_MyObject";
        private const string FALLBACK_SHADER_NAME = "Unlit/Transparent";

        private const int ZWRITE_OFF = 0;
        private const int ZWRITE_ON = 1;

        /// <summary>生成時の既定位置。メイドの初期位置と被らない手前に出す</summary>
        public static readonly Vector3 DefaultPosition = new Vector3(0f, 1f, 0f);
        public const int DefaultRenderQueue = 3000;

        private GameObject _root = null;
        private readonly List<PngObjectData> _pngObjects = new List<PngObjectData>();
        private readonly Dictionary<string, Texture2D> _textureCache =
            new Dictionary<string, Texture2D>();
        /// <summary>透過判定の結果。全ピクセル走査を画像ごとに 1 度で済ませる</summary>
        private readonly Dictionary<string, bool> _alphaCache = new Dictionary<string, bool>();
        private Shader _shader = null;

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
        private static string ResolveImagePath(string dir, string relativePath)
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

            var shader = GetShader();
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
            ApplyAspectScale(quad.transform, texture);

            var material = new Material(shader);
            material.mainTexture = texture;
            material.renderQueue = DefaultRenderQueue;
            // 透過画像は ZWrite を切る (マイオブジェクトと同じ判定基準)
            material.SetInt("_ZWrite",
                HasAlpha(source, relativePath, texture) ? ZWRITE_OFF : ZWRITE_ON);
            // 板の裏からも見えるよう両面描画にする
            material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            quad.GetComponent<MeshRenderer>().material = material;

            var data = new PngObjectData
            {
                rootObject = rootGo,
                quadObject = quad,
                material = material,
                source = source,
                relativePath = relativePath,
                renderQueue = DefaultRenderQueue,
            };
            _pngObjects.Add(data);
            return data;
        }

        /// <summary>
        /// 長辺を 1m に揃えて短辺をアスペクト比で縮める。
        /// ユーザースケールは親 (root) 側なのでここへは触らせない
        /// </summary>
        private static void ApplyAspectScale(Transform quad, Texture2D texture)
        {
            float w = texture.width;
            float h = texture.height;
            var ratio = Mathf.Min(w, h) / Mathf.Max(w, h);
            quad.localScale = w >= h
                ? new Vector3(1f, ratio, 1f)
                : new Vector3(ratio, 1f, 1f);
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

        private Shader GetShader()
        {
            if (_shader == null)
            {
                _shader = Shader.Find(SHADER_NAME);
                if (_shader == null)
                {
                    MTEUtils.LogWarning("シェーダーが見つからないため代替を使います: {0}",
                        SHADER_NAME);
                    _shader = Shader.Find(FALLBACK_SHADER_NAME);
                }
            }
            return _shader;
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

            if (data.material != null)
            {
                Object.Destroy(data.material);
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
            if (data.material != null)
            {
                var c = new Color(
                    color.r * brightness, color.g * brightness, color.b * brightness, color.a);
                data.material.SetColor("_Color", c);
            }
        }

        public void SetRenderQueue(PngObjectData data, int renderQueue)
        {
            data.renderQueue = renderQueue;
            if (data.material != null)
            {
                data.material.renderQueue = renderQueue;
            }
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
                if (data.material != null)
                {
                    Object.Destroy(data.material);
                }
                if (data.rootObject != null)
                {
                    Object.Destroy(data.rootObject);
                }
            }
            _pngObjects.Clear();
            _nextNumber = 1;
        }

        /// <summary>配置物・ルート・テクスチャキャッシュをすべて破棄する</summary>
        private void ReleaseAll()
        {
            ClearAll();

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
        }

        public override void Update()
        {
            // 外部要因 (シーン側の破棄等) で消えた配置物をリストへ残さない
            _pngObjects.RemoveAll(data => data.rootObject == null);
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
                if (!data.billboard || data.rootObject == null)
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
