using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>フリーテキスト全件のスナップショット (件数含む)</summary>
    public class TextSnapshot : PresetDtoSnapshot<List<ScenePresetText>>
    {
        public static TextSnapshot Capture()
        {
            var snapshot = new TextSnapshot();
            snapshot.Init();
            return snapshot;
        }

        /// <summary>
        /// 指定テキストの回転だけ差し替えて捕捉する。
        /// 回転行 (DrawEulerAngles) は書き込みと描画が一体で変更前に割り込めないため、
        /// 呼び出し側が描画前に控えた角度をここで戻す
        /// </summary>
        public static TextSnapshot Capture(int index, Vector3 eulerAngles)
        {
            var snapshot = Capture();
            if (index >= 0 && index < snapshot.state.Count)
            {
                snapshot.state[index].rotation = eulerAngles;
            }
            return snapshot;
        }

        protected override List<ScenePresetText> CaptureState() => MteEffectsSnapshot.CaptureTexts();
        protected override void ApplyState(List<ScenePresetText> state) => MteEffectsSnapshot.ApplyTexts(state);
        protected override PresetDtoSnapshot<List<ScenePresetText>> CreateEmpty() => new TextSnapshot();
    }
}
