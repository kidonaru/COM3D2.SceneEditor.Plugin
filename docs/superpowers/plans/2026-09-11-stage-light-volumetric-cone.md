# ステージライト 体積円錐化 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** ステージライトの描画をビルボード板ポリから、視線と円錐の交差区間を積分する体積円錐シェーダに置き換える（COM3D2.5 のみ）。

**Architecture:** `MTE/StageLight` シェーダをフラグメント側のレイマーチ（解析的な円錐交差 + 16 サンプル積分 + 深度クリップ）に書き換え、`StageLight.cs` は閉じた円錐メッシュを生成して LookAt をやめる。バンドルは Unity 2022.3.62f2 で再ビルドし、csproj が GameVersion 別に埋め込むバンドルを切り替える。

**Tech Stack:** Unity 2022.3.62f2（バンドルビルド）、Unity ShaderLab/CG、C# (.NET 4.7.1 / UnityInjector)、MSBuild、devbridge MCP（実機検証）

**Spec:** `docs/superpowers/specs/2026-09-11-stage-light-volumetric-cone-design.md`

## Global Constraints

- 対応対象は COM3D2.5 のみ。COM3D2 (2.0) ビルドは従来の 5.6 製 `Timeline/mte_bundle` を埋め込み続け、ステージライトの描画は非対応（警告ログ 1 回）
- データ形式（`TransformDataStageLight`）、XML、UI（`StageLightRowDrawer` / `StageLightItemInspector`）、`TimelineBundleManager` は変更しない
- コードのコメントとログ文言は日本語
- git worktree は使わない。`deploy.bat` は実行しない
- 正本は `source/COM3D2.SceneEditor.Plugin/Timeline/UnityScripts/StageLight.cs`。`UnityProject/Assets/Scripts/StageLight.cs` はエディタ確認用の同期コピー
- バンドルの batchmode ビルド中は Unity エディタで UnityProject を開いていてはならない（プロジェクトロックで失敗する）

---

### Task 1: UnityProject の追跡開始と .gitignore

**Files:**
- Modify: `.gitignore`
- Add: `UnityProject/`（Assets / Packages / ProjectSettings のみ）

**Interfaces:**
- Produces: 追跡された `UnityProject/Assets/Shaders/StageLight.shader`（Task 2 が編集）

- [ ] **Step 1: .gitignore に UnityProject の生成物を追加**

`.gitignore` 末尾に追記:

```gitignore
# UnityProject（mte_bundle ビルド用）。生成物は追跡しない。Bundles/ の正本は source 側のコピー
UnityProject/Library/
UnityProject/Logs/
UnityProject/Temp/
UnityProject/UserSettings/
UnityProject/*.sln
UnityProject/*.csproj
UnityProject/Assets/PostProcessing*
UnityProject/Assets/Bundles/
```

- [ ] **Step 2: 追跡対象を確認**

Run: `git add -n UnityProject | grep -v '^add .UnityProject/Assets/\|^add .UnityProject/Packages/\|^add .UnityProject/ProjectSettings/'`
Expected: 出力なし（Assets / Packages / ProjectSettings 以外が含まれない）

- [ ] **Step 3: Commit**

```bash
git add .gitignore UnityProject
git commit -m "chore: mte_bundle ビルド用 UnityProject を追加"
```

---

### Task 2: build-bundle.bat の作成とシェーダの体積円錐化

**Files:**
- Create: `build-bundle.bat`
- Modify: `.env.sample`
- Modify: `UnityProject/Assets/Shaders/StageLight.shader`（全面書き換え）
- Create: `source/COM3D2.SceneEditor.Plugin/Timeline/mte_bundle_2022`（build-bundle.bat の生成物）

**Interfaces:**
- Consumes: `UnityProject/Assets/Scripts/CreateAssetBundles.BuildAllAssetBundlesBatch`（既存）
- Produces: マテリアルプロパティ `_MainTex` `_Color` `_SubColor` `_ScrollSpeed` `_FalloffExp` `_EdgeSoftness` `_SpotRange` `_OffsetRange` `_TanHalfAngle` `_CoreRadius` `_NoiseStrength` `_NoiseScaleInv` `_Density` `_DepthClip`（Task 4 の `UpdateMaterial` が設定する）。生成物 `Timeline/mte_bundle_2022`（Task 3 が埋め込む）
- 削除: `_SpotAngle` `_ZTest`

