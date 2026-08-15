using System.Collections.Generic;
using System.Xml.Serialization;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>カメラの構図。CameraMain のオービットモデル（注視点 + 旋回角 + 距離）で保持する</summary>
    public class ScenePresetCamera
    {
        public Vector3 targetPos;
        /// <summary>GetAroundAngle().x（水平旋回）</summary>
        public float yaw;
        /// <summary>GetAroundAngle().y（仰俯角）</summary>
        public float pitch;
        /// <summary>UltimateOrbitCamera が管理しないため Transform の euler z を別途持つ</summary>
        public float roll;
        public float distance;
        public float fov;
    }

    /// <summary>背景の状態。id はフォトモードの PhotoBGData.id</summary>
    public class ScenePresetBackground
    {
        /// <summary>保存時点で背景が削除されていたか。true ならロード時に背景を消す</summary>
        [XmlAttribute]
        public bool deleted;

        [XmlAttribute]
        public string bgId;

        /// <summary>
        /// PhotoBGData の一覧に無い背景 (エディット画面の初期背景 ShinShitsumu_ChairRot 等) の
        /// prefab 名。id を逆引きできない場合のみ入り、復元は BgMgr.ChangeBg で直接行う
        /// </summary>
        [XmlAttribute]
        public string bgPrefabName;

        public Vector3 position;
        public Vector3 rotation;

        /// <summary>
        /// 背景色 (メインカメラのクリア色) を記録できたか。
        /// false なら旧プリセット (v7 以前) で、適用時に背景色を変更しない
        /// </summary>
        [XmlAttribute]
        public bool hasBgColor;

        /// <summary>背景を消しているときに見える色。アルファは撮影時の透過度</summary>
        public Color bgColor = Color.black;
    }

    /// <summary>追加ライト 1 灯分の状態</summary>
    public class ScenePresetAdditionalLight
    {
        /// <summary>UnityEngine.LightType の数値 (Point=2, Spot=0)</summary>
        [XmlAttribute]
        public int type;
        public Vector3 position;
        public Vector3 rotation;
        public Color color = Color.white;
        public float intensity;
        public float range;
        public float spotAngle;
        public bool enabled = true;
    }

    /// <summary>ライトの状態。メインライトと追加ライト一式</summary>
    public class ScenePresetLight
    {
        /// <summary>保存時にメインライトを記録できたか。false なら適用時もメインライトへ触らない</summary>
        [XmlAttribute]
        public bool hasMain;
        public Vector3 mainRotation;
        public Color mainColor = Color.white;
        public float mainIntensity;
        public float mainShadowStrength;

        [XmlElement("additionalLight")]
        public List<ScenePresetAdditionalLight> additionalLights =
            new List<ScenePresetAdditionalLight>();
    }

    /// <summary>脱衣の状態。要素なし (null) の旧プリセットでは脱衣状態を変更しない</summary>
    public class ScenePresetUndress
    {
        /// <summary>マスクで非表示にしている（脱衣中の）スロット名。TBody.SlotID の名前</summary>
        [XmlElement("slot")]
        public List<string> slots = new List<string>();

        /// <summary>ON のめくれ系。MaidCostumeChangeController.CostumeType の名前</summary>
        [XmlElement("costume")]
        public List<string> costumeTypes = new List<string>();
    }

    /// <summary>重力 1 カテゴリ分（髪・スカート）</summary>
    public class ScenePresetGravity
    {
        /// <summary>GravityCategory.id（"hair" / "skirt"）</summary>
        [XmlAttribute]
        public string category;

        [XmlAttribute]
        public bool enabled;

        public Vector3 offset;
    }

    /// <summary>PNG 配置 1 枚分の状態</summary>
    public class ScenePresetPngObject
    {
        /// <summary>画像の出所 (PngPlacementManager.SOURCE_*)</summary>
        [XmlAttribute]
        public string source;

        /// <summary>出所ディレクトリからの相対パス</summary>
        [XmlAttribute]
        public string relativePath;

        public Vector3 position;
        public Vector3 rotation;
        public Vector3 scale = Vector3.one;

        [XmlAttribute]
        public bool billboard = true;

        [XmlAttribute]
        public float brightness = 1f;

        public Color color = Color.white;

        [XmlAttribute]
        public int renderQueue;

        [XmlAttribute]
        public bool visible = true;
    }

    /// <summary>PNG 配置の状態一式</summary>
    public class ScenePresetPngPlacement
    {
        [XmlElement("png")]
        public List<ScenePresetPngObject> objects = new List<ScenePresetPngObject>();
    }

    /// <summary>IK 固定の接地パラメータ。MaidIKHoldParams と同項目</summary>
    public class ScenePresetIKParams
    {
        public bool isGroundingFootL;
        public bool isGroundingFootR;
        public float floorHeight;
        public float footBaseOffset;
        public float footStretchHeight;
        public float footStretchAngle;
        public float footGroundAngle;

        public static ScenePresetIKParams FromParams(MaidIKHoldParams p)
        {
            return new ScenePresetIKParams
            {
                isGroundingFootL = p.isGroundingFootL,
                isGroundingFootR = p.isGroundingFootR,
                floorHeight = p.floorHeight,
                footBaseOffset = p.footBaseOffset,
                footStretchHeight = p.footStretchHeight,
                footStretchAngle = p.footStretchAngle,
                footGroundAngle = p.footGroundAngle,
            };
        }

        public void ApplyTo(MaidIKHoldParams p)
        {
            p.isGroundingFootL = isGroundingFootL;
            p.isGroundingFootR = isGroundingFootR;
            p.floorHeight = floorHeight;
            p.footBaseOffset = footBaseOffset;
            // プリセット XML は外部入力のため、接地補間のゼロ除算 (NaN) を防いでから適用する
            p.footStretchHeight = Mathf.Max(footStretchHeight, 0.001f);
            p.footStretchAngle = footStretchAngle;
            p.footGroundAngle = footGroundAngle;
        }
    }

    /// <summary>
    /// v6: スロットボーン 1 本分の編集差分。
    /// XmlSerializer は Quaternion の eulerAngles プロパティまで拾ってしまうため、
    /// 値は素の float 配列で持つ
    /// </summary>
    public class ScenePresetBoneEdit
    {
        [XmlAttribute]
        public string slot;
        /// <summary>記録時のモデルファイル名。適用時に別アイテムを装着していたら飛ばす</summary>
        [XmlAttribute]
        public string item;
        [XmlAttribute]
        public string bone;

        /// <summary>localPosition xyz</summary>
        public float[] pos;
        /// <summary>localRotation xyzw</summary>
        public float[] rot;
        /// <summary>localScale xyz</summary>
        public float[] scl;

        public static ScenePresetBoneEdit FromEntry(BoneEditEntry entry)
        {
            return new ScenePresetBoneEdit
            {
                slot = entry.slotName,
                item = entry.itemFileName,
                bone = entry.boneName,
                pos = new[] { entry.position.x, entry.position.y, entry.position.z },
                rot = new[]
                {
                    entry.rotation.x, entry.rotation.y, entry.rotation.z, entry.rotation.w,
                },
                scl = new[] { entry.scale.x, entry.scale.y, entry.scale.z },
            };
        }

        /// <summary>XML は外部入力なので、要素数が足りないものは適用しない</summary>
        public bool isValid =>
            !string.IsNullOrEmpty(slot) && !string.IsNullOrEmpty(bone)
            && pos != null && pos.Length >= 3
            && rot != null && rot.Length >= 4
            && scl != null && scl.Length >= 3;
    }

    public class ScenePresetMorph
    {
        [XmlAttribute]
        public string name;
        [XmlAttribute]
        public float value;
    }

    /// <summary>
    /// 視線の状態。旧プリセット (v10 以前) は null になり、適用時に視線へ触らない。
    /// 注視対象はメイドなら guid + ボーン名、それ以外は階層パスで同定する
    /// </summary>
    public class ScenePresetLook
    {
        /// <summary>MaidLookMode の名前</summary>
        [XmlAttribute]
        public string mode;

        public float lookX;
        public float lookY;

        /// <summary>
        /// TBody.boHeadToCam (顔を向ける)。v14 以前のプリセットや、
        /// 保存時にボディを取得できなかったメイドでは headToCamSpecified が false になり、
        /// 適用時にトグルへ触らない
        /// </summary>
        [XmlAttribute]
        public bool headToCam;
        [XmlIgnore]
        public bool headToCamSpecified;

        /// <summary>TBody.boEyeToCam (目を向ける)。互換の扱いは headToCam と同じ</summary>
        [XmlAttribute]
        public bool eyeToCam;
        [XmlIgnore]
        public bool eyeToCamSpecified;

        /// <summary>注視対象が呼出済みメイドの一部だった場合の maid.status.guid</summary>
        [XmlAttribute]
        public string targetMaidGuid;

        /// <summary>同上の Transform 名 (Bip01 Head 等)</summary>
        [XmlAttribute]
        public string targetBone;

        /// <summary>メイド以外を注視している場合の、シーンルートからの階層パス</summary>
        [XmlAttribute]
        public string targetPath;
    }

    /// <summary>
    /// 再生中モーションの記録。ポーズ (poseAnmFile) とは排他で、
    /// 保存時にモーション再生中だった場合のみ入る
    /// </summary>
    public class ScenePresetMotion
    {
        /// <summary>PhotoMotionData.id。Mod はファイル内容の CRC なので更新されると変わる</summary>
        [XmlAttribute]
        public long id;

        /// <summary>PhotoMotionData.direct_file。バニラはファイル名、Mod は絶対パス</summary>
        [XmlAttribute]
        public string file;

        /// <summary>PhotoMotionData.name。見つからなかったときの警告表示に使う</summary>
        [XmlAttribute]
        public string name;
    }

    /// <summary>メイド 1 人分の状態。呼出順の並びで保存し、適用時は guid で割り当てる</summary>
    public class ScenePresetMaid
    {
        /// <summary>maid.status.guid。適用時の同一メイド照合に使う。旧形式 (v2 以前) は null</summary>
        [XmlAttribute]
        public string guid;

        public Vector3 position;
        public Vector3 rotation;
        public bool visible = true;
        public bool mabataki = true;
        /// <summary>
        /// ポーズ anm サイドカーのファイル名。ボディ未ロード等でポーズを取得できなかったときは null
        /// </summary>
        [XmlAttribute]
        public string poseAnmFile;

        /// <summary>
        /// 再生中だったモーション (v13)。非 null ならポーズは記録されず、適用時はこちらを再生する
        /// </summary>
        public ScenePresetMotion motion;

        /// <summary>
        /// anm バイナリ。サイドカーとの受け渡しにだけ使うため XML には出さない
        /// </summary>
        [XmlIgnore]
        public byte[] poseAnmBinary;
        /// <summary>値が 0 でない表情モーフだけ持つ。適用時は未記録のモーフを 0 に戻す</summary>
        [XmlElement("morph")]
        public List<ScenePresetMorph> morphs = new List<ScenePresetMorph>();

        /// <summary>脱衣状態。旧プリセット (v4 以前) は null になり、適用時に変更しない</summary>
        public ScenePresetUndress undress;

        /// <summary>重力。旧プリセット (v10 以前) は null になり、適用時に重力へ触らない</summary>
        [XmlElement("gravity")]
        public List<ScenePresetGravity> gravity;

        /// <summary>
        /// IK 固定の接地パラメータ。null なら IK 未記録 (v4 以前) で、適用時に固定へ触らない。
        /// 非 null のときは ikHolds が空でも「全固定 OFF」として適用する
        /// </summary>
        public ScenePresetIKParams ikParams;

        /// <summary>固定 ON の箇所。MaidIKHoldType の名前</summary>
        [XmlElement("ikHold")]
        public List<string> ikHolds = new List<string>();

        /// <summary>
        /// スロットボーンの編集差分。編集なし・旧プリセット (v5 以前) では null になり、
        /// 適用時にボーンへ触らない
        /// </summary>
        [XmlElement("boneEdit")]
        public List<ScenePresetBoneEdit> boneEdits;

        /// <summary>
        /// 視線。旧プリセット (v10 以前) は null になり、適用時に視線へ触らない
        /// </summary>
        public ScenePresetLook look;
    }

    /// <summary>
    /// 外部プラグインプロバイダ 1 件分の記録。中身はサイドカーへ生のまま保存し、
    /// 本体にはプロバイダ id とサイドカーのファイル名だけ残す
    /// </summary>
    public class ScenePresetExternal
    {
        [XmlAttribute]
        public string id;

        /// <summary>サイドカーのファイル名。本体 XML と同じフォルダに置かれる</summary>
        [XmlAttribute]
        public string file;

        /// <summary>ペイロード。サイドカーとの受け渡しにだけ使うため XML には出さない</summary>
        [XmlIgnore]
        public string payload;

        /// <summary>バイナリモードのペイロード。サイドカーとの受け渡しにだけ使う</summary>
        [XmlIgnore]
        public byte[] binaryPayload;
    }

    /// <summary>シーンプリセット 1 件。メイドの配置・ポーズ・表情とカメラをまとめて記録する</summary>
    public class ScenePresetData
    {
        // v2: externals（外部プラグインペイロード）を追加。v1 は externals 無しで読める
        // v3: maid に guid を追加。v2 以前は guid 無しで読め、適用時は未割当ストックで補う
        // v4: light（メインライト + 追加ライト）を追加。旧形式は light 無しで読める
        // v5: maid に undress（脱衣・めくれ系）と ikParams / ikHolds（IK 固定）を追加。
        //     旧形式は null / 空で読め、適用時に該当状態を変更しない
        // v6: maid に boneEdits（スロットボーンの編集差分）を追加。
        //     旧形式は null で読め、適用時にボーンへ触らない
        // v7: bgm（再生中の BGM）を追加。旧形式は bgm 無しで読め、適用時に BGM を変更しない
        // v8: background に bgColor（背景色）を追加。
        //     旧形式は hasBgColor=false で読め、適用時に背景色を変更しない
        // v9: bgm を保存対象から除外。BGM はシーンではなく再生環境の状態として扱う。
        //     v7〜v8 形式に bgm が残っていても読み飛ばす
        // v10: ポーズ anm を本体 XML の base64 埋め込みからサイドカー (.anm) へ移し、
        //      external もサイドカーのファイル名を記録する形に変更。
        //      v9 以前はポーズと externals が復元されない（互換は取らない）
        // v11: maid に look（視線モード・顔向き・注視対象）を追加。
        //      旧形式は null で読め、適用時に視線へ触らない
        // v12: maid に gravity（髪・スカートの重力）を追加。
        //      旧形式は null で読め、適用時に重力へ触らない
        // v13: maid に motion（再生中モーション）を追加。
        //      モーション再生中に保存したメイドはポーズ (poseAnmFile) を持たず、
        //      適用時はモーションを再生し直す。旧形式は null で読め、従来どおりポーズを復元する
        // v14: pngPlacement（PNG 配置）を追加。旧形式は null で読め、
        //      その時点では PNG 配置機能自体が無く配置は 0 枚なので、適用時は空として全消去する
        // v15: look に headToCam / eyeToCam（顔を向ける・目を向ける）を追加。
        //      旧形式は属性が無く、適用時にこの 2 つのトグルへ触らない
        public static readonly int CurrentVersion = 15;

        [XmlAttribute]
        public int version = CurrentVersion;

        public ScenePresetCamera camera;

        /// <summary>背景。旧プリセット（要素なし）は null になり、適用時に背景を変更しない</summary>
        public ScenePresetBackground background;

        /// <summary>ライト。旧プリセット（要素なし）は null になり、適用時にライトを変更しない</summary>
        public ScenePresetLight light;

        /// <summary>PNG 配置。旧プリセット（要素なし）は null になり、適用時は既存の配置を全て消去する</summary>
        public ScenePresetPngPlacement pngPlacement;

        [XmlElement("maid")]
        public List<ScenePresetMaid> maids = new List<ScenePresetMaid>();

        [XmlElement("external")]
        public List<ScenePresetExternal> externals = new List<ScenePresetExternal>();
    }
}
