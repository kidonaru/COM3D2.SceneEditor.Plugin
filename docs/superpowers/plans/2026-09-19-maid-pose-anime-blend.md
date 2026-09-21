# モーションウィンドウ アニメブレンド対応 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task (このリポジトリでは subagent-driven-development は使わない). Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** モーションウィンドウから、ベースモーションの上にモーション / マイポーズをアニメレイヤー 2〜8 へ重ねて再生・調整できるようにする。

**Architecture:** 状態はタイムラインと共有する `MaidCache.animationLayerInfos` に置き、操作は新設の static クラス `MaidAnimationBlendController` に集約する。`MaidMotionState` の停止サンプル / 再生再開にフックを足してブレンド層を停止編集モデルと両立させ、`PoseSnapshot` に層の状態を持たせて undo/redo に載せる。UI は `MaidPoseWindow` に「アニメブレンド」区間を足し、一覧クリックの適用先をレイヤーで切り替える。

**Tech Stack:** C# (C# 4 / .NET 3.5 互換の書き方。`?.` などは既存コードで使われている範囲で可)、Unity 5.6 legacy `Animation`、xunit (net48)、MSBuild 2 構成 (COM3D2 / COM3D25)。

**Spec:** `docs/superpowers/specs/2026-09-19-maid-pose-anime-blend-design.md`

## Global Constraints

- コメント・ログ・ダイアログ文言は日本語
- Unity ネイティブを呼ぶコードはテストしない。pure なロジックだけ切り出してテストする
- ビルドは必ず COM3D2 → COM3D25 の順で両方通す (`msbuild-from-bash` memory 参照)。ゲーム起動中に `debug.bat` を使うと DLL コピー警告が出るだけなので可
- `git worktree` は使わない
- 各タスクの最後に `commit` スキルでコミットする (メッセージは Conventional Commits の日本語)
- テスト実行: `cd source/COM3D2.SceneEditor.Plugin.Tests && dotnet test` (net48。プラグイン本体を先にビルドしておくこと)
- レイヤー番号の範囲は `MTEP.MaidCache.MinLayerIndex` (=2) 〜 `MaxLayerIndex` (=8) を参照し、数値をハードコードしない

---

### Task 1: state 名の解決とブレンド用 anmName の決定 (pure ロジック)

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/AnimationBlendNameResolver.cs`
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/AnimationBlendNameResolverTests.cs`

**Interfaces:**
- Produces:
  - `static string AnimationBlendNameResolver.GetStateTag(string anmTag)` — `AnimationLayerInfo.anmTag` から `AnimationState.name` に一致する文字列 (ファイル名部分) を返す
  - `static string AnimationBlendNameResolver.ResolveMotion(string directFile, bool isMod, bool applyCrcPrefix)` — `PhotoMotionData` 由来の `anmName`。`directFile` が空なら `null` (スクリプト経由 = ブレンド不可)
  - `static string AnimationBlendNameResolver.ResolveMyPose(string relativePath)` — `"sub\\name"` → `"sub\\name.anm"`

- [ ] **Step 1: 失敗するテストを書く**

```csharp
using COM3D2.SceneEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// ブレンド層に載せるアニメ名の決定を固定する。
    /// anmName はタイムライン (MaidCache.LoadAnimationLayer) が再ロードできる形で、
    /// state 名は CrossFadeLayerByFullPath がファイル名を使う仕様に合わせる
    /// </summary>
    public class AnimationBlendNameResolverTests
    {
        [Fact]
        public void state名はanmTagのファイル名部分になる()
        {
            Assert.Equal("name.anm", AnimationBlendNameResolver.GetStateTag("sub\\name.anm"));
            Assert.Equal("name.anm", AnimationBlendNameResolver.GetStateTag("c:\\mod\\name.anm"));
            Assert.Equal("dance.anm", AnimationBlendNameResolver.GetStateTag("dance.anm"));
            Assert.Equal("", AnimationBlendNameResolver.GetStateTag(""));
            Assert.Equal("", AnimationBlendNameResolver.GetStateTag(null));
        }

        [Fact]
        public void 公式モーションはdirect_fileをそのまま使う()
        {
            Assert.Equal("dance.anm", AnimationBlendNameResolver.ResolveMotion("dance.anm", false, false));
        }

        [Fact]
        public void 新ボディ男の公式モーションはcrc_を前置する()
        {
            Assert.Equal("crc_dance.anm", AnimationBlendNameResolver.ResolveMotion("dance.anm", false, true));
        }

        [Fact]
        public void Modモーションは絶対パスのままでcrc_を付けない()
        {
            Assert.Equal("c:\\mod\\x.anm", AnimationBlendNameResolver.ResolveMotion("c:\\mod\\x.anm", true, true));
        }

        [Fact]
        public void スクリプト経由はnullになる()
        {
            Assert.Null(AnimationBlendNameResolver.ResolveMotion("", false, false));
            Assert.Null(AnimationBlendNameResolver.ResolveMotion(null, false, false));
        }

        [Fact]
        public void マイポーズは相対パスに拡張子を足す()
        {
            Assert.Equal("sub\\pose.anm", AnimationBlendNameResolver.ResolveMyPose("sub\\pose"));
            Assert.Equal("pose.anm", AnimationBlendNameResolver.ResolveMyPose("pose"));
        }
    }
}
```

- [ ] **Step 2: テストが失敗することを確認する**

Run: `cd source/COM3D2.SceneEditor.Plugin.Tests && dotnet test --filter AnimationBlendNameResolverTests`
Expected: ビルドエラー (`AnimationBlendNameResolver` が存在しない)

- [ ] **Step 3: 実装する**

```csharp
using System.IO;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// ブレンド層 (アニメレイヤー 2〜8) に載せるアニメ名の決定。
    /// anmName はタイムラインの MaidCache.LoadAnimationLayer が再ロードできる形にそろえる
    /// (公式: ファイル名、Mod: 絶対パス、マイポーズ: 保存フォルダからの相対パス)。
    /// Unity に依存しない pure なロジックだけを置く
    /// </summary>
    public static class AnimationBlendNameResolver
    {
        /// <summary>
        /// anmTag (anmName の小文字) に対応する AnimationState.name。
        /// CrossFadeLayerByFullPath は state 名にファイル名だけを使うため、
        /// サブフォルダ入りマイポーズや絶対パスではファイル名部分で突き合わせる
        /// </summary>
        public static string GetStateTag(string anmTag)
        {
            if (string.IsNullOrEmpty(anmTag))
            {
                return "";
            }
            return Path.GetFileName(anmTag);
        }

        /// <summary>
        /// 一覧のモーションから anmName を決める。
        /// applyCrcPrefix は PhotoMotionData.Apply と同じ「新ボディ有効かつ男」の判定を呼び出し側で済ませて渡す。
        /// direct_file が無い (スクリプト経由) モーションはレイヤーへ載せられないため null
        /// </summary>
        public static string ResolveMotion(string directFile, bool isMod, bool applyCrcPrefix)
        {
            if (string.IsNullOrEmpty(directFile))
            {
                return null;
            }
            if (!isMod && applyCrcPrefix)
            {
                return "crc_" + directFile;
            }
            return directFile;
        }

        /// <summary>マイポーズの相対パス ("sub\name") を保存ファイル名に揃える</summary>
        public static string ResolveMyPose(string relativePath)
        {
            return relativePath + ".anm";
        }
    }
}
```

- [ ] **Step 4: ビルドしてテストを通す**

Run (プロジェクト直下):
```
MSBuild source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2 "/p:COM3D2_DIR=W:/COM3D2" "/p:COM3D25_DIR=W:/COM3D2_5"
MSBuild source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:/COM3D2" "/p:COM3D25_DIR=W:/COM3D2_5"
cd source/COM3D2.SceneEditor.Plugin.Tests && dotnet test --filter AnimationBlendNameResolverTests
```
Expected: 6 tests PASS

