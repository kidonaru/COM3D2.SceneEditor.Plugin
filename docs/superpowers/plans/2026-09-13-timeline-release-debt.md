# タイムライン リリース前 負債対応 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** タイムライン XML version 34 を公開する前に、形式・識別子に関わる負債 5 件（メイド衣装のメイド単位化・モデルのグループ番号固定・集約型レイヤーの値ごと補間・死に値の削除・空レイヤーの暗黙生成停止）をコードで解消し、残り 2 件を仕様書に明記する。

**Architecture:** XML 形式（version 34）は一切変えない。変更はすべて「読み書きの形はそのままに、適用側・採番側の振る舞いを直す」もので、既存 XML の読込結果は原則変わらない。集約型レイヤーの補間は、既に保存されている値ごとのタンジェントを適用側が読むようにするだけで、新しい保存項目は増えない。

**Tech Stack:** C#（プラグイン本体は COM3D2 構成 = .NET 3.5 / COM3D25 構成 = .NET 4.7.1 の 2 構成）、xunit（テストは net48、COM3D25 構成のビルド成果物を参照）、MSBuild、VitePress（`docs-site/`）

**Spec:** `docs/superpowers/specs/2026-09-13-timeline-release-debt-design.md`
（元となった負債一覧は `docs/timeline-release-debt-review.md`）

## Global Constraints

- **XML 形式と version は変えない。** `TimelineData.CurrentVersion` は 34 のまま。`TransformData*` の `valueCount` と値の並びも変えない
- **コメントとログメッセージは日本語で書く。** 既存コードの記述密度に合わせる
- **ビルドは 2 構成とも通すこと。** COM3D2 構成は **.NET 3.5**（入力 5 個以上の `Func<>`/`Action<>` 不可、`Tuple` 一部不可）。片方だけ通して「ビルド成功」と報告しない
- **`debug.bat` / `deploy.bat` / `deploy.ps1` は実行しない。** `debug.bat` はゲームフォルダへ DLL をコピーする。ビルドは MSBuild を直接叩く
- **`source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj` は `<Compile Include=...>` の明示列挙。** 本体側にファイルを新規追加したら csproj にも行を足す（本計画は本体側に新規ファイルを作らない設計にしてある）。テストプロジェクトは SDK スタイルなので追加不要
- **git worktree は使わない。** メインの作業ディレクトリで作業する
- ビルド・テストは常に次の 1 本で回す（Git Bash）。COM3D2 構成のビルドは `bin/Debug/COM3D25/` を消すため、必ず COM3D2 → COM3D25 → `dotnet test` の順にする

  ```bash
  cd "W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin"
  export MSYS2_ARG_CONV_EXCL="*"
  MSBUILD="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
  "$MSBUILD" source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2  "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" \
    && "$MSBUILD" source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" \
    && dotnet test source/COM3D2.SceneEditor.Plugin.Tests
  ```

## 設計書からの逸脱（実装前に把握すること）

1. **`PEP.PostEffectDataLerp` は削除しない。** 設計書は「他で使わなくなれば削除する」としているが、`source/COM3D2.SceneEditor.Plugin/MTEUtils/PostEffectData.cs` は `COM3D2.PostEffects.Plugin` 側の同名ファイルと**バイト単位で同一**の共有ファイル（確認済み）。SceneEditor 側だけ削ると共有が崩れる。Task 6 で未使用になるが、ファイルはそのまま残す
2. **「メイド削除時に衣装レイヤーが除去されること」のテストは書かない。** 設計書が根拠に挙げた `TimelineData.cs:677, 691` は `AddMaidShapeKey` / `RemoveMaidShapeKey` であり、**メイド削除時にレイヤーを除去する処理はコード上に存在しない**（`hasSlotNo` の全参照を確認済み）。衣装レイヤーだけの話ではないため、本計画の範囲外とする
3. **仕様書の更新は各項目のタスクに畳み込む。** 設計書の実装順では最後にまとめているが、記述の更新はその項目の成果物の一部なので同じタスクで行う。実装順そのものは設計書どおり

## File Structure

| ファイル | 役割 | 変更 |
|---|---|---|
| `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/DressTimelineLayer.cs` | 衣装レイヤー | `hasSlotNo => true` を追加 |
| `source/COM3D2.SceneEditor.Plugin/TimelineLayerGateHost.cs` | 外部プラグイン向けゲート API | メイド単位レイヤーは操作対象メイドのスロットで引く |
| `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineManager.cs` | タイムライン全体の管理 | `OnMaidSlotNoChanged` が暗黙にレイヤーを作らないようにする |
| `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineData.cs` | タイムラインのランタイムデータ | 読み手のないスカラー 3 値を削除 |
| `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineXml.cs` | タイムラインの XML DTO | 同 3 要素を削除 |
| `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/ITransformData.cs` | キー値のインタフェース | `LerpFrom` を追加 |
| `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataBase.cs` | キー値の実装基底 | `LerpFrom` を実装 |
| `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/TimelineLayerBase.cs` | レイヤー基底 | `CalcTangentValue` を `LerpScratch<T>` へ置き換え |
| `.../TimelineLayer/PostEffectTimelineLayer_{Bloom,DepthOfField,DistanceFog,GTToneMap,Parrifin,Rimlight}.cs` | ポストエフェクト適用 | 一括補間 → `LerpScratch` |
| `.../TimelineLayer/{MaidMaterial,ModelMaterial,BGModelMaterial}TimelineLayer.cs` | マテリアル適用 | `material.Lerp` → `material.Apply(scratch)` |
| `source/COM3D2.SceneEditor.Plugin/Timeline/ModelMaterial.cs` | マテリアル操作 | `Lerp` を削除 |
| `source/COM3D2.SceneEditor.Plugin/Timeline/StudioModelStat.cs` | モデル 1 体の状態 | 未採番定数を追加 |
| `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/ModelHackManager.cs` | モデル一覧の集約 | `FixGroup` を「未採番だけ採番」に変更 |
| `source/COM3D2.SceneEditor.Plugin/Timeline/Hack/ExternalModelHack.cs` | 外部プロバイダ委譲 | 新規 stat を未採番にする |
| `docs-site/timeline/*.md` | 仕様書 | 各項目の記述を訂正 |
| `source/COM3D2.SceneEditor.Plugin.Tests/*.cs` | テスト | 4 ファイル新規 + 1 ファイル修正 |

---

### Task 1: メイド衣装レイヤーをメイド単位にする

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/DressTimelineLayer.cs:12-13`
- Modify: `source/COM3D2.SceneEditor.Plugin/TimelineLayerGateHost.cs:19,35,56`
- Modify: `docs-site/timeline/layers.md:20`
- Modify: `docs-site/timeline/layers-maid.md:104`
- Modify: `docs-site/timeline/compatibility.md:37`
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/MaidLayerSlotNoTests.cs`（新規）

**Interfaces:**
- Consumes: なし（先頭タスク）
- Produces: 「`TimelineLayerCategory.Maid` のレイヤーは必ず `hasSlotNo == true`」という不変条件。Task 1 のテストがこれを固定し、`TimelineLayerGateHost` はこれに依存してスロット番号を決める

- [ ] **Step 1: 失敗するテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/MaidLayerSlotNoTests.cs` を新規作成する。

`TimelineLayerBase` のコンストラクタは `UnityEngine.Debug.Log` を呼ぶためゲーム外では実行できない。`hasSlotNo` は状態を読まない式本体プロパティなので、`FormatterServices.GetUninitializedObject` で未初期化インスタンスを作って読む。

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// メイドカテゴリのレイヤーがメイド単位 (hasSlotNo) であることを固定する。
    /// TimelineLayerGateHost はカテゴリからスロット番号の要否を決めるため、
    /// この対応が崩れると外部プラグインのゲート判定が別メイドを見る
    /// </summary>
    public class MaidLayerSlotNoTests
    {
        // コンストラクタが UnityEngine.Debug.Log を呼ぶためゲーム外では new できない。
        // hasSlotNo は状態を読まないので未初期化インスタンスで判定できる
        private static ITimelineLayer CreateUninitialized(Type type)
        {
            return (ITimelineLayer)FormatterServices.GetUninitializedObject(type);
        }

        private static List<Type> GetConcreteLayerTypes()
        {
            var assembly = typeof(ITimelineLayer).Assembly;
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                types = e.Types.Where(t => t != null).ToArray();
            }
            return types
                .Where(t => !t.IsAbstract && typeof(ITimelineLayer).IsAssignableFrom(t))
                .ToList();
        }

        [Fact]
        public void メイドカテゴリのレイヤーはすべてメイド単位である()
        {
            var types = GetConcreteLayerTypes();
            Assert.NotEmpty(types);

            var maidTypes = types
                .Where(t => t.GetCustomAttribute<TimelineLayerDescAttribute>()?.Category
                            == TimelineLayerCategory.Maid)
                .ToList();
            Assert.NotEmpty(maidTypes);

            foreach (var type in maidTypes)
            {
                Assert.True(CreateUninitialized(type).hasSlotNo,
                    type.Name + " はメイドカテゴリなのに hasSlotNo が false");
            }
        }

        [Fact]
        public void メイド衣装レイヤーはメイド単位である()
        {
            Assert.True(CreateUninitialized(typeof(DressTimelineLayer)).hasSlotNo);
        }
    }
}
```

