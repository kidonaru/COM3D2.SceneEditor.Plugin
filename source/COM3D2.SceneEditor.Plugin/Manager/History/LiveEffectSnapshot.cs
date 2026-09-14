using System.Collections.Generic;
using System.Linq;
using COM3D2.MotionTimelineEditor;
using COM3D2.MotionTimelineEditor.Plugin;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>ステージライト 1 灯のパラメータ (StageLight の可変フィールド全部)</summary>
    public class LiveEffectStageLightState
    {
        public bool visible = true;
        public Vector3 position;
        public Vector3 eulerAngles;
        public Color color = Color.white;
        public StageLightInfo lightInfo = new StageLightInfo();
    }

    /// <summary>ステージライトのコントローラー (一括設定) と配下のライト</summary>
    public class LiveEffectStageLightControllerState
    {
        public bool autoVisible;
        public bool visible = true;
        public bool autoPosition;
        public Vector3 positionMin;
        public Vector3 positionMax;
        public bool autoRotation;
        public Vector3 rotationMin;
        public Vector3 rotationMax;
        public bool autoColor;
        public Color colorMin = Color.white;
        public Color colorMax = Color.white;
        public bool autoLightInfo;
        public StageLightInfo lightInfo = new StageLightInfo();
        public StageLightController.PatternType patternType = StageLightController.PatternType.None;
        public float patternCycleTime = 5f;
        public List<LiveEffectStageLightState> lights = new List<LiveEffectStageLightState>();
    }

    /// <summary>ステージレーザー 1 本のパラメータ (StageLaser の可変フィールド全部)</summary>
    public class LiveEffectStageLaserState
    {
        public bool visible = true;
        public Vector3 eulerAngles;
        public Color color1 = Color.white;
        public Color color2 = Color.white;
        public float intensity = 1f;
        public float laserRange = 13f;
        public float laserWidth = 0.05f;
        public float falloffExp = 0.2f;
        public float noiseStrength = 0.2f;
        public float noiseScale = 5f;
        public float coreRadius;
        public float offsetRange;
        public float glowWidth = 0.1f;
        public int segmentRange = 10;
        public bool zTest = true;
    }

    /// <summary>ステージレーザーのコントローラー (一括設定) と配下のレーザー</summary>
    public class LiveEffectStageLaserControllerState
    {
        public Vector3 position;
        public Vector3 eulerAngles;
        public bool autoVisible = true;
        public bool visible = true;
        public bool autoRotation = true;
        public Vector3 rotationMin;
        public Vector3 rotationMax;
        public bool autoColor = true;
        public Color color1 = Color.white;
        public Color color2 = Color.white;
        public bool autoLaserInfo = true;
        public StageLaserInfo laserInfo = new StageLaserInfo();
        public List<LiveEffectStageLaserState> lasers = new List<LiveEffectStageLaserState>();
    }

    /// <summary>サイリウムのパターン 1 件 (パターン設定 + 移動回転設定)</summary>
    public class LiveEffectPsylliumPatternState
    {
        public PsylliumPatternConfig patternConfig = new PsylliumPatternConfig();
        public PsylliumTransformConfig transformConfig = new PsylliumTransformConfig();
    }

    /// <summary>サイリウムのコントローラーと配下のエリア・パターン</summary>
    public class LiveEffectPsylliumControllerState
    {
        public bool visible = true;
        public Vector3 position;
        public Vector3 eulerAngles;
        public Vector3 scale = Vector3.one;
        public PsylliumBarConfig barConfig = new PsylliumBarConfig();
        public PsylliumHandConfig handConfig = new PsylliumHandConfig();
        public List<PsylliumAreaConfig> areas = new List<PsylliumAreaConfig>();
        public List<LiveEffectPsylliumPatternState> patterns = new List<LiveEffectPsylliumPatternState>();

        /// <summary>メッシュ配置。areaIndex で対象エリアを指す。無いエリアは矩形配置</summary>
        public List<PsylliumPlacement> placements = new List<PsylliumPlacement>();
    }

    /// <summary>
    /// ライブ演出 3 マネージャ (ステージライト / レーザー / サイリウム) の全状態。
    /// 個数も含めて持つので、追加・削除も 1 件の履歴として戻せる
    /// </summary>
    public class LiveEffectState
    {
        public List<LiveEffectStageLightControllerState> stageLightControllers
            = new List<LiveEffectStageLightControllerState>();
        public List<LiveEffectStageLaserControllerState> stageLaserControllers
            = new List<LiveEffectStageLaserControllerState>();
        public List<LiveEffectPsylliumControllerState> psylliumControllers
            = new List<LiveEffectPsylliumControllerState>();
    }

    /// <summary>
    /// ライブ演出全体のスナップショット。
    /// 3 マネージャをまとめて 1 件で持つため、対象キーはウィンドウ単位で固定 (TargetKey)。
    /// 適用は個数を合わせてから各要素へ値を書き戻す
    /// </summary>
    public class LiveEffectSnapshot : PresetDtoSnapshot<LiveEffectState>
    {
        /// <summary>BeforeEdit へ渡す対象キー。全操作で同じものを使う</summary>
        public static readonly object TargetKey = new object();

        private static StageLightManager stageLightManager => StageLightManager.instance;
        private static StageLaserManager stageLaserManager => StageLaserManager.instance;
        private static PsylliumManager psylliumManager => PsylliumManager.instance;

        public static LiveEffectSnapshot Capture()
        {
            var snapshot = new LiveEffectSnapshot();
            snapshot.Init();
            return snapshot;
        }

        /// <summary>
        /// ライブ演出の変更を履歴へ記録する。値を書く「前」に呼ぶこと。
        /// 3 マネージャをまとめて 1 スナップショットにするので対象キーは固定で、
        /// ドラッグ中の連続変更はマウス解放時に 1 件へ集約される。
        /// BeforeEdit が内部で AutoEditMode.Enter を呼ぶため、
        /// 従来の onBeforeValueChanged (AutoEditMode.Enter) の置き換えとして使える
        /// </summary>
        public static void RecordEdit(string label)
        {
            HistoryManager.instance.BeforeEdit(
                null, HistoryScope.LiveEffect, "ライブ演出: " + label, TargetKey, Capture);
        }

        protected override PresetDtoSnapshot<LiveEffectState> CreateEmpty() => new LiveEffectSnapshot();

        protected override LiveEffectState CaptureState()
        {
            var state = new LiveEffectState();

            foreach (var controller in stageLightManager.controllers)
            {
                if (controller == null) continue;

                var dto = new LiveEffectStageLightControllerState
                {
                    autoVisible = controller.autoVisible,
                    visible = controller.visible,
                    autoPosition = controller.autoPosition,
                    positionMin = controller.positionMin,
                    positionMax = controller.positionMax,
                    autoRotation = controller.autoRotation,
                    rotationMin = controller.rotationMin,
                    rotationMax = controller.rotationMax,
                    autoColor = controller.autoColor,
                    colorMin = controller.colorMin,
                    colorMax = controller.colorMax,
                    autoLightInfo = controller.autoLightInfo,
                    patternType = controller.patternType,
                    patternCycleTime = controller.patternCycleTime,
                };
                CopyLightInfo(controller.lightInfo, dto.lightInfo);

                foreach (var light in controller.lights)
                {
                    if (light == null) continue;

                    var lightDto = new LiveEffectStageLightState
                    {
                        visible = light.visible,
                        position = light.position,
                        eulerAngles = light.eulerAngles,
                        color = light.color,
                    };
                    lightDto.lightInfo.spotAngle = light.spotAngle;
                    lightDto.lightInfo.spotRange = light.spotRange;
                    lightDto.lightInfo.rangeMultiplier = light.rangeMultiplier;
                    lightDto.lightInfo.falloffExp = light.falloffExp;
                    lightDto.lightInfo.noiseStrength = light.noiseStrength;
                    lightDto.lightInfo.noiseScale = light.noiseScale;
                    lightDto.lightInfo.coreRadius = light.coreRadius;
                    lightDto.lightInfo.offsetRange = light.offsetRange;
                    lightDto.lightInfo.segmentAngle = light.segmentAngle;
                    lightDto.lightInfo.segmentRange = light.segmentRange;
                    lightDto.lightInfo.zTest = light.zTest;
                    dto.lights.Add(lightDto);
                }

                state.stageLightControllers.Add(dto);
            }

            foreach (var controller in stageLaserManager.controllers)
            {
                if (controller == null) continue;

                var dto = new LiveEffectStageLaserControllerState
                {
                    position = controller.position,
                    eulerAngles = controller.eulerAngles,
                    autoVisible = controller.autoVisible,
                    visible = controller.visible,
                    autoRotation = controller.autoRotation,
                    rotationMin = controller.rotationMin,
                    rotationMax = controller.rotationMax,
                    autoColor = controller.autoColor,
                    color1 = controller.color1,
                    color2 = controller.color2,
                    autoLaserInfo = controller.autoLaserInfo,
                };
                dto.laserInfo.CopyFrom(controller.laserInfo);

                foreach (var laser in controller.lasers)
                {
                    if (laser == null) continue;

                    dto.lasers.Add(new LiveEffectStageLaserState
                    {
                        visible = laser.visible,
                        eulerAngles = laser.eulerAngles,
                        color1 = laser.color1,
                        color2 = laser.color2,
                        intensity = laser.intensity,
                        laserRange = laser.laserRange,
                        laserWidth = laser.laserWidth,
                        falloffExp = laser.falloffExp,
                        noiseStrength = laser.noiseStrength,
                        noiseScale = laser.noiseScale,
                        coreRadius = laser.coreRadius,
                        offsetRange = laser.offsetRange,
                        glowWidth = laser.glowWidth,
                        segmentRange = laser.segmentRange,
                        zTest = laser.zTest,
                    });
                }

                state.stageLaserControllers.Add(dto);
            }

            foreach (var controller in psylliumManager.controllers)
            {
                if (controller == null) continue;

                var dto = new LiveEffectPsylliumControllerState
                {
                    visible = controller.visible,
                    position = controller.position,
                    eulerAngles = controller.eulerAngles,
                    scale = controller.scale,
                };
                dto.barConfig.CopyFrom(controller.barConfig);
                dto.handConfig.CopyFrom(controller.handConfig);

                foreach (var area in controller.areas)
                {
                    if (area == null) continue;
                    dto.areas.Add(area.areaConfig.Clone());

                    if (area.placement != null)
                    {
                        var placement = area.placement.Clone();
                        placement.areaIndex = area.index;
                        dto.placements.Add(placement);
                    }
                }

                foreach (var pattern in controller.patterns)
                {
                    if (pattern == null) continue;

                    var patternDto = new LiveEffectPsylliumPatternState();
                    patternDto.patternConfig.CopyFrom(pattern.patternConfig);
                    patternDto.transformConfig.CopyFrom(pattern.transformConfig);
                    dto.patterns.Add(patternDto);
                }

                state.psylliumControllers.Add(dto);
            }

            return state;
        }

        protected override void ApplyState(LiveEffectState state)
        {
            if (state == null) return;

            ApplyStageLights(state);
            ApplyStageLasers(state);
            ApplyPsylliums(state);
        }

        private static void ApplyStageLights(LiveEffectState state)
        {
            // 個数を先に合わせる (SetupLights が不足分の追加と余剰の削除をまとめて行う)
            var counts = new List<int>();
            foreach (var dto in state.stageLightControllers)
            {
                counts.Add(dto.lights.Count);
            }
            stageLightManager.SetupLights(counts);

            var controllers = stageLightManager.controllers;
            for (var i = 0; i < controllers.Count && i < state.stageLightControllers.Count; i++)
            {
                var controller = controllers[i];
                var dto = state.stageLightControllers[i];
                if (controller == null) continue;

                controller.autoVisible = dto.autoVisible;
                controller.visible = dto.visible;
                controller.autoPosition = dto.autoPosition;
                controller.positionMin = dto.positionMin;
                controller.positionMax = dto.positionMax;
                controller.autoRotation = dto.autoRotation;
                controller.rotationMin = dto.rotationMin;
                controller.rotationMax = dto.rotationMax;
                controller.autoColor = dto.autoColor;
                controller.colorMin = dto.colorMin;
                controller.colorMax = dto.colorMax;
                controller.autoLightInfo = dto.autoLightInfo;
                controller.patternType = dto.patternType;
                controller.patternCycleTime = dto.patternCycleTime;
                CopyLightInfo(dto.lightInfo, controller.lightInfo);

                for (var k = 0; k < controller.lights.Count && k < dto.lights.Count; k++)
                {
                    var light = controller.lights[k];
                    var lightDto = dto.lights[k];
                    if (light == null) continue;

                    light.visible = lightDto.visible;
                    light.position = lightDto.position;
                    light.eulerAngles = lightDto.eulerAngles;
                    light.color = lightDto.color;
                    light.spotAngle = lightDto.lightInfo.spotAngle;
                    light.spotRange = lightDto.lightInfo.spotRange;
                    light.rangeMultiplier = lightDto.lightInfo.rangeMultiplier;
                    light.falloffExp = lightDto.lightInfo.falloffExp;
                    light.noiseStrength = lightDto.lightInfo.noiseStrength;
                    light.noiseScale = lightDto.lightInfo.noiseScale;
                    light.coreRadius = lightDto.lightInfo.coreRadius;
                    light.offsetRange = lightDto.lightInfo.offsetRange;
                    light.segmentAngle = lightDto.lightInfo.segmentAngle;
                    light.segmentRange = lightDto.lightInfo.segmentRange;
                    light.zTest = lightDto.lightInfo.zTest;
                }
            }

            stageLightManager.UpdateLights();
        }

        private static void ApplyStageLasers(LiveEffectState state)
        {
            var counts = new List<int>();
            foreach (var dto in state.stageLaserControllers)
            {
                counts.Add(dto.lasers.Count);
            }
            stageLaserManager.SetupLasers(counts);

            var controllers = stageLaserManager.controllers;
            for (var i = 0; i < controllers.Count && i < state.stageLaserControllers.Count; i++)
            {
                var controller = controllers[i];
                var dto = state.stageLaserControllers[i];
                if (controller == null) continue;

                controller.position = dto.position;
                controller.eulerAngles = dto.eulerAngles;
                controller.autoVisible = dto.autoVisible;
                controller.visible = dto.visible;
                controller.autoRotation = dto.autoRotation;
                controller.rotationMin = dto.rotationMin;
                controller.rotationMax = dto.rotationMax;
                controller.autoColor = dto.autoColor;
                controller.color1 = dto.color1;
                controller.color2 = dto.color2;
                controller.autoLaserInfo = dto.autoLaserInfo;
                controller.laserInfo.CopyFrom(dto.laserInfo);

                for (var k = 0; k < controller.lasers.Count && k < dto.lasers.Count; k++)
                {
                    var laser = controller.lasers[k];
                    var laserDto = dto.lasers[k];
                    if (laser == null) continue;

                    laser.visible = laserDto.visible;
                    laser.eulerAngles = laserDto.eulerAngles;
                    laser.color1 = laserDto.color1;
                    laser.color2 = laserDto.color2;
                    laser.intensity = laserDto.intensity;
                    laser.laserRange = laserDto.laserRange;
                    laser.laserWidth = laserDto.laserWidth;
                    laser.falloffExp = laserDto.falloffExp;
                    laser.noiseStrength = laserDto.noiseStrength;
                    laser.noiseScale = laserDto.noiseScale;
                    laser.coreRadius = laserDto.coreRadius;
                    laser.offsetRange = laserDto.offsetRange;
                    laser.glowWidth = laserDto.glowWidth;
                    laser.segmentRange = laserDto.segmentRange;
                    laser.zTest = laserDto.zTest;
                }
            }

            stageLaserManager.UpdateLasers();
        }

        private static void ApplyPsylliums(LiveEffectState state)
        {
            var datas = new List<TimelinePsylliumData>();
            foreach (var dto in state.psylliumControllers)
            {
                datas.Add(new TimelinePsylliumData
                {
                    areaCount = dto.areas.Count,
                    patternCount = dto.patterns.Count,
                    placements = dto.placements.Select(p => p.Clone()).ToList(),
                });
            }
            psylliumManager.Setup(datas);

            var controllers = psylliumManager.controllers;
            for (var i = 0; i < controllers.Count && i < state.psylliumControllers.Count; i++)
            {
                var controller = controllers[i];
                var dto = state.psylliumControllers[i];
                if (controller == null) continue;

                controller.visible = dto.visible;
                controller.position = dto.position;
                controller.eulerAngles = dto.eulerAngles;
                controller.scale = dto.scale;
                controller.barConfig.CopyFrom(dto.barConfig);
                controller.handConfig.CopyFrom(dto.handConfig);

                for (var k = 0; k < controller.areas.Count && k < dto.areas.Count; k++)
                {
                    var area = controller.areas[k];
                    if (area == null) continue;
                    area.areaConfig.CopyFrom(dto.areas[k], false);
                    area.refreshRequired = true;
                }

                for (var k = 0; k < controller.patterns.Count && k < dto.patterns.Count; k++)
                {
                    var pattern = controller.patterns[k];
                    if (pattern == null) continue;
                    pattern.patternConfig.CopyFrom(dto.patterns[k].patternConfig);
                    pattern.transformConfig.CopyFrom(dto.patterns[k].transformConfig);
                }

                controller.refreshRequired = true;
            }
        }

        /// <summary>StageLightInfo は CopyFrom を持たないため、ここで値を移す</summary>
        private static void CopyLightInfo(StageLightInfo src, StageLightInfo dst)
        {
            dst.spotAngle = src.spotAngle;
            dst.spotRange = src.spotRange;
            dst.rangeMultiplier = src.rangeMultiplier;
            dst.falloffExp = src.falloffExp;
            dst.noiseStrength = src.noiseStrength;
            dst.noiseScale = src.noiseScale;
            dst.coreRadius = src.coreRadius;
            dst.offsetRange = src.offsetRange;
            dst.segmentAngle = src.segmentAngle;
            dst.segmentRange = src.segmentRange;
            dst.zTest = src.zTest;
        }
    }
}
