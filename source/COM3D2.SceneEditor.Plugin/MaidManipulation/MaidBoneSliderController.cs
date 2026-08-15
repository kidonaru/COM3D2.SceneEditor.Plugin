using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    public class BoneSliderAxisDef
    {
        public string label;
        public float min;
        public float max;
    }

    public class BoneSliderDef
    {
        public string boneName;
        public string displayName;
        /// <summary>ローカル X/Y/Z 軸の順。常に 3 要素</summary>
        public BoneSliderAxisDef[] axes;
    }

    /// <summary>
    /// ボーンスライダーの値の読み書き。スライダーは独自状態を持たず、
    /// 停止ポーズ（基準回転）からのオフセット角として現在の localRotation を毎フレーム分解する。
    /// これによりドラッグ点・ギズモでの変更も自動的にスライダーへ反映される
    /// </summary>
    public static class MaidBoneSliderController
    {
        /// <summary>メイドごとの基準回転（モーション停止時の localRotation）</summary>
        private static readonly Dictionary<Maid, Dictionary<string, Quaternion>> _basePoses
            = new Dictionary<Maid, Dictionary<string, Quaternion>>();

        /// <summary>
        /// メイドごとのボーン Transform キャッシュ。
        /// TBody.GetBone はキャッシュなしの名前探索のため、OnGUI から毎フレーム呼ぶとコストが嵩む
        /// </summary>
        private static readonly Dictionary<Maid, Dictionary<string, Transform>> _boneCache
            = new Dictionary<Maid, Dictionary<string, Transform>>();

        private static BoneSliderAxisDef Axis(string label, float min, float max)
        {
            return new BoneSliderAxisDef { label = label, min = min, max = max };
        }

        private static BoneSliderDef Def(
            string boneName, string displayName,
            BoneSliderAxisDef x, BoneSliderAxisDef y, BoneSliderAxisDef z)
        {
            return new BoneSliderDef
            {
                boneName = boneName,
                displayName = displayName,
                axes = new[] { x, y, z },
            };
        }

        /// <summary>
        /// 対象ボーンと可動範囲の定義。範囲は AddBoneSlider の BoneParam.xml を参考にした値。
        /// ラベルの軸割当はボーンごとに異なる（Bip01 系はローカル X が骨方向のため大半は X=ひねり）
        /// </summary>
        private static readonly List<BoneSliderDef> _defs = new List<BoneSliderDef>
        {
            // 上半身
            Def("Bip01 Head",       "頭",     Axis("ひねり", -50f, 50f),   Axis("横曲げ", -40f, 40f),   Axis("縦曲げ", -40f, 80f)),
            Def("Bip01 Neck",       "首",     Axis("ひねり", -50f, 50f),   Axis("横曲げ", -40f, 40f),   Axis("縦曲げ", -50f, 42.5f)),
            Def("Bip01 Spine1a",    "背中上", Axis("ひねり", -20f, 20f),   Axis("横曲げ", -17.5f, 17.5f), Axis("縦曲げ", -30f, 22.5f)),
            Def("Bip01 Spine1",     "背中下", Axis("ひねり", -20f, 20f),   Axis("横曲げ", -17.5f, 17.5f), Axis("縦曲げ", -30f, 22.5f)),
            Def("Bip01 Spine0a",    "腰上",   Axis("ひねり", -10f, 10f),   Axis("横曲げ", -15f, 15f),   Axis("縦曲げ", -35f, 17.5f)),
            Def("Bip01 Spine",      "腰下",   Axis("縦曲げ", -35f, 17.5f), Axis("横曲げ", -15f, 15f),   Axis("ひねり", -10f, 10f)),
            Def("Bip01 L Clavicle", "左鎖骨", Axis("ひねり", -30f, 30f),   Axis("横曲げ", -20f, 30f),   Axis("縦曲げ", -30f, 30f)),
            Def("Bip01 L UpperArm", "左上腕", Axis("ひねり", -180f, 90f),  Axis("横曲げ", -90f, 180f),  Axis("縦曲げ", -90f, 90f)),
            Def("Bip01 L Forearm",  "左前腕", Axis("ひねり", -20f, 20f),   Axis("横曲げ", -10f, 10f),   Axis("縦曲げ", -10f, 155f)),
            Def("Bip01 L Hand",     "左手",   Axis("ひねり", -180f, 180f), Axis("横曲げ", -90f, 90f),   Axis("縦曲げ", -90f, 90f)),
            Def("Bip01 R Clavicle", "右鎖骨", Axis("ひねり", -30f, 30f),   Axis("横曲げ", -30f, 20f),   Axis("縦曲げ", -30f, 30f)),
            Def("Bip01 R UpperArm", "右上腕", Axis("ひねり", -90f, 180f),  Axis("横曲げ", -180f, 90f),  Axis("縦曲げ", -90f, 90f)),
            Def("Bip01 R Forearm",  "右前腕", Axis("ひねり", -20f, 20f),   Axis("横曲げ", -10f, 10f),   Axis("縦曲げ", -10f, 155f)),
            Def("Bip01 R Hand",     "右手",   Axis("ひねり", -180f, 180f), Axis("横曲げ", -90f, 90f),   Axis("縦曲げ", -90f, 90f)),
            Def("Mune_L",           "左胸",   Axis("ひねり", -90f, 90f),   Axis("横曲げ", -90f, 90f),   Axis("縦曲げ", -90f, 90f)),
            Def("Mune_R",           "右胸",   Axis("ひねり", -90f, 90f),   Axis("横曲げ", -90f, 90f),   Axis("縦曲げ", -90f, 90f)),

            // 下半身
            Def("Bip01",            "全体",   Axis("X回転", -180f, 180f),  Axis("Y回転", -180f, 180f),  Axis("Z回転", -180f, 180f)),
            Def("Bip01 Pelvis",     "骨盤",   Axis("縦曲げ", -40f, 25f),   Axis("横曲げ", -35f, 35f),   Axis("ひねり", -25f, 25f)),
            Def("Bip01 L Thigh",    "左太腿", Axis("ひねり", -45f, 55f),   Axis("横曲げ", -50f, 35f),   Axis("縦曲げ", -30f, 130f)),
            Def("Bip01 L Calf",     "左すね", Axis("ひねり", -40f, 30f),   Axis("横曲げ", -20f, 20f),   Axis("縦曲げ", -10f, 150f)),
            Def("Bip01 L Foot",     "左足",   Axis("ひねり", -30f, 20f),   Axis("横曲げ", -30f, 40f),   Axis("縦曲げ", -35f, 60f)),
            Def("Bip01 R Thigh",    "右太腿", Axis("ひねり", -55f, 45f),   Axis("横曲げ", -35f, 50f),   Axis("縦曲げ", -30f, 130f)),
            Def("Bip01 R Calf",     "右すね", Axis("ひねり", -30f, 40f),   Axis("横曲げ", -20f, 20f),   Axis("縦曲げ", -10f, 150f)),
            Def("Bip01 R Foot",     "右足",   Axis("ひねり", -20f, 30f),   Axis("横曲げ", -40f, 30f),   Axis("縦曲げ", -35f, 60f)),
        };

        /// <summary>ボーン名からの逆引き。ギズモ操作中ボーンへの自動追従に使う</summary>
        private static readonly Dictionary<string, BoneSliderDef> _defsByBoneName
            = _defs.ToDictionary(d => d.boneName);

        public static List<BoneSliderDef> allDefs => _defs;

        /// <summary>ボーン名から定義を引く。対象外のボーンなら null</summary>
        public static BoneSliderDef FindDef(string boneName)
        {
            BoneSliderDef def;
            return boneName != null && _defsByBoneName.TryGetValue(boneName, out def) ? def : null;
        }

        public static Transform GetBone(Maid maid, string boneName)
        {
            if (maid == null || maid.body0 == null)
            {
                return null;
            }

            Dictionary<string, Transform> cache;
            if (!_boneCache.TryGetValue(maid, out cache))
            {
                cache = new Dictionary<string, Transform>();
                _boneCache[maid] = cache;
            }

            Transform bone;
            if (!cache.TryGetValue(boneName, out bone))
            {
                // CRC ボディ差異等でボーンが無い場合は null をキャッシュ（呼び出し側で行をスキップ）
                bone = maid.body0.GetBone(boneName);
                cache[boneName] = bone;
            }
            return bone;
        }

        /// <summary>モーション停止時に呼ぶ。全対象ボーンの現在回転を基準として記録する</summary>
        public static void CaptureBasePose(Maid maid)
        {
            if (maid == null)
            {
                return;
            }

            // 再生位置のシーク中は毎フレーム呼ばれるため、既存の辞書を使い回して確保を避ける
            Dictionary<string, Quaternion> poses;
            if (_basePoses.TryGetValue(maid, out poses))
            {
                poses.Clear();
            }
            else
            {
                poses = new Dictionary<string, Quaternion>();
            }
            foreach (var def in _defs)
            {
                var bone = GetBone(maid, def.boneName);
                if (bone != null)
                {
                    poses[def.boneName] = bone.localRotation;
                }
            }
            _basePoses[maid] = poses;
        }

        public static void ClearBasePose(Maid maid)
        {
            if (maid != null)
            {
                _basePoses.Remove(maid);
                // 衣装替え等でボーンが作り直されている可能性があるためキャッシュも破棄する
                _boneCache.Remove(maid);
            }
        }

        public static void Clear()
        {
            _basePoses.Clear();
            _boneCache.Clear();
        }

        private static Quaternion GetBaseRotation(Maid maid, BoneSliderDef def, Transform bone)
        {
            Dictionary<string, Quaternion> poses;
            if (!_basePoses.TryGetValue(maid, out poses))
            {
                // 基準未記録なら現在の回転を基準にする。
                // モーション再生中は UI 側（DrawPoseTab）が IsPlaying でこの経路に入れないため、
                // ここに来るのは「そもそもモーションが再生されていない」ケースのみ
                CaptureBasePose(maid);
                poses = _basePoses[maid];
            }

            Quaternion baseRot;
            if (!poses.TryGetValue(def.boneName, out baseRot))
            {
                baseRot = bone.localRotation;
                poses[def.boneName] = baseRot;
            }
            return baseRot;
        }

        /// <summary>角度を -180〜180 に正規化する</summary>
        private static float NormalizeAngle(float angle)
        {
            angle = Mathf.Repeat(angle, 360f);
            return angle > 180f ? angle - 360f : angle;
        }

        /// <summary>基準回転からのオフセット角（±180 正規化済み）を返す</summary>
        public static Vector3 GetOffset(Maid maid, BoneSliderDef def)
        {
            var bone = GetBone(maid, def.boneName);
            if (bone == null)
            {
                return Vector3.zero;
            }

            var baseRot = GetBaseRotation(maid, def, bone);
            var euler = (Quaternion.Inverse(baseRot) * bone.localRotation).eulerAngles;
            return new Vector3(
                NormalizeAngle(euler.x),
                NormalizeAngle(euler.y),
                NormalizeAngle(euler.z));
        }

        /// <summary>指定軸のオフセット角を書き込む。他軸は現在値を維持する</summary>
        public static void SetOffsetAxis(Maid maid, BoneSliderDef def, int axisIndex, float value)
        {
            var bone = GetBone(maid, def.boneName);
            if (bone == null)
            {
                return;
            }

            var offset = GetOffset(maid, def);
            offset[axisIndex] = value;

            var baseRot = GetBaseRotation(maid, def, bone);
            bone.localRotation = baseRot * Quaternion.Euler(offset);
        }
    }
}