- [ ] **Step 2: テストを実行して失敗を確認する**

Run: 冒頭の「ビルド・テスト」1 本を実行し、`dotnet test` の出力を見る
Expected: `メイドカテゴリのレイヤーはすべてメイド単位である` と `メイド衣装レイヤーはメイド単位である` が **FAIL**（`DressTimelineLayer は ... hasSlotNo が false`）

- [ ] **Step 3: DressTimelineLayer をメイド単位にする**

`DressTimelineLayer.cs` の `layerName` の直後に追加する。

```csharp
        public override string layerName => nameof(DressTimelineLayer);

        // MTE 原本は hasSlotNo 未指定 (false) でメイド 0 固定だったため、SE ではメイド単位へ変更している
        public override bool hasSlotNo => true;
```

- [ ] **Step 4: テストを実行して通ることを確認する**

Run: 冒頭の「ビルド・テスト」1 本
Expected: 2 構成ともビルド成功、全テスト PASS

- [ ] **Step 5: ゲート API のスロット番号を操作対象メイドに合わせる**

`TimelineLayerGateHost.cs` は `GetState` / `AddLayer` がリテラル `0` を渡しており、メイド単位レイヤーだと常にメイド 0 を見てしまう。`TimelineLayerInfo` は `hasSlotNo` を持たないが `category` を持つので、Task 1 で固定した「メイドカテゴリ ⇔ メイド単位」の対応を使う。

**スコープに注意:** この変更は衣装レイヤーだけでなく、**メイドカテゴリの 11 レイヤーすべて**（`MotionTimelineLayer` / `AnimationTimelineLayer` / `MorphTimelineLayer` / `MoveTimelineLayer` / `GravityTimelineLayer` / `EyesTimelineLayer` / `ShapeKeyTimelineLayer` / `VoiceTimelineLayer` / `DressTimelineLayer` / `UndressTimelineLayer` / `MaidMaterialTimelineLayer`）の外部ゲート判定を「常にスロット 0」から「操作対象メイドのスロット」に変える。これは設計書が求める修正の必然的な範囲であり、既存の潜在バグ（選択中メイドと無関係にスロット 0 を見る）を直す方向。

現時点の外部呼び出し元は `COM3D2.PostEffects.Plugin` の `TimelineLayerGateDrawer` のみで、渡すレイヤー名は `PostEffectTimelineLayer`（カテゴリ `Effect` = メイド非依存）だけであることをワークスペース全体の grep で確認済み。つまり**既存の外部利用者には影響しない**。ただしクラスコメントは公開契約なので必ず更新する（Step 5 で対応）。

クラスコメント（19 行目）を差し替える。

```csharp
    /// - メイド単位レイヤー (メイドカテゴリ) は操作対象メイドのスロットで判定する。
    ///   カテゴリとメイド単位の対応は MaidLayerSlotNoTests で固定している
```

`GetState` の 35 行目を差し替える。

```csharp
            var timelineLoaded = timelineManager.timeline != null;
            var layerExists = timelineLoaded
                && timelineManager.GetLayer(info.layerType, GetSlotNo(info)) != null;
```

`AddLayer` の 56 行目を差し替える。

```csharp
            timelineManager.ChangeActiveLayer(info.layerType, GetSlotNo(info));
```

`GetDisplayName` の手前にヘルパーを足す。

```csharp
        /// <summary>
        /// メイド単位レイヤーは操作対象メイドのスロットで引く。
        /// メイド非依存レイヤーは GetLayer がスロットを見ないので 0 のままでよい
        /// </summary>
        private static int GetSlotNo(MTEP.TimelineLayerInfo info)
        {
            return info.category == MTEP.TimelineLayerCategory.Maid
                ? MTEP.MaidManager.instance.maidSlotNo
                : 0;
        }
```

- [ ] **Step 6: ビルドとテストを実行する**

Run: 冒頭の「ビルド・テスト」1 本
Expected: 2 構成ともビルド成功、全テスト PASS

- [ ] **Step 7: 実機でゲートの挙動を確認する（衣装 + 他のメイドレイヤー 1 種）**

ゲームが起動していること。起動していなければユーザーに起動を依頼し、この Step を保留する。Task 2 Step 4 と同じ手順で DLL を差し替えて再起動する。

メイドを 2 人出してタイムラインを新規作成し、次を確認する。

1. `操作対象` をメイド 2 にして衣装を着替える → メイド 2 の衣装レイヤーにキーが打たれる（メイド 1 のレイヤーではない）
2. 脱衣ウィンドウ（`UndressTimelineLayer`。カテゴリ `Maid` の代表として）をメイド 2 で開き、レイヤーゲートの表示と追加ボタンがメイド 2 のレイヤーを対象にしている
3. PostEffects.Plugin のウィンドウでポストエフェクトのレイヤーゲートが従来どおり動く（`GetSlotNo` がメイド非依存レイヤーで 0 を返すこと）

```csharp
var tm = COM3D2.MotionTimelineEditor.Plugin.TimelineManager.instance;
string.Join(", ", tm.timeline.layers.ConvertAll(
    l => l.layerName + "@" + (l.hasSlotNo ? l.slotNo : 0)).ToArray())
```

Expected: `DressTimelineLayer@1` が存在し、`DressTimelineLayer@0` と混ざらない

- [ ] **Step 8: 仕様書から「メイド単位ではない」を消す**

`docs-site/timeline/layers.md:20` の行を差し替える。

```markdown
| `メイド衣装` | ○ | 装備アイテムの差し替え | Inspector |
```

`docs-site/timeline/layers-maid.md:104` の行（`- **メイド単位ではありません。** タイムラインに 1 本だけで、先頭スロットのメイドにしか効きません`）を削除する。

`docs-site/timeline/compatibility.md:37` の行（`- \`メイド衣装\` はメイド単位ではなく、タイムラインに 1 本だけです`）を削除する。

- [ ] **Step 9: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/DressTimelineLayer.cs \
        source/COM3D2.SceneEditor.Plugin/TimelineLayerGateHost.cs \
        source/COM3D2.SceneEditor.Plugin.Tests/MaidLayerSlotNoTests.cs \
        docs-site/timeline/layers.md docs-site/timeline/layers-maid.md docs-site/timeline/compatibility.md
git commit -m "$(cat <<'EOF'
feat(timeline): メイド衣装レイヤーをメイド単位にする

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_018my9CHjXRQZWrUnfwDTRzV
EOF
)"
```

---

### Task 2: メイド切替時の暗黙レイヤー作成をやめる

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineManager.cs:2286-2292`
- Modify: `docs-site/timeline/compatibility.md:15`
- Modify: `docs-site/timeline/editing.md:110`
- Modify: `docs-site/timeline/control.md:66`

**Interfaces:**
- Consumes: `TimelineManager.GetLayer(Type layerType, int slotNo)` / `SetCurrentLayer(ITimelineLayer layer)`（既存、`TimelineManager.cs:1623` / `1722`）
- Produces: `private ITimelineLayer FindActiveLayerForSlot(int maidSlotNo)` — 切替先メイドで編集対象にするレイヤーを返す。見つからなければ `null`

このタスクはゲームのランタイム状態（`MaidManager` のイベント、レイヤーインスタンス）に依存するため単体テストを持てない。検証は稼働中のゲームに対する MCP `com3d25-devbridge` の `eval_csharp` で行う。

