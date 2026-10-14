# 身長補正のマネージャ化 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use executing-plans to implement this plan task-by-task (このワークスペースでは subagent-driven-development を使わない). Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 身長補正の設定（基準メイド・基準身長）を、手ブレの `CameraShakeManager` と同じくタイムラインから独立した `CameraHeightManager` に持たせ、シーンプリセットでも基準メイドが戻り、タイムライン未読込でも設定・保存できるようにする。

**Architecture:** 既存ブランチ `feature/camera-preset-height-correction` の続き。基準身長の辞書を Unity 非依存の `CameraHeightBases` に切り出し、`CameraHeightManager`（`ManagerBase`）が `CameraHeightState` と `CameraHeightBases` を 1 つずつ持つ。`TimelineData.cameraHeightBases` には `TimelineManager` がマネージャのインスタンスを差し込んで共有させ、XML の入出力だけを担わせる。補正の計算（`CameraTimelineLayer` の `GetSlotScale` など）はマネージャへ移し、カメラレイヤーと `CameraSnapshot` が同じ API を使う。

**Tech Stack:** C# 7.3 / .NET Framework（COM3D2 は 3.5、COM3D25 は 4.x）、Unity、xUnit（`source/COM3D2.SceneEditor.Plugin.Tests`、プラグイン DLL を参照）

**Spec:** `docs/superpowers/specs/2026-10-14-camera-preset-height-correction-design.md`（改訂版。「`CameraHeightManager`」「タイムラインとの関係」節）

## Global Constraints

- 基準メイドのライブ値は、タイムラインの新規作成・読込・アンロードで「なし」に戻さない。戻すのは `appliedSlotNo` と補間（`slotA` / `slotB` / `weight`）だけ
- 基準身長の保存先は `CameraHeightManager.bases` の 1 か所。`TimelineData.cameraHeightBases` は新規作成・読込・Undo 再構築のたびにこのインスタンスへ差し替える
- ファイル読込では XML の基準身長でスロットごとに上書きし、XML に無いスロットは残す
- 基準身長の復元規則（`CameraSnapshot.RestoreHeightSetting`）。メイドのいないスロット・`heightBase <= 0` には入れない:

  | 経路 | タイムライン未読込 | タイムライン読込中 |
  |---|---|---|
  | カメラプリセット（`CameraWindow.LoadPreset`） | 上書き（`Set`） | 未設定のときだけ（`SetIfUnset`） |
  | シーンプリセット（`FinishApply` → `ApplyHeight`。`LoadPreset` が先にアンロードするので常に未読込） | 上書き | （通らない） |
  | 履歴の Undo/Redo（`CameraSnapshot.Apply`） | 未設定のときだけ | 未設定のときだけ |

  履歴の経路で上書きすると、基準身長を手入力した後に無関係なカメラ操作を Undo しただけで、記録時の値へ戻る（基準身長は Undo の対象外）
- 基準メイドはシーン移動・プラグイン無効化でもリセットしない（手ブレと同じ。シーンをまたいでプリセットを使うため）
- この変更で次の挙動になる（ユーザー合意の範囲。docs に書く）:
  - 基準メイドを選んだまま新規作成すると、カメラレイヤーのフレーム 0 の初期キーに身長補正のキーが入る（`TimelineLayerBase.Init` の `UpdateFrame`）。カメラレイヤーは手ブレのキーを常に書くので、MTE で開けないのは元から同じ
  - 身長補正のキーが無いタイムラインを読み込んでも、選んでいる基準メイドで再生時に補正される（基準身長が今の身長と同じなら倍率 1）
  - 再生で身長補正のキーが当たると基準メイドはキーの値になり、アンロード後もその値が残る（手ブレと同じ）
  - 身長補正を持たない旧プリセットを読むと、タイムライン未読込でも基準メイドが「なし」になる（断面の扱い）
- 補正に使う基準身長はプリセットの `heightBase`（マネージャの値ではない）
- 基準身長は履歴（Undo）の対象外のまま。`TimelineXmlDiff` の除外も残す
- コードのコメント・ログは日本語。コメントは「却下した案にすると起きる問題」だけ
- 必ず COM3D2 と COM3D25 の両構成でビルドする。`debug.bat` は使わない（実機へ DLL をコピーするため）

## Review Focus

- タイムラインをファイルから読み込んだ後、Undo/Redo（`UpdateTimeline` の全再構築）をしても基準身長がマネージャの値のまま残る（XML の古い値へ戻らない） → Task 3 の実装と Task 7 の実機確認
- 現在のタイムラインに対して `TimelineData.FromXml` が呼ばれると、共有中のマネージャの基準身長が `Clear` で消える → Task 3 で `FromXml` の呼び出し元が `new TimelineData()` の直後だけであることを確かめる
- シーンプリセットを `LoadPreset` 経由で読むと（タイムラインのアンロードを経て）基準メイドが戻る → Task 7 の実機確認で `LoadPreset` を通す
- タイムライン未読込でカメラウィンドウの身長補正の行が出て、基準メイドの選択・基準身長の入力・取得ができる → Task 4 と Task 7
- 基準メイドを選んだまま新規作成したタイムラインで、編集してキーを打つと身長補正のキーが記録される（合意済みの変化） → Task 7

---

## File Structure

