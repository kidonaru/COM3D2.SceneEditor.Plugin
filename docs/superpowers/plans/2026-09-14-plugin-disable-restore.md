# プラグイン無効化時の復帰 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** プラグイン無効化でタイムラインをアンロードしても、再有効化で基盤マネージャの状態が復帰し、メインライトはプラグインが触る前の値へ戻るようにする。

**Architecture:** 復帰点を `OnLoad`（`timeline == null` で抜ける）から `OnPluginEnable`（ガード無しで配信される）へ移す。対象は「タイムラインと無関係な基盤」4 マネージャだけで、タイムライン由来の実体は未読込＝0 が正しい状態なので復帰させない。メインライトは SE 側がスナップショットを保持し、`TimelineManager` の直後に走る MTE 側 `StudioLightManager.OnPluginDisable` から書き戻す。

**Tech Stack:** C#（COM3D2 構成 = .NET 3.5 / COM3D25 構成 = .NET 4.7.1 の 2 構成ビルド）、xunit 2.9（net48、`source/COM3D2.SceneEditor.Plugin.Tests`）、MSBuild 直叩き。

**Spec:** `docs/superpowers/specs/2026-09-14-plugin-disable-restore-design.md`

## Global Constraints

- コードのコメントとエラーログメッセージは**日本語**で書く。
- **2 構成ともビルドする**。COM3D2 構成は .NET 3.5 なので `Tuple`、入力 5 個以上の `Func<>` / `Action<>` は使えない。
- ビルドは `debug.bat` を使わない（実機へ DLL をコピーするため）。MSBuild を直接叩く。
- Git Bash から MSBuild を叩くときは `export MSYS2_ARG_CONV_EXCL="*"` が必須。
- ビルド順は **COM3D2 → COM3D25 → `dotnet test`**。
- プラグイン csproj は旧形式。新規 `.cs` は `<Compile Include>` への追記が必須（テストプロジェクトは SDK 形式なので不要）。
- `deploy.bat` / `release.bat` は実行しない。
- メソッド名（テスト名を含む）に `-` は使えない。日本語識別子として妥当な名前にする。
- ハードコーディングは避ける。ゲーム側の既定値（`LightMain.Reset()` の白・0.95・(40,180,18)）を書き写さない。

**共通ビルドコマンド**（以降「共通ビルド」と書いたらこれ）:

```bash
export MSYS2_ARG_CONV_EXCL="*"
MSBUILD="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
cd W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin
"$MSBUILD" source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2  "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" /v:minimal \
&& "$MSBUILD" source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" /v:minimal \
&& dotnet test source/COM3D2.SceneEditor.Plugin.Tests
```

## ファイル構成

| ファイル | 責務 |
|---|---|
| `Manager/StudioLightManager.cs`（変更） | メインライトのスナップショット保持・復元 |
| `Timeline/Manager/StudioLightManager.cs`（変更） | 無効化でスナップショットを書き戻す / 再有効化でライト一覧を再構築 |
| `Timeline/Manager/CameraManager.cs`（変更） | 再有効化で `MTEFrontCamera` を作り直す |
| `Timeline/Manager/SubCameraManager.cs`（変更） | 台数復元を `OnLoad` から `OnPluginEnable` へ移す |
| `Timeline/Manager/TimelineTextManager.cs`（変更） | 再有効化でテキスト実体を作り直す |
| `COM3D2.SceneEditor.Plugin.Tests/ManagerPluginEnableTests.cs`（新規） | `OnPluginEnable` を実装したマネージャが期待表どおりか |
| `docs-site/timeline/files.md`（変更） | 再有効化時の挙動の記述 |

**新しいマネージャも新しい列挙も足さない。** `LightMain` のアクセサ（`GetColor` 等）は使わず、`Light` コンポーネントを直接読み書きする（`LightTimelineLayer` と同じ経路に揃えるため）。

---

