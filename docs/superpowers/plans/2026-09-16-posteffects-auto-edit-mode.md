# PostEffects.Plugin 自動編集モード連携 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** PostEffects.Plugin のウィンドウでタイムライン対応 6 系統の値を触ったとき、SceneEditor 側と同じく値を書く直前に自動で編集モードへ入るようにする。

**Architecture:** SceneEditor に新ホスト `AutoEditModeHost.Enter(string layerName)` を追加し、`AutoEditMode.Enter()` と「触ったレイヤー」の控え (`TimelineLayerGate.RecordEditedLayer`) を外部へ公開する。PostEffects 側は `TimelineLayerGateClient` と同じ作法のリフレクションクライアント `AutoEditModeClient` を追加し、タイムライン対応コントローラの描画区間で `GUIView.onBeforeValueChanged` にこれを差す。フックを通らないボタン書き込み (追加/削除/リセット) は `view.NotifyBeforeValueChanged()` で同じ経路に乗せる。

**Tech Stack:** C# (.NET Framework 3.5 / Unity 5.6 系、C# 4 相当の構文制約)、MSBuild、xUnit (SceneEditor テストのみ)

**Spec:** 本セッションの調査結論 (2026-09-16)。要点:
- PostEffects 側 `MTEUtils/GUIView.cs` は SceneEditor 側と同じ版で `onBeforeValueChanged` フックを持つ (スライダー / トグル / 色欄 / コンボ / ドラッグ数値欄で発火。`DrawColor` はピッカー経由の変更も、`GUIComboBox` はポップアップ選択も同フックを通す)
- `DrawColor` の「R」ボタンは `ColorFieldCache.ResetColor()` 後、同フレーム末尾の `fieldCache.color != color` 判定で `NotifyBeforeValueChanged()` を通るため対応不要 (`GUIView.cs:3240-3245`)
- `DrawButton` はフックを通さない。対象は `EffectControllerBase.DrawDataTabs` の追加/削除、`MainWindow.DrawEffectRow` のリセット、`MainWindow.DrawEffectContent` のカテゴリリセット
- SceneEditor 内の編集は履歴確定 (`HistoryManager.onEditCommitted`) → `TimelineWindow.FocusEditedLayer` で「触ったレイヤー」へ追従するが、外部プラグインの編集は履歴に乗らず確定イベントが来ない。そのためホストは控えを残さず、`TimelineWindow.FocusLayerKeepingEdit(Type, int)` でその場でアクティブレイヤーを切り替える (追記 2026-09-16: カテゴリ表示のコンボはアクティブレイヤーに追従するので、レイヤーカテゴリの自動切替もこれで実現する)
- 連携設定 (`linkExternalPlugin`) は参照しない。`TimelineLayerGateHost` と同じく、これはタイムライン再生値との整合 (巻き戻り防止) に必要な経路であり、OFF にすると PostEffects 側の操作が毎フレーム巻き戻されて操作不能になるため
- タイムライン未読込・ポストエフェクトレイヤー未登録の状態で触った場合も SceneEditor 自身のウィンドウと同じ扱い (`AutoEditMode.Enter` の既存分岐に従い編集モードへ入る)。タイムラインタブは既存ゲートで無効化されるので影響はエフェクトタブのみ
- 対象は PostEffects.Plugin のみ (ModItemExplorer は対象外)。タイムラインタブに加えエフェクトタブでも 6 系統 (DoF / GTToneMap / パラフィン / 距離フォグ / リムライト / ブルーム) に適用する

## Global Constraints

- プラグイン間連携はリフレクション経由のみ。コンパイル時参照は禁止 (UnityInjector のロード順で型解決が失敗し Mono がキャッシュする)
- 公開済みホスト (`EditorStateHost` / `TimelineLayerGateHost`) のシグネチャは変更禁止。新規ホストは別クラスで追加する
- コードのコメント・ログ文言は日本語
- ビルドは COM3D2 → COM3D25 の順に MSBuild を直接叩く (`debug.bat` はゲームフォルダへ DLL をコピーするため、反映したくないときは使わない)。Git Bash からは `export MSYS2_ARG_CONV_EXCL="*"` が必要
- `deploy.bat` / `deploy.ps1` は実行しない
- git worktree は使わない
- SceneEditor 側の作業ブランチは `feature/timeline-window`、PostEffects 側は `master`

