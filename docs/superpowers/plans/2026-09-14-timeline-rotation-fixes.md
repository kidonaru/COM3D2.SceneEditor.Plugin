# タイムライン回転表現 優先度「高」修正 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
> （このリポジトリでは CLAUDE.md の規定により subagent-driven-development は使わない）

**Goal:** 回転表現の調査で「優先度 高」となった 2 件（リムライト光源方向の無補間、未配線の `isFixRotation`）を解消する。あわせて、その修正が依存する `baseValues` のキャッシュ不整合を先に塞ぐ。

**Architecture:** リムライトの無補間は `TransformDataBase.lerpKinds` が構造値（位置・回転・拡縮）を補間対象から取りこぼしていることが原因なので、個別型ではなく `lerpKinds` 側を直して同種の取りこぼしを塞ぐ。その前提として、`lerpKinds` が新たに依存する `baseValues` のキャッシュが `Clone` / `Initialize` で破棄されない既存の不具合を先に直す。`isFixRotation` は読み手が無いプロパティの純粋な削除。

**Tech Stack:** C# / .NET 3.5（COM3D2 構成）および .NET 4.7.1（COM3D25 構成） / Unity / xUnit

**Spec:** `docs/timeline-rotation-quaternion-survey.md`（5-3 / 5-4 が本計画の対象。5-1 は調査で挙動が正しいと判明したため可読性改善のみで、Task 4 は任意）

## Global Constraints

- **2 構成ビルド必須**: `GameVersion=COM3D2`（.NET 3.5）と `GameVersion=COM3D25`（.NET 4.7.1）の両方をビルドする。片方だけの成功を「ビルド成功」と報告しない。
- **`debug.bat` / `deploy.bat` / `release.bat` は使わない**。`debug.bat` はゲームフォルダへ DLL をコピーするため、MSBuild を直接叩く。
- **ビルド順**: `COM3D2` → `COM3D25` → `dotnet test`。`GameVersion=COM3D2` のビルドは `bin/Debug/COM3D25/` の DLL を消すため、テストは必ず COM3D25 ビルドの後に走らせる。
- **Git Bash から MSBuild を叩くときは `export MSYS2_ARG_CONV_EXCL="*"` を先に実行する**。付けないと `/p:` がパスへ変換され MSB1008 になる。MSBuild の実体は `/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe`（PATH に無い）。
- **XML 形式は変更しない**。`TimelineData.CurrentVersion` は 34 のまま。本計画の全タスクは保存形式・値の個数・添字を変えない。
- **コメントとログメッセージは日本語**で書く。
- **.NET 3.5 の制約**: 入力 5 個以上の `Func<>` / `Action<>` は使えない。本計画では該当しないが、実装中に導入しないこと。
- **コミットメッセージの Co-Authored-By / Claude-Session 行は、本ファイルのテンプレートをそのまま使わず、実装セッション時点の attribution 指示に従う**。テンプレート中の行は計画作成時点のもの。

### 共通コマンド

ビルド（2 構成）とテストを 1 本で回す:

```bash
export MSYS2_ARG_CONV_EXCL="*"
MSBUILD="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin
"$MSBUILD" source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D2  "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" /v:minimal \
  && "$MSBUILD" source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj /p:Configuration=Debug /p:GameVersion=COM3D25 "/p:COM3D2_DIR=W:\COM3D2" "/p:COM3D25_DIR=W:\COM3D2_5" /v:minimal \
  && dotnet test source/COM3D2.SceneEditor.Plugin.Tests/COM3D2.SceneEditor.Plugin.Tests.csproj
```

単一テストだけ走らせたいとき（プラグインは COM3D25 構成でビルド済みであること）:

```bash
dotnet test source/COM3D2.SceneEditor.Plugin.Tests/COM3D2.SceneEditor.Plugin.Tests.csproj --filter "FullyQualifiedName~TransformLerpFromTests"
```

---

## Task 1: `baseValues` のキャッシュを `Clone` / `Initialize` で破棄する