- [ ] **Step 0a: build-bundle.bat を作成**

```bat
@echo off
chcp 65001
setlocal

cd /d %~dp0

rem mte_bundle を Unity 2022 でビルドして source 側へコピーする (COM3D2.5 用)
rem 注意: Unity エディタで UnityProject を開いていると batchmode がロックで失敗する

set ENV_FILE=%~dp0.env
if exist "%ENV_FILE%" (
    for /f "usebackq eol=# tokens=1,* delims==" %%a in ("%ENV_FILE%") do set "%%a=%%b"
)
if "%UNITY_2022_EXE%"=="" set "UNITY_2022_EXE=C:\Program Files\Unity\Hub\Editor\2022.3.62f2\Editor\Unity.exe"
if not exist "%UNITY_2022_EXE%" (
    echo Unity が見つかりません: %UNITY_2022_EXE%
    echo .env に UNITY_2022_EXE を設定してください
    exit /b 1
)

set PROJECT_DIR=%~dp0UnityProject
set LOG_FILE=%PROJECT_DIR%\Logs\build-bundle.log
if not exist "%PROJECT_DIR%\Logs" mkdir "%PROJECT_DIR%\Logs"

echo Unity: %UNITY_2022_EXE%
"%UNITY_2022_EXE%" -batchmode -nographics -quit -projectPath "%PROJECT_DIR%" -executeMethod CreateAssetBundles.BuildAllAssetBundlesBatch -logFile "%LOG_FILE%"
if %ERRORLEVEL% neq 0 (
    echo バンドルのビルドに失敗しました。ログ: %LOG_FILE%
    exit /b 1
)

set SRC=%PROJECT_DIR%\Assets\Bundles\mte_bundle
set DST=%~dp0source\COM3D2.SceneEditor.Plugin\Timeline\mte_bundle_2022
if not exist "%SRC%" (
    echo 生成物が見つかりません: %SRC%
    exit /b 1
)
copy /y "%SRC%" "%DST%" >nul
echo コピーしました: %DST%
```

- [ ] **Step 0b: .env.sample に任意項目を追記**

```
# 任意: mte_bundle 再ビルド用 Unity 2022 のパス (build-bundle.bat)
UNITY_2022_EXE=C:\Program Files\Unity\Hub\Editor\2022.3.62f2\Editor\Unity.exe
```

- [ ] **Step 1: シェーダを書き換える**