## ファイル構成

| リポジトリ | ファイル | 責務 |
|---|---|---|
| SceneEditor | `source/COM3D2.SceneEditor.Plugin/AutoEditModeHost.cs` (新規) | 外部プラグイン向けの編集モード開始 API。レイヤー名から型を引いて控えも行う |
| SceneEditor | `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj` | Compile 登録 |
| SceneEditor | `source/COM3D2.SceneEditor.Plugin.Tests/AutoEditModeHostContractTests.cs` (新規) | 型名・シグネチャの公開契約を固定 |
| PostEffects | `source/COM3D25.PostEffects.Plugin/MTEUtils/AutoEditModeClient.cs` (新規) | ホストへのリフレクションクライアント。不在なら no-op |
| PostEffects | `source/COM3D25.PostEffects.Plugin/COM3D25.PostEffects.Plugin.csproj` | Compile 登録 |
| PostEffects | `source/COM3D25.PostEffects.Plugin/MainWindow.cs` | 6 系統の描画区間でフックを差す。リセットボタンをフックに乗せる |
| PostEffects | `source/COM3D25.PostEffects.Plugin/Effects/EffectControllerBase.cs` | 追加/削除ボタンをフックに乗せる |

---

### Task 1: SceneEditor 側ホスト `AutoEditModeHost` の追加

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/AutoEditModeHost.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj:282` (`EditorStateHost.cs` の直後)
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/AutoEditModeHostContractTests.cs`

**Interfaces:**
- Consumes: `AutoEditMode.Enter()`、`TimelineLayerGate.RecordEditedLayer(Type, int)`、`MTEP.TimelineManager.instance.GetLayerInfo(string)`、`EditorStateHost.isEditorEnabled`
- Produces: `public static class COM3D2.SceneEditor.Plugin.AutoEditModeHost { public static void Enter(string layerName); }` (Task 2 のクライアントがリフレクションで探す名前)

