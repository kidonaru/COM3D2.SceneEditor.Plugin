# PostEffects.Plugin 操作履歴連携 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** PostEffects.Plugin のウィンドウでタイムライン対応 6 系統を編集したとき、SceneEditor 内部の編集と同じく操作履歴へ登録する (シーンモードでは undo/redo、タイムラインモードでは自動キーフレーム登録が効く)。

**Architecture:** 既存の `HistoryAPI.Register` は「確定済み 1 件」を積むだけで、タイムラインモードでは捨てられ自動キーフレームも走らない。代わりに SceneEditor 内部が使う `HistoryManager.BeforeEdit` (変更前スナップショット → マウス解放で確定 → `onEditCommitted`) を外部へ公開する。状態は外部側が文字列 (プリセット XML) で捕捉・復元し、SceneEditor 側は文字列を持つ汎用スナップショット `ExternalStateSnapshot` で包む。PostEffects 側は既存の値変更フック (`onBeforeValueChanged`) から `AutoEditModeClient.Enter` に続けて `HistoryClient.BeforeEdit` を呼ぶ。

**Tech Stack:** C# (.NET 3.5 / Unity 5.6、C# 4 相当構文)、MSBuild、xUnit (SceneEditor テストのみ)

**Spec:** 本セッションの調査結論 (2026-09-16)。要点:
- `HistoryManager.BeforeEditCore` は `AutoEditMode.Enter()` → 確定待ち生成 (初回だけ `capture()` 評価) → `Update` でマウス解放時に `CommitPending`。`CommitPending` は `before.CaptureCurrent()` と `Approximately` で無変化を除外し、シーンモードなら `AddEntry`、タイムラインモードでも `onEditCommitted` を通知する。`TimelineWindow.HandleEditFinished` がこれを受けて `FocusEditedLayer` と `TryAutoKeyFrame` (メイド無し編集は `editedMaid == null` で許可) を行う
- `HistoryScope` はメイド不要なスコープ (`Light` / `LiveEffect` 等) を `HistoryScopeUtils.RequiresMaid` で区別する。外部用に `External` を追加し、確定待ちの区別は `targetKey` (文字列) で行う
- PostEffects 側の状態は `PresetManager.instance.CapturePresetXml()` (string) と `ApplyPresetXml(string)` で全エフェクトを丸ごと捕捉・復元できる (シーンプリセット連携で実績あり)。エフェクト単位の直列化は無いので全体スナップショットにする。undo で同時に触った他エフェクトも戻るが、`LiveEffect` スコープと同じ「全体状態」扱いとして許容する
- 対象は自動編集モードと同じタイムライン対応 6 系統 (DoF / GTToneMap / パラフィン / 距離フォグ / リムライト / ブルーム)。`BeforeEditCore` が無条件に `AutoEditMode.Enter()` を呼ぶため、他の系統へ広げると再生停止を伴う。広げるかは別判断
- MTEUtils は SceneEditor / PostEffects 共有の submodule。`HistoryClient` の追加メソッドは submodule 側でコミットし両親リポの参照を進める
- ホスト側の追加は既存クラスへの「別名追加」で行う (`HistoryAPI.BeforeEdit`)。旧版ホストでは `HistoryClient` 側で任意メソッド扱い (null なら no-op)

## Global Constraints

- プラグイン間連携はリフレクション経由のみ。契約は標準型 (string / Func / Action) だけで組む
- 公開済みホストのシグネチャは変更禁止。新規メソッドは追加のみ
- コードのコメント・ログ文言は日本語
- ビルドは COM3D2 → COM3D25 の順に MSBuild を直接叩く。Git Bash からは `export MSYS2_ARG_CONV_EXCL="*"` が必要
- `deploy.bat` / `deploy.ps1` は実行しない。git worktree は使わない
- SceneEditor は `feature/timeline-window`、PostEffects は `feature/auto-edit-mode`、MTEUtils submodule は `master`

## ファイル構成

