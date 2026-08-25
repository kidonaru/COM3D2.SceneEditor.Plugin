using System.Collections.Generic;
using System.Linq;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// メイド 1 人分の表情モーフ変更追跡。ユーザーが明示的に編集した (=チェック済みの) モーフ生名を持つ。
    /// プリセット保存の対象選別とタイムラインのボーンメニュー絞り込みの共通ソース。
    /// 編集側 (FaceMorphDef) とタイムライン側 (FaceMorphUtils) でモーフ名テーブルが別物のため、
    /// 両者が共通に扱える生名文字列をキーにする
    /// </summary>
    public class FaceEditStore
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
            _modifiedNames.Clear();
            if (names != null)
            {
                foreach (var name in names)
                {
                    if (!string.IsNullOrEmpty(name))
                    {
                        _modifiedNames.Add(name);
                    }
                }
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
