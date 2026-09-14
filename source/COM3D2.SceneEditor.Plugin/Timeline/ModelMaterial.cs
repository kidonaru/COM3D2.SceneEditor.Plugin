using System.Collections.Generic;
using System.Linq;
using UnityEngine;

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
            hasColorProperties.Clear();
            initialColors.Clear();
            hasValueProperties.Clear();
            initialValues.Clear();

            for (int i = 0; i < ColorPropertyNameIds.Count; i++)
            {
                var nameId = ColorPropertyNameIds[i];
                if (material.HasProperty(nameId))
                {
                    hasColorProperties.Add((ColorPropertyType)i);
                    initialColors.Add(material.GetColor(nameId));
                }
                else
                {
                    initialColors.Add(Color.black);
                }
            }

            for (int i = 0; i < ValuePropertyNameIds.Count; i++)
            {
                var nameId = ValuePropertyNameIds[i];
                if (material.HasProperty(nameId))
                {
                    hasValueProperties.Add((ValuePropertyType)i);
                    initialValues.Add(material.GetFloat(nameId));
                }
                else
                {
                    initialValues.Add(0f);
                }
            }

            material.name = material.name.Replace(" (Instance)", "");
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
