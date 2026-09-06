using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using COM3D2.MotionTimelineEditor;
using HarmonyLib;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 視野角 (FoV) を変えるとギズモのサイズが破綻するゲーム側の不具合を修正する。
    ///
    /// GizmoRender.RenderGizmos はギズモの基準長 generalLens を求める際、度である fieldOfView を
    /// そのままラジアンとして Mathf.Tan に渡している。既定の FoV では偶然それらしい値になるものの、
    /// FoV を動かすと tan の周期をまたいで符号反転や発散を起こし、
    /// ギズモが消える・反転する・極端に巨大化する。
    ///
    /// IL の書き換えでしか直せないため Transpiler で差し替える。
    /// 具体的な書き換え内容は RenderGizmosTranspiler を参照。
    ///
    /// あわせて、SceneView の描画パスだけ Camera.main を SceneView カメラへ差し替え、
    /// ギズモの大きさと回転リングの表裏を SceneView 基準で描く。
    /// 掴み判定はゲーム画面基準のままにするため、差し替えたパスでは
    /// Prefix/Postfix で判定用の状態を退避・復元する
    /// </summary>
    public static class GizmoScaleFixPatch
    {
        /// <summary>
        /// 修正後の式の除数。元の 50 のままだと Deg2Rad を掛けた分だけギズモが極端に小さくなるため、
        /// 既定 FoV でのギズモ長が従来とほぼ一致する 4 に置き換える。
        /// 数式的な必然ではなく見た目を合わせるために選んだ値で、既定 FoV (35) では従来比 0.89 倍になる
        /// </summary>
        private const float LENS_DIVISOR = 4f;

        private const float ORIGINAL_LENS_DIVISOR = 50f;

        /// <summary>
        /// 除数を探す範囲。FoV パターンの直後に現れるものだけを狙い、
        /// 将来 RenderGizmos の後方に別の 50f が増えても巻き込まないようにする
        /// </summary>
        private const int DIVISOR_SEARCH_LENGTH = 16;

        // Harmony インスタンスは他のパッチと独立させ、有効・無効判定を他パッチの状態から切り離す
        private static Harmony _harmony = null;

        /// <summary>Transpiler が書き換えを行えたか。Init 側で成否を報告するために使う</summary>
        private static bool _patched = false;

        /// <summary>
        /// 移植元の単体プラグイン。同じ RenderGizmos を Transpiler で書き換えるため併存できない。
        /// Harmony の owner id は BepInPlugin の GUID と同じ
        /// </summary>
        private const string LEGACY_PLUGIN_ID = "COM3D2.RenderGizmosScaleFix.Plugin";

        /// <summary>差し替え対象。Camera.main のゲッター名</summary>
        private const string CAMERA_MAIN_GETTER = "get_main";

        /// <summary>Camera.main を差し替えた件数。ゲーム更新で数が変わったことに気付くためログへ出す</summary>
        private static int _replacedCameraMainCount = 0;

        /// <summary>
        /// 掴み判定へ漏れる private フィールドの名前。
        /// generalLens はハンドルの当たり範囲、*Forward は回転リングの表側判定に使われる
        /// </summary>
        private const string GENERAL_LENS_FIELD = "generalLens";
        private const string U_FORWARD_FIELD = "uForward";
        private const string R_FORWARD_FIELD = "rForward";
        private const string F_FORWARD_FIELD = "fForward";

        /// <summary>毎フレームの reflection を避けるため Init で一度だけ作る型付きアクセサ</summary>
        private static AccessTools.FieldRef<GizmoRender, float> _generalLensRef = null;
        private static AccessTools.FieldRef<GizmoRender, Vector3> _uForwardRef = null;
        private static AccessTools.FieldRef<GizmoRender, Vector3> _rForwardRef = null;
        private static AccessTools.FieldRef<GizmoRender, Vector3> _fForwardRef = null;

        /// <summary>
        /// Prefix と Postfix の間で共有する退避。RenderGizmos はメインスレッドで再入しないため
        /// 1 組のバッファを使い回してフレーム毎の確保を避ける
        /// </summary>
        private static float _savedGeneralLens = 0f;
        private static Vector3 _savedUForward = Vector3.zero;
        private static Vector3 _savedRForward = Vector3.zero;
        private static Vector3 _savedFForward = Vector3.zero;

        /// <summary>
        /// パッチを適用する。プラグイン初期化から 1 回だけ呼ばれるが、
        /// 二重パッチは Harmony の例外になるため保険を残す
        /// </summary>
        public static void Init()
        {
            if (_harmony != null)
            {
                return;
            }

            try
            {
                var original = AccessTools.Method(typeof(GizmoRender), "RenderGizmos");
                if (original == null)
                {
                    throw new Exception("GizmoRender.RenderGizmos が見つかりません");
                }

                if (HasLegacyPluginPatch(original))
                {
                    throw new Exception(LEGACY_PLUGIN_ID
                        + " が同じメソッドをパッチしています。二重適用を避けるため本体側の修正を見送ります。"
                        + @"BepInEx\plugins から同 DLL を削除してください");
                }

                foreach (var name in new[] { GENERAL_LENS_FIELD, U_FORWARD_FIELD, R_FORWARD_FIELD, F_FORWARD_FIELD })
                {
                    if (AccessTools.Field(typeof(GizmoRender), name) == null)
                    {
                        throw new Exception(name + " が見つかりません");
                    }
                }
                _generalLensRef = AccessTools.FieldRefAccess<GizmoRender, float>(GENERAL_LENS_FIELD);
                _uForwardRef = AccessTools.FieldRefAccess<GizmoRender, Vector3>(U_FORWARD_FIELD);
                _rForwardRef = AccessTools.FieldRefAccess<GizmoRender, Vector3>(R_FORWARD_FIELD);
                _fForwardRef = AccessTools.FieldRefAccess<GizmoRender, Vector3>(F_FORWARD_FIELD);

                var transpiler = AccessTools.Method(typeof(GizmoScaleFixPatch), nameof(RenderGizmosTranspiler));
                var prefix = AccessTools.Method(typeof(GizmoScaleFixPatch), nameof(RenderGizmosPrefix));
                var postfix = AccessTools.Method(typeof(GizmoScaleFixPatch), nameof(RenderGizmosPostfix));

                _harmony = new Harmony(PluginInfo.PluginFullName + ".GizmoScaleFix");
                _harmony.Patch(
                    original,
                    prefix: new HarmonyMethod(prefix),
                    postfix: new HarmonyMethod(postfix),
                    transpiler: new HarmonyMethod(transpiler));

                if (!_patched)
                {
                    // IL の形が変わっている。Transpiler は何も書き換えずに返しているが、
                    // 素通しのパッチを残す意味は無いので剥がして次回の Init に賭ける
                    throw new Exception("RenderGizmos の IL パターンが一致しませんでした");
                }

                MTEUtils.Log("GizmoRender.RenderGizmos のフックに成功しました (Camera.main の差し替え: "
                    + _replacedCameraMainCount + " 箇所)");
            }
            catch (Exception e)
            {
                MTEUtils.LogError("GizmoRender.RenderGizmos のフックに失敗しました。FoV 変更時のギズモサイズ修正は無効です");
                MTEUtils.LogException(e);
                Unpatch();
            }
        }

        /// <summary>
        /// 適用済みのパッチを剥がして未適用状態へ戻す。
        /// Transpiler は書き換えなしの IL を返しているため実挙動はゲーム標準のままだが、
        /// 素通しのパッチと _harmony を残さないことで再試行できる状態にする
        /// </summary>
        private static void Unpatch()
        {
            if (_harmony == null)
            {
                return;
            }

            try
            {
                _harmony.UnpatchSelf();
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
            }

            _generalLensRef = null;
            _uForwardRef = null;
            _rForwardRef = null;
            _fForwardRef = null;
            _renderCamera = null;
            _harmony = null;
        }

        /// <summary>
        /// 旧プラグインが同じメソッドを既にパッチしているか。
        /// ロード順によっては後からパッチされて検知できないが、
        /// 併存に気付けるようにするのが目的なので完全な防御は狙わない
        /// </summary>
        private static bool HasLegacyPluginPatch(MethodBase original)
        {
            var info = Harmony.GetPatchInfo(original);
            if (info == null)
            {
                return false;
            }

            foreach (var owner in info.Owners)
            {
                if (owner == LEGACY_PLUGIN_ID)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Prefix が解決した参照カメラ。RenderGizmos は再入しないため 1 個で足りる。
        /// Prefix・本体・Postfix が必ず同じカメラを見ることを保証し、
        /// 解決コストも RenderGizmos 1 回につき 1 度で済ませる
        /// </summary>
        private static Camera _renderCamera = null;

        /// <summary>
        /// RenderGizmos が参照するカメラを返す。Transpiler が Camera.main の呼び出しを
        /// これに差し替えるため、Camera.get_main と同じ「引数なし・戻り値 Camera」で揃えている。
        /// Prefix が走らなかった場合の保険として Camera.main へ落とす
        /// </summary>
        public static Camera GetRenderCamera()
        {
            return _renderCamera != null ? _renderCamera : Camera.main;
        }

        /// <summary>
        /// 描画中のカメラから参照カメラを解決する。
        ///
        /// SceneView は専用カメラで描くため、Camera.main のままだとギズモの大きさも
        /// 回転リングの表裏もゲーム画面基準になってしまう。描画中のカメラが SceneView なら
        /// そちらを返して見た目を正す。
        ///
        /// SceneView 以外のカメラ (MTEFrontCamera 等のオーバーレイ) と、
        /// SceneView がオルソ投影のときは Camera.main へ落とす。
        /// オルソでは Mathf.Tan(fov/2) もカメラ位置からの距離も意味を持たないため
        /// </summary>
        private static Camera ResolveRenderCamera(out bool overridden)
        {
            var current = Camera.current;
            if (current != null && !current.orthographic && current == SceneViewManager.instance.sceneCamera)
            {
                overridden = true;
                return current;
            }

            overridden = false;
            return Camera.main;
        }

        /// <summary>
        /// 参照カメラを解決してキャッシュし、差し替えるパスなら掴み判定用の状態を退避する。
        /// __state で Postfix へ「復元が必要か」を伝える。
        /// 毎フレーム走るため例外は握り潰し、ゲーム側の描画を止めない
        /// </summary>
        private static void RenderGizmosPrefix(GizmoRender __instance, out bool __state)
        {
            __state = false;
            try
            {
                bool overridden;
                _renderCamera = ResolveRenderCamera(out overridden);
                if (!overridden)
                {
                    return;
                }

                _savedGeneralLens = _generalLensRef(__instance);
                _savedUForward = _uForwardRef(__instance);
                _savedRForward = _rForwardRef(__instance);
                _savedFForward = _fForwardRef(__instance);
                __state = true;
            }
            catch (Exception e)
            {
                // 退避に失敗したら復元もしない。ギズモは Camera.main 基準で描かれるだけで済む
                _renderCamera = null;
                __state = false;
                MTEUtils.LogException(e);
            }
        }

        /// <summary>
        /// 描画は差し替えたカメラ基準で終わっているので、掴み判定が読む状態だけ
        /// Camera.main 基準の値へ戻す
        /// </summary>
        private static void RenderGizmosPostfix(GizmoRender __instance, bool __state)
        {
            _renderCamera = null;

            if (!__state)
            {
                return;
            }

            try
            {
                _generalLensRef(__instance) = _savedGeneralLens;
                _uForwardRef(__instance) = _savedUForward;
                _rForwardRef(__instance) = _savedRForward;
                _fForwardRef(__instance) = _savedFForward;
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
            }
        }

        /// <summary>
        /// generalLens の計算式を差し替える。
        ///   -2f * Mathf.Tan(0.5f * fieldOfView)          * magnitude / 50f
        /// → 2f * Mathf.Tan(0.5f * fieldOfView * Deg2Rad) * magnitude / 4f
        ///
        /// 壊れた元の式は既定 FoV 付近でたまたま tan が負値を返しており、先頭の -2 はその符号を
        /// 打ち消すためのものだった。Deg2Rad を掛けて tan が正値に戻るので係数も 2 へ直す。
        ///
        /// 片方だけ書き換えると元の式とも修正後の式とも違う第 3 の壊れた式が残るため、
        /// 2 箇所そろわなければ何も書き換えずに元の IL をそのまま返す
        /// </summary>
        private static IEnumerable<CodeInstruction> RenderGizmosTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);

            var fovIndex = FindFovPattern(codes);
            if (fovIndex < 0)
            {
                return codes;
            }

            // 除数は FoV パターンより後ろにしか現れない
            var divisorIndex = FindDivisor(codes, fovIndex + 4);
            if (divisorIndex < 0)
            {
                return codes;
            }

            // 後ろから書き換えれば、挿入によって前方の添字がずれない
            codes[divisorIndex] = new CodeInstruction(OpCodes.Ldc_R4, LENS_DIVISOR);

            codes[fovIndex] = new CodeInstruction(OpCodes.Ldc_R4, 2f);
            // fieldOfView の直後に Deg2Rad の乗算を挿し込む
            codes.Insert(fovIndex + 4, new CodeInstruction(OpCodes.Ldc_R4, Mathf.Deg2Rad));
            codes.Insert(fovIndex + 5, new CodeInstruction(OpCodes.Mul));

            // SceneView の描画パスでは参照カメラを差し替える。
            // FoV パターンの検出が get_main の並びを見ているため、検出が済んでから置き換える
            var getRenderCamera = AccessTools.Method(typeof(GizmoScaleFixPatch), nameof(GetRenderCamera));
            _replacedCameraMainCount = 0;
            for (var i = 0; i < codes.Count; i++)
            {
                if (IsCall(codes[i], OpCodes.Call, CAMERA_MAIN_GETTER))
                {
                    codes[i] = new CodeInstruction(OpCodes.Call, getRenderCamera);
                    _replacedCameraMainCount++;
                }
            }

            _patched = true;
            return codes;
        }

        /// <summary>
        /// -2f, 0.5f, Camera.get_main, get_fieldOfView が並ぶ箇所を探す。見つからなければ -1
        /// </summary>
        private static int FindFovPattern(List<CodeInstruction> codes)
        {
            for (var i = 0; i + 3 < codes.Count; i++)
            {
                if (IsConstant(codes[i], -2f)
                    && IsConstant(codes[i + 1], 0.5f)
                    && IsCall(codes[i + 2], OpCodes.Call, "get_main")
                    && IsCall(codes[i + 3], OpCodes.Callvirt, "get_fieldOfView"))
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// startIndex から近傍だけを見て元の除数を探す。見つからなければ -1
        /// </summary>
        private static int FindDivisor(List<CodeInstruction> codes, int startIndex)
        {
            var end = Math.Min(startIndex + DIVISOR_SEARCH_LENGTH, codes.Count);
            for (var i = startIndex; i < end; i++)
            {
                if (IsConstant(codes[i], ORIGINAL_LENS_DIVISOR))
                {
                    return i;
                }
            }
            return -1;
        }

        private static bool IsConstant(CodeInstruction code, float value)
        {
            return code.opcode == OpCodes.Ldc_R4 && code.operand is float && (float)code.operand == value;
        }

        private static bool IsCall(CodeInstruction code, OpCode opcode, string methodName)
        {
            return code.opcode == opcode
                && code.operand != null
                && code.operand.ToString().Contains(methodName);
        }
    }
}
