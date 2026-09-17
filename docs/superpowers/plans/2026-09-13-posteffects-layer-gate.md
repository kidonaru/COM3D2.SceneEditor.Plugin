# PostEffects.Plugin へのレイヤーゲート連携 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** SceneEditor のタイムライン読込中にポストエフェクトレイヤーが未登録なら、PostEffects.Plugin のメインウィンドウ「タイムライン」モードに注意文と「レイヤーを追加」ボタンを出し、項目を無効表示にする。

**Architecture:** SceneEditor 側に文字列キーの公開ホスト `TimelineLayerGateHost` を追加し、共有 submodule MTEUtils に `TimelineLayerGateClient`（リフレクション接続、`DockingClient.FindHostType` 経由）を追加する。PostEffects 側は薄いドロワー `TimelineLayerGateDrawer` でクライアントに状態と文言を問い合わせ、`GUIView.forceDisabled` で無効化する。SceneEditor 不在・タイムライン未読込時はクライアントが `NoTimeline` を返し、PostEffects の表示は従来通りになる。

**Tech Stack:** C#（COM3D2 = .NET 3.5 / COM3D25 = .NET 4.7.1 の 2 構成、両リポジトリとも）、Unity IMGUI、リフレクション（`Delegate.CreateDelegate`）、xunit（SceneEditor 側のみ）

**Spec:** 本計画の「要件」節（前回の計画 `2026-09-13-window-layer-gate.md` の延長）

## 要件

- 対象は PostEffects メインウィンドウの「タイムライン」モードのみ。「エフェクト」「プリセット」モードは変更しない（タイムラインが駆動する対象を編集する場所がタイムラインモードのため）
- モード切替ボタン・タイムライン内のエフェクトタブは無効化しない（SceneEditor 側と同じ「タブを描いた後に Begin」の規約）
- 追加ボタンは SceneEditor 側の `ChangeActiveLayer` に委譲し、アクティブレイヤー切替・履歴登録は SceneEditor 任せ
- SceneEditor が未ロード／タイムライン未読込のときは PostEffects の表示・操作性を一切変えない
- プラグイン間はコンパイル時参照禁止。既存の `EditorStateHost` / `EditorStateClient` の作法に従う

## Global Constraints

- コメント・ログ文言は日本語
- 両リポジトリとも COM3D2 / COM3D25 の 2 構成でビルドを通す。`Func<>`/`Action<>` は入力 4 個まで
- `debug.bat` / `deploy.bat` は実行しない（両リポジトリ）。PostEffects の `build.bat` もゲームフォルダへコピーするので使わず、MSBuild を直接叩く
- ホストの契約はプリミティブ＋デリゲートのみ。公開後シグネチャ変更禁止
- ロード順は `COM3D2.SceneEditor.Plugin.dll` → `COM3D25.PostEffects.Plugin.dll`（`.` < `5`）。PostEffects がクライアント側なので通常はホスト型が見つかるが、SceneEditor 不在環境のために「型が見つかるまで毎回探し直す」方式を採る（`PostEffectsClient` と同じ）
- MTEUtils submodule の新コミット `b619f30`（`forceDisabled`）は **origin 未プッシュ**。PostEffects 側の submodule は GitHub ではなく SceneEditor 側のローカル submodule から fetch する。プッシュはユーザー操作に委ねる

## リポジトリとパス

| 略称 | パス |
|---|---|
| B (SceneEditor) | `W:\COM3D2_5\work\COM3D2.SceneEditor.Plugin`、ソース `source/COM3D2.SceneEditor.Plugin/` |
| B-MTEUtils | `W:\COM3D2_5\work\COM3D2.SceneEditor.Plugin\source\COM3D2.SceneEditor.Plugin\MTEUtils`（submodule、master、HEAD `b619f30`） |
| A (PostEffects) | `W:\COM3D2_5\work\COM3D2.PostEffects.Plugin`、ソース `source/COM3D25.PostEffects.Plugin/`（名前空間 `COM3D25.PostEffects.Plugin`） |
| A-MTEUtils | `W:\COM3D2_5\work\COM3D2.PostEffects.Plugin\source\COM3D25.PostEffects.Plugin\MTEUtils`（submodule、HEAD `009d22b`、2 コミット遅れ） |

## ビルド・テストコマンド（Git Bash）

