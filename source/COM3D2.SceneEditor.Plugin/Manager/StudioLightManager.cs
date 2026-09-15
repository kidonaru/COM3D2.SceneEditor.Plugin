using System.Collections.Generic;
using System.Globalization;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 追加ライト（ポイント/スポット/平行）の実体を管理するマネージャー。
    /// フォトモードと違い背景配下には置かず専用ルート配下に生成する
    /// （背景切替でライトが消えるのを避けるため）。
    /// メインライトは実体を持たず GameMain.Instance.MainLight を直接操作する
    /// </summary>
    public class StudioLightManager : ManagerBase
    {
        private const string ROOT_NAME = "SceneEditorLightRoot";

        // 生成時の既定値（フォトモードのポイントライト初期値に合わせる）。
        // UI 側のスライダー既定値と食い違わないよう LightWindow からも参照する
        public static readonly Vector3 DefaultPosition = new Vector3(0f, 1.9f, 0.4f);
        public const float DefaultIntensity = 0.95f;
        public const float DefaultRange = 10f;
        public const float DefaultSpotAngle = 50f;

        private GameObject _root = null;
        private readonly List<Light> _lights = new List<Light>();

        /// <summary>追加ライトの表示名の接頭辞</summary>
        private const string LIGHT_NAME_PREFIX = "追加ライト ";

        /// <summary>追加ライトの一覧。破棄済み要素は Update で除去される</summary>
        public List<Light> lights => _lights;

        /// <summary>メインライト。シーンによっては取得できず null になる</summary>
        public LightMain mainLight
        {
            get
            {
                var gameMain = GameMain.Instance;
                return gameMain != null ? gameMain.MainLight : null;
            }
        }

        /// <summary>
        /// メインライトの Light コンポーネント。
        /// LightMain のアクセサではなく Light を直接使うのは、
        /// LightTimelineLayer が書き込む対象と同じものを読み書きするため
        /// </summary>
        public Light mainLightComponent
        {
            get
            {
                var main = mainLight;
                return main != null ? main.GetComponent<Light>() : null;
            }
        }

        /// <summary>
        /// プラグインが触る前のメインライトの値。
        /// LightTimelineLayer が書き込む項目をすべて持つ
        /// </summary>
        private struct MainLightSnapshot
        {
            public Vector3 localPosition;
            public Quaternion localRotation;
            public Color color;
            public float range;
            public float intensity;
            public float spotAngle;
            public float shadowStrength;
            public float shadowBias;
            public int cullingMask;
            public bool enabled;
        }

        private bool _hasMainLightSnapshot = false;
        private MainLightSnapshot _mainLightSnapshot;

        private static StudioLightManager _instance = null;
        public static StudioLightManager instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new StudioLightManager();
                }
                return _instance;
            }
        }

        private StudioLightManager()
        {
        }

        /// <summary>追加ライトを 1 灯生成する（既定はポイントライト）</summary>
        public Light AddLight()
        {
            if (_root == null)
            {
                _root = new GameObject(ROOT_NAME);
            }

            var go = new GameObject(LIGHT_NAME_PREFIX + GetNextLightNumber());
            go.transform.SetParent(_root.transform, false);
            go.transform.position = DefaultPosition;

            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.intensity = DefaultIntensity;
            light.range = DefaultRange;
            light.spotAngle = DefaultSpotAngle;
            light.color = Color.white;

            _lights.Add(light);
            return light;
        }

        /// <summary>次に生成するライトの表示名の番号。現存するライトが使っていない最小の番号を返す</summary>
        private int GetNextLightNumber()
        {
            var usedNumbers = new HashSet<int>();

            foreach (var light in _lights)
            {
                if (light == null)
                {
                    continue;
                }

                var name = light.gameObject.name;
                if (!name.StartsWith(LIGHT_NAME_PREFIX, System.StringComparison.Ordinal))
                {
                    continue;
                }

                int number;
                if (int.TryParse(
                    name.Substring(LIGHT_NAME_PREFIX.Length),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out number))
                {
                    usedNumbers.Add(number);
                }
            }

            var result = 1;
            while (usedNumbers.Contains(result))
            {
                result++;
            }
            return result;
        }

        public void RemoveLight(Light light)
        {
            // null（破棄済み含む）を Remove へ渡すと破棄済みの別要素に一致しうるため弾く。
            // 破棄済み要素の掃除は Update に任せる
            if (light == null)
            {
                return;
            }

            _lights.Remove(light);
            Object.Destroy(light.gameObject);
        }

        /// <summary>
        /// 追加ライトとして扱える種別か。
        /// Area / Rectangle 等はリアルタイムライトとして機能しないため除外する
        /// </summary>
        public static bool IsSupportedType(LightType type)
        {
            return type == LightType.Point
                || type == LightType.Spot
                || type == LightType.Directional;
        }

        /// <summary>ポイント/スポット/平行の種別切替。それ以外の型は受け付けない</summary>
        public void SetLightType(Light light, LightType type)
        {
            if (light == null || !IsSupportedType(type))
            {
                return;
            }
            light.type = type;
        }

        /// <summary>
        /// メインライトの値を控える。既に控えていれば何もしない。
        /// 有効化時ではなく Update から呼ぶのは、SceneEditorPlugin.OnPluginEnable が
        /// managerRegistry.OnPluginEnable より先に OnLoad (タイムラインの値を適用する) を
        /// 呼ぶため、有効化の時点では既にプラグインの値に染まっているからである
        /// </summary>
        public void CaptureMainLightSnapshot()
        {
            if (_hasMainLightSnapshot)
            {
                return;
            }

            var light = mainLightComponent;
            if (light == null)
            {
                return;
            }

            var transform = light.transform;
            _mainLightSnapshot = new MainLightSnapshot
            {
                localPosition = transform.localPosition,
                localRotation = transform.localRotation,
                color = light.color,
                range = light.range,
                intensity = light.intensity,
                spotAngle = light.spotAngle,
                shadowStrength = light.shadowStrength,
                shadowBias = light.shadowBias,
                cullingMask = light.cullingMask,
                enabled = light.enabled,
            };
            _hasMainLightSnapshot = true;
        }

        /// <summary>控えた値へ戻す。控える前なら何もしない</summary>
        public void RestoreMainLightSnapshot()
        {
            if (!_hasMainLightSnapshot)
            {
                return;
            }

            var light = mainLightComponent;
            if (light == null)
            {
                return;
            }

            var transform = light.transform;
            transform.localPosition = _mainLightSnapshot.localPosition;
            transform.localRotation = _mainLightSnapshot.localRotation;
            light.color = _mainLightSnapshot.color;
            light.range = _mainLightSnapshot.range;
            light.intensity = _mainLightSnapshot.intensity;
            light.spotAngle = _mainLightSnapshot.spotAngle;
            light.shadowStrength = _mainLightSnapshot.shadowStrength;
            light.shadowBias = _mainLightSnapshot.shadowBias;
            light.cullingMask = _mainLightSnapshot.cullingMask;
            light.enabled = _mainLightSnapshot.enabled;
        }

        public void ClearAll()
        {
            foreach (var light in _lights)
            {
                if (light != null)
                {
                    Object.Destroy(light.gameObject);
                }
            }
            _lights.Clear();
        }

        /// <summary>ライトとルートごと生成物を破棄する</summary>
        private void ReleaseAll()
        {
            ClearAll();

            if (_root != null)
            {
                Object.Destroy(_root);
            }
            _root = null;
        }

        public override void Update()
        {
            // メインライトはシーンによっては後から現れるので、取れるまで毎フレーム試す
            CaptureMainLightSnapshot();

            // 外部要因（シーン側の破棄等）で消えたライトをリストへ残さない
            _lights.RemoveAll(light => light == null);
        }

        public override void OnChangedSceneLevel(Scene scene, LoadSceneMode sceneMode)
        {
            // Additive ロードでは旧シーンの GameObject が破棄されず、参照を捨てるだけだと
            // ライトが残留したまま次の AddLight で二重生成される。明示的に破棄する
            ReleaseAll();

            // シーンが変わるとメインライトも別の実体になるので控え直す
            _hasMainLightSnapshot = false;
        }

        public override void OnPluginDisable()
        {
            ReleaseAll();
        }
    }
}
