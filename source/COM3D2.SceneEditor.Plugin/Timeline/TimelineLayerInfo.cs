using System;
using System.Reflection;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// タイムラインのレイヤーカテゴリ。カテゴリモードの表示範囲とカテゴリコンボの並び順に使う。
    /// 各レイヤーの所属は TimelineLayerDesc の Category で明示する。priority の数値帯とは独立で、
    /// 同じ番号帯に複数カテゴリが混在することもある (Camera は 20/25、Model は 21〜24)
    /// </summary>
    public enum TimelineLayerCategory
    {
        Maid,
        Camera,
        Model,
        Background,
        Effect,
        Other,
    }

    [AttributeUsage(AttributeTargets.Class)]
    public class TimelineLayerDescAttribute : Attribute
    {
        public string DisplayName { get; set; }
        public int Priority { get; set; }
        public TimelineLayerCategory Category { get; set; }

        /// <summary>
        /// レイヤー削除時に「レイヤーが生まれた時点」のシーン状態へ戻すか。
        /// 適用が再生トリガや非同期の着替えを起こすレイヤーは false にする
        /// </summary>
        public bool CanRestoreOnRemove { get; set; } = true;

        public TimelineLayerDescAttribute(string displayName, int priority, TimelineLayerCategory category)
        {
            DisplayName = displayName;
            Priority = priority;
            Category = category;
        }
    }

    public class TimelineLayerInfo
    {
        public int index;
        public readonly int priority;
        public readonly TimelineLayerCategory category;
        public readonly Type layerType;
        public readonly string className;
        public readonly string displayName;
        public readonly Func<int, ITimelineLayer> createLayer;

        public TimelineLayerInfo(
            Type layerType,
            Func<int, ITimelineLayer> createLayer)
        {
            this.layerType = layerType;
            this.createLayer = createLayer;

            className = layerType.Name;
            displayName = layerType.Name;

            var displayNameAttr = layerType.GetCustomAttribute<TimelineLayerDescAttribute>();
            if (displayNameAttr != null)
            {
                displayName = displayNameAttr.DisplayName;
                priority = displayNameAttr.Priority;
                category = displayNameAttr.Category;
            }
        }

        public bool ValidateLayer()
        {
            var validateMethod = layerType.GetMethod("ValidateLayer",
                BindingFlags.Public | BindingFlags.Static);
            if (validateMethod == null)
            {
                return true;
            }

            return (bool)validateMethod.Invoke(null, null);
        }
    }

    public class TransformInfo
    {
        public readonly TransformType type;
        public readonly Func<string, ITransformData> createTransform;

        public TransformInfo(
            TransformType type,
            Func<string, ITransformData> createTransform)
        {
            this.type = type;
            this.createTransform = createTransform;
        }
    }
}