### Task 1: メインライトのスナップショットを持たせる

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/StudioLightManager.cs`

**Interfaces:**
- Produces: `StudioLightManager.mainLightComponent`（`UnityEngine.Light`、取得できなければ `null`）
- Produces: `StudioLightManager.CaptureMainLightSnapshot()` — 未取得なら現在値を控える。取得済みなら何もしない
- Produces: `StudioLightManager.RestoreMainLightSnapshot()` — 控えた値を書き戻す。未取得なら何もしない

> Unity 実体に触れるためユニットテストで検証できない。挙動は Task 6 の実機確認で見る。

- [ ] **Step 1: 現在のフィールドとメソッドの並びを確認する**

Run: `grep -n "_lights\|_nextLightNumber\|public LightMain mainLight\|public override void Update()\|public override void OnChangedSceneLevel" source/COM3D2.SceneEditor.Plugin/Manager/StudioLightManager.cs`
Expected: `mainLight` プロパティ（35 行付近）、`Update()`、`OnChangedSceneLevel` の位置が分かる。以降の挿入位置はこの実測に合わせる

- [ ] **Step 2: `mainLightComponent` とスナップショットの器を足す**

`public LightMain mainLight { ... }` の直後へ足す。

```csharp
        /// <summary>
        /// メインライトの Light コンポーネント。
        /// LightMain のアクセサではなく Light を直接使うのは、
        /// LightTimelineLayer が書き込む対象と同じものを読み書きするため
        /// </summary>
        public Light mainLightComponent
        {
            get
            {
                var main = mainLight;
                return main != null ? main.GetComponent<Light>() : null;
            }
        }

        /// <summary>
        /// プラグインが触る前のメインライトの値。
        /// LightTimelineLayer が書き込む項目をすべて持つ
        /// </summary>
        private struct MainLightSnapshot
        {
            public Vector3 localPosition;
            public Quaternion localRotation;
            public Color color;
            public float range;
            public float intensity;
            public float spotAngle;
            public float shadowStrength;
            public float shadowBias;
            public int cullingMask;
            public bool enabled;
        }

        private bool _hasMainLightSnapshot = false;
        private MainLightSnapshot _mainLightSnapshot;
```

- [ ] **Step 3: 控える・戻すメソッドを足す**

`ClearAll()` の直前へ足す。

```csharp
        /// <summary>
        /// メインライトの値を控える。既に控えていれば何もしない。
        /// 有効化時ではなく Update から呼ぶのは、SceneEditorPlugin.OnPluginEnable が
        /// managerRegistry.OnPluginEnable より先に OnLoad (タイムラインの値を適用する) を
        /// 呼ぶため、有効化の時点では既にプラグインの値に染まっているからである
        /// </summary>
        public void CaptureMainLightSnapshot()
        {
            if (_hasMainLightSnapshot)
            {
                return;
            }

            var light = mainLightComponent;
            if (light == null)
            {
                return;
            }

            var transform = light.transform;
            _mainLightSnapshot = new MainLightSnapshot
            {
                localPosition = transform.localPosition,
                localRotation = transform.localRotation,
                color = light.color,
                range = light.range,
                intensity = light.intensity,
                spotAngle = light.spotAngle,
                shadowStrength = light.shadowStrength,
                shadowBias = light.shadowBias,
                cullingMask = light.cullingMask,
                enabled = light.enabled,
            };
            _hasMainLightSnapshot = true;
        }

        /// <summary>控えた値へ戻す。控える前なら何もしない</summary>
        public void RestoreMainLightSnapshot()
        {
            if (!_hasMainLightSnapshot)
            {
                return;
            }

            var light = mainLightComponent;
            if (light == null)
            {
                return;
            }

            var transform = light.transform;
            transform.localPosition = _mainLightSnapshot.localPosition;
            transform.localRotation = _mainLightSnapshot.localRotation;
            light.color = _mainLightSnapshot.color;
            light.range = _mainLightSnapshot.range;
            light.intensity = _mainLightSnapshot.intensity;
            light.spotAngle = _mainLightSnapshot.spotAngle;
            light.shadowStrength = _mainLightSnapshot.shadowStrength;
            light.shadowBias = _mainLightSnapshot.shadowBias;
            light.cullingMask = _mainLightSnapshot.cullingMask;
            light.enabled = _mainLightSnapshot.enabled;
        }
```

- [ ] **Step 4: `Update()` から控える**

変更前:
```csharp
        public override void Update()
        {
            // 外部要因（シーン側の破棄等）で消えたライトをリストへ残さない
            _lights.RemoveAll(light => light == null);
        }
```

変更後:
```csharp
        public override void Update()
        {
            // メインライトはシーンによっては後から現れるので、取れるまで毎フレーム試す
            CaptureMainLightSnapshot();

            // 外部要因（シーン側の破棄等）で消えたライトをリストへ残さない
            _lights.RemoveAll(light => light == null);
        }
