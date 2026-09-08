# 履歴未対応ウィンドウの Undo/Redo 対応 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 操作履歴（Undo/Redo）に未対応だったマテリアル編集・シェイプキー編集・サブカメラ・テキスト・音声（BGM 設定）・動画の各ウィンドウでの値変更を `HistoryManager` に載せ、Ctrl+Z / 履歴ウィンドウで戻せるようにする。副次的に、これらのウィンドウでの変更確定時にもタイムラインの自動キーフレーム登録（`onEditCommitted`）が働くようになる。

**Architecture:** 既存の履歴基盤（値変更直前に `HistoryManager.BeforeEdit` を呼び、マウス解放で 1 エントリに確定）をそのまま使う。対象がメイドでない（モデルのマテリアル・シェイプキー、演出の全体状態）スコープを載せるため、`BeforeEdit` に「対象キー + スナップショット生成関数」を受け取るオーバーロードを 1 つ足し、`SnapshotFactory` を経由しない経路を作る。マテリアルとシェイプキーは「編集中の 1 件（値 + 追跡チェック）」を粒度とした専用スナップショット、テキスト / サブカメラ / BGM 設定 / 動画はシーンプリセット用 DTO（`ScenePresetText` 等）を丸ごと持つスナップショットにし、キャプチャ・適用は `MteEffectsSnapshot` の既存関数をカテゴリ別に公開して共用する。DTO の無変化判定は XML 直列化文字列の一致で行い、フィールド列挙の二重管理を避ける。

**Tech Stack:** C# (Unity 5.6 / Mono、.NET 3.5 相当の構文制約: `out var` 不可、パターンマッチ不可、`Enum.TryParse` 不可)、xunit (net48) テスト。

**Spec:** 本ファイルの Goal / Architecture と「前提となる調査結果」「対象外」節が仕様。別途スペック文書は無い。

## Global Constraints

- コメント・ログ文言は日本語。
- ビルドは COM3D2 / COM3D25 の両 GameVersion を必ず通す（対象フレームワークが異なる）。ゲームフォルダへコピーしない MSBuild 直接実行を使う（下記コマンド）。
- `git worktree` は使わない。
- 新しい設定項目・新しいウィンドウは追加しない。既存の `historyLimit` と `HistoryWindow` の枠内で完結させる。
- 新規 .cs は `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj` の `<Compile Include>` へ必ず追加する（ワイルドカードではない）。
- 対象ファイルは CRLF。`Edit` ツールの部分置換で編集する（ファイル全体を書き戻すときは改行を保持する）。

### ビルド・テストコマンド

Git Bash から（`.env` に `COM3D2_DIR` / `COM3D25_DIR` がある前提）:

```bash
cd W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin
export COM3D2_DIR="$(grep '^COM3D2_DIR=' ../../.env | cut -d= -f2- | tr -d '\r')"
export COM3D25_DIR="$(grep '^COM3D25_DIR=' ../../.env | cut -d= -f2- | tr -d '\r')"
export MSYS_NO_PATHCONV=1
MSB="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
for gv in COM3D25 COM3D2; do
  "$MSB" COM3D2.SceneEditor.Plugin.csproj -p:Configuration=Debug -p:GameVersion=$gv \
    "-p:COM3D2_DIR=$COM3D2_DIR" "-p:COM3D25_DIR=$COM3D25_DIR" -nologo -v:m 2>&1 | grep -E "error CS|Plugin ->"
done
```

期待: 2 行の `COM3D2.SceneEditor.Plugin -> ...dll` が出て `error CS` が無い。

テスト（テストプロジェクトは COM3D25 版 Debug ビルドの DLL を参照するため、先に上記ビルドが必要）:

```bash
cd W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin
dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter "FullyQualifiedName~<TestClass>"
```

---

## 前提となる調査結果（2026-09-08）

### 履歴基盤の要点

- `Manager/HistoryManager.cs`
  - `BeforeEdit(Maid maid, HistoryScope scope, string description, IEnumerable<Transform> targetBones = null)`（:85-120）。`_pending` が同一 `(maid, scope)` なら何もしない → `onChanged` から毎フレーム呼んでよい。
  - `Update()`（:137-144）で `!Input.GetMouseButton(0)` のとき `CommitPending()`。ここが唯一の「ドラッグ終了」検出。GUIView のスライダーに完了コールバックは無い。
  - `CommitPending()`（:147-173）: `after = before.CaptureCurrent()`、`before.Approximately(after)` なら破棄、`AddEntry` 後に `onEditCommitted` を発火。
  - `HistoryEntry`（:26-38）: `maid` / `scope` / `before` / `after`。`canApply => before.CanApply(maid)`。
- `Manager/History/IStateSnapshot.cs`: `AddBones` / `CaptureCurrent` / `Apply(Maid)` / `Approximately` / `CanApply(Maid)`。
- `Manager/History/HistoryScope.cs`: enum 11 種。`HistoryScopeUtils.RequiresMaid`（:44-64）で maid 不要スコープを列挙。
- `Manager/History/SnapshotFactory.cs`: scope → `*Snapshot.Capture(...)`。未知スコープは `LogError` して null。
- 自動キーフレーム: `TimelineWindow.cs:179` が `onEditCommitted` を購読し `TryAutoKeyFrame(entry.maid)`。`entry.maid == null` なら `AutoKeyFrameGate` はメイド判定を通す（既存の Light / Background と同じ扱い）。

### 対象ウィンドウの書き込み箇所

| 対象 | ファイル | 値の書き込み |
|---|---|---|
| マテリアル | `MaterialPropertyRowsDrawer.cs` | チェック ON/OFF :64-75、ペースト :87-94、初期化 :127-131、色 :154-158、数値 :189-192。`MaterialEditWindow`（メイド/モデル/背景タブ）と `Timeline/ItemInspector/MaterialItemInspectorBase.cs:42` から共用 |
| シェイプキー（メイド） | `MaidShapeKeyRowDrawer.cs` | チェック :43-55、スライダー確定 :69-75（`updateTransform` が true のときに `blendShape.weight` 書込 + `FixBlendValues` + `Mark`） |
| シェイプキー（モデル） | `ModelShapeKeyRowDrawer.cs` | チェック :35-46、スライダー確定 :63-70 |
| サブカメラ | `SubCameraRowDrawer.cs` | 有効 :39、追従行 :42（`MaidFollowRowDrawer`）、位置 :45-51、回転 :76-96、FoV :55-66、ビューポート :109-131。台数は `CameraWindow.cs:698 DrawSubCameraCountRow` |
| テキスト | `TextRowDrawer.cs` | 本文 :76-81、フォント :83-91、サイズ/行間 :96-105、整列 :107-112、幅/高さ :114-135、色 :137-138、位置/回転/拡縮 :149-185。件数は `TextWindow.cs:168 DrawTextCountRow` |
| 音声 | `SoundWindow.cs` | BGM ファイル: 選択 :199-212、パス欄 :221、音量 :247-268、BPM ライン :270、BPM :275-283、オフセット :286-297 |
| 動画 | `VideoWindow.cs` | 本数 :152-155、有効 :180-186、表示形式 :93-97、パス選択 :217-220、パス欄 :231、開始位置/音量 :281-310、GUI :366/:379、Mesh :383-445、Backmost :446-491、Frontmost :492-535 |

### 再利用できる既存コード

- `Manager/MteEffectsSnapshot.cs`: `CaptureTexts` / `ApplyTexts` / `CaptureSubCameras` / `ApplySubCameras` / `CaptureSound` / `ApplySound` / `CaptureVideos` / `ApplyVideos`（いずれも private、`ScenePresetEffects` 一括）。
- `ScenePresetData.cs`: `ScenePresetText`(:684) / `ScenePresetSubCamera`(:719) / `ScenePresetSound`(:759) / `ScenePresetVideo`(:774)。
- `MaidManipulation/EditTargetStore.cs`: `IsModified` / `Mark` / `Unmark`。
- `Timeline/ModelMaterial.cs`: `HasColor/GetColor/SetColor`、`HasValue/GetValue/SetValue`、`material`（Unity `Material`、破棄で null 化）。
- `Timeline/MaidCache.cs`: `GetBlendShape(name)` / `SetBlendShapeValue(name, v)` / `GetBlendShapeValue(name)` / `FixBlendValues(names)`。
- `Timeline/Manager/MovieManager.cs`: `LoadMovie(i)` はパス未変更なら何もしない（:246-269）。`ReloadMovie(i)` / `UpdateTransform(i)` / `UpdateColor(i)` / `UpdateMesh(i)` / `UpdateVolume(i)` / `UpdateSeekTime(i)` / `UpdateVisible(i)`。
- `Timeline/Manager/BGMManager.cs`: `Load()` は同一パス読込済みなら何もしない（:91-94）。`Reload()` は `Stop()` → `Load()`。

### 対象外（この計画では扱わない）

- **ライブ演出（ステージライト / レーザー / サイリウム、`LiveEffectWindow.cs`）**: コントローラの増減を含む階層状態で、`StageLightController` に `CopyFrom` が無いなど値コピー基盤の整備が先に要る。別計画で扱う。
- **ポストエフェクト**: SE 側に専用ウィンドウが無く、タイムラインのキーフレーム編集は既に `TimelineHistoryEntry` で履歴対応済み。
- **ボイス / 効果音の再生操作、ゲーム内 BGM の再生**: 状態ではなく再生操作なので記録しない。Undo でゲーム BGM を頭出し再生するのも望ましくない。
- **動画の「再読込」ボタン、BGM の「再読込」「再生 / 一時停止 / 停止」**: 値を変えない操作。

