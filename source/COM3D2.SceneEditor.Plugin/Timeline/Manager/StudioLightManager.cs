using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    // MTE の StudioLightManager から移植したアダプタ版。
    // MTE 原本は写真モード専用の lightHackManager (studio.lightWindow) 経由でライトを操作するが、
    // SE では実体管理を SceneEditor 側の StudioLightManager (追加ライト) と
    // GameMain.Instance.MainLight (メインライト) に接続する。
    // stat の index 0 は常にメインライト、1 以降が追加ライト
    public class StudioLightManager : ManagerBase
    {
        private Dictionary<string, StudioLightStat> lightMap = new Dictionary<string, StudioLightStat>();

        public List<StudioLightStat> lights = new List<StudioLightStat>();
        public List<string> lightNames = new List<string>();

        public static event UnityAction<StudioLightStat> onLightAdded;
        public static event UnityAction<StudioLightStat> onLightRemoved;
        public static event UnityAction<StudioLightStat> onLightUpdated;

        private static StudioLightManager _instance = null;
        public static StudioLightManager instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new StudioLightManager();
                }
                return _instance;
            }
        }

        // SE ネイティブのライト管理（実体の生成・破棄・種別変更を担当）
        private static SceneEditor.Plugin.StudioLightManager seLightManager
            => SceneEditor.Plugin.StudioLightManager.instance;

        private StudioLightManager()
        {
        }

        // 写真モードの lightHackManager.lightList 相当。
        // メインライトが取得できないシーンでは空リストを返し、
        // 「index 0 = メインライト」の不変条件を崩さない（追加ライトの index ずれ・誤削除ガードを防ぐ）
        private List<StudioLightStat> BuildLightList()
        {
            var result = new List<StudioLightStat>();

            var mainLight = seLightManager.mainLight;
            var mainLightComponent = mainLight != null ? mainLight.GetComponent<Light>() : null;
            if (mainLightComponent == null)
            {
                return result;
            }
            result.Add(new StudioLightStat(mainLightComponent, mainLightComponent.transform, null, 0));

            foreach (var light in seLightManager.lights)
            {
                if (light == null)
                {
                    continue;
                }
                result.Add(new StudioLightStat(light, light.transform, null, result.Count));
            }

            return result;
        }

        public StudioLightStat GetLight(string name)
        {
            StudioLightStat light;
            if (lightMap.TryGetValue(name, out light))
            {
                return light;
            }
            return null;
        }

        public StudioLightStat GetLight(int index)
        {
            if (index < 0 || index >= lightNames.Count)
            {
                return null;
            }
            return GetLight(lightNames[index]);
        }

        private int _prevUpdateFrame = -1;

        public override void LateUpdate()
        {
            LateUpdate(false);
        }

        public void LateUpdate(bool force)
        {
            if (!force)
            {
                if (Time.frameCount < _prevUpdateFrame + 30 || currentLayer.isAnmPlaying)
                {
                    return;
                }
            }
            _prevUpdateFrame = Time.frameCount;

            var lightList = BuildLightList();

            var addedLights = new List<StudioLightStat>();
            var removedLights = new List<StudioLightStat>();
            var updatedLights = new List<StudioLightStat>();
            var refresh = false;

            foreach (var stat in lightList)
            {
                if (stat.light == null)
                {
                    MTEUtils.LogWarning("StudioLightManager: lightがありません: name={0}", stat.name);
                    continue;
                }

                StudioLightStat cachedLight;
                if (stat.index >= lights.Count)
                {
                    cachedLight = new StudioLightStat();
                    cachedLight.FromStat(stat);
                    lights.Add(cachedLight);
                    addedLights.Add(cachedLight);
                    refresh = true;
                    continue;
                }

                cachedLight = lights[stat.index];

                if (cachedLight.light != stat.light ||
                    cachedLight.transform != stat.transform ||
                    cachedLight.obj != stat.obj ||
                    cachedLight.type != stat.type)
                {
                    cachedLight.FromStat(stat);
                    updatedLights.Add(cachedLight);
                    refresh = true;
                    continue;
                }
            }

            while (lights.Count > lightList.Count)
            {
                var stat = lights[lights.Count - 1];
                lights.RemoveAt(lights.Count - 1);
                removedLights.Add(stat);
                refresh = true;
            }

            if (refresh)
            {
                lightMap.Clear();
                lightNames.Clear();

                foreach (var light in lights)
                {
                    lightMap.Add(light.name, light);
                    lightNames.Add(light.name);
                }

                MTEUtils.LogDebug("StudioLightManager: Light list updated");

                foreach (var light in lights)
                {
                    MTEUtils.LogDebug("light: type={0} displayName={1} name={2}",
                        light.type, light.displayName, light.name);
                }

                UpdateTimelineLights();
            }

            foreach (var light in addedLights)
            {
                onLightAdded?.Invoke(light);
            }

            foreach (var light in removedLights)
            {
                onLightRemoved?.Invoke(light);
            }

            foreach (var light in updatedLights)
            {
                onLightUpdated?.Invoke(light);
            }
        }

        private void UpdateTimelineLights()
        {
            if (timeline == null)
            {
                return;
            }

            var lights = this.lights;
            var timelineLights = timeline.lights;

            if (lights.Count != timelineLights.Count)
            {
                timelineLights.Clear();
                foreach (var light in lights)
                {
                    var timelineLight = new TimelineLightData(light);
                    timelineLights.Add(timelineLight);
                }
            }
            else
            {
                for (int i = 0; i < lights.Count; i++)
                {
                    var light = lights[i];
                    var timelineModel = timelineLights[i];
                    timelineModel.FromStat(light);
                }
            }
        }

        public void SetupLights(List<TimelineLightData> lightDataList)
        {
            MTEUtils.LogDebug("SetupLights: count={0}", lightDataList.Count);

            var lightList = BuildLightList();

            for (var i = 0; i < lightDataList.Count; ++i)
            {
                var lightData = lightDataList[i];
                if (i >= lightList.Count)
                {
                    var stat = CreateLightStat(lightData.type, i);
                    CreateLightInternal(stat);

                    MTEUtils.Log("Create light: type={0} displayName={1} name={2}",
                        stat.type, stat.displayName, stat.name);
                }
                else
                {
                    var stat = lightList[i];
                    var newStat = stat.Clone();
                    newStat.type = lightData.type;
                    newStat.index = i;
                    ChangeLight(newStat);
                }
            }

            foreach (var stat in lights)
            {
                if (stat.index >= lightDataList.Count)
                {
                    DeleteLightInternal(stat);

                    MTEUtils.Log("Remove light: type={0} displayName={1} name={2}",
                        stat.type, stat.displayName, stat.name);
                }
            }

            // SE のライトに写真モードの互換プレハブ構造はないため isLightCompatibilityMode は no-op

            LateUpdate(true);
        }

        public bool CanCreateLight()
        {
            return true;
        }

        public override void OnLoad()
        {
            var lightLayer = timelineManager.GetLayer(typeof(LightTimelineLayer));
            if (lightLayer != null)
            {
                SetupLights(timeline.lights);
            }
        }

        public override void OnPluginDisable()
        {
            Reset();
        }

        public void Reset()
        {
            lightMap.Clear();
            lights.Clear();
            lightNames.Clear();
            _prevUpdateFrame = -1;

            seLightManager.ClearAll();
        }

        public StudioLightStat CreateLightStat(
            LightType lightType,
            int index)
        {
            return new StudioLightStat(lightType, index);
        }

        // 追加ライトを 1 灯生成し stat の種別を適用する。stat.light は次回 LateUpdate で再収集される
        private void CreateLightInternal(StudioLightStat stat)
        {
            var newLight = seLightManager.AddLight();
            seLightManager.SetLightType(newLight, stat.type);
        }

        // メインライト (index 0) は削除不可
        private void DeleteLightInternal(StudioLightStat stat)
        {
            if (stat.index <= 0 || stat.light == null)
            {
                return;
            }
            seLightManager.RemoveLight(stat.light);
        }

        public void DeleteLight(StudioLightStat stat)
        {
            DeleteLightInternal(stat);
            LateUpdate(true);

            timelineManager.RequestHistory("ライトの削除: " + stat.displayName);
        }

        public void CreateLight(StudioLightStat stat)
        {
            CreateLightInternal(stat);
            LateUpdate(true);

            timelineManager.RequestHistory("ライトの追加: " + stat.displayName);
        }

        // メインライト (index 0) は種別変更不可
        public void ChangeLight(StudioLightStat stat)
        {
            if (stat.index <= 0 || stat.light == null)
            {
                return;
            }
            seLightManager.SetLightType(stat.light, stat.type);
        }

        // 位置・回転・色・強度等はレイヤーが stat 経由で直接書くため、ここでは可視状態のみ反映する
        public void ApplyLight(StudioLightStat stat)
        {
            if (stat.light == null)
            {
                return;
            }
            stat.light.enabled = stat.visible;
        }

        public override void OnChangedSceneLevel(Scene scene, LoadSceneMode sceneMode)
        {
            Reset();
        }
    }
}