```

- [ ] **Step 5: シーン遷移で控え直させる**

変更前:
```csharp
        public override void OnChangedSceneLevel(Scene scene, LoadSceneMode sceneMode)
        {
            // Additive ロードでは旧シーンの GameObject が破棄されず、参照を捨てるだけだと
            // ライトが残留したまま次の AddLight で二重生成される。明示的に破棄する
            ReleaseAll();
        }
```

変更後:
```csharp
        public override void OnChangedSceneLevel(Scene scene, LoadSceneMode sceneMode)
        {
            // Additive ロードでは旧シーンの GameObject が破棄されず、参照を捨てるだけだと
            // ライトが残留したまま次の AddLight で二重生成される。明示的に破棄する
            ReleaseAll();

            // シーンが変わるとメインライトも別の実体になるので控え直す
            _hasMainLightSnapshot = false;
        }
```

- [ ] **Step 6: 共通ビルドを通す**

Run: 共通ビルド
Expected: 2 構成ともビルド成功、テスト全 PASS（この時点では挙動は変わらない）

- [ ] **Step 7: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Manager/StudioLightManager.cs
git commit -m "feat(light): メインライトの初期値を控える"
```

---

### Task 2: 無効化でメインライトを戻し、再有効化でライト一覧を作り直す

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/StudioLightManager.cs`

**Interfaces:**
- Consumes: `SE.StudioLightManager.RestoreMainLightSnapshot()`（Task 1）
- Consumes: `StudioLightManager.LateUpdate(bool force)`（既存。`force = true` なら `currentLayer` を触らずに一覧を作り直す）

> `LateUpdate(true)` が `currentLayer` を参照しないことが前提。`force` が false のときだけ
> `currentLayer.isAnmPlaying` を見るので、タイムライン未読込でも安全に呼べる。

- [ ] **Step 1: `LateUpdate(bool)` が force 指定で currentLayer を触らないことを確認する**

Run: `sed -n '100,112p' source/COM3D2.SceneEditor.Plugin/Timeline/Manager/StudioLightManager.cs`
Expected: `if (!force) { if (Time.frameCount < _prevUpdateFrame + 30 || currentLayer.isAnmPlaying) return; }` の形。`force = true` なら `currentLayer` に触れない

- [ ] **Step 2: 無効化でメインライトを戻す**

変更前:
```csharp
        public override void OnPluginDisable()
        {
            Reset();
        }
```

変更後:
```csharp
        public override void OnPluginDisable()
        {
            Reset();

            // タイムラインが書いたメインライトの値を、プラグインが触る前へ戻す。
            // TimelineUpdateManager の並びで TimelineManager の直後に呼ばれるので、
            // アンロードの断面復元が書いた値をここで上書きできる
            seLightManager.RestoreMainLightSnapshot();
        }
```

`Reset()` には足さない。`Reset()` は `OnChangedSceneLevel` からも呼ばれ、そこではメインライトを戻す意味がないため。

- [ ] **Step 3: 再有効化でライト一覧を作り直す**

`OnPluginDisable()` の直前へ足す。

```csharp
        /// <summary>
        /// 再有効化でライト一覧を作り直す。メインライトはタイムラインが無くても
        /// 一覧へ載るので、timeline 必須の OnLoad 任せにはできない
        /// </summary>
        public override void OnPluginEnable()
        {
            LateUpdate(true);
        }
```

- [ ] **Step 4: 共通ビルドを通す**

Run: 共通ビルド
Expected: 2 構成ともビルド成功、テスト全 PASS

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/Manager/StudioLightManager.cs
git commit -m "fix(timeline): 再有効化でライト一覧とメインライトを戻す"
```

---

### Task 3: カメラ・サブカメラ・テキストを再有効化で戻す

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/CameraManager.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/SubCameraManager.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineTextManager.cs`

**Interfaces:**
- Consumes: `CameraManager.CreateCamera()`（private、同クラス内から呼ぶ）
- Consumes: `SubCameraManager.EnsureCameraCount(int)`（private）と `_restoreCameraCount`
- Consumes: `TimelineTextManager.InitTexts()`（`timeline` が null なら `_standaloneTextCount` で動く）

- [ ] **Step 1: カメラを再有効化で作り直す**

`CameraManager.cs` の `OnPluginDisable()` の直前へ足す。

```csharp
        /// <summary>
        /// 再有効化で MTEFrontCamera を作り直す。動画の最前面表示と
        /// レターボックスが使う実体で、タイムラインの有無とは無関係に要るため
        /// timeline 必須の OnLoad 任せにはできない
        /// </summary>
        public override void OnPluginEnable()
        {
            CreateCamera();
        }
