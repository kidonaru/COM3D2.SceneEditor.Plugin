using System;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// ウィンドウ内レイヤーゲート。
    /// タイムライン読込中に対象レイヤーが未登録なら、注意ラベルと追加ボタンを描いて
    /// 以降の項目を強制無効にする。追加は ChangeActiveLayer に委譲し、タイムライン
    /// ウィンドウのアクティブレイヤーも追加先へ切り替える（履歴登録も同メソッド内）。
    /// タイムライン未読込時は何も描かず、従来表示に一切干渉しない。
    ///
    /// Begin で無効化したら、同じ描画パス内で必ず End を呼ぶこと。
    /// SetEnabled はグローバル GUI.enabled を書き換えるため、戻し忘れると
    /// 後に描かれる ComboBoxPopupWindow まで操作できなくなる。
    ///
    /// タブ切替ボタンはゲートの対象外にする（無効化するとタブから抜けられなくなる）。
    /// 各ウィンドウはタブを描いた後に Begin を呼ぶこと。
    ///
    /// あわせて「どのレイヤーの値を触ったか」の控えも持つ。ゲートは
    /// 「この範囲の項目はこのレイヤーのもの」を既に知っている唯一の場所なので、
    /// 編集確定時のレイヤー自動追従はこの控えを頼りにする
    /// (RecordEditedLayerFromOpenGate / TakeEditedLayer)
    /// </summary>
    public static class TimelineLayerGate
    {
        private const float BUTTON_WIDTH = 220f;

        private static MTEP.TimelineManager timelineManager => MTEP.TimelineManager.instance;

        // 描画中に開いているゲート。Begin で設定し End で解除する
        private static Type _openLayerType;
        private static int _openSlotNo;

        // 値が書かれた瞬間に開いていたゲート。編集の確定時に取り出して消す
        private static Type _editedLayerType;
        private static int _editedSlotNo;

        /// <summary>
        /// 今開いているゲートを「触ったレイヤー」として控える。
        /// 値を書く直前に必ず通る AutoEditMode.Enter から呼ぶ。
        /// ゲートの外で触ったときは控えを消す (前の値を引きずらない)。
        ///
        /// 既知の穴: コンボボックスのポップアップは所有ウィンドウの描画パスが
        /// 終わった後 (End 済み) に選択を確定するため、ここでは拾えない
        /// </summary>
        public static void RecordEditedLayerFromOpenGate()
        {
            _editedLayerType = _openLayerType;
            _editedSlotNo = _openSlotNo;
        }

        /// <summary>
        /// ゲートを経由しない操作 (ビューポートでのボーンドラッグ等) 向けに、
        /// 触ったレイヤーを明示的に控える
        /// </summary>
        public static void RecordEditedLayer(Type layerType, int slotNo)
        {
            _editedLayerType = layerType;
            _editedSlotNo = slotNo;
        }

        /// <summary>控えていた「触ったレイヤー」を取り出して消す。無ければ null</summary>
        public static Type TakeEditedLayer(out int slotNo)
        {
            var layerType = _editedLayerType;
            slotNo = _editedSlotNo;

            _editedLayerType = null;
            _editedSlotNo = 0;

            return layerType;
        }

        /// <summary>
        /// メイド非依存レイヤー用。呼び出し後もそのまま項目を描き続ける（無効表示にするだけで隠さない）。
        /// 同一タブ内で同じレイヤーを区間ごとに複数回ゲートする場合、
        /// 2 回目以降は drawNotice を false にして注意ラベルと追加ボタンの重複を避ける
        /// </summary>
        public static TimelineLayerGateState Begin(
            GUIView view, Type layerType, float rowHeight, bool drawNotice = true)
        {
            var timelineLoaded = timelineManager.timeline != null;
            var layerExists = timelineLoaded && timelineManager.GetLayer(layerType, 0) != null;
            var state = TimelineLayerGateText.Resolve(timelineLoaded, false, false, layerExists);
            _openLayerType = layerType;
            _openSlotNo = 0;
            Apply(view, layerType, 0, state, rowHeight, drawNotice);
            return state;
        }

        /// <summary>
        /// メイド単位レイヤー (hasSlotNo == true) 用。slotNo はタイムライン側の MaidCache から引く。
        /// 対象メイドがタイムライン側に無い場合はレイヤーを作れないので、
        /// 追加ボタンは出さず案内だけ出して無効化する。
        /// 戻り値は判定結果。呼び出し側が同じ状況の注意文言を重ねて出さないために使う
        /// </summary>
        public static TimelineLayerGateState Begin(
            GUIView view, Type layerType, Maid maid, float rowHeight)
        {
            var timelineLoaded = timelineManager.timeline != null;
            var maidCache = maid != null ? MTEP.MaidManager.instance.GetMaidCache(maid) : null;
            var slotNo = maidCache != null ? maidCache.slotNo : 0;
            var layerExists = timelineLoaded && maidCache != null
                && timelineManager.GetLayer(layerType, slotNo) != null;
            var state = TimelineLayerGateText.Resolve(
                timelineLoaded, true, maidCache != null, layerExists);
            _openLayerType = layerType;
            _openSlotNo = slotNo;
            Apply(view, layerType, slotNo, state, rowHeight, true);
            return state;
        }

        /// <summary>強制無効を解除して有効へ戻す。冪等</summary>
        public static void End(GUIView view)
        {
            _openLayerType = null;
            _openSlotNo = 0;

            view.forceDisabled = false;
            view.SetEnabled(true);
        }

        private static void Apply(
            GUIView view, Type layerType, int slotNo, TimelineLayerGateState state,
            float rowHeight, bool drawNotice)
        {
            switch (state)
            {
                case TimelineLayerGateState.NoTimeline:
                case TimelineLayerGateState.Ready:
                    return;

                case TimelineLayerGateState.MaidNotFound:
                    if (drawNotice)
                    {
                        view.DrawLabel(TimelineLayerGateText.MaidNotFoundText, -1, rowHeight,
                            textColor: Color.yellow);
                    }
                    Disable(view);
                    return;

                case TimelineLayerGateState.Missing:
                default:
                    if (drawNotice)
                    {
                        DrawMissing(view, layerType, slotNo, rowHeight);
                    }
                    Disable(view);
                    return;
            }
        }

        private static void DrawMissing(GUIView view, Type layerType, int slotNo, float rowHeight)
        {
            var info = timelineManager.GetLayerInfo(layerType);
            var displayName = info != null ? info.displayName : layerType.Name;

            view.DrawLabel(TimelineLayerGateText.NoticeText(displayName), -1, rowHeight,
                textColor: Color.yellow);

            // ボタンは強制無効の前に描く（押せる必要がある）
            if (view.DrawButton(TimelineLayerGateText.AddButtonText(displayName), BUTTON_WIDTH, rowHeight))
            {
                timelineManager.ChangeActiveLayer(layerType, slotNo);
            }

            view.DrawHorizontalLine(Color.gray);
            view.AddSpace(5);
        }

        private static void Disable(GUIView view)
        {
            view.forceDisabled = true;
            view.SetEnabled(false);
        }
    }
}