**背景:** `TransformDataBase.Clone()`（`:1368-1388`）はコメントで「浅いコピーでは元インスタンスの `ValueData` を指すキャッシュを引き継いでしまう」と述べ、`ClearValueDataListCache()` を呼んで各種キャッシュを捨てている。ところが `_baseValues`（`:277`）はそのリセット対象から漏れている。`Initialize()`（`:369-377`）も `_values` を差し替えたあとに同じメソッドを呼ぶだけなので、同様に漏れる。

結果として、複製前に `.baseValues` を一度でも評価していた場合、複製先の `_baseValues` は複製元の `ValueData` を指したまま残り、複製先自身の `values` とは `ReferenceEquals` が一致しなくなる。`TransformDataMove` / `TransformDataModel` / `TransformDataModelBone` / `TransformDataBGModel` の 4 型は `tangentValues => baseValues` としてこの値を使っているため、既に影響を受けうる。

Task 2 は `lerpKinds` から `baseValues` を参照する新たな依存を足すので、先にこれを塞ぐ。

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataBase.cs:37-44`（`ClearValueDataListCache`）
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/TransformLerpFromTests.cs`

**Interfaces:**
- Consumes: `TransformDataBase.baseValues`（既存の public プロパティ）、`TransformDataBase.values`（既存の public プロパティ）
- Produces: `Clone()` 後・`Initialize()` 後の `baseValues` が自インスタンスの `ValueData` を指すようになる。Task 2 はこの保証に依存する

- [ ] **Step 1: 失敗するテストを書く**

`source/COM3D2.SceneEditor.Plugin.Tests/TransformLerpFromTests.cs` の `TransformLerpFromTests` クラス内、`文字列値は区間開始値になる()` の後ろに以下を追加する。ファイル冒頭の `using` は既存のもので足りる。

```csharp
        private static TransformDataRimlight CreateRimlight()
        {
            var trans = new TransformDataRimlight();
            trans.Initialize("Rimlight");
            return trans;
        }

        [Fact]
        public void Clone後のbaseValuesは複製先のValueDataを指す()
        {
            var start = CreateRimlight();
            // 複製前にキャッシュを作らせる (これが無いと不具合が再現しない)
            var _ = start.baseValues;

            var clone = (TransformDataRimlight)start.Clone();

            // Rimlight の baseValues は eulerAnglesValues = values[0..2]
            Assert.Same(clone.values[0], clone.baseValues[0]);
            Assert.NotSame(start.values[0], clone.baseValues[0]);
        }
```

- [ ] **Step 2: テストを走らせて失敗を確認する**

```bash
dotnet test source/COM3D2.SceneEditor.Plugin.Tests/COM3D2.SceneEditor.Plugin.Tests.csproj --filter "FullyQualifiedName~TransformLerpFromTests"
```

期待: `Clone後のbaseValuesは複製先のValueDataを指す` が失敗する。`clone.baseValues[0]` が複製元の `ValueData` のままなので `Assert.Same` が落ちる。

- [ ] **Step 3: `ClearValueDataListCache` に `_baseValues` を足す**

`source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataBase.cs` の `ClearValueDataListCache` を書き換える。

変更前:

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

変更後:

```csharp
        private void ClearValueDataListCache()
        {
            _valueDataListCache = null;
            _inTangentListCache = null;
            _outTangentListCache = null;
            _valuesWithoutColors = null;
            _lerpKinds = null;
            // baseValues も values の ValueData を直接参照するキャッシュなので一緒に捨てる。
            // 捨て忘れると Clone 後に複製元の ValueData を指したまま残り、
            // tangentValues => baseValues としている型が複製元へ書き込んでしまう
            _baseValues = null;
        }
```

- [ ] **Step 4: テストを走らせて通ることを確認する**

```bash
dotnet test source/COM3D2.SceneEditor.Plugin.Tests/COM3D2.SceneEditor.Plugin.Tests.csproj --filter "FullyQualifiedName~TransformLerpFromTests"
```

期待: 全 5 テストが PASS。

- [ ] **Step 5: 2 構成ビルドと全テストを回す**

「共通コマンド」のビルド＋テスト 1 本を実行する。