| リポジトリ | ファイル | 責務 |
|---|---|---|
| SceneEditor | `source/COM3D2.SceneEditor.Plugin/Manager/History/HistoryScope.cs` | `External` スコープ追加 (メイド不要) |
| SceneEditor | `source/COM3D2.SceneEditor.Plugin/Manager/History/ExternalStateSnapshot.cs` (新規) | 文字列状態を持つ汎用スナップショット |
| SceneEditor | `source/COM3D2.SceneEditor.Plugin/HistoryAPI.cs` | `BeforeEdit` を追加 |
| SceneEditor | `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj` | Compile 登録 |
| SceneEditor | `source/COM3D2.SceneEditor.Plugin.Tests/HistoryAPIContractTests.cs` (新規) | 契約固定 |
| SceneEditor | `source/COM3D2.SceneEditor.Plugin.Tests/ExternalStateSnapshotTests.cs` (新規) | スナップショットの純粋ロジック |
| MTEUtils | `HistoryClient.cs` | `BeforeEdit` を任意メソッドとして追加 |
| PostEffects | `source/COM3D25.PostEffects.Plugin/MainWindow.cs` | 値変更フックで履歴の変更前記録も行う |

---

### Task 1: SceneEditor 側に外部向け `BeforeEdit` 経路を追加

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/History/HistoryScope.cs` (enum 末尾と `RequiresMaid` の false 群)
- Create: `source/COM3D2.SceneEditor.Plugin/Manager/History/ExternalStateSnapshot.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/HistoryAPI.cs` (`Register` の直後)
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj:237` (`Manager\History\IStateSnapshot.cs` の直後)
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/ExternalStateSnapshotTests.cs`、`source/COM3D2.SceneEditor.Plugin.Tests/HistoryAPIContractTests.cs`

**Interfaces:**
- Consumes: `HistoryManager.instance.BeforeEdit(Maid, HistoryScope, string, object, Func<IStateSnapshot>)`、`IStateSnapshot`
- Produces: `public static void HistoryAPI.BeforeEdit(string description, string targetKey, Func<string> capture, Action<string> apply, Func<bool> canApply)` (Task 2 のクライアントがリフレクションで探す)、`public class ExternalStateSnapshot : IStateSnapshot`

- [ ] **Step 1: スナップショットのテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/ExternalStateSnapshotTests.cs`:

```csharp
using System;
using COM3D2.SceneEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// 外部プラグイン向けの文字列スナップショット。
    /// 捕捉・復元は外部のデリゲートに委ね、ここは前後比較と適用可否だけを持つ
    /// </summary>
    public class ExternalStateSnapshotTests
    {
        [Fact]
        public void 捕捉時の文字列を保持し_同じ文字列なら無変化とみなす()
        {
            var state = "a";
            var snapshot = ExternalStateSnapshot.Capture(() => state, _ => { }, null);

            var current = snapshot.CaptureCurrent();

            Assert.True(snapshot.Approximately(current));
        }

        [Fact]
        public void 文字列が変わっていれば変化ありとみなす()
        {
            var state = "a";
            var snapshot = ExternalStateSnapshot.Capture(() => state, _ => { }, null);

            state = "b";
            var current = snapshot.CaptureCurrent();

            Assert.False(snapshot.Approximately(current));
        }

        [Fact]
        public void Apply_は捕捉時の文字列を復元デリゲートへ渡す()
        {
            var applied = "";
            var snapshot = ExternalStateSnapshot.Capture(() => "before", s => applied = s, null);

            snapshot.Apply(null);

            Assert.Equal("before", applied);
        }

        [Fact]
        public void 捕捉が_null_を返したらスナップショットは作らない()
        {
            var snapshot = ExternalStateSnapshot.Capture(() => null, _ => { }, null);

            Assert.Null(snapshot);
        }

        [Fact]
        public void canApply_未指定なら常に適用可()
        {
            var snapshot = ExternalStateSnapshot.Capture(() => "a", _ => { }, null);

            Assert.True(snapshot.CanApply(null));
        }

        [Fact]
        public void canApply_が_false_なら適用不可()
        {
            var snapshot = ExternalStateSnapshot.Capture(() => "a", _ => { }, () => false);

            Assert.False(snapshot.CanApply(null));
        }

        [Fact]
        public void 別種のスナップショットとは常に変化ありとみなす()
        {
            var snapshot = ExternalStateSnapshot.Capture(() => "a", _ => { }, null);

            Assert.False(snapshot.Approximately(null));
        }
    }
}
```

