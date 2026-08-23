using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    using SE = SceneEditor.Plugin;
    using AttachPoint = PhotoTransTargetObject.AttachPoint;

    /// <summary>
    /// SceneEditor 環境向けの StudioHack 実装。
    /// タイムライン (MTE 移植コード) からのメイド・編集状態アクセスを
    /// SceneEditor の各マネージャへ橋渡しする
    /// </summary>
    public class SceneEditorHack : StudioHackBase
    {
        public override string pluginName => "SceneEditor";
        public override int priority => 0;

        private static SE.MaidManipulateManager manipulateManager
            => SE.MaidManipulateManager.instance;

        public override Maid selectedMaid => manipulateManager.targetMaid;

        private readonly List<Maid> _allMaids = new List<Maid>();
        public override List<Maid> allMaids
        {
            get
            {
                // SceneEditor はスタジオ専用ではないため、シーン上でアクティブな
                // メイド全員を対象にする
                _allMaids.Clear();
                var characterMgr = GameMain.Instance.CharacterMgr;
                var count = characterMgr.GetMaidCount();
                for (var i = 0; i < count; i++)
                {
                    var maid = characterMgr.GetMaid(i);
                    if (maid != null && maid.isActiveAndEnabled)
                    {
                        _allMaids.Add(maid);
                    }
                }
                return _allMaids;
            }
        }

        public override int selectedMaidSlotNo => allMaids.IndexOf(selectedMaid);

        public override string outputAnmPath
        {
            get
            {
                var path = PhotoModePoseSave.folder_path;
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }
                return path;
            }
        }

        public override bool isPoseEditing
        {
            get => manipulateManager.isEditMode;
            set
            {
                if (value && isAnmPlaying)
                {
                    isAnmPlaying = false;
                }
                manipulateManager.isEditMode = value;
            }
        }

        public override bool isIKVisible
        {
            get => manipulateManager.isBoneVisible;
            set => manipulateManager.isBoneVisible = value;
        }

        public override bool isAnmEnabled
        {
            get
            {
                var maid = selectedMaid;
                return maid != null && !SE.MaidMotionState.IsMotionStopped(maid);
            }
            set
            {
                foreach (var maid in allMaids)
                {
                    if (value)
                    {
                        if (SE.MaidMotionState.IsMotionStopped(maid))
                        {
                            SE.MaidMotionState.PlayMotion(maid);
                        }
                    }
                    else
                    {
                        SE.MaidMotionState.StopMotion(maid);
                    }
                }
            }
        }

        // タイムライン側が再生時間を直接制御するため、スライダー同期は不要
        public override float motionSliderRate
        {
            set { }
        }

        public override bool useMuneKeyL
        {
            set { }
        }

        public override bool useMuneKeyR
        {
            set { }
        }

        public override Camera subCamera => null;

        public override bool isUIVisible
        {
            get => !SE.WindowManager.instance.isWindowsHidden;
            set => SE.WindowManager.instance.isWindowsHidden = !value;
        }

        public override bool Init()
        {
            // 登録がシーンロード後になるため、初期状態はアクティブ扱いにする
            isSceneActive = true;

            // SceneEdit では photo mode の背景オブジェクト CSV が未ロードのため明示的に読み込む
            // (StudioModelManager の BGObjectIdMap / モデル生成が PhotoBGObjectData.data に依存する)
            if (PhotoBGObjectData.data == null)
            {
                PhotoBGObjectData.Create();
            }
            return true;
        }

        public override void OnChangedSceneLevel(Scene scene, LoadSceneMode sceneMode)
        {
            isSceneActive = scene.name != "SceneTitle";
        }

        public override bool IsValid()
        {
            _errorMessage = "";
            return true;
        }

        // ===== モデル管理 (MultipleMaidsHack の直接ロード方式を SE 向けに簡略化) =====
        // SceneEdit には photo studio の objectManagerWindow が無いため、
        // アセット/プレハブ/マイルーム/MOD を直接ロードして自前リストで管理する

        private List<StudioModelStat> _modelList = new List<StudioModelStat>();
        public override List<StudioModelStat> modelList => _modelList;

        public override void CreateModel(StudioModelStat model)
        {
            try
            {
                GameObject obj = null;
                if (model.info.type == StudioModelType.Prefab ||
                    model.info.type == StudioModelType.Asset)
                {
                    obj = LoadGameModel(model.info.fileName);
                }
                else if (model.info.type == StudioModelType.MyRoom)
                {
                    obj = LoadMyRoomObject((int)model.info.myRoomId);
                }
                else if (model.info.type == StudioModelType.Mod)
                {
                    obj = LoadModObject(model.info.fileName);
                }

                if (obj == null)
                {
                    MTEUtils.LogError("CreateModel: モデルの追加に失敗しました " + model.name);
                    return;
                }

                model.transform = obj.transform;
                model.obj = obj;
                _modelList.Add(model);

                UpdateAttachPoint(model);
            }
            catch (System.Exception e)
            {
                MTEUtils.LogException(e);
            }
        }

        public override void DeleteModel(StudioModelStat model)
        {
            var index = _modelList.FindIndex(m => m.transform == model.transform);
            if (index >= 0)
            {
                _modelList.RemoveAt(index);
            }
            if (model.transform != null)
            {
                Object.Destroy(model.transform.gameObject);
                model.transform = null;
            }
        }

        public override void DeleteAllModels()
        {
            foreach (var model in _modelList)
            {
                if (model.transform != null)
                {
                    Object.Destroy(model.transform.gameObject);
                    model.transform = null;
                }
            }
            _modelList.Clear();
        }

        public override void UpdateAttachPoint(StudioModelStat model)
        {
            var maidCache = maidManager.GetMaidCache(model.attachMaidSlotNo);
            AttachItem(maidCache, model.transform, model.attachPoint, false);
        }

        private static void AttachItem(MaidCache maidCache, Transform item, AttachPoint point, bool keepWorldPosition)
        {
            if (item == null)
            {
                return;
            }
            Transform parent = maidCache != null ? maidCache.GetAttachPointTransform(point) : null;
            Quaternion rotation = item.rotation;
            Vector3 localScale = item.localScale;

            item.SetParent(parent, keepWorldPosition);
            if (keepWorldPosition)
            {
                item.rotation = rotation;
            }
            else
            {
                item.localPosition = Vector3.zero;
                item.rotation = Quaternion.identity;
            }
            item.localScale = localScale;
        }

        /// <summary>親オブジェクト (自前配置モデルの置き場)。シーン跨ぎで作り直す</summary>
        private GameObject GetModelParent()
        {
            var parentObj = GameObject.Find("SceneEditor Model Parent");
            if (parentObj == null)
            {
                parentObj = new GameObject("SceneEditor Model Parent");
            }
            return parentObj;
        }

        private GameObject LoadGameModel(string assetName)
        {
            var sourceObj = GameMain.Instance.BgMgr.CreateAssetBundle(assetName);
            if (!sourceObj)
            {
                sourceObj = Resources.Load<GameObject>("Prefab/" + assetName);
            }
            if (!sourceObj)
            {
                sourceObj = Resources.Load<GameObject>("BG/" + assetName);
            }
            if (!sourceObj)
            {
                return LoadModObject(assetName);
            }

            var obj = Object.Instantiate(sourceObj);
            obj.name = assetName;
            obj.transform.SetParent(GetModelParent().transform, false);
            obj.transform.localPosition = Vector3.zero;

            foreach (var renderer in obj.GetComponentsInChildren<Renderer>())
            {
                if (renderer != null && renderer.gameObject.name.Contains("castshadow"))
                {
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            }
            foreach (var collider in obj.GetComponentsInChildren<Collider>())
            {
                if (collider != null)
                {
                    collider.enabled = false;
                }
            }
            foreach (var particle in obj.GetComponentsInChildren<ParticleSystem>())
            {
                if (particle != null)
                {
                    var main = particle.main;
                    main.loop = true;
                }
            }
            return obj;
        }

        private GameObject LoadMyRoomObject(int myRoomId)
        {
            var data = MyRoomCustom.PlacementData.GetData(myRoomId);
            if (data == null)
            {
                MTEUtils.LogError("LoadMyRoomObject: マイルームオブジェクトが見つかりません id=" + myRoomId);
                return null;
            }
            var prefab = data.GetPrefab();
            var newObj = Object.Instantiate(prefab);
            var obj = new GameObject(newObj.name);
            newObj.transform.SetParent(obj.transform, true);
            obj.transform.SetParent(GetModelParent().transform, false);
            obj.name = "MYR_" + myRoomId;
            return obj;
        }

        private GameObject LoadModObject(string assetName)
        {
            // menu から model ファイル名を解決して直接ロードする
            // (MultipleMaids の ProcScriptBin 経路は使わない)
            var fileName = Path.GetFileNameWithoutExtension(assetName);
            var menu = ModMenuLoader.Load(fileName);
            if (menu == null || string.IsNullOrEmpty(menu.modelFileName))
            {
                MTEUtils.LogError("LoadModObject: menu の読み込みに失敗しました " + assetName);
                return null;
            }

            var maid = selectedMaid;
            if (maid == null)
            {
                MTEUtils.LogError("LoadModObject: メイドが配置されていません");
                return null;
            }

            int modelVersion = 0;
            var bodySkin = maid.body0.GetSlot("handitemr");
            var obj = ImportCM.LoadSkinMesh_R(menu.modelFileName, null, "", bodySkin, 1, ref modelVersion);
            if (obj == null)
            {
                MTEUtils.LogError("LoadModObject: model の読み込みに失敗しました " + menu.modelFileName);
                return null;
            }
            obj.name = assetName;
            obj.transform.SetParent(GetModelParent().transform, false);
            return obj;
        }
    }
}
