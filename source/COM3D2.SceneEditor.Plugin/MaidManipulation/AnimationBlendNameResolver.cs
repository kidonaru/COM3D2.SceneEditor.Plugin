using System.IO;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// ブレンド層 (アニメレイヤー 2〜8) に載せるアニメ名の決定。
    /// anmName はタイムラインの MaidCache.LoadAnimationLayer が再ロードできる形にそろえる
    /// (公式: ファイル名、Mod: 絶対パス、マイポーズ: 保存フォルダからの相対パス)。
    /// Unity に依存しない pure なロジックだけを置く
    /// </summary>
    public static class AnimationBlendNameResolver
    {
        /// <summary>
        /// anmName に対応する AnimationState.name。
        /// CrossFadeLayer 系は state 名を小文字のファイル名にするため、
        /// サブフォルダ入りマイポーズや絶対パスではファイル名部分で突き合わせる。
        /// 小文字化はここで行うので、呼び出し側は生の anmName を渡してよい
        /// </summary>
        public static string GetStateTag(string anmName)
        {
            if (string.IsNullOrEmpty(anmName))
            {
                return "";
            }
            return Path.GetFileName(anmName).ToLower();
        }

        /// <summary>
        /// 一覧のモーションから anmName を決める。
        /// applyCrcPrefix は PhotoMotionData.Apply と同じ「新ボディ有効かつ男」の判定を呼び出し側で済ませて渡す。
        /// direct_file が無い (スクリプト経由) モーションはレイヤーへ載せられないため null。
        /// マイポーズ (is_mypose) には crc_ が付かないため、そちらは ResolveMyPose を使うこと
        /// (ゲーム側 PhotoMotionData.Apply も !is_mod &amp;&amp; !is_mypose を条件にしている)
        /// </summary>
        public static string ResolveMotion(string directFile, bool isMod, bool applyCrcPrefix)
        {
            if (string.IsNullOrEmpty(directFile))
            {
                return null;
            }
            if (!isMod && applyCrcPrefix)
            {
                return "crc_" + directFile;
            }
            return directFile;
        }

        /// <summary>マイポーズの相対パス ("sub\name") を保存ファイル名に揃える</summary>
        public static string ResolveMyPose(string relativePath)
        {
            return relativePath + ".anm";
        }
    }
}