- [ ] **Step 1: 契約テストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/AutoEditModeHostContractTests.cs`:

```csharp
using System;
using System.Reflection;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// 外部プラグイン向け編集モード開始 API の公開契約を固定する。
    /// MTEUtils の AutoEditModeClient は型名とメソッド名だけを頼りに
    /// リフレクションで探すため、コンパイル時には誰も参照しない。
    /// csproj への登録漏れや改名はビルドを壊さず、実機で黙って無効になるだけなので、
    /// ここで「プラグイン DLL に載っていること」まで含めて検証する
    /// </summary>
    public class AutoEditModeHostContractTests
    {
        private const string HostTypeName = "COM3D2.SceneEditor.Plugin.AutoEditModeHost";

        private static Type GetHostType()
        {
            // 型名で引くのは外部側 (DockingClient.FindHostType) と同じ経路
            return typeof(AutoEditMode).Assembly.GetType(HostTypeName);
        }

        [Fact]
        public void ホスト型がプラグインアセンブリに存在する()
        {
            Assert.True(GetHostType() != null,
                HostTypeName + " がアセンブリにありません (csproj の Compile 登録漏れの可能性)");
        }

        [Fact]
        public void Enter_は_string_1_引数の_public_static_void()
        {
            var method = GetHostType().GetMethod("Enter", BindingFlags.Public | BindingFlags.Static);

            Assert.True(method != null, "Enter が public static で見つかりません");
            Assert.Equal(typeof(void), method.ReturnType);

            var parameters = method.GetParameters();
            Assert.Single(parameters);
            Assert.Equal(typeof(string), parameters[0].ParameterType);
        }
    }
}
```

- [ ] **Step 2: テストが失敗することを確認する**

```bash
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin.Tests
dotnet test --filter "FullyQualifiedName~AutoEditModeHostContractTests" 2>&1 | tail -15
```

期待: 「ホスト型がプラグインアセンブリに存在する」が FAIL (型が無い)。テストプロジェクトはプラグイン DLL を `HintPath` で参照するため、先に一度は COM3D25 構成のビルドが済んでいること (未ビルドなら Step 5 のビルドコマンドを先に流す)。

- [ ] **Step 3: ホストを実装する**

`source/COM3D2.SceneEditor.Plugin/AutoEditModeHost.cs`:

```csharp
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 外部プラグインのウィンドウから編集モードへ自動移行するための公開 API。
    /// MTEUtils の AutoEditModeClient からリフレクションで発見・呼び出しされるため、
    /// クラス名・メソッドシグネチャは公開後変更禁止 (変更時は別名で追加する)。
    /// 契約はプリミティブのみ。レイヤーは Type ではなくクラス名文字列
    /// (例: "PostEffectTimelineLayer") で指定する。
    ///
    /// 契約:
    /// - 値を書く「直前」に呼ぶこと。編集モード外はタイムラインのレイヤーが
    ///   毎フレーム再生値を書き戻すため、書いた後に呼んでも巻き戻る
    /// - 既に編集モードなら再入しない (AutoEditMode.Enter と同じ)
    /// - layerName が既知なら「触ったレイヤー」として控え、SceneEditor 側の
    ///   編集確定時にそのレイヤーをアクティブへ切り替える。未知の名前なら控えない
    /// - SceneEditor の UI が無効の間は何もしない (レイヤーが動いていないため入る意味が無い)
    /// - 連携設定 (linkExternalPlugin) は見ない。TimelineLayerGateHost と同じく
    ///   タイムライン再生値との整合に必要な経路で、OFF にすると外部側の操作が毎フレーム巻き戻る
    /// </summary>
    public static class AutoEditModeHost
    {
        public static void Enter(string layerName)
        {
            if (!EditorStateHost.isEditorEnabled)
            {
                return;
            }

            // Enter 内の RecordEditedLayerFromOpenGate は SceneEditor 側のゲートを見るが、
            // 外部ウィンドウの描画中は開いていないため控えが消える。後から明示的に控え直す
            AutoEditMode.Enter();

            var info = FindLayerInfo(layerName);
            if (info == null)
            {
                return;
            }

            // メイド単位レイヤーは操作対象メイドのスロットで引く (TimelineLayerGateHost と同じ規約)
            var slotNo = info.category == MTEP.TimelineLayerCategory.Maid
                ? MTEP.MaidManager.instance.maidSlotNo
                : 0;
            TimelineLayerGate.RecordEditedLayer(info.layerType, slotNo);
        }

        /// <summary>TimelineManager 未初期化 (タイトル画面など) でも落ちないよう null を許容する</summary>
        private static MTEP.TimelineLayerInfo FindLayerInfo(string layerName)
        {
            var manager = MTEP.TimelineManager.instance;
            if (manager == null || string.IsNullOrEmpty(layerName))
            {
                return null;
            }
            return manager.GetLayerInfo(layerName);
        }
    }
}
```

- [ ] **Step 4: csproj に登録する**

`COM3D2.SceneEditor.Plugin.csproj` の `<Compile Include="EditorStateHost.cs" />` の直後に 1 行追加:

```xml
    <Compile Include="AutoEditModeHost.cs" />
```

- [ ] **Step 5: 両構成をビルドしてテストを通す**

```bash
export MSYS2_ARG_CONV_EXCL="*"
MSB="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin
"$MSB" COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" /v:m /nologo 2>&1 | tail -5
"$MSB" COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" /v:m /nologo 2>&1 | tail -5
cd ../COM3D2.SceneEditor.Plugin.Tests
dotnet test 2>&1 | tail -5
```

期待: 両構成とも「エラー 0」、テスト全件 PASS (AutoEditModeHostContractTests の 2 件を含む)。

- [ ] **Step 6: コミット**

```bash
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin
git add source/COM3D2.SceneEditor.Plugin/AutoEditModeHost.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj source/COM3D2.SceneEditor.Plugin.Tests/AutoEditModeHostContractTests.cs
git commit -m "feat(host): 外部プラグイン向けに編集モード自動移行 API AutoEditModeHost を追加"
```

---

### Task 2: PostEffects 側クライアント `AutoEditModeClient` の追加

**Files:**
- Create: `source/COM3D25.PostEffects.Plugin/MTEUtils/AutoEditModeClient.cs`
- Modify: `source/COM3D25.PostEffects.Plugin/COM3D25.PostEffects.Plugin.csproj:237` (`MTEUtils\TimelineLayerGateClient.cs` の直後)

**Interfaces:**
- Consumes: `DockingClient.FindHostType(string)` (internal、同アセンブリ内)、Task 1 のホスト `AutoEditModeHost.Enter(string)`
- Produces: `public static class COM3D2.MotionTimelineEditor.AutoEditModeClient { public static bool isAvailable { get; } public static void Enter(string layerName); }`

- [ ] **Step 1: クライアントを実装する**

`source/COM3D25.PostEffects.Plugin/MTEUtils/AutoEditModeClient.cs`:

```csharp
using System;
using System.Reflection;

