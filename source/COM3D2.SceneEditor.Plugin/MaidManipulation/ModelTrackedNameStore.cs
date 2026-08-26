using System;
using System.Collections.Generic;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// モデルごとの記録を「モデル修飾名の集合」へ集約する読み取り専用ビュー。
    /// タイムライン側の候補名はモデル修飾名だが、修飾名は group 振り直し
    /// (ModelHackManager.FixGroup) で変わりうるため、生名の記録を正とし
    /// 修飾名はここで毎回組み直す。
    /// Unity 型に依存しないよう対象キーはジェネリックにしてある (テスト可能性のため)
    /// </summary>
    public class ModelTrackedNameStore<TKey>
    {
        /// <summary>名前を解決できないモデルを取りに行く間隔 (フレーム)</summary>
        public const int RetryInterval = 30;

        private readonly EditTargetStore _store = new EditTargetStore();

        /// <summary>集約結果。タイムラインレイヤーの trackedStore として渡す</summary>
        public EditTargetStore store => _store;

        // 作り直し判定。世代は「対象集合そのものが入れ替わった」ことを表し、
        // version 合計は「各モデルの記録が変わった」ことを表す。
        // 世代が同じなら各 version は単調増加なので、合計の一致で変化なしと判定できる
        private int _generation;
        private int _lastGeneration = -1;
        private int _lastVersionSum = -1;

        // 名前を解決できなかったモデルが前回の集約に残っていたか
        private bool _hasUnresolved;
        private int _frameCount;

        // 毎フレームの集約でリストを作り直さないよう使い回す
        private readonly List<string> _names = new List<string>();

        /// <summary>
        /// 次の Sync で必ず作り直させる。
        /// モデルの追加・削除ではモデル名が振り直されるが、それは記録側の version を動かさないため、
        /// 一覧が変わった契機ではこれを呼ぶ必要がある
        /// </summary>
        public void Invalidate()
        {
            _generation++;
        }

        /// <summary>集約を空にして判定状態も初期化する (シーン遷移用)</summary>
        public void Clear()
        {
            _store.Clear();
            _generation++;
            _lastGeneration = -1;
            _lastVersionSum = -1;
            _hasUnresolved = false;
        }

        /// <summary>
        /// 毎フレーム呼ぶ。前回から変化が無ければ何もしない。
        /// tryCollect はそのモデルの修飾名を result へ積む。名前を解決できなければ false を返すこと
        /// (タイムライン未ロードなど。解決できるようになるまで RetryInterval ごとに再試行する)
        /// </summary>
        public void Sync(
            IList<TKey> keys,
            Func<TKey, int> getVersion,
            Func<TKey, List<string>, bool> tryCollect)
        {
            var versionSum = 0;
            for (var i = 0; i < keys.Count; i++)
            {
                versionSum += getVersion(keys[i]);
            }

            var changed = _generation != _lastGeneration || versionSum != _lastVersionSum;

            _frameCount++;
            if (!changed && !(_hasUnresolved && _frameCount >= RetryInterval))
            {
                return;
            }
            _frameCount = 0;
            _lastGeneration = _generation;
            _lastVersionSum = versionSum;

            _hasUnresolved = false;
            _names.Clear();
            for (var i = 0; i < keys.Count; i++)
            {
                if (!tryCollect(keys[i], _names))
                {
                    // 記録は呼び出し側に残るので、解決できるようになれば次の再試行で復帰する
                    _hasUnresolved = true;
                }
            }

            // EditTargetStore.SetNames は中身が変わったときだけ version を進めるため、
            // 集約し直してもタイムライン側のメニュー組み直しまでは連鎖しない
            _store.SetNames(_names);
        }
    }
}