`source/COM3D2.SceneEditor.Plugin.Tests/HistoryAPIContractTests.cs`:

```csharp
using System;
using System.Reflection;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// 外部プラグイン向け履歴 API の公開契約を固定する。
    /// MTEUtils の HistoryClient は型名とメソッド名だけを頼りにリフレクションで探すため、
    /// 改名やシグネチャ変更はビルドを壊さず実機で黙って無効になる
    /// </summary>
    public class HistoryAPIContractTests
    {
        private const string HostTypeName = "COM3D2.SceneEditor.Plugin.HistoryAPI";

        private static Type GetHostType()
        {
            return typeof(HistoryManager).Assembly.GetType(HostTypeName);
        }

        [Fact]
        public void ホスト型がプラグインアセンブリに存在する()
        {
            Assert.True(GetHostType() != null, HostTypeName + " がアセンブリにありません");
        }

        [Theory]
        [InlineData("Register", new[] { typeof(string), typeof(Action), typeof(Action), typeof(Func<bool>) })]
        [InlineData("BeforeEdit", new[] { typeof(string), typeof(string), typeof(Func<string>), typeof(Action<string>), typeof(Func<bool>) })]
        public void 公開メソッドのシグネチャが契約どおり(string methodName, Type[] parameterTypes)
        {
            var method = GetHostType().GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);

            Assert.True(method != null, methodName + " が public static で見つかりません");
            Assert.Equal(typeof(void), method.ReturnType);

            var parameters = method.GetParameters();
            Assert.Equal(parameterTypes.Length, parameters.Length);
            for (var i = 0; i < parameterTypes.Length; i++)
            {
                Assert.Equal(parameterTypes[i], parameters[i].ParameterType);
            }
        }
    }
}
```

- [ ] **Step 2: テストが失敗することを確認する**

```bash
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin.Tests
dotnet test --filter "FullyQualifiedName~ExternalStateSnapshotTests|FullyQualifiedName~HistoryAPIContractTests" 2>&1 | tail -15
```

期待: `ExternalStateSnapshot` が無いのでテストプロジェクトのビルドエラー (型未定義)。これが「失敗」の確認になる。

- [ ] **Step 3: `HistoryScope.External` を追加する**

`Manager/History/HistoryScope.cs` の enum 末尾 (`LiveEffect,` の後) に追加:

```csharp
        /// <summary>
        /// 外部プラグインの状態 (HistoryAPI.BeforeEdit 経由)。
        /// 捕捉・復元は外部のデリゲートに委ね、対象の区別は targetKey で行う
        /// </summary>
        External,
```

`RequiresMaid` の `case HistoryScope.LiveEffect:` の直後に追加:

```csharp
                case HistoryScope.External:
```

- [ ] **Step 4: `ExternalStateSnapshot` を実装する**