期待: 2 構成ともビルド成功、テスト全件 PASS。`tangentValues => baseValues` を使う 4 型（Move / Model / ModelBone / BGModel）の挙動が変わりうるため、`KeyFrameTangentLogicTests` / `TangentUnificationTests` / `XmlRoundTripTests` の結果に特に注意する。落ちた場合は、そのテストが「壊れた状態」を期待値として固定していなかったかを確認してから判断する。

- [ ] **Step 6: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataBase.cs source/COM3D2.SceneEditor.Plugin.Tests/TransformLerpFromTests.cs
git commit -m "$(cat <<'EOF'
fix(timeline): baseValues のキャッシュを Clone / Initialize で捨てる

ClearValueDataListCache は values の ValueData を指すキャッシュを捨てる
メソッドだが _baseValues が対象から漏れていた。複製前に baseValues を
評価していると、複製先が複製元の ValueData を指したまま残る。
tangentValues => baseValues としている 4 型 (Move / Model / ModelBone /
BGModel) が影響を受けうる。
EOF
)"
```

---

## Task 2: `lerpKinds` が構造値を補間対象に含める（リムライト光源方向の無補間を解消）

**背景:** `TransformDataBase.LerpFrom` は値ごとの補間種別を `lerpKinds` で決める。現在の `lerpKinds` は `GetCustomValueInfoMap()` に登録された値しか `LerpKind.Tangent` へ昇格させないため、`CustomValueInfoMap` に載らない構造値（位置・回転・拡縮）が `LerpKind.Hold`（区間開始値をコピー）に落ちる。`TransformDataRimlight` は `hasEulerAngles => true` で Euler X/Y/Z を持つがマップには載っていないため、光源方向が補間されず階段状に切り替わる。移植元の MTE では `RimlightData.Lerp` が `Vector3.Lerp` で補間していたので、これは移植による退行。

`CustomValueInfoMap` へ Euler を足す案は採らない。`KeyFrameInspector.cs:397` と `KeyFrameBatchDrawer.cs:191` が `hasEulerAngles` を見て回転行を別途描いているため、マップに足すと同じ値が 2 重に描画される。

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataBase.cs:645-706`（`lerpKinds` プロパティ）
- Test: `source/COM3D2.SceneEditor.Plugin.Tests/TransformLerpFromTests.cs`

**Interfaces:**
- Consumes: Task 1 が保証する「`baseValues` が自インスタンスの `ValueData` を指す」こと。`TransformDataBase.tangentValues`、`LerpKind` enum（既存の private enum。`Hold` / `Linear` / `Tangent`）、Task 1 で追加した `CreateRimlight()` ヘルパー
- Produces: 振る舞いの変更のみ。公開シグネチャの追加・変更はなし

- [ ] **Step 1: 失敗するテストを書く**

`TransformLerpFromTests.cs` の `Clone後のbaseValuesは複製先のValueDataを指す()` の後ろに以下を追加する。`CreateRimlight()` は Task 1 で追加済みなので再定義しないこと。

```csharp
        private static TransformDataRimlight LerpRimlight(
            TransformDataRimlight start, TransformDataRimlight end, float t)
        {
            var scratch = (TransformDataRimlight)start.Clone();
            scratch.LerpFrom(start, end, 0f, 1f, t);
            return scratch;
        }

        [Fact]
        public void リムライトの光源方向はタンジェント補間される()
        {
            var start = CreateRimlight();
            var end = CreateRimlight();
            start.eulerAngles = new Vector3(0f, 0f, 0f);
            end.eulerAngles = new Vector3(90f, 40f, 20f);

            var mid = LerpRimlight(start, end, 0.25f);

            var startValues = start.eulerAnglesValues;
            var endValues = end.eulerAnglesValues;
            for (var i = 0; i < 3; i++)
            {
                var expected = MTEP.PluginUtils.HermiteValue(
                    0f, 1f, startValues[i], endValues[i], 0.25f);
                Assert.Equal(expected, mid.eulerAngles[i], 4);
            }

            // Hold に誤分類されると区間開始値 0 のままになる (移植時の退行の再発検出)
            Assert.NotEqual(0f, mid.eulerAngles.x, 4);
        }

        [Fact]
        public void リムライトの表示トグルは構造値の昇格に巻き込まれない()
        {
            var start = CreateRimlight();
            var end = CreateRimlight();
            start.visible = false;
            end.visible = true;

            var mid = LerpRimlight(start, end, 0.9f);

            Assert.False(mid.visible);
        }
```