- [ ] **Step 5: コミット**

`commit` スキルで `feat(maid): ブレンド層のアニメ名解決を追加する` 相当のメッセージでコミット。

---

### Task 2: MaidCache のレイヤーロードを共用メソッドへ切り出し、絶対パスと state 名の突き合わせを直す

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/MaidCache.cs:1072-1131` (`ApplyAnimationLayerInfo`)

**Interfaces:**
- Consumes: `AnimationBlendNameResolver.GetStateTag` (MaidCache は `COM3D2.MotionTimelineEditor.Plugin` 名前空間なので既存の `SEP` エイリアス経由で呼ぶ)
- Produces: `public AnimationState MaidCache.LoadAnimationLayer(AnimationLayerInfo info)` — `info.anmName` を解決してそのレイヤーへ CrossFade し、`info.state` を更新して返す (失敗時 null)。空名なら既存 state を無効化して null

- [ ] **Step 1: `ApplyAnimationLayerInfo` のロード部分を切り出す**

`ApplyAnimationLayerInfo` を次の形にする (再ロード判定と `LoadAnimationLayer` 以外は現状維持):

```csharp
        public void ApplyAnimationLayerInfo(AnimationLayerInfo info, float t)
        {
            animationLayerInfos[info.layer] = info;

            if (animation == null)
            {
                return;
            }

            // state 名はファイル名部分 (CrossFadeLayerByFullPath の仕様)。
            // サブフォルダ入りマイポーズや絶対パスを anmTag と直接比べると毎フレーム再ロードしてしまう
            if (info.state == null
                || info.state.name != SEP.AnimationBlendNameResolver.GetStateTag(info.anmTag))
            {
                LoadAnimationLayer(info);
            }

            if (info.state == null)
            {
                return;
            }

            info.state.wrapMode = info.loop ? WrapMode.Loop : WrapMode.Once;
            info.state.weight = info.weight;

            info.state.time = t;
            info.state.speed = 0f;
        }

        /// <summary>
        /// info.anmName をロードしてそのレイヤーへ載せ、info.state を更新して返す。
        /// 名前解決の順: ゲーム内ファイル → 絶対パス (Mod モーション) → マイポーズ保存フォルダ。
        /// 空名なら既存 state を無効化して null。
        /// タイムラインの再生とモーションウィンドウのブレンド操作で共用する
        /// </summary>
        public AnimationState LoadAnimationLayer(AnimationLayerInfo info)
        {
            if (animation == null)
            {
                return null;
            }

            if (string.IsNullOrEmpty(info.anmName))
            {
                if (info.state != null)
                {
                    info.state.enabled = false;
                    info.state = null;
                }
                return null;
            }

            if (GameUty.IsExistFile(info.anmName))
            {
                info.state = maid.body0.CrossFadeLayer(
                    info.anmName,
                    GameUty.FileSystem,
                    info.layer,
                    false,
                    info.loop,
                    false,
                    0f,
                    info.weight);
                return info.state;
            }

            // Mod モーションは一覧の direct_file (絶対パス) をそのまま anmName にしている
            var path = File.Exists(info.anmName)
                ? info.anmName
                : MTEUtils.CombinePaths(PhotoModePoseSave.folder_path, info.anmName);
            if (!File.Exists(path))
            {
                MTEUtils.LogWarning("アニメレイヤーのファイルが見つかりません: " + info.anmName);
                return null;
            }

            info.state = maid.body0.CrossFadeLayerByFullPath(
                path,
                info.layer,
                false,
                info.loop,
                false,
                0f,
                info.weight);
            return info.state;
        }
