using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using SE = COM3D2.SceneEditor.Plugin;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    public class ModelMaterial
    {
        public enum ColorPropertyType
        {
            _Color,
            _ShadowColor,
            _RimColor,
            _OutlineColor,

            // NPR
            _EmissionColor,
            _MatcapColor,
            _MatcapMaskColor,
            _RimLightColor,
        }

        public enum ValuePropertyType
        {
            _Shininess,
            _OutlineWidth,
            _RimPower,
            _RimShift,

            // NPR
            _NormalValue,
            _ParallaxValue,
            _MatcapValue,
            _MatcapMaskValue,
            _EmissionValue,
            _EmissionHDRExposure,
            _EmissionPower,
            _RimLightValue,
            _RimLightPower,
            _MetallicValue,
            _SmoothnessValue,
            _OcclusionValue,
        }

        public static readonly List<ColorPropertyType> ColorPropertyTypes =
            MTEUtils.GetEnumValues<ColorPropertyType>().ToList();

        public static readonly List<int> ColorPropertyNameIds =
            ColorPropertyTypes.Select(x => Shader.PropertyToID(x.ToString())).ToList();

        public static readonly List<ValuePropertyType> ValuePropertyTypes =
            MTEUtils.GetEnumValues<ValuePropertyType>().ToList();

        public static readonly List<int> ValuePropertyNameIds =
            ValuePropertyTypes.Select(x => Shader.PropertyToID(x.ToString())).ToList();

        public ModelMaterialController controller { get; private set; }
        public Material material { get; private set; }

        public IModelStat model => controller.model;

        public string name
        {
            get => string.Format("{0}/{1}", model.name, material != null ? material.name : "");
        }

        public string displayName
        {
            get => material.name;
        }

        private HashSet<ColorPropertyType> hasColorProperties = new HashSet<ColorPropertyType>();
        private List<Color> initialColors = new List<Color>();

        private HashSet<ValuePropertyType> hasValueProperties = new HashSet<ValuePropertyType>();
        private List<float> initialValues = new List<float>();

        // 初期値を控え済みのプロパティ。シェーダー差し替えで初めて現れたプロパティは
        // その時点の値を初期値にし、元シェーダーから引き継いだものは元の初期値を保つ
        private HashSet<ColorPropertyType> capturedColors = new HashSet<ColorPropertyType>();
        private HashSet<ValuePropertyType> capturedValues = new HashSet<ValuePropertyType>();

        /// <summary>元のシェーダー。Init で掴み、ChangeShader では変えない (ゲーム側の差し替えは取り込む)</summary>
        public Shader originalShader { get; private set; }

        // 元シェーダーでの renderQueue。シェーダーを代入すると既定値へ戻るため控える
        private int originalRenderQueue;

        // 最後にこちらが設定したシェーダー。現在のシェーダーと食い違えば、
        // ゲーム (menu の shader コマンド等) が既存の Material を差し替えたと判断する
        private Shader appliedShader;

        /// <summary>こちらで差し替えたシェーダーが効いているか。ゲーム側に上書きされていれば false</summary>
        public bool isShaderChanged
            => material != null && originalShader != null
                && appliedShader != originalShader && material.shader == appliedShader;

        // テクスチャ差し替え。読み込んだテクスチャの所有と破棄もここが持つ
        private readonly ModelMaterialTextures _textures = new ModelMaterialTextures();

        public bool isTextureChanged => material != null && _textures.count > 0;

        /// <summary>シェーダーかテクスチャを差し替えているか。タイムラインへ保存する対象</summary>
        public bool isChanged => isShaderChanged || isTextureChanged;

        /// <summary>コントローラの一覧から外された。作り直しの検出 (MaterialShaderManager) に使う</summary>
        public bool isReleased { get; private set; }

        // シェーダーかテクスチャを変えたマテリアル。タイムラインへの同期 (MaterialShaderManager) が
        // 全メイド・全モデルを走査せずに済むよう、変更と戻しのたびに出し入れする
        private static readonly HashSet<ModelMaterial> changedMaterials = new HashSet<ModelMaterial>();

        /// <summary>changedMaterials の出し入れで増える。同期側の変更検出に使う</summary>
        public static int changedVersion { get; private set; }

        public ModelMaterial(ModelMaterialController controller, Material material)
        {
            this.controller = controller;
            this.material = material;

            if (material == null)
            {
                return;
            }

            Init();
        }

        public void Init()
        {
            // Material が作り直された。前の Material 用に読んだテクスチャは要らない
            // (再適用は MaterialShaderManager が保留から行う)
            _textures.DestroyAll();

            originalShader = material.shader;
            originalRenderQueue = material.renderQueue;
            appliedShader = material.shader;

            capturedColors.Clear();
            capturedValues.Clear();
            initialColors.Clear();
            initialValues.Clear();
            for (int i = 0; i < ColorPropertyNameIds.Count; i++)
            {
                initialColors.Add(Color.black);
            }
            for (int i = 0; i < ValuePropertyNameIds.Count; i++)
            {
                initialValues.Add(0f);
            }

            RefreshProperties();
            UpdateChangedRegistry();

            material.name = material.name.Replace(" (Instance)", "");
        }

        /// <summary>
        /// 現在のシェーダーが持つプロパティを数え直す。
        /// 初期値は初めて現れたプロパティだけ現在値で控え、既知のものは元の初期値を保つ
        /// </summary>
        private void RefreshProperties()
        {
            hasColorProperties.Clear();
            hasValueProperties.Clear();

            for (int i = 0; i < ColorPropertyNameIds.Count; i++)
            {
                var nameId = ColorPropertyNameIds[i];
                if (!material.HasProperty(nameId))
                {
                    continue;
                }
                var type = (ColorPropertyType)i;
                hasColorProperties.Add(type);
                if (capturedColors.Add(type))
                {
                    initialColors[i] = material.GetColor(nameId);
                }
            }

            for (int i = 0; i < ValuePropertyNameIds.Count; i++)
            {
                var nameId = ValuePropertyNameIds[i];
                if (!material.HasProperty(nameId))
                {
                    continue;
                }
                var type = (ValuePropertyType)i;
                hasValueProperties.Add(type);
                if (capturedValues.Add(type))
                {
                    initialValues[i] = material.GetFloat(nameId);
                }
            }
        }

        public void UpdateMaterial(Material material)
        {
            this.material = material;
            Init();
        }

        public bool HasColor(ColorPropertyType type)
        {
            return hasColorProperties.Contains(type);
        }

        public Color GetColor(ColorPropertyType type)
        {
            if (!hasColorProperties.Contains(type))
            {
                return Color.black;
            }

            return material.GetColor(ColorPropertyNameIds[(int)type]);
        }

        public void SetColor(ColorPropertyType type, Color color)
        {
            if (!hasColorProperties.Contains(type))
            {
                return;
            }

            material.SetColor(ColorPropertyNameIds[(int)type], color);
        }

        public Color GetInitialColor(ColorPropertyType type)
        {
            return initialColors[(int)type];
        }

        public bool HasValue(ValuePropertyType type)
        {
            return hasValueProperties.Contains(type);
        }

        public float GetValue(ValuePropertyType type)
        {
            if (!hasValueProperties.Contains(type))
            {
                return 0f;
            }

            return material.GetFloat(ValuePropertyNameIds[(int)type]);
        }

        public void SetValue(ValuePropertyType type, float value)
        {
            if (!hasValueProperties.Contains(type))
            {
                return;
            }

            material.SetFloat(ValuePropertyNameIds[(int)type], value);
        }

        public float GetInitialValue(ValuePropertyType type)
        {
            return initialValues[(int)type];
        }

        /// <summary>
        /// シェーダーを差し替える。同名プロパティの値は Unity が引き継ぐ。
        /// renderQueue は代入で既定値へ戻るため、元シェーダーでの状態から決め直す
        /// (直前のシェーダーから決めると、経由したシェーダーによって値が変わる)
        /// </summary>
        public void ChangeShader(Shader shader)
        {
            if (material == null || shader == null)
            {
                return;
            }
            AdoptExternalShader();
            if (material.shader == shader)
            {
                return;
            }

            material.shader = shader;
            material.renderQueue = SE.MaterialRenderQueue.Resolve(
                originalRenderQueue, originalShader.renderQueue, shader.renderQueue);
            appliedShader = shader;

            RefreshProperties();
            _textures.Reapply(material);
            UpdateChangedRegistry();
        }

        /// <summary>
        /// ゲーム側が既存の Material のシェーダーを差し替えていたら、それを新しい元の状態として取り込む。
        /// 取り込まないと「初期化」がゲームの入れたシェーダーを差し替え前へ戻してしまう
        /// </summary>
        private void AdoptExternalShader()
        {
            if (material.shader == appliedShader)
            {
                return;
            }
            originalShader = material.shader;
            originalRenderQueue = material.renderQueue;
            appliedShader = material.shader;
            RefreshProperties();
            UpdateChangedRegistry();
        }

        /// <summary>元のシェーダーへ戻す。値は戻さない (値は Reset が戻す)</summary>
        public void ResetShader()
        {
            if (material == null)
            {
                return;
            }
            // 引数の originalShader を評価する前に取り込む。後だとゲームが入れたシェーダーを差し替え前へ戻してしまう
            AdoptExternalShader();
            ChangeShader(originalShader);
        }

        public string GetTextureFile(string property) => _textures.GetFile(property);

        public bool IsTextureMissing(string property) => _textures.IsMissing(property);

        public void GetTextureOverrides(List<SE.MaterialTextureOverride> result) => _textures.GetOverrides(result);

        /// <summary>テクスチャを差し替える。file が空なら差し替え前へ戻す</summary>
        public void ChangeTexture(string property, string file)
        {
            if (material == null)
            {
                return;
            }
            _textures.DropExternallyReplaced(material);
            if (string.IsNullOrEmpty(file))
            {
                _textures.Reset(material, property);
            }
            else
            {
                _textures.Change(material, property, file);
            }
            UpdateChangedRegistry();
        }

        /// <summary>全テクスチャを差し替え前へ戻す。値とシェーダーは戻さない</summary>
        public void ResetTextures()
        {
            if (material == null)
            {
                return;
            }
            _textures.ResetAll(material);
            UpdateChangedRegistry();
        }

        /// <summary>差し替えを overrides の内容に揃える (Undo・ペースト・タイムライン読込)</summary>
        public void SetTextureOverrides(List<SE.MaterialTextureOverride> overrides)
        {
            if (material == null)
            {
                return;
            }
            _textures.DropExternallyReplaced(material);
            _textures.SetAll(material, overrides);
            UpdateChangedRegistry();
        }

        private void UpdateChangedRegistry()
        {
            var changed = isChanged
                ? changedMaterials.Add(this)
                : changedMaterials.Remove(this);
            if (changed)
            {
                changedVersion++;
            }
        }

        /// <summary>
        /// シェーダーかテクスチャを変えたマテリアルを result へ写す。
        /// 着替え・モデル削除で破棄されたものは読み込んだテクスチャを破棄して落とし、
        /// ゲーム側に上書きされて変更が残っていないものも落とす (落としたら version も進める)
        /// </summary>
        public static void CollectChanged(List<ModelMaterial> result)
        {
            result.Clear();
            var removed = changedMaterials.RemoveWhere(m =>
            {
                if (m.material == null || m.controller == null)
                {
                    m._textures.DestroyAll();
                    return true;
                }
                m._textures.DropExternallyReplaced(m.material);
                return !m.isChanged;
            });
            if (removed > 0)
            {
                changedVersion++;
            }
            result.AddRange(changedMaterials);
        }

        /// <summary>
        /// シーン遷移で全マテリアルのテクスチャ差し替えを元へ戻し、読み込んだテクスチャを破棄する。
        /// メイドはシーンをまたいで残るため、破棄したテクスチャを指したままにしない
        /// </summary>
        public static void ResetAllTextures()
        {
            foreach (var m in changedMaterials.ToList())
            {
                if (m.material != null)
                {
                    m._textures.ResetAll(m.material);
                }
                else
                {
                    m._textures.DestroyAll();
                }
            }
            if (changedMaterials.RemoveWhere(m => m.material == null || !m.isChanged) > 0)
            {
                changedVersion++;
            }
        }

        /// <summary>
        /// コントローラの一覧から外されたときに呼ぶ。Material も controller も生きたままなので
        /// CollectChanged の破棄検出では落ちず、ここで抜かないとレジストリに残り続ける
        /// </summary>
        public void Release()
        {
            isReleased = true;
            _textures.DestroyAll();
            if (changedMaterials.Remove(this))
            {
                changedVersion++;
            }
        }

        /// <summary>値を初期値へ戻す。シェーダーとテクスチャは戻さない (タイムラインのキー操作からも呼ばれるため)</summary>
        public void Reset()
        {
            foreach (var type in hasColorProperties)
            {
                if (HasColor(type))
                {
                    SetColor(type, GetInitialColor(type));
                }
            }

            foreach (var type in hasValueProperties)
            {
                if (HasValue(type))
                {
                    SetValue(type, GetInitialValue(type));
                }
            }
        }

        public void Apply(TransformDataModelMaterial trans)
        {
            SetColor(ColorPropertyType._Color, trans.color);
            SetColor(ColorPropertyType._ShadowColor, trans.ShadowColor);
            SetColor(ColorPropertyType._RimColor, trans.RimColor);
            SetColor(ColorPropertyType._OutlineColor, trans.OutlineColor);
            SetValue(ValuePropertyType._Shininess, trans.Shininess);
            SetValue(ValuePropertyType._OutlineWidth, trans.OutlineWidth);
            SetValue(ValuePropertyType._RimPower, trans.RimPower);
            SetValue(ValuePropertyType._RimShift, trans.RimShift);

            // NPR用プロパティ
            SetColor(ColorPropertyType._EmissionColor, trans.EmissionColor);
            SetColor(ColorPropertyType._MatcapColor, trans.MatcapColor);
            SetColor(ColorPropertyType._MatcapMaskColor, trans.MatcapMaskColor);
            SetColor(ColorPropertyType._RimLightColor, trans.RimLightColor);
            SetValue(ValuePropertyType._NormalValue, trans.NormalValue);
            SetValue(ValuePropertyType._ParallaxValue, trans.ParallaxValue);
            SetValue(ValuePropertyType._MatcapValue, trans.MatcapValue);
            SetValue(ValuePropertyType._MatcapMaskValue, trans.MatcapMaskValue);
            SetValue(ValuePropertyType._EmissionValue, trans.EmissionValue);
            SetValue(ValuePropertyType._EmissionHDRExposure, trans.EmissionHDRExposure);
            SetValue(ValuePropertyType._EmissionPower, trans.EmissionPower);
            SetValue(ValuePropertyType._RimLightValue, trans.RimLightValue);
            SetValue(ValuePropertyType._RimLightPower, trans.RimLightPower);
            SetValue(ValuePropertyType._MetallicValue, trans.MetallicValue);
            SetValue(ValuePropertyType._SmoothnessValue, trans.SmoothnessValue);
            SetValue(ValuePropertyType._OcclusionValue, trans.OcclusionValue);
        }
    }
}
