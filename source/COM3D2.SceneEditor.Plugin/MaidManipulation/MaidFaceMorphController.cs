using System;
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

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

        /// <summary>顔の TMorph。ボディ未ロードなら null</summary>
        public static TMorph GetFaceMorph(Maid maid)
        {
            return maid?.body0?.Face?.morph;
        }

        /// <summary>
        /// CRC 顔 (COM3D2.5 の新ボディ) か。
        /// ゲーム側 WindowPartsFaceMorph.GetBlendIdx と同じ PartsVersion で判定する。
        /// GetFaceTypeGP01FB は旧顔でも NORMAL を返すため、顔タイプでは判定できない
        /// </summary>
        private static bool IsCrcFace(TMorph morph)
        {
            return morph.bodyskin != null && CRC_FACE_PARTS_VERSION <= morph.bodyskin.PartsVersion;
        }

        /// <summary>CRC 顔として扱う PartsVersion の下限 (ゲーム側の判定値)</summary>
        private const int CRC_FACE_PARTS_VERSION = 120;

        /// <summary>
        /// モーフ名をインデックスに解決する。素の名前 → CRC 顔のサフィックス付きの順で探す。
        /// 見つからないときは -1。
        ///
        /// ゲーム側 (WindowPartsFaceMorph.GetBlendIdx) はサフィックス探索を eyeclose 系に
        /// 限定しているが、こちらは名前を問わず試す。サフィックス付きのキーを持つのは
        /// eyeclose 系と itome だけなので結果は変わらず、itome を扱えるぶん広い
        /// </summary>
        public static int ResolveMorphIndex(TMorph morph, string name)
        {
            if (morph == null)
            {
                return -1;
            }

            var index = morph.hash[name];
            if (index != null)
            {
                return (int)index;
            }

            if (!IsCrcFace(morph))
            {
                return -1;
            }

            index = morph.hash[
                GetCrcMorphName(name, (int)morph.GetFaceTypeGP01FB())];
            return index != null ? (int)index : -1;
        }

        /// <summary>
        /// CRC 顔でのモーフ名。eyeclose / itome などは目型ごとにサフィックスが付き、
        /// 素の eyeclose に相当するモーフは eyeclose1 になる
        /// (ゲーム側 WindowPartsFaceMorph.GetBlendIdx と同じ規則)。
        /// 目型が想定外でも配列外参照にならないよう丸める
        /// </summary>
        public static string GetCrcMorphName(string name, int faceTypeIndex)
        {
            var index = Mathf.Clamp(faceTypeIndex, 0, TMorph.crcFaceTypesStr.Length - 1);
            var baseName = name == "eyeclose" ? "eyeclose1" : name;
            return baseName + TMorph.crcFaceTypesStr[index];
        }

        /// <summary>
        /// CRC 顔で値域が 3 倍あるモーフか。
        /// ジト目だけ 0〜3 で、UI の 0〜1 のままだと 1/3 までしか効かない
        /// </summary>
        public static bool IsTripleRangeMorph(string name)
        {
            return name == "eyeclose3";
        }

        /// <summary>UI で扱う値 (0〜1) と TMorph のブレンド値の倍率</summary>
        public static float GetMorphRatio(TMorph morph, string name)
        {
            return morph != null && IsCrcFace(morph) && IsTripleRangeMorph(name) ? 3f : 1f;
        }

        /// <summary>
        /// 名前指定でモーフ値を UI 値として読む。存在しないモーフは 0。
        /// 生の TMorph 値が要る場合は GetBlendValues を直接使うこと
        /// </summary>
        public static float GetMorphValueByName(TMorph morph, string name)
        {
            var index = ResolveMorphIndex(morph, name);
            return index < 0 ? 0f : morph.GetBlendValues(index) / GetMorphRatio(morph, name);
        }

        /// <summary>
        /// 名前指定でモーフ値を UI 値として書く。存在しなければ何もしない。
        /// FixBlendValues_Face は呼ばないため、まとめて書く側が最後に 1 回呼ぶこと
        /// (単発で書くなら SetMorphValue(Maid, FaceMorphDef, float) を使う)
        /// </summary>
        public static void SetMorphValueByName(TMorph morph, string name, float value)
        {
            var index = ResolveMorphIndex(morph, name);
            if (index >= 0)
            {
                morph.SetBlendValues(index, value * GetMorphRatio(morph, name));
            }
        }

        /// <summary>モーフ名から定義を全カテゴリ横断で引く。該当なしは null</summary>
        public static FaceMorphDef FindDef(string name)
        {
            foreach (var defs in MorphDefs.Values)
            {
                foreach (var def in defs)
                {
                    if (def.name == name)
                    {
                        return def;
                    }
                }
            }
            return null;
        }

        /// <summary>対象メイドの顔にこのモーフが存在するか</summary>
        public static bool IsAvailable(Maid maid, FaceMorphDef def)
        {
            var morph = GetFaceMorph(maid);
            return morph != null && ResolveMorphIndex(morph, def.name) >= 0;
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

        /// <summary>スライダーが扱う UI 値 (0〜1) で読む</summary>
        public static float GetMorphValue(Maid maid, FaceMorphDef def)
        {
            return GetMorphValueByName(GetFaceMorph(maid), def.name);
        }

        /// <summary>スライダーが扱う UI 値 (0〜1) で書き、その場で顔へ反映する</summary>
        public static void SetMorphValue(Maid maid, FaceMorphDef def, float value)
        {
            var morph = GetFaceMorph(maid);
            if (morph == null)
            {
                return;
            }

            SetMorphValueByName(morph, def.name, value);
            morph.FixBlendValues_Face();
        }

        /// <summary>
        /// 保存用に TMorph の生の値で読む。
        /// プリセットや履歴は倍率を掛けない生値で記録してきたため、
        /// 既存ファイルと解釈を揃えるにはこちらを使う
        /// </summary>
        public static float GetStoredMorphValue(Maid maid, FaceMorphDef def)
        {
            var morph = GetFaceMorph(maid);
            var index = ResolveMorphIndex(morph, def.name);
            return index < 0 ? 0f : morph.GetBlendValues(index);
        }

        /// <summary>保存された生の値を書き戻す。GetStoredMorphValue の対</summary>
        public static void SetStoredMorphValue(Maid maid, FaceMorphDef def, float value)
        {
            var morph = GetFaceMorph(maid);
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

            // プリセット適用は表情の総入れ替え。非 0 のモーフをチェック済みへ置き換え、
            // シーンプリセット保存 (チェック済みのみ保存) で表情が欠落しないようにする
            var modifiedNames = new List<string>();
            foreach (FaceMorphCategory category in Enum.GetValues(typeof(FaceMorphCategory)))
            {
                foreach (var def in GetAvailableMorphs(maid, category))
                {
                    if (GetMorphValue(maid, def) != 0f)
                    {
                        modifiedNames.Add(def.name);
                    }
                }
            }
            FaceEditManager.instance.GetStore(maid).SetNames(modifiedNames);
        }

        /// <summary>
        /// タイムライン表情レイヤーによるまばたき抑止の退避値。
        /// キーはメイド、値は抑止前のユーザー設定 (boMabataki)。抑止解除時に復元する
        /// </summary>
        private static readonly Dictionary<Maid, bool> _mabatakiSuppressStates = new Dictionary<Maid, bool>();

        /// <summary>
        /// まばたき自動更新の切り替え。オフにしないと eyeclose が毎フレーム上書きされる。
        /// 抑止中はユーザー設定 (退避値) だけを書き換え、実体は抑止解除時に反映する
        /// </summary>
        public static void SetMabataki(Maid maid, bool enabled)
        {
            if (maid == null)
            {
                return;
            }

            if (_mabatakiSuppressStates.ContainsKey(maid))
            {
                _mabatakiSuppressStates[maid] = enabled;
                return;
            }

            maid.boMabataki = enabled;
        }

        /// <summary>
        /// タイムライン表情レイヤーによるまばたき抑止。
        /// 抑止中はゲーム側が立て直した boMabataki も毎回落とし、
        /// ユーザー設定は退避して解除時に復元する
        /// </summary>
        public static void SetMabatakiSuppressed(Maid maid, bool suppressed)
        {
            if (maid == null)
            {
                // 破棄済みメイド (Unity の null 化) の退避値は復元先が無いため捨てる。
                // Dictionary のキー比較は参照ベースで Unity の == と異なり破棄済みでも引ける。
                // 真の null (ReferenceEquals) だけは Remove が例外になるため除外する
                if (!suppressed && !ReferenceEquals(maid, null))
                {
                    _mabatakiSuppressStates.Remove(maid);
                }
                return;
            }

            bool stored;
            var isSuppressed = _mabatakiSuppressStates.TryGetValue(maid, out stored);

            if (suppressed)
            {
                if (!isSuppressed)
                {
                    _mabatakiSuppressStates[maid] = maid.boMabataki;
                }
                maid.boMabataki = false;
            }
            else if (isSuppressed)
            {
                _mabatakiSuppressStates.Remove(maid);
                maid.boMabataki = stored;
            }
        }

        public static bool IsMabatakiSuppressed(Maid maid)
        {
            return maid != null && _mabatakiSuppressStates.ContainsKey(maid);
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

        /// <summary>ユーザー設定としてのまばたき。抑止中は実体ではなく退避値を返す</summary>
        public static bool GetMabataki(Maid maid)
        {
            if (maid == null)
            {
                return false;
            }

            bool stored;
            if (_mabatakiSuppressStates.TryGetValue(maid, out stored))
            {
                return stored;
            }

            return maid.boMabataki;
        }
    }
}