- [ ] **Step 1: 現状の挙動を実機で記録する（変更前）**

ゲームが起動していること。起動していなければユーザーに起動を依頼し、このタスクを保留する（コード変更を先に入れない）。

MCP `com3d25-devbridge` の `eval_csharp` で、タイムラインを読み込んだ状態から次を評価する。

```csharp
var tm = COM3D2.MotionTimelineEditor.Plugin.TimelineManager.instance;
var before = tm.timeline.layers.Count;
COM3D2.MotionTimelineEditor.Plugin.MaidManager.instance.maidSlotNo + " / layers=" + before
```

続けて `操作対象` コンボで 2 人目のメイドへ切り替え、もう一度 `tm.timeline.layers.Count` を読む。
Expected（変更前）: レイヤー数が増える（暗黙作成が起きている）

- [ ] **Step 2: OnMaidSlotNoChanged を差し替える**

`TimelineManager.cs:2286-2292` の `OnMaidSlotNoChanged` を次で置き換える。

```csharp
        /// <summary>
        /// 操作対象メイドの切替。レイヤーは作らず、既にあるレイヤーからアクティブを選び直す。
        /// (切替だけでレイヤーが増えると、レイヤーが無いときだけ出るレイヤーゲートと噛み合わなくなる)
        /// </summary>
        private void OnMaidSlotNoChanged(int maidSlotNo)
        {
            if (!IsValidData())
            {
                return;
            }

            var layer = FindActiveLayerForSlot(maidSlotNo);
            if (layer != null)
            {
                SetCurrentLayer(layer);
            }
        }

        /// <summary>
        /// 切替先メイドで編集対象にするレイヤー。
        /// 同種 → 切替先メイドのメイドアニメ → メイド非依存の先頭 の順に探し、
        /// どれも無ければ null (アクティブレイヤーを変えない) を返す
        /// </summary>
        private ITimelineLayer FindActiveLayerForSlot(int maidSlotNo)
        {
            var current = currentLayer;
            if (current != null)
            {
                // メイド非依存レイヤーなら GetLayer が自分自身を返し、切替は起きない
                var sameType = GetLayer(current.layerType, maidSlotNo);
                if (sameType != null)
                {
                    return sameType;
                }
            }

            var motionLayer = GetLayer(typeof(MotionTimelineLayer), maidSlotNo);
            if (motionLayer != null)
            {
                return motionLayer;
            }

            foreach (var layer in layers)
            {
                if (!layer.hasSlotNo)
                {
                    return layer;
                }
            }

            return null;
        }
```

- [ ] **Step 3: ビルドとテストを実行する**

Run: 冒頭の「ビルド・テスト」1 本
Expected: 2 構成ともビルド成功、全テスト PASS（既存テストの退行がないこと）

- [ ] **Step 4: 実機で挙動を確認する**

`debug.bat` は使わない。ゲーム起動中なら CLAUDE.md の「Harmony プラグインのホットリロード」手順は使えない（本プラグインは Harmony パッチではない）ため、**ユーザーにゲームを一度落としてもらい**、次で DLL を差し替えてから再起動する。

```bash
cp source/COM3D2.SceneEditor.Plugin/bin/Debug/COM3D25/COM3D2.SceneEditor.Plugin.dll \
   "W:/COM3D2_5/Sybaris/UnityInjector/COM3D2.SceneEditor.Plugin.dll"
```

再起動後、`eval_csharp` で Step 1 と同じ手順を踏む。
Expected:
- 2 人目のメイドへ切り替えても `tm.timeline.layers.Count` が変わらない
- 切替先に同種レイヤーがあればそれがアクティブになる（`tm.currentLayer.layerName` と `tm.currentLayer.slotNo` で確認）
- 履歴が増えない（`操作対象` を切り替えただけで Undo できる操作が積まれない）

- [ ] **Step 5: 仕様書を訂正する**

`docs-site/timeline/compatibility.md:15` の行（`- 未使用のレイヤーは保存時に空のレイヤーとして実体化されます`）を削除する。

`docs-site/timeline/editing.md:110` の行を差し替える。

```markdown
- タイムライン作成後にメイドを追加しても、レイヤーは自動では作られません。`+` コンボか、各ウィンドウのレイヤーゲートの追加ボタンで追加します
```

`docs-site/timeline/control.md:66` の段落を差し替える。

```markdown
`操作対象` のコンボでアクティブメイドを切り替えます。メイド単位のレイヤーは、ここで選んだメイドのものが編集対象になります。切り替えてもレイヤーは作られません（切替先に同種のレイヤーが無ければ、そのメイドの `メイドアニメ` レイヤーへ寄せます）。
```

- [ ] **Step 6: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineManager.cs \
        docs-site/timeline/compatibility.md docs-site/timeline/editing.md docs-site/timeline/control.md
git commit -m "$(cat <<'EOF'
fix(timeline): メイド切替で空レイヤーを作らないようにする

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_018my9CHjXRQZWrUnfwDTRzV
EOF
)"
```

---

### Task 3: 読み手のないスカラー値を保存対象から外す

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineData.cs:369-371, 629-631, 794-796, 945-947`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineXml.cs:222-229`
- Modify: `source/COM3D2.SceneEditor.Plugin.Tests/XmlRoundTripTests.cs:51-87`（`RemovedElementNames`）
- Modify: `docs-site/timeline/compatibility.md:25`
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/TimelineXmlRemovedFieldsTests.cs`（新規）

**Interfaces:**
- Consumes: なし
- Produces: `TimelineXml` / `TimelineData` から `endOffsetTime` / `startFadeTime` / `endFadeTime` が消える。`startOffsetTime` は BGM・動画のシークと DCM の CSV 出力で使用中なので**残す**

- [ ] **Step 1: 失敗するテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/TimelineXmlRemovedFieldsTests.cs` を新規作成する。

```csharp
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Xml.Serialization;
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// 読み手の無いスカラー値 (終了オフセット時間・フェード時間) を保存対象から外したことを固定する。
    /// 旧 XML に要素が残っていても XmlSerializer は未知要素を捨てるので、読込互換の残置は不要
    /// </summary>
    public class TimelineXmlRemovedFieldsTests
    {
        private static readonly string[] RemovedElements =
            { "EndOffsetTime", "StartFadeTime", "EndFadeTime" };

        private static string Serialize(TimelineXml xml)
        {
            var serializer = new XmlSerializer(typeof(TimelineXml));
            using (var sw = new StringWriter())
            {
                serializer.Serialize(sw, xml);
                return sw.ToString();
            }
        }

        [Fact]
        public void 廃止したスカラー値は出力されない()
        {
            var saved = XDocument.Parse(Serialize(new TimelineXml()));
            var names = saved.Descendants().Select(e => e.Name.LocalName).ToList();

            foreach (var removed in RemovedElements)
            {
                Assert.DoesNotContain(removed, names);
            }

            // 開始オフセット時間は BGM・動画のシークで使うので残す
            Assert.Contains("StartOffsetTime", names);
        }

        [Fact]
        public void 廃止したスカラー値を含む旧XMLも読み込める()
        {
            var source =
                "<TimelineXml><StartOffsetTime>0.5</StartOffsetTime>" +
                "<EndOffsetTime>0.5</EndOffsetTime>" +
                "<StartFadeTime>0.1</StartFadeTime>" +
                "<EndFadeTime>0</EndFadeTime></TimelineXml>";

            var serializer = new XmlSerializer(typeof(TimelineXml));
            using (var sr = new StringReader(source))
            {
                var xml = (TimelineXml)serializer.Deserialize(sr);
                Assert.Equal(0.5f, xml.startOffsetTime, 4);
            }
        }
    }
}
```

- [ ] **Step 2: テストを実行して失敗を確認する**

Run: 冒頭の「ビルド・テスト」1 本
Expected: `廃止したスカラー値は出力されない` が **FAIL**（`EndOffsetTime` が出力に含まれる）

- [ ] **Step 3: TimelineXml と TimelineData から 3 値を削除する**

`TimelineXml.cs` の 222〜229 行、`EndOffsetTime` / `StartFadeTime` / `EndFadeTime` の 3 ブロックを削除する（`StartOffsetTime` は残す）。

```csharp
        [XmlElement("StartOffsetTime")]
        public float startOffsetTime = 0.5f;

        [XmlElement("SingleFrameType")]
        public SingleFrameType singleFrameType = SingleFrameType.Delay;
