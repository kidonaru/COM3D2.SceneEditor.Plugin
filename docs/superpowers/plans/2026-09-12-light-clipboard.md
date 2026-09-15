# ライトパラメータのコピー / ペースト Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** ライト行に「コピー」「ペースト」ボタンを付け、あるライトのパラメータを別のライト（追加ライト同士、メインライト⇔追加ライト）へ写せるようにする。

**Architecture:** `MaterialClipboard` と同じ形のプロセス内静的クリップボード `LightClipboard` を追加する。値は `Light` から直接読み書きし、メイド追従は `MTEP.MaidFollowLight` を通す。UI は `LightRowDrawer` の先頭行に 2 ボタンを置き、LightWindow と Inspector（LightItemInspector）の両方から同じ行が出る。

**Tech Stack:** C# / Unity (COM3D2.5)

**Spec:** 本計画の「仕様」節

## 仕様

- コピー対象（追加ライト）: 種別・有効・位置・回転・色・強度・範囲・スポット角度・影の濃さ・影の距離・照射対象・追従メイド（slotNo）・追従オフセット
- 位置もコピーする（当初は除外していたがユーザー要望で含めた）。追従中のライトは追従側が毎フレーム位置を上書きするため、オフセットの方が効く
- メインライトからコピーした場合: 種別は平行、回転・色・強度・影の濃さ・影の距離を記録し、他は既定値
- メインライトへペースト: `LightMain` の setter で回転・色・強度・影の濃さを反映し、影の距離は `Light.shadowBias` へ直接書く（既存行と同じ扱い）。種別・範囲・照射対象・追従は反映しない
- ペーストは履歴（`RecordLightEdit("ペースト")`）に 1 件記録する
- ペーストボタンはクリップボードが空なら無効表示
- クリップボードはプロセス内のみ。XML 等への永続化はしない
- `Light` は Unity 外で生成できないため単体テストは書かない。ビルド + 実機確認で担保する
- ペーストで書き換える項目は全て Undo で戻せること。現状の `LightSnapshot` は追加ライトの影の濃さ/距離・追従、メインライトの影の距離を記録しないため、先に拡張する（Task 0）。既定値は生成時の値に合わせ、旧プリセットの見た目を変えない

## Global Constraints

- コメント・ログ文言は日本語
- 2 構成（COM3D2 / COM3D25）とも MSBuild でビルドを通す（順序: COM3D2 → COM3D25）
- 新規 .cs は csproj に `Compile Include` を追加する

## File Structure

| ファイル | 責務 |
|---|---|
| Modify: `source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs` | 追加ライトに影の濃さ/距離・追従、メインに影の距離を追加 |
| Modify: `source/COM3D2.SceneEditor.Plugin/Manager/History/LightSnapshot.cs` | 上記の記録・復元・比較 |
| Create: `source/COM3D2.SceneEditor.Plugin/LightClipboard.cs` | 値の保持と Light への読み書き |
| Modify: `source/COM3D2.SceneEditor.Plugin/LightRowDrawer.cs` | コピー/ペースト行の描画 |
| Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj` | 新規ファイル登録 |
| Modify: `CHANGELOG.md` | 未リリース節へ追記 |

---

### Task 0: 履歴スナップショットに影・追従を含める

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/ScenePresetData.cs`（`ScenePresetAdditionalLight` / `ScenePresetLight`）
- Modify: `source/COM3D2.SceneEditor.Plugin/Manager/History/LightSnapshot.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/LightRowDrawer.cs`（`FindFollowLight` を `public static` にする）

**Interfaces:**
- Produces: `ScenePresetAdditionalLight.shadowStrength / shadowBias / maidSlotNo / followOffset`、`ScenePresetLight.mainShadowBias`、`LightRowDrawer.FindFollowLight(Light)`（public）

- [ ] **Step 1: フィールドを追加する**

`ScenePresetAdditionalLight` の `target` の直後:

```csharp
        // 影と追従。旧プリセットには無いので、生成時の既定値で初期化して見た目を変えない
        public float shadowStrength = LightRowDrawer.DefaultAdditionalShadowStrength;
        public float shadowBias = LightRowDrawer.DefaultAdditionalShadowBias;
        /// <summary>追従先メイドのスロット番号。-1 で追従なし</summary>
        public int maidSlotNo = -1;
        public Vector3 followOffset = Vector3.zero;
```

`ScenePresetLight` の `mainShadowStrength` の直後:

```csharp
        public float mainShadowBias = LightRowDrawer.DefaultMainShadowBias;
```

- [ ] **Step 2: `LightSnapshot` の記録・復元・比較に加える**

`CaptureState`: メイン側に `state.mainShadowBias = mainLight.shadowBias;`。追加ライト側は `var followLight = LightRowDrawer.FindFollowLight(light);` を取り、`shadowStrength = light.shadowStrength, shadowBias = light.shadowBias, maidSlotNo = followLight != null ? followLight.maidSlotNo : -1, followOffset = followLight != null ? followLight.offset : Vector3.zero`