```

- [ ] **Step 2: サブカメラの台数復元を OnPluginEnable へ移す**

変更前:
```csharp
        public override void OnLoad()
        {
            base.OnLoad();

            // プラグイン再有効化時はカメラ破棄済みのため、無効化前と同数を復元する
            EnsureCameraCount(_restoreCameraCount);
        }
```

変更後:
```csharp
        public override void OnLoad()
        {
            base.OnLoad();
        }

        /// <summary>
        /// 再有効化でカメラを無効化前と同数まで戻す。
        /// _restoreCameraCount は無効化時に控えた台数なので、復元先は OnLoad ではなくここ
        /// (タイムラインをアンロードすると OnLoad は timeline == null で呼ばれない)
        /// </summary>
        public override void OnPluginEnable()
        {
            base.OnPluginEnable();

            EnsureCameraCount(_restoreCameraCount);
        }
```

- [ ] **Step 3: `base.OnLoad()` だけになった OnLoad を消すか判断する**

Run: `grep -n "public override void OnLoad" -A 4 source/COM3D2.SceneEditor.Plugin/Timeline/Manager/SubCameraManager.cs`
Expected: 中身が `base.OnLoad();` だけになっている。基底の `ManagerBase.OnLoad()` が空なら override ごと削除する。基底に処理があるなら残す

基底を確認する。

Run: `grep -n "public virtual void OnLoad()" -A 4 source/COM3D2.SceneEditor.Plugin/Timeline/Manager/ManagerBase.cs`

- [ ] **Step 4: テキストを再有効化で作り直す**

`TimelineTextManager.cs` の `OnPluginDisable()` の直前へ足す。

```csharp
        /// <summary>
        /// 再有効化でテキスト実体を作り直す。textCount はタイムライン未読込でも
        /// _standaloneTextCount で決まるので OnLoad 任せにはできない
        /// </summary>
        public override void OnPluginEnable()
        {
            InitTexts();
        }
```

- [ ] **Step 5: 共通ビルドを通す**

Run: 共通ビルド
Expected: 2 構成ともビルド成功、テスト全 PASS

- [ ] **Step 6: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/Manager/
git commit -m "fix(timeline): 再有効化でカメラとテキストを戻す"
```

---

### Task 4: 復帰対象の表をテストで固定する

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin.Tests/ManagerPluginEnableTests.cs`

**Interfaces:**
- Consumes: `COM3D2.MotionTimelineEditor.IManager`（マネージャの共通インターフェース）

> マネージャを増やしたときに「再有効化で戻すのか戻さないのか」を判断させるためのテスト。
> Unity 実体には触れず、override の有無だけをリフレクションで見る。

- [ ] **Step 1: IManager の名前空間を確認する**

Run: `grep -rn "interface IManager" source/COM3D2.SceneEditor.Plugin/ --include=*.cs | grep -v "/obj/\|/bin/"`
Expected: `MTEUtils/ManagerRegistry.cs` に定義がある。名前空間を控え、テストの `using` をこれに合わせる

- [ ] **Step 2: 期待表のテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/ManagerPluginEnableTests.cs` を新規作成する。
`using` の名前空間は Step 1 の実測に合わせること。

