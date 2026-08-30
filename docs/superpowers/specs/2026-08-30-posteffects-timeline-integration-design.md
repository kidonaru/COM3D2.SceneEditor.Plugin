# PostEffects.Plugin タイムライン連携 設計

## 目的

SceneEditor のタイムライン（MTE 移植）と PostEffects.Plugin は、タイムライン対応 5 系統
（DoF / GTToneMap / パラフィン / 距離フォグ / リムライト）についてメインカメラ上に
**別々の実体**を持っており、併用すると同じエフェクトが二重に掛かりうる。

本設計はエフェクト実体を PostEffects.Plugin 側へ一本化し、SceneEditor のタイムラインは
ブリッジ経由で PostEffects.Plugin の設定値を駆動する構造へ改める。あわせて
PostEffects.Plugin の MainWindow にタイムライン対応エフェクトの集約ビュー
（「タイムライン」タブ）を追加する。

## 決定事項

| 論点 | 決定 |
|---|---|
| エフェクト実体の所有者 | PostEffects.Plugin 側を正とし、SceneEditor 側の実体は削除する |
| PostEffects.Plugin 未導入時 | 必須依存。ポストエフェクトのタイムラインレイヤー・関連 UI を無効化/非表示にする（フォールバック実装は持たない） |
| 結合方法 | SceneEditor が `COM3D25.PostEffects.Plugin.dll`（両構成とも同名）を直接参照。アクセスは 1 クラスに隔離し、型ロード例外で遅延バインド判定する |
| タイムラインタブと既存タブ | 既存の個別エフェクトタブは残し、タイムラインタブは対応 5 系統の集約ビュー（複製表示）とする |
| v29 プリセットのポストエフェクト部分 | 撤去する。演出カテゴリはテキスト + サブカメラのみ。ポストエフェクトのプリセットは既存の PostEffects サイドカープロバイダに一本化（v29 は未リリースのため互換対応なし） |

## 対象範囲

- 対象エフェクト: 現行 `PostEffectTimelineLayer` が対応する 5 系統のみ
  - DoF（被写界深度、メイド追従スロット含む）
  - GTToneMap
  - パラフィン（複数インスタンス）
  - 距離フォグ（複数インスタンス）
  - リムライト（複数インスタンス）
- PostEffects.Plugin の他エフェクト（Bloom / Vignetting 等 約 30 種）のタイムライン
  キーフレーム対応は**スコープ外**（将来の拡張。今回の構造で妨げない）

## アーキテクチャ

```
SceneEditor.Plugin                       COM3D25.PostEffects.Plugin
┌──────────────────────────┐             ┌──────────────────────────┐
│ PostEffectTimelineLayer  │             │ TimelineBridge (新設)     │
│   │ 補間値を毎フレーム適用 │             │   要素数 get/set          │
│   ▼                      │  DLL 直参照  │   値の Get/Apply (型付き) │
│ PostEffectManager ───────┼────────────▶│   マスター有効フラグ       │
│   (ブリッジ委譲に改修)     │             │        │                 │
│                          │             │        ▼                 │
│ PostEffectsBridge (新設)  │             │ EffectSettings /         │
│   isAvailable 判定・隔離   │             │ 各 Controller / Model    │
└──────────────────────────┘             │ （実体の唯一の所有者）      │
                                         │                          │
                                         │ MainWindow               │
                                         │   +「タイムライン」タブ    │
                                         └──────────────────────────┘
```

### A. PostEffects.Plugin 側: TimelineBridge と タイムラインタブ

**TimelineBridge（public static クラス、新設）**

SceneEditor から呼ぶための公開 API。現行 SceneEditor `PostEffectManager` の API 形を
踏襲し、再生パス（毎フレーム呼び出し）に XML やリフレクションを挟まない:

- 要素数: `paraffinCount` / `distanceFogCount` / `rimlightCount` の get/set
  （set は PostEffects 側データ数の増減へ反映。上限は PostEffects 側の
  `MAX_*_COUNT` 定数で丸める）
- 値: `GetParaffinData(index)` / `ApplyParaffin(index, data)` ほか 5 系統分の Get/Apply
- マスター有効フラグ: 系統ごとの enabled get/set
- DoF のメイド追従スロット等、現行レイヤーが使う付帯 API も同等に公開する

**実装時の追記: パラメータ構成の差分**

パラフィン・リムライトの値クラスは、PostEffects.Plugin 側で深度マスクを廃し
キャラマスク方式へ置き換えられていたため、タイムラインの値も実体へ揃えた:

- パラフィン: `depthMin` / `depthMax` / `depthFade` を廃止し、`maskMode` を追加
- リムライト: 上記 3 件と `edgeDepth` / `edgeRange` / `heightMin` を廃止し、
  `maskMode` / `excludeFace` / `applyHair` を追加
- 距離フォグ・DoF・GT トーンマップは差分なし

廃止した値のスロットを新パラメータが引き継ぐため、旧タイムライン XML の
`depthMin` の値が `maskMode` として読まれる (既定の 0 なら「マスクなし」になる)。

**「タイムライン」タブ（MainWindow 追加）**

- 対応 5 系統の既存描画（各 Controller の描画メソッド）を 1 タブに集約表示する
- 既存の個別タブはそのまま残す（複製表示）
- SceneEditor のタイムライン読込有無で内容は変えない。キーフレーム登録操作は
  従来通り SceneEditor のタイムラインウィンドウ側で行う

### B. SceneEditor 側: 委譲・実体削除・遅延バインド