---

## ファイル構成

| 種別 | パス | 責務 |
|---|---|---|
| Modify | `Manager/HistoryManager.cs` | `targetKey` + スナップショット生成関数を受けるオーバーロードの追加、確定待ち同一性判定に `targetKey` を含める |
| Modify | `Manager/History/HistoryScope.cs` | `Material` / `ShapeKey` / `Text` / `SubCamera` / `Sound` / `Video` を追加（すべて maid 不要） |
| Create | `Manager/History/PresetDtoUtils.cs` | DTO の XML 直列化による等価判定（純粋ロジック） |
| Create | `Manager/History/PresetDtoSnapshot.cs` | DTO を丸ごと持つスナップショットの抽象基底 |
| Create | `Manager/History/MaterialSnapshot.cs` | マテリアル 1 件（全プロパティ + 追跡チェック） |
| Create | `Manager/History/MaidShapeKeySnapshot.cs` | メイドのシェイプキー 1 件（重み + 追跡チェック） |
| Create | `Manager/History/ModelShapeKeySnapshot.cs` | モデルのシェイプキー 1 件（重み + 追跡チェック） |
| Create | `Manager/History/TextSnapshot.cs` | テキスト全件（件数含む） |
| Create | `Manager/History/SubCameraSnapshot.cs` | サブカメラ全台（台数含む） |
| Create | `Manager/History/SoundSnapshot.cs` | BGM ファイル設定（ゲーム BGM は含まない） |
| Create | `Manager/History/VideoSnapshot.cs` | 動画全本（本数含む） |
| Create | `Manager/History/VideoReloadPolicy.cs` | 動画 1 本の適用時に再読込が要るかの純粋判定 |
| Modify | `Manager/MteEffectsSnapshot.cs` | カテゴリ別 Capture/Apply を public 化、BGM 設定とゲーム BGM の適用を分離、動画の部分再読込 |
| Modify | `MaterialPropertyRowsDrawer.cs` / `MaterialEditWindow.cs` | `BeforeEdit` 差し込み、メイドタブから `Maid` を渡す |
| Modify | `MaidShapeKeyRowDrawer.cs` / `ModelShapeKeyRowDrawer.cs` / `ShapeKeyEditWindow.cs` | `BeforeEdit` 差し込み、古い「未対応」コメント除去 |
| Modify | `TextRowDrawer.cs` / `TextWindow.cs` | `BeforeEdit` 差し込み |
| Modify | `SubCameraRowDrawer.cs` / `MaidFollowRowDrawer.cs`(呼び出し側で対応) / `CameraWindow.cs` | `BeforeEdit` 差し込み |
| Modify | `SoundWindow.cs` | `BeforeEdit` 差し込み |
| Modify | `VideoWindow.cs` | `BeforeEdit` 差し込み |
| Modify | `COM3D2.SceneEditor.Plugin.csproj` | 新規ファイルの `Compile Include` |
| Test | `source/COM3D2.SceneEditor.Plugin.Tests/PresetDtoUtilsTests.cs` | XML 等価判定 |
| Test | `source/COM3D2.SceneEditor.Plugin.Tests/VideoReloadPolicyTests.cs` | 再読込判定 |
| Modify | `docs-site/guide/timeline.md` / `docs-site/guide/staging.md` | 対応範囲の記述更新 |

---

### Task 1: HistoryManager に対象キー付きオーバーロードと新スコープを追加

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/HistoryManager.cs:26-38, 71-135`
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/History/HistoryScope.cs`

**Interfaces:**
- Produces: `HistoryManager.BeforeEdit(Maid maid, HistoryScope scope, string description, object targetKey, Func<IStateSnapshot> capture)` — 後続タスクの全ウィンドウがこれを呼ぶ。`capture` は確定待ちが無いときだけ評価される。
- Produces: `HistoryScope.Material` / `.ShapeKey` / `.Text` / `.SubCamera` / `.Sound` / `.Video`。

- [ ] **Step 1: `HistoryScope` に 6 値を追加し、`RequiresMaid` で false を返す**

`HistoryScope.cs` の enum 末尾（`PngPlacement,` の後）に追加:

```csharp
        /// <summary>マテリアル 1 件 (色・数値プロパティと追跡チェック)。メイド・モデル・背景で共用</summary>
        Material,
        /// <summary>シェイプキー 1 件 (重みと追跡チェック)。メイド・モデルで共用</summary>
        ShapeKey,
        /// <summary>フリーテキスト全件 (件数含む)</summary>
        Text,
        /// <summary>サブカメラ全台 (台数含む)</summary>
        SubCamera,
        /// <summary>BGM ファイル設定 (パス・BPM・BPM ライン)</summary>
        Sound,
        /// <summary>動画全本 (本数含む)</summary>
        Video,
```

`RequiresMaid` の `switch` に追加（`case HistoryScope.PngPlacement:` の後）:

```csharp
                // 対象はスナップショット側が保持する。メイドは自動キーフレーム登録の判定用に任意で付く
                case HistoryScope.Material:
                case HistoryScope.ShapeKey:
                case HistoryScope.Text:
                case HistoryScope.SubCamera:
                case HistoryScope.Sound:
                case HistoryScope.Video:
```

- [ ] **Step 2: `HistoryEntry` に `targetKey` を追加**

`HistoryManager.cs` の `HistoryEntry` に、`public HistoryScope scope;` の直後へ:

```csharp
        /// <summary>
        /// 同一スコープ内で対象を区別するキー (編集中のマテリアル・シェイプキー名など)。
        /// 確定待ちの集約判定にだけ使い、null なら区別しない
        /// </summary>
        public object targetKey;
```

- [ ] **Step 3: `BeforeEdit` を共通コアに寄せ、対象キー付きオーバーロードを追加**

既存の `BeforeEdit(Maid, HistoryScope, string, IEnumerable<Transform>)`（:85-120）を次に置き換える:

```csharp
        public void BeforeEdit(Maid maid, HistoryScope scope, string description,
            IEnumerable<Transform> targetBones = null)
        {
            BeforeEditCore(maid, scope, description, null,
                () => SnapshotFactory.Capture(maid, scope, targetBones),
                targetBones);
        }

        /// <summary>
        /// SnapshotFactory を通さず、呼び出し側がスナップショットを組み立てるオーバーロード。
        /// メイドに紐付かない対象 (モデルのマテリアル・演出の全体状態) 向け。
        /// targetKey が異なれば別の操作として確定待ちを切り替える。
        /// capture は確定待ちが無いときだけ評価される
        /// </summary>
        public void BeforeEdit(Maid maid, HistoryScope scope, string description,
            object targetKey, Func<IStateSnapshot> capture)
        {
            BeforeEditCore(maid, scope, description, targetKey, capture, null);
        }

        private void BeforeEditCore(Maid maid, HistoryScope scope, string description,
            object targetKey, Func<IStateSnapshot> capture, IEnumerable<Transform> targetBones)
        {
            if ((maid == null && HistoryScopeUtils.RequiresMaid(scope))
                || config.historyLimit <= 0)
            {
                return;
            }

            if (_pending != null
                && (_pending.maid != maid
                    || _pending.scope != scope
                    || !Equals(_pending.targetKey, targetKey)))
            {
                CommitPending();
            }

            if (_pending == null)
            {
                var snapshot = capture();
                if (snapshot == null)
                {
                    return;
                }

                _pending = new HistoryEntry
                {
                    description = description,
                    maid = maid,
                    scope = scope,
                    targetKey = targetKey,
                    before = snapshot,
                };
            }
            else
            {
                _pending.before.AddBones(targetBones);
            }
        }
```

`Func<>` のため `using System;` が無ければ追加する（既に `Action` を使っているので存在するはず）。

- [ ] **Step 4: 両構成でビルドし、エラーが無いことを確認**

Run: 上記ビルドコマンド
Expected: 2 行の `Plugin ->` と `error CS` なし。

