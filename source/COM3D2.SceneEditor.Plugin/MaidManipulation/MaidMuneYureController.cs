using System.Collections.Generic;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 胸の揺れもの ON/OFF をメイド別・左右別に保持する。
    /// 手付けした胸が物理で上書きされないよう、揺れを止めた状態を
    /// 編集モードの出入り（＝ドラッグ点の生成・破棄）を跨いで維持するのが役割。
    /// ゲーム側 API のバージョン差もここに閉じ込める
    /// </summary>
    public class MaidMuneYureController
    {
        private class Entry
        {
            public bool yureL = true;
            public bool yureR = true;
        }

        private readonly Dictionary<Maid, Entry> _entries = new Dictionary<Maid, Entry>();

        /// <summary>揺れているか。記録が無いメイドは既定 ON として扱う</summary>
        public bool GetYure(Maid maid, bool isLeft)
        {
            Entry entry;
            if (maid == null || !_entries.TryGetValue(maid, out entry))
            {
                return true;
            }
            return isLeft ? entry.yureL : entry.yureR;
        }

        public void SetYure(Maid maid, bool isLeft, bool on)
        {
            if (maid == null || maid.body0 == null)
            {
                return;
            }

            Entry entry;
            if (!_entries.TryGetValue(maid, out entry))
            {
                entry = new Entry();
                _entries[maid] = entry;
            }

            if (isLeft)
            {
                entry.yureL = on;
            }
            else
            {
                entry.yureR = on;
            }

            Apply(maid, isLeft, on);
        }

        /// <summary>
        /// 保持している状態をボディへ焼き直す。
        /// 着替え等でボディが作り直されると揺れものが既定値に戻るため、
        /// ドラッグ点を作り直すタイミングで呼ぶ
        /// </summary>
        public void Reapply(Maid maid)
        {
            Entry entry;
            if (maid == null || maid.body0 == null || !_entries.TryGetValue(maid, out entry))
            {
                return;
            }

            Apply(maid, true, entry.yureL);
            Apply(maid, false, entry.yureR);
        }

        /// <summary>
        /// 2.5 は SetMuneYure*WithEnable が CRC 新ボディ (dbMune*) と旧ボディ (jbMune*) を
        /// 内部で振り分ける。2.0 にはこの API が無いため jbMune* を直接触る
        /// </summary>
        private static void Apply(Maid maid, bool isLeft, bool on)
        {
            var body = maid.body0;
#if COM3D25
            if (isLeft)
            {
                body.SetMuneYureLWithEnable(on);
            }
            else
            {
                body.SetMuneYureRWithEnable(on);
            }
#else
            var value = on ? 1f : 0f;
            if (isLeft)
            {
                body.MuneYureL(value);
                if (body.jbMuneL != null)
                {
                    body.jbMuneL.enabled = on;
                }
            }
            else
            {
                body.MuneYureR(value);
                if (body.jbMuneR != null)
                {
                    body.jbMuneR.enabled = on;
                }
            }
#endif
        }

        /// <summary>メイド解除時。破棄済みの Maid をキーに持ち続けない</summary>
        public void Release(Maid maid)
        {
            if (maid != null)
            {
                _entries.Remove(maid);
            }
        }

        public void Destroy()
        {
            _entries.Clear();
        }
    }
}
