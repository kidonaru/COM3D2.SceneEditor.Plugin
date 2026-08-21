using System;
using System.Collections.Generic;
using System.IO;
using COM3D2.MotionTimelineEditor;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// スタジオモードのモーション一覧 (PhotoMotionData) の参照と適用。
    /// カテゴリ分けはゲーム側の構築結果 (category_list) をそのまま使う
    /// </summary>
    public static class PhotoMotionUtils
    {
        /// <summary>カテゴリ一覧のキャッシュ。男女でモーションの所属が異なるため別々に持つ</summary>
        private static List<string> _categories = null;
        private static List<string> _categoriesForMan = null;

        /// <summary>
        /// モーション一覧を用意する。スタジオモード未経由だと未構築のため初回にここで構築する。
        /// 使える一覧が得られたかを返す
        /// </summary>
        public static bool EnsureMotionDataLoaded()
        {
            try
            {
                if (PhotoMotionData.data == null)
                {
                    PhotoMotionData.Create();
                }
            }
            catch (Exception e)
            {
                // ファイルシステム未初期化などで失敗しても描画は続行する
                MTEUtils.LogException(e);
            }
            return PhotoMotionData.data != null && PhotoMotionData.data.Count > 0;
        }

        /// <summary>
        /// 対象 (男女) が使えるモーションを含むカテゴリ一覧。
        /// マイポーズはウィンドウ側が別経路 (MaidPoseFileManager) で扱うため除外する
        /// </summary>
        public static List<string> GetCategories(bool forMan)
        {
            var cached = forMan ? _categoriesForMan : _categories;
            if (cached != null)
            {
                return cached;
            }

            // 未構築のまま空リストをキャッシュすると構築後も空のままになるため、キャッシュせず返す
            if (PhotoMotionData.category_list == null)
            {
                return new List<string>();
            }

            var categories = new List<string>();
            foreach (var pair in PhotoMotionData.category_list)
            {
                if (pair.Key == MaidPoseFileManager.MY_POSE_CATEGORY)
                {
                    continue;
                }
                foreach (var data in pair.Value)
                {
                    if (data.is_man_pose == forMan)
                    {
                        categories.Add(pair.Key);
                        break;
                    }
                }
            }

            if (forMan)
            {
                _categoriesForMan = categories;
            }
            else
            {
                _categories = categories;
            }
            return categories;
        }

        /// <summary>カテゴリ内で対象 (男女) が使えるモーションを列挙する</summary>
        public static IEnumerable<PhotoMotionData> GetMotions(string category, bool forMan)
        {
            List<PhotoMotionData> list;
            if (PhotoMotionData.category_list == null ||
                !PhotoMotionData.category_list.TryGetValue(category, out list))
            {
                yield break;
            }
            foreach (var data in list)
            {
                if (data.is_man_pose == forMan)
                {
                    yield return data;
                }
            }
        }

        /// <summary>
        /// モーションを適用する。ポーズ編集用の停止状態は破棄して再生を始める
        /// (適用はスタジオモードと同じ PhotoMotionData.Apply 経路)
        /// </summary>
        public static void Apply(Maid maid, PhotoMotionData data)
        {
            try
            {
                // 編集中ポーズの停止状態が残っていると、リセットや基準ポーズが
                // 適用前のモーションを指したままになるため先に破棄する
                MaidMotionState.Discard(maid);
                data.Apply(maid);
                // スクリプト経由エントリはクリップ名から特定できないため、何を当てたかを記録する
                MaidMotionState.RecordAppliedMotion(maid, data);
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
                DialogPopupWindow.ShowDialog("モーションの適用に失敗しました");
            }
        }

        /// <summary>
        /// クリップ名から一覧のモーションを引き当てる。見つからなければ null。
        /// マイポーズは別経路 (MaidPoseFileManager) の扱いなので対象外
        /// </summary>
        public static PhotoMotionData FindByClipName(string clipName)
        {
            if (string.IsNullOrEmpty(clipName) || !EnsureMotionDataLoaded())
            {
                return null;
            }

            foreach (var data in PhotoMotionData.data)
            {
                if (!data.is_mypose && IsCurrentMotion(data, clipName))
                {
                    return data;
                }
            }
            return null;
        }

        /// <summary>
        /// 記録した id / パスから一覧のモーションを引き当てる。見つからなければ null。
        /// Mod の id はファイル内容の CRC のため、更新されると一致しなくなる。
        /// その場合に備えてパス (バニラはファイル名、Mod は絶対パス) でも引き当てる。
        /// マイポーズは FindByClipName と同じく対象外
        /// </summary>
        public static PhotoMotionData Find(long id, string file)
        {
            if (!EnsureMotionDataLoaded())
            {
                return null;
            }

            foreach (var data in PhotoMotionData.data)
            {
                if (!data.is_mypose && data.id == id)
                {
                    return data;
                }
            }

            if (string.IsNullOrEmpty(file))
            {
                return null;
            }
            foreach (var data in PhotoMotionData.data)
            {
                if (!data.is_mypose
                    && string.Equals(data.direct_file, file, StringComparison.OrdinalIgnoreCase))
                {
                    return data;
                }
            }
            return null;
        }

        /// <summary>
        /// data が現在当たっているクリップ (clipName) のモーションかどうか。
        /// バニラは direct_file 名 (新ボディ男は crc_ 付き)、
        /// Mod は id 文字列がクリップ名になるため両方と突き合わせる
        /// </summary>
        public static bool IsCurrentMotion(PhotoMotionData data, string clipName)
        {
            if (string.IsNullOrEmpty(clipName))
            {
                return false;
            }
            if (string.Equals(clipName, data.id.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (string.IsNullOrEmpty(data.direct_file))
            {
                return false;
            }
            var fileName = Path.GetFileName(data.direct_file);
            return string.Equals(clipName, fileName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(clipName, "crc_" + fileName, StringComparison.OrdinalIgnoreCase);
        }
    }
}