- [ ] **Step 5: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin/Manager/HistoryManager.cs source/COM3D2.SceneEditor.Plugin/Manager/History/HistoryScope.cs
git commit -m "feat(history): 対象キー付きの BeforeEdit と非メイド対象のスコープを追加する"
```

---

### Task 2: DTO の XML 等価判定と DTO スナップショット基底

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/Manager/History/PresetDtoUtils.cs`
- Create: `source/COM3D2.SceneEditor.Plugin/Manager/History/PresetDtoSnapshot.cs`
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/PresetDtoUtilsTests.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`

**Interfaces:**
- Produces: `static bool PresetDtoUtils.AreEqual<T>(T a, T b)` — `XmlSerializer` で両者を直列化して文字列比較。どちらかが null なら参照一致のみ true。
- Produces: `abstract class PresetDtoSnapshot<T> : IStateSnapshot where T : class` — `protected T state`、`protected abstract T CaptureState()`、`protected abstract void ApplyState(T state)`、`protected abstract PresetDtoSnapshot<T> CreateEmpty()`。`Approximately` は `PresetDtoUtils.AreEqual`、`CanApply` は `state != null`、`AddBones` は空。

- [ ] **Step 1: 失敗するテストを書く**

`PresetDtoUtilsTests.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    // 演出系スナップショットの無変化判定は DTO の XML 直列化で行う。
    // フィールドを列挙して比較しないため、DTO へ項目を足しても判定が漏れない
    public class PresetDtoUtilsTests
    {
        private static ScenePresetText MakeText()
        {
            return new ScenePresetText
            {
                text = "abc",
                fontSize = 20,
                position = new Vector3(1f, 2f, 3f),
                color = Color.red,
            };
        }

        [Fact]
        public void 同じ内容なら等しい()
        {
            Assert.True(PresetDtoUtils.AreEqual(MakeText(), MakeText()));
        }

        [Fact]
        public void 値が違えば等しくない()
        {
            var b = MakeText();
            b.position = new Vector3(1f, 2f, 4f);
            Assert.False(PresetDtoUtils.AreEqual(MakeText(), b));
        }

        [Fact]
        public void リストの要素数が違えば等しくない()
        {
            var a = new List<ScenePresetText> { MakeText() };
            var b = new List<ScenePresetText> { MakeText(), MakeText() };
            Assert.False(PresetDtoUtils.AreEqual(a, b));
        }

        [Fact]
        public void 片方がnullなら等しくない()
        {
            Assert.False(PresetDtoUtils.AreEqual(MakeText(), null));
            Assert.True(PresetDtoUtils.AreEqual<ScenePresetText>(null, null));
        }
    }
}
```

- [ ] **Step 2: テストが失敗（コンパイルエラー）することを確認**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter "FullyQualifiedName~PresetDtoUtilsTests"`
Expected: `PresetDtoUtils` が見つからずビルド失敗。

- [ ] **Step 3: `PresetDtoUtils` を実装**

```csharp
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// シーンプリセット用 DTO の等価判定。
    /// XmlSerializer で直列化した文字列を比べる。フィールドを列挙して比較しないため、
    /// DTO に項目を足しても判定が漏れない。履歴の確定時 (操作 1 回に 1 度) にしか呼ばないので速度は問わない
    /// </summary>
    public static class PresetDtoUtils
    {
        private static readonly Dictionary<System.Type, XmlSerializer> _serializers
            = new Dictionary<System.Type, XmlSerializer>();

        public static bool AreEqual<T>(T a, T b) where T : class
        {
            if (ReferenceEquals(a, b))
            {
                return true;
            }
            if (a == null || b == null)
            {
                return false;
            }
            return Serialize(a) == Serialize(b);
        }

        private static string Serialize<T>(T value)
        {
            XmlSerializer serializer;
            lock (_serializers)
            {
                if (!_serializers.TryGetValue(typeof(T), out serializer))
                {
                    serializer = new XmlSerializer(typeof(T));
                    _serializers[typeof(T)] = serializer;
                }
            }
            using (var writer = new StringWriter())
            {
                serializer.Serialize(writer, value);
                return writer.ToString();
            }
        }
    }
}
```

- [ ] **Step 4: `PresetDtoSnapshot<T>` を実装**

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// シーンプリセット用 DTO を丸ごと持つスナップショットの基底。
    /// キャプチャと適用はシーンプリセットと共用し、無変化判定は DTO の XML 直列化で行う。
    /// 対象はマネージャのシングルトンなのでメイドとボーンには依存しない
    /// </summary>
    public abstract class PresetDtoSnapshot<T> : IStateSnapshot where T : class
    {
        protected T state;

        protected abstract T CaptureState();
        protected abstract void ApplyState(T state);
        /// <summary>CaptureCurrent 用。自身と同じ型の空インスタンスを返す</summary>
        protected abstract PresetDtoSnapshot<T> CreateEmpty();

        /// <summary>派生の static Capture から呼ぶ初期化</summary>
        protected PresetDtoSnapshot<T> Init()
        {
            state = CaptureState();
            return this;
        }

        public void AddBones(IEnumerable<Transform> targetBones)
        {
        }

        public IStateSnapshot CaptureCurrent() => CreateEmpty().Init();

        public void Apply(Maid maid) => ApplyState(state);

        public bool Approximately(IStateSnapshot other)
        {
            var o = other as PresetDtoSnapshot<T>;
            return o != null && PresetDtoUtils.AreEqual(state, o.state);
        }

        public bool CanApply(Maid maid) => state != null;
    }
}
```

- [ ] **Step 5: csproj に 2 ファイルを追加**

`COM3D2.SceneEditor.Plugin.csproj` の `<Compile Include="Manager\History\IStateSnapshot.cs" />` の直後に:

```xml
    <Compile Include="Manager\History\PresetDtoUtils.cs" />
    <Compile Include="Manager\History\PresetDtoSnapshot.cs" />
```

- [ ] **Step 6: ビルドしてテストを通す**

Run: ビルドコマンド → `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter "FullyQualifiedName~PresetDtoUtilsTests"`
Expected: 4 件 PASS。

- [ ] **Step 7: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin/Manager/History/PresetDtoUtils.cs source/COM3D2.SceneEditor.Plugin/Manager/History/PresetDtoSnapshot.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj source/COM3D2.SceneEditor.Plugin.Tests/PresetDtoUtilsTests.cs
git commit -m "feat(history): DTO を丸ごと持つスナップショット基底と XML 等価判定を追加する"
```

---

### Task 3: マテリアル編集の履歴対応

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/Manager/History/MaterialSnapshot.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/MaterialPropertyRowsDrawer.cs:36-196`
- Modify: `source/COM3D2.SceneEditor.Plugin/MaterialEditWindow.cs:139-307`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`

**Interfaces:**
- Consumes: `HistoryManager.BeforeEdit(maid, scope, description, targetKey, capture)`（Task 1）。
- Produces: `static MaterialSnapshot MaterialSnapshot.Capture(MTEP.ModelMaterial material, MaterialTrackTarget track, string trackKey)`。
- Produces: `MaterialPropertyRowsDrawer.Draw(GUIView view, MTEP.ModelMaterial material, MaterialTrackTarget track, float rowHeight, string colorLabelPrefix, Maid maid = null)` — 末尾に任意引数を足すだけで既存呼び出し (Inspector 3 種) は変更不要。

- [ ] **Step 1: `MaterialSnapshot` を実装**

```csharp
using System.Collections.Generic;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// マテリアル 1 件のスナップショット (全色・数値プロパティと追跡チェック)。
    /// 差分ではなく全プロパティを持つので、初期化ボタンやペーストのように
    /// 複数プロパティが一度に変わる操作も 1 エントリで戻せる
    /// </summary>
    public class MaterialSnapshot : IStateSnapshot
    {
        private MTEP.ModelMaterial _material;
        private MaterialTrackTarget _track;
        private string _trackKey;

        private readonly Dictionary<MTEP.ModelMaterial.ColorPropertyType, Color> _colors
            = new Dictionary<MTEP.ModelMaterial.ColorPropertyType, Color>();
        private readonly Dictionary<MTEP.ModelMaterial.ValuePropertyType, float> _values
            = new Dictionary<MTEP.ModelMaterial.ValuePropertyType, float>();
        private bool _isTracked;

        /// <param name="trackKey">追跡チェックの記録名。追跡しない対象 (背景タブ) は null</param>
        public static MaterialSnapshot Capture(
            MTEP.ModelMaterial material, MaterialTrackTarget track, string trackKey)
        {
            var snapshot = new MaterialSnapshot
            {
                _material = material,
                _track = track,
                _trackKey = trackKey,
            };

            foreach (var type in MTEP.ModelMaterial.ColorPropertyTypes)
            {
                if (material.HasColor(type))
                {
                    snapshot._colors[type] = material.GetColor(type);
                }
            }
            foreach (var type in MTEP.ModelMaterial.ValuePropertyTypes)
            {
                if (material.HasValue(type))
                {
                    snapshot._values[type] = material.GetValue(type);
                }
            }

            if (trackKey != null)
            {
                var store = track.findStore();
                snapshot._isTracked = store != null && store.IsModified(trackKey);
            }
            return snapshot;
        }

        public void AddBones(IEnumerable<Transform> targetBones)
        {
        }

        public IStateSnapshot CaptureCurrent() => Capture(_material, _track, _trackKey);

        public void Apply(Maid maid)
        {
            foreach (var pair in _colors)
            {
                if (_material.HasColor(pair.Key))
                {
                    _material.SetColor(pair.Key, pair.Value);
                }
            }
            foreach (var pair in _values)
            {
                if (_material.HasValue(pair.Key))
                {
                    _material.SetValue(pair.Key, pair.Value);
                }
            }

            if (_trackKey != null)
            {
                // 追跡 OFF へ戻すときもストアを作る (Unmark だけなら空ストアが残るが害はない)
                var store = _track.getStore();
                if (_isTracked)
                {
                    store.Mark(_trackKey);
                }
                else
                {
                    store.Unmark(_trackKey);
                }
            }
        }

        public bool Approximately(IStateSnapshot other)
        {
            var o = other as MaterialSnapshot;
            if (o == null || o._material != _material || o._isTracked != _isTracked
                || o._colors.Count != _colors.Count || o._values.Count != _values.Count)
            {
                return false;
            }
            foreach (var pair in _colors)
            {
                Color color;
                if (!o._colors.TryGetValue(pair.Key, out color) || color != pair.Value)
                {
                    return false;
                }
            }
            foreach (var pair in _values)
            {
                float value;
                if (!o._values.TryGetValue(pair.Key, out value)
                    || !Mathf.Approximately(value, pair.Value))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// 着替え・モデル削除で Unity 側の Material が破棄されたら適用しない。
        /// 着替え後に「更新」を押すまで古い ModelMaterial が一覧に残る場合は、
        /// 破棄済みでない限り旧マテリアルへ書き戻す (見た目に反映されないが害はない)
        /// </summary>
        public bool CanApply(Maid maid) => _material != null && _material.material != null;
    }
}
```

