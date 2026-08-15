using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 追加ライト（ポイント/スポット）の実体を管理するマネージャー。
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

        /// <summary>次に生成するライトの表示名の番号。削除しても戻さず名前の重複を避ける</summary>
        private int _nextLightNumber = 1;

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

            var go = new GameObject("追加ライト " + _nextLightNumber++);
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

        /// <summary>ポイント⇔スポットの種別切替。それ以外の型は受け付けない</summary>
        public void SetLightType(Light light, LightType type)
        {
            if (light == null || (type != LightType.Point && type != LightType.Spot))
            {
                return;
            }
            light.type = type;
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
            _nextLightNumber = 1;
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
            // 外部要因（シーン側の破棄等）で消えたライトをリストへ残さない
            _lights.RemoveAll(light => light == null);
        }

        public override void OnChangedSceneLevel(Scene scene, LoadSceneMode sceneMode)
        {
            // Additive ロードでは旧シーンの GameObject が破棄されず、参照を捨てるだけだと
            // ライトが残留したまま次の AddLight で二重生成される。明示的に破棄する
            ReleaseAll();
        }

        public override void OnPluginDisable()
        {
            ReleaseAll();
        }
    }
}
