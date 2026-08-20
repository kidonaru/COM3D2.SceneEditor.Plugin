using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>表情モーフのカテゴリ。UI の内部タブと 1:1 対応</summary>
    public enum FaceMorphCategory
    {
        目,
        眉,
        口,
        オプション,
    }

    /// <summary>表情モーフ 1 つ分の定義</summary>
    public class FaceMorphDef
    {
        public string name;
        public string displayName;
        /// <summary>トグル扱い (0/1) にするか。オプション系はオンオフで十分なため</summary>
        public bool isToggle;

        public FaceMorphDef(string name, string displayName, bool isToggle = false)
        {
            this.name = name;
            this.displayName = displayName;
            this.isToggle = isToggle;
        }
    }

    /// <summary>
    /// メイドの表情モーフを TMorph 直接操作で読み書きする。
    /// COM3D2.5 の CRC 顔はモーフ名に顔タイプのサフィックス（_normal 等）が付くため、
    /// 素の名前で見つからないときは現在の顔タイプで解決し直す
    /// </summary>
    public static class MaidFaceMorphController
    {
        /// <summary>カテゴリごとのモーフ定義。名前は MultipleMaids と同じ標準モーフ名</summary>
        private static readonly Dictionary<FaceMorphCategory, FaceMorphDef[]> MorphDefs =
            new Dictionary<FaceMorphCategory, FaceMorphDef[]>
        {
            {
                FaceMorphCategory.目, new[]
                {
                    new FaceMorphDef("eyeclose", "目閉じ"),
                    new FaceMorphDef("eyeclose2", "笑顔"),
                    new FaceMorphDef("eyeclose3", "ジト目"),
                    // eyeclose5/6 は左目、eyeclose7/8 は右目（メイドから見た左右）を閉じる。
                    // 6/8 は 2/笑顔 の片目版で、旧ボディの顔には 7/8 が無い
                    new FaceMorphDef("eyeclose5", "ウィンク左"),
                    new FaceMorphDef("eyeclose7", "ウィンク右"),
                    new FaceMorphDef("eyeclose6", "ウィンク左（笑顔）"),
                    new FaceMorphDef("eyeclose8", "ウィンク右（笑顔）"),
                    new FaceMorphDef("eyebig", "見開き"),
                    new FaceMorphDef("hitomih", "瞳大"),
                    new FaceMorphDef("hitomis", "瞳小"),
                }
            },
            {
                FaceMorphCategory.眉, new[]
                {
                    new FaceMorphDef("mayuha", "眉ハの字"),
                    new FaceMorphDef("mayuw", "眉困り"),
                    new FaceMorphDef("mayuup", "眉上げ"),
                    new FaceMorphDef("mayuv", "眉怒り"),
                    new FaceMorphDef("mayuvhalf", "眉怒り半"),
                }
            },
            {
                FaceMorphCategory.口, new[]
                {
                    new FaceMorphDef("moutha", "口あ"),
                    new FaceMorphDef("mouths", "口す"),
                    new FaceMorphDef("mouthc", "口開け"),
                    new FaceMorphDef("mouthi", "口い"),
                    new FaceMorphDef("mouthup", "口角上げ"),
                    new FaceMorphDef("mouthdw", "口角下げ"),
                    new FaceMorphDef("mouthhe", "口へ"),
                    new FaceMorphDef("mouthuphalf", "口角上げ半"),
                    new FaceMorphDef("tangout", "舌出し"),
                    new FaceMorphDef("tangup", "舌上げ"),
                    new FaceMorphDef("tangopen", "舌開き"),
                }
            },
            {
                FaceMorphCategory.オプション, new[]
                {
                    new FaceMorphDef("hohos", "頬染め小", isToggle: true),
                    new FaceMorphDef("hoho", "頬染め", isToggle: true),
                    new FaceMorphDef("hoho2", "頬染め大", isToggle: true),
                    new FaceMorphDef("hohol", "頬染め特大", isToggle: true),
                    new FaceMorphDef("namida", "涙", isToggle: true),
                    new FaceMorphDef("tear1", "涙流れ1", isToggle: true),
                    new FaceMorphDef("tear2", "涙流れ2", isToggle: true),
                    new FaceMorphDef("tear3", "涙流れ3", isToggle: true),
                    new FaceMorphDef("yodare", "よだれ", isToggle: true),
                    new FaceMorphDef("shock", "ショック", isToggle: true),
                    new FaceMorphDef("nosefook", "鼻フック", isToggle: true),
                    new FaceMorphDef("toothoff", "歯消し", isToggle: true),
                }
            },
        };

        private static TMorph GetFaceMorph(Maid maid)
        {
            return maid?.body0?.Face?.morph;
        }

        /// <summary>
        /// モーフ名をインデックスに解決する。素の名前 → CRC 顔のサフィックス付きの順で探す。
        /// 見つからないときは -1
        /// </summary>
        private static int ResolveMorphIndex(TMorph morph, string name)
        {
            var index = morph.hash[name];
            if (index != null)
            {
                return (int)index;
            }

            var faceType = morph.GetFaceTypeGP01FB();
            if (faceType != TMorph.GP01FB_FACE_TYPE.MAX)
            {
                // CRC 顔では素の eyeclose に相当するモーフが eyeclose1 になる
                // （ゲーム側 WindowPartsFaceMorph.GetBlendIdx と同じ補正）
                var baseName = name == "eyeclose" ? "eyeclose1" : name;
                index = morph.hash[baseName + TMorph.crcFaceTypesStr[(int)faceType]];
                if (index != null)
                {
                    return (int)index;
                }
            }

            return -1;
        }

        /// <summary>対象メイドの顔に存在するモーフだけ返す</summary>
        public static IEnumerable<FaceMorphDef> GetAvailableMorphs(
            Maid maid, FaceMorphCategory category)
        {
            var morph = GetFaceMorph(maid);
            if (morph == null)
            {
                yield break;
            }

            foreach (var def in MorphDefs[category])
            {
                if (ResolveMorphIndex(morph, def.name) >= 0)
                {
                    yield return def;
                }
            }
        }

        public static float GetMorphValue(Maid maid, FaceMorphDef def)
        {
            var morph = GetFaceMorph(maid);
            if (morph == null)
            {
                return 0f;
            }

            var index = ResolveMorphIndex(morph, def.name);
            return index < 0 ? 0f : morph.GetBlendValues(index);
        }

        public static void SetMorphValue(Maid maid, FaceMorphDef def, float value)
        {
            var morph = GetFaceMorph(maid);
            if (morph == null)
            {
                return;
            }

            var index = ResolveMorphIndex(morph, def.name);
            if (index < 0)
            {
                return;
            }

            morph.SetBlendValues(index, value);
            morph.FixBlendValues_Face();
        }

        /// <summary>カテゴリ内の全モーフを 0 に戻す</summary>
        public static void ResetCategory(Maid maid, FaceMorphCategory category)
        {
            foreach (var def in GetAvailableMorphs(maid, category))
            {
                SetMorphValue(maid, def, 0f);
            }
        }

        /// <summary>
        /// フォトモードの内蔵プリセットを適用する。
        /// まばたき中は eyeclose 系が毎フレーム上書きされるため、適用前に止める。
        /// ブレンド値は総入れ替えになるため、スライダー表示も新値に揃う
        /// </summary>
        public static void ApplyPhotoFacePreset(Maid maid, PhotoFaceData data)
        {
            var morph = GetFaceMorph(maid);
            if (morph == null || data == null || maid.IsBusy)
            {
                // 多忙中は PhotoFaceData.Apply も無反応なため、まばたき停止だけが残らないよう丸ごと見送る
                return;
            }

            SetMabataki(maid, false);

            // ゲーム側の FaceName も揃え、まばたき再開時に同じプリセットが維持されるようにする
            data.Apply(maid);

            // FaceAnime(t=0) は FaceName を設定するだけで、実際のブレンド反映は
            // boMabataki 有効時の毎フレーム処理 (Maid.Update) でしか走らない。
            // まばたきを止めた直後は誰も反映しないため、ブレンドセットを直接書き込む
            var settingName = data.setting_name;
            if (morph.dicBlendSet.ContainsKey(settingName + "〓通常"))
            {
                // 新ボディ顔向けの別名。Maid.FaceAnime と同じ解決順
                settingName += "〓通常";
            }
            if (!morph.dicBlendSet.ContainsKey(settingName))
            {
                MTEUtils.LogWarning("表情プリセットが見つかりません: {0}", settingName);
                return;
            }

            morph.MulBlendValues(settingName, 1f);
            morph.FixBlendValues_Face();
        }

        /// <summary>
        /// まばたき自動更新の切り替え。オフにしないと eyeclose が毎フレーム上書きされる
        /// </summary>
        public static void SetMabataki(Maid maid, bool enabled)
        {
            if (maid == null)
            {
                return;
            }

            maid.boMabataki = enabled;
        }

        /// <summary>現在の表情ブレンドセット名 (Maid.FaceAnime のタグ)。未設定なら空文字</summary>
        public static string GetFaceName(Maid maid)
        {
            return maid != null ? maid.ActiveFace : "";
        }

        /// <summary>
        /// 表情ブレンドセットを復元する。まばたき有効中は Maid.Update が毎フレーム
        /// ClearBlendValues してこのタグからブレンドを作り直すため、
        /// 個別のモーフ値より先に保存時のタグへ戻しておく必要がある。
        ///
        /// フェード時間 0 を渡すのは、進行中のフェードをここで畳むため。
        /// 新規呼出直後のメイドは FaceAnime("通常", 1f) のフェード中で、
        /// 畳まないとまばたきを止めていてもモーフ値が 1 秒かけて消えていく。
        /// t=0 の FaceAnime はブレンドを塗り直さないので、直後に書き込む値は残る
        /// </summary>
        public static void ApplyFaceName(Maid maid, string faceName)
        {
            var morph = GetFaceMorph(maid);
            if (morph == null)
            {
                return;
            }

            var tag = faceName;
            if (!string.IsNullOrEmpty(tag) && !HasBlendSet(morph, tag))
            {
                MTEUtils.LogWarning("表情ブレンドセットが見つかりません: {0}", tag);
                tag = null;
            }
            if (string.IsNullOrEmpty(tag))
            {
                // 旧プリセットや解決できないタグでは表情を変えず、フェードを畳むだけに留める。
                // ActiveFace が空なら FaceAnime 自体が未実行でフェードもないため何もしない
                tag = maid.ActiveFace;
                if (string.IsNullOrEmpty(tag))
                {
                    return;
                }
            }

            maid.FaceAnime(tag, 0f, 0);
        }

        /// <summary>新ボディ顔の別名 (〓通常) も含めてブレンドセットの有無を見る</summary>
        private static bool HasBlendSet(TMorph morph, string blendSetName)
        {
            return morph.dicBlendSet.ContainsKey(blendSetName)
                || morph.dicBlendSet.ContainsKey(blendSetName + "〓通常");
        }

        public static bool GetMabataki(Maid maid)
        {
            return maid != null && maid.boMabataki;
        }
    }
}
