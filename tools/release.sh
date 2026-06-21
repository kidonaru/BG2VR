#!/usr/bin/env bash
# BG2VR を drop-in zip にして GitHub Releases へ公開する。WSL2 から実行。
set -euo pipefail

# --- パス解決（スクリプトの 1 つ上が repo root） ---
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(cd "$SCRIPT_DIR/.." && pwd)"
CSPROJ="$REPO/BG2VR.csproj"
DIST="$REPO/dist"
FORK_OUT="$REPO/UnityVRMod/Release/OpenXR/UnityVRMod.BepInEx.Mono"
BG2VR_DLL="$REPO/bin/Release/netstandard2.1/BG2VR.dll"
PHONON="$REPO/native/phonon.dll"
DOTNET="${DOTNET:-/mnt/c/Program Files/dotnet/dotnet.exe}"

DRY_RUN=0
STAGE=""

# 致命的エラーで中断する。
die() { echo "[release] ERROR: $*" >&2; exit 1; }

# Windows exe へ渡すため WSL パスを UNC へ変換する。
winpath() { wslpath -w "$1"; }

# csproj の <Version> を 1 件だけ取り出す。
extract_version() {
  local csproj="$1"
  grep -oP '<Version>\K[^<]+' "$csproj" | head -1
}

# 引数を解釈する。--dry-run で publish 手前まで。
parse_args() {
  while [[ $# -gt 0 ]]; do
    case "$1" in
      --dry-run) DRY_RUN=1 ;;
      *) die "不明な引数: $1" ;;
    esac
    shift
  done
}

# --- 以降の関数は後続タスクで追加する ---

# 公開前提を検査する。資産不足は常に致命、git 状態は本番時のみ致命。
preflight() {
  # 資産検査（dry-run でも staging が壊れるので常に致命）
  [[ -f "$REPO/README.md" ]]                || die "README.md がありません（zip 同梱物）"
  [[ -f "$REPO/LICENSE" ]]                  || die "LICENSE がありません（GPL v3 全文を配置してください）"
  [[ -f "$REPO/THIRD_PARTY_NOTICES.txt" ]]  || die "THIRD_PARTY_NOTICES.txt がありません"
  [[ -f "$PHONON" ]]                        || die "native/phonon.dll がありません（git lfs pull）"

  # publish 安全検査（dry-run ではスキップ）
  if [[ "$DRY_RUN" != "1" ]]; then
    [[ "$(git -C "$REPO" branch --show-current)" == "main" ]] \
      || die "main ブランチで実行してください"
    [[ -z "$(git -C "$REPO" status --porcelain)" ]] \
      || die "working tree が dirty です（commit してから実行）"
    [[ -z "$(git -C "$REPO" submodule status --recursive | grep -E '^[+\-U]')" ]] \
      || die "submodule が未コミット/未同期です"
    gh auth status >/dev/null 2>&1 || die "gh が未認証です（gh auth login）"
    ! git -C "$REPO" rev-parse "refs/tags/$TAG" >/dev/null 2>&1 \
      || die "tag $TAG は既に存在します（csproj の Version を上げてください）"
  fi
}

# fork → BG2VR の順に Release ビルドする。
build_all() {
  echo "[release] build fork VR core ..."
  "$DOTNET" build "$(winpath "$REPO/UnityVRMod/src/UnityVRMod.csproj")" \
    -c BIE5_Unity_Mono_OpenXR_Release --nologo -v:minimal \
    || die "fork のビルドに失敗"
  echo "[release] build BG2VR companion ..."
  "$DOTNET" build "$(winpath "$CSPROJ")" \
    -c Release -p:VrModBackend=OpenXR -p:VrModConfig=UnityVRMod.BepInEx.Mono \
    --nologo -v:minimal \
    || die "BG2VR のビルドに失敗"
  [[ -f "$FORK_OUT/UnityVRMod.BepInEx.Mono_OpenXR.dll" ]] || die "fork dll が見つからない: $FORK_OUT"
  [[ -f "$BG2VR_DLL" ]] || die "BG2VR dll が見つからない: $BG2VR_DLL"
}

# drop-in レイアウトを一時ディレクトリに組む。
stage_files() {
  STAGE="$(mktemp -d)"
  trap '[[ -n "$STAGE" ]] && rm -rf "$STAGE"' EXIT
  local plug="$STAGE/BepInEx/plugins/BG2VR"
  local help="$STAGE/BUNNY GARDEN 2_Data/Plugins/x86_64"
  mkdir -p "$plug" "$help"
  cp "$BG2VR_DLL"                                   "$plug/BG2VR.dll"
  cp "$FORK_OUT/UnityVRMod.BepInEx.Mono_OpenXR.dll" "$plug/UnityVRMod.dll"
  cp "$FORK_OUT/UniverseLib.Mono.dll"              "$plug/UniverseLib.Mono.dll"
  cp "$FORK_OUT/openxr_loader.dll"                 "$plug/openxr_loader.dll"
  cp "$PHONON"                                     "$plug/phonon.dll"
  cp "$FORK_OUT/UnityGraphicsHelper.dll"           "$help/UnityGraphicsHelper.dll"
  cp "$REPO/README.md"                             "$STAGE/README.md"
  cp "$REPO/LICENSE"                               "$STAGE/LICENSE"
  cp "$REPO/THIRD_PARTY_NOTICES.txt"               "$STAGE/THIRD_PARTY_NOTICES.txt"
}