B:

```bash
export MSYS2_ARG_CONV_EXCL="*"
MSB="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin
"$MSB" COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2  "/p:COM3D2_DIR=W:\COM3D2" /v:m /nologo \
&& "$MSB" COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" /v:m /nologo \
&& cd ../COM3D2.SceneEditor.Plugin.Tests && dotnet test --nologo -v q
```

A（テストプロジェクトは無い）:

```bash
export MSYS2_ARG_CONV_EXCL="*"
MSB="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
cd /w/COM3D2_5/work/COM3D2.PostEffects.Plugin/source/COM3D25.PostEffects.Plugin
"$MSB" COM3D25.PostEffects.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2  "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" /v:m /nologo \
&& "$MSB" COM3D25.PostEffects.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" /v:m /nologo
```

A の csproj のプロパティ名が `COM3D2_DIR` / `COM3D25_DIR` でない場合は `grep -n 'COM3D2.*_DIR' COM3D25.PostEffects.Plugin.csproj` で確認して合わせる。

## ファイル構成

| ファイル | 役割 |
|---|---|
| Create (B): `source/COM3D2.SceneEditor.Plugin/TimelineLayerGateHost.cs` | 文字列キーの公開 API。`GetState` / `GetNoticeText` / `GetAddButtonText` / `AddLayer` |
| Modify (B): `source/COM3D2.SceneEditor.Plugin.Tests/TimelineLayerGateTextTests.cs` | enum の数値契約テストを追加 |
| Create (B-MTEUtils): `TimelineLayerGateClient.cs` | ホストをリフレクションで発見・呼出。未接続時は `NoTimeline`(0) を返す |
| Modify (A): submodule ポインタ | B-MTEUtils の新コミットへ更新（`forceDisabled` とクライアントを取り込む） |
| Create (A): `source/COM3D25.PostEffects.Plugin/TimelineLayerGateDrawer.cs` | `Begin(view, layerName, rowHeight)` / `End(view)`。SceneEditor 側 `TimelineLayerGate` の描画部分をクライアント経由で再現 |
| Modify (A): `source/COM3D25.PostEffects.Plugin/MainWindow.cs:130-138, 213-238` | `DrawContent` に try/finally、`DrawTimelineContent` のタブ後に `Begin` |

---

### Task 1: SceneEditor 側ホスト `TimelineLayerGateHost`（B）

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/TimelineLayerGateHost.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin.Tests/TimelineLayerGateTextTests.cs`

**Interfaces:**
- Consumes: `MTEP.TimelineManager.instance.timeline / GetLayerInfo(string) / GetLayer(Type,int) / ChangeActiveLayer(Type,int)`、`TimelineLayerGateText`、`TimelineLayerGateState`
- Produces（公開後変更禁止）:
  - `public static int GetState(string layerName)` — `(int)TimelineLayerGateState`。0=NoTimeline, 1=MaidNotFound, 2=Missing, 3=Ready。未知のレイヤー名は 0
  - `public static string GetNoticeText(string layerName)`
  - `public static string GetAddButtonText(string layerName)`
  - `public static void AddLayer(string layerName)` — 未知のレイヤー名・未読込時は何もしない

- [ ] **Step 1: enum の数値契約テストを追加（失敗を確認）**

`TimelineLayerGateTextTests.cs` のクラス末尾に追加:

```csharp
        [Fact]
        public void 状態の数値は外部プラグインとの契約なので固定()
        {
            // TimelineLayerGateHost.GetState が int で返す値。MTEUtils の
            // TimelineLayerGateClient がこの数値で解釈するため、並び替え・挿入は禁止
            Assert.Equal(0, (int)TimelineLayerGateState.NoTimeline);
            Assert.Equal(1, (int)TimelineLayerGateState.MaidNotFound);
            Assert.Equal(2, (int)TimelineLayerGateState.Missing);
            Assert.Equal(3, (int)TimelineLayerGateState.Ready);
        }