`ApplyState`: メイン側に `mainLight.shadowBias = state.mainShadowBias;`。`ApplyLightState` に `light.shadowStrength / shadowBias` と、`FindFollowLight(light)` が null でなければ `maidSlotNo / offset` を書く

`Approximately`: `mainShadowBias`、`shadowStrength`、`shadowBias`（`Mathf.Approximately`）、`maidSlotNo`、`followOffset` を比較に加える

- [ ] **Step 3: 両構成でビルドする**（Task 1 Step 2 のコマンド）

### Task 1: `LightClipboard`

**Files:**
- Create: `source/COM3D2.SceneEditor.Plugin/LightClipboard.cs`
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj`（`LightTarget.cs` の行の直後に `<Compile Include="LightClipboard.cs" />`）

**Interfaces:**
- Produces: `static class LightClipboard` に `bool hasData`, `void Copy(Light light, MTEP.MaidFollowLight followLight)`, `void CopyMain(Light light)`, `void Paste(Light light, MTEP.MaidFollowLight followLight)`, `void PasteMain(LightMain lightMain, Light light)`

- [ ] **Step 1: 実装する**

```csharp
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// ライトのパラメータのコピー & ペースト用クリップボード。
    /// 別のライトへ設定を写すためのもので、プロセス内でのみ保持する。
    /// 位置は写さない（貼り付け先の配置を保つ）
    /// </summary>
    public static class LightClipboard
    {
        private class Data
        {
            public LightType type;
            public bool enabled;
            public Vector3 rotation;
            public Color color;
            public float intensity;
            public float range;
            public float spotAngle;
            public float shadowStrength;
            public float shadowBias;
            public LightTargetMode target;
            public int maidSlotNo;
            public Vector3 followOffset;
        }

        private static Data _data = null;

        public static bool hasData => _data != null;

        /// <summary>追加ライトの値を記録する。followLight が null なら追従は「なし」として記録する</summary>
        public static void Copy(Light light, MTEP.MaidFollowLight followLight)
        {
            _data = new Data
            {
                type = light.type,
                enabled = light.enabled,
                rotation = light.transform.eulerAngles,
                color = light.color,
                intensity = light.intensity,
                range = light.range,
                spotAngle = light.spotAngle,
                shadowStrength = light.shadowStrength,
                shadowBias = light.shadowBias,
                target = LightTarget.FromCullingMask(light.cullingMask),
                maidSlotNo = followLight != null ? followLight.maidSlotNo : -1,
                followOffset = followLight != null ? followLight.offset : Vector3.zero,
            };
        }

        /// <summary>メインライトの値を記録する。追加ライトに無い項目は生成時の既定値にする</summary>
        public static void CopyMain(Light light)
        {
            _data = new Data
            {
                type = LightType.Directional,
                enabled = true,
                rotation = light.transform.eulerAngles,
                color = light.color,
                intensity = light.intensity,
                range = StudioLightManager.DefaultRange,
                spotAngle = StudioLightManager.DefaultSpotAngle,
                shadowStrength = light.shadowStrength,
                shadowBias = light.shadowBias,
                target = LightTargetMode.All,
                maidSlotNo = -1,
                followOffset = Vector3.zero,
            };
        }

        /// <summary>追加ライトへ反映する。followLight が null なら追従は触らない</summary>
        public static void Paste(Light light, MTEP.MaidFollowLight followLight)
        {
            if (_data == null || light == null)
            {
                return;
            }

            StudioLightManager.instance.SetLightType(light, _data.type);
            light.enabled = _data.enabled;
            light.transform.eulerAngles = _data.rotation;
            light.color = _data.color;
            light.intensity = _data.intensity;
            light.range = _data.range;
            light.spotAngle = _data.spotAngle;
            light.shadowStrength = _data.shadowStrength;
            light.shadowBias = _data.shadowBias;
            light.cullingMask = LightTarget.ToCullingMask(_data.target);

            if (followLight != null)
            {
                followLight.maidSlotNo = _data.maidSlotNo;
                followLight.offset = _data.followOffset;
            }
        }

        /// <summary>
        /// メインライトへ反映する。LightMain の setter が無い shadowBias だけ Light へ直接書く
        /// （LightRowDrawer のメインライト行と同じ扱い）。種別・範囲・照射対象・追従は反映しない
        /// </summary>
        public static void PasteMain(LightMain lightMain, Light light)
        {
            if (_data == null || lightMain == null || light == null)
            {
                return;
            }

            lightMain.SetRotation(_data.rotation);
            lightMain.SetColor(_data.color);
            lightMain.SetIntensity(_data.intensity);
            lightMain.SetShadowStrength(_data.shadowStrength);
            light.shadowBias = _data.shadowBias;
        }
    }
}
```

- [ ] **Step 2: 両構成でビルドする**

```
cd W:/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source
export MSYS2_ARG_CONV_EXCL="*"
MSB="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
for v in COM3D2 COM3D25; do "$MSB" COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=$v "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" /nologo /v:m; done
```

Expected: エラー 0

---

### Task 2: ライト行にコピー / ペーストボタンを出す

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/LightRowDrawer.cs`（`DrawMainLightParams` / `DrawAdditionalLightParams` の先頭）