`Manager/History/ExternalStateSnapshot.cs`:

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 外部プラグインの状態を文字列 1 本で持つスナップショット (HistoryAPI.BeforeEdit 用)。
    /// 捕捉・復元の中身は外部のデリゲートに委ね、こちらは前後比較と適用可否だけを担う。
    /// 前後比較は文字列の完全一致 (外部側の直列化が決定的である前提)
    /// </summary>
    public class ExternalStateSnapshot : IStateSnapshot
    {
        private readonly string _state;
        private readonly Func<string> _capture;
        private readonly Action<string> _apply;
        private readonly Func<bool> _canApply;

        private ExternalStateSnapshot(
            string state, Func<string> capture, Action<string> apply, Func<bool> canApply)
        {
            _state = state;
            _capture = capture;
            _apply = apply;
            _canApply = canApply;
        }

        /// <summary>現在の状態を捕捉する。捕捉が null を返した (記録できない) ときは null</summary>
        public static ExternalStateSnapshot Capture(
            Func<string> capture, Action<string> apply, Func<bool> canApply)
        {
            var state = capture();
            if (state == null)
            {
                return null;
            }
            return new ExternalStateSnapshot(state, capture, apply, canApply);
        }

        /// <summary>ボーンを持たないスコープなので何もしない</summary>
        public void AddBones(IEnumerable<Transform> targetBones)
        {
        }

        public IStateSnapshot CaptureCurrent()
        {
            // 確定時に捕捉できなくても before 側の対象集合は変わらないので、
            // null のままにせず「変化なし」扱いへ倒して無変化エントリとして捨てさせる
            return Capture(_capture, _apply, _canApply) ?? this;
        }

        public void Apply(Maid maid)
        {
            _apply(_state);
        }

        public bool Approximately(IStateSnapshot other)
        {
            var snapshot = other as ExternalStateSnapshot;
            return snapshot != null && snapshot._state == _state;
        }

        public bool CanApply(Maid maid)
        {
            return _canApply == null || _canApply();
        }
    }
}
```

- [ ] **Step 5: `HistoryAPI.BeforeEdit` を追加する**

`HistoryAPI.cs` の `Register` メソッドの直後に追加:

```csharp
        /// <summary>
        /// 変更前の状態を控える (SceneEditor 内部の BeforeEdit と同じ経路)。
        /// 値を書き換える操作の直前に毎回呼んでよく、同じ targetKey の連続変更は
        /// マウス解放時に 1 件へまとめて確定する。編集モードへの自動移行も伴う。
        /// シーンモードでは undo/redo に積まれ、タイムラインモードでは履歴には積まれず
        /// 自動キーフレーム登録の契機になる (内部の編集と同じ扱い)
        /// </summary>
        /// <param name="description">履歴ウィンドウに表示する操作名</param>
        /// <param name="targetKey">確定待ちを区別するキー (エフェクト名等)。null なら区別しない</param>
        /// <param name="capture">現在の状態を文字列で返す。記録できないときは null を返す</param>
        /// <param name="apply">文字列の状態を書き戻す (undo/redo)</param>
        /// <param name="canApply">対象消滅等で今は適用できないとき false。null なら常に適用可</param>
        public static void BeforeEdit(
            string description, string targetKey,
            Func<string> capture, Action<string> apply, Func<bool> canApply)
        {
            if (capture == null || apply == null)
            {
                MTEUtils.LogError("HistoryAPI.BeforeEdit: capture/apply は必須です: {0}", description);
                return;
            }

            HistoryManager.instance.BeforeEdit(
                null, HistoryScope.External, description, targetKey,
                () => ExternalStateSnapshot.Capture(capture, apply, canApply));
        }
```

クラスの doc コメント冒頭の契約列挙に 1 行足す:

```csharp
    /// - BeforeEdit は確定前の操作向け。値を書く直前に毎回呼び、確定 (マウス解放) は本体に任せる
```

- [ ] **Step 6: csproj に登録する**

`<Compile Include="Manager\History\IStateSnapshot.cs" />` の直後に:

```xml
    <Compile Include="Manager\History\ExternalStateSnapshot.cs" />