namespace COM3D2.MotionTimelineEditor
{
    /// <summary>
    /// SceneEditor の AutoEditModeHost へリフレクションで接続するクライアント。
    /// 外部プラグインのウィンドウで値を書く直前に呼び、SceneEditor 側の編集モードへ入れる。
    /// 編集モード外はタイムラインのレイヤーが毎フレーム再生値を書き戻すため、
    /// 先に入っておかないと変更が巻き戻る。
    ///
    /// 契約 (ホスト側 AutoEditModeHost と対):
    /// - Enter は値を書く「直前」に呼ぶ。既に編集モードならホスト側で無視される
    /// - layerName は SceneEditor 側レイヤーのクラス名 (例: "PostEffectTimelineLayer")。
    ///   ホストはこれを「触ったレイヤー」として控え、編集確定時にアクティブへ切り替える
    /// - SceneEditor 不在なら何もしない。ホスト型が見つかるまでは呼び出しのたびに探し直す
    ///   (OnGUI から呼ばれる前提なので再試行のタイマーは持たない)。
    ///   型は見つかったがシグネチャが合わない場合のみ恒久的に無効
    /// </summary>
    public static class AutoEditModeClient
    {
        private static Action<string> _enter;
        private static bool _initialized;

        public static bool isAvailable
        {
            get
            {
                Initialize();
                return _enter != null;
            }
        }

        private static void Initialize()
        {
            if (_initialized)
            {
                return;
            }

            // ホストがまだロードされていなければ _initialized を立てずに戻り、次回呼び出しで再試行する
            var type = DockingClient.FindHostType("AutoEditModeHost");
            if (type == null)
            {
                return;
            }

            // ここから先はホストの型は見つかっている。シグネチャ不一致は
            // バージョン差による恒久的な問題なので、この場合のみ無効へ確定する
            _initialized = true;

            try
            {
                var enter = type.GetMethod("Enter", BindingFlags.Public | BindingFlags.Static);
                if (enter == null)
                {
                    MTEUtils.LogWarning(
                        "AutoEditModeClient: AutoEditModeHost にシグネチャの一致するメンバーが見つかりませんでした");
                    return;
                }

                _enter = (Action<string>) Delegate.CreateDelegate(typeof(Action<string>), enter);
            }
            catch (Exception e)
            {
                // ホスト側のバージョン差でシグネチャが合わない場合は連動なしで動作する
                MTEUtils.LogWarning(
                    "AutoEditModeClient: AutoEditModeHost との接続に失敗しました: " + e.Message);
                _enter = null;
            }
        }

        /// <summary>値を書く直前に呼ぶ。SceneEditor 不在なら何もしない</summary>
        public static void Enter(string layerName)
        {
            if (isAvailable)
            {
                _enter(layerName);
            }
        }
    }
}
```

- [ ] **Step 2: csproj に登録する**

`COM3D25.PostEffects.Plugin.csproj` の `<Compile Include="MTEUtils\TimelineLayerGateClient.cs" />` の直後に 1 行追加:

```xml
    <Compile Include="MTEUtils\AutoEditModeClient.cs" />
