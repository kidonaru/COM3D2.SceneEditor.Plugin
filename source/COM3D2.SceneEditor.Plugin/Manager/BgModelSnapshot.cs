using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 背景モデル (BGModelManager の制御対象) のプリセット断面。
    /// 実体は背景オブジェクトの子 GameObject にしか無いため、値をここで DTO へ吸い出す。
    /// 背景切替のたびに子は作り直されるので、復元は背景適用の後に呼ぶ
    /// </summary>
    public static class BgModelSnapshot
    {
        private static MTEP.BGModelManager bgModelManager => MTEP.BGModelManager.instance;

        /// <summary>制御対象の背景モデル全件を DTO へ吸い出す</summary>
        public static ScenePresetBgModels CaptureState()
        {
            var data = new ScenePresetBgModels();
            foreach (var model in bgModelManager.models)
            {
                var transform = model.transform;
                if (transform == null)
                {
                    continue;
                }
                data.models.Add(new ScenePresetBgModel
                {
                    sourceName = model.sourceName,
                    group = model.group,
                    visible = model.visible,
                    position = transform.localPosition,
                    rotation = transform.localEulerAngles,
                    scale = transform.localScale,
                });
            }
            return data;
        }

        /// <summary>
        /// 背景モデルを書き戻す。null (旧プリセット / 未記録) なら何もしない。
        /// 個数 (複製) を先に合わせてから各モデルへ値を書く。
        /// 保存時の背景と違う背景に適用したときは、見つからないモデルを警告だけ出して飛ばす
        /// </summary>
        public static void ApplyState(ScenePresetBgModels data)
        {
            if (data == null)
            {
                return;
            }

            var models = data.models;
            var manager = bgModelManager;
            // 背景切替後に LateUpdate が回っていない (タイムライン未読込 / 同一フレーム) と
            // 旧背景の子を掴んだままなので、必ず現在の背景へ同期してから触る。
            // 個数合わせは下の SetupModels 1 回に一本化する
            manager.SyncToCurrentBg();

            var dataList = new List<MTEP.TimelineBGModelData>();
            foreach (var state in models)
            {
                if (manager.GetModelInfo(state.sourceName) == null)
                {
                    MTEUtils.LogWarning("背景モデルが現在の背景にありません: {0}", state.sourceName);
                    continue;
                }
                dataList.Add(new MTEP.TimelineBGModelData
                {
                    sourceName = state.sourceName,
                    group = state.group,
                });
            }
            // 不足分の追加 (複製の生成含む) と余剰の削除をまとめて行い、タイムラインの一覧も同期する
            manager.SetupModels(dataList);

            foreach (var state in models)
            {
                var model = manager.GetModel(state.sourceName + MTEP.PluginUtils.GetGroupSuffix(state.group));
                var transform = model != null ? model.transform : null;
                if (transform == null)
                {
                    continue;
                }
                model.visible = state.visible;
                transform.localPosition = state.position;
                transform.localEulerAngles = state.rotation;
                transform.localScale = state.scale;
            }
        }
    }
}