- [ ] **Step 2: テストを走らせて失敗を確認する**

```bash
dotnet test source/COM3D2.SceneEditor.Plugin.Tests/COM3D2.SceneEditor.Plugin.Tests.csproj --filter "FullyQualifiedName~TransformLerpFromTests"
```

期待: `リムライトの光源方向はタンジェント補間される` が失敗する。`mid.eulerAngles.x` が区間開始値の `0` のままなので、`Assert.Equal(expected, ...)` か末尾の `Assert.NotEqual(0f, ...)` のいずれかが落ちる。`リムライトの表示トグルは構造値の昇格に巻き込まれない` は最初から通る（現状も Hold のため）。

- [ ] **Step 3: `lerpKinds` を直す**

`TransformDataBase.cs` の `lerpKinds` プロパティ内で、タンジェント添字を集めるループを次のように書き換える。`bases` の収集を既存ループに相乗りさせる。

変更前:

```csharp
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
```

変更後:

```csharp
                var tangentIndices = new HashSet<int>();
                var baseIndices = new HashSet<int>();
                var tangents = tangentValues;
                var bases = baseValues;
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
                    foreach (var baseValue in bases)
                    {
                        if (ReferenceEquals(values[i], baseValue))
                        {
                            baseIndices.Add(i);
                            break;
                        }
                    }
                }

                // 位置・回転・拡縮は CustomValueInfoMap に載らないが、いずれも連続値なので
                // タンジェント補間の対象にする。ここで拾わないと Hold に落ちて補間が効かない
                // (リムライトの光源方向が MTE からの移植時にこれで無補間になっていた)。
                // baseValues は sub 系 (subPosition / subEulerAngles) を含まない。
                // LerpFrom を通る型に sub 系を持つものは無いため現状はこれで足りるが、
                // sub 系を持つ型を LerpFrom 経路へ乗せるときはここも見ること
                foreach (var index in baseIndices)
                {
                    if (kinds[index] == LerpKind.Hold && tangentIndices.Contains(index))
                    {
                        kinds[index] = LerpKind.Tangent;
                    }
                }
```

既存の `GetCustomValueInfoMap()` を回すループはそのまま残す（順序は問わない。どちらも `Hold` のときだけ昇格させるため競合しない）。

- [ ] **Step 4: テストを走らせて通ることを確認する**

```bash
dotnet test source/COM3D2.SceneEditor.Plugin.Tests/COM3D2.SceneEditor.Plugin.Tests.csproj --filter "FullyQualifiedName~TransformLerpFromTests"
```

期待: 全 7 テストが PASS。

- [ ] **Step 5: 2 構成ビルドと全テストを回す**

「共通コマンド」のビルド＋テスト 1 本を実行する。

期待: 2 構成ともビルド成功、テスト全件 PASS。

この変更の直接的な回帰確認は本タスクで追加した `TransformLerpFromTests` の 3 件と全件 PASS に依る。`lerpKinds` / `LerpFrom` を直接検証している既存テストは他に無いため、既存テストは「無関係な箇所を壊していないこと」の確認として扱う。

- [ ] **Step 6: 実機でリムライトの補間を確認する**

ゲームが起動していれば、以下を確認する。起動していなければこのステップは飛ばし、その旨をユーザーに報告する。

1. リムライトのレイヤーでフレーム 0 と 60 に光源方向の違うキーを打つ
2. 再生して、途中フレームで光源方向がなめらかに変化することを確認する（修正前は 60 フレーム目で不連続に切り替わる）

期待: なめらかに補間される。

- [ ] **Step 7: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataBase.cs source/COM3D2.SceneEditor.Plugin.Tests/TransformLerpFromTests.cs
git commit -m "$(cat <<'EOF'
fix(timeline): リムライトの光源方向が補間されない問題を直す

