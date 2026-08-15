using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// PNG 配置のスナップショット (配置一覧丸ごと)。
    /// 記録と復元はシーンプリセットと共用する
    /// </summary>
    public class PngPlacementSnapshot : IStateSnapshot
    {
        private ScenePresetPngPlacement _state;

        public static PngPlacementSnapshot Capture()
        {
            return new PngPlacementSnapshot { _state = CaptureState() };
        }

        public static ScenePresetPngPlacement CaptureState()
        {
            var state = new ScenePresetPngPlacement();
            foreach (var data in PngPlacementManager.instance.pngObjects)
            {
                if (data.rootObject == null)
                {
                    continue;
                }
                state.objects.Add(new ScenePresetPngObject
                {
                    source = data.source,
                    relativePath = data.relativePath,
                    position = data.transform.position,
                    rotation = data.transform.eulerAngles,
                    scale = data.transform.localScale,
                    billboard = data.billboard,
                    brightness = data.brightness,
                    color = data.color,
                    renderQueue = data.renderQueue,
                    visible = data.visible,
                });
            }
            return state;
        }

        /// <summary>
        /// PNG 配置を復元する。state == null（PNG 配置機能より前の v13 以前のプリセット）は
        /// 保存時点で配置が 0 枚だったことが確定しているため、空として扱い全消去する。
        /// この null の解釈はプリセット適用専用で、Undo/Redo 経由の _state は
        /// Capture() 由来のため常に非 null になる。
        /// 既存の配置物を使い回して差分適用する。全消去して作り直すと、
        /// Inspector・ギズモが Object スコープで積んだ Transform 参照が失効するため
        /// (LightSnapshot と同じ理由)。ただし画像が異なる場合は作り直す
        /// </summary>
        public static void ApplyState(ScenePresetPngPlacement state)
        {
            var manager = PngPlacementManager.instance;
            var current = manager.pngObjects;

            if (state == null)
            {
                // 適用直後は履歴がクリアされ Undo で戻せないため、
                // 旧プリセットによる意図しない全消去に気付けるようログを残す
                if (current.Count > 0)
                {
                    MTEUtils.Log("旧形式のシーンプリセットのため PNG 配置を {0} 枚消去しました",
                        current.Count);
                }
                state = new ScenePresetPngPlacement();
            }

            // 復元できた枚数。画像が欠けて配置できなかった分は進めないため
            // state のインデックスとは一致しない
            var index = 0;

            foreach (var objState in state.objects)
            {
                var data = index < current.Count ? current[index] : null;

                // 画像が違う既存物は使い回せないため入れ替える
                if (data != null
                    && (data.source != objState.source
                        || data.relativePath != objState.relativePath))
                {
                    Remove(manager, data);
                    data = null;
                }

                if (data == null)
                {
                    data = manager.AddPng(objState.source, objState.relativePath);
                    if (data == null)
                    {
                        // 画像が消えている等。1 枚欠けても残りの復元は続ける。
                        // index を進めないことで、詰められた既存物を次の周で見直す
                        continue;
                    }
                    // AddPng は末尾へ追加するため index 番目へ差し込み直す
                    manager.MovePng(data, index);
                }

                data.transform.position = objState.position;
                data.transform.eulerAngles = objState.rotation;
                data.transform.localScale = objState.scale;
                manager.SetBillboard(data, objState.billboard);
                manager.SetColor(data, objState.color, objState.brightness);
                manager.SetRenderQueue(data, objState.renderQueue);
                manager.SetVisible(data, objState.visible);
                index++;
            }

            // 記録に無い余りを末尾から消す
            while (current.Count > index)
            {
                Remove(manager, current[current.Count - 1]);
            }
        }

        /// <summary>配置物を 1 枚消す。消す物が選択中なら Inspector に残さない</summary>
        private static void Remove(PngPlacementManager manager, PngObjectData data)
        {
            if (data.rootObject != null
                && SelectionManager.instance.selectedObject == data.rootObject)
            {
                SelectionManager.instance.Select(null);
            }
            manager.RemovePng(data);
        }

        public void AddBones(IEnumerable<Transform> targetBones)
        {
        }

        public IStateSnapshot CaptureCurrent() => Capture();

        public void Apply(Maid maid) => ApplyState(_state);

        public bool Approximately(IStateSnapshot other)
        {
            var o = other as PngPlacementSnapshot;
            if (o == null || _state == null || o._state == null
                || _state.objects.Count != o._state.objects.Count)
            {
                return false;
            }

            for (var i = 0; i < _state.objects.Count; i++)
            {
                var a = _state.objects[i];
                var b = o._state.objects[i];
                if (a.source != b.source
                    || a.relativePath != b.relativePath
                    || a.position != b.position
                    || a.rotation != b.rotation
                    || a.scale != b.scale
                    || a.billboard != b.billboard
                    || !Mathf.Approximately(a.brightness, b.brightness)
                    || a.color != b.color
                    || a.renderQueue != b.renderQueue
                    || a.visible != b.visible)
                {
                    return false;
                }
            }
            return true;
        }

        public bool CanApply(Maid maid) => _state != null;
    }
}