| ファイル | 変更 |
|---|---|
| `source/COM3D2.SceneEditor.Plugin/Timeline/CameraHeightBases.cs` | 新規。基準身長の辞書（Unity 非依存） |
| `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineData.cs` | `cameraHeightBases` を `CameraHeightBases` 型へ |
| `source/COM3D2.SceneEditor.Plugin/Timeline/CameraHeightState.cs` | シングルトン廃止、`Reset` を `ResetApplied`（ライブ値を残す）へ |
| `source/COM3D2.SceneEditor.Plugin/Manager/CameraHeightManager.cs` | 新規。state・bases・補正 API |
| `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/CameraTimelineLayer.cs` | 補正 API をマネージャへ移す |
| `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineManager.cs` | 切替時のリセット、bases の共有 |
| `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.cs` | マネージャ登録 |
| `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj` | 新規 2 ファイルの `Compile` |
| `source/COM3D2.SceneEditor.Plugin/CameraHeightRowDrawer.cs` / `CameraWindow.cs` | タイムライン無しでも行を出す |
| `source/COM3D2.SceneEditor.Plugin/Manager/History/CameraSnapshot.cs` / `Manager/ScenePresetManager.cs` | マネージャから記録・復元 |
| `source/COM3D2.SceneEditor.Plugin.Tests/CameraHeightBasesTests.cs` | 新規 |
| `source/COM3D2.SceneEditor.Plugin.Tests/CameraHeightStateTests.cs` | リセットのテストを差し替え |
| `docs-site/timeline/layers-camera.md` / `docs-site/guide/staging.md` / `W:\COM3D2_5\work\CLAUDE.md` | 説明の更新 |

## ビルドとテストのコマンド

Git Bash から。COM3D2 → COM3D25 の順でビルドしてからテストする（COM3D2 のビルドが COM3D25 の DLL を消すため）。

```bash
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin
export MSYS2_ARG_CONV_EXCL="*"
MSB="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
"$MSB" COM3D2.SceneEditor.Plugin.csproj -v:m /p:Configuration=Debug /p:GameVersion=COM3D2 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" \
 && "$MSB" COM3D2.SceneEditor.Plugin.csproj -v:m /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" \
 && dotnet test ../COM3D2.SceneEditor.Plugin.Tests
```

以降「ビルドとテスト」はこのコマンドを指す。計画内の行番号は目安で、実コードとずれている箇所がある（例: `CameraWindow.cs` の身長補正の行は 540 付近、`TimelineManager.CreateNewTimeline` は 463 付近）。編集前に `rg` で場所を確かめる。特定のテストだけ回すときは末尾に `--filter "FullyQualifiedName~<クラス名>"` を付ける。

---

### Task 1: 基準身長の辞書を `CameraHeightBases` に切り出す

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/CameraHeightBases.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineData.cs:420-450,890-895,1074-1083`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`（`Timeline\CameraHeightState.cs` の行の隣）
- Create: `source/COM3D2.SceneEditor.Plugin.Tests/CameraHeightBasesTests.cs`

**Interfaces:**
- Produces:
  - `MTEP.CameraHeightBases`: `int Get(int slotNo)`（未設定は 0）、`void Set(int slotNo, int height)`（0 以下は無視）、`void SetIfUnset(int slotNo, int height)`、`void Clear()`、`void MergeFrom(CameraHeightBases other)`（other の設定済みスロットを上書き）、`IEnumerable<int> slotNos`
  - `TimelineData.cameraHeightBases` の型が `CameraHeightBases` になる。`TimelineData.GetCameraHeightBase` / `SetCameraHeightBase` / `SetCameraHeightBaseIfUnset` は残し、`cameraHeightBases` へ委譲する

- [ ] **Step 1: テストを書く**

`CameraHeightBasesTests.cs`:

```csharp
using System.Linq;
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class CameraHeightBasesTests
    {
        [Fact]
        public void 未設定のスロットは0()
        {
            Assert.Equal(0, new CameraHeightBases().Get(1));
        }

        [Fact]
        public void 負のスロットは0()
        {
            Assert.Equal(0, new CameraHeightBases().Get(-1));
        }

        [Fact]
        public void 身長が0以下なら設定しない()
        {
            var bases = new CameraHeightBases();
            bases.Set(0, 160);
            bases.Set(0, 0);
            Assert.Equal(160, bases.Get(0));
        }

        [Fact]
        public void 未設定のときだけ取り込む()
        {
            var bases = new CameraHeightBases();
            bases.SetIfUnset(0, 160);
            bases.SetIfUnset(0, 140);
            Assert.Equal(160, bases.Get(0));
        }

        [Fact]
        public void 取り込むと相手の設定済みスロットだけを上書きする()
        {
            var bases = new CameraHeightBases();
            bases.Set(0, 160);
            bases.Set(1, 150);
            var loaded = new CameraHeightBases();
            loaded.Set(1, 140);
            loaded.Set(2, 170);

            bases.MergeFrom(loaded);

            Assert.Equal(160, bases.Get(0));
            Assert.Equal(140, bases.Get(1));
            Assert.Equal(170, bases.Get(2));
            Assert.Equal(new[] { 0, 1, 2 }, bases.slotNos.OrderBy(x => x).ToArray());
        }

        [Fact]
        public void 消すと全スロットが未設定になる()
        {
            var bases = new CameraHeightBases();
            bases.Set(0, 160);
            bases.Clear();
            Assert.Equal(0, bases.Get(0));
            Assert.Empty(bases.slotNos);
        }

        [Fact]
        public void タイムラインは差し込まれたインスタンスへ書く()
        {
            var shared = new CameraHeightBases();
            var data = new TimelineData { cameraHeightBases = shared };
            data.SetCameraHeightBase(0, 160);
            Assert.Equal(160, shared.Get(0));
        }
    }
}
```

- [ ] **Step 2: テストが失敗することを確かめる**

Run: ビルドとテスト（`--filter "FullyQualifiedName~CameraHeightBasesTests"`）
Expected: `CameraHeightBases` が無いため、テストプロジェクトのコンパイルエラー

- [ ] **Step 3: `CameraHeightBases` を作る**

`Timeline/CameraHeightBases.cs`（`CameraHeightState.cs` と同じく BOM 付き UTF-8・CRLF にそろえる）:

