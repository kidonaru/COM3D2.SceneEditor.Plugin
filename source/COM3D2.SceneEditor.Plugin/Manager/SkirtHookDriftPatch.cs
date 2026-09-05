using System;
using System.Collections.Generic;
using System.Reflection;
using COM3D2.MotionTimelineEditor;
using HarmonyLib;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 長時間起動でスカートが上がったまま戻らなくなるゲーム側の不具合を抑える。
    ///
    /// DynamicSkirtBone.DynamicUpdate はスカート最上段のフックボーン (SkirtNN_A_yure_skirt_) を
    /// ソルバのアンカー入力として読み、同じ Transform に `position = ソルバ出力` を書き戻す。
    /// ソルバはフックを入力と同じ位置で返すので世界座標は変わらないが、親の Skirt ボーンが
    /// 非一様スケールのため world→local 変換の丸め誤差が一方向に偏り、モーション中に
    /// 毎フレーム ~3e-7 ずつ localPosition が累積する。誰も再アンカーしないため数時間で cm 単位ずれる。
    ///
    /// 書き戻しの前後で localPosition を控えて戻すことで、世界座標を変えずに丸め誤差だけを捨てる。
    /// ソルバが意図的にフックを動かしたフレーム (世界座標が変わった場合) は触らない。
    ///
    /// 対象はグローバル名前空間の DynamicSkirtBone (従来ボーン方式のスカート) のみ。
    /// 2.5 の KCES2Physics.DynamicSkirtBone (KCES / CRC スカート) は別実装で、
    /// フックボーンを入出力に共用する閉ループを持たないため対象外
    /// </summary>
    public static class SkirtHookDriftPatch
    {
        /// <summary>毎フレームの更新メソッド。本体は同じだが 2.5 は DynamicUpdate、2.0 は UpdateSelf と名前が違う</summary>
        private static readonly string[] UPDATE_METHOD_NAMES = { "DynamicUpdate", "UpdateSelf" };

        /// <summary>フックボーンのリスト。2.0 / 2.5 で同名</summary>
        private const string HOOK_BONE_LIST_FIELD = "m_listHookBoneTrs";

        /// <summary>
        /// 「ソルバがフックを動かしていない」とみなす世界座標のずれの上限 (m)。
        /// 丸め誤差は 1e-6 未満なので、これを超えるずれは意図的な移動として扱う
        /// </summary>
        private const float SOLVER_MOVED_THRESHOLD = 1e-4f;

        // Harmony インスタンスは他のパッチと独立させ、有効・無効判定を他パッチの状態から切り離す
        private static Harmony _harmony = null;

        /// <summary>毎フレームの reflection を避けるため Init で一度だけ作る型付きアクセサ</summary>
        private static AccessTools.FieldRef<DynamicSkirtBone, List<Transform>> _hookBoneListRef = null;

        /// <summary>
        /// Prefix と Postfix の間で共有する控え。DynamicUpdate はメインスレッドで再入しないため
        /// 1 組のバッファを使い回してフレーム毎の確保を避ける
        /// </summary>
        private static Vector3[] _localPositions = new Vector3[0];
        private static Vector3[] _worldPositions = new Vector3[0];

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
                if (AccessTools.Field(typeof(DynamicSkirtBone), HOOK_BONE_LIST_FIELD) == null)
                {
                    throw new Exception(HOOK_BONE_LIST_FIELD + " が見つかりません");
                }
                _hookBoneListRef = AccessTools.FieldRefAccess<DynamicSkirtBone, List<Transform>>(HOOK_BONE_LIST_FIELD);

                var original = FindUpdateMethod();
                if (original == null)
                {
                    throw new Exception("DynamicSkirtBone の更新メソッド (" +
                        string.Join(" / ", UPDATE_METHOD_NAMES) + ") が見つかりません");
                }

                var prefix = AccessTools.Method(typeof(SkirtHookDriftPatch), nameof(UpdatePrefix));
                var postfix = AccessTools.Method(typeof(SkirtHookDriftPatch), nameof(UpdatePostfix));

                _harmony = new Harmony(PluginInfo.PluginFullName + ".SkirtHookDrift");
                _harmony.Patch(original, prefix: new HarmonyMethod(prefix), postfix: new HarmonyMethod(postfix));
                MTEUtils.Log("DynamicSkirtBone." + original.Name + " のフックに成功しました");
            }
            catch (Exception e)
            {
                // 失敗してもスカート物理はゲーム標準のまま動く
                MTEUtils.LogError(
                    "DynamicSkirtBone の更新メソッドのフックに失敗しました。スカートのドリフト抑止は無効です");
                MTEUtils.LogException(e);
                _harmony = null;
                _hookBoneListRef = null;
            }
        }

        /// <summary>
        /// 書き戻し前のフックボーンの local / world 座標を控える。
        /// __state にはフック本数を渡し、Postfix に「控えが有効か」を伝える (0 なら何もしない)。
        /// 1 本でも取れなければ控え全体を無効にする (揃っていない控えで戻すと整合が取れないため)。
        /// 毎フレーム走るため例外は握り潰してゲーム側の物理を止めない
        /// </summary>
        private static void UpdatePrefix(DynamicSkirtBone __instance, out int __state)
        {
            __state = 0;
            try
            {
                var hooks = GetHookBones(__instance);
                if (hooks == null)
                {
                    return;
                }

                var count = hooks.Count;
                EnsureCapacity(count);
                for (var i = 0; i < count; i++)
                {
                    var hook = hooks[i];
                    if (hook == null)
                    {
                        return;
                    }
                    _localPositions[i] = hook.localPosition;
                    _worldPositions[i] = hook.position;
                }
                __state = count;
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
            }
        }

        /// <summary>
        /// 世界座標が書き戻し前と同じフックだけ localPosition を控えた値へ戻し、丸め誤差を捨てる
        /// </summary>
        private static void UpdatePostfix(DynamicSkirtBone __instance, int __state)
        {
            if (__state == 0)
            {
                return;
            }

            try
            {
                var hooks = GetHookBones(__instance);
                if (hooks == null || hooks.Count != __state)
                {
                    return;
                }

                for (var i = 0; i < __state; i++)
                {
                    var hook = hooks[i];
                    if (hook == null)
                    {
                        continue;
                    }

                    var moved = (hook.position - _worldPositions[i]).sqrMagnitude;
                    if (moved > SOLVER_MOVED_THRESHOLD * SOLVER_MOVED_THRESHOLD)
                    {
                        continue;
                    }
                    hook.localPosition = _localPositions[i];
                }
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
            }
        }

        private static MethodInfo FindUpdateMethod()
        {
            foreach (var name in UPDATE_METHOD_NAMES)
            {
                var method = AccessTools.Method(typeof(DynamicSkirtBone), name);
                if (method != null)
                {
                    return method;
                }
            }
            return null;
        }

        private static List<Transform> GetHookBones(DynamicSkirtBone instance)
        {
            if (_hookBoneListRef == null || instance == null)
            {
                return null;
            }
            return _hookBoneListRef(instance);
        }

        private static void EnsureCapacity(int count)
        {
            if (_localPositions.Length >= count)
            {
                return;
            }
            _localPositions = new Vector3[count];
            _worldPositions = new Vector3[count];
        }
    }
}