```

- [ ] **Step 3: ビルドが通ることを確認する**

```bash
export MSYS2_ARG_CONV_EXCL="*"
MSB="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
cd /w/COM3D2_5/work/COM3D2.PostEffects.Plugin/source/COM3D25.PostEffects.Plugin
"$MSB" COM3D25.PostEffects.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" /v:m /nologo 2>&1 | tail -5
"$MSB" COM3D25.PostEffects.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" /v:m /nologo 2>&1 | tail -5
```

期待: 両構成とも「エラー 0」。プロパティ名が SceneEditor 側と違う場合は `build.bat` を読んで合わせる (PostEffects の `build.bat` は同じ MSBuild パスを使っている)。

- [ ] **Step 4: コミット**

```bash
cd /w/COM3D2_5/work/COM3D2.PostEffects.Plugin
git add source/COM3D25.PostEffects.Plugin/MTEUtils/AutoEditModeClient.cs source/COM3D25.PostEffects.Plugin/COM3D25.PostEffects.Plugin.csproj
git commit -m "feat(mteutils): SceneEditor の編集モードへ入る AutoEditModeClient を追加"
```

---

### Task 3: PostEffects ウィンドウの値変更を編集モード自動移行に乗せる

**Files:**
- Modify: `source/COM3D25.PostEffects.Plugin/MainWindow.cs` (`DrawEffectContent` 207-240 行、`DrawTimelineContent` 247-273 行、`InitTimelineControllers` 302-314 行、`DrawEffectRow` 333-366 行)
- Modify: `source/COM3D25.PostEffects.Plugin/Effects/EffectControllerBase.cs:93-104` (`DrawDataTabs` の追加/削除ボタン)

**Interfaces:**
- Consumes: Task 2 の `AutoEditModeClient.Enter(string)`、`GUIView.onBeforeValueChanged` / `GUIView.NotifyBeforeValueChanged()`、既存定数 `POST_EFFECT_LAYER_NAME`
- Produces: なし (UI の挙動変更のみ)

- [ ] **Step 1: `DrawDataTabs` の追加/削除ボタンをフックに乗せる**

`Effects/EffectControllerBase.cs` の `DrawDataTabs` 内、2 つの `DrawButton` ブロックを次に置き換える:

```csharp
                // DrawButton は onBeforeValueChanged を通さないため、
                // データを書き換える直前に自分で通す (編集モードへの自動移行用)
                if (view.DrawButton("追加", 60, 20, count < maxCount))
                {
                    view.NotifyBeforeValueChanged();
                    s.AddData(createData());
                    dataIndex = s.GetDataCount() - 1;
                    SetDirty();
                }
                if (view.DrawButton("削除", 60, 20, count > 0))
                {
                    view.NotifyBeforeValueChanged();
                    s.RemoveData(dataIndex);
                    SetDirty();
                }
```

- [ ] **Step 2: `MainWindow` にフック用のデリゲートと判定を持たせる**

`MainWindow.cs` の `_timelineControllers` フィールド定義 (51 行付近) の直後に追加:

```csharp
        // タイムライン対応コントローラの値を書く直前に SceneEditor の編集モードへ入る。
        // 毎フレーム描画で使うため、クロージャを都度生成せず 1 本を使い回す
        private readonly Action _enterAutoEditMode =
            () => AutoEditModeClient.Enter(POST_EFFECT_LAYER_NAME);

        /// <summary>
        /// タイムラインが駆動する 6 系統か。エフェクトタブで触ったときも
        /// 編集モードへ入らないと再生値に巻き戻されるため、タブに関わらずこれで判定する
        /// </summary>
        private bool IsTimelineDriven(EffectControllerBase controller)
        {
            if (_timelineControllers.Count == 0)
            {
                InitTimelineControllers();
            }
            return _timelineControllers.Contains(controller);
        }
