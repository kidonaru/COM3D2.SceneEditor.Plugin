using System.Collections.Generic;
using COM3D2.SceneEditor.Plugin;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    public class TransformDataPsylliumTransform : TransformDataBase
    {
        public static TransformDataPsylliumTransform defaultTrans = new TransformDataPsylliumTransform();
        public static PsylliumTransformConfig defaultConfig = new PsylliumTransformConfig();

        public override TransformType type => TransformType.PsylliumTransform;

        public override int valueCount => 14;

        public override bool hasPosition => true;
        public override bool hasSubPosition => true;
        public override bool hasRotation => true;
        public override bool hasSubRotation => true;
        public override bool hasTangent => true;

        public override ValueData[] positionValues
        {
            get => new ValueData[] { values[0], values[1], values[2] };
        }

        public override ValueData[] subPositionValues
        {
            get => new ValueData[] { values[3], values[4], values[5] };
        }

        public override ValueData[] rotationValues
        {
            get => new ValueData[] { values[6], values[7], values[8], values[9] };
        }

        public override ValueData[] subRotationValues
        {
            get => new ValueData[] { values[10], values[11], values[12], values[13] };
        }

        public override ValueData[] tangentValues => values;

        public override Vector3 initialPosition => defaultConfig.positionLeft;
        public override Vector3 initialSubPosition => defaultConfig.positionRight;
        public override Vector3 initialEulerAngles => defaultConfig.eulerAnglesLeft;
        public override Vector3 initialSubEulerAngles => defaultConfig.eulerAnglesRight;
        public override Quaternion initialRotation
            => QuaternionUtils.EulerToQuaternion(initialEulerAngles);
        public override Quaternion initialSubRotation
            => QuaternionUtils.EulerToQuaternion(initialSubEulerAngles);

        public TransformDataPsylliumTransform()
        {
        }

        public void FromConfig(PsylliumTransformConfig config)
        {
            position = config.positionLeft;
            subPosition = config.positionRight;
            eulerAngles = config.eulerAnglesLeft;
            subEulerAngles = config.eulerAnglesRight;
        }

        private PsylliumTransformConfig _config = new PsylliumTransformConfig();

        public PsylliumTransformConfig ToConfig()
        {
            _config.positionLeft = position;
            _config.positionRight = subPosition;
            _config.eulerAnglesLeft = eulerAngles;
            _config.eulerAnglesRight = subEulerAngles;
            return _config;
        }
    }
}