```

- [ ] **Step 2: 両構成をビルドし、既存テストが通ることを確認する**

Run: Task 1 Step 4 と同じ MSBuild 2 回 + `dotnet test`
Expected: ビルド成功、テスト全 PASS

- [ ] **Step 3: コミット**

`refactor(timeline): アニメレイヤーのロードを切り出し絶対パスと state 名の突き合わせを直す`

---

### Task 3: `MaidAnimationBlendController` — 層の状態同期・適用・削除・値操作

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidAnimationBlendController.cs`
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/MaidAnimationBlendLayerStateTests.cs`

**Interfaces:**
- Consumes: `MaidCache.LoadAnimationLayer`, `AnimationBlendNameResolver`, `MTEUtils` 拡張 (`GetPlayingTime`, `StopAndDestroy`, `GetAnimation`)、`MaidMotionState.IsPlaying`
- Produces (すべて static):
  - `sealed class LayerState { int layer; string anmName; float time; float weight; float speed; bool loop; bool playing; bool Approximately(LayerState) }`
  - `int MinLayer` / `int MaxLayer` (MaidCache の定数を転写)
  - `List<AnimationLayerInfo> GetLayerInfos(Maid)` (MaidCache が無ければ null)
  - `AnimationLayerInfo GetLayerInfo(Maid, int layer)`
  - `void SyncFromAnimation(Maid)`
  - `bool ApplyMotion(Maid, int layer, PhotoMotionData)` / `bool ApplyMyPose(Maid, int layer, string relativePath)`
  - `void RemoveLayer(Maid, int layer)`
  - `void SetTime / SetWeight / SetSpeed (Maid, int layer, float)`, `void SetLoop(Maid, int layer, bool)`, `void Play(Maid, int layer)`, `void Stop(Maid, int layer)`
  - `bool IsLayerPlaying(Maid, int layer)`
  - `void CaptureTimesBeforeStop(Maid)`, `void EnableStatesForSample(Maid)`, `void DisableStatesAfterSample(Maid)`, `void ResumeAfterPlay(Maid)`
  - `List<LayerState> Capture(Maid)`, `void Restore(Maid, List<LayerState>)`
  - `static bool LayerStatesApproximately(List<LayerState> a, List<LayerState> b)`

- [ ] **Step 1: `LayerState` の比較テストを書く**

```csharp
using System.Collections.Generic;
using COM3D2.SceneEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// ブレンド層スナップショットの同値判定を固定する。
    /// PoseSnapshot.Approximately から使い、変化なしの編集を履歴に積まないための判定
    /// </summary>
    public class MaidAnimationBlendLayerStateTests
    {
        private static MaidAnimationBlendController.LayerState Create(
            int layer = 2, string anmName = "a.anm", float time = 0.5f,
            float weight = 1f, float speed = 1f, bool loop = true, bool playing = false)
        {
            return new MaidAnimationBlendController.LayerState
            {
                layer = layer, anmName = anmName, time = time,
                weight = weight, speed = speed, loop = loop, playing = playing,
            };
        }

        [Fact]
        public void 同じ値は同値()
        {
            Assert.True(Create().Approximately(Create()));
        }

        [Fact]
        public void 微小な数値差は同値扱い()
        {
            Assert.True(Create(time: 0.5f).Approximately(Create(time: 0.5f + 1e-5f)));
        }

        [Fact]
        public void 再生中は時間の違いを無視する()
        {
            // 再生中の層は毎フレーム time が進むため、履歴の同値判定から外す
            Assert.True(Create(playing: true, time: 0.5f).Approximately(Create(playing: true, time: 3f)));
        }

        [Fact]
        public void 名前_ループ_再生中の違いは別物()
        {
            Assert.False(Create().Approximately(Create(anmName: "b.anm")));
            Assert.False(Create().Approximately(Create(loop: false)));
            Assert.False(Create().Approximately(Create(playing: true)));
        }

        [Fact]
        public void 重み_速度_停止中の時間の違いは別物()
        {
            Assert.False(Create().Approximately(Create(weight: 0.5f)));
            Assert.False(Create().Approximately(Create(speed: 0.5f)));
            Assert.False(Create().Approximately(Create(time: 1f)));
        }

        [Fact]
        public void リスト比較はnullと空を同値とし件数差は別物()
        {
            Assert.True(MaidAnimationBlendController.LayerStatesApproximately(null, new List<MaidAnimationBlendController.LayerState>()));
            Assert.True(MaidAnimationBlendController.LayerStatesApproximately(null, null));
            Assert.False(MaidAnimationBlendController.LayerStatesApproximately(
                new List<MaidAnimationBlendController.LayerState> { Create() }, null));
            Assert.True(MaidAnimationBlendController.LayerStatesApproximately(
                new List<MaidAnimationBlendController.LayerState> { Create() },
                new List<MaidAnimationBlendController.LayerState> { Create() }));
        }
    }
}
```

- [ ] **Step 2: テストが失敗することを確認する**

Run: `dotnet test --filter MaidAnimationBlendLayerStateTests`
Expected: ビルドエラー (型が無い)

- [ ] **Step 3: コントローラを実装する**

```csharp
using System;
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// アニメブレンド (レイヤー 2〜8 への重ね再生) の操作。
    /// 状態はタイムラインと共有する MaidCache.animationLayerInfos に置き、
    /// ここでは「実 AnimationState との同期」「適用 / 削除」「値の反映」「停止編集との両立」だけを担う。
    /// ベースモーション (レイヤー 0) の停止・再開は MaidMotionState の責務で、
    /// そちらから CaptureTimesBeforeStop / EnableStatesForSample / ResumeAfterPlay を呼んでもらう
    /// </summary>
    public static class MaidAnimationBlendController
    {
        public static int MinLayer => MTEP.MaidCache.MinLayerIndex;
        public static int MaxLayer => MTEP.MaidCache.MaxLayerIndex;

        /// <summary>履歴用の層スナップショット。Unity 型を持たない</summary>
        public sealed class LayerState
        {
            public int layer;
            public string anmName;
            public float time;
            public float weight;
            public float speed;
            public bool loop;
            public bool playing;

            public bool Approximately(LayerState other)
            {
                if (other == null
                    || layer != other.layer
                    || anmName != other.anmName
                    || loop != other.loop
                    || playing != other.playing
                    || Mathf.Abs(weight - other.weight) >= 1e-4f
                    || Mathf.Abs(speed - other.speed) >= 1e-4f)
                {
                    return false;
                }
                // 再生中の層は毎フレーム time が進むため比較しない。
                // PoseSnapshot が _playbackTime を除外しているのと同じ理由で、
                // 含めると重みスライダーを触っただけの操作まで差分ありと判定される。
                // 停止中の層は time のスクラブ自体が編集操作なので比較する
                if (playing)
                {
                    return true;
                }
                return Mathf.Abs(time - other.time) < 1e-4f;
            }
        }

        /// <summary>null と空リストは同値。順序込みで比較する</summary>
        public static bool LayerStatesApproximately(List<LayerState> a, List<LayerState> b)
        {
            var countA = a != null ? a.Count : 0;
            var countB = b != null ? b.Count : 0;
            if (countA != countB)
            {
                return false;
            }
            for (var i = 0; i < countA; i++)
            {
                if (!a[i].Approximately(b[i]))
                {
                    return false;
                }
            }
            return true;
        }

        private static MTEP.MaidCache GetMaidCache(Maid maid)
        {
            return maid != null ? MTEP.MaidManager.instance.GetMaidCache(maid) : null;
        }

        private static Animation GetAnimation(Maid maid)
        {
            if (maid == null || maid.body0 == null || maid.body0.m_Bones == null)
            {
                return null;
            }
            return maid.GetAnimation();
        }

        /// <summary>MaidCache が無い (呼出直後など) 場合は null</summary>
        public static List<AnimationLayerInfo> GetLayerInfos(Maid maid)
        {
            var cache = GetMaidCache(maid);
            return cache != null ? cache.animationLayerInfos : null;
        }

        public static AnimationLayerInfo GetLayerInfo(Maid maid, int layer)
        {
            var cache = GetMaidCache(maid);
            return cache != null ? cache.GetAnimationLayerInfo(layer) : null;
        }

        /// <summary>info.state がまだ Animation 上に生きているか</summary>
        private static bool IsStateAlive(Animation anim, AnimationLayerInfo info)
        {
            if (info.state == null)
            {
                return false;
            }
            try
            {
                return anim.GetClip(info.state.name) != null;
            }
            catch (Exception)
            {
                // 破棄済みの AnimationState はメンバーアクセスで例外になる
                return false;
            }
        }

        /// <summary>
        /// 実 AnimationState から info を同期する。毎フレーム (ウィンドウ表示中) 呼ぶ。
        /// ベースアニメが変わると MaidCache.ResetAnm が info を空にするが state は残るため、
        /// enabled な state を拾い直す (ModItemExplorer の UpdateAnimationLayerInfos と同じ)。
        /// 逆に state が破棄されていれば info を空へ戻す
        /// </summary>
        public static void SyncFromAnimation(Maid maid)
        {
            var anim = GetAnimation(maid);
            var infos = GetLayerInfos(maid);
            if (anim == null || infos == null)
            {
                return;
            }

            for (var layer = MinLayer; layer <= MaxLayer; layer++)
            {
                var info = infos.GetOrDefault(layer);
                if (info == null)
                {
                    continue;
                }

                if (IsStateAlive(anim, info))
                {
                    continue;
                }

                AnimationState found = null;
                foreach (AnimationState state in anim)
                {
                    if (state != null && state.layer == layer && state.enabled)
                    {
                        found = state;
                        break;
                    }
                }

                if (found == null)
                {
                    if (info.state != null || !string.IsNullOrEmpty(info.anmName))
                    {
                        info.Reset();
                    }
                    continue;
                }

                // 名前しか復元できない (絶対パス等は失われる)。重み・速度は state から取る
                info.anmName = found.name;
                info.state = found;
                info.weight = found.weight;
                info.speed = found.speed > 0f ? found.speed : info.speed;
                info.loop = found.wrapMode == WrapMode.Loop;
            }
        }

        /// <summary>
        /// 一覧のモーションをレイヤーへ載せる。スクリプト経由や読込失敗は false
        /// (呼び出し元がダイアログを出す)
        /// </summary>
        public static bool ApplyMotion(Maid maid, int layer, PhotoMotionData data)
        {
            if (data == null)
            {
                return false;
            }
            // PhotoMotionData.Apply と同じ判定で crc_ を前置する (2.0 には新ボディ男の概念が無い)
#if COM3D25
            var applyCrc = Product.isEnabledNewBodyMan && maid.boMAN;
#else
            var applyCrc = false;
#endif
            var anmName = AnimationBlendNameResolver.ResolveMotion(data.direct_file, data.is_mod, applyCrc);
            if (anmName == null)
            {
                return false;
            }
            return ApplyAnmName(maid, layer, anmName, data.is_loop);
        }

        public static bool ApplyMyPose(Maid maid, int layer, string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath))
            {
                return false;
            }
            return ApplyAnmName(maid, layer, AnimationBlendNameResolver.ResolveMyPose(relativePath), true);
        }

        private static bool ApplyAnmName(Maid maid, int layer, string anmName, bool loop)
        {
            var cache = GetMaidCache(maid);
            var anim = GetAnimation(maid);
            var info = cache != null ? cache.GetAnimationLayerInfo(layer) : null;
            if (anim == null || info == null || layer < MinLayer || layer > MaxLayer)
            {
                MTEUtils.LogWarning("アニメレイヤーの情報が見つかりません。layer=" + layer);
                return false;
            }

            // Unity legacy Animation はクリップ名を Animation コンポーネント全体で一意な
            // キーとして扱う。同じ anmName を別の層へ重ねると AnimationState が
            // 後勝ちで移動し、元の層が黙って消える。先に他層を解放しておく
            ReleaseDuplicatedLayers(maid, anim, cache, layer, anmName);

            // 同じ層に載っていた旧クリップは破棄する (CrossFadeLayer は名前が違えば残す)
            RemoveStateOnly(maid, anim, info);

            info.anmName = anmName;
            info.loop = loop;
            info.startTime = 0f;
            var state = cache.LoadAnimationLayer(info);
            if (state == null)
            {
                info.Reset();
                return false;
            }

            state.wrapMode = loop ? WrapMode.Loop : WrapMode.Once;
            state.weight = info.weight;
            state.time = 0f;

            if (MaidMotionState.IsPlaying(maid))
            {
                state.enabled = true;
                state.speed = info.speed;
            }
            else
            {
                // 停止編集中はベースと同じく止めたまま今のフレームへ反映する
                state.speed = 0f;
                SampleStopped(maid, anim);
            }
            return true;
        }

        /// <summary>
        /// 同じ anmName を載せている他の層を解放する。
        /// クリップ名が Animation 全体で一意なため、重複適用は共存できない
        /// </summary>
        private static void ReleaseDuplicatedLayers(
            Maid maid, Animation anim, MTEP.MaidCache cache, int layer, string anmName)
        {
            var stateTag = AnimationBlendNameResolver.GetStateTag(anmName.ToLower());
            for (var i = MinLayer; i <= MaxLayer; i++)
            {
                if (i == layer)
                {
                    continue;
                }
                var other = cache.GetAnimationLayerInfo(i);
                if (other == null || string.IsNullOrEmpty(other.anmName))
                {
                    continue;
                }
                if (AnimationBlendNameResolver.GetStateTag(other.anmName.ToLower()) != stateTag)
                {
                    continue;
                }
                MTEUtils.LogWarning(
                    "同じモーションがレイヤー " + i + " に載っているため解放します。anmName=" + anmName);
                RemoveStateOnly(maid, anim, other);
                other.Reset();
            }
        }

        /// <summary>info の state をクリップごと破棄する。info 自体は触らない</summary>
        private static void RemoveStateOnly(Maid maid, Animation anim, AnimationLayerInfo info)
        {
            if (!IsStateAlive(anim, info))
            {
                info.state = null;
                return;
            }
            maid.body0.StopAndDestroy(info.state.name);
            info.state = null;
        }

        public static void RemoveLayer(Maid maid, int layer)
        {
            var anim = GetAnimation(maid);
            var info = GetLayerInfo(maid, layer);
            if (anim == null || info == null)
            {
                return;
            }
            RemoveStateOnly(maid, anim, info);
            info.Reset();
            if (!MaidMotionState.IsPlaying(maid))
            {
                SampleStopped(maid, anim);
            }
        }

        /// <summary>
        /// 停止中にベース + ブレンド層で 1 フレームぶんサンプルする。
        /// MaidMotionState.SetPlaybackTime と同じ流儀 (一時的に有効化して戻す)
        /// </summary>
        private static void SampleStopped(Maid maid, Animation anim)
        {
            var baseState = MaidMotionState.GetCurrentAnimationState(maid);
            if (baseState != null)
            {
                baseState.enabled = true;
                baseState.weight = 1f;
            }
            EnableStatesForSample(maid);
            anim.Sample();
            DisableStatesAfterSample(maid);
            if (baseState != null)
            {
                baseState.enabled = false;
            }
            MaidBoneSliderController.CaptureBasePose(maid);
        }

        private static AnimationLayerInfo GetLiveInfo(Maid maid, int layer, out Animation anim)
        {
            anim = GetAnimation(maid);
            var info = GetLayerInfo(maid, layer);
            if (anim == null || info == null || !IsStateAlive(anim, info))
            {
                return null;
            }
            return info;
        }

        public static void SetTime(Maid maid, int layer, float time)
        {
            Animation anim;
            var info = GetLiveInfo(maid, layer, out anim);
            if (info == null)
            {
                return;
            }
            info.startTime = time;
            info.state.time = time;
            if (!MaidMotionState.IsPlaying(maid))
            {
                SampleStopped(maid, anim);
            }
        }

        public static void SetWeight(Maid maid, int layer, float weight)
        {
            Animation anim;
            var info = GetLiveInfo(maid, layer, out anim);
            if (info == null)
            {
                return;
            }
            info.weight = weight;
            info.state.weight = weight;
            if (!MaidMotionState.IsPlaying(maid))
            {
                SampleStopped(maid, anim);
            }
        }

        public static void SetSpeed(Maid maid, int layer, float speed)
        {
            Animation anim;
            var info = GetLiveInfo(maid, layer, out anim);
            if (info == null)
            {
                return;
            }
            info.speed = speed;
            // 停止中 (speed=0) の state は動かさない。再生時に info.speed が使われる
            if (info.state.speed > 0f)
            {
                info.state.speed = speed;
            }
        }

        public static void SetLoop(Maid maid, int layer, bool loop)
        {
            Animation anim;
            var info = GetLiveInfo(maid, layer, out anim);
            if (info == null)
            {
                return;
            }
            info.loop = loop;
            info.state.wrapMode = loop ? WrapMode.Loop : WrapMode.Once;
        }

        /// <summary>層が動いているか。ベースが止まっていれば層も止まっている扱い</summary>
        public static bool IsLayerPlaying(Maid maid, int layer)
        {
            Animation anim;
            var info = GetLiveInfo(maid, layer, out anim);
            return info != null && MaidMotionState.IsPlaying(maid)
                && info.state.enabled && info.state.speed > 0f;
        }

        /// <summary>層だけ流す。ベースが停止中なら何もしない (停止編集を崩さない)</summary>
        public static void Play(Maid maid, int layer)
        {
            Animation anim;
            var info = GetLiveInfo(maid, layer, out anim);
            if (info == null || !MaidMotionState.IsPlaying(maid))
            {
                return;
            }
            info.state.enabled = true;
            info.state.speed = info.speed > 0f ? info.speed : 1f;
        }

        /// <summary>層だけ止める (speed=0 で現在フレームを保つ)</summary>
        public static void Stop(Maid maid, int layer)
        {
            Animation anim;
            var info = GetLiveInfo(maid, layer, out anim);
            if (info == null)
            {
                return;
            }
            info.startTime = info.state.GetPlayingTime();
            info.state.speed = 0f;
        }

        // ---- MaidMotionState から呼ぶ停止編集との両立フック ----

        /// <summary>
        /// Animation.Stop() は全 state を巻き戻すため、直前の再生位置を info.startTime に控える。
        /// anim.Stop() の前に呼ぶこと
        /// </summary>
        public static void CaptureTimesBeforeStop(Maid maid)
        {
            var anim = GetAnimation(maid);
            var infos = GetLayerInfos(maid);
            if (anim == null || infos == null)
            {
                return;
            }
            foreach (var info in infos)
            {
                if (info.layer >= MinLayer && IsStateAlive(anim, info))
                {
                    info.startTime = info.state.GetPlayingTime();
                }
            }
        }

        /// <summary>停止中サンプルの直前に、層の state を控えた位置・重みで有効化する</summary>
        public static void EnableStatesForSample(Maid maid)
        {
            var anim = GetAnimation(maid);
            var infos = GetLayerInfos(maid);
            if (anim == null || infos == null)
            {
                return;
            }
            foreach (var info in infos)
            {
                if (info.layer < MinLayer || !IsStateAlive(anim, info))
                {
                    continue;
                }
                info.state.enabled = true;
                info.state.weight = info.weight;
                info.state.time = info.startTime;
            }
        }

        /// <summary>停止中サンプルの直後に層の state を無効へ戻す</summary>
        public static void DisableStatesAfterSample(Maid maid)
        {
            var anim = GetAnimation(maid);
            var infos = GetLayerInfos(maid);
            if (anim == null || infos == null)
            {
                return;
            }
            foreach (var info in infos)
            {
                if (info.layer >= MinLayer && IsStateAlive(anim, info))
                {
                    info.state.enabled = false;
                }
            }
        }

        /// <summary>
        /// ベース再生の再開後に層を流し直す。anim.Play(clip) は同じレイヤーしか止めないが、
        /// 直前の anim.Stop() で層は無効化済みのためここで戻す
        /// </summary>
        public static void ResumeAfterPlay(Maid maid)
        {
            var anim = GetAnimation(maid);
            var infos = GetLayerInfos(maid);
            if (anim == null || infos == null)
            {
                return;
            }
            foreach (var info in infos)
            {
                if (info.layer < MinLayer || !IsStateAlive(anim, info))
                {
                    continue;
                }
                info.state.enabled = true;
                info.state.weight = info.weight;
                info.state.time = info.startTime;
                info.state.speed = info.speed;
            }
        }

        // ---- 履歴 ----

        /// <summary>層の状態を控える。何も載っていなければ空リスト</summary>
        public static List<LayerState> Capture(Maid maid)
        {
            var result = new List<LayerState>();
            var anim = GetAnimation(maid);
            var infos = GetLayerInfos(maid);
            if (anim == null || infos == null)
            {
                return result;
            }
            foreach (var info in infos)
            {
                if (info.layer < MinLayer || string.IsNullOrEmpty(info.anmName))
                {
                    continue;
                }
                var alive = IsStateAlive(anim, info);
                result.Add(new LayerState
                {
                    layer = info.layer,
                    anmName = info.anmName,
                    time = alive && info.state.speed > 0f ? info.state.GetPlayingTime() : info.startTime,
                    weight = info.weight,
                    speed = info.speed,
                    loop = info.loop,
                    playing = alive && info.state.enabled && info.state.speed > 0f,
                });
            }
            return result;
        }

        /// <summary>
        /// 控えた状態へ戻す。無い層は削除し、名前が違う層はロードし直す。
        /// ベースの再生状態は呼び出し元 (PoseSnapshot.RestoreMotion) が先に戻しておくこと
        /// </summary>
        public static void Restore(Maid maid, List<LayerState> states)
        {
            var cache = GetMaidCache(maid);
            var anim = GetAnimation(maid);
            if (cache == null || anim == null)
            {
                return;
            }

            for (var layer = MinLayer; layer <= MaxLayer; layer++)
            {
                LayerState target = null;
                if (states != null)
                {
                    foreach (var s in states)
                    {
                        if (s.layer == layer)
                        {
                            target = s;
                            break;
                        }
                    }
                }

                var info = cache.GetAnimationLayerInfo(layer);
                if (info == null)
                {
                    continue;
                }

                if (target == null)
                {
                    if (!string.IsNullOrEmpty(info.anmName) || info.state != null)
                    {
                        RemoveStateOnly(maid, anim, info);
                        info.Reset();
                    }
                    continue;
                }

                if (info.anmName != target.anmName || !IsStateAlive(anim, info))
                {
                    RemoveStateOnly(maid, anim, info);
                    info.anmName = target.anmName;
                    info.loop = target.loop;
                    info.weight = target.weight;
                    if (cache.LoadAnimationLayer(info) == null)
                    {
                        info.Reset();
                        continue;
                    }
                }

                info.startTime = target.time;
                info.weight = target.weight;
                info.speed = target.speed;
                info.loop = target.loop;
                info.state.wrapMode = target.loop ? WrapMode.Loop : WrapMode.Once;
                info.state.weight = target.weight;
                info.state.time = target.time;
                var basePlaying = MaidMotionState.IsPlaying(maid);
                info.state.enabled = basePlaying;
                info.state.speed = basePlaying && target.playing ? target.speed : 0f;
            }

            if (!MaidMotionState.IsPlaying(maid))
            {
                SampleStopped(maid, anim);
            }
        }
    }
}
```

- [ ] **Step 4: 両構成をビルドし、テストを通す**

Run: MSBuild 2 回 + `dotnet test --filter MaidAnimationBlendLayerStateTests`
Expected: ビルド成功、5 tests PASS

- [ ] **Step 5: コミット**

`feat(maid): アニメブレンド層の操作コントローラを追加する`

---

### Task 4: `MaidMotionState` の停止・再開にブレンド層のフックを足す

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidManipulation/MaidMotionState.cs` (`StopMotion` 126-170, `SampleWhileStopped` 174-187, `SetPlaybackTime` 268-291, `PlayMotion` 355-376, `PlayClip` 382-405, `ResetPose` 471-538)