```

- [ ] **Step 7: 両構成をビルドしてテストを通す**

```bash
export MSYS2_ARG_CONV_EXCL="*"
MSB="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin
for gv in COM3D2 COM3D25; do "$MSB" COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=$gv "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" /v:m /nologo 2>&1 | grep -E "error|エラー"; done
cd ../COM3D2.SceneEditor.Plugin.Tests && dotnet test 2>&1 | grep -E "^失敗|^成功"
```

期待: エラー 0、テスト全件 PASS (新規 9 件を含む)。

- [ ] **Step 8: コミット**

```bash
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin
git add source/COM3D2.SceneEditor.Plugin/Manager/History/HistoryScope.cs source/COM3D2.SceneEditor.Plugin/Manager/History/ExternalStateSnapshot.cs source/COM3D2.SceneEditor.Plugin/HistoryAPI.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj source/COM3D2.SceneEditor.Plugin.Tests/ExternalStateSnapshotTests.cs source/COM3D2.SceneEditor.Plugin.Tests/HistoryAPIContractTests.cs
git commit -m "feat(history): 外部プラグイン向けに変更前記録 API HistoryAPI.BeforeEdit を追加"
```

---

### Task 2: MTEUtils `HistoryClient.BeforeEdit` の追加 (submodule)

**Files:**
- Modify: `W:\COM3D2_5\work\COM3D2.PostEffects.Plugin\source\COM3D25.PostEffects.Plugin\MTEUtils\HistoryClient.cs` (フィールド群、`Initialize` の try 内、`Register` の直後)

**Interfaces:**
- Consumes: Task 1 の `HistoryAPI.BeforeEdit`
- Produces: `public static void HistoryClient.BeforeEdit(string description, string targetKey, Func<string> capture, Action<string> apply, Func<bool> canApply = null)`、`public static bool HistoryClient.canBeforeEdit`

- [ ] **Step 1: 任意メソッドとして束縛する**

フィールド `_removeOnChanged` の直後に追加:

```csharp
        // 旧版ホストには無い任意メソッド。未定義でも Register 等の接続自体は有効のまま
        private static Action<string, string, Func<string>, Action<string>, Func<bool>> _beforeEdit;
```

`Initialize` の try 内、`onChanged` の束縛ブロックの直後に追加:

```csharp
                // 変更前記録は新しめのホストにしか無いので、無ければ no-op に落とす
                var beforeEdit = type.GetMethod("BeforeEdit", BindingFlags.Public | BindingFlags.Static);
                if (beforeEdit != null)
                {
                    _beforeEdit = (Action<string, string, Func<string>, Action<string>, Func<bool>>)
                        Delegate.CreateDelegate(
                            typeof(Action<string, string, Func<string>, Action<string>, Func<bool>>),
                            beforeEdit);
                }
```

catch 節の `_removeOnChanged = null;` の直後に `_beforeEdit = null;` を追加。

`Register` の直後に追加:

```csharp
        /// <summary>ホストが変更前記録 (BeforeEdit) に対応しているか</summary>
        public static bool canBeforeEdit => isAvailable && _beforeEdit != null;

        /// <summary>
        /// 変更前の状態を控える。値を書き換える操作の直前に毎回呼んでよく、
        /// 同じ targetKey の連続変更はホスト側でマウス解放時に 1 件へまとまる。
        /// 編集モードへの自動移行も伴う。SceneEditor が無い・旧版なら何もしない
        /// </summary>
        /// <param name="description">履歴ウィンドウに表示する操作名</param>
        /// <param name="targetKey">確定待ちを区別するキー。null なら区別しない</param>
        /// <param name="capture">現在の状態を文字列で返す。記録できないときは null</param>
        /// <param name="apply">文字列の状態を書き戻す</param>
        /// <param name="canApply">今は適用できないとき false。null なら常に適用可</param>
        public static void BeforeEdit(
            string description, string targetKey,
            Func<string> capture, Action<string> apply, Func<bool> canApply = null)
        {
            if (canBeforeEdit)
            {
                _beforeEdit(description, targetKey, capture, apply, canApply);
            }
        }
```

クラス doc の契約列挙に 1 行足す:

```csharp
    /// - BeforeEdit は確定前の操作向け。値を書く直前に毎回呼び、確定は本体に任せる (旧版ホストでは no-op)
