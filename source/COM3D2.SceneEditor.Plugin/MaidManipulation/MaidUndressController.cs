using System;
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using MaidExtension;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// スタジオモードの脱衣ウィンドウ相当のロジック。
    /// 脱衣は着替えではなく TBody のマスク (SetMask) 切替なので即時反映され、
    /// AllProcPropSeqStart は不要。
    /// カテゴリ定義は公式と同じ phot_undressing_list.nei から読む
    /// </summary>
    public static class MaidUndressController
    {
        public class UndressCategory
        {
            /// <summary>nei ファイル側のカテゴリ名と一致させること</summary>
            private const string HeaddressCategoryName = "ヘッドドレス";

            private static readonly List<TBody.SlotID> HeaddressSlotIds =
                new List<TBody.SlotID> { TBody.SlotID.headset };
            private static readonly List<TBody.SlotID> HeaddressCrcSlotIds =
                new List<TBody.SlotID> { TBody.SlotID.accHat };

            public string name;

            /// <summary>○ 指定の行。女性の新ボディ (CRC) では非表示にするカテゴリ</summary>
            public bool crcHiddenCategory;

            public readonly List<TBody.SlotID> slotIds = new List<TBody.SlotID>();

            /// <summary>
            /// 対象スロットを返す。ヘッドドレスだけはボディ種別でスロットが異なる
            /// (公式 PhotUndressingManager.UpdateMaskStatus と同じ動的差し替え)
            /// </summary>
            public List<TBody.SlotID> GetSlotIds(Maid maid)
            {
                if (name == HeaddressCategoryName)
                {
                    return maid.IsCrcBody ? HeaddressCrcSlotIds : HeaddressSlotIds;
                }
                return slotIds;
            }
        }

        /// <summary>めくれ系チェックボックスの種別。表示順もこの順</summary>
        public static readonly MaidCostumeChangeController.CostumeType[] CostumeTypes =
        {
            MaidCostumeChangeController.CostumeType.MekureFront,
            MaidCostumeChangeController.CostumeType.MekureBack,
            MaidCostumeChangeController.CostumeType.Zurasi,
            MaidCostumeChangeController.CostumeType.Hadake,
        };

        public static string GetCostumeTypeName(MaidCostumeChangeController.CostumeType type)
        {
            switch (type)
            {
                case MaidCostumeChangeController.CostumeType.MekureFront: return "めくれ(前)";
                case MaidCostumeChangeController.CostumeType.MekureBack: return "めくれ(後)";
                case MaidCostumeChangeController.CostumeType.Zurasi: return "ずらし";
                case MaidCostumeChangeController.CostumeType.Hadake: return "はだけ";
                default: return type.ToString();
            }
        }

        private static List<UndressCategory> _categories = null;

        public static List<UndressCategory> categories
        {
            get
            {
                if (_categories == null)
                {
                    _categories = LoadCategories();
                }
                return _categories;
            }
        }

        /// <summary>
        /// phot_undressing_list.nei からカテゴリ→スロットの対応を読む。
        /// 列構成: カテゴリ名, スロットリスト, CRC 用スロットリスト(あれば優先), CRC 非表示フラグ(○)
        /// </summary>
        private static List<UndressCategory> LoadCategories()
        {
            var result = new List<UndressCategory>();
            try
            {
                using (var file = GameUty.FileSystem.FileOpen("phot_undressing_list.nei"))
                using (var csv = new CsvParser())
                {
                    if (!csv.Open(file))
                    {
                        MTEUtils.LogError("phot_undressing_list.nei を開けませんでした");
                        return result;
                    }
                    for (var i = 1; i < csv.max_cell_y; i++)
                    {
                        if (!csv.IsCellToExistData(0, i))
                        {
                            continue;
                        }

                        var col = 0;
                        var name = csv.GetCellAsString(col++, i);
                        var slotNames = csv.GetCellAsString(col++, i);
                        if (Product.isCREditSystemSupport && csv.IsCellToExistData(col, i))
                        {
                            slotNames = csv.GetCellAsString(col, i);
                        }
                        var crcFlag = csv.GetCellAsString(++col, i);

                        if (string.IsNullOrEmpty(slotNames))
                        {
                            continue;
                        }

                        var category = result.Find(c => c.name == name);
                        if (category == null)
                        {
                            category = new UndressCategory { name = name };
                            result.Add(category);
                        }
                        category.crcHiddenCategory = crcFlag == "○" || crcFlag == "〇";

                        foreach (var slotName in slotNames.Split(','))
                        {
                            try
                            {
                                category.slotIds.Add(
                                    (TBody.SlotID)Enum.Parse(typeof(TBody.SlotID), slotName));
                            }
                            catch
                            {
                                MTEUtils.LogError("脱衣カテゴリのスロット名を解釈できません: {0}", slotName);
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                MTEUtils.LogError("phot_undressing_list.nei の読み込みに失敗しました");
                MTEUtils.LogException(e);
            }
            return result;
        }

        /// <summary>カテゴリのボタンを出すか (公式の gameObject.SetActive 相当)</summary>
        public static bool IsVisible(Maid maid, UndressCategory category)
        {
            if (maid == null || maid.body0 == null)
            {
                return false;
            }
            if (category.crcHiddenCategory && !maid.boMAN && maid.IsCrcBody)
            {
                return false;
            }
            return category.GetSlotIds(maid).Count > 0;
        }

        /// <summary>カテゴリを操作できるか。対象スロットに何も装着していなければ不可</summary>
        public static bool IsEnabled(Maid maid, UndressCategory category)
        {
            if (maid == null || maid.body0 == null)
            {
                return false;
            }
            foreach (var slotId in category.GetSlotIds(maid))
            {
                if (maid.body0.GetSlotLoaded(slotId))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>脱衣中 (全対象スロットがマスクで非表示) か</summary>
        public static bool IsUndressed(Maid maid, UndressCategory category)
        {
            if (maid == null || maid.body0 == null)
            {
                return false;
            }
            foreach (var slotId in category.GetSlotIds(maid))
            {
                if (maid.body0.GetMask(slotId))
                {
                    return false;
                }
            }
            return true;
        }

        public static void SetUndressed(Maid maid, UndressCategory category, bool undressed)
        {
            if (maid == null || maid.body0 == null)
            {
                return;
            }

            foreach (var slotId in category.GetSlotIds(maid))
            {
                SetSlotMask(maid, slotId, !undressed);
            }
        }

        /// <summary>
        /// スロット単位のマスク書き込みの唯一の入口。
        /// 脱衣ウィンドウ (カテゴリ単位) もタイムラインの脱衣レイヤー (スロット単位) も
        /// 最終的にここへ合流し、MaskMode の解除が必ず走るようにする
        /// </summary>
        public static void SetSlotMask(Maid maid, TBody.SlotID slotId, bool visible)
        {
            if (maid == null || maid.body0 == null)
            {
                return;
            }

            EnsureIndividualMaskMode(maid.body0);
            maid.body0.SetMask(slotId, visible);
        }

        /// <summary>
        /// MaskMode (Nude 等) が効いていると個別マスクと干渉するため、
        /// 公式の OnMaidAddEvent と同様に個別制御へ切り替える。
        /// SetMaskMode(None) は全スロットを一旦可視へリセットするため、
        /// 解除が走った直後は操作対象外のスロットも表示に戻る (公式と同じ挙動)
        /// </summary>
        private static void EnsureIndividualMaskMode(TBody body)
        {
            if (GetMaskMode(body) != TBody.MaskMode.None)
            {
                body.SetMaskMode(TBody.MaskMode.None);
            }
        }

#if COM3D25
        private static TBody.MaskMode GetMaskMode(TBody body)
        {
            return body.GetMaskMode();
        }
#else
        private static readonly System.Reflection.FieldInfo _maskModeField =
            typeof(TBody).GetField("m_eMaskMode",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        /// <summary>2.0 の TBody には GetMaskMode が無いため private フィールドを直接読む</summary>
        private static TBody.MaskMode GetMaskMode(TBody body)
        {
            return (TBody.MaskMode)_maskModeField.GetValue(body);
        }
#endif

        /// <summary>
        /// 脱衣中（装着済みかつマスクで非表示）のスロット名を列挙する。
        /// シーンプリセットの保存用
        /// </summary>
        public static List<string> CaptureUndressedSlots(Maid maid)
        {
            var result = new List<string>();
            if (maid == null || maid.body0 == null)
            {
                return result;
            }

            foreach (var category in categories)
            {
                if (!IsVisible(maid, category))
                {
                    continue;
                }
                foreach (var slotId in category.GetSlotIds(maid))
                {
                    if (maid.body0.GetSlotLoaded(slotId) && !maid.body0.GetMask(slotId)
                        && !result.Contains(slotId.ToString()))
                    {
                        result.Add(slotId.ToString());
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// 脱衣状態を復元する。脱衣カテゴリ対象のスロットだけを操作し、
        /// リストにあるものは脱衣・ないものは着衣へ戻す（無関係なマスクは触らない）。
        /// MaskMode (Nude 等) は個別制御へ解除されるが、保存時の見かけのマスク状態を
        /// スロット単位で復元するため見た目は保存時と一致する
        /// </summary>
        public static void ApplyUndressedSlots(Maid maid, List<string> undressedSlotNames)
        {
            if (maid == null || maid.body0 == null)
            {
                return;
            }

            foreach (var category in categories)
            {
                if (!IsVisible(maid, category))
                {
                    continue;
                }
                foreach (var slotId in category.GetSlotIds(maid))
                {
                    if (!maid.body0.GetSlotLoaded(slotId))
                    {
                        continue;
                    }
                    var undressed = undressedSlotNames.Contains(slotId.ToString());
                    SetSlotMask(maid, slotId, !undressed);
                }
            }
        }

        /// <summary>ON のめくれ系種別名を列挙する。シーンプリセットの保存用</summary>
        public static List<string> CaptureCostumeTypes(Maid maid)
        {
            var result = new List<string>();
            foreach (var type in CostumeTypes)
            {
                if (IsCostumeTypeEnabled(maid, type)
                    && maid.mekureController.IsEnabledCostumeType(type))
                {
                    result.Add(type.ToString());
                }
            }
            return result;
        }

        /// <summary>めくれ系を復元する。操作可能な種別だけリストの ON/OFF に合わせる</summary>
        public static void ApplyCostumeTypes(Maid maid, List<string> enabledTypeNames)
        {
            foreach (var type in CostumeTypes)
            {
                if (IsCostumeTypeEnabled(maid, type))
                {
                    maid.mekureController.SetEnabledCostumeType(
                        type, enabledTypeNames.Contains(type.ToString()));
                }
            }
        }

        /// <summary>全脱衣 / 全着衣。操作可能な全カテゴリへ一括適用する</summary>
        public static void SetAllUndressed(Maid maid, bool undressed)
        {
            foreach (var category in categories)
            {
                if (IsVisible(maid, category) && IsEnabled(maid, category))
                {
                    SetUndressed(maid, category, undressed);
                }
            }
        }

        public static bool IsCostumeTypeEnabled(
            Maid maid, MaidCostumeChangeController.CostumeType type)
        {
            if (maid == null || Product.isPublic)
            {
                return false;
            }
            // はだけは新ボディ (CRC) 専用 (公式 WindowPartsMekureCheckBox と同じ制限)
            if (type == MaidCostumeChangeController.CostumeType.Hadake && !maid.IsCrcBody)
            {
                return false;
            }
            return maid.mekureController.IsSupportedCostumeType(type);
        }
    }
}
