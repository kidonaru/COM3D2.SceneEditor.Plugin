using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    using AttachPoint = PhotoTransTargetObject.AttachPoint;

    /// <summary>
    /// 配置モデル 1 つ分の管理行 (表示切替・プラグイン・複製・削除・アタッチ先)。
    /// 旧レイヤー編集ウィンドウの管理タブ (ModelTimelineLayerBase.DrawModelContent) から移設。
    /// 一覧性はヒエラルキーが受け持ち、ここは選択中のモデルへの操作だけを担う。
    /// コンボボックスの開閉状態を持つため、モデルごとにインスタンスを分ける
    /// </summary>
    public class ModelManageRowDrawer
    {
        private const float RowHeight = 20f;

        private static MTEP.StudioModelManager modelManager => MTEP.StudioModelManager.instance;
        private static MTEP.ModelHackManager modelHackManager => MTEP.ModelHackManager.instance;
        private static MTEP.TimelineManager timelineManager => MTEP.TimelineManager.instance;

        private readonly GUIComboBox<string> _pluginComboBox = new GUIComboBox<string>
        {
            getName = (name, _) => string.IsNullOrEmpty(name) ? "Default" : name,
        };

        private readonly GUIComboBox<MTEP.MaidCache> _maidComboBox = new GUIComboBox<MTEP.MaidCache>
        {
            getName = (maidCache, _) => maidCache == null ? "未選択" : maidCache.fullName,
            contentSize = new Vector2(150, 300),
        };

        private readonly GUIComboBox<string> _attachPointComboBox = new GUIComboBox<string>
        {
            getName = (name, _) => name,
            items = BoneUtils.AttachPointNames,
            buttonSize = new Vector2(60, 20),
        };

        /// <summary>アタッチ先メイドの選択肢 (先頭の null は「未選択」)</summary>
        private readonly List<MTEP.MaidCache> _maidCaches = new List<MTEP.MaidCache>();

        public void Draw(GUIView view, MTEP.StudioModelStat model)
        {
            if (model == null)
            {
                return;
            }

            view.DrawToggle("表示", model.visible, 80, RowHeight, newValue =>
            {
                modelManager.SetModelVisible(model, newValue);
                model.visible = newValue;
            });

            view.BeginHorizontal();
            {
                var pluginNames = modelHackManager.pluginNames;
                _pluginComboBox.items = pluginNames;
                _pluginComboBox.currentIndex = GetPluginIndex(pluginNames, model.pluginName);
                _pluginComboBox.onSelected = (pluginName, _) =>
                {
                    modelManager.ChangePluginName(model, pluginName);
                };
                _pluginComboBox.DrawButton(view);

                if (view.DrawButton("複製", 45, RowHeight))
                {
                    timelineManager.CopyModel(model);
                }

                if (view.DrawButton("削除", 45, RowHeight))
                {
                    modelManager.DeleteModel(model);
                }
            }
            view.EndLayout();

            view.BeginHorizontal();
            {
                _maidCaches.Clear();
                _maidCaches.Add(null);
                _maidCaches.AddRange(MTEP.MaidManager.instance.maidCaches);

                _maidComboBox.items = _maidCaches;
                _maidComboBox.currentIndex = Mathf.Clamp(
                    model.attachMaidSlotNo + 1, 0, _maidCaches.Count - 1);
                _maidComboBox.onSelected = (maidCache, index) =>
                {
                    model.attachMaidSlotNo = index - 1;
                    if (model.attachPoint == AttachPoint.Null)
                    {
                        model.attachPoint = AttachPoint.Head;
                    }
                    modelManager.UpdateAttachPoint(model);
                };
                _maidComboBox.DrawButton(view);

                if (model.attachMaidSlotNo >= 0)
                {
                    _attachPointComboBox.currentIndex = (int)model.attachPoint;
                    _attachPointComboBox.onSelected = (_, index) =>
                    {
                        model.attachPoint = (AttachPoint)index;
                        modelManager.UpdateAttachPoint(model);
                    };
                    _attachPointComboBox.DrawButton(view);
                }
            }
            view.EndLayout();
        }

        /// <summary>プラグイン名から選択肢の添字を引く。未設定・未知の名前は先頭 (Default) 扱い</summary>
        private static int GetPluginIndex(List<string> pluginNames, string pluginName)
        {
            if (string.IsNullOrEmpty(pluginName))
            {
                return 0;
            }

            var index = pluginNames.IndexOf(pluginName);
            return index >= 0 ? index : 0;
        }
    }
}