LerpFrom の補間種別を決める lerpKinds が CustomValueInfoMap 登録値しか
タンジェント補間へ昇格させず、マップに載らない構造値 (位置・回転・拡縮) が
Hold に落ちていた。リムライトの光源方向は編集・保存はできるのに再生時は
階段状に切り替わっており、移植元の MTE (RimlightData.Lerp) にあった補間が
失われていた。baseValues の添字も昇格対象に含めて解消する。
EOF
)"
```

---

## Task 3: 未配線の `isFixRotation` を削除する

**背景:** `isFixRotation` は「キー確定時の回転連続性補正（`FixRotation` / `FixEulerAngles`）を型ごとに OFF にする」意図のフラグだが、実行元の `TimelineLayerBase.FixRotation`（`:625-647`）は `hasRotation` / `hasEulerAngles` しか見ておらず一度も読まれない。移植元の MTE 本体でも同じ 3 箇所にしか出現せず読み手が無いため、移植時の取りこぼしではなく元から未配線。ユーザー判断により削除する。

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/ITransformData.cs:191`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataBase.cs:222`
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataPsylliumTransform.cs:21`

**Interfaces:**
- Consumes: なし
- Produces: `ITransformData` から `isFixRotation` が消える。読み手が無いため他タスクへの影響はない

- [ ] **Step 1: 削除前に読み手が無いことを再確認する**

```bash
grep -rn "isFixRotation" --include=*.cs source/
```

期待: ヒットが以下の 3 行だけであること。4 行目以降が出たら削除せず、その参照箇所を報告して判断を仰ぐ。

```
source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/ITransformData.cs:191:        bool isFixRotation { get; }
source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataBase.cs:222:        public virtual bool isFixRotation => true;
source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataPsylliumTransform.cs:21:        public override bool isFixRotation => false;
```

- [ ] **Step 2: 3 箇所を削除する**

`ITransformData.cs` から次の 1 行を削除する:

```csharp
        bool isFixRotation { get; }
```

`TransformDataBase.cs` から次の 1 行を削除する:

```csharp
        public virtual bool isFixRotation => true;
```

`TransformDataPsylliumTransform.cs` から次の 1 行を削除する。前後に空行が残る場合は空行も 1 行ぶん詰める:

```csharp
        public override bool isFixRotation => false;
```

- [ ] **Step 3: 参照が残っていないことを確認する**

```bash
grep -rn "isFixRotation" --include=*.cs source/
```

期待: ヒット 0 件（コマンドは終了コード 1 を返す）。

- [ ] **Step 4: 2 構成ビルドと全テストを回す**

「共通コマンド」のビルド＋テスト 1 本を実行する。

期待: 2 構成ともビルド成功、テスト全件 PASS。`ITransformData` はプラグイン内部のインターフェースなので、実装漏れがあればコンパイルエラーで即座に分かる。

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/ITransformData.cs source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataBase.cs source/COM3D2.SceneEditor.Plugin/Timeline/TransformData/TransformDataPsylliumTransform.cs
git commit -m "$(cat <<'EOF'
refactor(timeline): 未配線の isFixRotation を削除する

回転連続性補正を型ごとに OFF にする意図のフラグだったが、実行元の
TimelineLayerBase.FixRotation は hasRotation / hasEulerAngles しか見ておらず
一度も読まれていなかった。移植元の MTE でも同様に未配線だったため、
意図が実装されていないまま残っていたことになる。読み手の無い宣言を削除する。
EOF
)"
```

---

## Task 4（任意）: `StageLaser` の twist 抽出に意図を明示する（挙動不変）

> **このタスクは spec の優先度「低」の項目であり、ユーザーの「優先度高の計画作成して」という指示の範囲外。**
> 実施するかはユーザーに確認してから決めること。不要なら Task 4 を飛ばし、Task 5 の記述からも 5-1 の行を削る。

**背景:** `StageLaser.UpdateTransform` はクォータニオンの x / y に 0 を代入している。これは一見オイラー成分の取り違えに見えるが、実際には Z 軸まわりの swing-twist 分解の twist 抽出であり結果は正しい。レーザーのメッシュは局所 XZ 平面のリボン（`:493-513` の頂点生成。幅が X、ビーム長が Z、法線が ±Y）で、`localRotation` は親＝`StageLaser` 基準なので `(0, 0, z, w)` を正規化するとビーム軸まわりの回転成分だけが残る。

非正規化のまま代入している点も、Unity の `Transform.localRotation` setter が正規化するため実害が無い（実機で確認済み。`(0,0,0.3,0.4)` を代入して読み戻すと `(0,0,0.6,0.8)`。退化ケース `(0,0,0,0)` は identity になり NaN にならない）。

したがって挙動は変えず、暗黙の前提を読める形にするだけにする。`Quaternion.Normalize` は setter の挙動と完全に一致する（退化ケースも identity。実機で確認済み）ので、明示化しても結果は変わらない。

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/UnityScripts/StageLaser.cs:598-603`

