namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 「前回の同期から変わったか」の門番。
    /// 毎フレーム走る同期処理が、変化が無いときに重い作り直しを回さないために使う。
    ///
    /// 判定材料は 2 つ:
    /// - 世代 — 対象の集合そのものが入れ替わったこと (要素の増減、名前の振り直しなど)。
    ///   呼び出し側が Invalidate() で立てる
    /// - version 合計 — 各要素の中身が変わったこと
    ///
    /// 世代を別に持つのは、要素の削除による合計の減少と別要素の増分が偶然釣り合ったときに
    /// 変化を見逃さないため。世代が同じなら対象集合も同じで、各 version は単調増加するので、
    /// 合計の一致で「変化なし」と判定してよい
    /// </summary>
    public class TrackedDirtyGate
    {
        private int _generation;
        private int _lastGeneration = -1;
        private int _lastVersionSum = -1;

        /// <summary>対象集合が入れ替わったことを伝える。次の判定は必ず「変化あり」になる</summary>
        public void Invalidate()
        {
            _generation++;
        }

        /// <summary>判定状態を初期化する (シーン遷移などの全捨て用)</summary>
        public void Reset()
        {
            _generation++;
            _lastGeneration = -1;
            _lastVersionSum = -1;
        }

        /// <summary>変化したか。状態は変えないので、実際に同期したら MarkSynced を呼ぶこと</summary>
        public bool IsChanged(int versionSum)
        {
            return _generation != _lastGeneration || versionSum != _lastVersionSum;
        }

        /// <summary>同期し終えた時点の状態を記録する</summary>
        public void MarkSynced(int versionSum)
        {
            _lastGeneration = _generation;
            _lastVersionSum = versionSum;
        }
    }
}
