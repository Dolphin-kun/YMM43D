# YMM43D API リファレンス

YMM43D.dll は、YMM4 のプラグインから 3D 描画を行うための土台です。図形アイテム・映像エフェクトのどちらからでも、Direct3D 11 で描いた結果を YMM4 の動画出力に流し込めます。あわせて、シーン全体を 3D で確認するためのプレビューツールを提供します。

外部ライブラリには依存しません。使用するのは YMM4 本体が同梱している Vortice（Direct3D11 / Direct2D1 / DXGI / D3DCompiler / Mathematics）と SharpGen.Runtime だけです。リフレクションも使用していません。

ソースコードにコメントは置いていません。仕様と、そうしている理由はこの文書にまとめてあります。

## 目次

1. [全体像](#1-全体像)
2. [導入](#2-導入)
3. [座標系と単位](#3-座標系と単位)
4. [3D図形アイテムを作る](#4-3d図形アイテムを作る)
5. [3D映像エフェクトを作る](#5-3d映像エフェクトを作る)
6. [描画のしくみ](#6-描画のしくみ)
7. [型リファレンス](#7-型リファレンス)
8. [実装上の注意](#8-実装上の注意)

---

## 1. 全体像

プラグインから触るのは次の 3 つの名前空間です。

| 名前空間 | 内容 |
|---|---|
| `YMM43D.Plugin` | 継承する基底クラス |
| `YMM43D.Commons` | 3D の語彙。座標・時間・カメラ・光・描画コンテキスト |
| `YMM43D.Graphics` | 描画部品。パイプライン・メッシュ・マテリアル・シェーダー |

`YMM43D.Player` と `YMM43D.PreviewTool` は内部の実装です。プラグインから使うことは想定していません。

### 2つの描画経路

ひとつ 3D 描画を書けば、次の 2 つの経路の両方で使われます。

| 経路 | 呼ばれ方 | 結果 |
|---|---|---|
| 動画出力 | `Shape3DSourceBase.Update()` が YMM4 から呼ばれる | 3D の描画結果を 2D 画像に変換して `Output` に出す |
| 3Dプレビュー | `I3DProvider.Draw()` がプレビューから呼ばれる | プレビューのシーンに直接描かれる |

どちらも同じ `Draw` を通り、`DrawContext3D.World` には**どちらの経路でもアイテムの位置・拡大率・回転を反映した行列**が入ります。全アイテムが同じワールド空間に並ぶので、深度がどの絵の中でも共通になり、アイテムをまたいだ前後関係が成り立ちます。

出力経路では YMM4 が出来上がった画像にも配置を掛けますが、二重にならないよう `Output3DRenderer` が相殺します。プロバイダー側で意識することはありません。

### プロバイダーの見つけ方

3Dプレビューはタイムライン上のアイテムしか辿れませんが、実際に描けるのはアイテムが生成したソースやプロセッサの側です。両者を `Provider3DRegistry` が結び付けます。基底クラスを使っていれば登録は自動で行われるため、通常このクラスを直接触る必要はありません。

YMM4 は同じアイテムを、プレビューなど複数の描画系統で同時に描くことがあります。系統ごとにソースやプロセッサが作られるため、登録も `IGraphicsDevicesAndContext` ごとに分けて持ちます。別のアイテムを描くときは、`Render3DContext.SourceDevices` と同じ系統のものが使われます。

ひとつのアイテムに対して、プレビューは次の順に描画元を探します。

1. アイテム自身が `I3DProvider` を実装しているか
2. 図形アイテムの場合、その `ShapeParameter` に対応するソースが登録されているか
3. いずれも無ければ、有効な映像エフェクトのうち `I3DProvider` を実装しているもの
4. それも無ければ、アイテムの 2D 描画結果を板に貼って表示する

> 1 か 2 が見つかった場合、3 は使われません。エフェクト側の 3D 描画は、アイテムを平面化した絵をもとに立体を組み立てるものなので、既に立体であるアイテムに重ねると本体と平たい写しの二重表示になるためです。

---

## 2. 導入

プラグインのプロジェクトから YMM43D.dll を参照します。YMM4 本体の DLL 群と同じ場所に配置してください。

```xml
<ItemGroup>
  <Reference Include="YMM43D">
    <HintPath>$(YMM4DirPath)user\plugin\YMM43D\YMM43D.dll</HintPath>
  </Reference>
</ItemGroup>
```

ターゲットフレームワークは YMM4 本体に合わせます（`net10.0-windows`）。

同梱の実装が、そのまま例になります。

| 実装 | 表示名 | 種別 | 実装例として |
|---|---|---|---|
| `Extrusion3D` | 立体化3D | 映像エフェクト | 入力画像をテクスチャに使う。レイマーチングと `SV_Depth` |
| `PixelPoints3D` | 点群3D | 映像エフェクト | 頂点シェーダーで形を組み立てる。複数の形状を 1 つのシェーダーで描き分ける |
| `YMM43D.Project.Shape` | 3Dアイテム | 図形 | 面の並びから形を作る。面ごとの塗り分け |
| `YMM43D.Project.Effects.Flat3D` | 3D空間に置く | 映像エフェクト | 入力画像を板として置く。実寸と回転を自分で扱う（`ScalesToInputSize` が false）例 |

---

## 3. 座標系と単位

| 項目 | YMM43D | YMM4 |
|---|---|---|
| Y 軸 | 上が正 | 下が正 |
| Z 軸 | 手前が正（右手系） | 奥行きとして扱われる |
| 回転 | 反時計回り | 時計回り |
| 長さ | ワールド単位 | ピクセル |

ワールドの 1 単位は 100 ピクセルに相当します。アイテムの位置やサイズをワールド単位に直すときは 100 で割ってください。この換算は 3Dプレビューが行うため、プラグイン側で意識するのは自分の形状の大きさを決めるときだけです。

カメラは「3Dカメラ」アイテムとしてタイムラインに置かれます。カメラアイテムが無い区間は、原点を正面から見る既定のカメラになります。

画角は既定では固定値ではなく、**カメラから `DefaultFocalDistance`（10 単位 = 1000px）離れた面が画面とちょうど 1 対 1 で対応するように**決まります。1080px なら 56.7 度です。このため、既定の位置に置いたカメラでは、Z = 0 のアイテムが YMM4 の 2D と同じ位置・大きさに写ります。

| 式 | 意味 |
|---|---|
| `WorldScale.PixelsPerUnit` | 100。ワールド 1 単位 = 100 ピクセル |
| `WorldScale.ToWorld(px)` / `ToPixels(unit)` | 換算 |
| `WorldScale.ToWorldPosition(x, y, z)` / `ToPixelOffset(shift)` | YMM4 の座標（px、Y は下向き）とワールド座標（Y は上向き）の位置・差分を、Y の向きも含めて換算 |
| `SceneProjection.GetPixelsPerTangent(camera, screenHeight)` | 視線からの傾き 1 あたりのピクセル数。自動なら `PixelsPerUnit × DefaultFocalDistance` |
| `SceneProjection.GetTangentProjection()` | 除算後の x・y が傾きそのものになる射影。2D 側の変換を後から掛けるための土台 |

---

## 4. 3D図形アイテムを作る

必要なクラスは 3 つです。

| クラス | 継承元 | 役割 |
|---|---|---|
| プラグイン | `IShapePlugin` | YMM4 に図形の種類を登録する |
| パラメータ | `ShapeParameter3DBase` | 設定値の保持とエディタ表示 |
| ソース | `Shape3DSourceBase` | 3D の描画 |

### プラグイン

```csharp
public class CubePlugin : IShapePlugin
{
    public string Name => "立方体";
    public bool IsExoShapeSupported => false;
    public bool IsExoMaskSupported => false;

    public IShapeParameter CreateShapeParameter(SharedDataStore? store)
        => new CubeParameter(store);
}
```

### パラメータ

`ShapeParameter3DBase` を継承し、`Create3DSource` を実装します。`CreateShapeSource` は基底クラスが実装済みで、生成したソースを自動的に登録します。

`GetAnimatables()` の戻り値には `CameraSyncAnimation` を必ず含めてください。これはシーンカメラの変化を YMM4 に伝えるためのダミーで、含めないとカメラを動かしても標準プレビューが更新されません。

```csharp
internal sealed class CubeParameter : ShapeParameter3DBase
{
    [Display(GroupName = "", Name = "サイズ")]
    [AnimationSlider("F1", "px", 0, 500)]
    public Animation Size { get; } = new(100, 0, 100000);

    [Display(GroupName = "3D回転", Name = "X")]
    [AnimationSlider("F1", "°", -360, 360)]
    public Animation RotationX { get; } = new(0, -100000, 100000);

    public CubeParameter(SharedDataStore? store) : base(store) { }
    public CubeParameter() : this(null) { }

    protected override Shape3DSourceBase Create3DSource(IGraphicsDevicesAndContext devices)
        => new CubeSource(devices, this);

    protected override IEnumerable<IAnimatable> GetAnimatables()
        => [Size, RotationX, CameraSyncAnimation];

    // ExO 出力を行わない場合は空を返す
    public override IEnumerable<string> CreateMaskExoFilter(
        int keyFrameIndex, ExoOutputDescription desc, ShapeMaskExoOutputDescription maskDesc) => [];

    public override IEnumerable<string> CreateShapeItemExoFilter(
        int keyFrameIndex, ExoOutputDescription desc) => [];
}
```

### ソース

実装するのは 2 つのメソッドだけです。

| メンバー | 説明 |
|---|---|
| `Draw(in Render3DContext, DrawContext3D)` | 3D 空間に形状を描きます。プレビューと出力の両方から呼ばれます |
| `GetWorldBounds(in FrameContext)` | 形状がワールド空間で占める範囲（`WorldBounds`）を返します。出力画像の大きさを決めるのに使います。`Draw` の中で回転を掛ける場合は、どの向きに回っても収まる範囲を返してください。大きさが無い範囲を返すと何も描画しません |

描画先の大きさの決定、カメラ行列の解決、コマンドリストの生成は基底クラスが行います。

```csharp
internal sealed class CubeSource : Shape3DSourceBase
{
    private readonly CubeParameter parameter;
    private readonly DeviceResourceCache<RenderPipeline<TransformConstants>> pipelines;

    public CubeSource(IGraphicsDevicesAndContext devices, CubeParameter parameter) : base(devices)
    {
        this.parameter = parameter;

        // パイプラインはデバイスごとに作る。プレビューと出力でデバイスが異なるため。
        pipelines = new DeviceResourceCache<RenderPipeline<TransformConstants>>(
            device => new RenderPipeline<TransformConstants>(
                device,
                BoxMesh.CreateUnitCube(device),
                new VertexColorMaterial(device)));
    }

    public override void Draw(in Render3DContext render, DrawContext3D item)
    {
        var world = GetLocalMatrix(item.Time) * item.World;

        // シーンの光と霧を込みで組み立てる
        var constants = render.CreateConstants(world, item.Opacity);

        var pipeline = pipelines.Get(render.Device);

        // 半透明でも面の前後関係が正しく見えるよう、内側の面を先に描いてから外側を重ねる
        pipeline.Draw(render.Context, constants, item.ToDrawSettings(FaceCulling.Front));
        pipeline.Draw(render.Context, constants, item.ToDrawSettings(FaceCulling.Back));
    }

    private float GetEdgeLength(in FrameContext itemTime)
        => WorldScale.ToWorld(parameter.Size.GetFloat(itemTime));

    // 大きさと回転を掛けた、この図形だけの変換
    private Matrix4x4 GetLocalMatrix(in FrameContext itemTime)
        => Matrix4x4.CreateScale(GetEdgeLength(itemTime))
         * Rotation3D.ForObject(parameter.RotationX.GetFloat(itemTime), 0f, 0f);

    // Draw と同じ変換を範囲にも掛ける。どの向きにも対応できる外接立方体を
    // 返してもよいが、辺が最大 √3 倍になり出力画像が無駄に大きくなる
    protected override WorldBounds GetWorldBounds(in FrameContext itemTime)
        => WorldBounds.FromCube(1f).Transform(GetLocalMatrix(itemTime));

    public override void Dispose()
    {
        pipelines.Dispose();
        base.Dispose();
    }
}
```

> **ワールド行列の掛ける順序**：自分の形状固有の変換（拡大・回転）を左に、`item.World` を右に置きます。`item.World` にはアイテムの位置や拡大率が入っているため、これを先に掛けると形状の回転までアイテムの位置に巻き込まれます。

---

## 5. 3D映像エフェクトを作る

エフェクトは `VideoEffect3DBase` を継承し、プロセッサ側に `I3DProvider` を実装します。生成したプロセッサは `AttachProcessor` に渡してください。3Dプレビューからの描画要求がそのプロセッサへ転送されます。

```csharp
[VideoEffect("立体化3D", ["3D"], [])]
public class ExtrusionEffect : VideoEffect3DBase
{
    public override string Label => "立体化3D";

    [Display(GroupName = "立体化3D", Name = "厚み")]
    [AnimationSlider("F0", "", 0, 100)]
    public Animation Thickness { get; } = new(10, 0, 1000);

    public override IVideoEffectProcessor CreateVideoEffect(IGraphicsDevicesAndContext devices)
        => AttachProcessor(new ExtrusionProcessor(this, devices));

    protected override IEnumerable<IAnimatable> GetAnimatables()
        => [Thickness, CameraSyncAnimation];

    public override IEnumerable<string> CreateExoVideoFilters(
        int keyFrameIndex, ExoOutputDescription desc) => [];
}
```

### プロセッサ

プロセッサは `VideoEffect3DProcessorBase` を継承します。実装するのは 2 つだけです。入力画像のテクスチャ化・描画先の大きさの決定・カメラ行列の解決・アイテム配置の処理は基底クラスが行います。

| メンバー | 役割 |
|---|---|
| `Draw(in Render3DContext, DrawContext3D)` | 3D 空間への描画。プレビューと出力の両方から呼ばれる |
| `GetLocalBounds(in FrameContext)` | 描くものが占める範囲。`Update` が組み立てるワールド行列を掛ける前の座標系で答える |

```csharp
internal sealed class ExtrusionProcessor : VideoEffect3DProcessorBase
{
    private readonly ExtrusionEffect effect;
    private readonly DeviceResourceCache<RenderPipeline<ExtrusionConstants>> pipelines;

    public ExtrusionProcessor(ExtrusionEffect effect, IGraphicsDevicesAndContext devices)
        : base(effect, devices)
    {
        this.effect = effect;
        pipelines = new DeviceResourceCache<RenderPipeline<ExtrusionConstants>>(
            device => new RenderPipeline<ExtrusionConstants>(
                device, BoxMesh.CreateExtrusionBox(device), new ExtrusionMaterial(device)));
    }

    public override void Draw(in Render3DContext render, DrawContext3D item)
    {
        // 入力画像は基底クラスがテクスチャ化してくれる
        var texture = item.Texture ?? GetTexture(render.Device);
        if (texture is null)
            return;

        var thickness = effect.Thickness.GetFloat(item.Time) / 100f;
        var world = Matrix4x4.CreateScale(1f, 1f, thickness) * item.World;
        var constants = /* ... */;

        pipelines.Get(render.Device).Draw(
            render.Context, constants, item.ToDrawSettings(FaceCulling.Front, texture));
    }

    protected override WorldBounds GetLocalBounds(in FrameContext itemTime)
    {
        var thickness = effect.Thickness.GetFloat(itemTime) / 100f;

        // ワールド行列が入力画像の実寸を掛けてくれるので、1×1 の板として答える
        return new WorldBounds(
            new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, thickness));
    }

    public override void Dispose()
    {
        pipelines.Dispose();
        base.Dispose();   // DetachProcessor もここで行われる
    }
}
```

### 入力画像をテクスチャとして使う

3D 描画は YMM4 とは別のデバイスで行うため、入力画像をそのままテクスチャとして使うことはできません。`GetTexture(device)` を呼べば、基底クラスが共有テクスチャを介して変換したものを返します。実寸は `TryGetSize(out size, out offset)` で取れます。焼き込みは `Update` の中で済ませてあるので、`Draw` がプレビューのスレッドから呼ばれても安全です（[実装上の注意](#8-実装上の注意)を参照）。

> **サンプルした色は `Unpremultiply()` に通してください。** Direct2D の画像は色に不透明度をあらかじめ掛けた形（乗算済みアルファ）です。陰影・霧・混ぜ合わせはどれも掛かっていない色を前提にしているので、割り戻さないと不透明度が二度掛かります。登場・退場で薄くしたときに色まで一緒に沈み、消え際が黒ずんで見えます。

入力画像の実寸をワールド行列に取り込ませたくない場合は、`ScalesToInputSize` を `false` にしてください。粒や線のように太さを持つものは、縦横で違う倍率に引き伸ばされると歪みます。この値はプレビュー側からも参照されるので、出力と大きさが食い違うことはありません。

> **`DrawDescription` は基底クラスが空にして返します。** アイテムの位置・拡大率・回転・カメラはすべて 3D のワールド行列に取り込んで描画済みなので、YMM4 に二重に掛けさせないためです。副作用として、**このエフェクトより後ろに置いたエフェクトからは、アイテムの位置や拡大率が既定値に見えます。** それらを参照するエフェクトは前に置いてください。

### 板を変形させるエフェクトを作る

`Deform3DProcessorBase<TConstants>` と `Deform.hlsli` は、「アイテムの絵を貼った板を、頂点シェーダーで動かす」形のエフェクトのための土台です。湾曲3D・波打ち3D・折る3D・砕け散る3D はすべてこれに乗っており、それぞれ別のアセンブリになっています。

エフェクト側が書くのは、hlsl の関数2つだけです。

```hlsl
#include "../../YMM43D/Shaders/Deform.hlsli"

cbuffer MyConstants : register(b1) { float Amount; };

// 平らな板の1点が動いた先を返す
float3 Deform(float3 local, float3 piece) { ... }

// その点をどれだけ残すか（1 でそのまま、0 で消える）
float DeformFade(float3 local, float3 piece) { return 1.0; }
```

`VSMain` と `PSMain` は `Deform.hlsli` にあり、`Deform` を前方宣言だけ見て組み立てています。**法線は自動で出ます。** 少しずらした2点を同じ `Deform` に通し、その差から求めているので、変形の式を書けば陰影も霧もアルファも付いてきます。

`local` は板の上の位置で、x と y は -0.5〜0.5、z は 0 です。**この物差しは板の幅を 1 とします。** px で受け取った値は幅で割ってから渡してください（`Wave3DProcessor.ToPlaneUnits` が例です）。奥行きも同じ物差しで動き、描画時に画像の幅に合わせて拡大されるので、大きい画像でも小さい画像でも「曲げ90度」が同じ見え方になります。

`piece` は破片の中心です。つながった板では `local` と同じ値が入ります。`DeformGrid.Create(x, y, separated: true)` で作ると1マスずつ切り離した板になり、破片ごとに別々へ動かせます。

C# 側は `Deform3DProcessorBase<TConstants>` を継承して4つを用意します。

| 用意するもの | 何を返すか |
|---|---|
| `ShaderName` | どの hlsl を使うか。自分のアセンブリから探され、`Deform.hlsli` は YMM43D から拾われる |
| `GetGrid` | 板を何マスに割るか。折り目や破片の境目に頂点が来るように決める |
| `GetConstants` | b1 へ送る値 |
| `GetExtent` | 変形後にどこまではみ出すか。描画先の大きさと遮蔽の判定に使う |

---

## 6. 描画のしくみ

### RenderPipeline

形状・シェーダー・入力レイアウト・定数バッファをひとまとめにした描画単位です。`Draw` を呼ぶと、定数バッファの更新からステート設定、シェーダーとバッファのバインド、描画、後始末までを一度に行います。

```csharp
var pipeline = new RenderPipeline<TransformConstants>(device, mesh, material);

pipeline.Draw(context, constants, new DrawSettings
{
    Blend = BlendMode.Normal,
    Culling = FaceCulling.Back,
    Texture = shaderResourceView,
});
```

型引数の `TConstants` は HLSL 側の `cbuffer` と同じレイアウトの構造体です。定数バッファの大きさはこの型から決まるため、シェーダー側の宣言と食い違うと描画結果が壊れます。HLSL の 16 バイト境界の詰め方に合わせてください。

### DrawSettings

| メンバー | 説明 |
|---|---|
| `Blend` | 合成方法。`Normal` / `Add` / `Subtract` / `Multiply` / `Screen` |
| `Culling` | `None` / `Back`（前面のみ）/ `Front`（背面のみ） |
| `IgnoreDepth` | 深度テストを無効にして常に手前に描く。YMM4 の「最前面に表示」に対応 |
| `SkipDepthWrite` | 深度テストは行うが書き込まない。半透明な板を描くときに使う |
| `Texture` | ピクセルシェーダーのスロット 0 に設定するテクスチャ |
| `Sampler` | 省略時はリニア補間のサンプラー |

`DrawContext3D.ToDrawSettings()` を使うと、アイテムの合成モードと「最前面に表示」が反映された `DrawSettings` が得られます。カリングとテクスチャだけを足してください。

### メッシュとマテリアル

`IMesh` と `IMaterial` を実装すれば独自の形状・シェーダーを使えます。標準で次のものが用意されています。

| 種類 | 型 | 内容 |
|---|---|---|
| メッシュ | `BoxMesh.CreateUnitCube` | 一辺 1 の立方体（z が -0.5〜0.5、頂点色つき） |
| | `BoxMesh.CreateExtrusionBox` | 押し出し用の箱（z が 0〜1、白） |
| | `PlaneMesh` | 1×1 の板 |
| | `LineMesh` | 線分の並び |
| | `SurfaceMesh` | `SurfaceGeometry` から作る、面の並びのメッシュ |
| マテリアル | `VertexColorMaterial` | 頂点色をそのまま出す |
| | `TextureMaterial` | テクスチャを貼る |

### 面の並びから形を作る

多角形の面を並べた形は `SurfaceGeometry` で表します。頂点の配列と、面ごとの「どの頂点をどの順に結ぶか」を持ちます。`SurfaceMesh` に渡すと三角形に割って頂点バッファになります。

| メンバー | 説明 |
|---|---|
| `Vertices` | 頂点の位置 |
| `Normals` | 頂点ごとの外向き。なめらかに繋ぐ面だけが使う |
| `Faces` | 面の並び。`Indices`・`Group`・`IsSmooth` |
| `GroupCount` | 色を塗り分ける単位の数。球は 1、円柱は 3（側面・上面・底面） |
| `ScaledToUnit()` | いちばん長い差し渡しが 1 になるよう拡大縮小する |
| `FacingOutward()` | 面の並び順を、外を向くほうへ揃え直す |

`IsSmooth` が false の面は、その面自身の向きを法線に使います（角が立つ）。true の面は `Normals` をそのまま使うので、隣り合う面が繋がって見えます。円柱のように側面だけなめらかにして蓋は角を立てたい場合は、面ごとに分けて指定します。

```csharp
// 一辺 1 の四面体。面ごとに色を変えられるよう、面の数だけグループを振る。
var solid = SurfaceGeometry.Faceted(
    [new(1, 1, 1), new(1, -1, -1), new(-1, 1, -1), new(-1, -1, 1)],
    [[0, 1, 2], [0, 3, 1], [0, 2, 3], [1, 3, 2]]).ScaledToUnit();

using var mesh = new SurfaceMesh(device, solid, [new Color4(1f, 1f, 1f, 1f)]);
```

よく使う形は `Primitives` にあります。`Plane` / `Tetrahedron` / `Cube` / `Octahedron` / `Dodecahedron` / `Icosahedron` / `Sphere(分割数)` / `Cylinder(分割数)` / `Cone(分割数)` / `Torus(分割数, 太さ)`。どれも差し渡し 1 に揃えてあります。

> 法線の向きは、絵には出ません。シェーダーが必ずカメラ側へ向け直してから陰影を計算するためです。それでも `Normals` は外向きで揃えてあります。裏返っていると、面の並び順を揃える `FacingOutward()` が逆に働くためです。

### シェーダーの記述

HLSL はプロジェクトの `Shaders` フォルダに置き、埋め込みリソースとして持ちます。`ShaderLibrary.Compile` が読み出し、`#include` を展開してから実行時にコンパイルします。

拡張子は役割で使い分けます。`.hlsl` は入口を持つシェーダー本体、`.hlsli` は `#include` されるだけの部品です（C の `.h` と同じ立場で、単体ではコンパイルしません）。

```xml
<ItemGroup>
  <EmbeddedResource Include="Shaders\**\*.hlsl" />
  <EmbeddedResource Include="Shaders\**\*.hlsli" />
</ItemGroup>
```

```csharp
var assembly = typeof(MyMaterial).Assembly;

VertexShaderBytecode = ShaderLibrary.Compile(assembly, "My.hlsl", "VSMain", "vs_5_0");
var psBytes = ShaderLibrary.Compile(assembly, "My.hlsl", "PSMain", "ps_5_0");
```

同じアセンブリ・ファイル・入口・プロファイルの組は一度だけコンパイルされ、2回目からは覚えている結果が返ります。アイテムを増やしてもコンパイルの時間は増えません。入口を1つのファイルに `VSMain` / `PSMain` で置くなら、自分で呼ばずに `ShaderMaterial` を使うのがいちばん簡単です。頂点とピクセルを別のファイルに分けたときは、ファイル名と入口を個別に渡すコンストラクタを使ってください。

`#include "X.hlsli"` は、まず渡したアセンブリの `Shaders` から、無ければ YMM43D 本体から探します。**別のプラグインからも本体の部品を取り込めます。** 同じ名前は一度しか取り込まないので、取り込み順を気にする必要はありません。同じ名前のファイルを自分側に置けば、本体のものを差し替えられます。

> **他のプロジェクトの hlsli を取り込むときは、書き込む道もファイルの場所に合わせてください。** 探すのはファイル名だけなので、実行時は `#include "Lighting.hlsli"` でも通ります。しかし Visual Studio のエディタはファイルの場所をたどるため、それだと見つけられず、その中の関数が軒並み「宣言されていません」と赤くなります。
>
> ```hlsl
> #include "../../YMM43D/Shaders/Lighting.hlsli"
> ```
>
> 二重に取り込まれても壊れないよう、hlsli には見張り（`#ifndef`）を付けてあります。

YMM43D が持っている部品です。**どれも単体で完結している**ので、エディタで開いても赤くなりません。

| ファイル | 内容 |
|---|---|
| `Standard.hlsli` | 下の全部をまとめたもの。ふつうはこれ 1 つを取り込みます |
| `Scene.hlsli` | 3D の場の `cbuffer`（register **b0**）と、`Opacity` などの別名 |
| `Lighting.hlsli` | `ApplyLight` と `ApplyFog` |
| `Texture.hlsli` | 乗算済みアルファを割り戻す `Unpremultiply` |
| `Light.hlsli` | 光 1 つぶんの `struct Light` と、その並び `Lights`（register **t1**） |

`Standard.hlsli` を取り込むと、場の `cbuffer`、頂点入力 `VS_IN`（`Pos` / `Col` / `Tex` / `Nrm`）、ピクセル入力 `PS_IN`（`Pos` / `Col` / `Tex` / `Nrm` / `World`）、座標変換と法線変換を行う `VSMain`、陰影と霧と不透明度をまとめた `Shade` が揃います。

```hlsl
#include "Standard.hlsli"

float4 PSMain(PS_IN input) : SV_TARGET
{
    return Shade(input.Col, input);
}
```

`Shade` は `AlphaCutoff` より薄い画素を `clip` で捨てます。捨てないと板の四角いままに深度が書かれ、文字のまわりの何も無いところが後ろの物を隠します。しきい値は `DrawContext3D.AlphaCutoff` から渡ります。色を塗るときはごくわずか、深度だけを書くときは輪郭で切れる値になります。

### 自分の値を渡す

**b0 は場のもの、b1 はあなたのもの**です。自前の値は `register(b1)` に置いてください。b0 には触らなくてよく、並び順を合わせる必要もありません（「立体化3D」と「点群3D」がこの形です）。

同じように、**テクスチャの t0 はアイテムの絵、t1 は光の並び、t2 は影の板**です。自前のテクスチャは `t3` から、サンプラーは `s2` から使ってください（s0 は絵、s1 は影の比較用）。t1 と t2 は描画の頭でまとめて割り当てられるので、あなたが渡す必要はありません。

```hlsl
#include "Standard.hlsli"

cbuffer MyConstants : register(b1)
{
    float4 MyColor;
};

float4 PSMain(PS_IN input) : SV_TARGET
{
    return Shade(MyColor, input);
}
```

C# 側は、その `cbuffer` と同じ並びの構造体を作るだけです。

```csharp
[StructLayout(LayoutKind.Sequential)]
internal struct MyConstants
{
    public Vector4 MyColor;
}
```

描くときは、場と自分の値を並べて渡します。パイプラインの型引数は b0 の型なので `TransformConstants` のままです。

```csharp
private readonly DeviceResourceCache<RenderPipeline<TransformConstants>> pipelines = new(
    device => new RenderPipeline<TransformConstants>(device, mesh, material));

public override void Draw(in Render3DContext render, DrawContext3D item)
{
    var scene = render.CreateConstants(world, item, unlit: false);
    var mine = new MyConstants { MyColor = ... };

    pipelines.Get(render.Device).Draw(render.Context, scene, mine, item.ToDrawSettings());
}
```

`CreateConstants(world, item)` は、不透明度と透過のしきい値を `DrawContext3D` から拾います。b1 のバッファはパイプラインが初回に作って使い回すので、こちらで用意するものはありません。b1 が要らないマテリアルは `Draw(context, scene, settings)` と、値を 2 つで呼びます。

> `cbuffer` の大きさは 16 バイトの倍数でなければなりません。`float3` の後ろに `float` を置く、といった詰め方で揃えてください。余りは末尾に `float2 Padding;` のような詰め物を足します。

> **hlsl ファイルは UTF-8 で保存してください。** コンパイラに渡すのは UTF-8 のバイト列です。別の文字コードで保存するとコメントの日本語が化け、場合によっては読み込みで末尾が欠けます。

### デバイスごとの資源

3Dプレビューと動画出力では別々の D3D11 デバイスが使われます。デバイスに紐づく資源（パイプライン、テクスチャ、ステート）はデバイスごとに作る必要があるため、`DeviceResourceCache<T>` を使ってください。

```csharp
private readonly DeviceResourceCache<RenderPipeline<TransformConstants>> pipelines
    = new(device => new RenderPipeline<TransformConstants>(device, mesh, material));

// 描画時
var pipeline = pipelines.Get(render.Device);
```

描画ステート（ブレンド・深度・ラスタライザー・サンプラー）は不変なので、`RenderStates.For(device)` で共有インスタンスを取得できます。`RenderPipeline` は既定でこれを使います。

---

## 7. 型リファレンス

### YMM43D.Plugin

| 型 | 種別 | 説明 |
|---|---|---|
| `Shape3DSourceBase` | abstract class | 3D 図形アイテムの描画元。プレビューと出力の両経路を受け持つ |
| `ShapeParameter3DBase` | abstract class | 3D 図形アイテムの設定値 |
| `VideoEffect3DBase` | abstract class | 3D 描画を行う映像エフェクト |
| `VideoEffect3DProcessorBase` | abstract class | その描画処理側。テクスチャ化・描画先の決定・配置の打ち消しを受け持つ |
| `Output3DRenderer` | class | 3D 描画の結果を YMM4 の出力に流せる画像にする。図形もエフェクトもこれを通る |

### YMM43D.Commons

#### 描画の受け口

| 型 | 種別 | 説明 |
|---|---|---|
| `I3DProvider` | interface | 3D 空間に何かを描画できるオブジェクト。このライブラリの中心となる拡張点 |
| `I3DTextureProvider` | interface | 自前で用意したテクスチャを 3D 描画に提供できる |
| `I3DSizeProvider` | interface | 描画される内容の実寸と原点からのずれ、およびそれをワールド行列に取り込んでよいか（`ScalesToInputSize`）を伝えられる |
| `I3DLocalTransform` | interface | 自分が使っているワールド行列を伝えられる。アイテムをまたいだ前後関係に使う |
| `I3DBounds` | interface | 描くものが占める範囲を伝えられる。3Dプレビューで掴む範囲に使う |
| `I3DVideoEffect` | interface | `I3DProvider` と `I3DTextureProvider` の両方 |
| `Render3DContext` | struct | 描画先のデバイス・コンテキストとカメラ行列 |
| `DrawContext3D` | class | 1 つのアイテムを描画するための情報 |
| `Provider3DRegistry` | static class | パラメータとプロバイダーの対応表 |

#### 座標と時間

| 型 | 種別 | 説明 |
|---|---|---|
| `WorldScale` | static class | ピクセルとワールド単位の換算（1 単位 = 100px） |
| `WorldBounds` | struct | 描くものがワールド空間で占める範囲。出力画像の大きさを決めるのに使う |
| `Rotation3D` | static class | 度からの回転行列の生成と、角度の折り返し |
| `FrameContext` | struct | フレーム位置・長さ・FPS の組。`ForItem` でタイムライン上の位置からアイテムの頭から数えた時刻を作り、`IsAlive` でその位置にアイテムがいるかを調べる |
| `AnimationExtensions` | static class | `Animation` を `FrameContext` で評価する。差分で動かす `Nudge` / `NudgeAt` も持つ |
| `EditScope` | struct | ドラッグの結果をアニメーションのどこに書き込むか（全体か、中間点か）。`NudgePosition` で位置3つをまとめて動かす |
| `SnapGrid` | struct | ドラッグの吸い付き。位置は間隔の倍数、回転は角度の倍数に丸める |
| `SurfaceGloss` | struct | 光の映り込みの強さと鋭さ |

#### カメラと光

| 型 | 種別 | 説明 |
|---|---|---|
| `ISceneCamera` | interface | カメラとして振る舞うアイテム。設定値を返し、ドラッグの差分を時刻・`EditScope` とともに受け取る |
| `CameraState` | struct | ある瞬間のカメラの設定値（位置・向き・視野角） |
| `CameraMove` | struct | カメラをどれだけ動かすかの差分 |
| `CameraPose` | struct | ある時点でのカメラの位置・注視点・上方向 |
| `SceneProjection` | static class | シーンを画面に写す射影。クリップ面と画角の決め方 |
| `ICameraSync` / `CameraSync` | interface / class | カメラの変化を YMM4 に伝える |
| `ISceneLightSource` | interface | 自分で位置を持つ光源アイテム |
| `IPlacedSceneLightSource` | interface | アイテムの置き場所に従って光るもの。3D図形の形状パラメータでも実装できる |
| `ISceneEnvironment` | interface | 環境光と霧を決めるアイテム |
| `SceneLighting` | class | そのシーンの光ひとそろい。光源・環境光・霧 |
| `SceneLight` | struct | 光1灯。平行光なら向き、点光源とスポットなら位置と届く距離 |
| `LightKind` | enum | 光の種類。平行光・点光源・スポットライト |
| `SceneFog` | struct | 霧。色・濃さ・効き始める距離・届く距離 |
| `SceneConstants` | static class | シーンの光を `TransformConstants` に流し込む |
| `ShadowPlacement` | struct | 影の濃さと、その光に割り当てられた板の番号・行列 |
| `SceneShadows` | static class | 光源から場を描き直して影の板を作る |

#### 3Dプレビューで掴む

| 型 | 種別 | 説明 |
|---|---|---|
| `PickRay` | struct | 画面上の1点から伸ばす半直線 |
| `TransformGizmo` | struct | アイテムを動かす向きの案内。矢印と輪の当たり判定 |
| `GizmoHandle` | enum | 案内のどこを掴んでいるか |
| `ISceneMarkerSource` | interface | 3Dプレビューに目印を出すアイテム。掴んで動かされたら差分を受け取る |
| `SceneMarker` | struct | 目印の見た目と場所 |
| `MarkerKind` | enum | 目印の絵柄。平行光・点光源・スポットライト・カメラ |

#### DrawContext3D

| メンバー | 説明 |
|---|---|
| `World` | アイテムの位置・拡大率・回転を反映したワールド行列 |
| `Opacity` | 0.0〜1.0 の不透明度。フェードイン・アウトも反映済み |
| `Blend` | 合成方法 |
| `IsAlwaysOnTop` | YMM4 の「最前面に表示」 |
| `Time` | アイテム内での時間位置 |
| `Texture` | アイテムの 2D 描画結果。`RequiresMappedTexture` が false なら null |
| `ToDrawSettings(culling, texture)` | 合成方法などを反映した `DrawSettings` を作る |

#### Render3DContext

| メンバー | 説明 |
|---|---|
| `Device` / `Context` | 描画に使う D3D11 デバイスとコンテキスト |
| `View` / `Projection` | ビュー行列と射影行列 |
| `ViewProjection` | 両者の積 |
| `SourceDevices` | この描画を行っている YMM4 側の描画系統。ほかのアイテムを描くとき、同じ系統のプロセッサを選ぶのに使う。3Dプレビューでは null |
| `GetWorldViewProjection(in Matrix4x4 world)` | ワールド行列を掛けた最終変換 |
| `GetCameraPosition()` | ワールド空間でのカメラ位置。レイマーチングなどで使う |
| `CreateConstants(world, opacity, unlit)` | シーンの光と霧を込みで `TransformConstants` を作る |
| `BindLights()` | 光の並びを t1 に割り当てる。描画の頭で YMM43D が済ませるので、自分で呼ぶ必要はない |

#### I3DProvider

```csharp
public interface I3DProvider
{
    // 描画にアイテム本来の 2D 画像が必要な場合は true。
    // true のとき DrawContext3D.Texture に描画結果が渡される。
    bool RequiresMappedTexture { get; }

    void Draw(in Render3DContext render, DrawContext3D item);
}
```

#### I3DBounds

3Dプレビューでアイテムを掴む範囲は、これで決まります。実装していないプロバイダーはアイテム本来の 2D の大きさで判定されるため、立体化した部分や 2D より大きな図形は見えているのに掴めません。

基底クラスを使っていれば実装済みです。`Shape3DSourceBase.GetWorldBounds` と `VideoEffect3DProcessorBase.GetLocalBounds` がそのまま使われるので、出力画像の大きさを決めるのに返している範囲がそのまま掴む範囲になります。

#### 陰影と霧

`Vertex` は法線を持ちます。**法線が 0 の頂点は陰影をつけません。** 線や案内表示のような、光を当てたくないものはこれで済みます。

面がどちらを向いていても見た目が壊れないよう、シェーダーは**法線をカメラの側へ向け直してから**光を当てます。両面を描く板を裏から見ても、正しく明るくなります。閉じた立体では表の面がもともとカメラを向いているので、影響しません。

板をカメラに正対させて描くもの（点群3Dの粒や線）は、面の向きがありません。丸い粒と線は**球や円柱の表面とみなして、画素ごとに法線を作ります**。頂点で作ると四隅がどれも真横を向き、補間された中央の法線が打ち消し合って 0 に潰れます。

**丸く扱ってよいのは、輪郭も丸いものだけです。** 四角い粒に球の法線を当てると、四角の中に円が浮き出て見えます。輪郭が板のままのものは、法線も板のまま（カメラの向きの逆）にします。

光はタイムラインから集められます。**光源アイテムを1つも置かなければ既定の光が使われます**（左上手前からの平行光80% + 環境光40%）。この値は、**カメラを正面から向いた面がちょうど明るさ1になる**ように選んであります。2D の絵を「3D空間に置く」だけのときに暗くならないためです。

`Render3DContext.CreateConstants` がシーンの光を込みで `TransformConstants` を組み立てるので、自作のマテリアルでもこれを渡せば陰影と霧が効きます。

**光源の数に上限はありません。** 光は定数バッファではなく `StructuredBuffer`（t1）に置いてあり、いくつ置いても入ります。ただし画素ごとに全灯をなめるので、数を増やすとその分だけ重くなります。

光の種類は3つです。

| 種類 | 作り方 | 効き方 |
|---|---|---|
| 平行光 | `SceneLight.Directional(向き, 色)` / `SceneLight.FromAngles(水平角, 垂直角, 色)` | 太陽のように、場所によらず同じ向きから当たる |
| 点光源 | `SceneLight.Point(位置, 色, 届く距離)` | 置いた場所から全方向へ。距離の2乗で弱まる |
| スポットライト | `SceneLight.Spot(位置, 照らす向き, 色, 届く距離, 広がり, ふちのぼかし)` | 点光源をさらに円錐で絞る |

広がりは円錐の半頂角（度）で、`SceneLight.MinSpread`〜`MaxSpread` に収められます。ふちのぼかしは 0〜1 で、0 なら円の境目がくっきり、1 なら中心から外へなだらかに暗くなります。

#### つや

`SurfaceGloss.FromPercent(つや, 鋭さ)` を定数の組み立てに渡すと、光の映り込み（鏡面反射）が乗ります。どちらも 0〜100 の百分率で、鋭さを上げるほど映り込みが小さく締まります。

```csharp
var constants = render.CreateConstants(world, item, unlit: false, gloss: SurfaceGloss.FromPercent(40f, 60f));
```

`ApplyLight` を通していれば、自作のマテリアルでもそのまま効きます。`SurfaceGloss.None`（既定）なら、これまでどおり映り込みは乗りません。

#### 影

`SceneLight.WithShadow(濃さ, ぼかし)` を付けた光は、**さえぎった物の後ろを暗くします**。濃さは 0〜1 で、1 ならその光がまったく届かなくなります。ぼかしも 0〜1 で、大きいほど影のふちがやわらかくなります（板の上で 1.5〜12 画素ぶんの範囲を混ぜます）。

影は**平行光・スポットライト・点光源のどれでも落とせます**。使う板の枚数は種類で変わり、平行光とスポットは 1 枚、**点光源は 6 面ぶんで 6 枚**です（`ShadowPlacement.SliceCount`）。合計 `ShadowMapArray.MaxSlices` 枚に収まるところまで、光源の並び順に割り当てます。枚数に上限があるのは、1 枚ごとに場をもう一度描くためです。

板の解像度は `SceneLighting.ShadowResolution`（既定 1024、256〜4096）で、`ISceneEnvironment.ShadowResolution` から決まります。標準の「3D環境」アイテムでは「影の細かさ」がこれにあたります。板は必要な枚数ぶんだけ作られ、足りなくなったときと解像度が変わったときに作り直されます。

**影を落とすかどうかは、深度だけを書く番に描くかどうかで決まります。** つまり遮蔽（穴あけ）に参加するものは、そのまま影も落とします。光の筋のように物を隠さないものは、`item.DepthOnly` で早く返せば影からも外れます。

`SceneShadows.Build` が光源から場を描き直し、板の番号と行列を持たせた `SceneLighting` を返します。これは `Renderer3DTo2D` と 3Dプレビューが描画の頭で呼ぶので、**プラグイン側ですることはありません。** `ApplyLight` を通していれば、自作のマテリアルでも影を受け取ります。

板は**同じコマの中でだけ**使い回されます。同じ描き手がもう一度呼んだときは、次のコマに移った（または描き直しを求められた）とみなして描き直すので、一時停止したまま形を変えても影が古いままにはなりません。

#### 絵を描くものが光源も兼ねる

光源アイテムは自分で位置を持ちますが、3D 図形のように **YMM4 側の座標で動かされるもの**は、自分がどこに置かれたのかを知りません。`IPlacedSceneLightSource` を実装すると、アイテムの置き場所を受け取ってから光を返せます。3D 図形アイテムの場合は、**形状パラメータ側**に実装します。

```csharp
public bool IsLightEnabled => true;

public SceneLight GetLight(in FrameContext itemTime, in Matrix4x4 placement)
{
    var aimed = GetOrientation(itemTime) * placement;

    return SceneLight.Spot(
        Vector3.Transform(Vector3.Zero, aimed),
        Vector3.TransformNormal(Vector3.UnitZ, aimed),
        color, reach, spread, blur);
}
```

`placement` はアイテムの拡大率・回転・位置を含んだワールド行列です。**拡大率のぶんだけ、描かれる形も一緒に伸びます。** 届く距離にも同じ倍率を掛けないと、見えている形と当たる光がずれます。倍率は `Vector3.TransformNormal(Vector3.UnitZ, aimed).Length()` で取れます。

`IsLightEnabled` が false のときは `GetLight` を呼ばずに飛ばされるので、光を出さない設定のときに無駄な計算をしません。

#### 3Dプレビューの目印

絵を描かないアイテム（カメラや光源）は、そのままでは 3Dプレビュー上のどこにあるのか分かりません。`ISceneMarkerSource` を実装すると、線画の目印が出て、掴んで動かせるようになります。

```csharp
public SceneMarker GetMarker(in FrameContext itemTime)
    => SceneMarker.ForPointLight(GetPosition(itemTime), WorldScale.ToWorld(Reach.GetFloat(itemTime)));

public void MoveMarker(in Vector3 shift, in FrameContext itemTime, in EditScope scope)
    => scope.NudgePosition(X, Y, Z, shift);
```

`shift` はワールド単位の差分で、視線に垂直な面の上を動いた分です。`scope.NudgePosition` はピクセルへの換算と Y の向きの反転まで済ませて、「ドラッグで中間点を打つ」の設定に従って書き込みます。1つの値だけを動かすときは `scope.Nudge` を使ってください。

| 作り方 | 見た目 | 掴んだとき |
|---|---|---|
| `SceneMarker.ForDirectionalLight(向き)` | 原点から一定の距離に浮かぶ太陽。原点へ線が伸びる | 動かした先の向きに回る |
| `SceneMarker.ForPointLight(位置, 届く距離)` | 小さな球と、届く距離を表す大きな球 | その位置へ移る |
| `SceneMarker.ForSpotLight(位置, 照らす向き, 届く距離, 広がり)` | 光の届く範囲そのままの円錐 | その位置へ移る |
| `SceneMarker.ForCamera(位置)` | 線画は描かない（カメラの枠がそのまま目印になる） | その位置へ移る |

目印を持つアイテムをタイムラインで選ぶと、**目印の場所に軸ハンドルが出ます**。ハンドルを掴んだときの `shift` はその軸方向だけの差分になります。回転の輪は出ません。目印は位置しか持たないためです。

### YMM43D.Graphics

| 型 | 種別 | 説明 |
|---|---|---|
| `RenderPipeline<T>` | class | 形状・シェーダー・定数バッファをまとめた描画単位 |
| `DrawSettings` | struct | 1 回の描画ごとに変わる設定 |
| `RenderStates` | class | デバイス上で共有される描画ステート一式 |
| `IMesh` / `IMaterial` | interface | 形状とシェーダー。頂点が 65536 個を超える形状は `IndexFormat` に `R32_UInt` を返す |
| `Vertex` | struct | 位置・色・UV・法線を持つ標準頂点。法線が 0 の頂点は陰影を付けない |
| `SurfaceGeometry` | record | 面の並びで表した形。頂点・法線・面・面グループ数 |
| `SurfaceFace` | struct | 面 1 枚。頂点の並び・色を塗るグループ番号・なめらかに繋ぐかどうか |
| `Primitives` | static class | 基本の形を作る。平面・正多面体5種・球・円柱・円錐・ドーナツ |
| `SurfaceMesh` | class | `SurfaceGeometry` を GPU の頂点バッファにする |
| `TransformConstants` | struct | 変換行列・不透明度・光・霧を持つ標準定数バッファ |
| `ShaderLibrary` | static class | 埋め込んだ hlsl の読み出しと `#include` の展開 |
| `ShaderCompiler` | static class | HLSL の実行時コンパイル |
| `ShaderMaterial` | class | 自分のアセンブリに埋め込んだ hlsl から作るマテリアル。1つのファイルの `VSMain` / `PSMain` からでも、別々のファイルと入口からでも作れる |
| `DeviceResourceCache<T>` | class | デバイスごとの資源を保持する |
| `GraphicsDevicePool` | static class | 3D 描画用の独立デバイスを貸し出す |
| `D3D11Buffers` | static class | 頂点・インデックス・定数バッファの生成 |
| `BlendMode` / `FaceCulling` | enum | 合成方法とカリング。`Accumulate` は乗算済みアルファのまま足し込む |
| `SceneLightBuffer` | class | 光の並びを t1 に置く。`Render3DContext.BindLights` から使われる |
| `ShadowMapArray` | class | 影の板。t2 に置く。解像度と枚数は場に合わせて作り直す |
| `ModelLibrary` / `ObjModelLoader` / `GltfModelLoader` | static class | `.obj` / `.gltf` / `.glb` の読み込みと、読んだ結果の使い回し |
| `ModelMesh` | class | 読んだモデルを GPU へ載せたもの。材質ごとの部品と画像を持つ |

### FrameContext と Animation

`Animation` の値はフレーム位置に応じて変わります。`FrameContext` を渡して評価してください。

```csharp
float size = parameter.Size.GetFloat(item.Time);   // 拡張メソッド
double raw = parameter.Size.GetValue(item.Time);
```

アイテム内の時間は `FrameContext.FromItem`、タイムライン全体での時間は `FrameContext.FromTimeline` で取得します。カメラはシーン全体に属するため、後者で評価します。

---

## 8. 実装上の注意

### デバイスコンテキストを共有しない

YMM4 の `IGraphicsDevicesAndContext.DeviceContext` は本体の描画スレッドが使っています。描画先（`Target`）や描画中状態（`BeginDraw`〜`EndDraw`）はコンテキストが持つ状態なので、これを横から書き換えると本体側の呼び出しが `D2DERR_WRONG_STATE`（0x88990001）で失敗します。

Direct2D で自分の描画を行う場合は `PrivateD2DContext` を使ってください。同じ Direct2D デバイスから作った別のコンテキストなので、画像やコマンドリストは本体とそのまま受け渡しできます。

コンテキストを分けるだけでは足りません。`ID2D1DeviceContext` はスレッド安全ではなく、3Dプレビューは本体とは別のスレッドで回るため、同じコンテキストを 2 方向から使う場面が生まれます。Direct2D の操作は必ず `lock (D2DGate.Sync)` で囲ってください。囲わないと内部状態が壊れ、運が悪いと vtable 経由の呼び出しがアクセス違反を起こして `ExecutionEngineException` で落ちます。

> 鍵の入れ子の順序は「Direct2D → 3D デバイス」で固定です。`GraphicsDevicePool` のデバイスをロックしたまま `D2DGate.Sync` を取らないでください。

### YMM4 が所有する画像に触れてよい場所

`SetInput` で渡される入力画像は **YMM4 のもの**で、寿命もあちらが決めます。アイテムの設定を続けざまに変えたり、画像ファイルを差し替えたりすると、YMM4 は描画元やエフェクト連鎖を組み直し、その過程で前の画像を破棄します。

**この画像に触れてよいのは `Update` の中だけです。** 3Dプレビューのスレッドから触ると、破棄の瞬間に解放済みの領域へ vtable 経由で飛び、`ExecutionEngineException` でプロセスごと落ちます。`D2DGate` では防げません。あの鍵はこのプラグインの呼び出し同士を並べるだけで、YMM4 側の破棄は止められないからです。

`VideoEffect3DProcessorBase` は `Update` の中で入力画像をテクスチャに焼き込み、`GetTexture(device)` は焼いたあとのものを返すだけにしています。派生クラスが入力画像を直接扱う場合も、同じ切り分けにしてください。

**画像をフレームをまたいで持ち越すなら、タイムラインの並びが変わったときに捨ててください。** アイテムを足したり消したりすると、YMM4 は合成を組み直します。とくにエフェクトアイテムの絵は下のレイヤーの合成そのものなので、組み直しの前後で別物になります。`Timeline.Items` は差し替え式なので、参照が変わったかどうかで気づけます。

### D3D11 コンテキストの状態を戻す

3D 描画用のコンテキストも、プレビューや他のプラグインと共有されます。レンダーターゲット、ビューポート、各種ステートを変更したら必ず元に戻してください。`RenderPipeline.Draw` は自分が設定したステートを描画後に戻します。

### デバイス間で共有するテクスチャ

3D 描画用デバイスと YMM4 のデバイスは、それぞれ別の命令列を GPU に流します。共有テクスチャを `Flush()` だけで受け渡すと、書き込みの途中を読まれて表示が乱れます。`D2DTextureBridge` と `RenderSurface3D` は鍵付きミューテックス（`D3D11_RESOURCE_MISC_SHARED_KEYEDMUTEX`）で待ち合わせています。自前で共有テクスチャを作る場合も同様の対策が必要です。

### カメラ連動

シーンカメラが動いても、YMM4 は自分のパラメータが変わったとは思わないため、標準プレビューを再描画しません。`CameraSyncAnimation` を `GetAnimatables()` に含めておくと、ライブラリ側がこのダミーの値を動かして再描画を促します。

### アイテムをまたいだ前後関係

YMM4 はアイテムごとに平らな画像を作り、レイヤー順に重ねます。深度を渡す口が無いため、そのままではアイテムをまたいだ前後関係を表現できません。

そこで、自分を描く前に他のアイテムの形を**深度バッファにだけ**埋めます。すると自分の画像には「自分が最前面である画素」しか残らず、どの順に重ねても正しい絵になります。1 枚にまとめないので、レイヤーも不透明度も合成モードもアイテムごとに従来どおり効きます。

プロバイダー側で意識することはありません。`DrawContext3D.ToDrawSettings` を使っていれば、色を書かない設定が自動で伝わります。

| 制約 | 内容 |
|---|---|
| 費用 | 3D アイテムの数の 2 乗に比例する |
| カメラ系エフェクト | 他アイテムに掛かっている分は追えない。`I3DLocalTransform` で本人に答えてもらう |
| 厚みのあるもの | YMM4 の拡大は平らな画像への操作なので、奥行き方向の見え方は厳密には一致しない |

### Draw は掴む判定のためにもう一度呼ばれる

3Dプレビューでアイテムを掴むとき、**箱に当たっただけでは選ばれません。** 余白だらけのアイテムが手前にあるだけで、後ろのアイテムを掴めなくなるためです。

そこで、箱に当たったアイテムを手前から順に、**カーソルの1画素ぶんだけ描き直して**、そこに絵があるかを見ます。描かれていなければ次のアイテムへ進みます。

この呼び出しは、**表示のための描画を終えた直後、同じ1枚の中で**行われます。デバイスを別の場所から同時に触らないためです。状態は次のようになっています。

- 描画先は 1×1。ビューポートはカーソルのぶんだけずらしてある
- 深度バッファは無い（深度テストは素通し）
- `DrawContext3D` は不透明度 1・通常合成に差し替えてある（加算や乗算のままだと、掴めるかどうかが合成方法で変わってしまうため）

`Draw` の中で描画以外の副作用を持たせないでください。呼ばれる回数は表示のたびとは限りません。

### 編集欄の表示条件は bool で書く

`[ShowPropertyEditorWhen]` に**列挙型の値を渡してはいけません**。手元で試したかぎり、**値が 0 のメンバー（宣言した順で最初のもの）を選んでいる間は、どの条件も成り立ってしまいます**。隠れてほしい編集欄が出たままになります。

```csharp
// 種類が「点光源」のときだけ出したいのに、「平行光」（値 0）でも出てしまう
[ShowPropertyEditorWhen(nameof(Kind), LightKind.Point)]
public Animation X { get; } = new(0, -1000000, 1000000);
```

条件を bool のプロパティに直し、元の列挙型のセッターから変更を知らせます。

```csharp
[EnumComboBox]
public LightKind Kind
{
    get => kind;
    set
    {
        Set(ref kind, value);
        OnPropertyChanged(nameof(IsPoint));
    }
}
private LightKind kind = LightKind.Directional;

[Browsable(false)]
[EditorBrowsable(EditorBrowsableState.Never)]
public bool IsPoint => Kind == LightKind.Point;

[ShowPropertyEditorWhen(nameof(IsPoint), true)]
public Animation X { get; } = new(0, -1000000, 1000000);
```

`OnPropertyChanged` を忘れると、種類を切り替えても編集欄が開き閉じしません。

### 3Dプレビューに反映されないもの

3D 図形アイテムは本物の立体として描かれるため、平面化されたピクセルを加工するエフェクト（グリッチノイズやモザイクなど）は 3Dプレビューには現れません。動画出力では一度 2D 画像に焼かれてから適用されるため、出力には反映されます。

一方、`DrawDescription` を書き換えるエフェクトは、3D エフェクトより前に置かれていれば 3Dプレビューでも反映されます。`Camera` に変換行列を書き込むもの（回り込みカメラなど）も、`Draw` / `Zoom` / `Rotation` / `Opacity` を書き換えるもの（登場退場の移動・拡大・回転・フェードなど）も同じです。

**アイテムの位置・拡大率・回転・不透明度は、エフェクトを通す前の `DrawDescription` に載っています。** 置き場所はアイテムの値から直接ではなく、**通し終えた `DrawDescription` から** `ItemPlacement.GetWorldMatrix(DrawDescription)` で作ってください。アイテムの値を直接読むと、途中のエフェクトが足した分が抜け落ちます。

**グループ制御は `DrawDescription` には現れません。** レイヤー範囲に入っているかどうかはタイムラインを見ないと分からないためです。`TimelineGroups.GetTransform(timeline, item, frame, fps)` が、そのアイテムに掛かっているグループ制御をまとめた行列を返すので、置き場所の後ろに掛けてください。

```csharp
var world = ItemPlacement.GetWorldMatrix(draw)
          * TimelineGroups.GetTransform(timeline, item, frame, fps);
```

入れ子になったグループは内側から外側の順に重なります。「同一グループのみ」はグループ番号で絞り込まれ、「画像を合成」を入れたグループは YMM4 が中身を1枚の絵にまとめてから動かすので、ここでは掛かりません。

1 コマの間はグループの顔ぶれも置き場所も変わらないので、**`GroupLookup.Build` で一度だけ数えて持ち回ってください。** アイテムごとに `TimelineGroups.GetTransform` を呼ぶと、そのたびにタイムライン全体を数え直します。

**グループ制御に付けたエフェクトが位置を変えるときは、その中のアイテムを 3D から外して板として扱います。** グループ制御のエフェクトは、その下のアイテムそれぞれに配られ、**アイテム自身のエフェクトを通し終えた後に**掛かります。つまり 3D エフェクトより後ろに置いたのと同じ並びで、出来上がった平らな絵が動かされます。そのまま 3D に置くと、絵は動くのに 3D 空間での居場所が動かず、穴あけと影がずれます。

`GroupEffectProbe.MovesPlacement` が、そのグループのエフェクトを 1 コマに 1 回だけ空の絵に通し、`Draw` / `CenterPoint` / `Zoom` / `Rotation` / `Camera` のどれかが変わるかを見ます。変わるときだけ `GroupFlattening.Flattens` が真になり、`SceneDepthCollector` は穴あけと影から外し、`PreviewSceneBuilder` は板に切り替えて**グループのエフェクトも板に通します**。標準プレビューと 3Dプレビューが揃うためです。

ぼかしや色調補正のように位置を変えないエフェクトなら、これまでどおり 3D のままです。グループ自身の位置・拡大・回転（YMM4 が内部で配る `GroupedDrawingEffect`）は、`GroupLookup` が行列として掛けるので、この判定には含めません。

なお `DrawDescription.DrawPoint` と `DrawPointX/Y/Z` は `Draw` を読みやすくしただけの読み取り専用の値で、中身は同じです（後者は非推奨になっています）。

### カメラ系エフェクトとの並び順

`VideoEffect3DProcessorBase` は、自分より**前**に置かれたエフェクトが `DrawDescription.Camera` に書き込んだ変換を、3D の形そのものに掛けます。掛けたぶんは後段に渡しません。そうしないと YMM4 が出来上がった平らな絵に 2D の変形として掛けてしまい、立体が板のまま歪むためです。

見えるのは前に置かれたエフェクトだけです。連鎖の後ろにあるものは、この時点でまだ実行されていません。

3Dプレビューも同じ並びに従います。`ItemRenderPipeline` は 3D プロバイダーより**前**のエフェクトだけを走らせ、そこまでの `DrawDescription.Camera` を取ります。後ろのエフェクトは、貼る絵にもカメラにも入りません。3D 図形アイテムはソース自体が立体なので、エフェクトはすべて後ろ側になり、カメラは入りません。

後ろにエフェクトが並んでいる 3D エフェクトは、**3Dプレビューでも立体として置きません**。`PreviewSceneBuilder` が平面プロバイダーに切り替え、エフェクトを最後まで通した絵を1枚の板として、最終的なカメラで動かします。YMM4 が出力でやることと同じ形になるので、標準プレビューと 3Dプレビューが揃います。

| 並び | 動画出力 | 3Dプレビュー |
|---|---|---|
| 回り込みカメラ → 立体化3D | 立体が 3D で回る | 同じ |
| 立体化3D → 回り込みカメラ | 立体を描いた絵が 2D で傾く | 同じ（板として同じ変形が掛かる） |

**立体化するエフェクトは、カメラ系エフェクトより後ろに置いてください。** 後ろに置いても絵は揃いますが、立体としては扱われなくなります。

なお、後ろのカメラを 3D 側で先取りすることはできません。取り込んでも、そのカメラエフェクトは YMM4 の連鎖でもう一度走り、平らな絵に二重に掛かります。`DrawDescription.SetCustomValue` で後段へ値は渡せますが、YMM4 内蔵のカメラ系エフェクトはそれを読みません。

配置の組み立て方は、動画出力・遮蔽（深度）・3Dプレビューの3経路で揃えてあります。いずれも `ItemPlacement.WithCamera` でカメラを掛けたローカル行列に、`ItemPlacement.GetWorldMatrix` のアイテム配置を掛けます。片方だけ順序が違うと、穴の位置とプレビューがずれます。

### 遮蔽に参加できるアイテム

3D アイテムどうしの前後関係は、相手を深度だけで描いて（`DepthOnly`）穴を開けることで作ります。穴は 3D 空間の居場所に開きます。

3D エフェクトより後ろにエフェクトが並んでいると、出来上がった平らな絵は YMM4 側でさらに動かされます。カメラ系なら回り、グリッチ系ならずれる。**絵は動くのに、開けた穴は元の場所に残ります。** 穴から背景が見えるのはこれです。

`SceneDepthCollector.IsPlacedIn3D` が、有効なエフェクトのうち最後にあるものだけを遮蔽物として認めます。後ろに有効なエフェクトがあるものは、他アイテムに穴を開けず、自分も他アイテムの穴を受け取りません。前後関係は失われますが、位置の合わない穴は出ません。

3D 図形アイテム（`Shape3DSourceBase`）はソースなので、常に遮蔽に参加します。図形アイテムにカメラ系エフェクトを付けた場合は、この判定では防げません。

**光の筋や煙のように、物を隠さないものを描くときは、深度だけを書く番で何も描かないでください。**

```csharp
public override void Draw(in Render3DContext render, DrawContext3D item)
{
    if (item.DepthOnly)
        return;
    ...
}
```

自分を描くときも `DrawSettings.SkipDepthWrite` を立てておくと、同じ絵の中で後から描かれるものを隠しません。深度の比較そのものは効いたままなので、手前にある物にはちゃんと遮られます。

### 後ろに置かれたときの遠近

後ろに置かれたカメラは止められません。YMM4 は出来上がった平らな絵に `DrawDescription.PerspectiveDistance` を距離とする遠近を掛けます。既定の距離は 1000 で、ふつうのアイテムはこの内側に収まります。

3D の結果はそうとは限りません。立体が画面いっぱいに広がると、絵は原点から 1000 より遠くまで届きます。そこへ回転が掛かると奥行きが距離を追い越し、割る数が負に転じて絵が裏返ります。破綻して見えるのはこれです。

`VideoEffect3DProcessorBase` は、後段へ渡す `DrawDescription` の `PerspectiveDistance` を、絵の届く先（`RenderArea.Reach`）の `ScreenPlacement.FlatPerspectiveMargin` 倍まで広げます。遠近は薄まりますが、裏返らなくなり、平らな絵が傾くだけになります。

小さな立体では既定の 1000 のままなので、これまでの見え方は変わりません。上流のカメラがより遠い距離を指定していれば、そちらを残します。

3D 図形アイテム（`Shape3DSourceBase`）にはこの手当てがありません。`IShapeSource2` は `DrawDescription` を返さないため、後ろに置かれたカメラへ渡す値がないためです。
