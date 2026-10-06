using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    public enum BodySliderKind
    {
        /// <summary>骨の localScale へ掛ける倍率。値は (width, depth, height)</summary>
        Scale,
        /// <summary>骨の localPosition へ足す差分。値は (x, y, z)</summary>
        Position,
    }

    /// <summary>項目の成分 1 つ分 (ModsParam の value 1 行)</summary>
    public class BodySliderComponent
    {
        /// <summary>ModsParam のサブキー名 (width / x など)。カスタム値のキーにも使う</summary>
        public readonly string name;
        public readonly string label;
        public readonly float min;
        public readonly float max;
        /// <summary>ModsParam で visible="false" の成分は UI に出さない (値は持ち、適用もする)</summary>
        public readonly bool visible;

        public BodySliderComponent(string name, string label, float min, float max, bool visible)
        {
            this.name = name;
            this.label = label;
            this.min = min;
            this.max = max;
            this.visible = visible;
        }
    }

    /// <summary>位置の差分の軸 1 つ分。source は値の添字 (負なら常に 0)、sign は符号</summary>
    public struct BodySliderAxis
    {
        public int source;
        public float sign;
    }

    /// <summary>項目が掛かる骨 1 本分。axes は位置の項目だけが持つ (x, y, z の 3 軸)</summary>
    public class BodySliderTarget
    {
        public readonly string boneName;
        public readonly BodySliderAxis[] axes;

        public BodySliderTarget(string boneName, BodySliderAxis[] axes)
        {
            this.boneName = boneName;
            this.axes = axes;
        }
    }

    /// <summary>
    /// 体型スライダーの項目 1 件分。値は ModsParam のサブキーの並びで 3 個持ち、
    /// 骨の軸への変換は適用時に行う (UI とデータの数値を ModsSlider とそろえるため)
    /// </summary>
    public class BodySliderItem
    {
        public readonly string key;
        public readonly string displayName;
        public readonly string group;
        public readonly BodySliderKind kind;
        public readonly BodySliderComponent[] components;
        /// <summary>位置の差分を割る係数。スケールでは使わない</summary>
        public readonly float positionDivisor;
        public readonly List<BodySliderTarget> targets;

        public BodySliderItem(
            string key, string displayName, string group, BodySliderKind kind,
            BodySliderComponent[] components, float positionDivisor, List<BodySliderTarget> targets)
        {
            this.key = key;
            this.displayName = displayName;
            this.group = group;
            this.kind = kind;
            this.components = components;
            this.positionDivisor = positionDivisor;
            this.targets = targets;
        }

        public float defaultValue => kind == BodySliderKind.Scale ? 1f : 0f;

        public Vector3 defaultValues => new Vector3(defaultValue, defaultValue, defaultValue);

        /// <summary>成分ごとに範囲へ丸める。NaN (手で書き換えたデータなど) は既定値にする</summary>
        public Vector3 Clamp(Vector3 values)
        {
            var result = values;
            for (var i = 0; i < 3; i++)
            {
                var value = values[i];
                result[i] = float.IsNaN(value)
                    ? defaultValue
                    : Mathf.Clamp(value, components[i].min, components[i].max);
            }
            return result;
        }

        /// <summary>丸めた値が既定値か。状態を持つか・キーを適用するかの判定をそろえる</summary>
        public bool IsDefault(Vector3 values)
        {
            var clamped = Clamp(values);
            for (var i = 0; i < 3; i++)
            {
                if (!Mathf.Approximately(clamped[i], defaultValue))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>(width, depth, height) を localScale の倍率 (height, depth, width) へ変える。MVP の SetBoneScale と同じ割り当て</summary>
        public Vector3 ToScale(Vector3 values)
        {
            return new Vector3(values.z, values.y, values.x);
        }

        /// <summary>(x, y, z) を骨の localPosition の差分へ変える。軸の入れ替えと符号は骨ごとの定義に従う</summary>
        public Vector3 ToOffset(Vector3 values, BodySliderTarget target)
        {
            var result = Vector3.zero;
            for (var i = 0; i < 3; i++)
            {
                var axis = target.axes[i];
                if (axis.source < 0)
                {
                    continue;
                }
                result[i] = axis.sign * values[axis.source] / positionDivisor;
            }
            return result;
        }
    }

    /// <summary>
    /// 体型スライダーの全項目。骨・軸・係数は MaidVoicePitch v0.2.17.6 の WideSlider に合わせる。
    /// 並びは UI・タイムラインのメニュー・キーの並びを兼ねる
    /// </summary>
    public static class BodySliderDefs
    {
        public const string GroupLeg = "脚";
        public const string GroupHip = "腰";
        public const string GroupBody = "胴・首";
        public const string GroupMune = "胸";
        public const string GroupArm = "肩・腕";

        public static readonly string[] groupNames = { GroupLeg, GroupHip, GroupBody, GroupMune, GroupArm };

        private const float LegScaleMax = 2f;
        private const float BodyScaleMax = 3f;
        private const float ScaleMin = 0.1f;
        private const float HipPositionMin = -100f;
        private const float HipPositionMax = 200f;
        private const float PositionMin = -1f;
        private const float PositionMax = 1f;
        private const float HipPositionDivisor = 1000f;
        private const float PositionDivisor = 10f;

        /// <summary>旧メイドスケール (腕 6 本の均一倍率) の骨名 → 腕の左右別項目。タイムラインとプリセットの移行で使う</summary>
        public static readonly Dictionary<string, string> legacyMaidScaleKeys = new Dictionary<string, string>
        {
            { "Bip01 L UpperArm", "UPARMSCL_L" },
            { "Bip01 L Forearm", "FARMSCL_L" },
            { "Bip01 L Hand", "HANDSCL_L" },
            { "Bip01 R UpperArm", "UPARMSCL_R" },
            { "Bip01 R Forearm", "FARMSCL_R" },
            { "Bip01 R Hand", "HANDSCL_R" },
        };

        public static readonly List<BodySliderItem> items = BuildItems();

        private static Dictionary<string, BodySliderItem> _itemMap;
        private static HashSet<string> _targetBones;

        public static BodySliderItem Find(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }
            if (_itemMap == null)
            {
                _itemMap = new Dictionary<string, BodySliderItem>();
                foreach (var item in items)
                {
                    _itemMap[item.key] = item;
                }
            }
            BodySliderItem result;
            return _itemMap.TryGetValue(key, out result) ? result : null;
        }

        /// <summary>いずれかの項目が掛かる骨か。複製骨のキャッシュを作るときに使う</summary>
        public static bool IsTargetBone(string boneName)
        {
            if (string.IsNullOrEmpty(boneName))
            {
                return false;
            }
            if (_targetBones == null)
            {
                _targetBones = new HashSet<string>();
                foreach (var item in items)
                {
                    foreach (var target in item.targets)
                    {
                        _targetBones.Add(target.boneName);
                    }
                }
            }
            return _targetBones.Contains(boneName);
        }

        public static IEnumerable<BodySliderItem> ItemsInGroup(string group)
        {
            foreach (var item in items)
            {
                if (item.group == group)
                {
                    yield return item;
                }
            }
        }

        private static List<BodySliderItem> BuildItems()
        {
            return new List<BodySliderItem>
            {
                // 脚
                Scale("THISCL", "足全体のスケーリング", GroupLeg, LegScaleMax, true, "Bip01 L Thigh", "Bip01 R Thigh"),
                Scale("THISCL2", "膝のスケーリング", GroupLeg, LegScaleMax, true, "Bip01 L Thigh_SCL_", "Bip01 R Thigh_SCL_"),
                Position("THIPOS", "足の位置", GroupLeg, HipPositionComponents(hiddenY: true), HipPositionDivisor,
                    Target("Bip01 L Thigh", "0", "+Z", "-X"), Target("Bip01 R Thigh", "0", "+Z", "+X"),
                    Target("Hip_L", "0", "+Z", "-X"), Target("Hip_R", "0", "+Z", "+X")),
                Position("THI2POS", "膝の位置", GroupLeg, HipPositionComponents(hiddenY: false), HipPositionDivisor,
                    Target("Bip01 L Thigh_SCL_", "+Y", "+Z", "-X"), Target("Bip01 R Thigh_SCL_", "+Y", "+Z", "+X")),
                Scale("MTWSCL", "ももねじれ肉のスケーリング", GroupLeg, LegScaleMax, false, "momotwist_L", "momotwist_R"),
                Position("MTWPOS", "ももねじれ肉の位置", GroupLeg, Positions("高さ", "前後", "左右", -1), PositionDivisor,
                    Target("momotwist_L", "+X", "+Y", "+Z"), Target("momotwist_R", "+X", "+Y", "-Z")),
                Scale("MMNSCL", "もも肉のスケーリング", GroupLeg, LegScaleMax, false, "momoniku_L", "momoniku_R"),
                Position("MMNPOS", "もも肉の位置", GroupLeg, Positions("高さ", "前後", "左右", -1), PositionDivisor,
                    Target("momoniku_L", "+X", "+Y", "+Z"), Target("momoniku_R", "+X", "-Y", "+Z")),
                Scale("CALFSCL", "膝下のスケーリング", GroupLeg, LegScaleMax, true, "Bip01 L Calf", "Bip01 R Calf"),
                Scale("FOOTSCL", "足首より下のスケーリング", GroupLeg, LegScaleMax, false, "Bip01 L Foot", "Bip01 R Foot"),

                // 腰
                Scale("PELSCL", "骨盤スケーリング", GroupHip, LegScaleMax, false, "Bip01 Pelvis_SCL_", "Hip_L", "Hip_R"),
                Scale("HIPSCL", "尻スケーリング", GroupHip, LegScaleMax, false, "Hip_L", "Hip_R"),
                Position("HIPPOS", "尻の位置", GroupHip, HipPositionComponents(hiddenY: false), HipPositionDivisor,
                    Target("Hip_L", "+Y", "+Z", "-X"), Target("Hip_R", "+Y", "+Z", "+X")),
                Scale("SKTSCL", "スカート周辺スケーリング", GroupHip, BodyScaleMax, false, "Skirt"),
                Position("SKTPOS", "スカート位置", GroupHip,
                    new[]
                    {
                        new BodySliderComponent("x", "左右", PositionMin, PositionMax, false),
                        new BodySliderComponent("y", "前後", PositionMin, PositionMax, false),
                        new BodySliderComponent("z", "高さ", PositionMin, PositionMax, true),
                    },
                    PositionDivisor,
                    Target("Skirt", "-Z", "-Y", "+X")),

                // 胴・首
                Scale("SPISCL", "胴(下腹部)スケーリング", GroupBody, BodyScaleMax, false, "Bip01 Spine_SCL_"),
                Position("SPIPOS", "胴(下腹部)ポジション", GroupBody,
                    new[]
                    {
                        new BodySliderComponent("x", "前後", PositionMin, PositionMax, true),
                        new BodySliderComponent("y", "高さ", PositionMin, PositionMax, false),
                        new BodySliderComponent("z", "上下", PositionMin, PositionMax, true),
                    },
                    PositionDivisor,
                    Target("Bip01 Spine", "-X", "+Y", "+Z")),
                Scale("S0ASCL", "胴(腹部)スケーリング", GroupBody, BodyScaleMax, false, "Bip01 Spine0a_SCL_"),
                Position("S0APOS", "胴(腹部)ポジション", GroupBody, Positions("上下", "前後", "左右", 2), PositionDivisor,
                    Target("Bip01 Spine0a", "-X", "+Y", "+Z")),
                Scale("S1_SCL", "胴(みぞおち)スケーリング", GroupBody, BodyScaleMax, false, "Bip01 Spine1_SCL_"),
                Position("S1POS", "胴(みぞおち)ポジション", GroupBody, Positions("上下", "前後", "左右", 2), PositionDivisor,
                    Target("Bip01 Spine1", "-X", "+Y", "+Z")),
                Scale("S1ASCL", "胴(肋骨)スケーリング", GroupBody, BodyScaleMax, false, "Bip01 Spine1a_SCL_"),
                Position("S1APOS", "胴(肋骨)ポジション", GroupBody, Positions("上下", "前後", "左右", 2), PositionDivisor,
                    Target("Bip01 Spine1a", "-X", "+Y", "+Z")),
                Scale("NECKSCL", "首スケーリング", GroupBody, BodyScaleMax, false, "Bip01 Neck_SCL_"),
                Position("NECKPOS", "首ポジション", GroupBody, Positions("上下", "前後", "左右", 2), PositionDivisor,
                    Target("Bip01 Neck", "-X", "+Y", "+Z")),

                // 胸
                Item("MUNESCL", "胸スケーリング", GroupMune, BodySliderKind.Scale,
                    Scales("横幅", "縦幅", "奥行", BodyScaleMax, hiddenHeight: false), 1f,
                    Target("Mune_L"), Target("Mune_R")),
                Position("MUNEPOS", "胸ポジション", GroupMune, Positions("左右", "前後", "高さ", -1), PositionDivisor,
                    Target("Mune_L", "+Z", "-Y", "+X"), Target("Mune_R", "+Z", "-Y", "-X")),
                Item("MUNESUBSCL", "胸サブスケーリング", GroupMune, BodySliderKind.Scale,
                    Scales("横幅", "縦幅", "奥行", BodyScaleMax, hiddenHeight: false), 1f,
                    Target("Mune_L_sub"), Target("Mune_R_sub")),
                Position("MUNESUBPOS", "胸サブポジション", GroupMune, Positions("左右", "前後", "高さ", -1), PositionDivisor,
                    Target("Mune_L_sub", "-Y", "+Z", "-X"), Target("Mune_R_sub", "-Y", "-Z", "-X")),

                // 肩・腕
                // ModsParam のラベルは width=高さ / height=横幅 と逆に付いているが、ModsSlider と同じ表示にそろえる
                Item("CLVSCL", "鎖骨スケーリング", GroupArm, BodySliderKind.Scale,
                    Scales("高さ", "奥行", "横幅", BodyScaleMax, hiddenHeight: false), 1f,
                    Target("Bip01 L Clavicle"), Target("Bip01 R Clavicle")),
                Position("CLVPOS", "鎖骨の位置", GroupArm, Positions("上下", "前後", "左右", -1), PositionDivisor,
                    Target("Bip01 L Clavicle", "-X", "+Y", "+Z"), Target("Bip01 R Clavicle", "-X", "+Y", "-Z")),
                Scale("KATASCL", "肩スケーリング", GroupArm, BodyScaleMax, false, "Kata_L", "Kata_R"),
                // 腕は ModsParam では左右共通だが、SE では左右別に調整できるようにする
                Scale("UPARMSCL_L", "上腕スケーリング(左)", GroupArm, BodyScaleMax, false, "Bip01 L UpperArm"),
                Scale("UPARMSCL_R", "上腕スケーリング(右)", GroupArm, BodyScaleMax, false, "Bip01 R UpperArm"),
                Scale("FARMSCL_L", "前腕スケーリング(左)", GroupArm, BodyScaleMax, false, "Bip01 L Forearm"),
                Scale("FARMSCL_R", "前腕スケーリング(右)", GroupArm, BodyScaleMax, false, "Bip01 R Forearm"),
                Scale("HANDSCL_L", "手スケーリング(左)", GroupArm, BodyScaleMax, false, "Bip01 L Hand"),
                Scale("HANDSCL_R", "手スケーリング(右)", GroupArm, BodyScaleMax, false, "Bip01 R Hand"),
            };
        }

        private static BodySliderItem Item(
            string key, string displayName, string group, BodySliderKind kind,
            BodySliderComponent[] components, float positionDivisor, params BodySliderTarget[] targets)
        {
            return new BodySliderItem(key, displayName, group, kind, components, positionDivisor,
                new List<BodySliderTarget>(targets));
        }

        private static BodySliderItem Scale(
            string key, string displayName, string group, float max, bool hiddenHeight, params string[] boneNames)
        {
            var targets = new BodySliderTarget[boneNames.Length];
            for (var i = 0; i < boneNames.Length; i++)
            {
                targets[i] = Target(boneNames[i]);
            }
            return Item(key, displayName, group, BodySliderKind.Scale,
                Scales("横幅", "奥行", "高さ", max, hiddenHeight), 1f, targets);
        }

        private static BodySliderItem Position(
            string key, string displayName, string group, BodySliderComponent[] components,
            float divisor, params BodySliderTarget[] targets)
        {
            return Item(key, displayName, group, BodySliderKind.Position, components, divisor, targets);
        }

        private static BodySliderComponent[] Scales(
            string widthLabel, string depthLabel, string heightLabel, float max, bool hiddenHeight)
        {
            return new[]
            {
                new BodySliderComponent("width", widthLabel, ScaleMin, max, true),
                new BodySliderComponent("depth", depthLabel, ScaleMin, max, true),
                new BodySliderComponent("height", heightLabel, ScaleMin, max, !hiddenHeight),
            };
        }

        /// <param name="hiddenIndex">ModsParam で非表示の成分の添字。無ければ -1</param>
        private static BodySliderComponent[] Positions(string xLabel, string yLabel, string zLabel, int hiddenIndex)
        {
            return new[]
            {
                new BodySliderComponent("x", xLabel, PositionMin, PositionMax, hiddenIndex != 0),
                new BodySliderComponent("y", yLabel, PositionMin, PositionMax, hiddenIndex != 1),
                new BodySliderComponent("z", zLabel, PositionMin, PositionMax, hiddenIndex != 2),
            };
        }

        /// <summary>足・膝・尻の位置 (THIPOS / THI2POS / HIPPOS) の成分。範囲が広く /1000 で効く</summary>
        private static BodySliderComponent[] HipPositionComponents(bool hiddenY)
        {
            return new[]
            {
                new BodySliderComponent("x", "左右", HipPositionMin, HipPositionMax, true),
                new BodySliderComponent("y", "高さ", HipPositionMin, HipPositionMax, !hiddenY),
                new BodySliderComponent("z", "前後", HipPositionMin, HipPositionMax, true),
            };
        }

        private static BodySliderTarget Target(string boneName)
        {
            return new BodySliderTarget(boneName, null);
        }

        /// <summary>差分の x, y, z 軸を "+X" / "-Z" / "0" の記法で受け取る (MVP の式をそのまま写すため)</summary>
        private static BodySliderTarget Target(string boneName, string x, string y, string z)
        {
            return new BodySliderTarget(boneName, new[] { ParseAxis(x), ParseAxis(y), ParseAxis(z) });
        }

        private static BodySliderAxis ParseAxis(string spec)
        {
            if (spec == "0")
            {
                return new BodySliderAxis { source = -1, sign = 0f };
            }
            return new BodySliderAxis
            {
                source = spec[1] - 'X',
                sign = spec[0] == '-' ? -1f : 1f,
            };
        }
    }
}
