using System;
using System.Collections.Generic;
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
    /// IL の書き換えでしか直せないため Transpiler で 2 箇所だけ差し替える。
    /// 具体的な書き換え内容は RenderGizmosTranspiler を参照
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

                var transpiler = AccessTools.Method(typeof(GizmoScaleFixPatch), nameof(RenderGizmosTranspiler));

                _harmony = new Harmony(PluginInfo.PluginFullName + ".GizmoScaleFix");
                _harmony.Patch(original, transpiler: new HarmonyMethod(transpiler));

                if (!_patched)
                {
                    // IL の形が変わっている。Transpiler は何も書き換えずに返しているが、
                    // 素通しのパッチを残す意味は無いので剥がして次回の Init に賭ける
                    throw new Exception("RenderGizmos の IL パターンが一致しませんでした");
                }

                MTEUtils.Log("GizmoRender.RenderGizmos のフックに成功しました");
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
            _harmony = null;
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