```shaderlab
Shader "MTE/StageLight"
{
    Properties
    {
        _MainTex ("Noise Texture", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        _SubColor ("Sub Color", Color) = (1,1,1,1)
        _ScrollSpeed ("Scroll Speed", Vector) = (0.2,2.0,0,0)
        _FalloffExp ("Falloff Exponent", Range(0.1, 1)) = 0.5
        _EdgeSoftness ("Edge Softness", Range(0, 1)) = 1
        _SpotRange ("Spot Range", Float) = 10
        _OffsetRange ("Offset Range", Float) = 0.5
        _TanHalfAngle ("Tan Half Angle", Float) = 0.0875
        _CoreRadius ("Core Radius Ratio", Range(0, 1)) = 0.2
        _NoiseStrength ("Noise Strength", Range(0, 1)) = 0.2
        _NoiseScaleInv ("Noise Scale Inverse", Range(0.1, 1)) = 0.2
        _Density ("Density", Float) = 0.1
        _DepthClip ("Depth Clip", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "RenderType"="Transparent"
            "IgnoreProjector"="True"
        }

        // 裏面のみ描画: カメラが円錐内部にあっても描ける。遮蔽は深度テクスチャで行う
        Blend One One
        ZWrite Off
        Cull Front
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            #define SAMPLE_COUNT 16

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float4 screenPos : TEXCOORD1;
            };

            sampler2D _MainTex;
            float4 _ScrollSpeed;
            float4 _Color;
            float4 _SubColor;
            float _FalloffExp;
            float _EdgeSoftness;
            float _SpotRange;
            float _OffsetRange;
            float _TanHalfAngle;
            float _CoreRadius;
            float _NoiseStrength;
            float _NoiseScaleInv;
            float _Density;
            float _DepthClip;
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.screenPos = ComputeScreenPos(o.pos);
                return o;
            }

            // 視線 (o + d*t) と無限円錐 x^2+y^2 = (k z)^2 の内部区間を求める。
            // 区間が無ければ false。二重円錐のもう一方は呼び出し側の z スラブで除外する
            bool IntersectCone(float3 o, float3 d, float k, float tzMin, float tzMax, out float t0, out float t1)
            {
                float k2 = k * k;
                float a = d.x * d.x + d.y * d.y - k2 * d.z * d.z;
                float b = 2.0 * (o.x * d.x + o.y * d.y - k2 * o.z * d.z);
                float c = o.x * o.x + o.y * o.y - k2 * o.z * o.z;

                t0 = tzMin;
                t1 = tzMax;

                if (abs(a) < 1e-6)
                {
                    // 視線が円錐の母線と平行: 一次式 b t + c < 0 が内部
                    if (abs(b) < 1e-6) return c < 0.0;
                    float t = -c / b;
                    if (b > 0.0) t1 = min(t1, t); else t0 = max(t0, t);
                    return t1 > t0;
                }

                float disc = b * b - 4.0 * a * c;
                if (disc < 0.0)
                {
                    // 実根なし: a<0 なら全域が内部、a>0 なら全域が外部
                    return a < 0.0;
                }

                float sq = sqrt(disc);
                float ra = (-b - sq) / (2.0 * a);
                float rb = (-b + sq) / (2.0 * a);
                float rMin = min(ra, rb);
                float rMax = max(ra, rb);

                if (a > 0.0)
                {
                    // 内部は根の間
                    t0 = max(t0, rMin);
                    t1 = min(t1, rMax);
                    return t1 > t0;
                }

                // a<0: 内部は根の外側 (2 区間)。z スラブと重なる方を採る
                float s0 = tzMin;
                float s1 = min(tzMax, rMin);
                if (s1 > s0)
                {
                    t0 = s0;
                    t1 = s1;
                    return true;
                }
                t0 = max(tzMin, rMax);
                t1 = tzMax;
                return t1 > t0;
            }

            float SampleNoise(float3 worldPos)
            {
                float2 uvXY = (worldPos.xy + _Time.x * _ScrollSpeed.xy) * _NoiseScaleInv;
                float2 uvZY = (worldPos.zy + _Time.x * _ScrollSpeed.xy) * _NoiseScaleInv;
                float n = (tex2D(_MainTex, frac(uvXY)).r + tex2D(_MainTex, frac(uvZY)).r) * 0.5;
                return 1.0 + (n - 0.5) * _NoiseStrength;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // ワールド空間の視線。t はワールド距離
                float3 camWorld = _WorldSpaceCameraPos;
                float3 dirWorld = normalize(i.worldPos - camWorld);

                // オブジェクト空間へ変換。方向は正規化しない (t をワールド距離のまま扱うため)
                float3 o = mul(unity_WorldToObject, float4(camWorld, 1.0)).xyz;
                float3 d = mul((float3x3)unity_WorldToObject, dirWorld);

                // z スラブ [_OffsetRange, _SpotRange]
                float tzMin = 0.0;
                float tzMax = 1e10;
                if (abs(d.z) < 1e-6)
                {
                    if (o.z < _OffsetRange || o.z > _SpotRange) discard;
                }
                else
                {
                    float ta = (_OffsetRange - o.z) / d.z;
                    float tb = (_SpotRange - o.z) / d.z;
                    tzMin = max(0.0, min(ta, tb));
                    tzMax = max(ta, tb);
                }

                // 深度テクスチャで手前の不透明物にクリップ
                if (_DepthClip > 0.5)
                {
                    // 視線距離 t とビュー深度は比例する (このフラグメント自身の t と w で換算)
                    float sceneEye = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.screenPos)));
                    float fragT = length(i.worldPos - camWorld);
                    float fragEye = max(i.screenPos.w, 1e-4);
                    tzMax = min(tzMax, sceneEye * fragT / fragEye);
                }
                if (tzMax <= tzMin) discard;

                float t0, t1;
                if (!IntersectCone(o, d, _TanHalfAngle, tzMin, tzMax, t0, t1)) discard;

                // 区間内を等間隔サンプルして減衰とノイズを積分
                float step = (t1 - t0) / SAMPLE_COUNT;
                float sum = 0.0;
                for (int s = 0; s < SAMPLE_COUNT; s++)
                {
                    float t = t0 + step * (s + 0.5);
                    float3 p = o + d * t;
                    float z = max(p.z, 1e-4);

                    float distanceFalloff = pow(1.0 - saturate(z / _SpotRange), _FalloffExp);
                    distanceFalloff = smoothstep(0.0, _EdgeSoftness, distanceFalloff);

                    float normalizedRadius = length(p.xy) / (z * _TanHalfAngle);
                    float rt = saturate((normalizedRadius - _CoreRadius) / (1.0 - _CoreRadius));
                    float angleFalloff = 1.0 - smoothstep(0.0, 1.0, rt);
                    angleFalloff = smoothstep(0.0, _EdgeSoftness, angleFalloff);

                    float3 pw = camWorld + dirWorld * t;
                    sum += distanceFalloff * angleFalloff * SampleNoise(pw);
                }

                float alpha = saturate(sum * step * _Density * _Color.a);
                float4 finalColor = lerp(_SubColor, _Color, alpha);
                finalColor.rgb *= alpha;
                finalColor.a = alpha;
                return finalColor;
            }
            ENDCG
        }
    }

    FallBack Off
}
```