**Interfaces:**
- Consumes: `LightClipboard.*`, `RecordLightEdit(string)`

- [ ] **Step 1: 共通の行描画を追加する**

`DrawFollowMaidRow` の直前に追加:

```csharp
        /// <summary>コピー / ペーストの 2 ボタン。ペーストはクリップボードが空なら押せない</summary>
        private static void DrawClipboardRow(
            GUIView view, float rowHeight, Action onCopy, Action onPaste)
        {
            view.BeginHorizontal();
            {
                if (view.DrawButton("コピー", ClipboardButtonWidth, rowHeight))
                {
                    onCopy();
                }

                if (view.DrawButton("ペースト", ClipboardButtonWidth, rowHeight,
                        enabled: LightClipboard.hasData))
                {
                    RecordLightEdit("ペースト");
                    onPaste();
                }
            }
            view.EndLayout();
        }
```

定数を `TypeButtonWidth` の近くに追加:

```csharp
        /// <summary>コピー / ペーストボタンの幅（MaterialPropertyRowsDrawer と同じ）</summary>
        private const float ClipboardButtonWidth = 60f;
```

- [ ] **Step 2: メインライト行の先頭で呼ぶ**

`DrawMainLightParams` の `var lightMain = lightManager.mainLight;` の直後:

```csharp
            DrawClipboardRow(view, rowHeight,
                () => LightClipboard.CopyMain(light),
                () => LightClipboard.PasteMain(lightMain, light));
```

- [ ] **Step 3: 追加ライト行の先頭で呼ぶ**

`DrawAdditionalLightParams` の `followLight = FindFollowLight(light);` ブロックの直後:

```csharp
            // followLight はこの時点で解決済み。ラムダは描画時の参照を掴む
            DrawClipboardRow(view, rowHeight,
                () => LightClipboard.Copy(light, followLight),
                () => LightClipboard.Paste(light, followLight));
```

- [ ] **Step 4: 両構成でビルドする**

Task 1 Step 2 と同じ。Expected: エラー 0

- [ ] **Step 5: 実機確認（ゲーム起動中に DLL を差し替えられる場合のみ）**

追加ライト A をスポット・赤・強度 2 にしてコピー、追加ライト B でペースト。B が同じ設定になり位置は動かないこと。Undo で B が戻ること。メインライトでペーストして回転と色だけ変わること

---

### Task 3: CHANGELOG とコミット

- [ ] **Step 1: `## 未リリース` 先頭に追記**

```markdown
- ライトのパラメータをコピー / ペーストできるようになりました
  - ライトウィンドウと Inspector のライト行にボタンが付きます。別のライトへ設定を写せます
  - 位置は写しません。メインライトへは回転・色・強度・影だけ反映されます
```

- [ ] **Step 2: コミット**

```
git add source/COM3D2.SceneEditor.Plugin/LightClipboard.cs source/COM3D2.SceneEditor.Plugin/LightRowDrawer.cs source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj CHANGELOG.md docs/superpowers/plans/2026-09-12-light-clipboard.md
git commit -m "feat(light): ライトのパラメータをコピー / ペーストできるようにする"
```

## Self-Review

- 仕様の各項目は Task 1（値の範囲・メイン/追加の非対称）と Task 2（UI・履歴・無効表示）で網羅
- `LightClipboard.Copy/Paste/CopyMain/PasteMain/hasData` の名前は Task 1 と Task 2 で一致
- 位置を写さない判断は仕様に明記。要望があれば `Data.position` を足すだけで拡張できる

## レビュー却下メモ

- `maidSlotNo` はシーン跨ぎで別メイドを指しうる — 追従機能自体がスロット番号で管理しており、履歴・タイムラインキーも同じ。クリップボード固有の問題ではない
- 位置を含めるかユーザー確認すべき — 報告後にユーザーから位置も欲しいと要望があり、含める仕様に変更した
- `FindFollowLight` が null のとき追従が写らないフィードバックが無い — タイムライン未収集は一時的な状態で、追従行自体も出ない。未確認のまま見送り
- `CopyMain` が range/spotAngle に既定値を詰めるのが紛らわしい — コメントで説明済み。スタイルの範囲

## コードレビュー却下メモ

- `ScenePresetData` / `LightSnapshot` が UI クラス `LightRowDrawer` の既定値定数と `FindFollowLight` に依存する — 定数は既に public で `StudioLightManager` の既定値と同じ扱い。移設は別リファクタとして見送り
- `CaptureState` の `FindFollowLight` が O(N²) — ライトは数灯〜十数灯で実害なし
- コピー時に追従未収集なら「追従なし」で静かに記録される — 計画レビュー時と同じ理由で見送り
