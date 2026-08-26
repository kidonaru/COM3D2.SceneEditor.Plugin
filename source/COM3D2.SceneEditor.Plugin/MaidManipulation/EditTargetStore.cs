using System.Collections.Generic;
using System.Linq;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 編集対象 1 つ分(メイド 1 人・モデル 1 体など)の変更追跡。
    /// ユーザーが明示的に編集した (=チェック済みの) 項目名を持つ。
    /// プリセット保存の対象選別とタイムラインのボーンメニュー絞り込みの共通ソース。
    /// 編集側とタイムライン側で名前テーブルが分かれても共通に扱えるよう、生名文字列をキーにする
    /// </summary>
    public class EditTargetStore
    {
        private readonly HashSet<string> _modifiedNames = new HashSet<string>();

        /// <summary>変更検知用。集合が実際に変わったときだけ増える (タイムラインのメニュー再構築判定に使う)</summary>
        public int version { get; private set; }

        public bool isEmpty => _modifiedNames.Count == 0;

        public bool IsModified(string name)
        {
            return name != null && _modifiedNames.Contains(name);
        }

        public void Mark(string name)
        {
            if (!string.IsNullOrEmpty(name) && _modifiedNames.Add(name))
            {
                version++;
            }
        }

        public void Unmark(string name)
        {
            if (name != null && _modifiedNames.Remove(name))
            {
                version++;
            }
        }

        /// <summary>集合を丸ごと置き換える。プリセット適用と履歴復元に使う</summary>
        public void SetNames(IEnumerable<string> names)
        {
            var newNames = new HashSet<string>();
            if (names != null)
            {
                foreach (var name in names)
                {
                    if (!string.IsNullOrEmpty(name))
                    {
                        newNames.Add(name);
                    }
                }
            }

            // 同じ集合への undo/redo やプリセット再適用で version が無駄に進むと、
            // タイムライン側が毎回メニューを再構築するため、中身が変わったときだけ進める
            if (_modifiedNames.SetEquals(newNames))
            {
                return;
            }

            _modifiedNames.Clear();
            foreach (var name in newNames)
            {
                _modifiedNames.Add(name);
            }
            version++;
        }

        /// <summary>列挙中の変更に耐えるようコピーを返す</summary>
        public List<string> GetNames()
        {
            return _modifiedNames.ToList();
        }

        public void Clear()
        {
            if (_modifiedNames.Count > 0)
            {
                _modifiedNames.Clear();
                version++;
            }
        }
    }
}
