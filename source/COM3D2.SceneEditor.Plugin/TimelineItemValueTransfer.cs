using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// タイムラインのレイヤー項目の現在値のコピー & ペースト。
    /// キーフレームを作らず、シーン上の値を読み書きする
    /// </summary>
    public static class TimelineItemValueTransfer
    {
        private static MTEP.TimelineManager timelineManager => MTEP.TimelineManager.instance;

        private static readonly List<string> _copyNames = new List<string>();
        private static readonly List<MTEP.ITransformData> _copyTransforms = new List<MTEP.ITransformData>();
        private static readonly List<string> _pasteNames = new List<string>();

        /// <summary>
        /// 現在値を読み出す。キーフレーム登録 (AddKeyFrames) と同じく一時フレームへ
        /// UpdateFrame で全項目を書き出し、指定の名前だけを取り出す。
        /// 値を持たない項目 (実体の無いモデル等) は結果に含めない
        /// </summary>
        public static void Capture(
            MTEP.ITimelineLayer layer, IList<string> names,
            List<string> outNames, List<MTEP.ITransformData> outTransforms)
        {
            outNames.Clear();
            outTransforms.Clear();
            if (layer == null || names == null || names.Count == 0)
            {
                return;
            }

            var frame = layer.CreateFrame(timelineManager.currentFrameNo);
            layer.UpdateFrame(frame, force: true);

            foreach (var name in names)
            {
                var bone = frame.GetBone(name);
                if (bone == null)
                {
                    continue;
                }
                outNames.Add(name);
                outTransforms.Add(bone.transform);
            }
        }

        public static void Copy(MTEP.ITimelineLayer layer, IList<string> names)
        {
            Capture(layer, names, _copyNames, _copyTransforms);
            if (_copyTransforms.Count == 0)
            {
                MTEUtils.LogWarning("コピーできる値がありません");
                return;
            }
            ItemValueClipboard.Set(_copyNames, _copyTransforms);
        }

        /// <summary>貼り付け先のうち 1 件以上に当たる値があるか。毎フレーム呼ぶので値の捕捉はしない</summary>
        public static bool CanPaste(MTEP.ITimelineLayer layer, IList<string> names)
        {
            if (layer == null || names == null)
            {
                return false;
            }
            foreach (var name in names)
            {
                if (CanPasteTo(layer, name))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool CanPasteTo(MTEP.ITimelineLayer layer, string name)
        {
            return layer.CanApplyTransformDirect(name)
                && ItemValueClipboard.CanResolve(name, layer.GetTransformType(name));
        }

        /// <summary>
        /// 当たる値を現在値へ直接当てる。先に BeforeEdit を通して編集モードへ入るので、
        /// 貼った値が再生データで上書きされず、確定時に自動キーフレーム登録の対象になる
        /// </summary>
        public static void Paste(MTEP.ITimelineLayer layer, IList<string> names)
        {
            if (!CanPaste(layer, names))
            {
                return;
            }

            _pasteNames.Clear();
            foreach (var name in names)
            {
                if (CanPasteTo(layer, name))
                {
                    _pasteNames.Add(name);
                }
            }

            // Capture は確定待ちが無いときだけ評価されるため、対象名は複製して渡す
            var targets = new List<string>(_pasteNames);
            var description = targets.Count == 1
                ? "貼り付け: " + targets[0]
                : string.Format("貼り付け: {0}件", targets.Count);
            // 貼り付けごとに別の操作として確定させるため、対象キーは毎回新しくする
            HistoryManager.instance.BeforeEdit(
                null, HistoryScope.TimelineItem, description, new object(),
                () => TimelineItemSnapshot.Capture(layer, targets));
            // ポップアップからの操作はタイムラインのゲート外なので、自動キー登録とレイヤー追従の
            // 対象を明示する。BeforeEdit 内の AutoEditMode.Enter が控えを上書きするため、その後に呼ぶ
            TimelineLayerGate.RecordEditedLayer(layer.layerType, layer.slotNo);

            foreach (var name in targets)
            {
                layer.ApplyTransformDirect(
                    ItemValueClipboard.CreateFor(name, layer.GetTransformType(name)));
            }
        }
    }
}
