using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// カメラ手ブレのパラメータ行 (位置振幅・回転振幅・周波数倍率・シード)。
    /// 編集先は CameraShakeManager.instance.shakeParams で、キー化時はそこからキーが作られる。
    /// 値変更時は他のカメラ行と同じく履歴を記録してから編集モードへ入る
    /// (揺れパラメータは CameraSnapshot が持つので Undo で戻る)
    /// </summary>
    public static class CameraShakeRowDrawer
    {
        private const float LabelWidth = 40f;

        private static readonly string[] AxisNames = { "X", "Y", "Z" };

        /// <summary>振幅スライダーの刻み。位置と回転で上限が違うため刻みも分けている</summary>
        private const float PositionStep = 0.001f;
        private const float RotationStep = 0.01f;

        /// <summary>シードのランダムボタンの幅</summary>
        private const float RandomButtonWidth = 70f;

        /// <summary>履歴に出る操作名</summary>
        private const string EditLabel = "手ブレ";

        private static MTEP.CameraShakeParams shakeParams => CameraShakeManager.instance.shakeParams;

        public static void Draw(GUIView view, float rowHeight)
        {
            var p = shakeParams;

            // 履歴を記録してから編集モードへ入る (BeforeEdit が内部で AutoEditMode.Enter を呼ぶ)
            view.BeginAutoEditMode(() => MainCameraRowDrawer.RecordCameraEdit(EditLabel));

            view.DrawLabel("位置振幅 (m)", -1, rowHeight);
            DrawAmplitudeSliders(view, p.positionAmplitude,
                MTEP.TransformDataCameraShake.MaxPositionAmplitude, PositionStep,
                SetPositionAmplitude);

            view.DrawHorizontalLine();

            view.DrawLabel("回転振幅 (度)", -1, rowHeight);
            DrawAmplitudeSliders(view, p.rotationAmplitude,
                MTEP.TransformDataCameraShake.MaxRotationAmplitude, RotationStep,
                SetRotationAmplitude);

            view.DrawHorizontalLine();

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "周波数",
                labelWidth = LabelWidth,
                width = -1,
                min = MTEP.TransformDataCameraShake.MinFrequencyScale,
                max = MTEP.TransformDataCameraShake.MaxFrequencyScale,
                step = 0.01f,
                defaultValue = 1f,
                value = p.frequencyScale,
                onChanged = SetFrequencyScale,
            });

            // 0 は位相が進まず揺れが止まる。振幅を入れても動かないので理由を出す
            if (p.frequencyScale <= 0f)
            {
                view.DrawLabel("周波数 0 では揺れません", -1, rowHeight,
                    textColor: Color.yellow);
            }

            view.BeginHorizontal();
            {
                view.DrawSliderValue(new GUIView.SliderOption
                {
                    label = "シード",
                    labelWidth = LabelWidth,
                    width = -1,
                    fieldType = FloatFieldType.Int,
                    min = 0f,
                    max = MTEP.TransformDataCameraShake.MaxSeed,
                    step = 1f,
                    defaultValue = 0f,
                    value = p.seed,
                    onChanged = value => SetSeed(Mathf.RoundToInt(value)),
                });

                if (view.DrawButton("ランダム", RandomButtonWidth, rowHeight))
                {
                    // ボタンは onBeforeValueChanged を通らないので自分で履歴を記録する
                    MainCameraRowDrawer.RecordCameraEdit(EditLabel);

                    var maxSeed = Mathf.RoundToInt(MTEP.TransformDataCameraShake.MaxSeed);
                    SetSeed(UnityEngine.Random.Range(0, maxSeed + 1));
                }
            }
            view.EndLayout();

            view.EndAutoEditMode();
        }

        /// <summary>振幅 1 種類分の XYZ スライダー。振幅は負の値に意味が無いので下限は 0</summary>
        private static void DrawAmplitudeSliders(
            GUIView view, Vector3 value, float max, float step,
            System.Action<Vector3> onChanged)
        {
            for (var i = 0; i < AxisNames.Length; i++)
            {
                // ラムダが共有しないよう軸番号をループ内で確定させる
                var axis = i;
                view.DrawSliderValue(new GUIView.SliderOption
                {
                    label = AxisNames[axis],
                    labelWidth = LabelWidth,
                    width = -1,
                    min = 0f,
                    max = max,
                    step = step,
                    defaultValue = 0f,
                    value = value[axis],
                    onChanged = newValue =>
                    {
                        var amplitude = value;
                        amplitude[axis] = Mathf.Clamp(newValue, 0f, max);
                        onChanged(amplitude);
                    },
                });
            }
        }

        private static void SetPositionAmplitude(Vector3 value)
        {
            var current = shakeParams;
            current.positionAmplitude = value;
            Apply(current);
        }

        private static void SetRotationAmplitude(Vector3 value)
        {
            var current = shakeParams;
            current.rotationAmplitude = value;
            Apply(current);
        }

        private static void SetFrequencyScale(float value)
        {
            var current = shakeParams;
            current.frequencyScale = value;
            Apply(current);
        }

        private static void SetSeed(int value)
        {
            var current = shakeParams;
            current.seed = value;
            Apply(current);
        }

        /// <summary>書き換えたパラメータをライブ値へ反映し、カメラレイヤーをアクティブにする</summary>
        private static void Apply(MTEP.CameraShakeParams value)
        {
            CameraShakeManager.instance.shakeParams = value;

            // TimelineWindow の自動追従はカメラ系レイヤーを一律で除外するため、
            // AutoEditModeHost と同じくその場で切り替える。
            // 手ブレは構図を操作しないので、カメラ系の除外を免除してよい
            TimelineWindow.FocusLayerKeepingEdit(
                typeof(MTEP.CameraTimelineLayer), 0, allowCameraLayer: true);
        }
    }
}
