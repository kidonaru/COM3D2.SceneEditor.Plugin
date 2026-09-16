using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 外部プラグインのウィンドウから編集モードへ自動移行するための公開 API。
    /// MTEUtils の AutoEditModeClient からリフレクションで発見・呼び出しされるため、
    /// クラス名・メソッドシグネチャは公開後変更禁止 (変更時は別名で追加する)。
    /// 契約はプリミティブのみ。レイヤーは Type ではなくクラス名文字列
    /// (例: "PostEffectTimelineLayer") で指定する。
    ///
    /// 契約:
    /// - 値を書く「直前」に呼ぶこと。編集モード外はタイムラインのレイヤーが
    ///   毎フレーム再生値を書き戻すため、書いた後に呼んでも巻き戻る
    /// - 既に編集モードなら再入しない (AutoEditMode.Enter と同じ)
    /// - layerName が既知なら「触ったレイヤー」として控え、SceneEditor 側の
    ///   編集確定時にそのレイヤーをアクティブへ切り替える。未知の名前なら控えない
    /// - SceneEditor の UI が無効の間は何もしない (レイヤーが動いていないため入る意味が無い)
    /// - 連携設定 (linkExternalPlugin) は見ない。TimelineLayerGateHost と同じく
    ///   タイムライン再生値との整合に必要な経路で、OFF にすると外部側の操作が毎フレーム巻き戻る
    /// </summary>
    public static class AutoEditModeHost
    {
        public static void Enter(string layerName)
        {
            if (!EditorStateHost.isEditorEnabled)
            {
                return;
            }

            // Enter 内の RecordEditedLayerFromOpenGate は SceneEditor 側のゲートを見るが、
            // 外部ウィンドウの描画中は開いていないため控えが消える。後から明示的に控え直す
            AutoEditMode.Enter();

            var info = FindLayerInfo(layerName);
            if (info == null)
            {
                return;
            }

            // メイド単位レイヤーは操作対象メイドのスロットで引く (TimelineLayerGateHost と同じ規約)
            var slotNo = info.category == MTEP.TimelineLayerCategory.Maid
                ? MTEP.MaidManager.instance.maidSlotNo
                : 0;
            TimelineLayerGate.RecordEditedLayer(info.layerType, slotNo);
        }

        /// <summary>TimelineManager 未初期化 (タイトル画面など) でも落ちないよう null を許容する</summary>
        private static MTEP.TimelineLayerInfo FindLayerInfo(string layerName)
        {
            var manager = MTEP.TimelineManager.instance;
            if (manager == null || string.IsNullOrEmpty(layerName))
            {
                return null;
            }
            return manager.GetLayerInfo(layerName);
        }
    }
}
