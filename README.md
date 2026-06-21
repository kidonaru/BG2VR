# BG2VR

**Bunny Garden 2 を VR 化する BepInEx プラグイン**（非公式・自己責任でご利用ください）

OpenXR 対応 HMD で『Bunny Garden 2』を一人称 VR としてプレイできるようにする companion MOD です。
ゲーム本体には手を入れず、BepInEx + Harmony で VR レンダリング・入力・UI 投影を後付けします。

> ⚠️ バージョンは 0.x（開発中）です。互換性は保証されません。

## 主な機能

- **VR レンダリング** — 両眼立体視 + HMD ヘッドトラッキング（OpenXR backend）
- **シームレスな遷移** — シーン遷移・環境切替で視界を保ち、遷移絵柄も VR 空間に表示
- **Comfort 設定** — World Scale / 目の高さ / 酔いカメラ抑制 / フレームペーシング（リプロジェクション由来のちらつき対策）
- **ワールド UI 投影** — ゲーム UI を VR 空間の曲面パネルに投影し、コントローラのレーザーで操作（位置・距離・サイズ・曲面を調整可能、固定位置の保存も）
- **手元モデル** — コントローラ実機モデル / アニメ調ハンドモデルを表示・切替
- **ジェスチャ & ゲームパッド入力** — ゲームパッド準拠のボタン割当、カラオケの振り入力（タンバリン / サイリウム）、カルタのスティック操作など
- **空間音声** — Steam Audio (HRTF) によるキャストボイスの 3D 化
- **視線追従** — 顔 / 虹彩の HMD 追従（Head / Eye Look-At）
- **ミニゲーム対応** — チェキ・手押し相撲・あ〜ん・カラオケ等の VR 操作対応

## 動作要件

- **Bunny Garden 2**（Steam）
- **BepInEx 5**（Unity Mono build。BepInEx 5 上の FixMod と共存可）
- **Doorstop 4.5.0**（D3D12 に必須。4.3.0 等の古い版はクラッシュします）
- **OpenXR 対応 VR HMD**（Meta Quest + Link / Air Link / Virtual Desktop など）

## インストール

BG2VR は **BepInEx 5（Unity Mono）+ Doorstop 4.5.0** の上で動く drop-in プラグインです。
配布 zip には BepInEx 本体も Doorstop（`winhttp.dll`）も含まれないため、まず loader を用意してから BG2VR を重ねます。
以下の手順はすべて **`BUNNY GARDEN 2.exe` があるインストールフォルダ**（例: `...\steamapps\common\BUNNY GARDEN 2\`）で作業します。

> ✅ **BepInEx 5 上の FixMod と共存できます。** 本プラグインは BepInEx 5（Unity Mono）で動作するため、同じ BepInEx 5 で動く FixMod と同居できます。
> FixMod は既定が BepInEx 5 なので、その環境にそのまま追加できます。
> （FixMod を BepInEx 6 ビルドで使っている場合は、両方を共有する 1 つの BepInEx に載せる必要があるため、FixMod 側も BepInEx 5 ビルドに揃えてください。）

### 1. BepInEx 5 を用意する

- **BepInEx 5 を導入済みの場合**（FixMod の既定ビルド等） … そのまま使えます（手順 2 へ）。同じ BepInEx 5 上の FixMod と同居するので退避は不要です。
- **未導入の場合** … [BepInEx 5.4.23.5](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5) の win-x64 版
  （`BepInEx_win_x64_5.4.23.5.zip`）をダウンロードし、ゲームを終了した状態でインストールフォルダに解凍する。
  この版は Doorstop 4.5.0 を同梱するので手順 2 は確認だけで済みます。
  `BepInEx\`・`winhttp.dll`・`doorstop_config.ini` が `BUNNY GARDEN 2.exe` と同じ階層に並ぶのが正しい配置。

### 2. Doorstop が 4.5.0 以上か確認する

D3D12 起動には **Doorstop 4.5.0 以上**が必要です（4.3.0 等の古い版は手順 4 の D3D12 でクラッシュします）。
**BepInEx 5.4.23.5 は Doorstop 4.5.0 を同梱**しているので、手順 1 で最新を入れた場合はそのままで OK です。

- ゲームフォルダの `.doorstop_version` を開き、**4.5.0 以上**なら確認完了（手順 3 へ）。
- 古い場合（以前に入れた BepInEx が古い等） … [最新の BepInEx 5.4.23.5](https://github.com/BepInEx/BepInEx/releases) に更新する（Doorstop 4.5.0 が同梱されます）。

### 3. BG2VR を導入する

1. [BG2VR Releases](https://github.com/kidonaru/BG2VR/releases) から最新の `BG2VR-vX.Y.Z.zip` をダウンロードする。
2. ゲームを終了した状態で、zip をインストールフォルダに解凍する（中身が自動で 2 箇所に配置される）。
   - `BepInEx\plugins\BG2VR\` … VR 本体 DLL
   - `BUNNY GARDEN 2_Data\Plugins\x86_64\UnityGraphicsHelper.dll` … D3D12 ネイティブ

### 4. D3D12 起動オプションを設定する（必須）

Steam ライブラリで **BUNNY GARDEN 2 → プロパティ → 起動オプション** に **`-force-d3d12`** を追加する。
出荷構成は D3D12 で、NVIDIA ドライバ由来のフリーズを構造的に回避します（Doorstop 4.5.0 と組で必須）。

### 5. 起動する

VR HMD を接続してゲームを起動する。設定はゲーム内 **F10** パネル、または `BepInEx\config\BG2VR.cfg` から変更できます。

## ソースからビルド

ゲーム本体（Managed DLL）が必要なため、Bunny Garden 2 をインストール済みの環境でビルドします。

```bash
# submodule（VR コア fork + UniverseLib）込みで取得
git clone --recursive https://github.com/kidonaru/BG2VR.git
cd BG2VR

# VR コア fork → BG2VR の順にビルド（fork を先に）
dotnet build UnityVRMod/src/UnityVRMod.csproj -c BIE5_Unity_Mono_OpenXR_Release
dotnet build BG2VR.csproj -c Release -p:VrModBackend=OpenXR -p:VrModConfig=UnityVRMod.BepInEx.Mono
```

- ゲーム参照 DLL は初回ビルド時にゲームの `*_Data/Managed` から自動 populate されます（別パスは `-p:UnityManagedDir=...` で指定）。
- リリース用の drop-in zip 生成は `tools/release.sh`（`--dry-run` で検証可）。

## ライセンス

- 本プラグインは **GPL v3** です（[LICENSE](LICENSE) 参照）。
- VR コアは [UnityVRMod](https://github.com/kidonaru/UnityVRMod) の fork（GPL v3）を同梱しています。
- 同梱サードパーティアセット（コントローラ / ハンド / 各種 3D モデル・Steam Audio）の帰属は [THIRD_PARTY_NOTICES.txt](THIRD_PARTY_NOTICES.txt) を参照。