**Interfaces:**
- Consumes: `MaidAnimationBlendController.CaptureTimesBeforeStop / EnableStatesForSample / DisableStatesAfterSample / ResumeAfterPlay`

- [ ] **Step 1: `StopMotion` で停止前に層の位置を控える**

`var stoppedTime = ...; anim.Stop();` の直前に追加:

```csharp
            // ブレンド層は anim.Stop() で巻き戻るため、先に位置を控える
            MaidAnimationBlendController.CaptureTimesBeforeStop(maid);
```

- [ ] **Step 2: `SampleWhileStopped` で層を乗せる**

`GetComponentInParent<Maid>()` は毎回の走査になるため、シグネチャに `maid` を足して呼び出し元 (`StopMotion`, `ResetPoseWhileStopped`) から渡す。既存の呼び出し元もあわせて直すこと。

```csharp
        private static void SampleWhileStopped(Maid maid, Animation anim, AnimationState state, float time)
        {
            if (state == null)
            {
                return;
            }

            state.enabled = true;
            state.weight = 1f;
            state.time = time;
            // ブレンド層も一緒に乗せないと、停止した瞬間にブレンド分のポーズが消える
            MaidAnimationBlendController.EnableStatesForSample(maid);
            anim.Sample();
            MaidAnimationBlendController.DisableStatesAfterSample(maid);
            state.enabled = false;
        }
```