# STAGE の中身を zip 直下に固めて生成する（python3 = WSL パスをそのまま扱える）。
make_zip() {
  mkdir -p "$DIST"
  python3 -c "import shutil,sys; shutil.make_archive(sys.argv[1],'zip',sys.argv[2])" \
    "${ZIP%.zip}" "$STAGE" || die "zip 生成に失敗"
  [[ -f "$ZIP" ]] || die "zip が生成されていない: $ZIP"
}

# リリースノートを生成する。前 tag が無ければ初回リリース表記。
gen_notes() {
  local prev
  prev="$(git -C "$REPO" describe --tags --abbrev=0 "$TAG^" 2>/dev/null || true)"
  echo "## 変更点"
  if [[ -n "$prev" ]]; then
    git -C "$REPO" log --oneline "$prev..$TAG"
  else
    echo "- 初回リリース"
  fi
  echo
  echo "## インストール"
  echo "1. BepInEx 5 (Unity Mono) + Doorstop 4.5.0 導入済みの BUNNY GARDEN 2 を用意（FixMod と共存可）"
  echo "2. zip を BUNNY GARDEN 2 フォルダに解凍（BepInEx/ と BUNNY GARDEN 2_Data/ が自動配置）"
  echo "   詳細は同梱 README.md を参照。"
  echo
  echo "GPL v3 / source: https://github.com/kidonaru/BG2VR"
}

# annotated tag をローカルに作る（submodule SHA を固定）。push はしない。
make_tag() {
  git -C "$REPO" tag -a "$TAG" -m "BG2VR $TAG"
}

# 部分失敗時の後始末。サーバリリース→リモートタグ→ローカルタグの順で巻き戻す。
# release はタグを参照するので、タグ削除より先に release を消す。各段は best-effort。
rollback_tag() {
  echo "[release] 公開に失敗 → タグ/リリースを巻き戻します" >&2
  gh release delete "$TAG" --repo kidonaru/BG2VR --yes >/dev/null 2>&1 || true
  git -C "$REPO" push origin ":refs/tags/$TAG"          >/dev/null 2>&1 || true
  git -C "$REPO" tag -d "$TAG"                          >/dev/null 2>&1 || true
}

# GitHub Release を作成し zip を添付する。
# 失敗時は呼び出し側で push 済みタグを巻き戻すため、die せず非ゼロ return する。
create_release() {
  local notes; notes="$(mktemp)"
  gen_notes > "$notes"
  if gh release create "$TAG" "$ZIP" \
      --repo kidonaru/BG2VR \
      --title "BG2VR $TAG" \
      --prerelease \
      --notes-file "$notes"; then
    rm -f "$notes"; return 0
  fi
  rm -f "$notes"; return 1
}

main() {
  parse_args "$@"
  VERSION="$(extract_version "$CSPROJ")"
  [[ -n "$VERSION" ]] || die "csproj から Version を取得できない"
  TAG="v$VERSION"
  ZIP="$DIST/BG2VR-v$VERSION.zip"
  echo "[release] version=$VERSION tag=$TAG dry_run=$DRY_RUN"

  preflight
  build_all
  stage_files
  make_zip
  echo "[release] zip: $ZIP"

  if [[ "$DRY_RUN" == "1" ]]; then
    echo "[release] --dry-run: tag / push / gh release をスキップしました"
    return 0
  fi
  # tag 作成→push→release の各段は、失敗すると以降が孤児化する（ローカルタグ残置／
  # リモートタグ残置／サーバリリース残置）。どの段で落ちても rollback_tag で全て巻き戻し、
  # 同一版の再実行が preflight の「tag 既存」で弾かれないようにする。
  make_tag
  git -C "$REPO" push origin "$TAG" || { rollback_tag; die "tag の push に失敗（巻き戻し済み）"; }
  create_release || { rollback_tag; die "公開に失敗（タグ/リリースは巻き戻し済み。原因解消後に再実行可）"; }
  echo "[release] published $TAG : https://github.com/kidonaru/BG2VR/releases/tag/$TAG"
}

# テスト用に source されたら main を実行しない。
if [[ "${RELEASE_LIB_ONLY:-}" != "1" ]]; then
  main "$@"
fi