- [ ] **Step 2: バンドルをビルドしてコンパイルエラーが無いことを確認**

Unity エディタで UnityProject を閉じた状態で実行する:

Run: `cmd //c build-bundle.bat 2>&1 | tail -5`
Expected: `コピーしました: ...mte_bundle_2022`
Run: `grep -n -i 'error\|Asset bundles created' UnityProject/Logs/build-bundle.log | head`
Expected: `Asset bundles created at Assets/Bundles` があり、`Shader error` が無い

- [ ] **Step 3: 実機で見た目を確認（REPL、ゲーム再起動なし）**

バンドル名の衝突を避けるため別名にコピーしてから devbridge `eval_csharp` で読む:

```
cp UnityProject/Assets/Bundles/mte_bundle "$SCRATCH/mte_bundle_test"
```

```csharp
var ab = UnityEngine.AssetBundle.LoadFromFile(@"<scratch>\mte_bundle_test");
var mat = new UnityEngine.Material(ab.LoadAsset<UnityEngine.Material>("Assets/Shaders/StageLight.mat"));
// 円錐メッシュ (Task 3 と同じ生成規則)
int n = 16; float k = UnityEngine.Mathf.Tan(10f * 0.5f * UnityEngine.Mathf.Deg2Rad); float z0 = 0.5f, z1 = 8f;
var v = new System.Collections.Generic.List<UnityEngine.Vector3>(); var tri = new System.Collections.Generic.List<int>();
for (int r = 0; r < 2; r++) { float z = r == 0 ? z0 : z1; float rad = z * k; for (int a = 0; a <= n; a++) { float th = a * UnityEngine.Mathf.PI * 2f / n; v.Add(new UnityEngine.Vector3(UnityEngine.Mathf.Cos(th) * rad, UnityEngine.Mathf.Sin(th) * rad, z)); } }
int c0 = v.Count; v.Add(new UnityEngine.Vector3(0, 0, z0)); int c1 = v.Count; v.Add(new UnityEngine.Vector3(0, 0, z1));
for (int a = 0; a < n; a++) { int i0 = a, i1 = a + 1, j0 = n + 1 + a, j1 = n + 1 + a + 1;
  tri.Add(i0); tri.Add(j0); tri.Add(i1); tri.Add(i1); tri.Add(j0); tri.Add(j1);
  tri.Add(c0); tri.Add(i1); tri.Add(i0);
  tri.Add(c1); tri.Add(j0); tri.Add(j1); }
var mesh = new UnityEngine.Mesh(); mesh.SetVertices(v); mesh.SetTriangles(tri, 0); mesh.RecalculateBounds();
var go = new UnityEngine.GameObject("ConeTest"); go.transform.position = new UnityEngine.Vector3(0, 3f, 0); go.transform.eulerAngles = new UnityEngine.Vector3(90f, 0, 0);
go.AddComponent<UnityEngine.MeshFilter>().mesh = mesh; var mr = go.AddComponent<UnityEngine.MeshRenderer>(); mr.material = mat;
mat.SetFloat("_SpotRange", z1); mat.SetFloat("_OffsetRange", z0); mat.SetFloat("_TanHalfAngle", k); mat.SetFloat("_Density", 1f / z1); mat.SetFloat("_DepthClip", 1f);
mat.SetColor("_Color", UnityEngine.Color.white); mat.SetColor("_SubColor", UnityEngine.Color.white);
"ok " + mesh.vertexCount
```