- [ ] **Step 3: `SetPlaybackTime` で層を乗せる**

```csharp
            var wasPlaying = anim.isPlaying;
            state.enabled = true;
            state.weight = 1f;
            state.time = time;
            if (!wasPlaying)
            {
                MaidAnimationBlendController.EnableStatesForSample(maid);
            }
            anim.Sample();

            if (!wasPlaying)
            {
                MaidAnimationBlendController.DisableStatesAfterSample(maid);
                state.enabled = false;
                MaidBoneSliderController.CaptureBasePose(maid);
            }
```

- [ ] **Step 4: 再生再開の 3 箇所で層を流し直す**

`PlayMotion` の `anim.Play(clipName);` 直後、`PlayClip` の `state.time = time;` を含む if の直後、`ResetPose` の `anim.Play(clipName);` 直後と `anim.Rewind(); anim.Play();` 直後にそれぞれ:

```csharp
                MaidAnimationBlendController.ResumeAfterPlay(maid);
```

- [ ] **Step 5: 両構成をビルドする**

Run: MSBuild 2 回
Expected: ビルド成功

- [ ] **Step 6: 実機で停止編集との両立を確認する (COM3D2.5, devbridge)**

ゲームを起動してモーションウィンドウを開き、次を確認する (UI は Task 6 まで無いため、`eval_csharp` で直接呼ぶ):

```csharp
var maid = GameMain.Instance.CharacterMgr.GetMaid(0);
var data = COM3D2.SceneEditor.Plugin.PhotoMotionUtils.GetMotions("ダンス", false).First();
COM3D2.SceneEditor.Plugin.MaidAnimationBlendController.ApplyMotion(maid, 2, data);
COM3D2.SceneEditor.Plugin.MaidAnimationBlendController.SetWeight(maid, 2, 0.5f);
```