**Interfaces:**
- Consumes: なし
- Produces: なし（挙動不変のコメント追記と正規化の明示のみ）

- [ ] **Step 1: 該当箇所を書き換える**

変更前:

```csharp
                _meshFilter.transform.LookAt(camera.transform, transform.forward);
                
                var localRotation = _meshFilter.transform.localRotation;
                localRotation.x = 0;
                localRotation.y = 0;
                _meshFilter.transform.localRotation = localRotation;
```

変更後:

```csharp
                _meshFilter.transform.LookAt(camera.transform, transform.forward);

                // メッシュは局所 XZ 平面のリボン (幅が X、ビーム長が Z、法線が ±Y) なので、
                // ビーム軸を保ったままリボンだけをカメラへ向けたい。
                // localRotation は親 (StageLaser) 基準なので、(0, 0, z, w) を正規化すると
                // 親の Z 軸 = ビーム軸まわりの回転成分だけが残る (swing-twist 分解の twist)。
                // z も w も 0 になる退化ケースでは identity に落ちてロール 0 になる
                var localRotation = _meshFilter.transform.localRotation;
                _meshFilter.transform.localRotation = Quaternion.Normalize(
                    new Quaternion(0f, 0f, localRotation.z, localRotation.w));
```

- [ ] **Step 2: 2 構成ビルドと全テストを回す**

「共通コマンド」のビルド＋テスト 1 本を実行する。

期待: 2 構成ともビルド成功、テスト全件 PASS。`StageLaser` は `MonoBehaviour` でユニットテスト対象外のため、テストは回帰が無いことの確認に使う。

- [ ] **Step 3: 実機で見た目が変わっていないことを確認する**

ゲームが起動していれば、ステージレーザーを配置して以下を確認する。起動していなければこのステップは飛ばし、その旨をユーザーに報告する。

1. タイムラインでステージレーザーを表示する
2. カメラを水平・真上・真下へ動かし、レーザーのリボンが常にカメラを向き、ビームの向き自体は変わらないことを目視する
3. カメラをレーザーの真後ろ（ビーム軸の延長線上）へ置き、描画が破綻しないことを確認する（退化ケース）

期待: 変更前と見た目が変わらない。差が出たら twist 抽出の前提が崩れているので、変更を戻して報告する。

- [ ] **Step 4: コミット**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/UnityScripts/StageLaser.cs
git commit -m "$(cat <<'EOF'
docs(timeline): StageLaser の twist 抽出の意図をコードに残す

クォータニオンの x/y へ 0 を代入する処理は Z 軸まわりの swing-twist 分解の
twist 抽出だが、暗黙の正規化 (Transform setter) と分解の前提が読み取れず
成分の取り違えに見えていた。Quaternion.Normalize を明示しコメントを足す。
挙動は変えない。
EOF
)"
```

---

## Task 5: 調査ドキュメントに対応済みを反映する

**背景:** `docs/timeline-rotation-quaternion-survey.md` は本計画の spec であり、対応状況を残さないと次に読んだときに未修正と誤読される。`docs/layer-window-duplication-survey.md` が冒頭に対応状況の注記を置く形式を採っているので、それに倣う。

**Files:**
- Modify: `docs/timeline-rotation-quaternion-survey.md`（冒頭ヘッダー部と 8 章の優先度表）

**Interfaces:**
- Consumes: Task 1〜4 のコミットハッシュ
- Produces: なし

- [ ] **Step 1: コミットハッシュを控える**

```bash
git log --oneline -5
```

Task 1 / 2 / 3（と、実施した場合は Task 4）のハッシュを控える。

- [ ] **Step 2: 冒頭に対応状況の注記を追加する**

`調査日: 2026-09-14` から始まるヘッダー部の直後、`## 結論` の前に以下を挿入する。`<hash1>` 〜 `<hash4>` は Step 1 で控えたハッシュに置き換える。Task 4 を実施していない場合は最後の一文を削る。

