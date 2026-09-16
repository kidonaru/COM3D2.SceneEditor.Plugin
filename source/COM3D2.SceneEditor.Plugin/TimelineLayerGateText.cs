namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>ウィンドウ内レイヤーゲートの判定結果</summary>
    public enum TimelineLayerGateState
    {
        /// <summary>タイムライン未読込。ゲートは何もしない（従来表示のまま）</summary>
        NoTimeline,
        /// <summary>メイド単位のレイヤーだが、対象メイドがタイムライン側に無い</summary>
        MaidNotFound,
        /// <summary>レイヤー未登録。無効表示＋追加ボタン</summary>
        Missing,
        /// <summary>レイヤー登録済み。通常表示</summary>
        Ready,
    }

    /// <summary>
    /// ウィンドウ内レイヤーゲートの状態判定と文言。
    /// Unity 非依存にしてユニットテスト可能にしている
    /// </summary>
    public static class TimelineLayerGateText
    {
        public const string MaidNotFoundText = "タイムライン側の対象メイドが見つかりません";

        public static TimelineLayerGateState Resolve(
            bool timelineLoaded, bool needsMaid, bool maidFound, bool layerExists)
        {
            if (!timelineLoaded) return TimelineLayerGateState.NoTimeline;
            if (needsMaid && !maidFound) return TimelineLayerGateState.MaidNotFound;
            return layerExists ? TimelineLayerGateState.Ready : TimelineLayerGateState.Missing;
        }

        public static string NoticeText(string displayName)
        {
            return "「" + displayName + "」レイヤーが未登録のためタイムラインに記録されません";
        }

        public static string AddButtonText(string displayName)
        {
            return "「" + displayName + "」レイヤーを追加";
        }

        /// <summary>
        /// レイヤーがタイムラインに表示されているか。キーが記録されるのは表示中のレイヤーだけ。
        /// 判定は TimelineLayerViewFilter と同規則 (レイヤーモードはアクティブのみ、
        /// カテゴリモードはアクティブレイヤーと同カテゴリ)
        /// </summary>
        public static bool IsLayerDisplayed(
            bool isCurrent, bool isCategoryViewMode, bool isSameCategoryAsCurrent)
        {
            return isCurrent || (isCategoryViewMode && isSameCategoryAsCurrent);
        }

        public static string HiddenNoticeText(string displayName)
        {
            return "「" + displayName + "」レイヤーが非表示のためタイムラインに記録されません";
        }

        public static string ShowButtonText(string displayName)
        {
            return "「" + displayName + "」レイヤーを表示";
        }
    }
}