- 再生中に適用 → 2 つのモーションが混ざって動く
- ■ (`MaidMotionState.StopMotion`) → 混ざったポーズのまま止まる
- 再生位置スライダー相当 (`SetPlaybackTime`) → 混ざったまま追従する
- ▶ (`PlayMotion`) → 両方動き出す
- リセット (`ResetPose`) → 層が残ったまま復帰先へ戻る
- 同じモーションをレイヤー 2 と 3 へ続けて適用 → レイヤー 2 が解放され警告ログが出る (クリップ名が Animation 全体で一意なため共存できない。`ReleaseDuplicatedLayers` の確認)
- タイムラインのポーズ編集を開始 → 終了 (`AnimationTimelineLayer.OnPoseEditEnd` が `timelineManager.isMotionEditing` のとき層 2〜8 の `state` を破棄する) → ブレンド層がどう見えるかを確認する。`anmName` は残るので `SyncFromAnimation` が拾い直せるが、Mod の絶対パスは失われる。壊れ方が実用に耐えないなら Task 6 の UI 側で層の再適用導線を用意する

問題があれば Task 3 / 4 を直してから進む。

- [ ] **Step 7: コミット**

`feat(maid): モーション停止・再開でブレンド層を保つ`

---

### Task 5: 履歴 (`PoseSnapshot`) にブレンド層を載せる

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/History/PoseSnapshot.cs` (フィールド 33-45, `Capture` 47-75 付近, `Apply` 176-235 の `RestoreMotion` 呼び出し直後, `Approximately` 236-270)

**Interfaces:**
- Consumes: `MaidAnimationBlendController.Capture / Restore / LayerStatesApproximately`, `LayerState`

- [ ] **Step 1: フィールドと Capture を追加する**

`_appliedMotion` の下に:

```csharp
        /// <summary>記録時のアニメブレンド層。undo/redo で層の載せ替え・重みも戻す</summary>
        private List<MaidAnimationBlendController.LayerState> _blendLayers;
```

`Capture` の `snapshot._appliedMotion = ...;` の直後に:

```csharp
            snapshot._blendLayers = MaidAnimationBlendController.Capture(maid);
```

- [ ] **Step 2: Apply で復元する**

`RestoreMotion(maid);` の直後 (ボーン書き戻しより前か後かは既存順に合わせ、`RestoreMotion` と隣接させる):

```csharp
            // ベースの再生状態を戻してから層を戻す (層の enabled/speed はベースの再生中かで決まる)
            MaidAnimationBlendController.Restore(maid, _blendLayers);
```

- [ ] **Step 3: Approximately に比較を足す**

`_isPlaying != o._isPlaying || _clipName != o._clipName` を含む判定の隣に:

```csharp
            if (!MaidAnimationBlendController.LayerStatesApproximately(_blendLayers, o._blendLayers))
            {
                return false;
            }
```

- [ ] **Step 4: 両構成をビルドし、テスト全体を通す**

Run: MSBuild 2 回 + `dotnet test`
Expected: 成功

- [ ] **Step 5: コミット**

`feat(history): ポーズ履歴にアニメブレンド層を含める`

---

### Task 6: 表示レイヤーの決定 (pure) と `MaidPoseWindow` の UI

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/MaidPoseBlendRows.cs` (表示レイヤー決定の pure ロジック + 行描画)
- Modify: `source/COM3D2.SceneEditor.Plugin/Config.cs:113-117` 付近 (`maidPoseBlendVisible` 追加)
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidPoseWindow.cs` (`DrawMaidContent` 137-174, `ApplyMotionEntry` 564-569, `LoadMyPoseEntry` 695-700, 新規フィールド)
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/MaidPoseBlendRowsTests.cs`

**Interfaces:**
- Consumes: `MaidAnimationBlendController` 全般、`TimelineLayerGate.Begin/End`、`GUIViewAutoEditModeExtensions.BeginAutoEditMode/EndAutoEditMode`
- Produces:
  - `static List<int> MaidPoseBlendRows.GetVisibleLayers(IList<string> anmNamesByLayer, int targetLayer, int minLayer, int maxLayer)` — anmName が空でない層 + 適用先の層 (空でも) を昇順で返す
  - `static void MaidPoseBlendRows.Draw(GUIView view, Maid maid, int targetLayer, Action<int> setTargetLayer, float rowHeight, float labelWidth)`

- [ ] **Step 1: 表示レイヤー決定のテストを書く**

```csharp
using System.Collections.Generic;
using COM3D2.SceneEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>ブレンド区間に描く層の決定を固定する (空の層は適用先だけ見せる)</summary>
    public class MaidPoseBlendRowsTests
    {
        // index = layer 番号。0,1 は未使用
        private static readonly string[] Names = { "", "", "a.anm", "", "b.anm", "", "", "", "" };

        [Fact]
        public void 名前のある層だけを昇順で返す()
        {
            var result = MaidPoseBlendRows.GetVisibleLayers(Names, 0, 2, 8);
            Assert.Equal(new List<int> { 2, 4 }, result);
        }

        [Fact]
        public void 空の適用先は行として含める()
        {
            var result = MaidPoseBlendRows.GetVisibleLayers(Names, 3, 2, 8);
            Assert.Equal(new List<int> { 2, 3, 4 }, result);
        }

        [Fact]
        public void 適用先が既に載っている層なら重複しない()
        {
            var result = MaidPoseBlendRows.GetVisibleLayers(Names, 4, 2, 8);
            Assert.Equal(new List<int> { 2, 4 }, result);
        }

        [Fact]
        public void 適用先が通常なら層は増えない()
        {
            var result = MaidPoseBlendRows.GetVisibleLayers(new string[9], 0, 2, 8);
            Assert.Empty(result);
        }
    }
}
```

- [ ] **Step 2: テストが失敗することを確認する**

Run: `dotnet test --filter MaidPoseBlendRowsTests`
Expected: ビルドエラー

- [ ] **Step 3: Config にトグルを足す**

`Config.cs` の `maidPoseVisible` の直後:

```csharp
        /// <summary>モーションウィンドウのアニメブレンド区間を開いているか</summary>
        public bool maidPoseBlendVisible = false;
```

- [ ] **Step 4: `MaidPoseBlendRows` を実装する**