```markdown
> **2026-09-14: 優先度「高」は対応済み。** 5-4 リムライト光源方向の無補間（`<hash2>`）と
> 5-3 `isFixRotation` の削除（`<hash3>`）を実施した。あわせて 5-4 の修正が依存する
> `baseValues` のキャッシュ不整合も修正している（`<hash1>`。本書の調査時点では未検出だった）。
> 5-1 は挙動が正しいと判明したため意図の明示のみ行った（`<hash4>`）。
> 優先度 中 / 低 と「保留」は未対応。
```

- [ ] **Step 3: 8 章の優先度表に状態列を足す**

8 章の表に「状態」列を追加し、各行へ次の値を入れる。`timeline-release-debt-review.md` の表記に合わせる。

| 対象 | 状態 |
|---|---|
| 5-3 `isFixRotation` の未使用 | 対応済（`<hash3>`。削除） |
| 5-4 リムライト光源方向の無補間 | 対応済（`<hash2>`） |
| 5-2 `FrameData.Flip()` のオイラー算術 | 未対応 |
| 3 章の補正ロジック重複 | 未対応 |
| 5-1 `StageLaser` の twist 抽出の明示化 | 対応済（`<hash4>`。挙動不変） |
| 6 章の非正規化 nlerp | 未対応 |
| 1〜2 章のオイラー保持そのもの | 保留 |

Task 4 を実施していない場合、5-1 の行は「未対応」にする。

- [ ] **Step 4: コミット**

```bash
git add docs/timeline-rotation-quaternion-survey.md
git commit -m "$(cat <<'EOF'
docs(timeline): 回転表現調査に優先度高の対応状況を反映する
EOF
)"
```

---

## 完了条件

- [ ] `grep -rn "isFixRotation" --include=*.cs source/` が 0 件
- [ ] `TransformLerpFromTests` にリムライトの補間テストと `baseValues` キャッシュのテストがあり、緑
- [ ] 2 構成（COM3D2 / COM3D25）ともビルド成功
- [ ] `dotnet test` 全件 PASS
- [ ] `docs/timeline-rotation-quaternion-survey.md` に対応状況が反映されている
- [ ] 実装完了後、ユーザーへ提示する前に `code-review` スキルでレビュー済み

## スコープ外（今回は触らない）

- 5-2 `FrameData.Flip()` のオイラー算術による左右反転（優先度 中。挙動変更を伴い実機検証が必要）
- 3 章の 360 度補正ロジックの共通化（優先度 中）
- 6 章の `HermiteQuaternion` 非正規化 nlerp（優先度 低）
- 1〜2 章のオイラー保持そのものの置き換え（XML version 35 とマイグレーションのセットになるため保留）

## レビュー却下メモ

- **`LerpFrom` を通る全型で sub 系が false であることを検証するガードテストを足す** — 却下。現状 sub 系を持つ型（`TransformDataStageLightController` / `TransformDataPsylliumTransform`）は `LerpFrom` 経路に乗っておらず、乗せる改修自体が別途の設計判断になる。その時点で `lerpKinds` を見直すべきことは Task 2 Step 3 のコメントで明示したため、現時点でのテスト追加は YAGNI と判断した。
- **コミットメッセージの `Co-Authored-By` が `Claude Opus 5` でセッションと不一致** — 却下（誤検知）。レビュー側サブエージェントが自分の実行モデルの attribution を基準に比較したもの。親セッションの指示は `Claude Opus 5` で一致している。ただし実装が別セッション・別モデルで行われる可能性はあるため、テンプレートから attribution 行を外し「実装セッション時点の指示に従う」旨を Global Constraints に明記する形で対応した。
