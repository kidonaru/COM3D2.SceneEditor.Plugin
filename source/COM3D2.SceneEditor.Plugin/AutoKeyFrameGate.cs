namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 操作確定時に現在フレームへ自動でキーフレーム登録するかの判定。
    /// ドラッグ完了と操作履歴の確定の両方から使うため、Unity 型に依存しない純粋ロジックにしている
    /// </summary>
    public static class AutoKeyFrameGate
    {
        /// <param name="isAutoKeyFrame">「自動登録」トグルの状態</param>
        /// <param name="isEditing">タイムラインの編集モード中か (編集開始時スナップショットがあるか)</param>
        /// <param name="editedMaid">
        /// 操作対象のメイド。ライト・カメラ等メイドに紐づかない操作は null。
        /// ここは参照比較しかしないため、Unity の破棄済みオブジェクト (fake-null) の判定は
        /// 呼び出し側で済ませてから渡すこと
        /// </param>
        /// <param name="activeMaid">タイムラインのアクティブメイド。未配置なら null</param>
        public static bool ShouldRegister(
            bool isAutoKeyFrame, bool isEditing, object editedMaid, object activeMaid)
        {
            if (!isAutoKeyFrame || !isEditing)
            {
                return false;
            }

            // 登録対象レイヤーはアクティブメイドのスロットに限られるため、
            // 別メイドへの操作は差分が出ず、登録しても無駄になる
            if (editedMaid != null && !ReferenceEquals(editedMaid, activeMaid))
            {
                return false;
            }

            return true;
        }
    }
}