```csharp
using System.Collections.Generic;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>身長補正の基準身長 (スロット番号 → ゲーム内身長 cm)。未設定のスロットは補正しない</summary>
    public class CameraHeightBases
    {
        private readonly Dictionary<int, int> _heights = new Dictionary<int, int>();

        public IEnumerable<int> slotNos => _heights.Keys;

        public int Get(int slotNo)
        {
            int height;
            return _heights.TryGetValue(slotNo, out height) ? height : 0;
        }

        /// <summary>0 以下は設定しない (未設定と同じ扱い)</summary>
        public void Set(int slotNo, int height)
        {
            if (height > 0)
            {
                _heights[slotNo] = height;
            }
        }

        /// <summary>
        /// 未設定のスロットに限り取り込む。設定済みを上書きすると、
        /// 別のメイドで開いて基準メイドを選び直したりキーを打ったりしただけで作者の基準が失われる
        /// </summary>
        public void SetIfUnset(int slotNo, int height)
        {
            if (Get(slotNo) <= 0)
            {
                Set(slotNo, height);
            }
        }

        public void Clear()
        {
            _heights.Clear();
        }

        /// <summary>other の設定済みスロットだけを上書きで取り込む。other に無いスロットは残す</summary>
        public void MergeFrom(CameraHeightBases other)
        {
            foreach (var pair in other._heights)
            {
                _heights[pair.Key] = pair.Value;
            }
        }
    }
}
```

csproj に `<Compile Include="Timeline\CameraHeightBases.cs" />` を `Timeline\CameraHeightState.cs` の行の直前に足す。

- [ ] **Step 4: `TimelineData` を委譲にする**

`TimelineData.cs` の基準身長の部分を置き換える（`SetCameraHeightBaseIfUnset` の summary は `CameraHeightBases.SetIfUnset` へ移したので消す）:

```csharp
        /// <summary>
        /// 身長補正の基準身長。実行中は TimelineManager が CameraHeightManager のインスタンスを差し込んで共有するため、
        /// 現在のタイムラインに対して FromXml を呼ぶとシーンの基準身長まで消える
        /// </summary>
        public CameraHeightBases cameraHeightBases = new CameraHeightBases();

        public int GetCameraHeightBase(int slotNo)
        {
            return cameraHeightBases.Get(slotNo);
        }

        public void SetCameraHeightBase(int slotNo, int height)
        {
            cameraHeightBases.Set(slotNo, height);
        }

        public void SetCameraHeightBaseIfUnset(int slotNo, int height)
        {
            cameraHeightBases.SetIfUnset(slotNo, height);
        }
```

`FromXml` の `cameraHeightBases.Clear();` はそのまま（型が変わっても同名のメソッド）。`ToXml` のループを置き換える:

```csharp
            xml.cameraHeightBases = new List<TimelineCameraHeightBaseXml>();
            foreach (var slotNo in cameraHeightBases.slotNos.OrderBy(x => x))
            {
                xml.cameraHeightBases.Add(new TimelineCameraHeightBaseXml
                {
                    maidSlotNo = slotNo,
                    height = cameraHeightBases.Get(slotNo),
                });
            }
```

この時点で `TimelineManager.UpdateTimeline` の `var cameraHeightBases = _timeline != null ? _timeline.cameraHeightBases : null;` / `_timeline.cameraHeightBases = cameraHeightBases;` は型が変わるだけでそのままコンパイルが通る（Task 3 で置き換える）。

- [ ] **Step 5: テストが通ることを確かめる**

Run: ビルドとテスト
Expected: 両構成のビルド成功、`CameraHeightBasesTests` と既存の `CameraHeightBaseTests` を含めすべて PASS

- [ ] **Step 6: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/CameraHeightBases.cs source/COM3D2.SceneEditor.Plugin/Timeline/TimelineData.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj source/COM3D2.SceneEditor.Plugin.Tests/CameraHeightBasesTests.cs
git commit -m "refactor(camera): 身長補正の基準身長を CameraHeightBases に切り出す"
```

本文: 「タイムラインから独立したマネージャと共有するため」

---

### Task 2: タイムライン切替で基準メイドのライブ値を残す

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/CameraHeightState.cs:44-52`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineManager.cs:434`
- Modify: `source/COM3D2.SceneEditor.Plugin.Tests/CameraHeightStateTests.cs:102-114`

**Interfaces:**
- Produces: `CameraHeightState.ResetApplied()`（`Reset()` を置き換える。ライブ値は残し、補間をライブ値へ戻し、`appliedSlotNo` を `NoSlot` にする）

- [ ] **Step 1: テストを差し替える**

`リセットするとキーで当たっていた値も消え変更なしになる` を次の 2 つに置き換える:

```csharp
        [Fact]
        public void タイムラインを切り替えてもライブ値は残りキーで当たっていた値と補間は消える()
        {
            var state = new CameraHeightState();
            state.ApplyKeys(0, 1, 0.5f);
            state.ResetApplied();
            Assert.Equal(0, state.liveSlotNo);
            Assert.Equal(0, state.slotA);
            Assert.Equal(0, state.slotB);
            Assert.Equal(0f, state.weight);
            Assert.Equal(CameraHeightState.NoSlot, state.appliedSlotNo);
            // 新しいタイムラインのキーには基準メイドがまだ無いので、新規作成時のフレーム 0 の初期キーや次に打つキーに書かれる
            Assert.True(state.isLiveChanged);
        }

        [Fact]
        public void 補正なしのままタイムラインを切り替えても変更なし()
        {
            var state = new CameraHeightState();
            state.ResetApplied();
            Assert.False(state.isLiveChanged);
        }
