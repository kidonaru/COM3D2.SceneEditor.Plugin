using System.Collections.Generic;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    public class ModelHackManager : ManagerBase
    {
        private Dictionary<string, IModelHack> modelHackMap = new Dictionary<string, IModelHack>();
        /// <summary>
        /// fileName ごとの使用中グループ番号。FixGroup の作業用に使い回す。
        /// FixGroup をテストから直接呼べるよう static にしてあるので、作業領域も static
        /// </summary>
        private static readonly Dictionary<string, HashSet<int>> _usedGroupMap
            = new Dictionary<string, HashSet<int>>();

        private List<StudioModelStat> _modelList = new List<StudioModelStat>();
        public List<StudioModelStat> modelList
        {
            get
            {
                _modelList.Clear();

                foreach (var modelHack in modelHackMap.Values)
                {
                    if (modelHack.IsValid())
                    {
                        _modelList.AddRange(modelHack.modelList);
                    }
                }

                FixGroup(_modelList);

                return _modelList;
            }
        }

        private List<string> _pluginNames = new List<string>();
        public List<string> pluginNames
        {
            get
            {
                _pluginNames.Clear();

                foreach (var modelHack in modelHackMap.Values)
                {
                    if (modelHack.IsValid())
                    {
                        _pluginNames.Add(modelHack.pluginName);
                    }
                }

                return _pluginNames;
            }
        }

        private static ModelHackManager _instance;
        public static ModelHackManager instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new ModelHackManager();
                }

                return _instance;
            }
        }

        public static event UnityAction<StudioModelStat> onCreateModel;

        private ModelHackManager()
        {
        }

        public void Register(IModelHack modelHack)
        {
            if (modelHack == null || !modelHack.Init())
            {
                return;
            }

            modelHackMap[modelHack.pluginName] = modelHack;
        }

        public IModelHack GetOrDefault(string pluginName)
        {
            IModelHack modelHack;
            if (!string.IsNullOrEmpty(pluginName) &&
                modelHackMap.TryGetValue(pluginName, out modelHack))
            {
                if (modelHack.IsValid())
                {
                    return modelHack;
                }
            }

            // モデル配置プロバイダ未登録なら null。呼び出し側はすべて null チェック済み
            return null;
        }

        public void DeleteAllModels()
        {
            try
            {
                foreach (var modelHack in modelHackMap.Values)
                {
                    if (modelHack.IsValid())
                    {
                        modelHack.DeleteAllModels();
                    }
                }
            }
            catch (System.Exception e)
            {
                MTEUtils.LogException(e);
            }
        }

        public void DeleteModel(StudioModelStat model)
        {
            try
            {
                var modelHack = GetOrDefault(model.pluginName);
                if (modelHack != null)
                {
                    modelHack.DeleteModel(model);
                }
            }
            catch (System.Exception e)
            {
                MTEUtils.LogException(e);
            }
        }

        public void CreateModel(StudioModelStat model)
        {
            try
            {
                var modelHack = GetOrDefault(model.pluginName);
                if (modelHack != null)
                {
                    modelHack.CreateModel(model);
                    onCreateModel?.Invoke(model);
                }
            }
            catch (System.Exception e)
            {
                MTEUtils.LogException(e);
            }
        }

        public void UpdateAttachPoint(StudioModelStat model)
        {
            try
            {
                var modelHack = GetOrDefault(model.pluginName);
                if (modelHack != null)
                {
                    modelHack.UpdateAttachPoint(model);
                }
            }
            catch (System.Exception e)
            {
                MTEUtils.LogException(e);
            }
        }

        public void SetModelVisible(StudioModelStat model, bool visible)
        {
            try
            {
                var modelHack = GetOrDefault(model.pluginName);
                if (modelHack != null)
                {
                    modelHack.SetModelVisible(model, visible);
                }
            }
            catch (System.Exception e)
            {
                MTEUtils.LogException(e);
            }
        }

        public void ChangePluginName(StudioModelStat model, string pluginName)
        {
            try
            {
                var prevModelHack = GetOrDefault(model.pluginName);
                var nextModelHack = GetOrDefault(pluginName);

                if (prevModelHack == null || nextModelHack == null)
                {
                    MTEUtils.LogWarning(
                        "モデル配置プロバイダが見つからないためプラグインを変更できません: {0} -> {1}",
                        model.pluginName, pluginName);
                    return;
                }

                if (nextModelHack != prevModelHack)
                {
                    prevModelHack.DeleteModel(model);
                    nextModelHack.CreateModel(model);
                }
            }
            catch (System.Exception e)
            {
                MTEUtils.LogException(e);
            }
        }

        /// <summary>
        /// 未採番 (group &lt; 0) の stat と、番号が先着と重複した stat にだけ番号を振る。
        /// 採番済みで一意な番号は削除されるまで変えない。列挙順が変わっても name が動かないようにするため。
        /// 削除で空いた番号は次の採番で再利用する。1 は使わない (0, 2, 3, ... の規則は従来どおり)
        /// </summary>
        public static void FixGroup(List<StudioModelStat> models)
        {
            _usedGroupMap.Clear();

            // 採番済みの番号を先着順で確保する
            List<StudioModelStat> pendings = null;
            foreach (var model in models)
            {
                if (model.group < 0 || !GetUsedGroups(model.info.fileName).Add(model.group))
                {
                    if (pendings == null)
                    {
                        pendings = new List<StudioModelStat>();
                    }
                    pendings.Add(model);
                }
            }

            if (pendings == null)
            {
                return;
            }

            foreach (var model in pendings)
            {
                var usedGroups = GetUsedGroups(model.info.fileName);

                var group = 0;
                while (usedGroups.Contains(group))
                {
                    group++;
                    if (group == 1) group++; // 1は使わない
                }

                model.SetGroup(group);
                usedGroups.Add(group);
            }
        }

        private static HashSet<int> GetUsedGroups(string fileName)
        {
            HashSet<int> groups;
            if (!_usedGroupMap.TryGetValue(fileName, out groups))
            {
                groups = new HashSet<int>();
                _usedGroupMap[fileName] = groups;
            }
            return groups;
        }

        public override void OnChangedSceneLevel(Scene scene, LoadSceneMode sceneMode)
        {
            foreach (var modelHack in modelHackMap.Values)
            {
                modelHack.OnChangedSceneLevel(scene, sceneMode);
            }
        }
    }
}