```

`TimelineData.cs` の 4 か所から該当行を削除する。

- 369〜371 行: `endOffsetTime` / `startFadeTime` / `endFadeTime` のフィールド宣言（`startOffsetTime` は残す）
- 629〜631 行: `ResetSettings` 系の `DefaultTimeline.*` 代入
- 794〜796 行: `FromXml` の `xml.*` 代入
- 945〜947 行: `ToXml` の `xml.* =` 代入

- [ ] **Step 4: ゴールデンテストの除外リストへ追加する**

`XmlRoundTripTests.cs` の `RemovedElementNames`（51 行目〜）へ追加する。フィクスチャ `mte-v25-motion.xml` / `mte-v9-motion.legacy.xml` は 3 要素を持つため、追加しないと `フィクスチャXMLの情報が保存される` が落ちる。

```csharp
                // 読み手が無いので保存対象から外した (開始オフセット時間だけは BGM・動画のシークで使う)
                "EndOffsetTime",
                "StartFadeTime",
                "EndFadeTime",
```

- [ ] **Step 5: テストを実行して通ることを確認する**

Run: 冒頭の「ビルド・テスト」1 本
Expected: 2 構成ともビルド成功、全テスト PASS

- [ ] **Step 6: 仕様書を訂正する**

`docs-site/timeline/compatibility.md:25` の行（`- 開始 / 終了のオフセット時間とフェード時間は使われません`）を差し替える。

```markdown
- 終了オフセット時間とフェード時間は廃止しました。旧 XML に残っていても読み飛ばされ、保存もされません。開始オフセット時間は BGM・動画のシーク基準として使われます（編集 UI はありません）
```

- [ ] **Step 7: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/TimelineData.cs \
        source/COM3D2.SceneEditor.Plugin/Timeline/TimelineXml.cs \
        source/COM3D2.SceneEditor.Plugin.Tests/XmlRoundTripTests.cs \
        source/COM3D2.SceneEditor.Plugin.Tests/TimelineXmlRemovedFieldsTests.cs \
        docs-site/timeline/compatibility.md
git commit -m "$(cat <<'EOF'
refactor(timeline): 読み手の無い終了オフセット・フェード時間を保存対象から外す

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_018my9CHjXRQZWrUnfwDTRzV
EOF
)"
```

---

### Task 4: TransformData に値ごとの補間 `LerpFrom` を足す

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/ITransformData.cs:228`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataBase.cs`（`FromTransformData` の直後）
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/TransformLerpFromTests.cs`（新規）

**Interfaces:**
- Consumes: `PluginUtils.HermiteValue(float t0, float t1, ValueData start, ValueData end, float t)`（`Timeline/PluginUtils.cs:198`）、`CustomValueInfo.type`（`Timeline/TransformData/ITransformData.cs:79`）、`GetColorValueInfoMap()`、`tangentValues`
- Produces: `void LerpFrom(ITransformData start, ITransformData end, float t0, float t1, float t)` — `start`〜`end` を時刻 `t`（秒。`t0`〜`t1` の範囲）で補間した値を**自身の `values` / `strValues` へ書き込む**。Task 5 の `TimelineLayerBase.LerpScratch<T>` がこれを呼ぶ

値ごとの扱い（設計書の表どおり）:

| 値の種類 | 補間 |
|---|---|
| 色（`GetColorValueInfoMap` の RGBA 添字） | `Mathf.Lerp`（線形。現状と同じ） |
| `CustomValueInfo.type` が `FloatValue` / `FloatSlider` かつ `tangentValues` に含まれる | `PluginUtils.HermiteValue`（その値自身の out / in タンジェント） |
| 上記以外（`BoolValue` / `IntValue`、`easingValue`、`visibleValue` など） | 区間開始値をコピー |
| `strValues` | 区間開始値をコピー |

- [ ] **Step 1: 失敗するテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/TransformLerpFromTests.cs` を新規作成する。`TransformDataBloom` は色・Float・Int・Bool・`easingValue` をすべて持つので代表として使う。

```csharp
using COM3D2.MotionTimelineEditor.Plugin;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// 集約型レイヤー (ポストエフェクト・マテリアル) の再生に使う値ごとの補間を固定する。
    /// 色は線形、数値はその値自身のタンジェント、Bool / Int と文字列は区間開始値
    /// </summary>
    public class TransformLerpFromTests
    {
        private static TransformDataBloom CreateBloom()
        {
            var trans = new TransformDataBloom();
            trans.Initialize("Bloom");
            return trans;
        }

        private static TransformDataBloom Lerp(
            TransformDataBloom start, TransformDataBloom end, float t)
        {
            var scratch = (TransformDataBloom)start.Clone();
            scratch.LerpFrom(start, end, 0f, 1f, t);
            return scratch;
        }

        [Fact]
        public void 色は線形補間される()
        {
            var start = CreateBloom();
            var end = CreateBloom();
            start.color = new Color(0f, 0f, 0f, 0f);
            end.color = new Color(1f, 1f, 1f, 1f);

            var mid = Lerp(start, end, 0.25f);

            Assert.Equal(0.25f, mid.color.r, 4);
            Assert.Equal(0.25f, mid.color.a, 4);
        }

        [Fact]
        public void 数値はその値自身のタンジェントで補間される()
        {
            var start = CreateBloom();
            var end = CreateBloom();
            start.intensity = 0f;
            end.intensity = 1f;
            start.threshold = 0f;
            end.threshold = 1f;

            // intensity だけ両端のタンジェントを 0 にし、threshold には傾きを与える。
            // TangentData.value は setter が無いので normalizedValue + UpdateValue で入れる
            SetTangent(start.intensityValue.outTangent, 0f);
            SetTangent(end.intensityValue.inTangent, 0f);
            SetTangent(start.thresholdValue.outTangent, 3f);
            SetTangent(end.thresholdValue.inTangent, 3f);

            // t=0.5 は両端のタンジェントが対称だと曲線の形に関わらず中点になるので、
            // 差が出る 0.25 で見る
            var mid = Lerp(start, end, 0.25f);

            // 囲みの名前空間 COM3D2.SceneEditor.Plugin にも PluginUtils があるので MTEP 別名で呼ぶ
            var expectedIntensity = MTEP.PluginUtils.HermiteValue(
                0f, 1f, start.intensityValue, end.intensityValue, 0.25f);
            var expectedThreshold = MTEP.PluginUtils.HermiteValue(
                0f, 1f, start.thresholdValue, end.thresholdValue, 0.25f);

            Assert.Equal(expectedIntensity, mid.intensity, 4);
            Assert.Equal(expectedThreshold, mid.threshold, 4);
            // タンジェントが違えば中間値も違う (一括補間なら一致してしまう)
            Assert.NotEqual(mid.intensity, mid.threshold, 4);
            // 値が Hold に誤分類されると両方 0 のままになる (lerpKinds の添字照合バグの検出)
            Assert.NotEqual(0f, mid.intensity, 4);
        }

        [Fact]
        public void Bool値とInt値は区間開始値になる()
        {
            var start = CreateBloom();
            var end = CreateBloom();
            start.highQuality = false;
            end.highQuality = true;
            start.blurIterations = 1;
            end.blurIterations = 9;
            start.visible = false;
            end.visible = true;

            var mid = Lerp(start, end, 0.9f);

            Assert.False(mid.highQuality);
            Assert.Equal(1, mid.blurIterations);
            Assert.False(mid.visible);
        }

        [Fact]
        public void 文字列値は区間開始値になる()
        {
            var start = new TransformDataModelMaterial();
            start.Initialize("mat");
            var end = new TransformDataModelMaterial();
            end.Initialize("mat");

            var scratch = (TransformDataModelMaterial)start.Clone();
            scratch.LerpFrom(start, end, 0f, 1f, 0.9f);

            for (var i = 0; i < start.strValues.Length; i++)
            {
                Assert.Equal(start.strValues[i], scratch.strValues[i]);
            }
        }
    }
}
```

- [ ] **Step 2: テストを実行して失敗を確認する**

Run: 冒頭の「ビルド・テスト」1 本
Expected: **ビルド失敗**（`ITransformData` に `LerpFrom` が無い: CS1061）