devbridge `screenshot` で確認する観点:
- 円錐が見え、輪郭に明るい縁取りが無い
- 床（y=0）で光が切れる（深度クリップ）
- カメラを真上・円錐内部へ動かしても破綻しない
- `_Density` を変えて既定値の明るさが従来と近くなる係数を決める（決めた係数は Task 3 の `UpdateMaterial` に反映）
- 巻き順が逆で何も見えない場合は `Cull Front` を一時的に `Cull Back` にして切り分け、Task 3 の三角形順を修正する

確認後に片付け: `UnityEngine.Object.Destroy(go); ab.Unload(true);`

- [ ] **Step 4: Commit**

```bash
git add build-bundle.bat .env.sample UnityProject/Assets/Shaders/StageLight.shader source/COM3D2.SceneEditor.Plugin/Timeline/mte_bundle_2022
git commit -m "feat(stagelight): シェーダを視線積分の体積円錐に書き換え"
```

---

### Task 3: csproj のバンドル切替（COM3D2.5 は mte_bundle_2022 を埋め込む）

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj:618-622`

**Interfaces:**
- Consumes: Task 2 の `Timeline/mte_bundle_2022`
- Produces: 埋め込みリソース `mte_bundle`（LogicalName は従来どおり。`TimelineBundleManager` 無変更）

- [ ] **Step 1: csproj の埋め込みを GameVersion で切替**

`COM3D2.SceneEditor.Plugin.csproj` の既存ブロックを置き換え:

```xml
  <!-- mte_bundle: COM3D2 は Unity 5.6 製、COM3D2.5 は Unity 2022 製 (build-bundle.bat で生成)。LogicalName は共通 -->
  <ItemGroup Condition=" '$(GameVersion)' != 'COM3D25' ">
    <EmbeddedResource Include="Timeline\mte_bundle">
      <LogicalName>mte_bundle</LogicalName>
    </EmbeddedResource>
  </ItemGroup>
  <ItemGroup Condition=" '$(GameVersion)' == 'COM3D25' ">
    <EmbeddedResource Include="Timeline\mte_bundle_2022">
      <LogicalName>mte_bundle</LogicalName>
    </EmbeddedResource>
  </ItemGroup>
```

- [ ] **Step 2: 両構成をビルドして埋め込みサイズを確認**

Run: `cmd //c debug.bat all 2>&1 | tail -20`
Expected: エラーなし（ゲーム起動中は実機コピーの失敗警告のみ）
Run: `ls -la source/COM3D2.SceneEditor.Plugin/bin/Debug/COM3D2.SceneEditor.Plugin.dll source/COM3D2.SceneEditor.Plugin/bin/Debug/COM3D25/COM3D2.SceneEditor.Plugin.dll`
Expected: 2 つの DLL サイズが埋め込みバンドルの差分だけ異なる