- [ ] **Step 2: `MaterialPropertyRowsDrawer.Draw` に `Maid maid = null` を足し、書き込み直前に `BeforeEdit` を差す**

`Draw` のシグネチャを次に変更:

```csharp
        public static void Draw(
            GUIView view,
            MTEP.ModelMaterial material,
            MaterialTrackTarget track,
            float rowHeight,
            string colorLabelPrefix,
            Maid maid = null)
```

`var trackKey = ...;` の直後に記録用クロージャを追加:

```csharp
            // 値を書き込む直前に呼ぶ。同じマテリアルへの連続変更はマウス解放まで 1 件に集約される。
            // maid はメイドタブでだけ渡り、自動キーフレーム登録の対象判定に使う
            Action<string> recordEdit = label =>
            {
                HistoryManager.instance.BeforeEdit(maid, HistoryScope.Material,
                    "マテリアル: " + material.displayName + " " + label,
                    material, () => MaterialSnapshot.Capture(material, track, trackKey));
            };
```

`DrawNameRow` は `recordEdit` を受け取る引数を追加し（`Action markTracked` の後に `Action<string> recordEdit`）、次の 3 箇所の直前に差す:

- チェック ON/OFF（:64-75）: `onCheckChanged` の先頭に `recordEdit("追跡");`
- ペースト（:87-94）: `if (MaterialClipboard.Paste(material))` の前に `recordEdit("ペースト");`。`Paste` が false（何も貼れなかった）でも確定時に無変化として破棄されるので問題ない。
- 「初期化」ボタン（:127）: `material.Reset();` の前に `recordEdit("初期化");`
- 色（:154）: `material.SetColor(...)` の前に `recordEdit(propertyType.ToString());`
- 数値（:189）: `material.SetValue(...)` の前に `recordEdit(propertyType.ToString());`

`DrawNameRow` の呼び出しも `DrawNameRow(view, material, track, trackKey, rowHeight, markTracked, recordEdit);` に変える。

- [ ] **Step 3: `MaterialEditWindow` のメイドタブから `Maid` を渡す**

- `DrawMaterialSelector(List<MTEP.ModelMaterial> materials, MaterialTrackTarget track)` → `(..., MaterialTrackTarget track, Maid maid = null)`
- `DrawMaterialProperties(MTEP.ModelMaterial material, MaterialTrackTarget track)` → `(..., Maid maid)` とし、内部の呼び出しを `MaterialPropertyRowsDrawer.Draw(view, material, track, ROW_HEIGHT, null, maid);` に変更
- `DrawMaidMaterial` 内の `DrawMaterialSelector(slot.materials, new MaterialTrackTarget {...});` を `DrawMaterialSelector(slot.materials, new MaterialTrackTarget {...}, target);` に変更
- `DrawMaterialSelector` 内の `DrawMaterialProperties(material, track);` を `DrawMaterialProperties(material, track, maid);` に変更

- [ ] **Step 4: csproj に追加**

`<Compile Include="Manager\History\PresetDtoSnapshot.cs" />` の直後:

```xml
    <Compile Include="Manager\History\MaterialSnapshot.cs" />
```

- [ ] **Step 5: 両構成でビルド**

Expected: `error CS` なし。

- [ ] **Step 6: 実機確認（ゲーム起動中なら）**

マテリアル編集ウィンドウで数値スライダーを動かして離す → 履歴ウィンドウに「マテリアル: <名前> <プロパティ>」が 1 件増える → Ctrl+Z で値と追跡チェックが戻る。「初期化」→ Ctrl+Z で全プロパティが戻る。着替え後に古いエントリが `canApply=false` で飛ばされること。

- [ ] **Step 7: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin/Manager/History/MaterialSnapshot.cs source/COM3D2.SceneEditor.Plugin/MaterialPropertyRowsDrawer.cs source/COM3D2.SceneEditor.Plugin/MaterialEditWindow.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj
git commit -m "feat(material): マテリアル編集を操作履歴に載せる"
```

---

### Task 4: シェイプキー編集の履歴対応

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/Manager/History/MaidShapeKeySnapshot.cs`
- Create: `source/COM3D2.SceneEditor.Plugin/Manager/History/ModelShapeKeySnapshot.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/MaidShapeKeyRowDrawer.cs:35-77`
- Modify: `source/COM3D2.SceneEditor.Plugin/ModelShapeKeyRowDrawer.cs:27-70`
- Modify: `source/COM3D2.SceneEditor.Plugin/ShapeKeyEditWindow.cs:15`（コメント）
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`

**Interfaces:**
- Produces: `static MaidShapeKeySnapshot MaidShapeKeySnapshot.Capture(Maid maid, MTEP.MaidCache maidCache, string shapeKeyName)`
- Produces: `static ModelShapeKeySnapshot ModelShapeKeySnapshot.Capture(MTEP.StudioModelStat model, string shapeKeyName)`

- [ ] **Step 1: `MaidShapeKeySnapshot` を実装**

```csharp
using System.Collections.Generic;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// メイドのシェイプキー 1 件のスナップショット (重みと追跡チェック)。
    /// MaidBlendShape は着替えでキャッシュが作り直されるため参照を持たず、名前で引き直す
    /// </summary>
    public class MaidShapeKeySnapshot : IStateSnapshot
    {
        private Maid _maid;
        private MTEP.MaidCache _maidCache;
        private string _shapeKeyName;
        private float _weight;
        private bool _isTracked;

        public static MaidShapeKeySnapshot Capture(
            Maid maid, MTEP.MaidCache maidCache, string shapeKeyName)
        {
            var store = MaidShapeKeyEditManager.instance.FindStore(maid);
            return new MaidShapeKeySnapshot
            {
                _maid = maid,
                _maidCache = maidCache,
                _shapeKeyName = shapeKeyName,
                _weight = maidCache.GetBlendShapeValue(shapeKeyName),
                _isTracked = store != null && store.IsModified(shapeKeyName),
            };
        }

        public void AddBones(IEnumerable<Transform> targetBones)
        {
        }

        public IStateSnapshot CaptureCurrent() => Capture(_maid, _maidCache, _shapeKeyName);

        public void Apply(Maid maid)
        {
            _maidCache.SetBlendShapeValue(_shapeKeyName, _weight);
            _maidCache.FixBlendValues(new[] { _shapeKeyName });

            var store = MaidShapeKeyEditManager.instance.GetStore(_maid);
            if (_isTracked)
            {
                store.Mark(_shapeKeyName);
            }
            else
            {
                store.Unmark(_shapeKeyName);
            }
        }

        public bool Approximately(IStateSnapshot other)
        {
            var o = other as MaidShapeKeySnapshot;
            return o != null && o._maid == _maid && o._shapeKeyName == _shapeKeyName
                && o._isTracked == _isTracked && Mathf.Approximately(o._weight, _weight);
        }

        /// <summary>着替え中や該当 morph を失ったスロット構成では適用しない</summary>
        public bool CanApply(Maid maid)
        {
            return HistoryScopeUtils.CanEditMaid(_maid)
                && MaidShapeKeyRowDrawer.IsEditable(_maidCache.GetBlendShape(_shapeKeyName));
        }
    }
}
```

- [ ] **Step 2: `ModelShapeKeySnapshot` を実装**

```csharp
using System.Collections.Generic;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// モデルのシェイプキー 1 件のスナップショット (重みと追跡チェック)。
    /// ModelBlendShape は名前でモデルから引き直す (モデル差し替えで実体が変わるため)
    /// </summary>
    public class ModelShapeKeySnapshot : IStateSnapshot
    {
        private MTEP.StudioModelStat _model;
        private string _shapeKeyName;
        private float _weight;
        private bool _isTracked;

        public static ModelShapeKeySnapshot Capture(MTEP.StudioModelStat model, string shapeKeyName)
        {
            var blendShape = FindBlendShape(model, shapeKeyName);
            var store = ModelShapeKeyEditManager.instance.FindStore(model.transform.gameObject);
            return new ModelShapeKeySnapshot
            {
                _model = model,
                _shapeKeyName = shapeKeyName,
                _weight = blendShape != null ? blendShape.weight : 0f,
                _isTracked = store != null && store.IsModified(shapeKeyName),
            };
        }

        private static MTEP.ModelBlendShape FindBlendShape(MTEP.StudioModelStat model, string shapeKeyName)
        {
            if (model == null || model.transform == null || model.blendShapes == null)
            {
                return null;
            }
            foreach (var blendShape in model.blendShapes)
            {
                if (blendShape.shapeKeyName == shapeKeyName)
                {
                    return blendShape;
                }
            }
            return null;
        }

        public void AddBones(IEnumerable<Transform> targetBones)
        {
        }

        public IStateSnapshot CaptureCurrent() => Capture(_model, _shapeKeyName);

        public void Apply(Maid maid)
        {
            var blendShape = FindBlendShape(_model, _shapeKeyName);
            if (blendShape == null)
            {
                return;
            }
            blendShape.weight = _weight;
            _model.FixBlendValues();

            var store = ModelShapeKeyEditManager.instance.GetStore(_model.transform.gameObject);
            if (_isTracked)
            {
                store.Mark(_shapeKeyName);
            }
            else
            {
                store.Unmark(_shapeKeyName);
            }
        }

        public bool Approximately(IStateSnapshot other)
        {
            var o = other as ModelShapeKeySnapshot;
            return o != null && o._model == _model && o._shapeKeyName == _shapeKeyName
                && o._isTracked == _isTracked && Mathf.Approximately(o._weight, _weight);
        }

        public bool CanApply(Maid maid) => FindBlendShape(_model, _shapeKeyName) != null;
    }
}
```

`model.blendShapes` の型が `List<ModelBlendShape>` であることは `ShapeKeyEditWindow.cs:333` 付近の列挙で確認できる。

- [ ] **Step 3: `MaidShapeKeyRowDrawer` に `BeforeEdit` を差す**

`Draw` の `var weight = blendShape.weight;` の直後に:

```csharp
            // 値を書き込む直前に呼ぶ。同じシェイプキーへの連続変更はマウス解放まで 1 件に集約される
            Action recordEdit = () => HistoryManager.instance.BeforeEdit(
                target, HistoryScope.ShapeKey, "シェイプキー: " + shapeKeyName,
                shapeKeyName, () => MaidShapeKeySnapshot.Capture(target, maidCache, shapeKeyName));