- [ ] **Step 3: インタフェースへ宣言を足す**

`ITransformData.cs:228` の `FromTransformData` の下に足す。

```csharp
        void FromTransformData(ITransformData transform);

        void LerpFrom(
            ITransformData start,
            ITransformData end,
            float t0,
            float t1,
            float t);
```

- [ ] **Step 4: TransformDataBase に実装する**

`TransformDataBase.cs` の `FromTransformData`（576 行目付近）の直後に足す。

補間の種別は型ごとに不変なので、値添字ごとの判定表を 1 度だけ作ってキャッシュする（毎フレーム全レイヤーぶん呼ばれるため）。

```csharp
        /// <summary>値 1 個の補間種別。型ごとに不変なので初回に作って使い回す</summary>
        private enum LerpKind
        {
            /// <summary>区間開始値をコピー (Bool / Int / easing / 表示フラグなど)</summary>
            Hold,
            /// <summary>線形補間 (色成分)</summary>
            Linear,
            /// <summary>その値自身の out / in タンジェントでエルミート補間</summary>
            Tangent,
        }

        private LerpKind[] _lerpKinds = null;

        private LerpKind[] lerpKinds
        {
            get
            {
                if (_lerpKinds != null)
                {
                    return _lerpKinds;
                }

                var kinds = new LerpKind[values.Length];

                foreach (var info in GetColorValueInfoMap().Values)
                {
                    kinds[info.indexR] = LerpKind.Linear;
                    kinds[info.indexG] = LerpKind.Linear;
                    kinds[info.indexB] = LerpKind.Linear;
                    if (info.hasAlpha)
                    {
                        kinds[info.indexA] = LerpKind.Linear;
                    }
                }

                // タンジェント補間の対象は「タンジェントを保存している値」かつ「連続値」だけ。
                // Bool / Int は中間値に意味が無いので区間開始値のまま保つ。
                // ValueData は Equals を数値本体だけで上書きしているため Array.IndexOf は使えない
                // (既定値 0 同士が一致して常に添字 0 を返す)。必ず参照で突き合わせること
                var tangentIndices = new HashSet<int>();
                var tangents = tangentValues;
                for (var i = 0; i < values.Length; i++)
                {
                    foreach (var tangentValue in tangents)
                    {
                        if (ReferenceEquals(values[i], tangentValue))
                        {
                            tangentIndices.Add(i);
                            break;
                        }
                    }
                }

                foreach (var info in GetCustomValueInfoMap().Values)
                {
                    if (info.index < 0 || info.index >= kinds.Length)
                    {
                        continue;
                    }
                    if (kinds[info.index] != LerpKind.Hold || !tangentIndices.Contains(info.index))
                    {
                        continue;
                    }
                    if (info.type == CustomValueType.FloatValue || info.type == CustomValueType.FloatSlider)
                    {
                        kinds[info.index] = LerpKind.Tangent;
                    }
                }

                _lerpKinds = kinds;
                return _lerpKinds;
            }
        }

        /// <summary>
        /// start〜end の区間を時刻 t (秒。t0〜t1 の範囲) で補間した値を自身へ書き込む。
        /// 集約型レイヤー (ポストエフェクト・マテリアル) の再生用で、毎フレーム同じ
        /// インスタンスへ書き込む前提。色は線形、数値はその値自身のタンジェント、
        /// Bool / Int と文字列は区間開始値になる
        /// </summary>
        public void LerpFrom(
            ITransformData start,
            ITransformData end,
            float t0,
            float t1,
            float t)
        {
            var startValues = start.values;
            var endValues = end.values;
            var kinds = lerpKinds;

            var count = Mathf.Min(values.Length, Mathf.Min(startValues.Length, endValues.Length));
            for (var i = 0; i < count; i++)
            {
                switch (kinds[i])
                {
                    case LerpKind.Linear:
                        values[i].value = Mathf.Lerp(startValues[i].value, endValues[i].value, t);
                        break;
                    case LerpKind.Tangent:
                        values[i].value = PluginUtils.HermiteValue(
                            t0, t1, startValues[i], endValues[i], t);
                        break;
                    default:
                        values[i]._value = startValues[i]._value;
                        break;
                }
            }

            var strCount = Mathf.Min(strValues.Length, start.strValues.Length);
            for (var i = 0; i < strCount; i++)
            {
                strValues[i] = start.strValues[i];
            }
        }
```

`_lerpKinds` は `_values` の差し替えで無効になるので、`ClearValueDataListCache()`（37 行目付近）へ 1 行足す。

```csharp
        private void ClearValueDataListCache()
        {
            _valueDataListCache = null;
            _inTangentListCache = null;
            _outTangentListCache = null;
            _valuesWithoutColors = null;
            _lerpKinds = null;
        }
```

- [ ] **Step 5: テストを実行して通ることを確認する**

Run: 冒頭の「ビルド・テスト」1 本
Expected: 2 構成ともビルド成功、`TransformLerpFromTests` の 4 件を含め全テスト PASS

- [ ] **Step 6: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/ITransformData.cs \
        source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataBase.cs \
        source/COM3D2.SceneEditor.Plugin.Tests/TransformLerpFromTests.cs
git commit -m "$(cat <<'EOF'
feat(timeline): TransformData に値ごとの補間 LerpFrom を追加する

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_018my9CHjXRQZWrUnfwDTRzV
EOF
)"
```

---

### Task 5: 集約型レイヤーを値ごとのタンジェントで補間する

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/TimelineLayerBase.cs:466-482`
- Modify: `.../TimelineLayer/PostEffectTimelineLayer_Bloom.cs:7-17`
- Modify: `.../TimelineLayer/PostEffectTimelineLayer_DepthOfField.cs:10-20`
- Modify: `.../TimelineLayer/PostEffectTimelineLayer_DistanceFog.cs:11-22`
- Modify: `.../TimelineLayer/PostEffectTimelineLayer_GTToneMap.cs:104-114`
- Modify: `.../TimelineLayer/PostEffectTimelineLayer_Parrifin.cs:11-22`
- Modify: `.../TimelineLayer/PostEffectTimelineLayer_Rimlight.cs:11-22`
- Modify: `.../TimelineLayer/MaidMaterialTimelineLayer.cs:107-126`
- Modify: `.../TimelineLayer/ModelMaterialTimelineLayer.cs:115-134`
- Modify: `.../TimelineLayer/BGModelMaterialTimelineLayer.cs:88-106`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/ModelMaterial.cs:244-372`（`Lerp` を削除）
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataBloom.cs:77-78`（コメント）
- Modify: `docs-site/timeline/interpolation.md:37`
- Modify: `docs-site/timeline/compatibility.md:42`
- Modify: `docs-site/timeline/layers-effect.md:85`

**Interfaces:**
- Consumes: `ITransformData.LerpFrom(...)`（Task 4）、`TimelineData.frameDuration`（`TimelineData.cs:277`）、`MotionData.stFrame` / `edFrame`
- Produces: `protected T LerpScratch<T>(MotionData motion, float t) where T : class, ITransformData` — 区間を値ごとに補間した TransformData を返す。戻り値は**型ごとの使い回しインスタンス**なので、呼び出し側はその場で読み切ること

- [ ] **Step 1: 共通のスクラッチ取得を TimelineLayerBase に足す**

`TimelineLayerBase.cs` の `CalcTangentValue`（466〜482 行）を次で置き換える。`CalcTangentValue` はこの後どこからも呼ばれなくなるので削除する。

```csharp
        // 集約型レイヤー (ポストエフェクト・マテリアル) の補間結果を受ける作業用 TransformData。
        // 毎フレーム作ると GC 圧になるので TransformType ごとに 1 つ使い回す
        private readonly Dictionary<TransformType, ITransformData> _lerpScratchMap
            = new Dictionary<TransformType, ITransformData>();

        /// <summary>
        /// 区間 motion を時刻 t (0〜1) で値ごとに補間した TransformData を返す。
        /// 戻り値は型ごとの使い回しインスタンスなので、呼び出し側はその場で読み切ること。
        /// 構造体を一度に適用する集約型レイヤー用で、構造体ゲッター (scratch.bloom 等) と併せて使う
        /// </summary>
        protected T LerpScratch<T>(MotionData motion, float t) where T : class, ITransformData
        {
            var start = motion.start;
            var end = motion.end;

            ITransformData scratch;
            if (!_lerpScratchMap.TryGetValue(start.type, out scratch))
            {
                scratch = start.Clone();
                _lerpScratchMap[start.type] = scratch;
            }

            var t0 = motion.stFrame * timeline.frameDuration;
            var t1 = motion.edFrame * timeline.frameDuration;
            scratch.LerpFrom(start, end, t0, t1, t);

            return scratch as T;
        }
```