**キーは短縮名ではなく `FullName` を使う。** `StudioLightManager` は
SE 側（`COM3D2.SceneEditor.Plugin`）と MTE 側（`COM3D2.MotionTimelineEditor.Plugin`）の
2 つが同一アセンブリ内にあり、どちらも `IManager` を実装している。本計画で
`OnPluginEnable` を足すのは MTE 側だけなので、短縮名キーでは SE 側が必ず不一致になる。

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using COM3D2.MotionTimelineEditor;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class ManagerPluginEnableTests
    {
        // 設計メモ「決定事項」の表。
        // タイムラインと無関係な基盤だけが再有効化で戻す。
        // 空実装を持つ 3 つは「タイムライン由来なので戻さない」という判断の表明として含める。
        // SE 側にも同名の StudioLightManager があるため、キーは FullName で持つ
        private static readonly HashSet<string> EXPECTED_OVERRIDES = new HashSet<string>
        {
            "COM3D2.MotionTimelineEditor.Plugin.StudioLightManager",
            "COM3D2.MotionTimelineEditor.Plugin.CameraManager",
            "COM3D2.MotionTimelineEditor.Plugin.SubCameraManager",
            "COM3D2.MotionTimelineEditor.Plugin.TimelineTextManager",
            "COM3D2.MotionTimelineEditor.Plugin.StageLightManager",
            "COM3D2.MotionTimelineEditor.Plugin.StageLaserManager",
            "COM3D2.MotionTimelineEditor.Plugin.PsylliumManager",
        };

        private static List<Type> GetConcreteManagerTypes()
        {
            var assembly = typeof(IManager).Assembly;
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
                .Where(t => !t.IsAbstract && !t.IsInterface && typeof(IManager).IsAssignableFrom(t))
                .ToList();
        }

        private static bool HasOwnOnPluginEnable(Type type)
        {
            var method = type.GetMethod("OnPluginEnable", Type.EmptyTypes);
            return method != null && method.DeclaringType == type;
        }

        [Fact]
        public void 再有効化で戻すマネージャが期待どおり()
        {
            var types = GetConcreteManagerTypes();
            Assert.NotEmpty(types);

            foreach (var type in types)
            {
                var expected = EXPECTED_OVERRIDES.Contains(type.FullName);
                var actual = HasOwnOnPluginEnable(type);
                Assert.True(expected == actual,
                    type.FullName + " の OnPluginEnable 実装が期待と違う: expected=" + expected + " actual=" + actual);
            }
        }

        [Fact]
        public void 期待表のマネージャ型がすべて実在する()
        {
            var names = GetConcreteManagerTypes().Select(t => t.FullName).ToList();
            foreach (var name in EXPECTED_OVERRIDES)
            {
                Assert.Contains(name, names);
            }
        }
    }
}
```

- [ ] **Step 3: テストを走らせる**

Run: `dotnet test source/COM3D2.SceneEditor.Plugin.Tests --filter "ManagerPluginEnableTests"`
Expected: PASS。FAIL したら失敗メッセージが挙げる型の `FullName` を見て、
**実装ではなく期待表が正しいかを先に考える**。マネージャを増やしたときは
「再有効化で戻す必要があるか」を判断してから期待表へ足すこと

- [ ] **Step 4: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin.Tests/ManagerPluginEnableTests.cs
git commit -m "test(timeline): 再有効化で戻すマネージャの表を固定する"
```

---

### Task 5: ドキュメントを更新する

**Files:**
- Modify: `docs-site/timeline/files.md`

- [ ] **Step 1: アンロードの節へ再有効化の挙動を足す**

変更前:
```markdown
- **プラグインの表示を閉じるとタイムラインもアンロードされます**（ギアメニュー、`プラグイン表示切替` のホットキー、メニューバーの閉じるボタン）。確認は出ないので、閉じる前に保存してください
```

変更後:
```markdown
- **プラグインの表示を閉じるとタイムラインもアンロードされます**（ギアメニュー、`プラグイン表示切替` のホットキー、メニューバーの閉じるボタン）。確認は出ないので、閉じる前に保存してください
- 閉じるとメインライトはプラグインを起動する前の色・明るさ・角度へ戻ります。`ライト` レイヤーを使っていなくても戻ります
- 開き直すとタイムライン未読込の状態で立ち上がります。タイムラインで増やした実体（ステージライト、モデル、追加ライトなど）は戻りません
- サブカメラの台数と字幕テキストの表示数は、閉じる前の状態のまま開き直せます
```

- [ ] **Step 2: docs-review スキルでレビューする**

Run: `docs-review` スキル
Expected: 指摘なし、または指摘を取り込んで書き直し済み

- [ ] **Step 3: コミット**

```bash
git add docs-site/timeline/files.md
git commit -m "docs(timeline): 無効化でメインライトが戻ることを記載する"
```

---

### Task 6: 実機で挙動を確認する

**Files:** なし（検証のみ）