```

- `onCheckChanged` の先頭（`if (newChecked)` の前）に `recordEdit();`
- `if (updateTransform)` ブロックの先頭（`blendShape.weight = weight;` の前）に `recordEdit();`

- [ ] **Step 4: `ModelShapeKeyRowDrawer` に `BeforeEdit` を差す**

`var weight = blendShape.weight;` の直後に:

```csharp
            // 値を書き込む直前に呼ぶ。対象キーは blendShape 実体 (モデルが生きている間は同一)
            Action recordEdit = () => HistoryManager.instance.BeforeEdit(
                null, HistoryScope.ShapeKey, "シェイプキー: " + blendShape.name,
                blendShape, () => ModelShapeKeySnapshot.Capture(model, shapeKeyName));
```

- `onCheckChanged` の先頭に `recordEdit();`
- `if (updateTransform)` ブロックの先頭に `recordEdit();`

- [ ] **Step 5: `ShapeKeyEditWindow.cs:15` の「チェック集合は HistoryManager 未対応のため undo で戻らない」というコメント行を削除する**

該当行を `sed -n 10,20p` で確認してから、その 1 文だけを消す（前後の説明は残す）。

- [ ] **Step 6: csproj に追加**

```xml
    <Compile Include="Manager\History\MaidShapeKeySnapshot.cs" />
    <Compile Include="Manager\History\ModelShapeKeySnapshot.cs" />
```

- [ ] **Step 7: 両構成でビルド**

Expected: `error CS` なし。

- [ ] **Step 8: 実機確認（ゲーム起動中なら）**

メイドタブでスライダーを動かして離す → 履歴 1 件、Ctrl+Z で重みとチェックが戻る。チェック OFF（重み 0 化）→ Ctrl+Z で重みとチェック両方が戻る。モデルタブも同様。

- [ ] **Step 9: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin/Manager/History/MaidShapeKeySnapshot.cs source/COM3D2.SceneEditor.Plugin/Manager/History/ModelShapeKeySnapshot.cs source/COM3D2.SceneEditor.Plugin/MaidShapeKeyRowDrawer.cs source/COM3D2.SceneEditor.Plugin/ModelShapeKeyRowDrawer.cs source/COM3D2.SceneEditor.Plugin/ShapeKeyEditWindow.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj
git commit -m "feat(shapekey): シェイプキー編集を操作履歴に載せる"
```

---

### Task 5: MteEffectsSnapshot のカテゴリ別公開と動画再読込判定

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/MteEffectsSnapshot.cs`
- Create: `source/COM3D2.SceneEditor.Plugin/Manager/History/VideoReloadPolicy.cs`
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/VideoReloadPolicyTests.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`

**Interfaces:**
- Produces（public 化・シグネチャ変更）:
  - `static List<ScenePresetText> MteEffectsSnapshot.CaptureTexts()` / `static void ApplyTexts(List<ScenePresetText> texts)`
  - `static List<ScenePresetSubCamera> CaptureSubCameras()` / `static void ApplySubCameras(List<ScenePresetSubCamera> subCameras)`
  - `static ScenePresetSound CaptureBgmSettings()`（`gameBgmFile` は空のまま）/ `static void ApplyBgmSettings(ScenePresetSound src)`（パスが変わったときだけ `Reload`）
  - `static List<ScenePresetVideo> CaptureVideos()` / `static void ApplyVideos(List<ScenePresetVideo> videos, bool reloadAll)`
  - 既存の `CaptureState()` / `ApplyState(ScenePresetEffects)` は上記を組み合わせて同じ挙動を保つ（プリセット側の互換維持）。
- Produces: `enum VideoApplyAction { Update, Visible, Reload }` と `static VideoApplyAction VideoReloadPolicy.Decide(ScenePresetVideo before, ScenePresetVideo after)`。

- [ ] **Step 1: `VideoReloadPolicy` の失敗するテストを書く**

`VideoReloadPolicyTests.cs`:

```csharp
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    // 動画の履歴適用は ReloadMovie が重いため、パス・表示形式が変わったときだけ再読込する
    public class VideoReloadPolicyTests
    {
        private static ScenePresetVideo Make()
        {
            return new ScenePresetVideo { path = "a.mp4", enabled = true, displayType = 0, volume = 0.5f };
        }

        [Fact]
        public void 同一なら更新だけ()
        {
            Assert.Equal(VideoApplyAction.Update, VideoReloadPolicy.Decide(Make(), Make()));
        }

        [Fact]
        public void 音量など値だけの違いは更新だけ()
        {
            var after = Make();
            after.volume = 1f;
            Assert.Equal(VideoApplyAction.Update, VideoReloadPolicy.Decide(Make(), after));
        }

        [Fact]
        public void 有効フラグの違いは表示切替()
        {
            var after = Make();
            after.enabled = false;
            Assert.Equal(VideoApplyAction.Visible, VideoReloadPolicy.Decide(Make(), after));
        }

        [Fact]
        public void パスの違いは再読込()
        {
            var after = Make();
            after.path = "b.mp4";
            Assert.Equal(VideoApplyAction.Reload, VideoReloadPolicy.Decide(Make(), after));
        }

        [Fact]
        public void 表示形式の違いは再読込()
        {
            var after = Make();
            after.displayType = 1;
            Assert.Equal(VideoApplyAction.Reload, VideoReloadPolicy.Decide(Make(), after));
        }

        [Fact]
        public void 比較元が無ければ再読込()
        {
            Assert.Equal(VideoApplyAction.Reload, VideoReloadPolicy.Decide(null, Make()));
        }
    }
}
```

- [ ] **Step 2: テストが失敗することを確認**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter "FullyQualifiedName~VideoReloadPolicyTests"`
Expected: ビルド失敗（型が無い）。

- [ ] **Step 3: `VideoReloadPolicy` を実装**

```csharp
namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>動画 1 本へ設定を書き戻したあとに必要なプレイヤー操作</summary>
    public enum VideoApplyAction
    {
        /// <summary>位置・色・音量・シークの反映だけ</summary>
        Update,
        /// <summary>表示切替 (読込は試みる) + 反映</summary>
        Visible,
        /// <summary>プレイヤーを作り直す</summary>
        Reload,
    }

    /// <summary>
    /// 動画の履歴適用で ReloadMovie を避けるための判定。
    /// プレイヤーの作り直しはパスと表示形式が変わったときだけ必要 (MovieManager.SetupImpl の条件)
    /// </summary>
    public static class VideoReloadPolicy
    {
        public static VideoApplyAction Decide(ScenePresetVideo before, ScenePresetVideo after)
        {
            if (before == null || after == null)
            {
                return VideoApplyAction.Reload;
            }
            if (before.path != after.path || before.displayType != after.displayType)
            {
                return VideoApplyAction.Reload;
            }
            if (before.enabled != after.enabled)
            {
                return VideoApplyAction.Visible;
            }
            return VideoApplyAction.Update;
        }
    }
}
```

- [ ] **Step 4: `MteEffectsSnapshot` をカテゴリ別に公開する**

`CaptureState` / `ApplyState` を次に置き換える:

```csharp
        public static ScenePresetEffects CaptureState()
        {
            var data = new ScenePresetEffects();
            data.texts = CaptureTexts();
            data.subCameras = CaptureSubCameras();
            data.sound = CaptureSound();
            data.videos = CaptureVideos();
            return data;
        }

        /// <summary>null (旧プリセット / 未保存) なら何もしない</summary>
        public static void ApplyState(ScenePresetEffects data)
        {
            if (data == null)
            {
                return;
            }
            ApplyTexts(data.texts);
            ApplySubCameras(data.subCameras);
            ApplySound(data.sound);
            ApplyVideos(data.videos, reloadAll: true);
        }
