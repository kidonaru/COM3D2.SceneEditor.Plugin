using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace COM3D2.MotionTimelineEditor.Plugin
{
    [ExecuteInEditMode]
    public class StageLight : MonoBehaviour
    {
        [SerializeField]
        public StageLightController _controller;
        public StageLightController controller
        {
            get
            {
                return _controller;
            }
            set
            {
                if (_controller == value) return;
                _controller = value;
                UpdateName();
            }
        }

        [SerializeField]
        private int _index = 0;
        public int index
        {
            get
            {
                return _index;
            }
            set
            {
                if (_index == value) return;
                _index = value;
                UpdateName();
            }
        }

        public string displayName;

        public Light spotLight;

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
        private Color _color = Color.white;
        public Color color
        {
            get
            {
                return _color;
            }
            set
            {
                if (_color == value) return;
                _color = value;
                _requestedMaterialUpdate = true;
            }
        }

        [SerializeField]
        [Range(1f, 179f)]
        private float _spotAngle = 10.0f;
        public float spotAngle
        {
            get
            {
                return _spotAngle;
            }
            set
            {
                if (_spotAngle == value) return;
                _spotAngle = value;
                _requestedMeshUpdate = true;
                _requestedMaterialUpdate = true;
            }
        }

        [SerializeField]
        private float _spotRange = 10.0f;
        public float spotRange
        {
            get
            {
                return _spotRange;
            }
            set
            {
                if (_spotRange == value) return;
                _spotRange = value;
                _requestedMeshUpdate = true;
                _requestedMaterialUpdate = true;
            }
        }

        [SerializeField]
        [Range(0.1f, 1.0f)]
        private float _rangeMultiplier = 0.8f;
        public float rangeMultiplier
        {
            get
            {
                return _rangeMultiplier;
            }
            set
            {
                if (_rangeMultiplier == value) return;
                _rangeMultiplier = value;
                _requestedMeshUpdate = true;
                _requestedMaterialUpdate = true;
            }
        }

        [SerializeField]
        [Range(0.1f, 1f)]
        private float _falloffExp = 0.5f;
        public float falloffExp
        {
            get
            {
                return _falloffExp;
            }
            set
            {
                if (_falloffExp == value) return;
                _falloffExp = value;
                _requestedMaterialUpdate = true;
            }
        }

        [SerializeField]
        [Range(0f, 1f)]
        private float _noiseStrength = 0.2f;
        public float noiseStrength
        {
            get
            {
                return _noiseStrength;
            }
            set
            {
                if (_noiseStrength == value) return;
                _noiseStrength = value;
                _requestedMaterialUpdate = true;
            }
        }

        [SerializeField]
        [Range(1f, 10f)]
        private float _noiseScale = 5f;
        public float noiseScale
        {
            get
            {
                return _noiseScale;
            }
            set
            {
                if (_noiseScale == value) return;
                _noiseScale = value;
                _requestedMaterialUpdate = true;
            }
        }

        [SerializeField]
        [Range(0f, 1f)]
        private float _coreRadius = 0.2f;
        public float coreRadius
        {
            get
            {
                return _coreRadius;
            }
            set
            {
                if (_coreRadius == value) return;
                _coreRadius = value;
                _requestedMaterialUpdate = true;
            }
        }

        [SerializeField]
        private float _offsetRange = 0.5f;
        public float offsetRange
        {
            get
            {
                return _offsetRange;
            }
            set
            {
                if (_offsetRange == value) return;
                _offsetRange = value;
                _requestedMeshUpdate = true;
                _requestedMaterialUpdate = true;
            }
        }

        [SerializeField]
        [Range(1f, 64)]
        private int _segmentAngle = 10;
        public int segmentAngle
        {
            get
            {
                return _segmentAngle;
            }
            set
            {
                if (_segmentAngle == value) return;
                _segmentAngle = value;
                _requestedMeshUpdate = true;
            }
        }

        [SerializeField]
        [Range(1, 64)]
        private int _segmentRange = 10;
        public int segmentRange
        {
            get
            {
                return _segmentRange;
            }
            set
            {
                if (_segmentRange == value) return;
                _segmentRange = value;
                _requestedMeshUpdate = true;
            }
        }

        [SerializeField]
        private bool _zTest = true;
        public bool zTest
        {
            get
            {
                return _zTest;
            }
            set
            {
                if (_zTest == value) return;
                _zTest = value;
                _requestedMaterialUpdate = true;
            }
        }

        // 描画非対応の COM3D2 (2.0) では _meshObject が生成されないため、
        // 表示状態はメッシュの活性ではなくフィールドで保持する (キーフレームの記録値がずれないように)
        [SerializeField]
        private bool _visible = true;
        public bool visible
        {
            get
            {
                return _visible;
            }
            set
            {
                _visible = value;
                if (_meshObject != null && _meshObject.activeSelf != value)
                {
                    _meshObject.SetActive(value);
                }
            }
        }

        public int groupIndex
        {
            get
            {
                if (_controller != null)
                {
                    return _controller.groupIndex;
                }
                return 0;
            }
        }

        public static Vector3 DefaultPosition = new Vector3(0f, 10f, 0f);
        public static Vector3 DefaultEulerAngles = new Vector3(90f, 0f, 0f);

        private bool _requestedMeshUpdate = false;
        private bool _requestedMaterialUpdate = false;

        // COM3D2 (2.0) ビルドでは Initialize が生成しないため null のまま
        private GameObject _meshObject = null;
        private MeshFilter _meshFilter = null;
        private MeshRenderer _meshRenderer = null;

#if COM3D2
        private static TimelineBundleManager bundleManager => TimelineBundleManager.instance;
#endif

        void OnEnable()
        {
            Initialize();
#if UNITY_EDITOR
            EditorApplication.update += OnEditorUpdate;
#endif
        }

        void OnDisable()
        {
#if UNITY_EDITOR
            EditorApplication.update -= OnEditorUpdate;
#endif
        }

        void Reset()
        {
            Initialize();
        }

        void OnValidate()
        {
            if (_meshFilter != null)
            {
                UpdateMesh();
                UpdateMaterial();
            }
        }

        void OnEditorUpdate()
        {
            if (!Application.isPlaying)
            {
                LateUpdate();
            }
        }

        void LateUpdate()
        {
            // _meshFilter が null なのは描画非対応ビルド。更新するものが無い
            if (!visible || _meshFilter == null)
            {
                return;
            }

            if (spotLight != null)
            {
                if (spotLight.color != color)
                {
                    spotLight.color = color;
                }
                if (spotLight.spotAngle != spotAngle)
                {
                    spotLight.spotAngle = spotAngle;
                }
                if (spotLight.range != spotRange)
                {
                    spotLight.range = spotRange;
                }
            }

            if (_requestedMeshUpdate)
            {
                UpdateMesh();
                _requestedMeshUpdate = false;
            }

            if (_requestedMaterialUpdate)
            {
                UpdateMaterial();
                _requestedMaterialUpdate = false;
            }

            UpdateTransform();
        }

        public void CopyFrom(StageLight other)
        {
            if (other == null) return;
            position = other.position;
            eulerAngles = other.eulerAngles;
            color = other.color;
            spotAngle = other.spotAngle;
            spotRange = other.spotRange;
            rangeMultiplier = other.rangeMultiplier;
            falloffExp = other.falloffExp;
            noiseStrength = other.noiseStrength;
            noiseScale = other.noiseScale;
            coreRadius = other.coreRadius;
            offsetRange = other.offsetRange;
            segmentAngle = other.segmentAngle;
            segmentRange = other.segmentRange;
        }

#if COM3D2 && !COM3D25
        // COM3D2 (2.0) ビルドは 5.6 製バンドルのままで新シェーダを持たないため描画非対応
        private static bool _unsupportedWarned = false;
#endif

        public void Initialize()
        {
            if (spotLight != null && spotLight.type != LightType.Spot)
            {
                Debug.LogError("このコンポーネントはスポットライトにのみ使用できます");
            }

#if COM3D2 && !COM3D25
            if (!_unsupportedWarned)
            {
                _unsupportedWarned = true;
                Debug.LogWarning("COM3D2 ではステージライトの描画は未対応です");
            }
            transform.localPosition = _position;
            transform.localEulerAngles = _eulerAngles;
            UpdateName();
            return;
#else
            var meshTransform = transform.Find("Mesh");
            _meshObject = meshTransform != null ? meshTransform.gameObject : null;
            if (_meshObject == null)
            {
                _meshObject = new GameObject("Mesh");
                _meshObject.transform.parent = transform;
                _meshObject.transform.localPosition = Vector3.zero;
                _meshObject.transform.localRotation = Quaternion.identity;
            }
            _meshObject.SetActive(_visible);

            _meshFilter = _meshObject.GetComponent<MeshFilter>();
            if (_meshFilter == null)
            {
                _meshFilter = _meshObject.AddComponent<MeshFilter>();
            }

            _meshRenderer = _meshObject.GetComponent<MeshRenderer>();
            if (_meshRenderer == null)
            {
                _meshRenderer = _meshObject.AddComponent<MeshRenderer>();
                _meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

#if COM3D2
                var material = bundleManager.LoadMaterial("StageLight");
#else
                var material = new Material(Shader.Find("MTE/StageLight"));
                material.SetTexture("_MainTex", Resources.Load<Texture2D>("noise_texture"));
#endif
                _meshRenderer.material = material;
            }

            transform.localPosition = _position;
            transform.localEulerAngles = _eulerAngles;

            UpdateName();
            UpdateMesh();
            UpdateMaterial();
            UpdateTransform();
#endif
        }

        private const int MinConeSegments = 3;

        private Vector3[] _vertices = null;
        private int[] _triangles = null;

        // 先端リング (z=offsetRange) と底面リング (z=range) を側面で結び、両端をキャップで閉じた円錐。
        // 法線が外向きになる巻き順にする (シェーダは Cull Front で裏面を描く)
        void UpdateMesh()
        {
            Mesh mesh = _meshFilter.mesh;
            mesh.Clear();

            float tanHalf = Mathf.Tan(spotAngle * 0.5f * Mathf.Deg2Rad);
            float range = CalculateEffectiveRange();
            float zNear = Mathf.Clamp(offsetRange, 0f, range);
            // UI は 1 から選べるが、円錐として成立するには 3 分割が要る
            int segments = Mathf.Max(segmentAngle, MinConeSegments);

            int ringCount = segments + 1;
            int verticesCount = ringCount * 2 + 2;
            if (_vertices == null || _vertices.Length != verticesCount)
            {
                _vertices = new Vector3[verticesCount];
            }

            for (int a = 0; a < ringCount; a++)
            {
                float theta = a * Mathf.PI * 2f / segments;
                float cos = Mathf.Cos(theta);
                float sin = Mathf.Sin(theta);
                _vertices[a] = new Vector3(cos * zNear * tanHalf, sin * zNear * tanHalf, zNear);
                _vertices[ringCount + a] = new Vector3(cos * range * tanHalf, sin * range * tanHalf, range);
            }
            int nearCenter = ringCount * 2;
            int farCenter = nearCenter + 1;
            _vertices[nearCenter] = new Vector3(0f, 0f, zNear);
            _vertices[farCenter] = new Vector3(0f, 0f, range);

            // 側面 2 三角形 + 先端キャップ 1 + 底面キャップ 1 = 12 インデックス / セグメント
            int trianglesCount = segments * 12;
            if (_triangles == null || _triangles.Length != trianglesCount)
            {
                _triangles = new int[trianglesCount];
            }

            int t = 0;
            for (int a = 0; a < segments; a++)
            {
                int n0 = a;
                int n1 = a + 1;
                int f0 = ringCount + a;
                int f1 = ringCount + a + 1;

                _triangles[t++] = n0; _triangles[t++] = n1; _triangles[t++] = f0;
                _triangles[t++] = n1; _triangles[t++] = f1; _triangles[t++] = f0;
                _triangles[t++] = nearCenter; _triangles[t++] = n1; _triangles[t++] = n0;
                _triangles[t++] = farCenter; _triangles[t++] = f0; _triangles[t++] = f1;
            }

            mesh.vertices = _vertices;
            mesh.triangles = _triangles;
            mesh.RecalculateBounds();
        }

        void UpdateTransform()
        {
            if (spotLight != null)
            {
                if (transform.position != spotLight.transform.position)
                {
                    spotLight.transform.position = transform.position;
                }
                if (transform.rotation != spotLight.transform.rotation)
                {
                    spotLight.transform.rotation = transform.rotation;
                }
            }
        }

        private void UpdateMaterial()
        {
            if (_meshRenderer != null && _meshRenderer.material != null)
            {
                var material = _meshRenderer.material;
                float range = CalculateEffectiveRange();
                material.SetFloat(Uniforms._SpotRange, range);
                material.SetFloat(Uniforms._OffsetRange, Mathf.Clamp(offsetRange, 0f, range));
                material.SetColor(Uniforms._Color, color);
                material.SetColor(Uniforms._SubColor, color);
                material.SetFloat(Uniforms._FalloffExp, falloffExp);
                material.SetFloat(Uniforms._NoiseStrength, noiseStrength);
                material.SetFloat(Uniforms._NoiseScaleInv, 1f / noiseScale);
                material.SetFloat(Uniforms._CoreRadius, coreRadius);
                material.SetFloat(Uniforms._TanHalfAngle, Mathf.Tan(spotAngle * 0.5f * Mathf.Deg2Rad));
                // 積分は経路長に比例するため距離で正規化する (係数は実機で調整済み)
                material.SetFloat(Uniforms._Density, DensityScale / Mathf.Max(range, 0.01f));
                material.SetFloat(Uniforms._DepthClip, zTest ? 1f : 0f);
            }
        }

        // 実機で従来の板ポリ描画と明るさが近くなる係数
        private const float DensityScale = 12f;

        private static class Uniforms
        {
            internal static readonly int _SpotRange = Shader.PropertyToID("_SpotRange");
            internal static readonly int _OffsetRange = Shader.PropertyToID("_OffsetRange");
            internal static readonly int _Color = Shader.PropertyToID("_Color");
            internal static readonly int _SubColor = Shader.PropertyToID("_SubColor");
            internal static readonly int _FalloffExp = Shader.PropertyToID("_FalloffExp");
            internal static readonly int _NoiseStrength = Shader.PropertyToID("_NoiseStrength");
            internal static readonly int _NoiseScaleInv = Shader.PropertyToID("_NoiseScaleInv");
            internal static readonly int _CoreRadius = Shader.PropertyToID("_CoreRadius");
            internal static readonly int _TanHalfAngle = Shader.PropertyToID("_TanHalfAngle");
            internal static readonly int _Density = Shader.PropertyToID("_Density");
            internal static readonly int _DepthClip = Shader.PropertyToID("_DepthClip");
        }

        private void UpdateName()
        {
            var suffix = " (" + groupIndex + ", " + index + ")";
            name = "StageLight" + suffix;
            displayName = "ステージライト" + suffix;
        }

        private float CalculateEffectiveRange()
        {
            return spotRange * rangeMultiplier;
        }

    }
}