```

このテストは現状の enum 定義（宣言順 0..3）で最初から通る。契約の固定が目的なので、Step 2 のビルドで PASS を確認するだけでよい。

- [ ] **Step 2: ホストを実装**

`source/COM3D2.SceneEditor.Plugin/TimelineLayerGateHost.cs`:

```csharp
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// ウィンドウ内レイヤーゲートの状態を外部プラグインへ公開する API。
    /// MTEUtils の TimelineLayerGateClient からリフレクションで発見・呼び出しされるため、
    /// クラス名・メソッドシグネチャは公開後変更禁止 (変更時は別名で追加する)。
    /// 契約はプリミティブのみ。レイヤーは Type ではなくクラス名文字列
    /// (例: "PostEffectTimelineLayer") で指定する。
    ///
    /// 契約:
    /// - GetState は TimelineLayerGateState を int で返す (0=未読込, 1=メイド不在, 2=未登録, 3=登録済み)。
    ///   enum の並びは契約なので変更禁止 (TimelineLayerGateTextTests で固定)
    /// - 未知のレイヤー名・タイムライン未読込では 0 を返し、AddLayer は何もしない。
    ///   外部側はこのとき従来表示のままにする。
    ///   PostEffectTimelineLayer は PostEffectsBridge 接続後に登録される (TimelineIntegration.TryRegisterPostEffects)
    ///   ため、起動直後の数フレームは登録済みでも 0 になる。一時的なもので不具合ではない
    /// - メイド単位レイヤー (hasSlotNo) は対象外。slotNo は常に 0
    /// - AddLayer は ChangeActiveLayer に委譲し、アクティブレイヤー切替と履歴登録もそちらで行う
    /// </summary>
    public static class TimelineLayerGateHost
    {
        private static MTEP.TimelineManager timelineManager => MTEP.TimelineManager.instance;

        public static int GetState(string layerName)
        {
            var info = FindLayerInfo(layerName);
            if (info == null)
            {
                return (int) TimelineLayerGateState.NoTimeline;
            }

            var timelineLoaded = timelineManager.timeline != null;
            var layerExists = timelineLoaded && timelineManager.GetLayer(info.layerType, 0) != null;
            return (int) TimelineLayerGateText.Resolve(timelineLoaded, false, false, layerExists);
        }

        public static string GetNoticeText(string layerName)
        {
            return TimelineLayerGateText.NoticeText(GetDisplayName(layerName));
        }

        public static string GetAddButtonText(string layerName)
        {
            return TimelineLayerGateText.AddButtonText(GetDisplayName(layerName));
        }

        public static void AddLayer(string layerName)
        {
            var info = FindLayerInfo(layerName);
            if (info == null || timelineManager.timeline == null)
            {
                return;
            }
            timelineManager.ChangeActiveLayer(info.layerType, 0);
        }

        private static string GetDisplayName(string layerName)
        {
            var info = FindLayerInfo(layerName);
            return info != null ? info.displayName : layerName;
        }

        /// <summary>TimelineManager 未初期化 (プラグイン無効時など) でも落ちないよう null を許容する</summary>
        private static MTEP.TimelineLayerInfo FindLayerInfo(string layerName)
        {
            var manager = timelineManager;
            if (manager == null || string.IsNullOrEmpty(layerName))
            {
                return null;
            }
            return manager.GetLayerInfo(layerName);
        }
    }
}
```

`TimelineManager.instance` は既存の `TimelineLayerGate.cs` が try/catch なしで直接使っている実績があるので、例外対策は不要。`FindLayerInfo` の null チェックだけで足りる。

- [ ] **Step 3: B の両構成ビルド＋テスト**

Expected: エラー 0、`状態の数値は外部プラグインとの契約なので固定` を含め全 PASS。

- [ ] **Step 4: コミット**

```bash
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin
git add source/COM3D2.SceneEditor.Plugin/TimelineLayerGateHost.cs source/COM3D2.SceneEditor.Plugin.Tests/TimelineLayerGateTextTests.cs
git commit -m "feat(timeline): レイヤーゲートの状態を外部プラグインへ公開する TimelineLayerGateHost を追加する"
```

---

### Task 2: 共有クライアント `TimelineLayerGateClient`（B-MTEUtils submodule）

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/MTEUtils/TimelineLayerGateClient.cs`

**Interfaces:**
- Consumes: `DockingClient.FindHostType(string)`（internal、同アセンブリ）、Task 1 のホスト 4 メソッド
- Produces（namespace `COM3D2.MotionTimelineEditor`）:
  - `public static bool isAvailable`
  - `public static int GetState(string layerName)` — 未接続なら 0
  - `public static string GetNoticeText(string layerName)` / `GetAddButtonText(string layerName)` — 未接続なら空文字
  - `public static void AddLayer(string layerName)` — 未接続なら何もしない