```

- [ ] **Step 2: PostEffects をビルドして通ることを確認する**

```bash
export MSYS2_ARG_CONV_EXCL="*"
MSB="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
cd /w/COM3D2_5/work/COM3D2.PostEffects.Plugin/source/COM3D25.PostEffects.Plugin
for gv in COM3D2 COM3D25; do "$MSB" COM3D25.PostEffects.Plugin.csproj /p:Configuration=Debug /p:GameVersion=$gv "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" /v:m /nologo 2>&1 | grep -E "error|エラー"; done
```

- [ ] **Step 3: submodule でコミットし、両親リポの参照を進める**

```bash
cd /w/COM3D2_5/work/COM3D2.PostEffects.Plugin/source/COM3D25.PostEffects.Plugin/MTEUtils
git add HistoryClient.cs && git commit -m "feat: HistoryClient に変更前記録 BeforeEdit を追加"
cd /w/COM3D2_5/work/COM3D2.PostEffects.Plugin
git add source/COM3D25.PostEffects.Plugin/MTEUtils && git commit -m "chore: MTEUtils を更新 (HistoryClient.BeforeEdit)"
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin/MTEUtils
git fetch -q /w/COM3D2_5/work/COM3D2.PostEffects.Plugin/source/COM3D25.PostEffects.Plugin/MTEUtils master && git merge --ff-only -q FETCH_HEAD
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin
git add source/COM3D2.SceneEditor.Plugin/MTEUtils && git commit -m "chore: MTEUtils を更新 (HistoryClient.BeforeEdit)"
```

---

### Task 3: PostEffects ウィンドウの値変更を履歴に乗せる

**Files:**
- Modify: `source/COM3D25.PostEffects.Plugin/MainWindow.cs` (`_enterAutoEditMode` フィールド周辺、`DrawEffectContent` のカテゴリリセット、`DrawEffectRow`)

**Interfaces:**
- Consumes: Task 2 の `HistoryClient.BeforeEdit`、`AutoEditModeClient.Enter`、`PresetManager.instance.CapturePresetXml()` / `ApplyPresetXml(string)`、`EffectControllerBase.effectId` / `effectName`

- [ ] **Step 1: フックを「編集モード + 履歴」に置き換える**

`_enterAutoEditMode` フィールドとそのコメントを次に置き換える:

```csharp
        // 値を書く直前に SceneEditor の編集モードへ入り、変更前の状態を履歴に控える。
        // GUIComboBox は描画時にフックのデリゲートを掴み、ポップアップ確定時 (描画パス終了後) に
        // 呼ぶため、対象をフィールドで差し替える方式だと確定時に対象を見失う。
        // コントローラごとのクロージャをキャッシュして、デリゲート自体が対象を持つようにする
        private readonly Dictionary<EffectControllerBase, Action> _beforeEditHooks =
            new Dictionary<EffectControllerBase, Action>();

        // 履歴の捕捉・復元はプリセット XML で全エフェクトを丸ごと扱う (エフェクト単位の直列化は無い)
        private static readonly Func<string> CaptureHistoryState = () => presetManager.CapturePresetXml();
        private static readonly Action<string> ApplyHistoryState = xml => presetManager.ApplyPresetXml(xml);

        private Action GetBeforeEditHook(EffectControllerBase controller)
        {
            Action hook;
            if (!_beforeEditHooks.TryGetValue(controller, out hook))
            {
                hook = () => BeforeEdit(controller);
                _beforeEditHooks[controller] = hook;
            }
            return hook;
        }

        /// <summary>
        /// 値を書く直前に呼ぶ。編集モードへ入ってレイヤーを切り替え、履歴の変更前を控える。
        /// HistoryClient.BeforeEdit もホスト側で編集モードへ入るが、レイヤー切替は
        /// AutoEditModeClient.Enter だけが行うため両方呼ぶ
        /// </summary>
        private static void BeforeEdit(EffectControllerBase controller)
        {
            AutoEditModeClient.Enter(POST_EFFECT_LAYER_NAME);
            HistoryClient.BeforeEdit(
                "ポストエフェクト: " + controller.effectName, controller.effectId,
                CaptureHistoryState, ApplyHistoryState);
        }