```

（`ScenePresetEffects.texts` 等が `List<>` の初期化済みフィールドであることを `ScenePresetData.cs:801` 付近で確認し、代入で置き換える。）

`CaptureTexts(ScenePresetEffects data)` → `public static List<ScenePresetText> CaptureTexts()`（ローカル `var texts = new List<ScenePresetText>();` に Add して返す）。`ApplyTexts(ScenePresetEffects data)` → `public static void ApplyTexts(List<ScenePresetText> texts)`（`data.texts` を `texts` に読み替え。`texts == null || texts.Count == 0` で return）。

`CaptureSubCameras` / `ApplySubCameras` も同様に `List<ScenePresetSubCamera>` を返す / 受ける形へ。

サウンドは分離する:

```csharp
        /// <summary>BGM ファイル設定だけ (ゲーム BGM は含まない)。履歴とプリセットで共用</summary>
        public static ScenePresetSound CaptureBgmSettings()
        {
            var settings = bgmManager.settings;
            return new ScenePresetSound
            {
                bgmPath = settings.bgmPath,
                bpm = settings.bpm,
                isShowBPMLine = settings.isShowBPMLine,
                bpmLineOffsetFrame = settings.bpmLineOffsetFrame,
            };
        }

        private static ScenePresetSound CaptureSound()
        {
            var data = CaptureBgmSettings();
            // 無音は空文字。適用時に「停止」として働く
            data.gameBgmFile = BgmUtils.GetPlayingFileName() ?? "";
            return data;
        }

        /// <summary>
        /// BGM ファイル設定を書き戻す。パスが変わったときだけ読み直す
        /// (BPM だけの変更で曲を止めない)
        /// </summary>
        public static void ApplyBgmSettings(ScenePresetSound src)
        {
            var settings = bgmManager.settings;
            var pathChanged = settings.bgmPath != src.bgmPath;
            settings.bgmPath = src.bgmPath;
            settings.bpm = src.bpm;
            settings.isShowBPMLine = src.isShowBPMLine;
            settings.bpmLineOffsetFrame = src.bpmLineOffsetFrame;
            if (pathChanged)
            {
                // パスが空なら Stop だけが走る
                bgmManager.Reload();
            }
        }

        /// <summary>null (v29 以前 / 未記録) なら何もしない</summary>
        private static void ApplySound(ScenePresetSound src)
        {
            if (src == null)
            {
                return;
            }

            // プリセットは従来どおり必ず 1 回読み直す。
            // ApplyBgmSettings はパスが変わったときだけ Reload するので、
            // 変わらなかったときにここで補う (呼び出し前のパスで判定する)
            var prevBgmPath = bgmManager.settings.bgmPath;
            ApplyBgmSettings(src);
            if (prevBgmPath == src.bgmPath)
            {
                bgmManager.Reload();
            }

            // （以下、タイムライン BGM 再生中のスキップ判定と ApplyGameBgm は既存のまま）
```

注意: 既存 `ApplySound` は無条件に `bgmManager.Reload()` を 1 回していた。判定は **`ApplyBgmSettings` を呼ぶ前のパス** で行うこと（呼んだ後は `settings.bgmPath == src.bgmPath` が常に成立し、パス変更時に Reload が 2 回走る）。

動画:

```csharp
        public static List<ScenePresetVideo> CaptureVideos()
        {
            var videos = new List<ScenePresetVideo>();
            foreach (var settings in movieManager.settingsList)
            {
                videos.Add(new ScenePresetVideo { /* 既存の全項目コピー */ });
            }
            return videos;
        }

        /// <summary>
        /// 空 (v31 以前 / 未保存) なら触らない。
        /// v31 以前は動画が 1 件しか無いため、適用すると本数も 1 本へ戻る。
        /// reloadAll=false (履歴) では、パス・表示形式が変わった本だけプレイヤーを作り直し、
        /// それ以外は値の反映だけにする (ReloadMovie は全本のデコーダ再生成で重い)
        /// </summary>
        public static void ApplyVideos(List<ScenePresetVideo> videos, bool reloadAll)
        {
            if (videos == null || videos.Count == 0)
            {
                return;
            }

            var current = reloadAll ? null : CaptureVideos();

            // 手編集や破損 XML の異常値で大量生成しないよう UI と同じ上限へ丸める
            var count = Mathf.Min(videos.Count, MTEP.MovieManager.MaxVideoCount);
            movieManager.videoCount = count;

            for (var i = 0; i < count; i++)
            {
                var src = videos[i];
                var settings = movieManager.GetSettings(i);
                /* 既存の全項目コピー (settings.enabled = src.enabled; ...) */

                if (reloadAll)
                {
                    continue;
                }

                var before = i < current.Count ? current[i] : null;
                switch (VideoReloadPolicy.Decide(before, src))
                {
                    case VideoApplyAction.Reload:
                        movieManager.ReloadMovie(i);
                        break;
                    case VideoApplyAction.Visible:
                        movieManager.LoadMovie(i);
                        movieManager.UpdateVisible(i);
                        break;
                }
                movieManager.UpdateTransform(i);
                movieManager.UpdateMesh(i);
                movieManager.UpdateColor(i);
                movieManager.UpdateVolume(i);
                movieManager.UpdateSeekTime(i);
            }

            if (reloadAll)
            {
                // パス空の本は Unload だけが走る (LoadMovie は IsValidPath を見る)
                movieManager.ReloadMovie();
            }
        }
```

`Reload` した本は `Update*` を重ねても無害（プレイヤー未生成なら `WithPlayer` が何もしない）。

- [ ] **Step 5: csproj に追加**

```xml
    <Compile Include="Manager\History\VideoReloadPolicy.cs" />
```

- [ ] **Step 6: ビルドしてテストを通す**

Run: ビルド → `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter "FullyQualifiedName~VideoReloadPolicyTests"`
Expected: 6 件 PASS。既存テスト全体も `dotnet test source/COM3D2.SceneEditor.Plugin.Tests` で PASS。

- [ ] **Step 7: 実機確認（ゲーム起動中なら）**

シーンプリセットの保存 → 読込で、テキスト / サブカメラ / BGM / 動画が従来どおり復元されること（リファクタの回帰確認）。

- [ ] **Step 8: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin/Manager/MteEffectsSnapshot.cs source/COM3D2.SceneEditor.Plugin/Manager/History/VideoReloadPolicy.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj source/COM3D2.SceneEditor.Plugin.Tests/VideoReloadPolicyTests.cs
git commit -m "refactor(preset): 演出状態のキャプチャ・適用をカテゴリ別に公開し、動画の部分再読込を追加する"
```

---

### Task 6: テキスト / サブカメラ / BGM 設定 / 動画のスナップショット

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/Manager/History/TextSnapshot.cs`
- Create: `source/COM3D2.SceneEditor.Plugin/Manager/History/SubCameraSnapshot.cs`
- Create: `source/COM3D2.SceneEditor.Plugin/Manager/History/SoundSnapshot.cs`
- Create: `source/COM3D2.SceneEditor.Plugin/Manager/History/VideoSnapshot.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`

**Interfaces:**
- Consumes: `PresetDtoSnapshot<T>`（Task 2）、`MteEffectsSnapshot` のカテゴリ別 API（Task 5）。
- Produces: `static TextSnapshot TextSnapshot.Capture()` と `static TextSnapshot TextSnapshot.Capture(int index, Vector3 eulerAngles)`（指定テキストの回転だけ差し替えて捕捉）、`static SubCameraSnapshot SubCameraSnapshot.Capture()`、`static SoundSnapshot SoundSnapshot.Capture()`、`static VideoSnapshot VideoSnapshot.Capture()`。

- [ ] **Step 1: 4 クラスを実装**

`TextSnapshot.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>フリーテキスト全件のスナップショット (件数含む)</summary>
    public class TextSnapshot : PresetDtoSnapshot<List<ScenePresetText>>
    {
        public static TextSnapshot Capture() => (TextSnapshot)new TextSnapshot().Init();

        /// <summary>
        /// 指定テキストの回転だけ差し替えて捕捉する。
        /// 回転行 (DrawEulerAngles) は書き込みと描画が一体で変更前に割り込めないため、
        /// 呼び出し側が描画前に控えた角度をここで戻す
        /// </summary>
        public static TextSnapshot Capture(int index, Vector3 eulerAngles)
        {
            var snapshot = Capture();
            if (index >= 0 && index < snapshot.state.Count)
            {
                snapshot.state[index].rotation = eulerAngles;
            }
            return snapshot;
        }

        protected override List<ScenePresetText> CaptureState() => MteEffectsSnapshot.CaptureTexts();
        protected override void ApplyState(List<ScenePresetText> state) => MteEffectsSnapshot.ApplyTexts(state);
        protected override PresetDtoSnapshot<List<ScenePresetText>> CreateEmpty() => new TextSnapshot();
    }
}
```

`SubCameraSnapshot.cs`:

```csharp
using System.Collections.Generic;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>サブカメラ全台のスナップショット (台数含む)</summary>
    public class SubCameraSnapshot : PresetDtoSnapshot<List<ScenePresetSubCamera>>
    {
        public static SubCameraSnapshot Capture() => (SubCameraSnapshot)new SubCameraSnapshot().Init();

        protected override List<ScenePresetSubCamera> CaptureState() => MteEffectsSnapshot.CaptureSubCameras();
        protected override void ApplyState(List<ScenePresetSubCamera> state) => MteEffectsSnapshot.ApplySubCameras(state);
        protected override PresetDtoSnapshot<List<ScenePresetSubCamera>> CreateEmpty() => new SubCameraSnapshot();
    }
}
```

`SoundSnapshot.cs`:

```csharp
namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>BGM ファイル設定のスナップショット。ゲーム BGM の再生状態は含まない (再生操作は履歴に載せない)</summary>
    public class SoundSnapshot : PresetDtoSnapshot<ScenePresetSound>
    {
        public static SoundSnapshot Capture() => (SoundSnapshot)new SoundSnapshot().Init();

        protected override ScenePresetSound CaptureState() => MteEffectsSnapshot.CaptureBgmSettings();
        protected override void ApplyState(ScenePresetSound state) => MteEffectsSnapshot.ApplyBgmSettings(state);
        protected override PresetDtoSnapshot<ScenePresetSound> CreateEmpty() => new SoundSnapshot();
    }
}
```

`VideoSnapshot.cs`:

```csharp
using System.Collections.Generic;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>動画全本のスナップショット (本数含む)。適用は変わった本だけ再読込する</summary>
    public class VideoSnapshot : PresetDtoSnapshot<List<ScenePresetVideo>>
    {
        public static VideoSnapshot Capture() => (VideoSnapshot)new VideoSnapshot().Init();

        protected override List<ScenePresetVideo> CaptureState() => MteEffectsSnapshot.CaptureVideos();
        protected override void ApplyState(List<ScenePresetVideo> state) => MteEffectsSnapshot.ApplyVideos(state, reloadAll: false);
        protected override PresetDtoSnapshot<List<ScenePresetVideo>> CreateEmpty() => new VideoSnapshot();
    }
}
```

- [ ] **Step 2: csproj に 4 ファイルを追加**

```xml
    <Compile Include="Manager\History\TextSnapshot.cs" />
    <Compile Include="Manager\History\SubCameraSnapshot.cs" />
    <Compile Include="Manager\History\SoundSnapshot.cs" />
    <Compile Include="Manager\History\VideoSnapshot.cs" />