- [ ] **Step 1: 実装**

```csharp
using System;
using System.Reflection;

namespace COM3D2.MotionTimelineEditor
{
    /// <summary>
    /// SceneEditor の TimelineLayerGateHost へリフレクションで接続するクライアント。
    /// 外部プラグインのウィンドウが「タイムライン読込中にレイヤー未登録」の状態を
    /// 問い合わせ、注意文言と追加ボタンを出すために使う。
    ///
    /// 契約 (ホスト側 TimelineLayerGateHost と対):
    /// - GetState は 0=未読込, 1=メイド不在, 2=未登録, 3=登録済み。SceneEditor 不在なら 0
    /// - ホスト型が見つかるまでは呼び出しのたびに探し直す (OnGUI から毎フレーム呼ばれる前提なので
    ///   再試行のタイマーは持たない)。型は見つかったがシグネチャが合わない場合のみ恒久的に無効
    /// </summary>
    public static class TimelineLayerGateClient
    {
        public const int StateNoTimeline = 0;
        public const int StateMaidNotFound = 1;
        public const int StateMissing = 2;
        public const int StateReady = 3;

        private static Func<string, int> _getState;
        private static Func<string, string> _getNoticeText;
        private static Func<string, string> _getAddButtonText;
        private static Action<string> _addLayer;
        private static bool _initialized;

        public static bool isAvailable
        {
            get
            {
                Initialize();
                return _getState != null;
            }
        }

        private static void Initialize()
        {
            if (_initialized)
            {
                return;
            }

            // ホストがまだロードされていなければ _initialized を立てずに戻り、次回呼び出しで再試行する
            var type = DockingClient.FindHostType("TimelineLayerGateHost");
            if (type == null)
            {
                return;
            }

            // ここから先はホストの型は見つかっている。シグネチャ不一致は
            // バージョン差による恒久的な問題なので、この場合のみ無効へ確定する
            _initialized = true;

            try
            {
                var flags = BindingFlags.Public | BindingFlags.Static;
                var getState = type.GetMethod("GetState", flags);
                var getNoticeText = type.GetMethod("GetNoticeText", flags);
                var getAddButtonText = type.GetMethod("GetAddButtonText", flags);
                var addLayer = type.GetMethod("AddLayer", flags);
                if (getState == null || getNoticeText == null || getAddButtonText == null || addLayer == null)
                {
                    MTEUtils.LogWarning(
                        "TimelineLayerGateClient: TimelineLayerGateHost にシグネチャの一致するメンバーが見つかりませんでした");
                    return;
                }

                _getState = (Func<string, int>) Delegate.CreateDelegate(typeof(Func<string, int>), getState);
                _getNoticeText = (Func<string, string>) Delegate.CreateDelegate(typeof(Func<string, string>), getNoticeText);
                _getAddButtonText = (Func<string, string>) Delegate.CreateDelegate(typeof(Func<string, string>), getAddButtonText);
                _addLayer = (Action<string>) Delegate.CreateDelegate(typeof(Action<string>), addLayer);
            }
            catch (Exception e)
            {
                // ホスト側のバージョン差でシグネチャが合わない場合はゲート無しで動作する
                MTEUtils.LogWarning(
                    "TimelineLayerGateClient: TimelineLayerGateHost との接続に失敗しました: " + e.Message);
                _getState = null;
                _getNoticeText = null;
                _getAddButtonText = null;
                _addLayer = null;
            }
        }

        /// <summary>レイヤーの登録状態。SceneEditor 不在・タイムライン未読込は StateNoTimeline</summary>
        public static int GetState(string layerName)
        {
            return isAvailable ? _getState(layerName) : StateNoTimeline;
        }

        public static string GetNoticeText(string layerName)
        {
            return isAvailable ? _getNoticeText(layerName) : "";
        }

        public static string GetAddButtonText(string layerName)
        {
            return isAvailable ? _getAddButtonText(layerName) : "";
        }

        /// <summary>レイヤーを追加し SceneEditor 側のアクティブレイヤーも切り替える</summary>
        public static void AddLayer(string layerName)
        {
            if (isAvailable)
            {
                _addLayer(layerName);
            }
        }
    }
}
```

