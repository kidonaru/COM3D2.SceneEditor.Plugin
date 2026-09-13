using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// ウィンドウ内レイヤーゲートの状態を外部プラグインへ公開する API。
    /// MTEUtils の TimelineLayerGateClient からリフレクションで発見・呼び出しされるため、
    /// クラス名・メソッドシグネチャは公開後変更禁止 (変更時は別名で追加する)。
    /// 契約はプリミティブのみ。レイヤーは Type ではなくクラス名文字列
    /// (例: "PostEffectTimelineLayer") で指定する。
    ///
    /// 契約:
    /// - GetState は TimelineLayerGateState を int で返す (0=未読込, 1=メイド不在, 2=未登録, 3=登録済み)。
    ///   enum の並びは契約なので変更禁止 (TimelineLayerGateTextTests で固定)
    /// - 未知のレイヤー名・タイムライン未読込では 0 を返し、AddLayer は何もしない。
    ///   外部側はこのとき従来表示のままにする。
    ///   PostEffectTimelineLayer は PostEffectsBridge 接続後に登録される (TimelineIntegration.TryRegisterPostEffects)
    ///   ため、起動直後の数フレームは登録済みでも 0 になる。一時的なもので不具合ではない
    /// - メイド単位レイヤー (hasSlotNo) は対象外。slotNo は常に 0
    /// - AddLayer は ChangeActiveLayer に委譲し、アクティブレイヤー切替と履歴登録もそちらで行う
    /// </summary>
    public static class TimelineLayerGateHost
    {
        private static MTEP.TimelineManager timelineManager => MTEP.TimelineManager.instance;

        public static int GetState(string layerName)
        {
            var info = FindLayerInfo(layerName);
            if (info == null)
            {
                return (int) TimelineLayerGateState.NoTimeline;
            }

            var timelineLoaded = timelineManager.timeline != null;
            var layerExists = timelineLoaded && timelineManager.GetLayer(info.layerType, 0) != null;
            return (int) TimelineLayerGateText.Resolve(timelineLoaded, false, false, layerExists);
        }

        public static string GetNoticeText(string layerName)
        {
            return TimelineLayerGateText.NoticeText(GetDisplayName(layerName));
        }

        public static string GetAddButtonText(string layerName)
        {
            return TimelineLayerGateText.AddButtonText(GetDisplayName(layerName));
        }

        public static void AddLayer(string layerName)
        {
            var info = FindLayerInfo(layerName);
            if (info == null || timelineManager.timeline == null)
            {
                return;
            }
            timelineManager.ChangeActiveLayer(info.layerType, 0);
        }

        private static string GetDisplayName(string layerName)
        {
            var info = FindLayerInfo(layerName);
            return info != null ? info.displayName : layerName;
        }

        /// <summary>TimelineManager 未初期化 (プラグイン無効時など) でも落ちないよう null を許容する</summary>
        private static MTEP.TimelineLayerInfo FindLayerInfo(string layerName)
        {
            var manager = timelineManager;
            if (manager == null || string.IsNullOrEmpty(layerName))
            {
                return null;
            }
            return manager.GetLayerInfo(layerName);
        }
    }
}
