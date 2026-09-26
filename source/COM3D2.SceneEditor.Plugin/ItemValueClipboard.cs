using System.Collections.Generic;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// Inspector のキーフレーム・レイヤー項目の値のコピー & ペースト用クリップボード。
    /// (名前, 値) の組を 1 件以上持ち、キーフレームとレイヤー項目で共有する。
    /// タイムラインの「フレームコピー」(システムクリップボードの XML) とは別にプロセス内だけで保持する
    /// </summary>
    public static class ItemValueClipboard
    {
        private static readonly List<string> _names = new List<string>();
        private static readonly List<MTEP.TransformType> _types = new List<MTEP.TransformType>();
        private static readonly List<MTEP.ITransformData> _transforms = new List<MTEP.ITransformData>();

        public static bool hasData => _transforms.Count > 0;

        /// <summary>値を記録する。元データを後から書き換えられても影響しないよう複製して持つ</summary>
        public static void Set(IList<string> names, IList<MTEP.ITransformData> transforms)
        {
            if (names == null || transforms == null || transforms.Count == 0
                || names.Count != transforms.Count)
            {
                return;
            }

            _names.Clear();
            _types.Clear();
            _transforms.Clear();
            for (var i = 0; i < transforms.Count; i++)
            {
                var transform = transforms[i];
                _names.Add(names[i]);
                _types.Add(transform.type);
                _transforms.Add(transform.Clone());
            }
        }

        /// <summary>
        /// 貼り付け先 1 件に当てる値。当たらなければ null。
        /// クリップボード内の実体を返すので、書き換えずに写し元としてだけ使うこと
        /// </summary>
        public static MTEP.ITransformData Resolve(string targetName, MTEP.TransformType targetType)
        {
            var index = ResolveIndex(_names, _types, targetName, targetType);
            return index >= 0 ? _transforms[index] : null;
        }

        /// <summary>
        /// 貼り付け先の名前を持つ複製を作る。当たらなければ null。
        /// 直接適用 (ApplyTransformDirect) は値の name を当て先に使うため、
        /// 別名へ写すときは写し元の名前のままでは写し元自身へ書き戻してしまう
        /// </summary>
        public static MTEP.ITransformData CreateFor(string targetName, MTEP.TransformType targetType)
        {
            var source = Resolve(targetName, targetType);
            if (source == null)
            {
                return null;
            }

            // Initialize は型によって既定値を入れ直すので、名前を変えた後に値を写し直す
            var transform = source.Clone();
            transform.Initialize(targetName);
            transform.FromTransformData(source);
            return transform;
        }

        public static bool CanResolve(string targetName, MTEP.TransformType targetType)
        {
            return ResolveIndex(_names, _types, targetName, targetType) >= 0;
        }

        /// <summary>
        /// 貼り付け先に当てる組の添字。名前と型が一致する組を優先し、
        /// 無ければ 1 件だけのときに限り型が同じなら使う (別のボーン・モーフへ写す用途。衣装は除く)。
        /// 型が同じなら値の並びも同じなので FromTransformData で安全に写せる
        /// </summary>
        public static int ResolveIndex(
            IList<string> names, IList<MTEP.TransformType> types,
            string targetName, MTEP.TransformType targetType)
        {
            for (var i = 0; i < names.Count; i++)
            {
                if (names[i] == targetName && types[i] == targetType)
                {
                    return i;
                }
            }

            if (types.Count == 1 && types[0] == targetType && AllowsCrossNamePaste(targetType))
            {
                return 0;
            }
            return -1;
        }

        /// <summary>
        /// 別名の項目へ写してよい型か。衣装は項目名 (スロット) と値 (menu) が対応しており、
        /// 別スロットへ写すと上衣がスカート枠に入るなど装着先と中身が食い違う
        /// </summary>
        private static bool AllowsCrossNamePaste(MTEP.TransformType type)
        {
            return type != MTEP.TransformType.Dress;
        }
    }
}