```

- [ ] **Step 2: テストが失敗することを確かめる**

Run: ビルドとテスト（`--filter "FullyQualifiedName~CameraHeightStateTests"`）
Expected: `ResetApplied` が無いためコンパイルエラー

- [ ] **Step 3: `ResetApplied` にする**

`CameraHeightState.cs` の `Reset` を置き換える:

```csharp
        /// <summary>
        /// タイムラインの切り替え時に呼ぶ。基準メイドはシーンの設定なので残し、
        /// 前のタイムラインのキーで当たっていた値と補間だけを消す。
        /// appliedSlotNo を残すと、新しいカメラレイヤーで基準メイドを変えていないのに変えていない扱いになり、キーが書かれない
        /// </summary>
        public void ResetApplied()
        {
            SetLive(liveSlotNo);
            appliedSlotNo = NoSlot;
        }
```

`TimelineManager.cs:434` の `CameraHeightState.instance.Reset();` を `CameraHeightState.instance.ResetApplied();` にする（Task 3 でマネージャ経由へ変える）。

- [ ] **Step 4: テストが通ることを確かめる**

Run: ビルドとテスト
Expected: すべて PASS

- [ ] **Step 5: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/CameraHeightState.cs source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineManager.cs source/COM3D2.SceneEditor.Plugin.Tests/CameraHeightStateTests.cs
git commit -m "feat(camera): タイムラインを切り替えても身長補正の基準メイドを残す"
```

本文: 「手ブレと同じくシーンのライブ値として扱い、シーンプリセットで戻した基準メイドがタイムラインのアンロードで消えないようにするため」

---

### Task 3: `CameraHeightManager` を作り、補正の計算と基準身長を集める

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/Manager/CameraHeightManager.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/CameraHeightState.cs:11-22`（シングルトン削除）
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/CameraTimelineLayer.cs:92,164,180,218,237-302,313,347`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineManager.cs:434,453-470,520-525,608-626`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.cs:471`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`（`Manager\CameraShakeManager.cs` の行の隣）
- Modify: `source/COM3D2.SceneEditor.Plugin/CameraHeightRowDrawer.cs` / `Manager/History/CameraSnapshot.cs`（コンパイルを通すための参照の付け替えだけ。動作の変更は Task 4・5）

**Interfaces:**
- Consumes: Task 1 の `CameraHeightBases`、Task 2 の `CameraHeightState.ResetApplied`
- Produces（`COM3D2.SceneEditor.Plugin.CameraHeightManager`）:
  - `static CameraHeightManager instance`
  - `readonly MTEP.CameraHeightState state` / `readonly MTEP.CameraHeightBases bases`
  - `static int GetSlotHeight(int slotNo)`（メイドがいなければ 0）
  - `static float GetSlotScale(int slotNo, int baseHeight, out float rootY)`（基準身長を渡す版）
  - `float GetSlotScale(int slotNo, out float rootY)`（`bases` の値を使う版）
  - `Vector3 CorrectTargetPos(Vector3 position)` / `Vector3 UncorrectTargetPos(Vector3 position)`（`state` の補間込み。再生とキー化用）
- `CameraTimelineLayer.GetSlotScale` / `GetSlotHeight` は削除する

単体テストは Task 1・2 で済ませた純粋ロジックが担う。ここは参照の付け替えなので、両構成のビルドと既存テストで確かめる。

- [ ] **Step 1: マネージャを作る**

`Manager/CameraHeightManager.cs`（`CameraShakeManager.cs` と同じ文字コード・改行にそろえる）:

```csharp
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// カメラの身長補正の設定 (基準メイド・基準身長) と補正の計算。
    /// CameraShakeManager と同じく保持している値が常にライブ値で、タイムラインを読み込んでいなくても存在する。
    /// 身長補正のキーは CameraTimelineLayer が補間結果を state へ書き込む形で上書きする。
    /// 基準身長の保存先はここだけで、TimelineData.cameraHeightBases には TimelineManager がこの bases を差し込む
    /// </summary>
    public class CameraHeightManager : ManagerBase
    {
        private static CameraHeightManager _instance;
        public static CameraHeightManager instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new CameraHeightManager();
                }
                return _instance;
            }
        }

        public readonly MTEP.CameraHeightState state = new MTEP.CameraHeightState();
        public readonly MTEP.CameraHeightBases bases = new MTEP.CameraHeightBases();

        private CameraHeightManager()
        {
        }

        /// <summary>スロットのメイドのゲーム内身長 (cm)。メイドがいなければ 0</summary>
        public static int GetSlotHeight(int slotNo)
        {
            var maidCache = MTEP.MaidManager.instance.GetMaidCache(slotNo);
            return maidCache != null ? MTEP.MaidHeightReader.GetHeight(maidCache.maid) : 0;
        }

        /// <summary>
        /// スロット 1 人ぶんの身長補正の倍率と、補正の中心になるルートの高さ。
        /// 補正しない条件では 1 を返し、rootY は 0。プリセットは保存した基準身長を渡す
        /// </summary>
        public static float GetSlotScale(int slotNo, int baseHeight, out float rootY)
        {
            rootY = 0f;
            var maidCache = MTEP.MaidManager.instance.GetMaidCache(slotNo);
            var maid = maidCache != null ? maidCache.maid : null;
            // isLoadedBody を条件にすると、着替えでボディを再ロードする間だけ s = 1 に落ちてカメラが跳ねる
            if (maid == null)
            {
                return 1f;
            }

            rootY = maid.transform.position.y;
            return MTEP.CameraHeightCorrection.CalcScale(baseHeight, MTEP.MaidHeightReader.GetHeight(maid));
        }

        public float GetSlotScale(int slotNo, out float rootY)
        {
            return GetSlotScale(slotNo, bases.Get(slotNo), out rootY);
        }

        /// <summary>再生で決まった補間 (state の slotA / slotB / weight) で注視点の Y を補正する</summary>
        public Vector3 CorrectTargetPos(Vector3 position)
        {
            float rootA, scaleA, rootB, scaleB;
            var w = ResolveBlend(out rootA, out scaleA, out rootB, out scaleB);
            position.y = MTEP.CameraHeightCorrection.Blend(position.y, rootA, scaleA, rootB, scaleB, w);
            return position;
        }

        /// <summary>CorrectTargetPos の逆。キーには基準身長の空間の値を記録する</summary>
        public Vector3 UncorrectTargetPos(Vector3 position)
        {
            float rootA, scaleA, rootB, scaleB;
            var w = ResolveBlend(out rootA, out scaleA, out rootB, out scaleB);
            position.y = MTEP.CameraHeightCorrection.Unblend(position.y, rootA, scaleA, rootB, scaleB, w);
            return position;
        }

        private float ResolveBlend(out float rootA, out float scaleA, out float rootB, out float scaleB)
        {
            scaleA = GetSlotScale(state.slotA, out rootA);
            scaleB = GetSlotScale(state.slotB, out rootB);
            return state.weight;
        }
    }
}
```

旧 `GetSlotScale(int, int, out float)` にあった `if (slotNo < 0) return 1f;` は、`GetMaidCache` が負のスロットで null を返す（`MaidManager.cs:265`）ため不要。

csproj に `<Compile Include="Manager\CameraHeightManager.cs" />` を `Manager\CameraShakeManager.cs` の行の直前に足す。

`COM3D2.SceneEditor.Plugin.cs:471` の `managerRegistry.RegisterManager(CameraShakeManager.instance);` の直後に足す:

```csharp
                managerRegistry.RegisterManager(CameraHeightManager.instance);
