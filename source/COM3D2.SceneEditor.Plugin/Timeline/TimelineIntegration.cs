using COM3D2.MotionTimelineEditor;
using UnityEngine.SceneManagement;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// タイムライン機能 (MTE 移植コード) の SceneEditor への組み込み。
    /// レイヤー・Transform・マネージャの登録を一括で行う
    /// </summary>
    public static class TimelineIntegration
    {
        /// <summary>
        /// タイムライン系マネージャの更新を MTE 本体と同じガード付きフローで回す複合マネージャ。
        /// timeline 未生成時に TimelineManager.Update が defaultLayer を触って NPE するため、
        /// 個別登録ではなくこの複合マネージャ経由でのみ更新する
        /// </summary>
        private class TimelineUpdateManager : IManager
        {
            private static MTEP.StudioHackManager studioHackManager => MTEP.StudioHackManager.instance;
            private static MTEP.StudioHackBase studioHack => studioHackManager.studioHack;
            private static MTEP.MaidManager maidManager => MTEP.MaidManager.instance;
            private static MTEP.TimelineManager timelineManager => MTEP.TimelineManager.instance;

            private readonly IManager[] _managers =
            {
                MTEP.ConfigManager.instance,
                studioHackManager,
                MTEP.MaidManager.instance,
                MTEP.TimelineManager.instance,
                MTEP.StudioLightManager.instance,
                MTEP.ModelHackManager.instance,
                MTEP.StudioModelManager.instance,
                MTEP.BGModelManager.instance,
                MTEP.BGGroundManager.instance,
                MTEP.SubCameraManager.instance,
                MTEP.TimelineBundleManager.instance,
                MTEP.StageLightManager.instance,
                MTEP.StageLaserManager.instance,
                MTEP.PsylliumManager.instance,
                MTEP.PostEffectManager.instance,
                MTEP.PngObjectTimelineManager.instance,
                MTEP.TimelineFaceManager.instance,
                MTEP.TimelineSeManager.instance,
                // MovieManager は frontCamera 依存のため CameraManager より先に破棄する
                MTEP.MovieManager.instance,
                MTEP.BGMManager.instance,
                MTEP.CameraManager.instance,
                MTEP.TimelineTextManager.instance,
                MTEP.TimelineHistoryManager.instance,
                MTEP.TimelineTemplateManager.instance,
            };

            public void Init()
            {
                foreach (var manager in _managers)
                {
                    manager.Init();
                }
            }

            public void PreUpdate()
            {
            }

            /// <summary>MTE 本体 Update のガード順 (hack 選択 → メイド解決 → データ検証) を踏襲する</summary>
            private bool UpdateGuards()
            {
                studioHackManager.PreUpdate();

                if (studioHack == null || !studioHack.IsValid())
                {
                    return false;
                }

                maidManager.PreUpdate();

                if (maidManager.maid == null)
                {
                    return false;
                }

                return timelineManager.IsValidData();
            }

            public void Update()
            {
                if (!UpdateGuards())
                {
                    return;
                }

                foreach (var manager in _managers)
                {
                    manager.Update();
                }
            }

            public void LateUpdate()
            {
                if (studioHack == null || maidManager.maid == null ||
                    !timelineManager.IsValidData())
                {
                    return;
                }

                foreach (var manager in _managers)
                {
                    manager.LateUpdate();
                }
            }

            public void OnLoad()
            {
                // MTE 本体の OnLoad と同じガード。タイムライン未生成のままプラグインを
                // 有効化すると MaidManager.OnLoad が currentLayer (null) を触って NRE する
                if (studioHack == null || !studioHack.IsValid() ||
                    timelineManager.timeline == null)
                {
                    return;
                }

                foreach (var manager in _managers)
                {
                    manager.OnLoad();
                }
            }

            public void OnPluginDisable()
            {
                foreach (var manager in _managers)
                {
                    manager.OnPluginDisable();
                }
            }

            public void OnChangedSceneLevel(Scene scene, LoadSceneMode sceneMode)
            {
                // SE より後にロードされたモデル配置プラグインをここで拾う。
                // 登録済みなら何もしないので、シーン切り替えごとの負荷は無視できる
                TryRegisterModelPlacer();

                foreach (var manager in _managers)
                {
                    manager.OnChangedSceneLevel(scene, sceneMode);
                }
            }
        }

        /// <summary>モデル配置プロバイダを登録済みか。二重登録と警告の連発を防ぐ</summary>
        private static bool _modelPlacerRegistered;

        /// <summary>プロバイダ不在の警告を出したか。シーン切り替えのたびに出さないようにする</summary>
        private static bool _warnedModelPlacerMissing;

        /// <summary>
        /// モデル配置プロバイダを探し、見つかれば ModelHack とモデル系レイヤーを登録する。
        /// プラグインのロード順は保証されず、SE の初期化時点ではゲスト側が
        /// まだロードされていないことがあるため、シーン切り替えごとに呼び直せるようにしてある
        /// </summary>
        private static void TryRegisterModelPlacer()
        {
            if (_modelPlacerRegistered)
            {
                return;
            }

            // 初回の走査は Refresh の中身に関わらず行われる。
            // 2 回目以降はアセンブリが増えていなければ再走査されない
            ModelPlacerProviderRegistry.Refresh();

            var modelPlacer = ModelPlacerProviderRegistry.current;
            if (modelPlacer == null)
            {
                if (!_warnedModelPlacerMissing)
                {
                    _warnedModelPlacerMissing = true;
                    MTEUtils.LogWarning(
                        "モデル配置プロバイダが見つかりません。ModItemExplorer を導入するとタイムラインのモデル機能が使えます");
                }
                return;
            }

            _modelPlacerRegistered = true;

            MTEP.ModelHackManager.instance.Register(new MTEP.ExternalModelHack(modelPlacer));

            // モデル系レイヤーは配置をプロバイダへ委譲するため、プロバイダが見つかってから登録する
            var timelineManager = MTEP.TimelineManager.instance;
            timelineManager.RegisterLayer(
                typeof(MTEP.ModelTimelineLayer), MTEP.ModelTimelineLayer.Create);
            timelineManager.RegisterLayer(
                typeof(MTEP.ModelBoneTimelineLayer), MTEP.ModelBoneTimelineLayer.Create);
            timelineManager.RegisterLayer(
                typeof(MTEP.ModelShapeKeyTimelineLayer), MTEP.ModelShapeKeyTimelineLayer.Create);
            timelineManager.RegisterLayer(
                typeof(MTEP.ModelMaterialTimelineLayer), MTEP.ModelMaterialTimelineLayer.Create);
        }

        public static void Initialize(ManagerRegistry managerRegistry)
        {
            var timelineManager = MTEP.TimelineManager.instance;

            MTEP.StudioHackManager.instance.Register(new MTEP.SceneEditorHack());

            TryRegisterModelPlacer();

            timelineManager.RegisterLayer(
                typeof(MTEP.MotionTimelineLayer), MTEP.MotionTimelineLayer.Create);
            timelineManager.RegisterLayer(
                typeof(MTEP.CameraTimelineLayer), MTEP.CameraTimelineLayer.Create);
            timelineManager.RegisterLayer(
                typeof(MTEP.LightTimelineLayer), MTEP.LightTimelineLayer.Create);
            timelineManager.RegisterLayer(
                typeof(MTEP.EyesTimelineLayer), MTEP.EyesTimelineLayer.Create);
            timelineManager.RegisterLayer(
                typeof(MTEP.ShapeKeyTimelineLayer), MTEP.ShapeKeyTimelineLayer.Create);
            timelineManager.RegisterLayer(
                typeof(MTEP.SubCameraTimelineLayer), MTEP.SubCameraTimelineLayer.Create);
            timelineManager.RegisterLayer(
                typeof(MTEP.StageLightTimelineLayer), MTEP.StageLightTimelineLayer.Create);
            timelineManager.RegisterLayer(
                typeof(MTEP.StageLaserTimelineLayer), MTEP.StageLaserTimelineLayer.Create);
            timelineManager.RegisterLayer(
                typeof(MTEP.PsylliumTimelineLayer), MTEP.PsylliumTimelineLayer.Create);
            timelineManager.RegisterLayer(
                typeof(MTEP.PostEffectTimelineLayer), MTEP.PostEffectTimelineLayer.Create);
            timelineManager.RegisterLayer(
                typeof(MTEP.PngPlacementTimelineLayer), MTEP.PngPlacementTimelineLayer.Create);
            // モデル系レイヤーの登録は TryRegisterModelPlacer が担う
            // （プロバイダが見つかったときだけ、後から登録されることもあるため）
            timelineManager.RegisterLayer(
                typeof(MTEP.MoveTimelineLayer), MTEP.MoveTimelineLayer.Create);
            timelineManager.RegisterLayer(
                typeof(MTEP.AnimationTimelineLayer), MTEP.AnimationTimelineLayer.Create);
            timelineManager.RegisterLayer(
                typeof(MTEP.BGTimelineLayer), MTEP.BGTimelineLayer.Create);
            timelineManager.RegisterLayer(
                typeof(MTEP.BGColorTimelineLayer), MTEP.BGColorTimelineLayer.Create);
            timelineManager.RegisterLayer(
                typeof(MTEP.BGModelTimelineLayer), MTEP.BGModelTimelineLayer.Create);
            timelineManager.RegisterLayer(
                typeof(MTEP.BGModelMaterialTimelineLayer), MTEP.BGModelMaterialTimelineLayer.Create);
            timelineManager.RegisterLayer(
                typeof(MTEP.MaidMaterialTimelineLayer), MTEP.MaidMaterialTimelineLayer.Create);
            timelineManager.RegisterLayer(
                typeof(MTEP.UndressTimelineLayer), MTEP.UndressTimelineLayer.Create);
            timelineManager.RegisterLayer(
                typeof(MTEP.DressTimelineLayer), MTEP.DressTimelineLayer.Create);
            timelineManager.RegisterLayer(
                typeof(MTEP.VoiceTimelineLayer), MTEP.VoiceTimelineLayer.Create);
            timelineManager.RegisterLayer(
                typeof(MTEP.MorphTimelineLayer), MTEP.MorphTimelineLayer.Create);
            timelineManager.RegisterLayer(
                typeof(MTEP.SeTimelineLayer), MTEP.SeTimelineLayer.Create);
            timelineManager.RegisterLayer(
                typeof(MTEP.TextTimelineLayer), MTEP.TextTimelineLayer.Create);

            timelineManager.RegisterTransform(
                MTEP.TransformType.Animation,
                MTEP.TimelineManager.CreateTransform<MTEP.TransformDataAnimation>);
            timelineManager.RegisterTransform(
                MTEP.TransformType.BG,
                MTEP.TimelineManager.CreateTransform<MTEP.TransformDataBG>);
            timelineManager.RegisterTransform(
                MTEP.TransformType.BGModel,
                MTEP.TimelineManager.CreateTransform<MTEP.TransformDataBGModel>);
            timelineManager.RegisterTransform(
                MTEP.TransformType.BGColor,
                MTEP.TimelineManager.CreateTransform<MTEP.TransformDataBGColor>);
            timelineManager.RegisterTransform(
                MTEP.TransformType.BGGroundColor,
                MTEP.TimelineManager.CreateTransform<MTEP.TransformDataBGGroundColor>);
            timelineManager.RegisterTransform(
                MTEP.TransformType.SubCamera,
                MTEP.TimelineManager.CreateTransform<MTEP.TransformDataSubCamera>);
            timelineManager.RegisterTransform(
                MTEP.TransformType.StageLight,
                MTEP.TimelineManager.CreateTransform<MTEP.TransformDataStageLight>);
            timelineManager.RegisterTransform(
                MTEP.TransformType.StageLightController,
                MTEP.TimelineManager.CreateTransform<MTEP.TransformDataStageLightController>);
            timelineManager.RegisterTransform(
                MTEP.TransformType.StageLaser,
                MTEP.TimelineManager.CreateTransform<MTEP.TransformDataStageLaser>);
            timelineManager.RegisterTransform(
                MTEP.TransformType.StageLaserController,
                MTEP.TimelineManager.CreateTransform<MTEP.TransformDataStageLaserController>);
            timelineManager.RegisterTransform(
                MTEP.TransformType.PngObject,
                MTEP.TimelineManager.CreateTransform<MTEP.TransformDataPngObject>);
            timelineManager.RegisterTransform(
                MTEP.TransformType.DepthOfField,
                MTEP.TimelineManager.CreateTransform<MTEP.TransformDataDepthOfField>);
            timelineManager.RegisterTransform(
                MTEP.TransformType.DistanceFog,
                MTEP.TimelineManager.CreateTransform<MTEP.TransformDataDistanceFog>);
            timelineManager.RegisterTransform(
                MTEP.TransformType.GTToneMap,
                MTEP.TimelineManager.CreateTransform<MTEP.TransformDataGTToneMap>);
            timelineManager.RegisterTransform(
                MTEP.TransformType.Paraffin,
                MTEP.TimelineManager.CreateTransform<MTEP.TransformDataParaffin>);
            timelineManager.RegisterTransform(
                MTEP.TransformType.Rimlight,
                MTEP.TimelineManager.CreateTransform<MTEP.TransformDataRimlight>);
            timelineManager.RegisterTransform(
                MTEP.TransformType.PsylliumArea,
                MTEP.TimelineManager.CreateTransform<MTEP.TransformDataPsylliumArea>);
            timelineManager.RegisterTransform(
                MTEP.TransformType.PsylliumBar,
                MTEP.TimelineManager.CreateTransform<MTEP.TransformDataPsylliumBar>);
            timelineManager.RegisterTransform(
                MTEP.TransformType.PsylliumController,
                MTEP.TimelineManager.CreateTransform<MTEP.TransformDataPsylliumController>);
            timelineManager.RegisterTransform(
                MTEP.TransformType.PsylliumHand,
                MTEP.TimelineManager.CreateTransform<MTEP.TransformDataPsylliumHand>);
            timelineManager.RegisterTransform(
                MTEP.TransformType.PsylliumPattern,
                MTEP.TimelineManager.CreateTransform<MTEP.TransformDataPsylliumPattern>);
            timelineManager.RegisterTransform(
                MTEP.TransformType.PsylliumTransform,
                MTEP.TimelineManager.CreateTransform<MTEP.TransformDataPsylliumTransform>);
            timelineManager.RegisterTransform(
                MTEP.TransformType.Undress,
                MTEP.TimelineManager.CreateTransform<MTEP.TransformDataUndress>);
            timelineManager.RegisterTransform(
                MTEP.TransformType.Dress,
                MTEP.TimelineManager.CreateTransform<MTEP.TransformDataDress>);
            timelineManager.RegisterTransform(
                MTEP.TransformType.Model,
                MTEP.TimelineManager.CreateTransform<MTEP.TransformDataModel>);
            timelineManager.RegisterTransform(
                MTEP.TransformType.ModelBone,
                MTEP.TimelineManager.CreateTransform<MTEP.TransformDataModelBone>);
            timelineManager.RegisterTransform(
                MTEP.TransformType.ModelShapeKey,
                MTEP.TimelineManager.CreateTransform<MTEP.TransformDataModelShapeKey>);
            timelineManager.RegisterTransform(
                MTEP.TransformType.ModelMaterial,
                MTEP.TimelineManager.CreateTransform<MTEP.TransformDataModelMaterial>);
            timelineManager.RegisterTransform(
                MTEP.TransformType.Voice,
                MTEP.TimelineManager.CreateTransform<MTEP.TransformDataVoice>);
            timelineManager.RegisterTransform(
                MTEP.TransformType.Move,
                MTEP.TimelineManager.CreateTransform<MTEP.TransformDataMove>);
            timelineManager.RegisterTransform(
                MTEP.TransformType.Camera,
                MTEP.TimelineManager.CreateTransform<MTEP.TransformDataCamera>);
            timelineManager.RegisterTransform(
                MTEP.TransformType.ExtendBone,
                MTEP.TimelineManager.CreateTransform<MTEP.TransformDataExtendBone>);
            timelineManager.RegisterTransform(
                MTEP.TransformType.Eyes,
                MTEP.TimelineManager.CreateTransform<MTEP.TransformDataEyes>);
            timelineManager.RegisterTransform(
                MTEP.TransformType.FingerBlend,
                MTEP.TimelineManager.CreateTransform<MTEP.TransformDataFingerBlend>);
            timelineManager.RegisterTransform(
                MTEP.TransformType.Grounding,
                MTEP.TimelineManager.CreateTransform<MTEP.TransformDataGrounding>);
            timelineManager.RegisterTransform(
                MTEP.TransformType.IKHold,
                MTEP.TimelineManager.CreateTransform<MTEP.TransformDataIKHold>);
            timelineManager.RegisterTransform(
                MTEP.TransformType.Light,
                MTEP.TimelineManager.CreateTransform<MTEP.TransformDataLight>);
            timelineManager.RegisterTransform(
                MTEP.TransformType.LookAtTarget,
                MTEP.TimelineManager.CreateTransform<MTEP.TransformDataLookAtTarget>);
            timelineManager.RegisterTransform(
                MTEP.TransformType.Root,
                MTEP.TimelineManager.CreateTransform<MTEP.TransformDataRoot>);
            timelineManager.RegisterTransform(
                MTEP.TransformType.Rotation,
                MTEP.TimelineManager.CreateTransform<MTEP.TransformDataRotation>);
            timelineManager.RegisterTransform(
                MTEP.TransformType.ShapeKey,
                MTEP.TimelineManager.CreateTransform<MTEP.TransformDataShapeKey>);

            // DCM 由来レイヤーの TransformData
            timelineManager.RegisterTransform(
                MTEP.TransformType.Morph,
                MTEP.TimelineManager.CreateTransform<MTEP.TransformDataMorph>);
            timelineManager.RegisterTransform(
                MTEP.TransformType.Se,
                MTEP.TimelineManager.CreateTransform<MTEP.TransformDataSe>);
            timelineManager.RegisterTransform(
                MTEP.TransformType.Text,
                MTEP.TimelineManager.CreateTransform<MTEP.TransformDataText>);

            var updateManager = new TimelineUpdateManager();
            managerRegistry.RegisterManager(updateManager);

            // タイムライン作成・読み込み時の mte.OnLoad も同じ複合マネージャへ集約し、
            // OnLoad の二重発火とガード漏れを防ぐ
            MTEP.MotionTimelineEditor.instance.RegisterManager(updateManager);
        }
    }
}