```csharp
using System;
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// モーションウィンドウのアニメブレンド区間。適用先レイヤーの選択と、
    /// 載っている層ごとの再生時間 / 重み / 速度 / ループ / 削除の行を描く。
    /// 値の書き込みは全て MaidAnimationBlendController 経由で、履歴は HistoryScope.Pose に積む
    /// </summary>
    public static class MaidPoseBlendRows
    {
        /// <summary>適用先コンボの先頭は「通常」(レイヤー 0 = 従来経路)</summary>
        public const int BaseLayer = 0;

        private static readonly GUIComboBox<int> _targetComboBox = new GUIComboBox<int>
        {
            getName = (layer, _) => layer == BaseLayer ? "通常" : "レイヤー" + layer,
            buttonSize = new Vector2(110, 20),
            contentSize = new Vector2(110, 200),
        };

        private static List<int> _targetItems;

        /// <summary>
        /// 描く層の番号。名前が入っている層と、空でも適用先に選ばれている層を昇順で返す
        /// (全 7 段を常に並べると 300px 幅のウィンドウでは一覧が押し出されるため)
        /// </summary>
        public static List<int> GetVisibleLayers(IList<string> anmNamesByLayer, int targetLayer, int minLayer, int maxLayer)
        {
            var result = new List<int>();
            for (var layer = minLayer; layer <= maxLayer; layer++)
            {
                var name = layer < anmNamesByLayer.Count ? anmNamesByLayer[layer] : null;
                if (!string.IsNullOrEmpty(name) || layer == targetLayer)
                {
                    result.Add(layer);
                }
            }
            return result;
        }

        public static void Draw(GUIView view, Maid maid, int targetLayer, Action<int> setTargetLayer,
            float rowHeight, float labelWidth)
        {
            var infos = MaidAnimationBlendController.GetLayerInfos(maid);
            if (infos == null)
            {
                view.DrawLabel("アニメレイヤーの情報がありません", -1, rowHeight, textColor: Color.gray);
                return;
            }

            if (_targetItems == null)
            {
                _targetItems = new List<int> { BaseLayer };
                for (var layer = MaidAnimationBlendController.MinLayer; layer <= MaidAnimationBlendController.MaxLayer; layer++)
                {
                    _targetItems.Add(layer);
                }
            }

            view.BeginHorizontal();
            {
                view.DrawLabel("適用先", labelWidth, rowHeight, style: GUIView.gsLabelRight);
                _targetComboBox.items = _targetItems;
                _targetComboBox.currentIndex = Mathf.Max(0, _targetItems.IndexOf(targetLayer));
                _targetComboBox.onSelected = (layer, _) => setTargetLayer(layer);
                _targetComboBox.DrawButton(view);
            }
            view.EndLayout();

            var names = new List<string>(infos.Count);
            foreach (var info in infos)
            {
                names.Add(info.anmName);
            }

            foreach (var layer in GetVisibleLayers(names, targetLayer,
                MaidAnimationBlendController.MinLayer, MaidAnimationBlendController.MaxLayer))
            {
                DrawLayer(view, maid, infos[layer], rowHeight, labelWidth);
            }
        }

        private static void DrawLayer(GUIView view, Maid maid, AnimationLayerInfo info,
            float rowHeight, float labelWidth)
        {
            var layer = info.layer;
            var hasState = info.state != null && !string.IsNullOrEmpty(info.anmName);
            var length = hasState ? Mathf.Max(info.state.length, 0.01f) : 1f;

            view.DrawHorizontalLine(Color.gray);

            view.BeginHorizontal();
            {
                var title = "レイヤー" + layer + ": " + (hasState
                    ? System.IO.Path.GetFileNameWithoutExtension(info.anmName)
                    : "(未設定)");
                view.DrawLabel(title, -1, rowHeight);

                view.AddRightAlignSpace(30 + 50 + view.margin, rowHeight);

                var playing = MaidAnimationBlendController.IsLayerPlaying(maid, layer);
                // 層の ▶ はベース再生中しか効かない (停止編集を崩さない)
                if (view.DrawButton(playing ? "■" : "▶", 30, rowHeight,
                    enabled: hasState && (playing || MaidMotionState.IsPlaying(maid))))
                {
                    if (playing)
                    {
                        MaidAnimationBlendController.Stop(maid, layer);
                    }
                    else
                    {
                        MaidAnimationBlendController.Play(maid, layer);
                    }
                }

                if (view.DrawButton("削除", 50, rowHeight, enabled: hasState))
                {
                    AutoEditMode.Enter();
                    HistoryManager.instance.BeforeEdit(maid, HistoryScope.Pose,
                        "ブレンド削除: レイヤー" + layer, () => PoseSnapshot.GetAllBodyBones(maid));
                    MaidAnimationBlendController.RemoveLayer(maid, layer);
                }
            }
            view.EndLayout();

            if (!hasState)
            {
                return;
            }

            view.BeginAutoEditMode();

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "再生時間",
                labelWidth = labelWidth,
                width = -1,
                fieldType = FloatFieldType.Float,
                min = 0f,
                max = length,
                step = 0.01f,
                defaultValue = 0f,
                value = info.state.GetPlayingTime(),
                hiddenResetButton = true,
                onChanged = value =>
                {
                    HistoryManager.instance.BeforeEdit(maid, HistoryScope.Pose,
                        "ブレンド再生時間", () => PoseSnapshot.GetAllBodyBones(maid));
                    MaidAnimationBlendController.SetTime(maid, layer, value);
                },
            });

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "重み",
                labelWidth = labelWidth,
                width = -1,
                fieldType = FloatFieldType.Float,
                min = 0f,
                max = 1f,
                step = 0.01f,
                defaultValue = 1f,
                value = info.weight,
                onChanged = value =>
                {
                    HistoryManager.instance.BeforeEdit(maid, HistoryScope.Pose,
                        "ブレンド重み", () => PoseSnapshot.GetAllBodyBones(maid));
                    MaidAnimationBlendController.SetWeight(maid, layer, value);
                },
            });

            view.BeginHorizontal();
            {
                view.DrawSliderValue(new GUIView.SliderOption
                {
                    label = "速度",
                    labelWidth = labelWidth,
                    width = view.viewRect.width - 70 - view.margin,
                    fieldType = FloatFieldType.Float,
                    min = 0f,
                    max = 2f,
                    step = 0.01f,
                    defaultValue = 1f,
                    value = info.speed,
                    onChanged = value =>
                    {
                        HistoryManager.instance.BeforeEdit(maid, HistoryScope.Pose,
                            "ブレンド速度", () => PoseSnapshot.GetAllBodyBones(maid));
                        MaidAnimationBlendController.SetSpeed(maid, layer, value);
                    },
                });

                view.DrawToggle("ループ", info.loop, 70, rowHeight, value =>
                {
                    HistoryManager.instance.BeforeEdit(maid, HistoryScope.Pose,
                        "ブレンドループ", () => PoseSnapshot.GetAllBodyBones(maid));
                    MaidAnimationBlendController.SetLoop(maid, layer, value);
                });
            }
            view.EndLayout();

            view.EndAutoEditMode();
        }
    }
}
```

`AutoEditMode.Enter()` の実シグネチャは `AutoEditMode.cs` を読んで合わせる (引数なしの static でなければ既存呼び出し例に倣う)。`GUIView.SliderOption` の `width = -1` が「残り幅」を意味するかは `MaidPoseWindow.DrawPlaybackRows` (再生位置スライダー) と同じ書き方なので踏襲する。

- [ ] **Step 5: `MaidPoseWindow` に区間と適用先を組み込む**

フィールド追加:

```csharp
        /// <summary>一覧クリックの適用先レイヤー。0 は従来のベースモーション</summary>
        private int _blendTargetLayer = MaidPoseBlendRows.BaseLayer;
```

`DrawMaidContent` の `DrawPlaybackRows(view, target); view.DrawHorizontalLine();` の直後に:

```csharp
            DrawBlendSection(view, target);
```

メソッド追加:

```csharp
        /// <summary>
        /// アニメブレンド区間。トグルで開閉し、開いている間だけ層の同期と描画を行う。
        /// ゲートはアニメレイヤー (AnimationTimelineLayer) で張り直し、区間の後で
        /// モーションレイヤーのゲートへ戻す (以降の一覧は従来どおりモーションレイヤーの範囲)
        /// </summary>
        private void DrawBlendSection(GUIView view, Maid maid)
        {
            view.DrawToggle("アニメブレンド", config.maidPoseBlendVisible, -1, ROW_HEIGHT,
                value => config.maidPoseBlendVisible = value);
            if (!config.maidPoseBlendVisible)
            {
                // 閉じている間は適用先も通常へ戻す (見えない層へ載せてしまわないように)
                _blendTargetLayer = MaidPoseBlendRows.BaseLayer;
                view.DrawHorizontalLine();
                return;
            }

            MaidAnimationBlendController.SyncFromAnimation(maid);

            TimelineLayerGate.End(view);
            TimelineLayerGate.Begin(view, typeof(MTEP.AnimationTimelineLayer), maid, ROW_HEIGHT);
            MaidPoseBlendRows.Draw(view, maid, _blendTargetLayer,
                layer => _blendTargetLayer = layer, ROW_HEIGHT, LABEL_WIDTH);
            TimelineLayerGate.End(view);
            TimelineLayerGate.Begin(view, typeof(MTEP.MotionTimelineLayer), maid, ROW_HEIGHT);

            view.DrawHorizontalLine();
        }
```

