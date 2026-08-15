using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// ポーズの anm ファイル保存/読み込み。
    /// スタジオモードのマイポーズと同じ形式・同じ保存先（PhotoModeData\MyPose）を使う
    /// </summary>
    public static class MaidPoseFileManager
    {
        /// <summary>スタジオモードのマイポーズと同じカテゴリ名。モーション一覧の特別扱い判定に使う</summary>
        public const string MY_POSE_CATEGORY = "マイポーズ";

        /// <summary>ポーズ名の最大長。スタジオモードの PhotoModePoseSave に合わせる</summary>
        private const int MAX_POSE_NAME_LENGTH = 250;

        /// <summary>スタジオモードのマイポーズと同じフォルダ</summary>
        public static string poseFolderPath
            => Path.Combine(PhotoWindowManager.path_photo_folder, "MyPose");

        // subDir を受け取る各 API は、呼び出し元が GetSubDirectoryNames の列挙結果から
        // 組み立てた相対パスだけを渡す契約。任意入力を渡すと ".." で保存先の外を指せる

        /// <summary>
        /// 指定サブディレクトリ内のポーズ名一覧 ("" はルート)。
        /// GUI 描画中に呼ばれるため、列挙に失敗しても例外を投げず空リストを返す
        /// </summary>
        public static List<string> GetPoseFileNames(string subDir = "")
        {
            try
            {
                var folder = Path.Combine(poseFolderPath, subDir);
                if (!Directory.Exists(folder))
                {
                    return new List<string>();
                }
                return Directory.GetFiles(folder, "*.anm")
                    .Select(Path.GetFileNameWithoutExtension)
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToList();
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
                return new List<string>();
            }
        }

        /// <summary>指定サブディレクトリ直下のフォルダ名一覧 ("" はルート)。失敗時は空リスト</summary>
        public static List<string> GetSubDirectoryNames(string subDir = "")
        {
            try
            {
                var folder = Path.Combine(poseFolderPath, subDir);
                if (!Directory.Exists(folder))
                {
                    return new List<string>();
                }
                return Directory.GetDirectories(folder)
                    .Select(Path.GetFileName)
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToList();
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
                return new List<string>();
            }
        }

        public static bool Exists(string poseName)
        {
            return File.Exists(GetPoseFilePath(poseName));
        }

        /// <summary>ポーズ名の検証。問題があればエラーメッセージ、なければ null を返す</summary>
        public static string ValidatePoseName(string poseName)
        {
            if (string.IsNullOrEmpty(poseName))
            {
                return "ポーズ名を入力してください";
            }
            if (poseName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                return "ポーズ名に使用できない文字が含まれています";
            }
            if (poseName.Length > MAX_POSE_NAME_LENGTH)
            {
                return "ポーズ名が長すぎます（" + MAX_POSE_NAME_LENGTH + "文字まで）";
            }
            return null;
        }

        /// <summary>
        /// 現在のボーン状態を anm として保存する。上書き確認は UI 側の責務。
        /// スタジオモードの PoseEditWindow と同じく CacheBoneDataArray から書き出す
        /// </summary>
        public static void SavePose(Maid maid, string poseName)
        {
            try
            {
                var binary = CapturePoseBinary(maid);
                if (binary == null)
                {
                    DialogPopupWindow.ShowDialog("ポーズの取得に失敗しました");
                    return;
                }

                var filePath = GetPoseFilePath(poseName);
                var folder = Path.GetDirectoryName(filePath);
                if (!Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                }
                File.WriteAllBytes(filePath, binary);
                MTEUtils.Log("ポーズを保存しました: {0}", poseName);
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
                DialogPopupWindow.ShowDialog("ポーズの保存に失敗しました");
            }
        }

        /// <summary>
        /// anm を読み込んで適用し、そのままボーン編集を続けられるよう停止状態にする
        /// </summary>
        public static void LoadPose(Maid maid, string poseName)
        {
            try
            {
                var filePath = GetPoseFilePath(poseName);
                if (!File.Exists(filePath))
                {
                    DialogPopupWindow.ShowDialog("ポーズファイルが見つかりません: " + poseName);
                    return;
                }

                var binary = File.ReadAllBytes(filePath);
                // サブディレクトリ内でもクリップ名はファイル名だけにし、再生中表示の突き合わせを揃える
                ApplyPoseBinary(maid, binary, Path.GetFileName(poseName) + ".anm");
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
                DialogPopupWindow.ShowDialog("ポーズの読み込みに失敗しました");
            }
        }

        /// <summary>
        /// 現在のボーン状態を anm バイナリとして取得する。失敗時は null。
        /// シーンプリセットのポーズ記録にも使う
        /// </summary>
        public static byte[] CapturePoseBinary(Maid maid)
        {
            var cacheBoneData = GetOrCreateCacheBoneData(maid);

            // バストキー = 「胸を物理ではなくキーで動かす」フラグなので、揺れ OFF が true。
            // Mune_* のボーンキー自体はフラグに関わらず常に書き出される
            var controller = MaidManipulateManager.instance.muneYureController;
            var useBustKeyL = !controller.GetYure(maid, true);
            var useBustKeyR = !controller.GetYure(maid, false);

            var binary = cacheBoneData.GetAnmBinary(useBustKeyL, useBustKeyR);
            return (binary == null || binary.Length == 0) ? null : binary;
        }

        /// <summary>
        /// anm バイナリを適用し、ボーン編集を続けられるよう停止状態にする。
        /// シーンプリセットのポーズ復元にも使う
        /// </summary>
        public static void ApplyPoseBinary(Maid maid, byte[] binary, string clipName)
        {
            // 物理が動いたまま CrossFade すると手付けの胸が即座に上書きされるため、
            // ゲーム側 PhotoMotionData.Apply と同じく先にフラグを反映する
            ApplyBustKeyFlags(maid, binary);

            // CrossFade の前に停止して元モーション名を控える。
            // 読み込んだ後に控えると、リセットの復帰先が読み込んだポーズ自身になってしまう
            MaidMotionState.StopMotion(maid);

            DetachAllIK(maid);

            // スタジオモードのマイポーズ再生と同経路（PhotoMotionData 参照）
            maid.body0.CrossFade(clipName, binary,
                additive: false, loop: true, boAddQue: false, fade: 0f);

            // 即座にポーズを反映してから停止し、ボーン編集を継続できる状態にする。
            // CrossFade で再生が再開されるため、読み込んだポーズを基準として停止し直す
            // （復帰先のクリップ名は上で控えた分が保持される）
            var anim = maid.GetAnimation();
            if (anim != null)
            {
                anim.Sample();
            }
            MaidMotionState.StopMotion(maid);
        }

        /// <summary>anm の先頭マジックと、バストキーが載り始めたバージョン</summary>
        private const string ANM_MAGIC = "CM3D2_ANIM";
        private const int ANM_BUST_KEY_VERSION = 1001;

        /// <summary>
        /// anm 末尾 2 バイトのバストキーフラグを読み、胸の揺れもの状態へ反映する。
        /// フラグは「胸をキーで動かす」= 揺れ OFF を意味する。
        /// 判別できない anm ではフラグを変更せず、現在の状態を保つ
        /// </summary>
        private static void ApplyBustKeyFlags(Maid maid, byte[] binary)
        {
            if (maid == null || binary == null)
            {
                return;
            }

            try
            {
                using (var stream = new MemoryStream(binary))
                using (var reader = new BinaryReader(stream))
                {
                    if (reader.ReadString() != ANM_MAGIC)
                    {
                        return;
                    }
                    if (reader.ReadInt32() < ANM_BUST_KEY_VERSION)
                    {
                        return;
                    }
                }

                if (binary.Length < 2)
                {
                    return;
                }

                var useBustKeyL = binary[binary.Length - 2] != 0;
                var useBustKeyR = binary[binary.Length - 1] != 0;

                var controller = MaidManipulateManager.instance.muneYureController;
                controller.SetYure(maid, true, !useBustKeyL);
                controller.SetYure(maid, false, !useBustKeyR);
            }
            catch (Exception e)
            {
                // ポーズ適用自体は続行する。フラグは現状維持
                MTEUtils.LogWarning("anm のバストキーを読めませんでした: {0}", e.Message);
            }
        }

        /// <summary>
        /// IK を全て外す。付いたままだと手足が IK ターゲットに引かれてポーズが反映されない
        /// （スタジオモードの PhotoMotionData.Apply も CrossFade 直前に AllIKDetach している）。
        /// 2.5 は FullBodyIKMgr 経由、2.0 は Maid 直下と経路が異なる
        /// </summary>
        private static void DetachAllIK(Maid maid)
        {
#if COM3D25
            if (maid.fullBodyIK != null)
            {
                maid.fullBodyIK.AllIKDetach();
            }
#else
            maid.AllIKDetach();
#endif
        }

        /// <summary>poseName はサブディレクトリを含む相対パス ("sub\name") でもよい</summary>
        private static string GetPoseFilePath(string poseName)
        {
            return Path.Combine(poseFolderPath, poseName + ".anm");
        }

        /// <summary>
        /// ゲーム側 IKManager と同じ流儀で CacheBoneDataArray を取得/生成する。
        /// キャッシュはボーンの Transform を直接持つため、ボディ再構築で不正になる。
        /// CreateCache は全状態を作り直す冪等な実装なので、保存のたびに張り直す
        /// </summary>
        private static CacheBoneDataArray GetOrCreateCacheBoneData(Maid maid)
        {
            var cache = maid.gameObject.GetComponent<CacheBoneDataArray>();
            if (cache == null)
            {
                cache = maid.gameObject.AddComponent<CacheBoneDataArray>();
            }
            cache.CreateCache(maid.body0.GetBone("Bip01"));
            return cache;
        }
    }
}