`MTEUtils.LogWarning` の実在は `grep -n 'public static void LogWarning' MTEUtils/MTEUtils.cs` で確認する（`EditorStateClient` が使っているので存在するはず）。

- [ ] **Step 2: B の両構成ビルド＋テスト**

MTEUtils のファイルが csproj のワイルドカードで拾われることを確認（`grep -n 'MTEUtils' COM3D2.SceneEditor.Plugin.csproj`）。個別列挙なら `TimelineLayerGateClient.cs` を追加する。Expected: エラー 0、全 PASS。

- [ ] **Step 3: submodule でコミット**

```bash
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin/MTEUtils
git add TimelineLayerGateClient.cs
git commit -m "feat(client): SceneEditor のレイヤーゲートへ接続する TimelineLayerGateClient を追加する"
git log --oneline -1   # このハッシュを Task 4 で使う
```

- [ ] **Step 4: 親リポジトリ (B) でポインタ更新をコミット**

```bash
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin
git add source/COM3D2.SceneEditor.Plugin/MTEUtils
git commit -m "chore(submodule): MTEUtils を更新（TimelineLayerGateClient）"
```

---

### Task 3: PostEffects 側の MTEUtils submodule を更新（A）

**Files:**
- Modify: A の submodule ポインタ `source/COM3D25.PostEffects.Plugin/MTEUtils`

- [ ] **Step 1: B のローカル submodule から fetch して checkout**

origin (GitHub) には `b619f30` 以降が無いので、B 側のローカル作業ツリーを一時リモートとして使う:

```bash
cd /w/COM3D2_5/work/COM3D2.PostEffects.Plugin/source/COM3D25.PostEffects.Plugin/MTEUtils
git status -sb            # 変更が無いことを確認
git rev-parse HEAD        # 元のコミット (009d22b) を控える。失敗時は git checkout -B master <このハッシュ> で戻す
git log --oneline origin/master..HEAD   # 空であること (A 側独自コミットが無い)
git fetch /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin/MTEUtils master
git checkout -B master FETCH_HEAD
git log --oneline -3      # 先頭が Task 2 のコミット、その下に b619f30 があること
grep -c forceDisabled GUIView.cs   # 1 以上
```

- [ ] **Step 2: A の csproj にクライアントを登録**

A の csproj は非 SDK 形式で MTEUtils 配下を `<Compile Include="MTEUtils\XXX.cs" />` と個別列挙している（ワイルドカードではない）。登録しないとファイルがあってもコンパイルされず、Task 4 で初めて型未解決になる。`EditorStateClient.cs` の行の隣に追加:

```xml
    <Compile Include="MTEUtils\TimelineLayerGateClient.cs" />
```

```bash
grep -n 'MTEUtils\EditorStateClient.cs\|MTEUtils\TimelineLayerGateClient.cs' /w/COM3D2_5/work/COM3D2.PostEffects.Plugin/source/COM3D25.PostEffects.Plugin/COM3D25.PostEffects.Plugin.csproj
```

- [ ] **Step 3: A の両構成ビルド**

「ビルド・テストコマンド」の A を実行。Expected: エラー 0。

- [ ] **Step 4: A でポインタ更新と csproj をコミット**

```bash
cd /w/COM3D2_5/work/COM3D2.PostEffects.Plugin
git add source/COM3D25.PostEffects.Plugin/MTEUtils source/COM3D25.PostEffects.Plugin/COM3D25.PostEffects.Plugin.csproj
git commit -m "chore(submodule): MTEUtils を更新（GUIView.forceDisabled / TimelineLayerGateClient）"
```

注意: A の `.gitmodules` の URL は GitHub。ユーザーが B-MTEUtils を `git push origin master` するまで、他環境で A を clone しても submodule が解決できない。完了報告で明記する。

---

### Task 4: PostEffects 側ドロワーとメインウィンドウへの組み込み（A）

**Files:**
- Create: `source/COM3D25.PostEffects.Plugin/TimelineLayerGateDrawer.cs`
- Modify: `source/COM3D25.PostEffects.Plugin/MainWindow.cs:130-138`（`DrawContent`）、`:213-238`（`DrawTimelineContent`）

