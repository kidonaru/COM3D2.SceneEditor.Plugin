# プラグイン無効化時の復帰 設計メモ

## 背景

`2e6ca99` で `TimelineManager.OnPluginDisable` が `UnloadTimeline()` を呼ぶようにした。
その結果 `timeline` が `null` になり、**再有効化で各マネージャの状態が復帰しなくなった**。

`TimelineUpdateManager.OnLoad()` は次のガードで抜けるため、`OnLoad` で復元していたものが全て止まる。

```csharp
if (studioHack == null || timelineManager.timeline == null)
{
    return;
}
```

実機で計測した退行（タイムライン読込 → 無効化 → 再有効化）:

| 観測対象 | 読込後 | 無効化後 | 再有効化後 |
|---|---|---|---|
| `StudioLightManager(MTE).lights` | 3 | 0 | **0（戻らない）** |
| `StudioLightManager(SE).lights`（追加ライト） | 2 | 0 | 0 |
| メインライトの色 | 青 | 青のまま | 青のまま |

## 決定事項

### 1. 復帰が要るマネージャと要らないマネージャ

`OnPluginDisable` で壊すものを「タイムライン由来の実体」と「タイムラインと無関係な基盤」に分ける。
**前者は未読込なら 0 が正しい状態なので復帰させない。**

| マネージャ | `OnPluginDisable` | 分類 | 対応 |
|---|---|---|---|
| `StudioLightManager`(MTE) | `Reset()` | 基盤（メインライトは常に一覧へ載る） | **復帰する** |
| `CameraManager` | `DestroyCamera()` | 基盤（`MTEFrontCamera`） | **復帰する** |
| `SubCameraManager` | `DestroyAllCameras()` | 基盤（最小 1 台は常にある） | **復帰する** |
| `TimelineTextManager` | `ReleaseTexts()` | 基盤（`_standaloneTextCount` を持つ） | **復帰する** |
| `StageLightManager` / `StageLaserManager` / `PsylliumManager` | `Reset()` | タイムライン由来 | しない |
| `StudioModelManager` / `PngObjectTimelineManager` / `BGModelManager` / `BGGroundManager` | `Reset()` / `Release()` | タイムライン由来 | しない |
| `MovieManager` / `BGMManager` | `UnloadMovie()` / `Stop()` | タイムライン由来 | しない |
| `PostEffectManager` | `DisableAllEffects()` | `InitPostEffects()` が `timeline != null` ガード付き | しない |
| `MaidManager` | `ResetEyes()` + `Reset()` | `PreUpdate()` が毎フレーム自己修復する | しない |

`MaidManager` を対象外にできる根拠は `TimelineUpdateManager.UpdateGuards()` が
ガード判定より**先**に `maidManager.PreUpdate()` を呼ぶこと。タイムライン未読込でも走る。

### 2. 復帰は `OnPluginEnable` で行う

`TimelineUpdateManager.OnPluginEnable()` はガード無しで全マネージャへ配信される。
`OnLoad()` と違い `timeline == null` でも通るので、ここが timeline 非依存の復帰点になる。

`StageLightManager` / `StageLaserManager` / `PsylliumManager` は既に空の `OnPluginEnable` を持つ。
この空実装は「タイムライン由来なので復帰しない」という判断の表明として残す。

### 3. メインライトは「プラグインが最初に掌握した時点」の値へ戻す

#### 控えるタイミング

有効化時には控えられない。`SceneEditorPlugin.OnPluginEnable` の順序が次のようになっており、
`OnLoad()`（タイムラインの値をシーンへ適用する）が `managerRegistry.OnPluginEnable()` より**先**だからである。

```csharp
private void OnPluginEnable()
{
    MTEUtils.Log("プラグインが有効になりました");
    OnLoad();                          // ← ここでタイムラインの値が適用される
    managerRegistry.OnPluginEnable();  // ← ここで控えても手遅れ
    ...
}
```

そこで **`Update()` で「まだ控えていなければ控える」** 方式にする。
メインライトはシーンによっては後から現れる（`GameMain.Instance.MainLight` が null を返す場面がある）ため、
取得できるまで毎フレーム試す形が確実で、`Init()` の 1 回きりより漏れが少ない。

シーンが変わるとメインライトは作り直されるので、`OnChangedSceneLevel` で控え直す。

#### 戻すタイミング

`TimelineUpdateManager._managers` の並びは `... MaidManager, TimelineManager, StudioLightManager(MTE), ...` で、
`TimelineManager.OnPluginDisable`（アンロードと断面復元）の**直後**に `StudioLightManager(MTE).OnPluginDisable` が来る。
ここで戻せば、断面復元が書いた値を上書きする形になり順序が正しい。

SE 側の `StudioLightManager.OnPluginDisable` は `managerRegistry` の 7 番目で
`TimelineUpdateManager`（19 番目）より先に走るため、**SE 側に復元を置いてはいけない**。
スナップショットの保持は SE 側（`mainLight` の窓口があるため）、復元の呼び出しは MTE 側から行う。

#### 控える値

`LightTimelineLayer` がメインライトへ書くもの全てを控える。

| 値 | 出所 |
|---|---|
| `transform.localPosition` / `localRotation` | `TransformDataLight` の position / rotation |
| `color` / `range` / `intensity` / `spotAngle` | `CustomValueInfoMap` |
| `shadowStrength` / `shadowBias` | 同上 |
| `cullingMask` | `LightTarget.ToCullingMask` |
| `enabled` | `StudioLightStat.visible` |

ゲーム側の `LightMain.Reset()` は既定値（白・intensity 0.95・回転 (40,180,18)）へ戻す API だが、
**使わない**。ユーザーがプラグイン起動前に写真モード等で設定していた値を壊すため。

#### 許容するトレードオフ

復元は「今回タイムラインがメインライトを触ったか」を区別せず、**無効化のたびに無条件で走る**。
そのため次の挙動になる。

- `ライト` レイヤーを一度も使わずに無効化しても、控えた値へ戻る
- プラグイン有効中にゲーム側の機能でメインライトを変えても、無効化でその変更が巻き戻る

これを許容する理由は 2 つある。

1. 「プラグインが触ったか」を正確に判定する手段が無い。`LightTimelineLayer` は
   `light.color = ...` のように `Light` へ直接書くのでフックできず、プラグインのライトウィンドウ
   経由の変更も同じ経路を通るため、タイムライン由来かユーザー操作かを区別できない
2. プラグインのライトウィンドウで変えた値は「プラグインが触った」ものなので、戻るのが期待どおり。
   区別できずに巻き戻るのはゲーム側 UI で変えた場合だけで、プラグインを開いたままゲーム側の
   ライト UI を操作する場面は限られる

この挙動が許容できないと分かった場合は、スナップショットの更新契機を
「プラグイン起動時の 1 回」から「タイムラインを読み込む直前」へ移す案が次の候補になる。
ただしそれは「プラグイン起動前の値へ戻す」という今回の要件からは外れる。

## 却下した案

- **アンロードを取りやめる（`2e6ca99` を revert）** — 却下。プラグインを閉じたらシーンから実体を消したい、という要望が満たせない
- **再有効化で同じタイムラインを読み直す** — 却下。実装は小さいが、未保存の変更が保存済みの内容で上書きされる
- **`LightMain.Reset()` を使う** — 却下。プラグイン起動前の値ではなくゲームの既定値へ飛ぶ
- **`SceneEditorPlugin.OnPluginEnable` の `OnLoad()` と `managerRegistry.OnPluginEnable()` を入れ替える** — 却下。スナップショットのためだけに既存の初期化順序を変えるのは影響が読み切れない
