using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// PNG 配置の複製ボタンの処理。Inspector と PNG配置ウィンドウの「配置済み」タブから呼ぶ。
    /// タイムライン読込中は元のキーを新しい名前へ写してから対応表へ載せ、履歴はタイムラインへ 1 件積む。
    /// 未読込ならシーン履歴へ 1 件積む
    /// </summary>
    public static class PngDuplicator
    {
        public static PngObjectData Duplicate(PngObjectData source)
        {
            if (source == null || source.rootObject == null)
            {
                return null;
            }

            var historyManager = HistoryManager.instance;
            var pngTimelineManager = MTEP.PngObjectTimelineManager.instance;
            var isTimelineMode = historyManager.isTimelineMode;

            if (isTimelineMode)
            {
                // シーン履歴の BeforeEdit は確定時に自動キーフレーム登録を誘発し、
                // 「PNGの複製」と別の履歴が積まれうるので通さない (モデルの複製ボタンと同じ)
                AutoEditMode.Enter();
                // 元の実体が対応表に載っていないと写し元のキーを引けない
                pngTimelineManager.RebuildIfChanged();
            }
            else
            {
                historyManager.BeforeEdit(null, HistoryScope.PngPlacement, "PNGの複製: " + source.name);
            }

            var created = PngPlacementManager.instance.DuplicatePng(source);
            if (created == null)
            {
                return null;
            }

            if (isTimelineMode)
            {
                pngTimelineManager.RegisterDuplicate(source, created);
            }

            // 元と同じ位置に重なるので、すぐギズモで動かせるよう選択する
            SelectionManager.instance.Select(created.rootObject, true, true);
            return created;
        }
    }
}