一覧ボタンのクリック (`DrawMotionButtons` / `DrawMyPoseEntries`) だけを適用先で分岐する。
前後送り (`ApplyNavEntry`) は「再生中のベースエントリが属する一覧」を送る機能なので、
既存の static メソッド (`ApplyMotionEntry` / `LoadMyPoseEntry`) をそのまま使い、常にベースへ適用する。

`DrawMotionButtons` の `ApplyMotionEntry(maid, data);` を `ApplyMotionEntryToTarget(maid, data);` に、
`DrawMyPoseEntries` の `LoadMyPoseEntry(maid, myPoseDir, poseName);` を `LoadMyPoseEntryToTarget(maid, myPoseDir, poseName);` に置き換え、次を追加する:

```csharp
        /// <summary>
        /// 一覧クリックの適用。適用先が「通常」ならベースを差し替え、レイヤーならブレンド層へ載せる。
        /// 前後送りは常にベース (ApplyMotionEntry) なのでこちらは通らない
        /// </summary>
        private void ApplyMotionEntryToTarget(Maid maid, PhotoMotionData data)
        {
            if (_blendTargetLayer == MaidPoseBlendRows.BaseLayer)
            {
                ApplyMotionEntry(maid, data);
                return;
            }

            AutoEditMode.Enter();
            HistoryManager.instance.BeforeEdit(maid, HistoryScope.Pose,
                "ブレンド適用: " + data.name, PoseSnapshot.GetAllBodyBones(maid));
            if (!MaidAnimationBlendController.ApplyMotion(maid, _blendTargetLayer, data))
            {
                DialogPopupWindow.ShowDialog("このモーションはブレンドできません (スクリプト経由か読み込みに失敗)");
            }
        }

        private void LoadMyPoseEntryToTarget(Maid maid, string myPoseDir, string poseName)
        {
            if (_blendTargetLayer == MaidPoseBlendRows.BaseLayer)
            {
                LoadMyPoseEntry(maid, myPoseDir, poseName);
                return;
            }

            AutoEditMode.Enter();
            HistoryManager.instance.BeforeEdit(maid, HistoryScope.Pose,
                "ブレンド適用: " + poseName, PoseSnapshot.GetAllBodyBones(maid));
            if (!MaidAnimationBlendController.ApplyMyPose(maid, _blendTargetLayer,
                Path.Combine(myPoseDir, poseName)))
            {
                DialogPopupWindow.ShowDialog("ポーズの読み込みに失敗しました");
            }
        }
```

- [ ] **Step 6: 両構成をビルドし、テストを通す**

Run: MSBuild 2 回 + `dotnet test`
Expected: 成功 (新規 4 tests 含む)

- [ ] **Step 7: 実機で UI を確認する (COM3D2.5)**

- 「アニメブレンド」を開き、適用先を「レイヤー2」にして一覧のモーションをクリック → 行が現れ混ざって動く
- 重み 0.5 → 半分だけ混ざる。■ で止めても混ざったまま。再生位置スライダーで追従
- 「削除」→ ベースだけに戻る。Ctrl+Z → 層が戻る (履歴ウィンドウに「ブレンド削除」)
- タイムライン読込中: アニメレイヤー未登録なら区間が無効化され「追加」ボタンが出る。登録後に重みを動かすとキーが打たれる
- マイポーズでも同様に載る
- スクリプト経由 (エディットカテゴリ等) をレイヤーへ → ダイアログ
- 適用先を「通常」に戻して一覧クリック → 従来どおり

- [ ] **Step 8: コミット**

`feat(pose): モーションウィンドウにアニメブレンド区間を追加する`

---

### Task 7: ドキュメント

**Files:**
- Modify: `docs-site/guide/maid-editing.md` (「モーションとポーズ」節 186-223 行付近)

- [ ] **Step 1: 節を追記する**

「モーションとポーズ」の操作表の後に:

```markdown
### アニメブレンド

`アニメブレンド` を開くと、ベースモーションの上に別のモーションやマイポーズを
アニメレイヤー (2〜8) へ重ねて再生できます。

| 項目 | 内容 |
|---|---|
| `適用先` | 一覧クリックの載せ先。`通常` は従来どおりベースモーションを差し替え、`レイヤーN` はそのレイヤーへ重ねます |
| `▶` / `■` | そのレイヤーだけを再生 / 停止します (ベースが停止中は操作できません) |
| `削除` | レイヤーからアニメを外します |
| `再生時間` | レイヤーの再生位置。停止中は即座にそのフレームが混ざります |
| `重み` | 混ぜる割合 (0〜1) |
| `速度` / `ループ` | レイヤーの再生速度とループ |

ベースを `■` で止めると、重ねたレイヤーもそのフレームで止まり、ボーン編集の基準になります。
スクリプト経由で再生されるモーション (エディットカテゴリの一部) はレイヤーへ載せられません。
タイムライン読込中は「メイドアニメブレンド」レイヤーの範囲として扱われ、値の変更がキーになります。
Mod モーションを載せた状態はタイムライン XML に絶対パスで保存されるため、別環境では再生できません。
```

- [ ] **Step 2: docs-review スキルでレビューし、指摘を反映する**

- [ ] **Step 3: コミット**

`docs(docs-site): アニメブレンドを反映する`

---

## 自己レビュー結果

- **Spec coverage**: 状態の置き場 (Task 2/3)、anmName 決定 (Task 1)、MaidCache 変更 (Task 2)、MaidMotionState 変更 (Task 4)、履歴 (Task 5)、UI (Task 6)、ドキュメント (Task 7)。スコープ外項目は計画に含めない
- **既知の割り切り**:
  - ゲートの張り直しで、モーションレイヤー未登録かつブレンド区間を開いているときは注意ラベルが 2 回出る。区間を閉じれば 1 回になるため許容する
  - `SyncFromAnimation` で拾い直した層は名前しか復元できない (Mod の絶対パスは失われる)。ベースを差し替えた後の層はウィンドウでの操作は可能だがタイムライン再ロードは効かない。ブレンド中にベースを差し替える運用は稀と判断
  - 前後送り (`<` `>`) は適用先に関わらず常にベースを送る (再生中ベースの一覧を辿る機能のため)
  - `Product.isEnabledNewBodyMan` は 2.0 に無いので `#if COM3D25` で分ける
  - 同じモーション / マイポーズを複数の層へ同時に載せることはできない (Unity legacy `Animation` はクリップ名を全体で一意なキーとして扱うため)。`ReleaseDuplicatedLayers` が先に載っていた層を解放し、警告ログを出す
  - タイムライン側の `AnimationTimelineLayer.OnPoseEditEnd` は `timelineManager.isMotionEditing` のとき層 2〜8 の `state` を破棄する (`anmName` は残る)。ウィンドウで組んだブレンド層がタイムラインのポーズ編集終了に巻き込まれて落ちる。`SyncFromAnimation` が `anmName` から拾い直すが Mod の絶対パスは失われる。Task 4 Step 6 で実機の見え方を確認し、実用に耐えない場合のみ対処する

## レビュー却下メモ

- `SyncFromAnimation` の毎フレーム走査コスト (`foreach (AnimationState state in anim)` が OnGUI 回数ぶん走る) — 却下。層は最大 7、走査はブレンド区間を開いている間だけで、`state` が生きていれば走査自体が起きない。現時点では過剰品質