```

- [ ] **Step 2: `CameraHeightState` のシングルトンを消す**

`CameraHeightState.cs` から `_instance` と `instance` プロパティを削除する（`NoSlot` 定数は残す）。

- [ ] **Step 3: カメラレイヤーをマネージャへ付け替える**

`CameraTimelineLayer.cs`:
- `GetSlotScale` の 2 つの overload、`GetSlotHeight`、`ResolveHeightBlend`、`CorrectTargetPos`、`UncorrectTargetPos` を削除する（`GetUOCamera` の後ろから `UpdateFrame` の手前まで）
- クラスに参照を足す: `private static SE.CameraHeightManager heightManager => SE.CameraHeightManager.instance;`
- `CameraHeightState.instance.BeginApply();` → `heightManager.state.BeginApply();`
- `uoCamera.SetTargetPos(CorrectTargetPos(position));`（2 か所）→ `uoCamera.SetTargetPos(heightManager.CorrectTargetPos(position));`
- `ApplyHeightMotion` の `CameraHeightState.instance.ApplyKeys(` → `heightManager.state.ApplyKeys(`
- `trans.position = UncorrectTargetPos(target.position);` → `trans.position = heightManager.UncorrectTargetPos(target.position);`
- `var heightState = CameraHeightState.instance;` → `var heightState = heightManager.state;`

- [ ] **Step 4: `TimelineManager` で基準身長を共有させる**

`ResetTimelineState` の `CameraHeightState.instance.ResetApplied();` → `SE.CameraHeightManager.instance.state.ResetApplied();`

`CreateNewTimeline` の `_timeline = new TimelineData { ... };` の直後:

```csharp
            _timeline.cameraHeightBases = SE.CameraHeightManager.instance.bases;
```

`LoadTimeline` の `_timeline.FromXml(xml);` の直後:

```csharp
                // 作者の基準身長を優先して取り込み、以後はシーンの基準身長を共有する。
                // 差し替えずに XML の値だけを持たせると、UI やプリセットで変えた基準身長が保存に載らない
                var heightBases = SE.CameraHeightManager.instance.bases;
                heightBases.MergeFrom(_timeline.cameraHeightBases);
                _timeline.cameraHeightBases = heightBases;
```

`UpdateTimeline` の基準身長の引き継ぎ（`var cameraHeightBases = ...` の 2 行のコメントと宣言、`if (cameraHeightBases != null) { ... }`）を消し、`_timeline.FromXml(xml);` の後（`materialShaders` の差し戻しの後）に置く:

```csharp
            // 身長補正の基準身長は履歴の管理外。履歴の XML の値で上書きすると、Undo で作者の基準へ戻せなくなる
            _timeline.cameraHeightBases = SE.CameraHeightManager.instance.bases;
```

`rg -n "FromXml\(" source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineManager.cs` で、`TimelineData.FromXml` の呼び出しが `new TimelineData()` の直後の 2 か所（`LoadTimeline` / `UpdateTimeline`）だけであることを確かめる（`layer.FromXml` は別物）。他にあれば、共有中の bases を `Clear` しないよう、その箇所でも差し込み直しが要る。

- [ ] **Step 5: 残りの参照を付け替えてコンパイルを通す**

`CameraHeightRowDrawer.cs`:
- `MTEP.CameraHeightState.instance` → `CameraHeightManager.instance.state`
- `MTEP.CameraTimelineLayer.GetSlotHeight(` → `CameraHeightManager.GetSlotHeight(`
- `MTEP.CameraTimelineLayer.GetSlotScale(timeline, slotNo, out rootY)` → `CameraHeightManager.instance.GetSlotScale(slotNo, out rootY)`

`CameraSnapshot.cs`:
- `MTEP.CameraHeightState.instance` → `CameraHeightManager.instance.state`（2 か所）
- `MTEP.CameraTimelineLayer.GetSlotScale(state.heightSlotNo, state.heightBase, out rootY)` → `CameraHeightManager.GetSlotScale(state.heightSlotNo, state.heightBase, out rootY)`

`rg -n "CameraHeightState.instance|CameraTimelineLayer.GetSlot" source` が 0 件になることを確かめる。

- [ ] **Step 6: ビルドとテスト**

Run: ビルドとテスト
Expected: 両構成のビルド成功、すべて PASS

- [ ] **Step 7: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin/Manager/CameraHeightManager.cs source/COM3D2.SceneEditor.Plugin/Timeline/CameraHeightState.cs source/COM3D2.SceneEditor.Plugin/Timeline/TimelineLayer/CameraTimelineLayer.cs source/COM3D2.SceneEditor.Plugin/Timeline/Manager/TimelineManager.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj source/COM3D2.SceneEditor.Plugin/CameraHeightRowDrawer.cs source/COM3D2.SceneEditor.Plugin/Manager/History/CameraSnapshot.cs
git commit -m "refactor(camera): 身長補正の設定と補正の計算を CameraHeightManager に集める"
```

本文: 「手ブレと同じくタイムラインから独立したライブ値にし、カメラレイヤーとプリセットが同じ計算を使うようにするため」

---

### Task 4: タイムライン未読込でも身長補正の行を出す

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/CameraHeightRowDrawer.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/CameraWindow.cs:527-532`

**Interfaces:**
- Consumes: Task 3 の `CameraHeightManager`

- [ ] **Step 1: `CameraHeightRowDrawer` をマネージャだけで動かす**

summary を次にし、`timeline` の取得と `if (timeline == null) return;` を消す:

```csharp
    /// <summary>
    /// カメラの身長補正の設定行 (基準メイド・そのスロットの基準身長)。編集先は CameraHeightManager のライブ値で、
    /// タイムラインを読み込んでいなくても編集できる。カメラキーを打つと身長補正のキーにも書かれる。
    /// コンボボックスの開閉状態を持つため、描画するビューごとにインスタンスを分ける
    /// </summary>
```

static プロパティを置き換える（`timelineManager` は使わなくなるので消す）:

```csharp
        private static MTEP.MaidManager maidManager => MTEP.MaidManager.instance;
        private static CameraHeightManager heightManager => CameraHeightManager.instance;
        private static MTEP.CameraHeightState heightState => heightManager.state;
        private static MTEP.CameraHeightBases bases => heightManager.bases;
```

`SetBaseHeight` を次にする:

```csharp
        private static void SetBaseHeight(int slotNo, int height)
        {
            // RecordCameraEdit にすると、基準身長を Undo の対象外にした方針 (全再構築で古い値へ戻る) に反する
            AutoEditMode.Enter();
            bases.Set(slotNo, height);
        }
```

`Draw` 内の置き換え:
- `timeline.SetCameraHeightBaseIfUnset(selectedSlotNo, CameraHeightManager.GetSlotHeight(selectedSlotNo));` → `bases.SetIfUnset(selectedSlotNo, CameraHeightManager.GetSlotHeight(selectedSlotNo));`
- `value = timeline.GetCameraHeightBase(slotNo),` → `value = bases.Get(slotNo),`
- `onChanged = x => SetBaseHeight(timeline, slotNo, x),` → `onChanged = x => SetBaseHeight(slotNo, x),`
- `SetBaseHeight(timeline, slotNo, CameraHeightManager.GetSlotHeight(slotNo));` → `SetBaseHeight(slotNo, CameraHeightManager.GetSlotHeight(slotNo));`
- `if (timeline.GetCameraHeightBase(slotNo) <= 0)` → `if (bases.Get(slotNo) <= 0)`

- [ ] **Step 2: `CameraWindow` のタイムライン判定を外す**

`DrawMainCameraContent` の次の部分:

```csharp
            // 判定を CameraHeightRowDrawer 任せにすると、タイムライン未読込で区切り線だけが残る
            if (MTEP.TimelineManager.instance.timeline != null)
            {
                _heightRowDrawer.Draw(_view, LABEL_WIDTH, ROW_HEIGHT);
                _view.DrawHorizontalLine();
            }
```

を次にする:

```csharp
            _heightRowDrawer.Draw(_view, LABEL_WIDTH, ROW_HEIGHT);
            _view.DrawHorizontalLine();
```

- [ ] **Step 3: ビルドとテスト**

Run: ビルドとテスト
Expected: 両構成のビルド成功、すべて PASS

- [ ] **Step 4: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin/CameraHeightRowDrawer.cs source/COM3D2.SceneEditor.Plugin/CameraWindow.cs
git commit -m "feat(camera): タイムラインを読み込んでいなくても身長補正を設定できるようにする"
```

---

### Task 5: プリセットの記録・復元をマネージャへ向ける

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/History/CameraSnapshot.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetManager.cs`（`ApplyScenery` のコメント、`FinishApply` の呼び出し）

**Interfaces:**
- Consumes: Task 3 の `CameraHeightManager`
- Produces:
  - `CameraSnapshot.ApplyState(ScenePresetCamera state, bool applyHeight = true, bool isPreset = false)`（`isPreset` はプリセット適用。履歴の Undo/Redo は既定の false）
  - `CameraSnapshot.ApplyHeight(ScenePresetCamera state)`（`ApplyHeightCorrection` を置き換え。シーンプリセット専用で、設定の復元（プリセット扱い）と注視点の補正）

- [ ] **Step 1: 記録をタイムライン非依存にする**

`CaptureState` の身長補正の部分を置き換える:

```csharp
            var heightManager = CameraHeightManager.instance;
            var slotNo = heightManager.state.liveSlotNo;
            state.heightSlotNo = slotNo;
            state.heightBase = heightManager.bases.Get(slotNo);

            // 再生中の補間 (CameraHeightState の slotA/B/weight) で逆補正すると、
            // 補間を持たないプリセットを同じメイドで読んだときに画面の位置へ戻らない
            if (CanCorrectHeight(state))
            {
                float rootY;
                var scale = GetPresetScale(state, out rootY);
                var pos = state.targetPos;
                pos.y = MTEP.CameraHeightCorrection.Uncorrect(pos.y, rootY, scale);
                state.targetPos = pos;
            }
            return state;
```

（`var timeline = ...; if (timeline != null) { ... }` の囲みを外す）

- [ ] **Step 2: 復元をタイムライン非依存にする**

`RestoreHeightSetting` を置き換える:

```csharp
        /// <summary>
        /// 身長補正の設定を戻す。プリセットは断面として扱うため、補正を持たない旧形式は基準メイドなしになる
        /// </summary>
        /// <param name="isPreset">プリセットの適用なら true。タイムライン未読込のときだけ基準身長を上書きする</param>
        private static void RestoreHeightSetting(ScenePresetCamera state, bool isPreset)
        {
            var heightManager = CameraHeightManager.instance;
            heightManager.state.SetLive(state.heightSlotNo);

            // いないスロットへ入れると、後でそのスロットにメイドを呼んでも「設定済み」で取り込まれない
            if (state.heightBase <= 0
                || MTEP.MaidManager.instance.GetMaidCache(state.heightSlotNo) == null)
            {
                return;
            }

            // 履歴の Undo/Redo で上書きすると、基準身長を手入力した後に無関係なカメラ操作を戻しただけで記録時の値へ戻る。
            // タイムライン読込中に上書きすると、設定済みの基準身長を変えるのは「取得」と手入力だけという作者の基準を守る規則が崩れる
            if (isPreset && MTEP.TimelineManager.instance.timeline == null)
            {
                heightManager.bases.Set(state.heightSlotNo, state.heightBase);
            }
            else
            {
                heightManager.bases.SetIfUnset(state.heightSlotNo, state.heightBase);
            }
        }
```

`ApplyHeightCorrection` を `ApplyHeight` に戻し、設定の復元も行う:

```csharp
        /// <summary>
        /// ApplyState(state, false) で飛ばした身長補正 (設定の復元と注視点の補正) をかける。
        /// 補正前の値 (state.targetPos) から計算するため、何度呼んでも二重にはかからない
        /// </summary>
        public static void ApplyHeight(ScenePresetCamera state)
        {
            if (state == null)
            {
                return;
            }

            RestoreHeightSetting(state, isPreset: true);
            if (!CanCorrectHeight(state))
            {
                return;
            }

            var mainCamera = GameMain.Instance != null ? GameMain.Instance.MainCamera : null;
            if (mainCamera == null)
            {
                return;
            }
            mainCamera.SetTargetPos(CorrectTargetPos(state));
        }
```

`ApplyState` のシグネチャを `ApplyState(ScenePresetCamera state, bool applyHeight = true, bool isPreset = false)` にし、`applyHeight` の param 説明の「`ApplyHeightCorrection`」を「`ApplyHeight`」に戻し、`isPreset` の param を足す（`/// <param name="isPreset">プリセットの適用なら true。履歴の Undo/Redo は false</param>`）。本体の `RestoreHeightSetting(state);` を `RestoreHeightSetting(state, isPreset);` にする。

`CameraWindow.LoadPreset` の `CameraSnapshot.ApplyState(state);` を `CameraSnapshot.ApplyState(state, isPreset: true);` にする。`ScenePresetManager.ApplySceneCapturePreset` の `CameraSnapshot.ApplyState(converted.camera);` も `isPreset: true` を渡す（SceneCapture は `heightBase = 0` なので基準身長は変わらず、基準メイドだけ「なし」になる）。履歴の `Apply(Maid maid) => ApplyState(_state);` は変えない。

- [ ] **Step 3: シーンプリセットの呼び出しを直す**

`ScenePresetManager.ApplyScenery` のコメントを置き換える:

```csharp
                // ここで補正すると、メイドの呼出・配置前で基準メイドの身長とルートの高さが定まっておらず誤った値で補正される。
                // 基準身長の取り込みもメイドがいるスロットに限るため、設定の復元ごと FinishApply へ回す
                CameraSnapshot.ApplyState(data.camera, applyHeight: false);
```

`FinishApply` の `CameraSnapshot.ApplyHeightCorrection(data.camera);` → `CameraSnapshot.ApplyHeight(data.camera);`

`rg -n "ApplyHeightCorrection" source` が 0 件になることを確かめる。

- [ ] **Step 4: ビルドとテスト**

Run: ビルドとテスト
Expected: 両構成のビルド成功、すべて PASS

- [ ] **Step 5: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin/Manager/History/CameraSnapshot.cs source/COM3D2.SceneEditor.Plugin/Manager/ScenePresetManager.cs source/COM3D2.SceneEditor.Plugin/CameraWindow.cs
git commit -m "feat(scene-preset): シーンプリセットで身長補正の基準メイドと基準身長を戻す"
```

本文: 「基準メイドがタイムラインから独立したため、タイムラインをアンロードしたあとでも戻せるようになった」

---

### Task 6: ドキュメントを更新する

**Files:**
- Modify: `docs-site/timeline/layers-camera.md`（「身長補正」の節）
- Modify: `docs-site/guide/staging.md:17-21`
- Modify: `W:\COM3D2_5\work\CLAUDE.md`（「カメラの身長補正は SE 独自。…」の項目。git 管理外）

- [ ] **Step 1: `layers-camera.md`**

「身長補正」の節の最初の段落の「（タイムラインを読み込んでいるときだけ表示されます）」を消し、次の 1 文を段落の末尾に足す:

```markdown
設定は手ブレと同じくタイムラインとは別に保たれ、タイムラインを読み込んでいなくても選べます。
```

節の末尾の 2 項目（「カメラプリセットとシーンプリセットにも…」「カメラプリセットをタイムラインの読み込み中に…」）を次の 3 項目に置き換える:

```markdown
- タイムラインを新規作成・読み込み・アンロードしても、そのとき選ばれている基準メイドはそのまま残ります（再生で `身長補正` のキーが当たった後は、そのキーのメイドになります）
  - 基準メイドを選んだまま新規作成すると、最初のカメラのキーに `身長補正` のキーも入ります。読み込んだタイムラインでも、キーを打つと記録されます
  - `身長補正` のキーが無いタイムラインでも、選んでいる基準メイドの身長に合わせて再生時に補正されます。補正したくないときは基準メイドを「なし」にします
- タイムラインを読み込むと、タイムラインに保存された基準身長が優先されます。保存すると、それまでに設定した全メイドの基準身長がタイムラインに書き込まれます
- カメラプリセットとシーンプリセットにも基準メイドと基準身長が保存されます。読み込むと、そのときのメイドの身長に合わせて注視点の高さを補正し、基準メイドの選択も戻します。タイムラインの読み込み中は、設定済みの基準身長をプリセットの値で上書きしません。身長補正に対応する前に保存したプリセットを読むと、基準メイドは「なし」になります
```

- [ ] **Step 2: `staging.md`**

「`Main` タブには、タイムラインを読み込んでいるときだけ `身長補正` の設定も出ます（…）。」を次にする:

```markdown
`Main` タブには `身長補正` の設定もあります（[カメラ系レイヤー](/timeline/layers-camera)の「身長補正」）。
```

- [ ] **Step 3: `W:\COM3D2_5\work\CLAUDE.md`**

「カメラの身長補正は SE 独自。…」の項目の末尾に足す:

```markdown
基準メイド・基準身長は SE の `CameraHeightManager` がシーンのライブ値として持つ（手ブレと同じ）。タイムラインの切り替えで基準メイドを「なし」に戻さず、読込時は XML の基準身長でスロットごとに上書きし、保存時はマネージャの全スロットを書き出す（使っていないスロットが入りうる）
```

- [ ] **Step 4: Commit（docs-site のみ）**

```bash
git add docs-site/timeline/layers-camera.md docs-site/guide/staging.md
git commit -m "docs(camera): 身長補正の設定がタイムラインから独立したことを説明する"
```

---

### Task 7: 実機確認

**Files:** なし（不具合があれば直してコミット）

`restart-verify` スキルで DLL を反映し、通常シーン（`SceneDaily`）でエディタを有効にして確かめる。撮影モードでは確かめない。`eval_csharp` はリフレクションで `COM3D2.SceneEditor.Plugin.CameraHeightManager.instance` の `state.liveSlotNo` / `bases.Get(n)` と `GameMain.Instance.MainCamera.GetTargetPos()` を読む。テストのためにメイドの `MPN.sintyou` を変えたら最後に戻す。作ったタイムライン・プリセットは消すか元へ戻す。

- [ ] **Step 1: タイムライン未読込**
  - カメラウィンドウの `Main` タブに身長補正の行が出る（`screenshot` で確認）
  - 基準メイドを選ぶ → `liveSlotNo` がそのスロットになり、基準身長が今の身長で入る
- [ ] **Step 2: タイムラインとの受け渡し**
  - 基準メイドを選んだまま新規作成 → `liveSlotNo` が残り、`timeline.GetCameraHeightBase(n)` がマネージャの値と同じ
  - XML に基準身長のあるタイムラインを読み込む → そのスロットの値が XML の値になり、XML に無いスロットの値は残る
  - キーを 1 つ打って Undo / Redo → 基準身長がマネージャの値のまま
  - アンロード → `liveSlotNo` と基準身長が残る
- [ ] **Step 3: シーンプリセット（`LoadPreset` 経由）**
  - タイムライン未読込で基準メイド・基準身長を設定し、カメラを含むシーンプリセットを保存する（`ScenePresetManager.SavePreset`）
  - 基準メイドを「なし」にし、メイドの身長を変えてから、そのプリセットを `LoadPreset(item)` で読む → `FinishApply` 後に `liveSlotNo` が戻り、注視点の Y が `rootY + (y - rootY) * 今の身長 / 基準身長` になる
  - タイムラインを読み込んだ状態で同じプリセットを読む（`LoadPreset` がタイムラインをアンロードする）→ 同じ結果
  - テスト用に保存したプリセットファイルは消す
- [ ] **Step 4: カメラプリセット**
  - タイムライン未読込で保存・読込 → 基準メイドと基準身長が戻る
  - タイムライン読込中に、基準身長の違うプリセットを読む → 設定済みの基準身長は上書きされない
- [ ] **Step 5: 履歴で基準身長が戻らない**
  - タイムライン未読込で基準メイドを選び、基準身長を手入力で変える → カメラの距離を変える → Undo → 基準身長は手入力の値のまま（距離だけ戻る）
- [ ] **Step 6: 新規作成のフレーム 0**
  - 基準メイドを選んだまま新規作成 → カメラレイヤーのフレーム 0 に身長補正のキーがあり、値が選んだスロット
- [ ] **Step 7: ログ**
  - `LogOutput.log` に SceneEditor 由来の例外が無い
- [ ] **Step 8: 修正があればコミットする**

## レビュー却下メモ

- 基準メイド・基準身長をシーン移動やプラグイン無効化でリセットすべき — 手ブレ（`CameraShakeManager`）も同じくリセットせず、シーンをまたいでプリセットを使う前提に合う。メイドのいないスロットは補正が無効になり、UI でも「基準メイドが見つかりません」と出る
- 新規作成時のフレーム 0 の身長補正キーを避けるべき — カメラレイヤーは手ブレのキーを常に書くため MTE 互換は元から無く、選んだ基準メイドを最初のキーに記録するのはシーンの設定として扱う合意に沿う。docs に明記する