```

- [ ] **Step 3: 両構成でビルド**

Expected: `error CS` なし。

- [ ] **Step 4: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin/Manager/History/TextSnapshot.cs source/COM3D2.SceneEditor.Plugin/Manager/History/SubCameraSnapshot.cs source/COM3D2.SceneEditor.Plugin/Manager/History/SoundSnapshot.cs source/COM3D2.SceneEditor.Plugin/Manager/History/VideoSnapshot.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj
git commit -m "feat(history): 演出系 (テキスト・サブカメラ・BGM 設定・動画) のスナップショットを追加する"
```

---

### Task 7: テキストウィンドウの履歴対応

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/TextRowDrawer.cs:66-185`
- Modify: `source/COM3D2.SceneEditor.Plugin/TextWindow.cs:168-190`

**Interfaces:**
- Consumes: `TextSnapshot.Capture()` / `TextSnapshot.Capture(int, Vector3)`、`HistoryManager.BeforeEdit(null, HistoryScope.Text, desc, null, capture)`。
- Produces: `TextRowDrawer.Draw(GUIView view, MTEP.FreeTextSet freeTextSet, float rowHeight, string boneName, int textIndex)` — 回転行の差し替え捕捉に添字が要る。

- [ ] **Step 1: `TextRowDrawer.Draw` に `int textIndex` を追加し、記録クロージャを置く**

`Draw` のシグネチャ末尾に `int textIndex` を足す。`var rect = freeTextSet.rect;` の直後に:

```csharp
            // 値を書き込む直前に呼ぶ。テキスト全件を 1 スナップショットで持つため対象キーは不要
            Action<string> recordEdit = label => HistoryManager.instance.BeforeEdit(
                null, HistoryScope.Text, "テキスト: " + label, null, () => TextSnapshot.Capture());
```

各ハンドラの書き込み直前に差す:

- 本文: `onChanged = value => { recordEdit("本文"); text.text = value; }`
- フォント: `_fontNameComboBox.onSelected` 内の先頭に `recordEdit("フォント");`
- サイズ / 行間: `value => { recordEdit("サイズ"); text.fontSize = value; }`、`value => { recordEdit("行間"); text.lineSpacing = value; }`
- 整列: `onSelected` 内の先頭に `recordEdit("整列");`
- 幅 / 高さ: 各ラムダの先頭に `recordEdit("幅");` / `recordEdit("高さ");`
- 色: `value => { recordEdit("色"); text.color = value; }`

`DrawTransformRows(view, rect, boneName)` は `DrawTransformRows(view, rect, boneName, textIndex, recordEdit)` に変え、シグネチャを `(GUIView view, RectTransform rect, string boneName, int textIndex, Action<string> recordEdit)` にする。内部:

- 位置: `if (DrawTransformVector3(...)) { recordEdit("位置"); transformCache.position = position; transformCache.Apply(); }`
- 回転: 描画前に `var prevEulerAngles = rect.eulerAngles;` を控え、`if (MTEP.TimelineLayerBase.DrawEulerAngles(...))` の戻り値を受けて true なら:

```csharp
            {
                // DrawEulerAngles は描画中に書き込むため、描画前の角度で変更前を捕捉する
                HistoryManager.instance.BeforeEdit(null, HistoryScope.Text, "テキスト: 回転", null,
                    () => TextSnapshot.Capture(textIndex, prevEulerAngles));
            }
```

- 拡縮: `if (...) { recordEdit("拡縮"); transformCache.scale = scale; transformCache.Apply(); }`

- [ ] **Step 2: 呼び出し側を更新**

- `TextWindow.cs:158` 付近: `.Draw(_view, textManager.GetFreeTextSet(_textIndex), ROW_HEIGHT, boneName)` → 末尾に `, _textIndex` を追加。
- `Timeline/ItemInspector/TextItemInspector.cs` の `Draw` 呼び出しも同様に添字を渡す（項目名 `TextBoneName + i` から `i` を取っている箇所を確認して渡す）。

- [ ] **Step 3: 件数行を記録する**

`TextWindow.SetTextCount(int count)` の先頭に:

```csharp
            HistoryManager.instance.BeforeEdit(null, HistoryScope.Text, "テキスト: 表示数", null,
                () => TextSnapshot.Capture());
```

- [ ] **Step 4: 両構成でビルド、実機確認**

テキストの本文・サイズ・位置・回転・件数を変更 → 履歴に載り、Ctrl+Z で戻る。本文はキー入力 1 文字ごとに 1 件になる（マウス非押下で即確定する既存仕様）。

- [ ] **Step 5: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin/TextRowDrawer.cs source/COM3D2.SceneEditor.Plugin/TextWindow.cs source/COM3D2.SceneEditor.Plugin/Timeline/ItemInspector/TextItemInspector.cs
git commit -m "feat(text): テキスト編集を操作履歴に載せる"
```

---

### Task 8: サブカメラの履歴対応

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/SubCameraRowDrawer.cs:9-16, 30-131`
- Modify: `source/COM3D2.SceneEditor.Plugin/CameraWindow.cs:698-705`

**Interfaces:**
- Consumes: `SubCameraSnapshot.Capture()`。
- `MaidFollowRowDrawer.Draw(view, follow.state, labelWidth, rowHeight)` の内部書き込みは追従設定（`state`）へ行われる。`MaidFollowRowDrawer` の `Draw` に `Action onBeforeChange` 引数があるかを最初に確認し、無ければ追加して各書き込み直前で呼ぶ（ライトの追従行と共用のため、引数は末尾の任意引数 `Action onBeforeChange = null` とし、既存呼び出しを壊さない）。

- [ ] **Step 1: `MaidFollowRowDrawer` を確認して `onBeforeChange` を通す**

`sed -n 1,80p MaidFollowRowDrawer.cs` で書き込み箇所（`state.maidSlotNo = ...` 等）を確認し、`Draw` 末尾に `Action onBeforeChange = null` を追加。各書き込み直前に `onBeforeChange?.Invoke();`（`?.` は `HistoryManager.cs` で既に使用している）。

- [ ] **Step 2: `SubCameraRowDrawer` に記録を差す**

クラスコメントの「書き込み先はどのスナップショットにも含まれないため履歴は記録しない (レイヤー側の UI も記録していない)」の 2 行を削除し、`Draw` の `var follow = cameraData.follow;` の直後に:

```csharp
            // 値を書き込む直前に呼ぶ。サブカメラ全台を 1 スナップショットで持つため対象キーは不要
            Action<string> recordEdit = label => HistoryManager.instance.BeforeEdit(
                null, HistoryScope.SubCamera, "サブカメラ: " + cameraData.name + " " + label,
                null, () => SubCameraSnapshot.Capture());