**Interfaces:**
- Consumes: `TimelineLayerGateClient`（Task 2）、`GUIView.forceDisabled`
- Produces:
  - `static void TimelineLayerGateDrawer.Begin(GUIView view, string layerName, float rowHeight)`
  - `static void TimelineLayerGateDrawer.End(GUIView view)`（冪等）

- [ ] **Step 1: ドロワーを実装**

`source/COM3D25.PostEffects.Plugin/TimelineLayerGateDrawer.cs`:

```csharp
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D25.PostEffects.Plugin
{
    /// <summary>
    /// SceneEditor のウィンドウ内レイヤーゲートを本プラグインのウィンドウで再現する。
    /// タイムライン読込中にレイヤー未登録なら注意文と追加ボタンを描き、以降の項目を強制無効にする。
    /// 判定・文言・追加処理は TimelineLayerGateClient 経由で SceneEditor 側に任せる。
    /// SceneEditor 不在・タイムライン未読込では何も描かず従来表示のまま。
    ///
    /// Begin で無効化したら同じ描画パス内で必ず End を呼ぶこと。
    /// SetEnabled はグローバル GUI.enabled を書き換えるため、戻し忘れると
    /// 後に描かれる ComboBoxPopupWindow まで操作できなくなる。
    /// モード切替・タブのボタンはゲートの対象外にする (無効化すると抜けられなくなる)
    /// </summary>
    public static class TimelineLayerGateDrawer
    {
        private const float BUTTON_WIDTH = 220f;

        public static void Begin(GUIView view, string layerName, float rowHeight)
        {
            var state = TimelineLayerGateClient.GetState(layerName);
            switch (state)
            {
                case TimelineLayerGateClient.StateNoTimeline:
                case TimelineLayerGateClient.StateReady:
                    return;

                case TimelineLayerGateClient.StateMissing:
                    DrawMissing(view, layerName, rowHeight);
                    Disable(view);
                    return;

                default:
                    // メイド不在 (本プラグインのレイヤーでは起きない) や未知の値は無効化だけ行う
                    Disable(view);
                    return;
            }
        }

        /// <summary>強制無効を解除して有効へ戻す。冪等</summary>
        public static void End(GUIView view)
        {
            view.forceDisabled = false;
            view.SetEnabled(true);
        }

        private static void DrawMissing(GUIView view, string layerName, float rowHeight)
        {
            view.DrawLabel(TimelineLayerGateClient.GetNoticeText(layerName), -1, rowHeight,
                textColor: Color.yellow);

            // ボタンは強制無効の前に描く (押せる必要がある)
            if (view.DrawButton(TimelineLayerGateClient.GetAddButtonText(layerName), BUTTON_WIDTH, rowHeight))
            {
                TimelineLayerGateClient.AddLayer(layerName);
            }

            view.DrawHorizontalLine(Color.gray);
            view.AddSpace(5);
        }

        private static void Disable(GUIView view)
        {
            view.forceDisabled = true;
            view.SetEnabled(false);
        }
    }
}
```

- [ ] **Step 2: MainWindow.DrawContent で End を保証**

```csharp
        protected override void DrawContent()
        {
            _rootView.ResetLayout();

            try
            {
                DrawModeContent();
            }
            finally
            {
                // レイヤーゲートで強制無効にした状態を、早期 return や例外に関わらずここで必ず解く
                TimelineLayerGateDrawer.End(_contentView);
            }

            // ボタン押下で _rootView に登録されたフォーカスをポップアップへ引き渡す
            ComboBoxPopupWindow.instance.ProcessFocus(_rootView, this);
        }
```

`DrawModeContent` が `_contentView` 以外のビューに描いている場合は、その `view` に対して `End` を呼ぶ（`grep -n 'DrawModeContent' MainWindow.cs` で実体を確認）。

- [ ] **Step 3: DrawTimelineContent にゲートを挿入**

```csharp
        private void DrawTimelineContent(GUIView view)
        {
            if (_timelineControllers.Count == 0)
            {
                InitTimelineControllers();
            }
            if (_timelineControllers.Count == 0)
            {
                return;
            }

            DrawTimelineTabs(view);

            view.DrawHorizontalLine(Color.gray);
            view.AddSpace(5);

            // エフェクトタブの後に置き、タブ切替は無効化しない。
            // レイヤー名は SceneEditor 側 PostEffectTimelineLayer のクラス名 (文字列契約)
            TimelineLayerGateDrawer.Begin(view, POST_EFFECT_LAYER_NAME, 20f);

            var controller = _timelineControllers[Mathf.Clamp(_timelineTabIndex, 0, _timelineControllers.Count - 1)];

            view.BeginScrollView(-1, GetScrollHeight(view), GUIView.AutoScrollViewRect, false, true);
            {
                DrawEffectRow(view, controller);
            }
            view.EndScrollView();
        }
```