```

`POST_EFFECT_LAYER_NAME` は 58 行で `private const string` として定義済みなので、フィールド初期化子から参照できる。ファイル先頭の `using System;` は既にある。

- [ ] **Step 3: `DrawEffectRow` でフックを差し、リセットもフックに乗せる**

`MainWindow.cs` の `DrawEffectRow` 全体を次に置き換える:

```csharp
        private void DrawEffectRow(GUIView view, EffectControllerBase controller)
        {
            // タイムライン対応の系統は、この行と設定項目の値変更で編集モードへ自動移行する。
            // サブビュー (スクロール内のレイアウト) は親のフックを引き継ぐ
            var isTimelineDriven = IsTimelineDriven(controller);
            if (isTimelineDriven)
            {
                view.onBeforeValueChanged = _enterAutoEditMode;
            }

            try
            {
                view.BeginHorizontal();
                {
                    // 行のチェックボックスが有効トグルそのもの。ON で下に設定項目を展開する
                    view.DrawToggle(controller.effectName, controller.effectEnabled,
                        view.viewRect.width - 90, 20, value =>
                    {
                        controller.effectEnabled = value;
                        settings.dirty = true;
                    });

                    if (controller.effectEnabled)
                    {
                        view.currentPos.x = view.viewRect.width - 80;
                        if (view.DrawButton("リセット", 60, 20))
                        {
                            // DrawButton はフックを通さないため、書き換える直前に自分で通す
                            view.NotifyBeforeValueChanged();
                            controller.ResetSetting();
                        }
                    }
                }
                view.EndLayout();

                if (controller.effectEnabled)
                {
                    // 設定項目を左右にインデントして、行との親子関係を見せる
                    var savedPadding = view.padding;
                    view.padding = new Vector2(savedPadding.x + 15, savedPadding.y);
                    controller.DrawContent(view);
                    view.padding = savedPadding;
                }

                view.DrawHorizontalLine(Color.gray);
            }
            finally
            {
                // 次の行 (タイムライン非対応の系統) へフックを持ち越さない
                if (isTimelineDriven)
                {
                    view.onBeforeValueChanged = null;
                }
            }
        }
```

- [ ] **Step 4: カテゴリリセットもフックに乗せる**

`DrawEffectContent` 内のリセットボタンを次に置き換える:

```csharp
                view.currentPos.x = view.viewRect.width - 80;
                if (view.DrawButton("リセット", 60, 20))
                {
                    var category = _categoryComboBox.currentItem;
                    // カテゴリ内にタイムライン対応の系統があれば、書き換える前に編集モードへ入る
                    if (HasTimelineDrivenController(category))
                    {
                        AutoEditModeClient.Enter(POST_EFFECT_LAYER_NAME);
                    }
                    ResetCategory(category);
                }
```

`IsTimelineDriven` の直後にヘルパーを追加:

```csharp
        private bool HasTimelineDrivenController(EffectCategory category)
        {
            foreach (var controller in postEffectManager.controllers)
            {
                if (controller.category == category && IsTimelineDriven(controller))
                {
                    return true;
                }
            }
            return false;
        }
```

- [ ] **Step 5: 両構成をビルドする**

```bash
export MSYS2_ARG_CONV_EXCL="*"
MSB="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
cd /w/COM3D2_5/work/COM3D2.PostEffects.Plugin/source/COM3D25.PostEffects.Plugin
"$MSB" COM3D25.PostEffects.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" /v:m /nologo 2>&1 | tail -5
"$MSB" COM3D25.PostEffects.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" /v:m /nologo 2>&1 | tail -5
```

期待: 両構成とも「エラー 0」。

- [ ] **Step 6: コミット**

```bash
cd /w/COM3D2_5/work/COM3D2.PostEffects.Plugin
git add source/COM3D25.PostEffects.Plugin/MainWindow.cs source/COM3D25.PostEffects.Plugin/Effects/EffectControllerBase.cs
git commit -m "feat(window): タイムライン対応 6 系統の値変更で SceneEditor の編集モードへ自動移行する"
```

---

### Task 4: 実機検証

**Files:** なし (検証のみ)

**Interfaces:**
- Consumes: MCP `com3d25-devbridge` (`ping` / `eval_csharp` / `screenshot`)

- [ ] **Step 1: ゲームが起動していれば新 DLL を反映する**

ゲーム停止中なら各リポジトリで `debug.bat com3d25` を実行して DLL をゲームフォルダへコピーし、ゲームを起動する。起動中なら `ping` で死活確認のうえ、ユーザーにゲーム再起動を依頼する (PostEffects の `MainWindow` は MonoBehaviour 配下でホットリロード不可)。

- [ ] **Step 2: 接続を確認する**

`eval_csharp`:

```csharp
var t = System.AppDomain.CurrentDomain.GetAssemblies()
    .Where(a => a.GetName().Name == "COM3D25.PostEffects.Plugin")
    .Select(a => a.GetType("COM3D2.MotionTimelineEditor.AutoEditModeClient")).FirstOrDefault(x => x != null);
