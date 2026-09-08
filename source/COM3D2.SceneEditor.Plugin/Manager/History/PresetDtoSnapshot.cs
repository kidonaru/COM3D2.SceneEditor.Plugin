using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// シーンプリセット用 DTO を丸ごと持つスナップショットの基底。
    /// キャプチャと適用はシーンプリセットと共用し、無変化判定は DTO の XML 直列化で行う。
    /// 対象はマネージャのシングルトンなのでメイドとボーンには依存しない
    /// </summary>
    public abstract class PresetDtoSnapshot<T> : IStateSnapshot where T : class
    {
        protected T state;

        protected abstract T CaptureState();
        protected abstract void ApplyState(T state);
        /// <summary>CaptureCurrent 用。自身と同じ型の空インスタンスを返す</summary>
        protected abstract PresetDtoSnapshot<T> CreateEmpty();

        /// <summary>派生の static Capture から呼ぶ初期化</summary>
        protected PresetDtoSnapshot<T> Init()
        {
            state = CaptureState();
            return this;
        }

        public void AddBones(IEnumerable<Transform> targetBones)
        {
        }

        public IStateSnapshot CaptureCurrent() => CreateEmpty().Init();

        public void Apply(Maid maid) => ApplyState(state);

        public bool Approximately(IStateSnapshot other)
        {
            var o = other as PresetDtoSnapshot<T>;
            return o != null && PresetDtoUtils.AreEqual(state, o.state);
        }

        public bool CanApply(Maid maid) => state != null;
    }
}
