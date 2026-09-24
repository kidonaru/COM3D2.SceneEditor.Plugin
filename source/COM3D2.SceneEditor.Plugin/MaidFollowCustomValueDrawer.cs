using System;
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 追従メイド / 追従点のカスタム値 (CustomValueUIType) をコンボボックスで描く。
    /// キーフレーム詳細と一括編集で共有する。
    /// GUIComboBox は開閉状態と選択ハンドラを持ち使い回せないため、
    /// 行ごと (owner × カスタム値キー) にインスタンスを保持し、
    /// 描かれなくなった行のぶんは EndFrame で捨てる
    /// </summary>
    public class MaidFollowCustomValueDrawer
    {
        /// <summary>値が混在しているとき (NaN) にコンボへ出す表示</summary>
        private const string MixedName = "混在";

        /// <summary>混在時の選択位置。どの項目も選択中に見せないため範囲外にする</summary>
        private const int MixedIndex = -1;

        /// <summary>
        /// 行を一意にするキー。owner は呼び出し側がフレームをまたいで同一にする。
        /// キーフレーム詳細は BoneData (参照の同一性)、一括は TransformType を
        /// boxing した値 (値の同一性) を渡すため、どちらでも成り立つ比較にする
        /// </summary>
        public struct RowKey : IEquatable<RowKey>
        {
            public readonly object owner;
            public readonly string customKey;

            public RowKey(object owner, string customKey)
            {
                this.owner = owner;
                this.customKey = customKey;
            }

            public bool Equals(RowKey other)
            {
                return Equals(owner, other.owner) && customKey == other.customKey;
            }

            public override bool Equals(object obj)
            {
                return obj is RowKey && Equals((RowKey)obj);
            }

            public override int GetHashCode()
            {
                var hash = owner != null ? owner.GetHashCode() : 0;
                return hash * 31 + (customKey != null ? customKey.GetHashCode() : 0);
            }
        }

        private readonly Dictionary<RowKey, GUIComboBox<MTEP.MaidCache>> _maidComboBoxes =
            new Dictionary<RowKey, GUIComboBox<MTEP.MaidCache>>();
        private readonly Dictionary<RowKey, GUIComboBox<MTEP.MaidPointType>> _pointComboBoxes =
            new Dictionary<RowKey, GUIComboBox<MTEP.MaidPointType>>();
        /// <summary>
        /// 部位コンボの選択肢。先頭の Null (設定なし) はアタッチなしの別表現になるので除く
        /// (アタッチなしはメイド側の「なし」で表す)
        /// </summary>
        private static readonly List<string> AttachPointItems = BoneUtils.AttachPointNames.GetRange(
            1, BoneUtils.AttachPointNames.Count - 1);

        private readonly Dictionary<RowKey, GUIComboBox<string>> _attachPointComboBoxes =
            new Dictionary<RowKey, GUIComboBox<string>>();

        /// <summary>このフレームで描いた行。EndFrame の掃除に使う</summary>
        private readonly HashSet<RowKey> _usedKeys = new HashSet<RowKey>();
        private readonly List<RowKey> _unusedKeys = new List<RowKey>();

        /// <summary>先頭に「なし」(null) を含む追従メイドの選択肢</summary>
        private readonly List<MTEP.MaidCache> _maidItems = new List<MTEP.MaidCache>();

        /// <summary>このカスタム値をコンボで描くか (対象は MaidSlot / MaidPoint / AttachPoint)</summary>
        public static bool IsComboValue(MTEP.CustomValueInfo info)
        {
            return info.uiType == MTEP.CustomValueUIType.MaidSlot
                || info.uiType == MTEP.CustomValueUIType.MaidPoint
                || info.uiType == MTEP.CustomValueUIType.AttachPoint;
        }

        /// <summary>
        /// コンボ 1 行を描く。値が NaN (一括編集での混在) のときは「混在」と出し、
        /// 選択されたときだけ onChanged を呼ぶ
        /// </summary>
        /// <param name="owner">行の持ち主。キーフレームなら BoneData、一括ならグループの型</param>
        public void Draw(
            GUIView view,
            object owner,
            string customKey,
            MTEP.CustomValueInfo info,
            float value,
            float labelWidth,
            float rowHeight,
            Action<float> onChanged)
        {
            var rowKey = new RowKey(owner, customKey);
            _usedKeys.Add(rowKey);

            switch (info.uiType)
            {
                case MTEP.CustomValueUIType.MaidSlot:
                    DrawMaidSlotCombo(view, rowKey, info, value, labelWidth, rowHeight, onChanged);
                    break;

                case MTEP.CustomValueUIType.MaidPoint:
                    DrawMaidPointCombo(view, rowKey, info, value, labelWidth, rowHeight, onChanged);
                    break;

                case MTEP.CustomValueUIType.AttachPoint:
                    DrawAttachPointCombo(view, rowKey, info, value, labelWidth, rowHeight, onChanged);
                    break;

                default:
                    MTEUtils.LogError(
                        "MaidFollowCustomValueDrawer: コンボで描けない uiType です " + info.uiType);
                    break;
            }
        }

        /// <summary>
        /// 描かれなかった行のコンボを捨てる。
        /// 開いているポップアップは ComboBoxPopupWindow がコンボの実体を直接持つため、
        /// 辞書から消しても描画は壊れない (再び描かれた行は実体を作り直す)
        /// </summary>
        public void EndFrame()
        {
            Sweep(_maidComboBoxes);
            Sweep(_pointComboBoxes);
            Sweep(_attachPointComboBoxes);
            _usedKeys.Clear();
        }

        /// <summary>選択が変わったときなど、保持しているコンボをまとめて捨てる</summary>
        public void Clear()
        {
            _maidComboBoxes.Clear();
            _pointComboBoxes.Clear();
            _attachPointComboBoxes.Clear();
            _usedKeys.Clear();
        }

        private void Sweep<T>(Dictionary<RowKey, GUIComboBox<T>> comboBoxes)
        {
            _unusedKeys.Clear();
            foreach (var rowKey in comboBoxes.Keys)
            {
                if (!_usedKeys.Contains(rowKey))
                {
                    _unusedKeys.Add(rowKey);
                }
            }

            foreach (var rowKey in _unusedKeys)
            {
                comboBoxes.Remove(rowKey);
            }
            _unusedKeys.Clear();
        }

        private void DrawMaidSlotCombo(
            GUIView view,
            RowKey rowKey,
            MTEP.CustomValueInfo info,
            float value,
            float labelWidth,
            float rowHeight,
            Action<float> onChanged)
        {
            GUIComboBox<MTEP.MaidCache> comboBox;
            if (!_maidComboBoxes.TryGetValue(rowKey, out comboBox))
            {
                comboBox = new GUIComboBox<MTEP.MaidCache>
                {
                    getName = MaidFollowRowDrawer.GetFollowMaidName,
                };
                _maidComboBoxes[rowKey] = comboBox;
            }

            // メイドの出入りで選択肢が変わるため毎回作り直す。
            // 中身はどの行でも同じなので、リストの実体は全コンボで共有する
            MaidFollowRowDrawer.FillFollowMaidItems(_maidItems);

            comboBox.items = _maidItems;
            comboBox.defaultName = float.IsNaN(value) ? MixedName : null;
            comboBox.currentIndex = float.IsNaN(value)
                ? MixedIndex
                : MaidFollowRowDrawer.ToFollowMaidIndex(
                    Mathf.RoundToInt(value), _maidItems.Count);
            comboBox.onSelected = (maidCache, index) =>
                onChanged(MaidFollowRowDrawer.ToFollowMaidSlotNo(index));

            LabeledComboRow.Draw(view, info.name, comboBox, labelWidth, rowHeight);
        }

        private void DrawMaidPointCombo(
            GUIView view,
            RowKey rowKey,
            MTEP.CustomValueInfo info,
            float value,
            float labelWidth,
            float rowHeight,
            Action<float> onChanged)
        {
            GUIComboBox<MTEP.MaidPointType> comboBox;
            if (!_pointComboBoxes.TryGetValue(rowKey, out comboBox))
            {
                comboBox = new GUIComboBox<MTEP.MaidPointType>
                {
                    items = MaidFollowRowDrawer.followPointItems,
                    getName = MaidFollowRowDrawer.GetFollowPointName,
                };
                _pointComboBoxes[rowKey] = comboBox;
            }

            comboBox.defaultName = float.IsNaN(value) ? MixedName : null;
            comboBox.currentIndex = float.IsNaN(value)
                ? MixedIndex
                : Mathf.Clamp(
                    Mathf.RoundToInt(value), 0, MaidFollowRowDrawer.followPointItems.Count - 1);
            comboBox.onSelected = (type, _) => onChanged((float)(int)type);

            LabeledComboRow.Draw(view, info.name, comboBox, labelWidth, rowHeight);
        }

        private void DrawAttachPointCombo(
            GUIView view,
            RowKey rowKey,
            MTEP.CustomValueInfo info,
            float value,
            float labelWidth,
            float rowHeight,
            Action<float> onChanged)
        {
            GUIComboBox<string> comboBox;
            if (!_attachPointComboBoxes.TryGetValue(rowKey, out comboBox))
            {
                comboBox = new GUIComboBox<string>
                {
                    items = AttachPointItems,
                    getName = (name, _) => name,
                };
                _attachPointComboBoxes[rowKey] = comboBox;
            }

            comboBox.defaultName = float.IsNaN(value) ? MixedName : null;
            comboBox.currentIndex = float.IsNaN(value)
                ? MixedIndex
                : ToAttachPointIndex(value);
            comboBox.onSelected = (_, index) => onChanged(ToAttachPointValue(index));

            LabeledComboRow.Draw(view, info.name, comboBox, labelWidth, rowHeight);
        }

        /// <summary>部位の値 (PhotoTransTargetObject.AttachPoint) を、Null を除いた選択肢の添字へ変換する</summary>
        public static int ToAttachPointIndex(float value)
        {
            var index = (int)Math.Round(value) - 1;
            return Math.Max(0, Math.Min(index, AttachPointItems.Count - 1));
        }

        public static float ToAttachPointValue(int index)
        {
            return index + 1;
        }
    }
}