`MainWindow.cs:45` の `TIMELINE_TAB_WIDTH` の隣に定数を追加:

```csharp
        /// <summary>SceneEditor 側のポストエフェクトレイヤーのクラス名。TimelineLayerGateHost の文字列契約</summary>
        private const string POST_EFFECT_LAYER_NAME = "PostEffectTimelineLayer";
```

- [ ] **Step 4: A の両構成ビルド**

Expected: エラー 0。

- [ ] **Step 5: コミット**

```bash
cd /w/COM3D2_5/work/COM3D2.PostEffects.Plugin
git add source/COM3D25.PostEffects.Plugin/TimelineLayerGateDrawer.cs source/COM3D25.PostEffects.Plugin/MainWindow.cs
git commit -m "feat(window): タイムラインモードにポストエフェクトレイヤーの未登録ゲートを追加する"
```

---

### Task 5: 実機検証とレビュー

- [ ] **Step 1: 実機確認（DLL 反映はユーザー操作。`debug.bat` は実行しない）**

確認項目（MCP `com3d25-devbridge` の `screenshot` / `tail_log`）:

1. SceneEditor 無効・タイムライン未読込: PostEffects のタイムラインモードは従来通り（注意文・ボタン無し）
2. 新規タイムラインを作成し PostEffects のタイムラインモードを開く: 黄色の「「ポストエフェクト」レイヤーが未登録のためタイムラインに記録されません」と「「ポストエフェクト」レイヤーを追加」ボタンが出て、下のスライダーが灰色で操作不能。エフェクトタブとモード切替ボタンは押せる
3. ボタン押下: レイヤーが追加され、SceneEditor のタイムラインウィンドウのアクティブレイヤーが「ポストエフェクト」になり、PostEffects 側は通常表示に戻る。履歴に「「ポストエフェクト」レイヤー新規作成」
4. 無効表示中に PostEffects の「エフェクト」モードへ切り替えると通常操作できる（ゲートはタイムラインモード限定）
5. 無効表示中でも ColorPicker / ComboBox ポップアップなど他ウィンドウの操作が効く（`GUI.enabled` の戻し漏れ無し）
6. `tail_log` に `TimelineLayerGateClient:` の警告が出ていない

- [ ] **Step 2: code-review スキルでレビューし、指摘を取り込む（A・B 両方の差分）**

- [ ] **Step 3: 最終ビルド（B: 両構成＋テスト、A: 両構成）を実行して結果を報告**

完了報告に含めること: B-MTEUtils の未プッシュコミット（`b619f30` と Task 2 のコミット）を `git push origin master` するまで A の submodule は他環境で解決できない旨。

---

## 自己レビューメモ

- 要件カバレッジ: タイムラインモード限定（Task 4 Step 3）、タブ非無効化（Begin をタブ描画後に配置）、ChangeActiveLayer 委譲（Task 1 `AddLayer`）、不在時不変（クライアントの 0 返却 → ドロワーの早期 return）、リフレクション連携（Task 1・2）
- 型整合: ホスト `GetState(string)→int` / `GetNoticeText(string)→string` / `GetAddButtonText(string)→string` / `AddLayer(string)` と、クライアントの `Func<string,int>` / `Func<string,string>` ×2 / `Action<string>` が一致。状態値 0..3 は Task 1 のテストで固定し、クライアントの `State*` 定数と一致
- .NET 3.5: 使うデリゲートは `Func<string,int>` 等の入力 1 個のみ
- 既知の限界: ホストはメイド単位レイヤー非対応（slotNo 固定 0）。PostEffects には不要なので YAGNI

## レビュー却下メモ

- 「ドロワーの default 分岐（メイド不在・未知の値）でラベルを出すべき」 — PostEffectTimelineLayer は hasSlotNo=false で到達しない分岐。YAGNI として無効化のみに留める