**PostEffectsBridge（新設）**

- `COM3D25.PostEffects.Plugin.dll` への参照を隔離する唯一のクラス
- 初回アクセス時の型ロード例外を捕捉して `isAvailable` を確定する
  （JIT 遅延ロードを利用する定石。ブリッジ外に PostEffects の型を漏らさない）
- 未導入時の挙動:
  - `PostEffectTimelineLayer` を登録しない（レイヤー一覧に出ない）
  - ポストエフェクト関連 UI を非表示にする
  - 既存タイムライン XML にポストエフェクトレイヤーが含まれる場合は
    レイヤー生成をスキップし、警告ログを出す（読込自体は失敗させない）

**実装時の追記: 連携方式をリフレクションへ変更 (2026-08-30)**

当初は `COM3D25.PostEffects.Plugin.dll` へのコンパイル時参照で設計したが、
UnityInjector がファイル名順にロードするため SceneEditor が先に来てしまい、
ロード時の型解決に失敗する。Mono はこの束縛失敗をプロセス寿命の間キャッシュ
するため、後から PostEffects がロードされても復帰しない (`AssemblyResolve` の
後付けも効かない)。

そのため参照を撤去し、次の方式へ変更した:

- 値は MTEUtils の共有 DTO (`COM3D2.MotionTimelineEditor.PostEffects` 名前空間)
  で受け渡す。両プラグインが同じソースをコンパイルするため CLR 上は別型になり、
  境界では `ReflectionFieldCopier` が同名フィールドを写す
- ホストへの接続は `MTEUtils/PostEffectsClient.cs` が担う。既存の
  `ModelProviderClient` と同じく、ホスト型が見つかるまで再試行し続ける
- レイヤー登録は `TimelineIntegration.Initialize` の 1 回きりではなく、
  接続できたフレームで 1 回だけ行う (`TryRegisterPostEffects`)
- `TimelineBridge` の公開契約はプロパティではなくメソッドに統一した
  (`Delegate.CreateDelegate` でプロパティを直接束縛できないため)

**PostEffectManager の改修**

- 各 Get/Apply・要素数プロパティ・enabled 操作の実装をブリッジ呼び出しへ置換
- `Timeline/UnityScripts/PostEffect/` のエフェクトモデル・コントローラ実体
  （`PostEffectController` / 各 EffectModel / `GTToneMapController` 等）を削除
- キーフレーム値の保持・補間に使うデータクラスは PostEffects 側の型を直接使い、
  変換層を作らない。ただし SceneEditor 内でその型が漏れてよいのは
  タイムライン層（TransformData / レイヤー / マネージャ）までとし、
  未導入時に触れない経路であることを保証する
- タイムライン XML（`timeline.paraffinCount` 等）との同期規約は現行のまま
  （読込中は timeline 側が正、未読込時はマネージャ自前値）

**v29 プリセットスキーマの改修**

- `ScenePresetEffects` から `paraffins` / `distanceFogs` / `rimlights` と
  3 つの enabled 属性を撤去し、演出カテゴリはテキスト + サブカメラのみとする
- `MteEffectsSnapshot` の CapturePostEffects / ApplyPostEffects を削除
- ポストエフェクトのプリセット保存・復元は既存の PostEffects サイドカープロバイダ
  （`PostEffectsScenePresetProvider`）が引き続き担う
- v29 は未リリースのため後方互換対応は行わない（xUnit テストを新スキーマへ更新）

## エラー処理

- ブリッジの型ロード失敗は一度だけ警告ログを出し、以後 `isAvailable == false` で
  静かに機能を無効化する（毎フレームのログ出力はしない）
- PostEffects 側の要素数 set は `MAX_*_COUNT` で丸め、異常値でも大量生成しない
  （v29 実装の `MteEffectsSnapshot` と同じ流儀）

## テスト・検証

- **ビルド**: 両リポジトリとも COM3D2 / COM3D25 の 2 構成を MSBuild 直叩きで確認
  （`debug.bat` は使わない）。SceneEditor の csproj は両構成とも
  `COM3D25.PostEffects.Plugin.dll` を参照する（アセンブリ名は構成共通と確認済み）
- **xUnit**: v29 スキーマ変更に伴う `ScenePresetEffectsTests` の更新
> 実装済み (2026-08-30、実装計画: `docs/superpowers/plans/2026-08-30-posteffects-timeline-integration.md`)。
> 以下は次回ゲーム起動時の実機確認チェックリスト。

- **実機確認**（次回ゲーム起動時チェックリスト）:
  - 併用時の二重掛かりが解消されている（パラフィン等が 1 実体）
  - タイムライン再生で PostEffects 側の値が動き、タイムラインタブに反映される
  - PostEffects.Plugin の DLL を外した状態で SceneEditor が正常起動し、
    ポストエフェクトレイヤー・UI が無効化される
  - ポストエフェクトレイヤー入りの既存タイムライン XML が未導入環境で
    エラーなく読み込める（該当レイヤーはスキップ）
  - シーンプリセット: 演出カテゴリ（テキスト + サブカメラ）と
    PostEffects サイドカーがそれぞれ独立に保存・復元できる

## リリース・依存関係の注意

- 2 リポジトリにまたがる変更のため、実装計画はリポジトリごとのタスクに分ける
  （PostEffects 側の TimelineBridge → SceneEditor 側の委譲、の順で依存する）
- SceneEditor の新版は PostEffects.Plugin の新版（TimelineBridge 入り）を要求する。
  リリースノートで必要バージョンを明記する
