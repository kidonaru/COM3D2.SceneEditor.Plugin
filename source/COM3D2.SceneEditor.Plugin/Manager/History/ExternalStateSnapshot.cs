using System;
using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 外部プラグインの状態を文字列 1 本で持つスナップショット (HistoryAPI.BeforeEdit 用)。
    /// 捕捉・復元の中身は外部のデリゲートに委ね、こちらは前後比較と適用可否だけを担う。
    /// 前後比較は文字列の完全一致 (外部側の直列化が決定的である前提)
    /// </summary>
    public class ExternalStateSnapshot : IStateSnapshot
    {
        private readonly string _state;
        private readonly Func<string> _capture;
        private readonly Action<string> _apply;
        private readonly Func<bool> _canApply;

        private ExternalStateSnapshot(
            string state, Func<string> capture, Action<string> apply, Func<bool> canApply)
        {
            _state = state;
            _capture = capture;
            _apply = apply;
            _canApply = canApply;
        }

        /// <summary>現在の状態を捕捉する。捕捉が null を返した (記録できない) ときは null</summary>
        public static ExternalStateSnapshot Capture(
            Func<string> capture, Action<string> apply, Func<bool> canApply)
        {
            var state = capture();
            if (state == null)
            {
                return null;
            }
            return new ExternalStateSnapshot(state, capture, apply, canApply);
        }

        /// <summary>ボーンを持たないスコープなので何もしない</summary>
        public void AddBones(IEnumerable<Transform> targetBones)
        {
        }

        public IStateSnapshot CaptureCurrent()
        {
            // 確定時に捕捉できなくても before 側の対象集合は変わらないので、
            // null のままにせず「変化なし」扱いへ倒して無変化エントリとして捨てさせる
            return Capture(_capture, _apply, _canApply) ?? this;
        }

        public void Apply(Maid maid)
        {
            _apply(_state);
        }

        public bool Approximately(IStateSnapshot other)
        {
            var snapshot = other as ExternalStateSnapshot;
            return snapshot != null && snapshot._state == _state;
        }

        public bool CanApply(Maid maid)
        {
            return _canApply == null || _canApply();
        }
    }
}