```

- 有効: `newValue => { recordEdit("有効"); cameraData.visible = newValue; }`
- 追従: `_followRowDrawer.Draw(view, follow.state, labelWidth, rowHeight, () => recordEdit("追従"));`
- 位置: `value => { recordEdit("位置"); cameraData.position = value; }`、リセットも同様
- 回転: `DrawRotationRow(view, cameraData, follow, labelWidth, rowHeight, recordEdit)` にして、内部の `follow.eulerAnglesOffset = value` と `SetWorldEulerAngles(...)` の各直前に `recordEdit("回転");`
- FoV: `value => { recordEdit("FoV"); camera.fieldOfView = value; }`
- ビューポート: `DrawViewportRows` 内の `if (updated)` の先頭に `recordEdit("ビューポート");`（`viewportRect` はローカルコピーなので、`UpdateCameraViewport` の前に呼べば変更前を捕捉できる）

- [ ] **Step 3: 台数行を記録する**

`CameraWindow.DrawSubCameraCountRow` のラムダを:

```csharp
                x =>
                {
                    HistoryManager.instance.BeforeEdit(null, HistoryScope.SubCamera, "サブカメラ: 台数",
                        null, () => SubCameraSnapshot.Capture());
                    subCameraManager.SetCameraCount(x);
                });
```

- [ ] **Step 4: 両構成でビルド、実機確認**

サブカメラの位置・FoV・ビューポート・台数を変更 → 履歴に載り Ctrl+Z で戻る。回転を Ctrl+Z で戻した直後にもう一度回転をドラッグし、表示角度が飛ばないこと（`EulerOffsetCache` が Undo に追随しない懸念の確認。飛ぶ場合は `SubCameraSnapshot.Apply` 後に `_offsetCache` を捨てる対処を追加する）。

- [ ] **Step 5: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin/SubCameraRowDrawer.cs source/COM3D2.SceneEditor.Plugin/MaidFollowRowDrawer.cs source/COM3D2.SceneEditor.Plugin/CameraWindow.cs
git commit -m "feat(camera): サブカメラ編集を操作履歴に載せる"
```

---

### Task 9: 音声（BGM 設定）と動画の履歴対応

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/SoundWindow.cs:192-297`
- Modify: `source/COM3D2.SceneEditor.Plugin/VideoWindow.cs:88-98, 150-156, 174-231, 262-310, 354-535`

- [ ] **Step 1: `SoundWindow.DrawBgmFileSection` に記録を差す**

`var settings = bgmManager.settings;` の直後に:

```csharp
            // 値を書き込む直前に呼ぶ。ゲーム BGM の再生は記録しない (再生操作のため)
            Action<string> recordEdit = label => HistoryManager.instance.BeforeEdit(
                null, HistoryScope.Sound, "BGM: " + label, null, () => SoundSnapshot.Capture());
```

- 選択ダイアログ OK 時: `settings.bgmPath = ...` の前に `recordEdit("パス");`
- パス欄: `newText => { recordEdit("パス"); settings.bgmPath = newText; }`
- BPM ライン表示: `newValue => { recordEdit("BPMライン表示"); settings.isShowBPMLine = newValue; }`
- BPM: `value => { recordEdit("BPM"); settings.bpm = value; }`
- オフセット: `value => { recordEdit("オフセット"); settings.bpmLineOffsetFrame = value; }`
- 音量スライダー（`bgmManager.volumeDance`）は MTE 設定値（`timelineConfig`）であり `ScenePresetSound` に無いため記録しない。

`using System;` が無ければ追加。

- [ ] **Step 2: `VideoWindow` に記録を差す**

クラスのフィールド付近（`settings` プロパティの近く）に:

```csharp
        /// <summary>値を書き込む直前に呼ぶ。動画全本を 1 スナップショットで持つため対象キーは不要</summary>
        private void RecordEdit(string label)
        {
            HistoryManager.instance.BeforeEdit(null, HistoryScope.Video,
                "動画" + (_videoIndex + 1) + ": " + label, null, () => VideoSnapshot.Capture());
        }
```

差し込み先:

- 表示形式コンボ `onSelected`（:93-97）: 先頭に `RecordEdit("表示形式");`
- 本数（:152-155）: `count => { RecordEdit("動画数"); movieManager.videoCount = count; }`（この行のラベルは `_videoIndex` に依らない「動画数」で十分だが、`RecordEdit` の接頭辞が付いても害はない）
- 有効トグル（:180）: 先頭に `RecordEdit("有効");`
- パス選択 OK 時（:217）: `settings.path = ...` の前に `RecordEdit("パス");`
- パス欄（:231）: `newText => { RecordEdit("パス"); settings.path = newText; }`
- 開始位置 `onChanged` / `onReset`、音量 `onChanged` / `onReset`（:281-310）: 各先頭に `RecordEdit("開始位置");` / `RecordEdit("音量");`
- GUI 拡縮 / 透明度（:366, :379）: `value => { RecordEdit("GUI拡縮"); settings.guiScale = value; }` 等
- Mesh 位置 / 回転 / 拡縮 / 透明度（:383-445）: 各 `onChanged` / `onReset` の先頭に `RecordEdit("位置")` 等
- Backmost / Frontmost の位置 / 拡縮 / 透明度（:446-535）: 同様

`DrawPositionRow(view, value, defaultValue, onChanged)` は `onReset = () => onChanged(defaultValue)` なので、`onChanged` 側に差せばリセットも拾える。

- [ ] **Step 3: 両構成でビルド、実機確認**

- BGM: BPM を変えて Ctrl+Z → 曲が止まらずに BPM だけ戻る。パスを変えて Ctrl+Z → 元のファイルへ読み直る。
- 動画: 音量・位置を変えて Ctrl+Z → 再読込なしで戻る（ログに読込が出ない）。パスを変えて Ctrl+Z → その本だけ再読込される。本数変更 → Ctrl+Z で戻る。

- [ ] **Step 4: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin/SoundWindow.cs source/COM3D2.SceneEditor.Plugin/VideoWindow.cs
git commit -m "feat(sound,video): BGM 設定と動画設定を操作履歴に載せる"
```

---

### Task 10: ドキュメント更新

**Files:**
- Modify: `docs-site/guide/timeline.md:63`
- Modify: `docs-site/guide/staging.md:41-44`
- Modify: `docs/superpowers/specs/timeline-window-roadmap.md`（「履歴未対応ウィンドウは対象外」の記述があれば）

- [ ] **Step 1: `timeline.md:63` を書き換える**

旧:
> 操作履歴（Undo）に対応していないウィンドウ（マテリアル編集・シェイプキー編集・演出系・音声・テキスト・動画）での変更は対象外なので、「登録」ボタンか `Return` で登録してください。

新:
> 操作履歴（Undo）に対応していないウィンドウ（ライブ演出：ステージライト・レーザー・サイリウム）での変更は対象外なので、「登録」ボタンか `Return` で登録してください。

- [ ] **Step 2: `staging.md` の操作履歴節に対応範囲を 1 行足す**

「主要な操作は履歴に記録され、…」の段落の後に:

> マテリアル編集・シェイプキー編集・サブカメラ・テキスト・BGM 設定・動画の各ウィンドウでの値変更も記録されます。ライブ演出（ステージライト・レーザー・サイリウム）と、ボイス / 効果音 / ゲーム BGM の再生操作は記録されません。

- [ ] **Step 3: roadmap の該当記述を grep して更新**

Run: `grep -n "履歴未対応\|履歴に対応していない" docs/superpowers/specs/timeline-window-roadmap.md`
該当行があれば「ライブ演出を除き対応済み（2026-09-08）」へ書き換える。

- [ ] **Step 4: Commit**

```bash
git add docs-site/guide/timeline.md docs-site/guide/staging.md docs/superpowers/specs/timeline-window-roadmap.md
git commit -m "docs: 操作履歴の対応ウィンドウ一覧を更新する"
```

---

## 自己レビュー

- **仕様カバレッジ**: マテリアル (Task 3)、シェイプキー (Task 4)、演出系のうちサブカメラ (Task 8)、テキスト (Task 7)、音声 (Task 9)、動画 (Task 9)。ライブ演出とポストエフェクトは「対象外」節で理由付きで除外し、ドキュメント (Task 10) にも反映。
- **プレースホルダ**: Task 5 の `/* 既存の全項目コピー */` は既存コード (`MteEffectsSnapshot.cs:255-283, 286-320`) をそのまま移す指示であり、新規に書く内容は無い。
- **型整合**: `BeforeEdit(Maid, HistoryScope, string, object, Func<IStateSnapshot>)` を Task 3〜9 で同じ引数順で使用。`MaterialSnapshot.Capture(material, track, trackKey)`、`TextSnapshot.Capture(int, Vector3)`、`ApplyVideos(list, reloadAll)` の名前と引数は定義と呼び出しで一致。
- **既知の制限（仕様として受け入れる）**: テキスト本文のキー入力は 1 文字 1 エントリ。`historyLimit` 既定 20 のため長文入力で他の履歴が押し出される。

## レビュー却下メモ

- XmlSerializer の初回生成コスト（確信度: 低） — 未計測のため見送り。Task 7 の実機確認で初回確定時のヒッチを体感確認し、目立てばシリアライザを起動時に事前生成する。
- 着替え後に「更新」未押下でのマテリアル Undo の陳腐化（確信度: 中） — 実害が「見た目に反映されない」に留まるため、`CanApply` のコメントで既知の制限として明記するに留めた（世代チェックは追加しない）。
