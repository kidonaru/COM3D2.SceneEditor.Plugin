using System;
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// メイドのシェイプキー一覧 (スロット / タグのキャッシュ・検索欄・行の列挙)。
    /// シェイプキーウィンドウのメイドタブと表情ウィンドウのシェイプキータブで共有する。
    /// 全スロットの morph 走査と GetTags() はどちらも毎フレーム回すには重いため、
    /// 対象が変わったときだけ作り直す。着替えはここから検知できないので、
    /// 呼び出し側の「更新」ボタンで ClearCache する
    /// </summary>
    public class MaidShapeKeyListView
    {
        /// <summary>検索欄の下限幅。右に要素を置いてウィンドウを縮めても潰れないようにする</summary>
        private const float MinSearchFieldWidth = 40f;

        private readonly Func<string, bool> _tagFilter;

        private readonly List<string> _slotNames = new List<string>();
        private Maid _slotNamesMaid = null;

        private List<string> _tags = new List<string>();
        private Maid _tagsMaid = null;
        private string _tagsSlotName = null;

        /// <summary>シェイプキー名の絞り込み。シェイプキーウィンドウではモデルタブとも共用する</summary>
        public string searchText = "";

        /// <param name="tagFilter">一覧に出すシェイプキーか。null なら全部出す</param>
        public MaidShapeKeyListView(Func<string, bool> tagFilter = null)
        {
            _tagFilter = tagFilter;
        }

        /// <summary>シェイプキーを持つスロットのカテゴリ名。メイドが変わったときだけ作り直す</summary>
        public List<string> GetSlotNames(Maid target)
        {
            if (_slotNamesMaid == target)
            {
                return _slotNames;
            }
            _slotNamesMaid = target;
            _slotNames.Clear();

            // COM3D2.5 の goSlot は直接列挙できないため、インデックス走査で両バージョンに対応する
            var slotCount = Mathf.Min((int) TBody.SlotID.end, target.body0.goSlot.Count);
            for (var i = 0; i < slotCount; i++)
            {
                var slot = target.body0.GetSlot(i);
                if (slot != null && slot.morph != null && slot.morph.hash.Count > 0)
                {
                    _slotNames.Add(slot.Category);
                }
            }
            return _slotNames;
        }

        /// <summary>
        /// スロットのタグ一覧 (フィルタ・並べ替え済み)。GetTags() は毎回リストを作るため、
        /// メイドかスロットが変わったときだけ作り直す (同名スロットでもメイドが違えばタグは別物)
        /// </summary>
        public List<string> GetTags(Maid target, string slotName)
        {
            if (_tagsMaid == target && _tagsSlotName == slotName)
            {
                return _tags;
            }
            _tagsMaid = target;
            _tagsSlotName = slotName;

            var slot = target.body0.GetSlot(slotName);
            var morph = slot != null ? slot.morph : null;
            _tags = morph != null
                ? BuildTagList(morph.GetTags(), _tagFilter)
                : new List<string>();
            return _tags;
        }

        /// <summary>スロット / タグ一覧のキャッシュを捨てる。「更新」ボタンから呼ぶ</summary>
        public void ClearCache()
        {
            _slotNamesMaid = null;
            _tagsMaid = null;
            _tagsSlotName = null;
        }

        /// <summary>検索欄の絞り込み判定。未入力なら素通し</summary>
        public static bool IsSearchMatched(string name, string searchText)
        {
            return string.IsNullOrEmpty(searchText)
                || name.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>フィルタを通した名前を並べ替えた新しいリスト。元の列は書き換えない</summary>
        public static List<string> BuildTagList(IEnumerable<string> tags, Func<string, bool> tagFilter)
        {
            var result = new List<string>();
            foreach (var tag in tags)
            {
                if (tagFilter == null || tagFilter(tag))
                {
                    result.Add(tag);
                }
            }
            result.Sort();
            return result;
        }

        /// <summary>シェイプキー名の検索欄。スロット / 対象の行と列を揃える</summary>
        /// <param name="trailingWidth">検索欄の後ろに置くコントロールのために空ける幅 (間隔込み)</param>
        /// <param name="drawTrailing">検索欄の後ろに置くコントロールの描画</param>
        public void DrawSearchField(
            GUIView view, float labelWidth, float rowHeight,
            float trailingWidth = 0f, Action drawTrailing = null)
        {
            view.BeginHorizontal();
            {
                view.DrawLabel("検索", labelWidth, rowHeight, style: GUIView.gsLabelRight);

                var fieldWidth = drawTrailing == null
                    ? -1f
                    : Mathf.Max(
                        view.viewRect.width - view.padding.x * 2 - labelWidth - view.margin - trailingWidth,
                        MinSearchFieldWidth);
                view.DrawTextField(searchText, fieldWidth, rowHeight, value => searchText = value);

                if (drawTrailing != null)
                {
                    drawTrailing();
                }
            }
            view.EndLayout();
        }

        /// <summary>
        /// 検索に合う編集可能なシェイプキーをスクロール内に並べる。
        /// 行の描画・履歴・変更追跡は MaidShapeKeyRowDrawer に任せる
        /// </summary>
        /// <param name="onBeforeEdit">値を書く直前に呼ぶ (表情ウィンドウの強制上書き ON)。null なら何もしない</param>
        public void DrawRows(
            GUIView view, Maid target, MTEP.MaidCache maidCache, List<string> tags,
            float rowHeight, Action onBeforeEdit = null)
        {
            view.SetEnabled(view.focusedComboBox == null);

            var matchedCount = 0;

            view.BeginScrollView();
            {
                foreach (var tag in tags)
                {
                    if (!IsSearchMatched(tag, searchText))
                    {
                        continue;
                    }

                    var blendShape = maidCache.GetBlendShape(tag);
                    if (!MaidShapeKeyRowDrawer.IsEditable(blendShape))
                    {
                        continue;
                    }

                    matchedCount++;

                    MaidShapeKeyRowDrawer.Draw(
                        view, target, maidCache, tag, blendShape, rowHeight,
                        onBeforeEdit: onBeforeEdit);
                }

                if (matchedCount == 0)
                {
                    view.DrawLabel("該当するシェイプキーがありません", -1, rowHeight);
                }
            }
            view.EndScrollView();
        }
    }
}
