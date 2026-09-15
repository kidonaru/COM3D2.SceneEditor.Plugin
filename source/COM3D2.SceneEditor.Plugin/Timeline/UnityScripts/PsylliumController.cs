using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace COM3D2.MotionTimelineEditor.Plugin
{
    [ExecuteInEditMode]
    public class PsylliumController : MonoBehaviour
    {
        const float BPM_TO_TIME = 1 / 60f;

        [SerializeField]
        private int _groupIndex = 0;
        public int groupIndex
        {
            get
            {
                return _groupIndex;
            }
            set
            {
                if (_groupIndex == value) return;
                _groupIndex = value;
                UpdateName();
            }
        }

        public string displayName;

        public PsylliumBarConfig barConfig = new PsylliumBarConfig();
        public PsylliumHandConfig handConfig = new PsylliumHandConfig();
        public List<PsylliumArea> areas = new List<PsylliumArea>();
        public List<PsylliumPattern> patterns = new List<PsylliumPattern>();
        public Material[] materials;
        public Mesh[] meshes;
        public float time;
        public PsylliumRefreshKind refreshKind;

        /// <summary>
        /// 旧コード互換用。true で全再構築、false で全種別クリア（部分クリアは不可）。
        /// 新規コードは RequestRefresh(kind) で必要な種別だけ積むこと
        /// </summary>
        public bool refreshRequired
        {
            get { return refreshKind != PsylliumRefreshKind.None; }
            set { refreshKind = value ? PsylliumRefreshKind.All : PsylliumRefreshKind.None; }
        }

        /// <summary>必要な再構築種別を積む。次の ManualUpdate でまとめて実行される</summary>
        public void RequestRefresh(PsylliumRefreshKind kind)
        {
            refreshKind |= kind;
        }

        [SerializeField]
        private Vector3 _position = DefaultPosition;
        public Vector3 position
        {
            get
            {
                return _position;
            }
            set
            {
                if (_position == value) return;
                _position = value;
                transform.localPosition = value;
            }
        }

        [SerializeField]
        private Vector3 _eulerAngles = DefaultEulerAngles;
        public Vector3 eulerAngles
        {
            get
            {
                return _eulerAngles;
            }
            set
            {
                if (_eulerAngles == value) return;
                _eulerAngles = value;
                transform.localEulerAngles = value;
            }
        }

        [SerializeField]
        private Vector3 _scale = Vector3.one;
        public Vector3 scale
        {
            get
            {
                return _scale;
            }
            set
            {
                if (_scale == value) return;
                _scale = value;
                transform.localScale = value;
            }
        }

        public bool visible
        {
            get
            {
                return gameObject.activeSelf;
            }
            set
            {
                if (gameObject.activeSelf == value) return;
                gameObject.SetActive(value);
            }
        }

        public static Vector3 DefaultPosition = new Vector3(0f, 0f, 0f);
        public static Vector3 DefaultEulerAngles = new Vector3(0f, 0f, 0f);

#if COM3D2
        private static TimelineBundleManager bundleManager => TimelineBundleManager.instance;

        private Config config => ConfigManager.instance.config;
#endif

        void OnEnable()
        {
            Initialize();
#if UNITY_EDITOR
            EditorApplication.update += OnEditorUpdate;
#endif
        }

        void Reset()
        {
            Initialize();
        }

        private Material CreateMaterial(string materialName)
        {
#if COM3D2
            var material = bundleManager.LoadMaterial(materialName);
#else
            var material = new Material(Shader.Find("MTE/" + materialName));
            material.SetTexture("_MainTex", Resources.Load<Texture2D>("psyllium"));
#endif
            return material;
        }

        public void Initialize()
        {
            time = 0.0f;
            areas = GetComponentsInChildren<PsylliumArea>().ToList();
            areas.Sort((a, b) => a.index - b.index);

            materials = new Material[2];
            materials[0] = CreateMaterial("Psyllium");
            materials[1] = CreateMaterial("PsylliumAdd");

            meshes = new Mesh[2];
            meshes[0] = new Mesh();
            meshes[1] = new Mesh();

            UpdateName();
            UpdateMaterials();
            UpdateMeshs();
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            refreshRequired = true;
        }

        void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
        }

        private void OnEditorUpdate()
        {
            if (Application.isPlaying)
            {
                ManualUpdate(time + Time.deltaTime);
            }
            else
            {
                ManualUpdate((float) EditorApplication.timeSinceStartup);
            }
        }
#endif

        public void Refresh()
        {
            Refresh(PsylliumRefreshKind.All);
        }

        public void Refresh(PsylliumRefreshKind kind)
        {
            if ((kind & PsylliumRefreshKind.Placement) != 0)
            {
                foreach (var area in areas)
                {
                    area.Refresh();
                }
            }

            if ((kind & PsylliumRefreshKind.Material) != 0)
            {
                UpdateMaterials();
            }

            if ((kind & PsylliumRefreshKind.Mesh) != 0)
            {
                UpdateMeshs();
            }

            refreshKind = PsylliumRefreshKind.None;
        }

        public void UpdateName()
        {
            var suffix = " (" + groupIndex + ")";
            name = "PsylliumController" + suffix;
            displayName = "コントローラー" + suffix;

            barConfig.UpdateName(groupIndex);
            handConfig.UpdateName(groupIndex);

            foreach (var area in areas)
            {
                area.UpdateName();
            }

            foreach (var pattern in patterns)
            {
                pattern.UpdateName();
            }
        }

        public void UpdateMaterials()
        {
            foreach (var material in materials)
            {
                material.SetColor(Uniforms._Color1a, barConfig.color1a);
                material.SetColor(Uniforms._Color1b, barConfig.color1b);
                material.SetColor(Uniforms._Color1c, barConfig.color1c);
                material.SetColor(Uniforms._Color2a, barConfig.color2a);
                material.SetColor(Uniforms._Color2b, barConfig.color2b);
                material.SetColor(Uniforms._Color2c, barConfig.color2c);
                material.SetFloat(Uniforms._CutoffAlpha, barConfig.cutoffAlpha);
            }
        }

        private static class Uniforms
        {
            internal static readonly int _Color1a = Shader.PropertyToID("_Color1a");
            internal static readonly int _Color1b = Shader.PropertyToID("_Color1b");
            internal static readonly int _Color1c = Shader.PropertyToID("_Color1c");
            internal static readonly int _Color2a = Shader.PropertyToID("_Color2a");
            internal static readonly int _Color2b = Shader.PropertyToID("_Color2b");
            internal static readonly int _Color2c = Shader.PropertyToID("_Color2c");
            internal static readonly int _CutoffAlpha = Shader.PropertyToID("_CutoffAlpha");
        }

        // UpdateMesh 用の作業配列。頂点 8 個固定なので使い回す（メインスレッドから逐次呼ぶ前提。
        // Mesh のセッターは配列をコピーするので、書き込み後に再利用してよい）
        private readonly Vector3[] _meshVertices = new Vector3[8];
        private readonly Vector2[] _meshUv = new Vector2[8];
        private readonly Vector2[] _meshUv2 = new Vector2[8];
        private static readonly int[] MeshTriangles = new int[] {
            0, 1, 2,
            1, 3, 2,
            1, 4, 3,
            4, 5, 3,
            4, 6, 5,
            6, 7, 5,
        };

        public void UpdateMeshs()
        {
            UpdateMesh(0);
            UpdateMesh(1);
        }

        public void UpdateMesh(int colorIndex)
        {
            var halfWidth = barConfig.width * 0.5f * barConfig.baseScale;
            var barHeight = barConfig.height * barConfig.baseScale;
            var barRadius = barConfig.radius * barConfig.baseScale;
            var positionY = barConfig.positionY * barConfig.baseScale;
            var barTopThreshold = barConfig.topThreshold;

            var vertices = _meshVertices;
            vertices[0] = new Vector3(-halfWidth, positionY, 0);
            vertices[1] = new Vector3(-halfWidth, positionY, 0);
            vertices[2] = new Vector3( halfWidth, positionY, 0);
            vertices[3] = new Vector3( halfWidth, positionY, 0);
            vertices[4] = new Vector3(-halfWidth, positionY + barHeight, 0);
            vertices[5] = new Vector3( halfWidth, positionY + barHeight, 0);
            vertices[6] = new Vector3(-halfWidth, positionY + barHeight, 0);
            vertices[7] = new Vector3( halfWidth, positionY + barHeight, 0);

            var uv = _meshUv;
            uv[0] = new Vector2(0, 0);
            uv[1] = new Vector2(0, barTopThreshold);
            uv[2] = new Vector2(1, 0);
            uv[3] = new Vector2(1, barTopThreshold);
            uv[4] = new Vector2(0, 1 - barTopThreshold);
            uv[5] = new Vector2(1, 1 - barTopThreshold);
            uv[6] = new Vector2(0, 1);
            uv[7] = new Vector2(1, 1);

            // uv2.y は colorIndex（シェーダー側で色セットの選択に使う）
            var uv2 = _meshUv2;
            uv2[0] = new Vector2(-barRadius, colorIndex);
            uv2[1] = new Vector2(0, colorIndex);
            uv2[2] = new Vector2(-barRadius, colorIndex);
            uv2[3] = new Vector2(0, colorIndex);
            uv2[4] = new Vector2(0, colorIndex);
            uv2[5] = new Vector2(0, colorIndex);
            uv2[6] = new Vector2(barRadius, colorIndex);
            uv2[7] = new Vector2(barRadius, colorIndex);

            var mesh = meshes[colorIndex];
            mesh.Clear();

            mesh.subMeshCount = 2;
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.uv2 = uv2;

            mesh.SetTriangles(MeshTriangles, 0);
            mesh.SetTriangles(MeshTriangles, 1);
        }

        public PsylliumArea AddArea()
        {
            var obj = new GameObject("PsylliumArea");
            obj.transform.SetParent(this.transform, false);

            var area = obj.AddComponent<PsylliumArea>();
            area.index = areas.Count;
            area.Setup(this);

            areas.Add(area);

            // 1つ前のエリア設定を引き継ぐ
            if (areas.Count > 1)
            {
                var prevArea = areas[areas.Count - 2];
                area.CopyFrom(prevArea, false);
            }

            return area;
        }

        public string RemoveAreaLast()
        {
            if (areas.Count == 0) return "";

            var area = areas[areas.Count - 1];
            var areaName = area.name;

            if (Application.isPlaying)
            {
                Destroy(area.gameObject);
            }
            else
            {
                DestroyImmediate(area.gameObject);
            }

            areas.RemoveAt(areas.Count - 1);

            return areaName;
        }

        public PsylliumPattern GetPattern(int index)
        {
            if (patterns.Count == 0) return null;
            if (index < 0 || index > patterns.Count - 1) return null;
            return patterns[index];
        }

        public PsylliumPattern AddPattern()
        {
            var pattern = new PsylliumPattern();
            pattern.index = patterns.Count;
            pattern.Setup(this);

            // 1つ前のパターン設定を引き継ぐ
            if (patterns.Count > 0)
            {
                var prevPattern = patterns[patterns.Count - 1];
                pattern.CopyFrom(prevPattern);
            }

            patterns.Add(pattern);
            refreshRequired = true;

            return pattern;
        }

        public string RemovePatternLast()
        {
            if (patterns.Count == 0) return "";

            var pattern = patterns[patterns.Count - 1];
            var patternName = pattern.patternConfig.name;

            patterns.RemoveAt(patterns.Count - 1);
            refreshRequired = true;

            return patternName;
        }

        public void ManualUpdate(float time)
        {
            if (refreshKind != PsylliumRefreshKind.None)
            {
                // 席の再配置は area.refreshRequired 経由で area.ManualUpdate に委譲する
                // （ここで area.Refresh() を呼ぶと直後の area.ManualUpdate と位置計算が二重に走る）
                if ((refreshKind & PsylliumRefreshKind.Placement) != 0)
                {
                    foreach (var area in areas)
                    {
                        area.refreshRequired = true;
                    }
                }

                if ((refreshKind & PsylliumRefreshKind.Material) != 0)
                {
                    UpdateMaterials();
                }

                if ((refreshKind & PsylliumRefreshKind.Mesh) != 0)
                {
                    UpdateMeshs();
                }

                refreshKind = PsylliumRefreshKind.None;
            }

            this.time = time;

            foreach (var pattern in patterns)
            {
                pattern.ManualUpdate();
            }

            foreach (var area in areas)
            {
                area.ManualUpdate();
            }
        }

        public void CopyFrom(PsylliumController src)
        {
            groupIndex = src.groupIndex;
            position = src.position;
            eulerAngles = src.eulerAngles;
            visible = src.visible;

            barConfig.CopyFrom(src.barConfig);
            handConfig.CopyFrom(src.handConfig);
        }
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(PsylliumController))]
    public class PsylliumControllerEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var controller = (PsylliumController) target;

            if (GUILayout.Button("エリア追加"))
            {
                controller.AddArea();
            }

            if (GUILayout.Button("エリア削除"))
            {
                controller.RemoveAreaLast();
            }

            if (GUILayout.Button("パターン追加"))
            {
                controller.AddPattern();
            }

            if (GUILayout.Button("パターン削除"))
            {
                controller.RemovePatternLast();
            }
        }
    }
#endif
}