t.GetProperty("isAvailable").GetValue(null, null)
```

期待: `True`。

- [ ] **Step 3: 巻き戻りが解消したことを確認する**

1. SceneEditor でポストエフェクトレイヤーを含むタイムラインを読み込み、再生状態 (編集モード OFF) にする
2. PostEffects ウィンドウのタイムラインタブでパラフィンの強度スライダーを動かす
3. `eval_csharp` で `COM3D2.MotionTimelineEditor.Plugin.SceneEditorHack.isPoseEditing` を読む

期待: `True` (編集モードへ入り再生が止まる)。スライダーの値が次フレームで元に戻らない。

4. エフェクトタブでブルームの値を動かしても同様に `isPoseEditing == true` になる
5. エフェクトタブでタイムライン非対応の系統 (例: ビネット) を動かしても編集モードへは入らない (先に F1 で編集モードを抜けてから確認)
6. パラフィンの「追加」「削除」、行の「リセット」、カテゴリ「リセット」でも編集モードへ入る
7. SceneEditor のタイムラインウィンドウで編集を確定したとき、アクティブレイヤーがポストエフェクトレイヤーへ切り替わる (`TimelineLayerGate.RecordEditedLayer` の控えが効いている)

- [ ] **Step 4: 色欄の経路を確認する**

パラフィンの色 1 について「編集」ボタンからピッカーで色を変える場合と「R」ボタンで戻す場合の両方で `isPoseEditing == true` になることを確認する (どちらも `DrawColor` 内で `NotifyBeforeValueChanged()` を通る設計)。

---

## Self-Review

- **Spec coverage**: ホスト追加 (Task 1)、クライアント追加 (Task 2)、タイムラインタブ + エフェクトタブの 6 系統への適用とボタン経路 (Task 3)、レイヤー控え (Task 1 の `RecordEditedLayer`)、実機確認 (Task 4) を網羅
- **Placeholder scan**: なし
- **Type consistency**: `AutoEditModeHost.Enter(string)` ↔ `AutoEditModeClient.Enter(string)` ↔ `_enterAutoEditMode` の引数 `POST_EFFECT_LAYER_NAME` で一致。`IsTimelineDriven` / `HasTimelineDrivenController` は Task 3 内で定義と使用が閉じている

## レビュー却下メモ

- `AutoEditModeHost.Enter` が `isLinkEnabled` を見ていない — 意図的。`TimelineLayerGateHost` と同じく巻き戻り防止に必須の経路で、連携設定で止めると外部側が操作不能になる。Spec とホストのコメントに明記した
- `InitTimelineControllers` の遅延初期化がエフェクトタブで行数ぶん空振りする — 既存 `DrawTimelineContent` と同じパターンで、起動直後の数フレームに限られるため見送り (未確認のまま見送り)
- 契約テストがオーバーロード追加時の `AmbiguousMatchException` を検出しない — 既存 `TimelineLayerGateHostContractTests` と同水準で許容
- タイムライン未読込時に `AutoEditMode.Enter` が走る — SceneEditor 自身のウィンドウと同じ挙動。Spec に明記した

## コードレビュー却下メモ

- `DrawColor` の「R」ボタンがフックを通らない (機能面 🔴) — 誤検知。`ColorFieldCache.ResetColor` は同フレームで `_color` を書き換え、`DrawColor` 末尾の `fieldCache.color != color` 判定 (`GUIView.cs:3240-3245`) で `NotifyBeforeValueChanged()` → `onColorChanged` の順に通る
- `_timelineControllers` の空判定を初期化済みフラグへ置き換える (機能面 🟡) — 既存 `InitTimelineControllers` の「登録前なら次回描画で再試行する」意図を壊すため見送り。重複していた判定は `EnsureTimelineControllers` へ集約した
- `HasTimelineDrivenController` の線形走査 (可読性 🟢) — ボタン押下時のみの経路で実害なし
- 編集モード継続中に新規登録されたレイヤーはスナップショット欠如で切り替わらない (機能面 🟡、レイヤー追従の追加分) — レイヤー追加は `ChangeActiveLayer` → `SetCurrentLayer` で編集セッションが張り直されスナップショットが揃うため、既存の追加経路では起きない。該当ケースは `FocusLayerKeepingEdit` の LogDebug で可視化した
- `AutoEditModeHost` が `TimelineWindow` の static を直接呼ぶ結合 (機能面 🟢) — 既存 `FocusEditedLayer` と同じ構造。`TimelineManager` への移設は別リファクタ
