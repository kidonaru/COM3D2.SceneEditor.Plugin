using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// ライトのスナップショット (メインライトと追加ライト一覧)。
    /// 追加ライトは一覧丸ごとで扱うため、追加・削除もそのまま復元できる
    /// </summary>
    public class LightSnapshot : IStateSnapshot
    {
        private ScenePresetLight _state;

        public static LightSnapshot Capture()
        {
            return new LightSnapshot { _state = CaptureState() };
        }

        /// <summary>
        /// メインライトと追加ライトの状態を記録する。
        /// メインライトが取れないシーンでも追加ライトだけは記録する (hasMain で区別)
        /// </summary>
        public static ScenePresetLight CaptureState()
        {
            var lightManager = StudioLightManager.instance;
            var lightMain = lightManager.mainLight;
            var mainLight = lightMain != null ? lightMain.GetComponent<Light>() : null;

            var state = new ScenePresetLight();
            if (mainLight != null)
            {
                state.hasMain = true;
                state.mainRotation = mainLight.transform.eulerAngles;
                state.mainColor = mainLight.color;
                state.mainIntensity = mainLight.intensity;
                state.mainShadowStrength = mainLight.shadowStrength;
            }

            foreach (var light in lightManager.lights)
            {
                if (light == null)
                {
                    continue;
                }
                state.additionalLights.Add(new ScenePresetAdditionalLight
                {
                    type = (int)light.type,
                    position = light.transform.position,
                    rotation = light.transform.eulerAngles,
                    color = light.color,
                    intensity = light.intensity,
                    range = light.range,
                    spotAngle = light.spotAngle,
                    enabled = light.enabled,
                });
            }

            return state;
        }

        /// <summary>
        /// ライトを復元する。state == null なら触らない。
        /// 追加ライトは既存の灯を使い回して差分適用する。全消去して作り直すと、
        /// Inspector・ギズモが Object スコープで積んだ Transform 参照が失効し、
        /// 該当エントリが無言でスキップされる (履歴の一部が黙って消える)
        /// </summary>
        public static void ApplyState(ScenePresetLight state)
        {
            if (state == null)
            {
                return;
            }

            var lightManager = StudioLightManager.instance;
            var lightMain = lightManager.mainLight;
            var mainLight = lightMain != null ? lightMain.GetComponent<Light>() : null;
            // 記録時にメインライトを取れていない場合は適用しない
            if (state.hasMain && mainLight != null)
            {
                lightMain.SetRotation(state.mainRotation);
                lightMain.SetColor(state.mainColor);
                lightMain.SetIntensity(state.mainIntensity);
                lightMain.SetShadowStrength(state.mainShadowStrength);
            }

            var lights = lightManager.lights;

            // 余った灯を末尾から消す。消す灯が選択中なら Inspector に残さない
            while (lights.Count > state.additionalLights.Count)
            {
                var last = lights[lights.Count - 1];
                if (last != null
                    && SelectionManager.instance.selectedObject == last.gameObject)
                {
                    SelectionManager.instance.Select(null);
                }
                lightManager.RemoveLight(last);
            }

            while (lights.Count < state.additionalLights.Count)
            {
                lightManager.AddLight();
            }

            for (var i = 0; i < state.additionalLights.Count; i++)
            {
                ApplyLightState(lights[i], state.additionalLights[i]);
            }
        }

        /// <summary>追加ライト 1 灯へ記録値を反映する</summary>
        private static void ApplyLightState(Light light, ScenePresetAdditionalLight lightState)
        {
            if (light == null)
            {
                return;
            }

            StudioLightManager.instance.SetLightType(light, (LightType)lightState.type);
            light.transform.position = lightState.position;
            light.transform.eulerAngles = lightState.rotation;
            light.color = lightState.color;
            light.intensity = lightState.intensity;
            light.range = lightState.range;
            light.spotAngle = lightState.spotAngle;
            light.enabled = lightState.enabled;
        }

        public void AddBones(IEnumerable<Transform> targetBones)
        {
        }

        public IStateSnapshot CaptureCurrent() => Capture();

        public void Apply(Maid maid) => ApplyState(_state);

        public bool Approximately(IStateSnapshot other)
        {
            var o = other as LightSnapshot;
            if (o == null || _state == null || o._state == null)
            {
                return false;
            }

            if (_state.hasMain != o._state.hasMain
                || _state.mainRotation != o._state.mainRotation
                || _state.mainColor != o._state.mainColor
                || !Mathf.Approximately(_state.mainIntensity, o._state.mainIntensity)
                || !Mathf.Approximately(_state.mainShadowStrength, o._state.mainShadowStrength)
                || _state.additionalLights.Count != o._state.additionalLights.Count)
            {
                return false;
            }

            for (var i = 0; i < _state.additionalLights.Count; i++)
            {
                var a = _state.additionalLights[i];
                var b = o._state.additionalLights[i];
                if (a.type != b.type
                    || a.enabled != b.enabled
                    || a.position != b.position
                    || a.rotation != b.rotation
                    || a.color != b.color
                    || !Mathf.Approximately(a.intensity, b.intensity)
                    || !Mathf.Approximately(a.range, b.range)
                    || !Mathf.Approximately(a.spotAngle, b.spotAngle))
                {
                    return false;
                }
            }
            return true;
        }

        public bool CanApply(Maid maid) => _state != null;
    }
}