- [ ] **Step 2: ポストエフェクト 6 か所を差し替える**

`PostEffectTimelineLayer_Bloom.cs` の `ApplyBloom` を置き換える。

```csharp
        private void ApplyBloom(MotionData motion, float t)
        {
            var scratch = LerpScratch<TransformDataBloom>(motion, t);
            postEffectManager.ApplyBloom(scratch.bloom);
        }
```

`PostEffectTimelineLayer_DepthOfField.cs` の `ApplyDepthOfField` を置き換える。

```csharp
        private void ApplyDepthOfField(MotionData motion, float t)
        {
            var scratch = LerpScratch<TransformDataDepthOfField>(motion, t);
            postEffectManager.ApplyDepthOfField(scratch.depthOfField);
        }
```

`PostEffectTimelineLayer_DistanceFog.cs` の `ApplyDistanceFog` を置き換える。`index` は区間開始キーのものを使う（スクラッチの `index` は使い回しで別インスタンスのものが入っている）。

```csharp
        private void ApplyDistanceFog(MotionData motion, float t)
        {
            var start = motion.start as TransformDataDistanceFog;
            var scratch = LerpScratch<TransformDataDistanceFog>(motion, t);
            postEffectManager.ApplyDistanceFog(start.index, scratch.distanceFog);
        }
```

`PostEffectTimelineLayer_Parrifin.cs` の `ApplyParaffin` を置き換える。

```csharp
        private void ApplyParaffin(MotionData motion, float t)
        {
            var start = motion.start as TransformDataParaffin;
            var scratch = LerpScratch<TransformDataParaffin>(motion, t);
            postEffectManager.ApplyParaffin(start.index, scratch.paraffin);
        }
```

`PostEffectTimelineLayer_Rimlight.cs` の `ApplyRimlight` を置き換える。

```csharp
        private void ApplyRimlight(MotionData motion, float t)
        {
            var start = motion.start as TransformDataRimlight;
            var scratch = LerpScratch<TransformDataRimlight>(motion, t);
            postEffectManager.ApplyRimlight(start.index, scratch.rimlight);
        }
```

`PostEffectTimelineLayer_GTToneMap.cs` の `ApplyGTToneMap` を置き換える。

```csharp
        private void ApplyGTToneMap(MotionData motion, float t)
        {
            var scratch = LerpScratch<TransformDataGTToneMap>(motion, t);
            postEffectManager.ApplyGTToneMap(scratch.data);
        }
```

- [ ] **Step 3: マテリアル 3 レイヤーを差し替える**

`MaidMaterialTimelineLayer.cs` / `ModelMaterialTimelineLayer.cs` / `BGModelMaterialTimelineLayer.cs` の `ApplyMotion` 末尾（`var start = motion.start as TransformDataModelMaterial;` 以降）を、3 ファイルとも次で置き換える。

`indexUpdated` 時の `material.Apply(start)` は、直後の `material.Apply(scratch)` が同じプロパティ集合を上書きするため削除する。

```csharp
            // 数値はその値自身のタンジェント、色は線形で補間した結果を一度に適用する
            var scratch = LerpScratch<TransformDataModelMaterial>(motion, t);
            material.Apply(scratch);
```

`ApplyMotion` の引数 `indexUpdated` は使わなくなるが、`override` なのでシグネチャは変えない。

`ModelMaterial.cs:244-372` の `public void Lerp(...)` メソッド全体を削除する。

- [ ] **Step 4: 補間形状キャリアのコメントを直す**

`CalcTangentValue` を消したことで古くなるコメントを洗い出す。

Run: `rg "補間形状キャリア|CalcTangentValue" source/COM3D2.SceneEditor.Plugin`

該当は次の 2 系統。

**(a) 「集約型レイヤーの補間形状キャリアとして残す」** — `TransformData{Bloom,DepthOfField,DistanceFog,Eyes,GTToneMap,ModelMaterial,Paraffin,Rimlight,ShapeKey,Text,Animation}.cs` の計 11 か所。いずれも `// Tangent 統一により easing 補間は廃止 (easingValue は` の続き行なので、2 行を次の 1 行へ差し替える。

```csharp
        // Tangent 統一により easing 補間は廃止 (easingValue は XML 互換のためだけに残す)
```

**(b) `CalcTangentValue` を名指しするコメント** — `TransformDataBase.cs:195` と `:201`、および `:555`。`hasEasingChannel` 自体は `TangentUnification.cs:132` と `TransformDataBase.cs:1187` がまだ使うので**プロパティは消さない**。コメントだけ直す。

```csharp
        /// <summary>easing 値のスロットを values 内に持つ型か (旧 easing 型の判定に使う)</summary>
```

```csharp
                // 型ごとに不変。タンジェント統一の判定から繰り返し呼ばれるためキャッシュする
```

`:555` の「ここで normalizedValue を 0 で潰すと、集約型レイヤーの補間形状キャリアとして...」は、前後の処理を読んだうえで `easingValue` の normalizedValue を潰さない理由を現状に即した文言へ書き直す（キャリアとしての用途は無くなったが、XML 互換のため値自体は保つ）。

- [ ] **Step 5: ビルドとテストを実行する**

Run: 冒頭の「ビルド・テスト」1 本
Expected: 2 構成ともビルド成功、全テスト PASS

`CalcTangentValue` の参照が残っているとビルドが落ちる。落ちたら残った呼び出し元も本 Step の方針（`LerpScratch`）で置き換える。

- [ ] **Step 6: 実機で確認する**

Task 2 Step 4 と同じ手順で DLL を差し替えて再起動する。

1. ブルームの `強度` と `しきい値` に 2 つのキーを打ち、カーブエディタで両者に別々のタンジェントを与える
2. 中間フレームへシークし、`eval_csharp` で適用値を読む

```csharp
var b = COM3D2.MotionTimelineEditor.PostEffects.PostEffectsClient.GetBloom();
b.intensity + " / " + b.threshold + " / " + b.thresholdColor + " / " + b.blurIterations
```

`PostEffectsClient` の名前空間は実環境で `eval_csharp` の補完に失敗することがある。その場合は
`typeof(COM3D2.MotionTimelineEditor.Plugin.PostEffectManager).Assembly.GetType("...")` で型を引き直す。

Expected: 開始キーと終了キーを結ぶ線上で、`intensity` と `threshold` の進み具合が**一致しない**
（一括補間なら 2 値の正規化した進行率が必ず一致する）。
`thresholdColor` が線形補間のままであること、`blurIterations` が区間内で変わらないことも併せて確認する。

- [ ] **Step 7: 仕様書を訂正する**

`docs-site/timeline/interpolation.md:37` の行（`ポストエフェクトは値ごとの個別補間ができず、区間の代表タンジェントで全項目をまとめて補間します。`）を削除する。直前の段落だけが残る形でよい。

`docs-site/timeline/compatibility.md:42` の行（`- ポストエフェクトは値ごとの個別補間ができません`）を削除する。

`docs-site/timeline/layers-effect.md:85` の行を差し替える。

```markdown
- 数値は値ごとのタンジェント、色は線形で補間します。表示フラグや段数などの整数値は区間開始値のまま切り替わります
```

- [ ] **Step 8: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/ \
        source/COM3D2.SceneEditor.Plugin/Timeline/ModelMaterial.cs \
        source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataBloom.cs \
        docs-site/timeline/interpolation.md docs-site/timeline/compatibility.md docs-site/timeline/layers-effect.md
