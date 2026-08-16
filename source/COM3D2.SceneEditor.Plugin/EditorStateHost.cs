using System;
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// SceneEditor の有効/無効を外部プラグインへ伝える公開 API。
    /// MTEUtils の EditorStateClient からリフレクションで発見・呼び出しされるため、
    /// クラス名・メソッドシグネチャは公開後変更禁止 (変更時は別名で追加する)。
    /// 契約はプリミティブ + デリゲートのみ (型は DLL 間で共有できない)
    ///
    /// 契約:
    /// - 連動は SceneEditor → 外部の一方向のみ。ゲスト側の有効/無効は SceneEditor へ反映されない
    /// - 通知は SceneEditor 側の有効化・無効化処理が完了した後に流れる
    /// - 設定 (linkExternalPlugin) が OFF の間は通知しない。購読自体は維持される
    /// - Subscribe した時点では通知しない。プラグインのロード順は不定なので、
    ///   接続直後に現状へ合わせたいゲストは isEditorEnabled を読むこと
    /// - Subscribe したら不要になった時点で必ず Unsubscribe すること。
    ///   ホストは常駐するため、解除を怠るとハンドラが掴んだ参照ごと残る
    /// </summary>
    public static class EditorStateHost
    {
        private static readonly List<Action<bool>> _subscribers = new List<Action<bool>>();

        /// <summary>SceneEditor の UI が現在有効か</summary>
        public static bool isEditorEnabled
        {
            get
            {
                var plugin = SceneEditorPlugin.instance;
                return plugin != null && plugin.isEnable;
            }
        }

        /// <summary>連動設定が ON か。OFF の間は通知が流れない</summary>
        public static bool isLinkEnabled => ConfigManager.instance.config.linkExternalPlugin;

        /// <summary>
        /// 有効/無効の変化を購読する。引数は変化後の有効状態
        /// </summary>
        public static void Subscribe(Action<bool> onChanged)
        {
            if (onChanged == null)
            {
                MTEUtils.LogError("EditorStateHost.Subscribe: null は購読できません");
                return;
            }

            if (_subscribers.Contains(onChanged))
            {
                return;
            }

            _subscribers.Add(onChanged);
        }

        public static void Unsubscribe(Action<bool> onChanged)
        {
            if (onChanged == null)
            {
                return;
            }

            _subscribers.Remove(onChanged);
        }

        /// <summary>
        /// 有効/無効の変化を購読者へ配る。
        /// 購読者ごとに例外を握り潰し、1 プラグインの不具合でホストや他ゲストを巻き込まない
        /// </summary>
        internal static void NotifyEditorEnabledChanged(bool enabled)
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
                    subscriber(enabled);
                }
                catch (Exception e)
                {
                    MTEUtils.LogException(e);
                }
            }
        }

        /// <summary>
        /// 連動設定が OFF → ON になったときに現在の状態を配る。
        /// 設定を入れた瞬間からゲストの状態がズレたままになるのを防ぐ
        /// </summary>
        internal static void OnLinkEnabledChanged(bool linkEnabled)
        {
            if (!linkEnabled)
            {
                return;
            }

            NotifyEditorEnabledChanged(isEditorEnabled);
        }
    }
}
