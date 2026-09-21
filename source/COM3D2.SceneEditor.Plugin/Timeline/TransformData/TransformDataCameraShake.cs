using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// カメラ手ブレのキーフレームデータ。
    /// 位置・回転そのものではなく揺れの振幅を持ち、CameraTimelineLayer がノイズへ通して
    /// カメラ Transform のオフセットに変換する。
    /// 振幅自体がキーで動くのでフェードイン/アウトは振幅キーで表現する
    /// </summary>
    public class TransformDataCameraShake : TransformDataBase
    {
        public override TransformType type => TransformType.CameraShake;

        public enum Index
        {
            PosAmplitudeX = 0,
            PosAmplitudeY = 1,
            PosAmplitudeZ = 2,
            RotAmplitudeX = 3,
            RotAmplitudeY = 4,
            RotAmplitudeZ = 5,
            FrequencyScale = 6,
            Seed = 7,
        }

        public override int valueCount => 8;

        public override bool hasPosition => false;
        public override bool hasEulerAngles => false;
        public override bool hasScale => false;
        // カメラ値と同じく常時 Tangent 補間
        public override bool hasTangent => true;

        public override ValueData[] tangentValues => values;

        /// <summary>振幅の上限。手持ちカメラとして自然な範囲に収める</summary>
        public const float MaxPositionAmplitude = 0.2f;
        public const float MaxRotationAmplitude = 5f;

        /// <summary>周波数倍率の範囲。0 は位相が進まず揺れが止まるので、下限は 0 のまま許す</summary>
        public const float MinFrequencyScale = 0f;
        public const float MaxFrequencyScale = 5f;

        /// <summary>シードの上限</summary>
        public const float MaxSeed = 9999f;

        private readonly static Dictionary<string, CustomValueInfo> CustomValueInfoMap
            = new Dictionary<string, CustomValueInfo>
        {
            {
                "posAmplitudeX",
                new CustomValueInfo
                {
                    index = (int)Index.PosAmplitudeX,
                    name = "位置X",
                    min = 0f,
                    max = MaxPositionAmplitude,
                    step = 0.001f,
                    defaultValue = 0f,
                }
            },
            {
                "posAmplitudeY",
                new CustomValueInfo
                {
                    index = (int)Index.PosAmplitudeY,
                    name = "位置Y",
                    min = 0f,
                    max = MaxPositionAmplitude,
                    step = 0.001f,
                    defaultValue = 0f,
                }
            },
            {
                "posAmplitudeZ",
                new CustomValueInfo
                {
                    index = (int)Index.PosAmplitudeZ,
                    name = "位置Z",
                    min = 0f,
                    max = MaxPositionAmplitude,
                    step = 0.001f,
                    defaultValue = 0f,
                }
            },
            {
                "rotAmplitudeX",
                new CustomValueInfo
                {
                    index = (int)Index.RotAmplitudeX,
                    name = "回転X",
                    min = 0f,
                    max = MaxRotationAmplitude,
                    step = 0.01f,
                    defaultValue = 0f,
                }
            },
            {
                "rotAmplitudeY",
                new CustomValueInfo
                {
                    index = (int)Index.RotAmplitudeY,
                    name = "回転Y",
                    min = 0f,
                    max = MaxRotationAmplitude,
                    step = 0.01f,
                    defaultValue = 0f,
                }
            },
            {
                "rotAmplitudeZ",
                new CustomValueInfo
                {
                    index = (int)Index.RotAmplitudeZ,
                    name = "回転Z",
                    min = 0f,
                    max = MaxRotationAmplitude,
                    step = 0.01f,
                    defaultValue = 0f,
                }
            },
            {
                "frequencyScale",
                new CustomValueInfo
                {
                    index = (int)Index.FrequencyScale,
                    name = "周波数",
                    min = MinFrequencyScale,
                    max = MaxFrequencyScale,
                    step = 0.01f,
                    defaultValue = 1f,
                }
            },
            {
                "seed",
                new CustomValueInfo
                {
                    index = (int)Index.Seed,
                    name = "シード",
                    min = 0f,
                    max = MaxSeed,
                    step = 1f,
                    defaultValue = 0f,
                }
            },
        };

        public override Dictionary<string, CustomValueInfo> GetCustomValueInfoMap()
        {
            return CustomValueInfoMap;
        }

        /// <summary>8 個の値とパラメータ構造体の相互変換</summary>
        public CameraShakeParams shakeParams
        {
            get
            {
                return new CameraShakeParams
                {
                    positionAmplitude = new Vector3(
                        values[(int)Index.PosAmplitudeX].value,
                        values[(int)Index.PosAmplitudeY].value,
                        values[(int)Index.PosAmplitudeZ].value),
                    rotationAmplitude = new Vector3(
                        values[(int)Index.RotAmplitudeX].value,
                        values[(int)Index.RotAmplitudeY].value,
                        values[(int)Index.RotAmplitudeZ].value),
                    frequencyScale = values[(int)Index.FrequencyScale].value,
                    seed = values[(int)Index.Seed].intValue,
                };
            }
            set
            {
                values[(int)Index.PosAmplitudeX].value = value.positionAmplitude.x;
                values[(int)Index.PosAmplitudeY].value = value.positionAmplitude.y;
                values[(int)Index.PosAmplitudeZ].value = value.positionAmplitude.z;
                values[(int)Index.RotAmplitudeX].value = value.rotationAmplitude.x;
                values[(int)Index.RotAmplitudeY].value = value.rotationAmplitude.y;
                values[(int)Index.RotAmplitudeZ].value = value.rotationAmplitude.z;
                values[(int)Index.FrequencyScale].value = value.frequencyScale;
                values[(int)Index.Seed].intValue = value.seed;
            }
        }

        public TransformDataCameraShake()
        {
        }
    }
}