git commit -m "$(cat <<'EOF'
feat(timeline): ポストエフェクトとマテリアルを値ごとのタンジェントで補間する

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_018my9CHjXRQZWrUnfwDTRzV
EOF
)"
```

---

### Task 6: モデルのグループ番号を生成時固定にする

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/StudioModelStat.cs:26-28`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/ModelHackManager.cs:10, 27, 206-230`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Hack/ExternalModelHack.cs:105-141`
- Modify: `docs-site/timeline/compatibility.md:43`
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/ModelGroupAssignTests.cs`（新規）

**Interfaces:**
- Consumes: `StudioModelStat(OfficialObjectInfo info, int group, Transform, AttachPoint, int, object, string, bool)` と `SetGroup(int)`（既存）
- Produces:
  - `public const int StudioModelStat.UnassignedGroup = -1`
  - `public static void ModelHackManager.FixGroup(List<StudioModelStat> models)` — 未採番（`group < 0`）と番号が重複した stat にだけ採番する。採番済みで一意な番号は変えない

- [ ] **Step 1: 失敗するテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/ModelGroupAssignTests.cs` を新規作成する。

```csharp
using System.Collections.Generic;
using Xunit;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// モデルのグループ採番を固定する。
    /// タイムライン XML はモデルを name ("ファイル名 (N)") で照合するため、
    /// 並び順が変わっても採番済みの番号が動いてはいけない
    /// </summary>
    public class ModelGroupAssignTests
    {
        private static MTEP.StudioModelStat CreateStat(string fileName, int group)
        {
            var info = new MTEP.OfficialObjectInfo
            {
                type = MTEP.StudioModelType.Mod,
                label = fileName,
                fileName = fileName,
            };
            return new MTEP.StudioModelStat(
                info, group, null, PhotoTransTargetObject.AttachPoint.Null, -1, null, "ModItemExplorer", true);
        }

        private static MTEP.StudioModelStat CreateUnassigned(string fileName)
        {
            return CreateStat(fileName, MTEP.StudioModelStat.UnassignedGroup);
        }

        [Fact]
        public void 未採番のstatに0と2から順に番号が振られる()
        {
            var a = CreateUnassigned("x.menu");
            var b = CreateUnassigned("x.menu");
            var c = CreateUnassigned("x.menu");
            var models = new List<MTEP.StudioModelStat> { a, b, c };

            MTEP.ModelHackManager.FixGroup(models);

            Assert.Equal("x.menu", a.name);
            Assert.Equal("x.menu (2)", b.name);
            Assert.Equal("x.menu (3)", c.name);
        }

        [Fact]
        public void 採番済みstatは並び順を変えても番号が変わらない()
        {
            var a = CreateUnassigned("x.menu");
            var b = CreateUnassigned("x.menu");
            var models = new List<MTEP.StudioModelStat> { a, b };
            MTEP.ModelHackManager.FixGroup(models);

            models.Reverse();
            MTEP.ModelHackManager.FixGroup(models);

            Assert.Equal("x.menu", a.name);
            Assert.Equal("x.menu (2)", b.name);
        }

        [Fact]
        public void 削除で空いた番号は次の生成で再利用される()
        {
            var a = CreateUnassigned("x.menu");
            var b = CreateUnassigned("x.menu");
            var models = new List<MTEP.StudioModelStat> { a, b };
            MTEP.ModelHackManager.FixGroup(models);

            // b を削除して新しいモデルを足す
            models.Remove(b);
            var c = CreateUnassigned("x.menu");
            models.Add(c);
            MTEP.ModelHackManager.FixGroup(models);

            Assert.Equal("x.menu", a.name);
            Assert.Equal("x.menu (2)", c.name);
        }

        [Fact]
        public void 別ファイルの番号は独立して振られる()
        {
            var a = CreateUnassigned("x.menu");
            var b = CreateUnassigned("y.menu");
            MTEP.ModelHackManager.FixGroup(new List<MTEP.StudioModelStat> { a, b });

            Assert.Equal("x.menu", a.name);
            Assert.Equal("y.menu", b.name);
        }

        [Fact]
        public void 名前から採番済みの番号はそのまま使われる()
        {
            // タイムライン読込で "x.menu (2)" から作られた stat を想定する
            var loaded = CreateStat("x.menu", 2);
            var existing = CreateUnassigned("x.menu");

            MTEP.ModelHackManager.FixGroup(new List<MTEP.StudioModelStat> { existing, loaded });

            Assert.Equal("x.menu (2)", loaded.name);
            Assert.Equal("x.menu", existing.name);
        }

        [Fact]
        public void 未採番statが採番済みstatより先に列挙されても既存の番号は動かない()
        {
            // プロバイダの列挙順で新規モデルが既存モデルより前に来るケース。
            // 新規 stat を未採番にしていないと (group 0 のままだと) 先に 0 を確保してしまい、
            // 既存の 0 番モデルが押し出されて名前が変わる
            var existing = CreateStat("x.menu", 0);
            var created = CreateUnassigned("x.menu");

            MTEP.ModelHackManager.FixGroup(new List<MTEP.StudioModelStat> { created, existing });

            Assert.Equal("x.menu", existing.name);
            Assert.Equal("x.menu (2)", created.name);
        }

        [Fact]
        public void 採番済み同士が衝突したら後勝ちで振り直される()
        {
            var a = CreateStat("x.menu", 2);
            var b = CreateStat("x.menu", 2);

            MTEP.ModelHackManager.FixGroup(new List<MTEP.StudioModelStat> { a, b });

            Assert.NotEqual(a.name, b.name);
            Assert.Equal("x.menu (2)", a.name);
        }
    }
}
```

- [ ] **Step 2: テストを実行して失敗を確認する**

Run: 冒頭の「ビルド・テスト」1 本
Expected: **ビルド失敗**（`StudioModelStat.UnassignedGroup` が無い / `ModelHackManager.FixGroup` が private: CS0117 / CS0122）

- [ ] **Step 3: StudioModelStat に未採番定数を足す**

`StudioModelStat.cs:26-28` の直前に足す。`group` の既定値は変えない（`FromModel` で必ず上書きされるため）。

```csharp
        /// <summary>未採番。ModelHackManager.FixGroup が一度だけ番号を振る</summary>
        public const int UnassignedGroup = -1;

        public OfficialObjectInfo info { get; private set; }
```

- [ ] **Step 4: FixGroup を「未採番だけ採番」に変える**

`ModelHackManager.cs:10` のフィールドを差し替える。

```csharp
        /// <summary>fileName ごとの使用中グループ番号。FixGroup の作業用に使い回す</summary>
        private static readonly Dictionary<string, HashSet<int>> _usedGroupMap
            = new Dictionary<string, HashSet<int>>();
```

`ModelHackManager.cs:27` の呼び出しは `FixGroup(_modelList);` のままでよい（静的メソッドになる）。

`ModelHackManager.cs:206-230` の `FixGroup` を次で置き換える。

```csharp
        /// <summary>
        /// 未採番 (group &lt; 0) の stat と、番号が先着と重複した stat にだけ番号を振る。
        /// 採番済みで一意な番号は削除されるまで変えない。列挙順が変わっても name が動かないようにするため。
        /// 削除で空いた番号は次の採番で再利用する。1 は使わない (0, 2, 3, ... の規則は従来どおり)
        /// </summary>
        public static void FixGroup(List<StudioModelStat> models)
        {
            _usedGroupMap.Clear();

            // 採番済みの番号を先着順で確保する
            List<StudioModelStat> pendings = null;
            foreach (var model in models)
            {
                if (model.group < 0 || !GetUsedGroups(model.info.fileName).Add(model.group))
                {
                    if (pendings == null)
                    {
                        pendings = new List<StudioModelStat>();
                    }
                    pendings.Add(model);
                }
            }

            if (pendings == null)
            {
                return;
            }

            foreach (var model in pendings)
            {
                var usedGroups = GetUsedGroups(model.info.fileName);

                var group = 0;
                while (usedGroups.Contains(group))
                {
                    group++;
                    if (group == 1) group++; // 1は使わない
                }

                model.SetGroup(group);
                usedGroups.Add(group);
            }
        }

        private static HashSet<int> GetUsedGroups(string fileName)
        {
            HashSet<int> groups;
            if (!_usedGroupMap.TryGetValue(fileName, out groups))
            {
                groups = new HashSet<int>();
                _usedGroupMap[fileName] = groups;
            }
            return groups;
        }
```

- [ ] **Step 5: プロバイダ由来の stat を未採番にする**

`ExternalModelHack.cs` の `GetOrCreateStat`（105〜141 行）のコメントと生成部を差し替える。プロバイダが返すファイル名にはグループ接尾辞が無く、`CreateModelStat` は接尾辞が無いと group 0 を付けてしまうため、明示的に未採番へ落とす。