- [ ] **Step 3: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin/COM3D2.SceneEditor.Plugin.csproj
git commit -m "build: COM3D2.5 構成では Unity 2022 製 mte_bundle を埋め込む"
```

---

### Task 4: StageLight.cs を円錐メッシュ + 新ユニフォームに変更

**Files:**
- Modify: `source/COM3D2.SceneEditor.Plugin/Timeline/UnityScripts/StageLight.cs`（`Initialize` / `UpdateMesh` / `UpdateTransform` / `UpdateMaterial` / `Uniforms`）
- Modify: `UnityProject/Assets/Scripts/StageLight.cs`（同期コピー）

**Interfaces:**
- Consumes: Task 2 のマテリアルプロパティ名（Task 3 で COM3D25 構成に埋め込み済み）
- Produces: なし（公開プロパティは不変）

- [ ] **Step 1: `Initialize` に COM3D2 非対応ガードを追加**

`Initialize()` 先頭のスポットライト型チェック直後を次のように変更:

```csharp
        // COM3D2 (2.0) ビルドは 5.6 製バンドルのままで新シェーダを持たないため描画非対応
        private static bool _unsupportedWarned = false;

        public void Initialize()
        {
            if (spotLight != null && spotLight.type != LightType.Spot)
            {
                Debug.LogError("このコンポーネントはスポットライトにのみ使用できます");
            }

#if !COM3D25
            if (!_unsupportedWarned)
            {
                _unsupportedWarned = true;
                Debug.LogWarning("COM3D2 ではステージライトの描画は未対応です");
            }
            transform.localPosition = _position;
            transform.localEulerAngles = _eulerAngles;
            UpdateName();
            return;
#endif
            // 以下は既存の Mesh 子オブジェクト生成〜 UpdateTransform() まで変更なし
```

- [ ] **Step 1b: `offsetRange` の setter でマテリアル更新も要求する**

`_OffsetRange` が新たにマテリアル依存の値になるため、`offsetRange` の setter を次のように変更する（現状は `_requestedMeshUpdate` のみ）:

```csharp
            set
            {
                if (_offsetRange == value) return;
                _offsetRange = value;
                _requestedMeshUpdate = true;
                _requestedMaterialUpdate = true;
            }
```

- [ ] **Step 2: `UpdateMesh` を閉じた円錐生成に置き換え**

```csharp
        private Vector3[] _vertices = null;
        private int[] _triangles = null;

        // 先端リング (z=offsetRange) と底面リング (z=range) を側面で結び、両端をキャップで閉じた円錐。
        // 法線が外向きになる巻き順にする (シェーダは Cull Front で裏面を描く)
        void UpdateMesh()
        {
            Mesh mesh = _meshFilter.mesh;
            mesh.Clear();

            float tanHalf = Mathf.Tan(spotAngle * 0.5f * Mathf.Deg2Rad);
            float range = CalculateEffectiveRange();
            float zNear = Mathf.Clamp(offsetRange, 0f, range);
            int segments = Mathf.Max(segmentAngle, 3);

            int ringCount = segments + 1;
            int verticesCount = ringCount * 2 + 2;
            if (_vertices == null || _vertices.Length != verticesCount)
            {
                _vertices = new Vector3[verticesCount];
            }

            for (int a = 0; a < ringCount; a++)
            {
                float theta = a * Mathf.PI * 2f / segments;
                float cos = Mathf.Cos(theta);
                float sin = Mathf.Sin(theta);
                _vertices[a] = new Vector3(cos * zNear * tanHalf, sin * zNear * tanHalf, zNear);
                _vertices[ringCount + a] = new Vector3(cos * range * tanHalf, sin * range * tanHalf, range);
            }
            int nearCenter = ringCount * 2;
            int farCenter = nearCenter + 1;
            _vertices[nearCenter] = new Vector3(0f, 0f, zNear);
            _vertices[farCenter] = new Vector3(0f, 0f, range);

            // 側面 2 三角形 + 先端キャップ 1 + 底面キャップ 1 = 12 インデックス / セグメント
            int trianglesCount = segments * 12;
            if (_triangles == null || _triangles.Length != trianglesCount)
            {
                _triangles = new int[trianglesCount];
            }

            int t = 0;
            for (int a = 0; a < segments; a++)
            {
                int n0 = a;
                int n1 = a + 1;
                int f0 = ringCount + a;
                int f1 = ringCount + a + 1;

                _triangles[t++] = n0; _triangles[t++] = f0; _triangles[t++] = n1;
                _triangles[t++] = n1; _triangles[t++] = f0; _triangles[t++] = f1;
                _triangles[t++] = nearCenter; _triangles[t++] = n1; _triangles[t++] = n0;
                _triangles[t++] = farCenter; _triangles[t++] = f0; _triangles[t++] = f1;
            }

            mesh.vertices = _vertices;
            mesh.triangles = _triangles;
            mesh.RecalculateBounds();
        }
```

- [ ] **Step 3: `UpdateTransform` から LookAt を削除**

```csharp
        void UpdateTransform()
        {
            if (spotLight != null)
            {
                if (transform.position != spotLight.transform.position)
                {
                    spotLight.transform.position = transform.position;
                }
                if (transform.rotation != spotLight.transform.rotation)
                {
                    spotLight.transform.rotation = transform.rotation;
                }
            }
        }
```

`GetCurrentCamera()` は他に呼び出しが無くなるので削除する（`#if COM3D2` の `PluginUtils.MainCamera` 参照も含めて）。`using UnityEditor` / `SceneView` 参照が残らないことを確認する。

- [ ] **Step 4: `UpdateMaterial` と `Uniforms` を新プロパティに合わせる**

```csharp
        private void UpdateMaterial()
        {
            if (_meshRenderer != null && _meshRenderer.material != null)
            {
                var material = _meshRenderer.material;
                float range = CalculateEffectiveRange();
                material.SetFloat(Uniforms._SpotRange, range);
                material.SetFloat(Uniforms._OffsetRange, Mathf.Clamp(offsetRange, 0f, range));
                material.SetColor(Uniforms._Color, color);
                material.SetColor(Uniforms._SubColor, color);
                material.SetFloat(Uniforms._FalloffExp, falloffExp);
                material.SetFloat(Uniforms._NoiseStrength, noiseStrength);
                material.SetFloat(Uniforms._NoiseScaleInv, 1f / noiseScale);
                material.SetFloat(Uniforms._CoreRadius, coreRadius);
                material.SetFloat(Uniforms._TanHalfAngle, Mathf.Tan(spotAngle * 0.5f * Mathf.Deg2Rad));
                // 積分は経路長に比例するため距離で正規化する (係数は実機で調整済み)
                material.SetFloat(Uniforms._Density, DensityScale / Mathf.Max(range, 0.01f));
                material.SetFloat(Uniforms._DepthClip, zTest ? 1f : 0f);
            }
        }

        // Task 2 Step 3 で決めた係数に置き換える
        private const float DensityScale = 1f;

        private static class Uniforms
        {
            internal static readonly int _SpotRange = Shader.PropertyToID("_SpotRange");
            internal static readonly int _OffsetRange = Shader.PropertyToID("_OffsetRange");
            internal static readonly int _Color = Shader.PropertyToID("_Color");
            internal static readonly int _SubColor = Shader.PropertyToID("_SubColor");
            internal static readonly int _FalloffExp = Shader.PropertyToID("_FalloffExp");
            internal static readonly int _NoiseStrength = Shader.PropertyToID("_NoiseStrength");
            internal static readonly int _NoiseScaleInv = Shader.PropertyToID("_NoiseScaleInv");
            internal static readonly int _CoreRadius = Shader.PropertyToID("_CoreRadius");
            internal static readonly int _TanHalfAngle = Shader.PropertyToID("_TanHalfAngle");
            internal static readonly int _Density = Shader.PropertyToID("_Density");
            internal static readonly int _DepthClip = Shader.PropertyToID("_DepthClip");
        }
```

`segmentRange` プロパティは XML 互換のため残す。setter の `_requestedMeshUpdate = true` はそのままでよい（無害）。

- [ ] **Step 5: UnityProject 側へ同期**

Run: `cp source/COM3D2.SceneEditor.Plugin/Timeline/UnityScripts/StageLight.cs UnityProject/Assets/Scripts/StageLight.cs`
Run: `diff <(tr -d '\r' < UnityProject/Assets/Scripts/StageLight.cs) <(tr -d '\r' < source/COM3D2.SceneEditor.Plugin/Timeline/UnityScripts/StageLight.cs)`
Expected: 差分なし

- [ ] **Step 6: 両構成をビルド（ゲーム起動中なので実機コピーは失敗警告のみ）**

COM3D2 構成は `#if !COM3D25` ガードのコンパイル確認が目的。

Run: `cmd //c debug.bat all 2>&1 | tail -20`
Expected: COM3D2 / COM3D25 ともに `error CS` なし

- [ ] **Step 7: Commit**

```bash
git add source/COM3D2.SceneEditor.Plugin/Timeline/UnityScripts/StageLight.cs UnityProject/Assets/Scripts/StageLight.cs
git commit -m "feat(stagelight): ビルボードを閉じた円錐メッシュに置き換え"
```

---

### Task 5: ドキュメント更新

**Files:**
- Modify: `docs-site/dev/build.md`（「成果物」の後に節を追加）
- Modify: `CHANGELOG.md`（Unreleased に追記。既存の書式に従う）

- [ ] **Step 1: build.md に節を追加**

```markdown
## アセットバンドル（mte_bundle）

ステージライト等のマテリアルは `UnityProject/`（Unity 2022.3.62f2）から
ビルドしたアセットバンドルを DLL に埋め込んでいます。

| 対象 | 埋め込むファイル | ビルド元 |
|---|---|---|
| COM3D2 (2.0) | `Timeline\mte_bundle` | Unity 5.6（MTE 由来、再ビルド手段なし） |
| COM3D2.5 | `Timeline\mte_bundle_2022` | `build-bundle.bat` |

シェーダやテクスチャを変更したら、Unity エディタで UnityProject を閉じた状態で
`build-bundle.bat` を実行し、生成された `mte_bundle_2022` をコミットします。
Unity のパスは `.env` の `UNITY_2022_EXE` で変更できます。

`UnityProject/Assets/Scripts/` の C# はエディタ上の見た目確認用のコピーで、
正本は `source/COM3D2.SceneEditor.Plugin/Timeline/UnityScripts/` です。

COM3D2 (2.0) 向けバンドルは更新できないため、ステージライトの描画は COM3D2.5 のみ対応です。
```

- [ ] **Step 2: CHANGELOG に追記**

既存の Unreleased セクションの書式に合わせて 1 行:

```markdown
- ステージライトの描画をビルボードから体積円錐に変更（COM3D2.5 のみ。COM3D2 では描画非対応）
```

- [ ] **Step 3: Commit**

```bash
git add docs-site/dev/build.md CHANGELOG.md
git commit -m "docs: mte_bundle のビルド手順とステージライト円錐化を記載"
```

---

### Task 6: 最終確認（ゲーム再起動が必要）

- [ ] **Step 1: ユーザーにゲーム停止 → `debug.bat com3d25` → ゲーム起動を依頼**

- [ ] **Step 2: 既存タイムラインで確認**

`tools/timeline/scenes/kasou-hiro` のシーンをロードし、devbridge `screenshot` で:
- ステージライトが円錐として表示される
- 真上・真下・円錐内部からの視点で破綻しない
- 床・人物との交差がソフトに切れる
- `zTest` OFF のライトが遮蔽されずに描かれる

- [ ] **Step 3: 明るさが従来と大きく違う場合は `DensityScale` を調整して再ビルド・再確認**

---

## レビュー却下メモ

- COM3D2 ビルドで `Initialize` の早期 return により `visible` が false になり `spotLight` 同期が止まる — 誤検知。`spotLight` は SE 側のどこからも代入されない（`grep 'spotLight\s*='` 該当なし）ため同期対象が存在しない
- `Cull Front` + `ZTest Always` で半透明物との前後関係を考慮しない — 旧シェーダも同じ制約なのでリグレッションではない。spec に「既存同様に未対応」と明記して対応