ビルド済み DLL を `%COM3D25_DIR%\Sybaris\UnityInjector\` へ反映してゲームを再起動する（反映はユーザーに依頼する）。
各手順のあとに `tail_log` で例外が出ていないかを見る。

- [ ] **Step 1: メインライトが戻るか確認する**

1. プラグインを起動した直後のメインライトの色・明るさ・角度を控える
2. タイムラインを読み込み、`ライト` レイヤーでメインライトの色を変える
3. プラグインを無効化する

Expected: メインライトが 1 で控えた値へ戻る。シーンが真っ暗にならない

- [ ] **Step 2: ライト一覧が戻るか確認する**

Step 1 のあとプラグインを再有効化する。

Expected: ライトウィンドウにメインライトが 1 灯だけ並ぶ（追加ライトはタイムライン由来なので戻らない）

- [ ] **Step 3: カメラが戻るか確認する**

Step 2 の状態で GameView とシーン表示が正常に描画されているか見る。

Expected: `MTEFrontCamera` が作り直され、描画が壊れていない

- [ ] **Step 4: サブカメラが戻るか確認する**

1. `サブカメラ` レイヤーを作り台数を 3 にする
2. 無効化 → 再有効化

Expected: サブカメラが 3 台へ戻る

- [ ] **Step 5: テキストが戻るか確認する**

1. `テキスト` レイヤーを作り表示数を 2 にする
2. 無効化 → 再有効化

Expected: テキスト実体が作り直される（タイムラインはアンロードされているので表示数は既定の 1）

- [ ] **Step 6: タイムライン由来の実体が戻らないことを確認する**

ステージライトとモデルを置いた状態で無効化 → 再有効化する。

Expected: どちらも戻らない（アンロードされたので正しい）。例外も出ない

- [ ] **Step 7: 無効化・有効化を続けて繰り返す**

ホットキーで 5 回ほど連続してトグルする。

Expected: 例外が出ない。メインライトの値が回を追うごとにずれていかない（スナップショットが上書きされていないこと）

- [ ] **Step 8: ライトレイヤーを使わない場合の巻き戻りを確認する**

設計メモ「許容するトレードオフ」の確認。

1. プラグインを起動し、タイムラインを読み込まないまま、プラグインのライトウィンドウでメインライトの色を変える
2. プラグインを無効化する

Expected: メインライトが起動時の色へ戻る（`ライト` レイヤーを使っていなくても戻るのが仕様）。
**この挙動が受け入れられないようなら、ここで実装を止めてユーザーへ判断を仰ぐ**

- [ ] **Step 9: 有効化直後に即ロードして確認する**

有効化した直後（1〜2 秒以内）にタイムラインを読み込む。

Expected: メインライトのスナップショットがタイムライン適用後の値で上書きされていない。
つまり続けて無効化すると、有効化直前の色へ戻る。
`CaptureMainLightSnapshot()` は `Update()` から呼ばれ、タイムラインの適用は
`LateUpdate` 系なので理屈の上では安全だが、実機で裏を取る

- [ ] **Step 10: タイトルへ戻る経路を確認する**

タイムラインを読み込んだ状態でタイトル画面へ戻る。

Expected: `SceneEditorHack.isTitleScene` のガードでアンロードが走らず、例外も出ない

- [ ] **Step 11: 結果を記録する**

確認結果をこの計画書の本セクションへ追記する（PASS / 差異）。

---

## 完了条件

- 2 構成（COM3D2 / COM3D25）ともビルドが通る
- `dotnet test` が全 PASS
- Task 6 の実機確認 10 項目がすべて期待どおり
- `docs-site/timeline/files.md` の記述が実装と一致している

## 先行実装との関係

- `2e6ca99`（プラグイン無効化でアンロードする）は残す。本計画はその退行を塞ぐもの
- `TimelineUpdateManager.OnLoad` の `timeline == null` ガードは変えない。タイムライン由来の復元は
  タイムラインがあるときだけ走るのが正しい

## レビュー却下メモ

- **`MaidManager` も復帰対象に入れるべきでは** — 対象外。`TimelineUpdateManager.UpdateGuards()` が
  ガード判定より先に `maidManager.PreUpdate()` を呼び、`PreUpdate` が `IsValid()` を見て自力で
  `Reset()` / 再構築する。毎フレーム自己修復するので復帰点は要らない
- **`PostEffectManager` も復帰対象に入れるべきでは** — 対象外。`InitPostEffects()` は
  `timeline != null` のときだけ実体数を合わせる。タイムライン未読込では無効のままが正しい状態
- **メインライトの復元が「今回タイムラインを触ったか」を区別せず無条件に走る** — 仕様として許容する。
  判定手段が無い（`LightTimelineLayer` は `Light` へ直接書くのでフックできず、プラグインの
  ライトウィンドウ経由の変更と区別できない）ため。設計メモ「許容するトレードオフ」へ理由を記載し、
  Task 6 Step 8 で実機の挙動をユーザーに見せて最終判断を仰ぐ
- **`CameraManager.OnPluginEnable` が動画を使わないセッションでも毎回カメラを作る** — 許容する。
  `2e6ca99` 以前は `OnLoad` 経由で毎回再生成されていたので挙動は元に戻るだけ。
  コメントには実態（動画の最前面表示とレターボックス用）を書いた
