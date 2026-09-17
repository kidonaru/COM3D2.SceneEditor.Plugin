using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// モデルボーン 1 本分の Transform 行 (位置 / 回転 / 拡縮)。
    ///
    /// ボーン編集ウィンドウ経由の編集は BoneEditManager の差分ストアへ記録され、
    /// リセット・プリセット保存の対象になる。Inspector から編集したときも同じ扱いに
    /// なるよう、素の Transform 書き込みではなくストアへの記録を伴う形で描く。
    /// BoneEditManager の Selected〜 系 API は「選択中の 1 本」しか扱えず、
    /// タイムラインの項目 (任意のボーン) には使えないため、
    /// 同じ意味の処理をモデルとボーンを指定する形でここに用意している。
    ///
    /// 回転オフセットのキャッシュを持つため、描画するビューごとにインスタンスを分ける
    /// </summary>
    public class ModelBoneRowDrawer
    {
        private static BoneEditManager boneEditManager => BoneEditManager.instance;

        private readonly EulerOffsetCache _offsetCache = new EulerOffsetCache();

        /// <param name="model">ボーンが属するモデルのルート (差分ストアのキー)</param>
        /// <param name="bone">編集対象のボーン</param>
        public void Draw(
            GUIView view,
            GameObject model,
            Transform bone,
            float labelWidth,
            float scaleLabelWidth,
            float rowHeight)
        {
            // ギズモの Local/Global に合わせて表示・編集する座標系を切り替える
            // (ボーン編集ウィンドウのボーン行と同じ)
            var useLocal = GizmoRenderer.useLocalSpace;

            // 位置は 0 が原点ではないため、リセットは編集前の値へ戻す (拡縮も同様)
            Vector3RowDrawer.Draw(view, "位置", ObjectTransformRowDrawer.PositionSensitivity,
                labelWidth, rowHeight,
                useLocal ? bone.localPosition : bone.position,
                value =>
                {
                    BeginEdit(model, bone);
                    if (useLocal)
                    {
                        bone.localPosition = value;
                    }
                    else
                    {
                        bone.position = value;
                    }
                    RecordEdit(model, bone);
                },
                () =>
                {
                    BeginEdit(model, bone);
                    bone.localPosition = GetBasePosition(model, bone);
                    RecordEdit(model, bone);
                });

            // 回転は元の姿勢を 0 とするオフセット角なので、リセットは 0 で正しい
            Vector3RowDrawer.Draw(view, "回転", ObjectTransformRowDrawer.RotationSensitivity,
                labelWidth, rowHeight,
                _offsetCache.GetOffsetFromLocalBase(
                    bone, GetBaseRotation(model, bone), useLocal),
                value =>
                {
                    BeginEdit(model, bone);
                    _offsetCache.SetOffsetFromLocalBase(
                        bone, GetBaseRotation(model, bone), value, useLocal);
                    RecordEdit(model, bone);
                },
                () =>
                {
                    BeginEdit(model, bone);
                    _offsetCache.SetOffsetFromLocalBase(
                        bone, GetBaseRotation(model, bone), Vector3.zero, useLocal);
                    RecordEdit(model, bone);
                });

            ScaleRowDrawer.Draw(view, bone.localScale, scaleLabelWidth, rowHeight,
                value =>
                {
                    BeginEdit(model, bone);
                    bone.localScale = value;
                    RecordEdit(model, bone);
                },
                () =>
                {
                    BeginEdit(model, bone);
                    bone.localScale = GetBaseScale(model, bone);
                    RecordEdit(model, bone);
                });
        }

        /// <summary>
        /// 書き込み前の準備。履歴へ変更前を控え、差分ストアの元値も先に確保する。
        /// 元値の確保を書き込み後にすると「編集後の値」が元値になりリセットが効かなくなる
        /// (BoneEditManager.EnsureOrigRecorded と同じ理由)
        /// </summary>
        private static void BeginEdit(GameObject model, Transform bone)
        {
            // モデルは特定メイドに紐付かないため Object スコープで記録する
            // (BoneEditManager.BeginEditHistory のモデルモードと同じ)
            HistoryManager.instance.BeforeEdit(
                null, HistoryScope.Object, "ボーン編集: " + bone.name, new[] { bone });

            if (FindEntry(model, bone) == null)
            {
                RecordEdit(model, bone);
            }
        }

        private static void RecordEdit(GameObject model, Transform bone)
        {
            boneEditManager.GetModelStore(model)
                .RecordEdit(BoneEditManager.ModelSlotKey, null, bone);
        }

        /// <summary>モデルの差分ストアからエントリを引く。新規生成はしない (未編集なら null)</summary>
        private static BoneEditEntry FindEntry(GameObject model, Transform bone)
        {
            var store = boneEditManager.FindModelStore(model);
            return store != null
                ? store.GetEntry(BoneEditManager.ModelSlotKey, bone.name) : null;
        }

        /// <summary>基準の姿勢。編集済みなら記録時の元値、未編集なら現在値 (= オフセット 0)</summary>
        private static Quaternion GetBaseRotation(GameObject model, Transform bone)
        {
            var entry = FindEntry(model, bone);
            return entry != null ? entry.origRotation : bone.localRotation;
        }

        /// <summary>基準のローカル位置。0 が原点ではないためリセットはこの値へ戻す</summary>
        private static Vector3 GetBasePosition(GameObject model, Transform bone)
        {
            var entry = FindEntry(model, bone);
            return entry != null ? entry.origPosition : bone.localPosition;
        }

        /// <summary>基準のローカルスケール。GetBasePosition と同じ趣旨</summary>
        private static Vector3 GetBaseScale(GameObject model, Transform bone)
        {
            var entry = FindEntry(model, bone);
            return entry != null ? entry.origScale : bone.localScale;
        }
    }
}
