using System.Collections.Generic;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// Hierarchy の配置物ビューに出す物をゲームと各マネージャーから集め、
    /// Unity に依存しない PlacedObjectSource の並びへ写す。木の組み立ては PlacedObjectTree が行う。
    /// 集める順がそのままカテゴリ内の並び順になる
    /// </summary>
    public static class PlacedObjectCollector
    {
        public static void Collect(List<PlacedObjectSource> results)
        {
            CollectMaids(results);
            CollectModels(results);
            CollectBackgroundModels(results);
            CollectPngs(results);
            CollectLights(results);
            CollectSubCameras(results);
        }

        private static void Add(
            List<PlacedObjectSource> results, PlacedObjectCategory category,
            GameObject go, string label, int parentId)
        {
            results.Add(new PlacedObjectSource
            {
                category = category,
                id = go.GetInstanceID(),
                label = string.IsNullOrEmpty(label) ? go.name : label,
                parentId = parentId,
                target = go,
            });
        }

        private static void CollectMaids(List<PlacedObjectSource> results)
        {
            var gameMain = GameMain.Instance;
            var characterMgr = gameMain != null ? gameMain.CharacterMgr : null;
            if (characterMgr == null)
            {
                return;
            }

            for (var i = 0; i < characterMgr.GetMaidCount(); i++)
            {
                var maid = characterMgr.GetMaid(i);
                // 呼び出し途中 (ボディ未読込) のメイドはまだ操作できないので出さない
                if (maid == null || maid.body0 == null || !maid.body0.isLoadedBody)
                {
                    continue;
                }
                Add(results, PlacedObjectCategory.Maid, maid.gameObject,
                    maid.status.fullNameJpStyle, PlacedObjectTree.NoParent);
            }
        }

        /// <summary>
        /// 提供モデル。アタッチ中のモデルは提供側がメイドのボーンの子へ付け替えているので、
        /// 実際の親子関係から付け先メイドを求める。タイムラインのキー値
        /// (StudioModelStat.attachMaidSlotNo) はタイムライン有効時しか同期されず、シーンモードで使えないため
        /// </summary>
        private static void CollectModels(List<PlacedObjectSource> results)
        {
            var entries = ModelProviderHost.GetModels();
            var modelObjects = new HashSet<GameObject>();
            foreach (var entry in entries)
            {
                modelObjects.Add(entry.obj);
            }

            foreach (var entry in entries)
            {
                var owner = FindOwner(entry.obj.transform.parent, modelObjects);
                Add(results, PlacedObjectCategory.Model, entry.obj, entry.displayName,
                    owner != null ? owner.GetInstanceID() : PlacedObjectTree.NoParent);
            }
        }

        /// <summary>
        /// transform から親をたどって最初に見つかったメイドか配置モデル。
        /// GetComponentInParent は 2.0 の Unity では非アクティブな親を飛ばし、
        /// includeInactive 引数も無いため自前でたどる
        /// </summary>
        private static GameObject FindOwner(Transform transform, HashSet<GameObject> modelObjects)
        {
            for (var t = transform; t != null; t = t.parent)
            {
                if (modelObjects.Contains(t.gameObject))
                {
                    return t.gameObject;
                }
                var maid = t.GetComponent<Maid>();
                if (maid != null)
                {
                    return maid.gameObject;
                }
            }
            return null;
        }

        /// <summary>背景モデルの木。背景ウィンドウの「追加」タブと同じ木 (複製は出さない)</summary>
        private static void CollectBackgroundModels(List<PlacedObjectSource> results)
        {
            var bgModelManager = MTEP.BGModelManager.instance;

            // 列挙はタイムライン有効時の LateUpdate でしか同期されないため、無効時だけ背景ウィンドウと同じく
            // ここで同期する (同期済みなら no-op)。有効時に呼ぶと背景切替をこちらが先に消費し、
            // LateUpdate 側の SetupModels (キーに沿った複製の生成) が走らなくなる
            if (!MTEP.TimelineManager.instance.IsValidData())
            {
                bgModelManager.SyncToCurrentBg();
            }

            foreach (var node in BGModelTree.Build(bgModelManager.modelInfoList))
            {
                AddBackgroundNode(results, node, PlacedObjectTree.NoParent);
            }
        }

        private static void AddBackgroundNode(List<PlacedObjectSource> results, BGModelNode node, int parentId)
        {
            var go = node.info.gameObject;
            Add(results, PlacedObjectCategory.Background, go, node.info.displayName, parentId);

            var id = go.GetInstanceID();
            foreach (var child in node.children)
            {
                AddBackgroundNode(results, child, id);
            }
        }

        private static void CollectPngs(List<PlacedObjectSource> results)
        {
            foreach (var png in PngPlacementManager.instance.pngObjects)
            {
                if (png != null && png.rootObject != null)
                {
                    Add(results, PlacedObjectCategory.Png, png.rootObject, png.name, PlacedObjectTree.NoParent);
                }
            }
        }

        /// <summary>
        /// 追加ライトだけを出す。メインライトは LightMain 経由でしか正しく編集できず、
        /// LightWindow も SelectionManager に載せない方針なので揃える
        /// </summary>
        private static void CollectLights(List<PlacedObjectSource> results)
        {
            foreach (var light in StudioLightManager.instance.lights)
            {
                if (light != null)
                {
                    Add(results, PlacedObjectCategory.Light, light.gameObject,
                        light.gameObject.name, PlacedObjectTree.NoParent);
                }
            }
        }

        private static void CollectSubCameras(List<PlacedObjectSource> results)
        {
            foreach (var subCamera in MTEP.SubCameraManager.instance.subCameras)
            {
                if (subCamera != null && subCamera.camera != null)
                {
                    Add(results, PlacedObjectCategory.SubCamera, subCamera.camera.gameObject,
                        subCamera.displayName, PlacedObjectTree.NoParent);
                }
            }
        }
    }
}
