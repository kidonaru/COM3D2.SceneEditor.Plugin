using System;
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// SceneEditor の選択中メイドを外部プラグインと共有する公開 API。
    /// MTEUtils の MaidSelectClient からリフレクションで発見・呼び出しされるため、
    /// クラス名・メソッドシグネチャは公開後変更禁止 (変更時は別名で追加する)。
    /// Maid はゲーム本体 (Assembly-CSharp) の型で全プラグインが共有参照しているため、
    /// プラグイン独自型と違い DLL 間でそのまま受け渡せる
    ///
    /// 契約:
    /// - 選択の共有は双方向。読み取り (selectedMaid) と購読 (Subscribe) に加え、
    ///   TrySelectMaid で外部から SceneEditor の選択を変更できる
    /// - 通知は SceneEditor 側の選択切り替え処理 (ギズモ選択の同期) が完了した後に流れる。
    ///   選択解除 (null) も通知される
    /// - 設定 (linkExternalPlugin) が OFF の間は通知せず、TrySelectMaid も受け付けない。
    ///   購読自体は維持される
    /// - Subscribe した時点では通知しない。接続直後に現状へ合わせたいゲストは
    ///   selectedMaid を読むこと
    /// - 自分の TrySelectMaid に対しても通知が流れる (エコー)。抑止はゲスト側の責務
    /// - 受け取った Maid を保持する場合、解放済みメイドを掴み続けないようゲスト側で
    ///   生存チェック (maid.body0 != null 等) を行うこと
    /// - Subscribe したら不要になった時点で必ず Unsubscribe すること。
    ///   ホストは常駐するため、解除を怠るとハンドラが掴んだ参照ごと残る
    /// </summary>
    public static class MaidSelectHost
    {
        private static readonly List<Action<Maid>> _subscribers = new List<Action<Maid>>();

        /// <summary>
        /// 現在の選択中メイド。未選択・解放済みなら null (生存チェックは targetMaid getter が行う)
        /// </summary>
        public static Maid selectedMaid => MaidManipulateManager.instance.targetMaid;

        /// <summary>連動設定が ON か。OFF の間は通知が流れず、TrySelectMaid も失敗する</summary>
        public static bool isLinkEnabled => ConfigManager.instance.config.linkExternalPlugin;

        /// <summary>
        /// 外部から SceneEditor の選択中メイドを変更する。
        /// SceneEditor が無効・連動設定が OFF・メイドが呼出済みでない場合は何もせず false。
        /// 成功時は購読者へ通知が流れる (呼び出し元にもエコーされる)
        /// </summary>
        public static bool TrySelectMaid(Maid maid)
        {
            if (!EditorStateHost.isEditorEnabled || !isLinkEnabled)
            {
                return false;
            }

            if (maid == null || !MTEUtils.GetReadyMaidList().Contains(maid))
            {
                return false;
            }

            MaidManipulateManager.instance.targetMaid = maid;
            return true;
        }

        /// <summary>
        /// 選択中メイドの変化を購読する。引数は変化後のメイド (選択解除は null)
        /// </summary>
        public static void Subscribe(Action<Maid> onChanged)
        {
            if (onChanged == null)
            {
                MTEUtils.LogError("MaidSelectHost.Subscribe: null は購読できません");
                return;
            }

            if (_subscribers.Contains(onChanged))
            {
                return;
            }

            _subscribers.Add(onChanged);
        }

        public static void Unsubscribe(Action<Maid> onChanged)
        {
            if (onChanged == null)
            {
                return;
            }

            _subscribers.Remove(onChanged);
        }

        /// <summary>
        /// 選択の変化を購読者へ配る。
        /// 購読者ごとに例外を握り潰し、1 プラグインの不具合でホストや他ゲストを巻き込まない
        /// </summary>
        internal static void NotifyMaidChanged(Maid maid)
        {
            if (!isLinkEnabled)
            {
                return;
            }

            // 通知中に Subscribe / Unsubscribe されてもコレクションが壊れないよう複製して回す
            var subscribers = _subscribers.ToArray();
            foreach (var subscriber in subscribers)
            {
                try
                {
                    subscriber(maid);
                }
                catch (Exception e)
                {
                    MTEUtils.LogException(e);
                }
            }
        }

        /// <summary>
        /// 連動設定が OFF → ON になったときに現在の選択を配る。
        /// 設定を入れた瞬間からゲストの選択がズレたままになるのを防ぐ
        /// </summary>
        internal static void OnLinkEnabledChanged(bool linkEnabled)
        {
            if (!linkEnabled)
            {
                return;
            }

            NotifyMaidChanged(selectedMaid);
        }
    }
}
