using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>ボーン 1 本分の Transform 値</summary>
    public struct BoneTrs
    {
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 scale;

        public bool Approximately(BoneTrs other)
        {
            return (position - other.position).sqrMagnitude < 1e-10f
                && Quaternion.Angle(rotation, other.rotation) < 0.01f
                && (scale - other.scale).sqrMagnitude < 1e-10f;
        }

        public static BoneTrs Capture(Transform t)
        {
            return new BoneTrs
            {
                position = t.localPosition,
                rotation = t.localRotation,
                scale = t.localScale,
            };
        }

        public void ApplyTo(Transform t)
        {
            t.localPosition = position;
            t.localRotation = rotation;
            t.localScale = scale;
        }
    }
}