```csharp
        /// <summary>
        /// GameObject に対応する StudioModelStat を返す。
        /// group は ModelHackManager.FixGroup が未採番のものにだけ一度振り、以後は変えない
        /// (列挙順が変わっても name が動かないようにするため)。
        /// 作り直しの判定は fileName の変化だけで行う
        /// </summary>
        private StudioModelStat GetOrCreateStat(GameObject obj)
        {
```

生成部（`var stat = modelManager.CreateModelStat(...)` の直後）へ 2 行足す。

```csharp
            var stat = modelManager.CreateModelStat(
                fileName,
                obj.transform,
                AttachPoint.Null,
                -1,
                obj,
                pluginName,
                obj.activeSelf);

            // プロバイダの列挙名にはグループ接尾辞が無く、CreateModelStat は接尾辞が無いと 0 を付ける。
            // 0 のままだと列挙順によっては新規 stat が既存の 0 番を押し出すため、明示的に未採番へ落とす
            stat.SetGroup(StudioModelStat.UnassignedGroup);

            _statMap[obj] = stat;
            return stat;
```

- [ ] **Step 6: テストを実行して通ることを確認する**

Run: 冒頭の「ビルド・テスト」1 本
Expected: 2 構成ともビルド成功、`ModelGroupAssignTests` の 7 件を含め全テスト PASS

- [ ] **Step 7: 実機で確認する**

ModItemExplorer が入っていること。Task 2 Step 4 と同じ手順で DLL を差し替えて再起動する。

1. 同じ MOD 家具を 3 つ配置し、`eval_csharp` で名前を読む

```csharp
string.Join(", ", COM3D2.MotionTimelineEditor.Plugin.ModelHackManager.instance.modelList
    .ConvertAll(m => m.name).ToArray())
```

Expected: `x.menu, x.menu (2), x.menu (3)`

2. ModItemExplorer 側で配置順を入れ替えて、もう一度同じ式を評価する
Expected: 並びは変わるが各モデルの名前は変わらない
3. 2 番目を削除してから同じ家具を 1 つ追加し、同じ式を評価する
Expected: 空いた `(2)` が再利用される
4. `x.menu (2)` のキーを含むタイムラインを読み込む
Expected: 生成されたモデルの名前が `x.menu (2)` で、キーが割り当たる

- [ ] **Step 8: 仕様書を訂正する**

`docs-site/timeline/compatibility.md:43` の行（`- モデル系レイヤーで同じファイルのモデルを複数置いている場合、配置順が変わるとキーの割り当て先がずれることがあります`）を削除する。

- [ ] **Step 9: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/StudioModelStat.cs \
        source/COM3D2.SceneEditor.Plugin/Timeline/Manager/ModelHackManager.cs \
        source/COM3D2.SceneEditor.Plugin/Timeline/Hack/ExternalModelHack.cs \
        source/COM3D2.SceneEditor.Plugin.Tests/ModelGroupAssignTests.cs \
        docs-site/timeline/compatibility.md
git commit -m "$(cat <<'EOF'
fix(timeline): モデルのグループ番号を生成時に固定して並び替えでずれないようにする

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_018my9CHjXRQZWrUnfwDTRzV
EOF
)"
```

---

### Task 7: フレームレート変更の仕様明記とレターボックスの実機確認

**Files:**
- Modify: `docs-site/timeline/playback.md:26-34`
- Modify: `docs-site/timeline/settings.md:22-23`（レターボックスが描画される場合のみ）
- Modify: `docs-site/timeline/compatibility.md:24`（同上）

**Interfaces:**
- Consumes: なし
- Produces: なし（文書のみ）

- [ ] **Step 1: フレームレートの仕様を playback.md に追記する**

`docs-site/timeline/playback.md` の「フレームレートと最終フレーム」節、`1 フレームの時間は ...` の段落（33 行目）の直後に足す。

```markdown

フレームレートを変えてもキーフレームのフレーム番号は変わりません。同じフレーム番号のまま 1 フレームの時間だけが変わるため、30 → 60 にするとアニメーション全体が 2 倍速になります。尺を保ちたい場合はキーを手で打ち直してください。
```

- [ ] **Step 2: レターボックスを実機で確認する**

ゲームが起動していること。起動していなければユーザーに起動を依頼し、この Step を保留する。

`LetterBoxView`（`source/COM3D2.SceneEditor.Plugin/Timeline/LetterBoxView.cs`）は実装済みで `CameraManager` が生成している。仕様書の「反映されません」が未検証なので確かめる。

タイムラインを読み込んだ状態で `タイムライン設定` → `アスペクト比` に `16` / `9` を入れ、`ﾚﾀｰﾎﾞｯｸｽ透過度` を `1.0` にする。MCP `com3d25-devbridge` の `screenshot` で画面を撮り、上下（または左右）に黒帯が出ているか見る。

- [ ] **Step 3: 確認結果に応じて仕様書を直す**

**描画される場合:**

`docs-site/timeline/settings.md:22-23` の 2 行を差し替える。

```markdown
| `アスペクト比` の `幅` / `高さ` | 0 | レターボックスの比率。0 なら非表示 |
| `ﾚﾀｰﾎﾞｯｸｽ透過度` | 1.0 | レターボックスの不透明度 |
```

`docs-site/timeline/compatibility.md:24` の行（`- \`タイムライン設定\` の \`アスペクト比\` / \`ﾚﾀｰﾎﾞｯｸｽ透過度\` は画面に反映されません。値は保存されます`）を削除する。

**描画されない場合:**

仕様書は現状のまま残し、`docs/timeline-release-debt-review.md` の「検討結果」節へ 1 行追記する。

```markdown
- 5: レターボックスは実機で描画されないことを確認した（`LetterBoxView` は生成されている）。原因調査は本設計の範囲外で、別途課題とする
```

- [ ] **Step 4: 仕様書のビルドを確認する**

Run: `npm --prefix docs-site run docs:build`
Expected: エラーなく完了する（`docs-site/package.json` の `docs:build` = `vitepress build`）

- [ ] **Step 5: コミット**

```bash
git add docs-site/timeline/ docs/timeline-release-debt-review.md
git commit -m "$(cat <<'EOF'
docs(timeline): フレームレート変更の挙動とレターボックスの記述を実態に合わせる

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_018my9CHjXRQZWrUnfwDTRzV
EOF
)"
```

---

## 完了条件

- [ ] 2 構成（COM3D2 / COM3D25）ともビルドが通る
- [ ] `dotnet test source/COM3D2.SceneEditor.Plugin.Tests` が全件 PASS
- [ ] 実機確認（Task 2 / 5 / 6 / 7）が済んでいる
- [ ] `docs-site/timeline/` に「メイド単位ではない」「空のレイヤーとして実体化」「値ごとの個別補間ができない」「配置順が変わるとキーの割り当て先がずれる」の記述が残っていない
  - Run: `rg "メイド単位ではな|空のレイヤーとして実体化|個別補間ができ|配置順が変わると" docs-site/timeline/`
  - Expected: ヒット 0 件
- [ ] `docs/superpowers/specs/2026-09-13-timeline-release-debt-design.md` の決定一覧で「実装: あり」の 5 項目すべてにタスクが対応している（1→Task 1、2→Task 6、4→Task 4+5、5→Task 3、6→Task 2、7→Task 7）

## レビュー却下メモ（2026-09-13 plan-review）

- **`ExternalModelHack.GetOrCreateStat` の `stat.SetGroup(UnassignedGroup)` は無くても採番結果は変わらないので冗長** — 誤検知として却下。プロバイダの列挙順で新規モデルが既存モデルより**前**に来ると、group 0 のままの新規 stat が先に 0 を確保し、既存の 0 番モデルが pending へ回されて名前が変わる（まさに Task 6 で直そうとしている症状）。Task 6 Step 1 に `未採番statが採番済みstatより先に列挙されても既存の番号は動かない` を追加して、この呼び出しが必要であることをテストで固定した
- **`lerpKinds` は型ごとに不変なのでインスタンスフィールドではなく static キャッシュにすべき** — YAGNI として却下。`LerpFrom` はレイヤーごと・TransformType ごとに 1 つだけ作るスクラッチインスタンスからしか呼ばれないため、計算はプロセス全体で数回にとどまる。型ごとの static キャッシュは `Dictionary<Type, ...>` を増やすだけで割に合わない