```

`presetManager` は既存の `private static PresetManager presetManager => PresetManager.instance;` を使う。`System.Collections.Generic` は既に using 済み。

- [ ] **Step 2: `DrawEffectRow` で対象ごとのフックを差す**

`DrawEffectRow` の先頭ブロックの `view.onBeforeValueChanged = _enterAutoEditMode;` を次に置き換える:

```csharp
                view.onBeforeValueChanged = GetBeforeEditHook(controller);
```

`finally` 内は現状 (`view.onBeforeValueChanged = null;`) のまま。

- [ ] **Step 3: カテゴリリセットを履歴に乗せる**

`DrawEffectContent` のリセットボタン内、`AutoEditModeClient.Enter(POST_EFFECT_LAYER_NAME);` を次に置き換える:

```csharp
                        AutoEditModeClient.Enter(POST_EFFECT_LAYER_NAME);
                        HistoryClient.BeforeEdit(
                            "ポストエフェクト: " + category.GetName() + " をリセット", "category:" + category,
                            CaptureHistoryState, ApplyHistoryState);
```

- [ ] **Step 4: 両構成をビルドする**

Task 2 Step 2 と同じコマンド。期待: エラー 0。

- [ ] **Step 5: コミット**

```bash
cd /w/COM3D2_5/work/COM3D2.PostEffects.Plugin
git add source/COM3D25.PostEffects.Plugin/MainWindow.cs
git commit -m "feat(window): タイムライン対応 6 系統の値変更を SceneEditor の操作履歴に乗せる"
```

---

### Task 4: 実機検証

- [ ] ゲーム停止中に両リポジトリで `debug.bat com3d25` を実行し、ゲームを起動する
- [ ] **シーンモード** (タイムライン未読込): PostEffects ウィンドウでブルームの強度を動かし、SceneEditor の履歴ウィンドウに「ポストエフェクト: ブルーム」が 1 件だけ (ドラッグ 1 回 = 1 件) 積まれ、undo で値が戻り redo で進むことを確認
- [ ] 値を動かさずスライダーをクリックしただけでは履歴が増えないことを確認
- [ ] **タイムラインモード** (ポストエフェクトレイヤーあり、自動キーフレーム ON): パラフィンの値を動かして離すと、現在フレームにポストエフェクトのキーが自動登録されることを確認 (履歴ウィンドウにはタイムライン操作として積まれる)
- [ ] ブルームの HDR モード (コンボボックス) をポップアップから変えても履歴が 1 件積まれることを確認 (ポップアップ確定は描画パス終了後に発火する経路)
- [ ] 検証中は別エフェクトを同時に触らない (全体スナップショットのため巻き込まれる)。undo 後に PostEffects 側の未保存 (dirty) 表示が残るのは仕様として許容
- [ ] `eval_csharp` で `COM3D2.SceneEditor.Plugin.HistoryManager.instance.entries.Count` を前後で読んで件数の増減を裏取りする

## レビュー却下メモ

- GUIComboBox 経由で `_editingController` が null に戻って空振りする (🔴) — 取り込み済み。コントローラごとのクロージャを Dictionary でキャッシュする方式へ変更した
- `ApplyPresetXml` が undo でも dirty を立てる (🟡) — 許容。dirty は「選択中プリセットと差がある」の意味で、undo 後も差はある。検証項目に明記した
- カテゴリリセットの targetKey に enum の ToString を使う (🟡) — 確定待ちの区別にしか使わず永続化しないため、enum 名変更の影響は無い
- 契約テストのアセンブリ解決 (🟡) — 既存 `TimelineLayerGateHostContractTests` / `AutoEditModeHostContractTests` と同じ経路で通っている

