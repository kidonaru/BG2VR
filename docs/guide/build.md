# ソースからビルド

ゲーム本体（Managed DLL）が必要なため、Bunny Garden 2 をインストール済みの環境でビルドします。

```bash
# submodule（VR コア fork + UniverseLib）込みで取得
git clone --recursive https://github.com/kidonaru/BG2VR.git
cd BG2VR

# VR コア fork → BG2VR の順にビルド（fork を先に）
dotnet build UnityVRMod/src/UnityVRMod.csproj -c BIE5_Unity_Mono_OpenXR_Release
dotnet build BG2VR.csproj -c Release -p:VrModBackend=OpenXR -p:VrModConfig=UnityVRMod.BepInEx.Mono
```

- ゲーム参照 DLL は初回ビルド時にゲームの `*_Data/Managed` から自動 populate されます
  （別パスは `-p:UnityManagedDir=...` で指定）。
- リリース用の drop-in zip 生成は `tools/release.sh`（`--dry-run` で検証可）。

## ライセンス

- 本プラグインは **GPL v3** です。
- VR コアは [UnityVRMod](https://github.com/kidonaru/UnityVRMod) の fork（GPL v3）を同梱しています。
- 同梱サードパーティアセット（コントローラ / ハンド / 各種 3D モデル・Steam Audio）の帰属は
  リポジトリの `THIRD_PARTY_NOTICES.txt` を参照してください。
