// BG2VR.csproj の <Version> を semver 規則で繰り上げ、その 1 ファイルだけを commit する。
// tag / push は行わない（tools/release.sh が tag を作る前提。二重 tag を避ける 2 段運用）。
//
// 使い方: node tools/bump-version.mjs <patch|minor|major>
//   patch → x.y.(z+1) / minor → x.(y+1).0 / major → (x+1).0.0

import { readFileSync, writeFileSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const kind = process.argv[2];
if (!['patch', 'minor', 'major'].includes(kind)) {
  console.error('使い方: node tools/bump-version.mjs <patch|minor|major>');
  process.exit(1);
}

// cwd 非依存でリポジトリルートと csproj を解決する（npm script / 直接実行どちらでも同じ）。
const repoRoot = fileURLToPath(new URL('..', import.meta.url));
const csprojPath = fileURLToPath(new URL('../BG2VR.csproj', import.meta.url));

// git をリポジトリルート基準で同期実行する小ヘルパ。
const git = (args, opts = {}) =>
  execFileSync('git', ['-C', repoRoot, ...args], { encoding: 'utf8', ...opts });

// 事前検査: csproj に未 commit の変更があると pathspec commit が bump 無関係の編集を巻き込む。
// staged / unstaged の双方を見て、dirty なら停止する（他ファイルの変更は元から巻き込まれない）。
const csprojDirty = () => {
  try {
    git(['diff', '--quiet', '--', 'BG2VR.csproj']);
    git(['diff', '--cached', '--quiet', '--', 'BG2VR.csproj']);
    return false;
  } catch {
    return true;
  }
};
if (csprojDirty()) {
  console.error('BG2VR.csproj に未 commit の変更があります。先に commit / stash してください。');
  process.exit(1);
}

// 現在の <Version> を取得する（タグ内に空白の無い現状フォーマット前提・先頭 1 件のみ）。
const original = readFileSync(csprojPath, 'utf8');
const m = original.match(/<Version>(\d+)\.(\d+)\.(\d+)<\/Version>/);
if (!m) {
  console.error('BG2VR.csproj から <Version>X.Y.Z</Version> を取得できません。');
  process.exit(1);
}

let [major, minor, patch] = [Number(m[1]), Number(m[2]), Number(m[3])];
if (kind === 'major') { major += 1; minor = 0; patch = 0; }
else if (kind === 'minor') { minor += 1; patch = 0; }
else { patch += 1; }

const oldVersion = `${m[1]}.${m[2]}.${m[3]}`;
const newVersion = `${major}.${minor}.${patch}`;

// 先頭 1 件のみ置換して書き戻す（非グローバル replace）。
writeFileSync(csprojPath, original.replace(m[0], `<Version>${newVersion}</Version>`));

// csproj 1 ファイルだけを pathspec で commit する（他の staged 変更は混ぜない）。
try {
  git(['commit', 'BG2VR.csproj', '-m', `chore: バージョンを v${newVersion} に更新`], {
    stdio: 'inherit',
  });
} catch {
  // commit に失敗したら書き換えを巻き戻し、dirty 残留を防ぐ。
  // 巻き戻し書き込み自体が失敗した場合は手動確認を促す（最終状態を握り潰さない）。
  try {
    writeFileSync(csprojPath, original);
    console.error('commit に失敗したため BG2VR.csproj を元に戻しました。');
  } catch {
    console.error('commit に失敗し、巻き戻しにも失敗しました。BG2VR.csproj を手動で確認してください。');
  }
  process.exit(1);
}

console.log(`バージョンを v${oldVersion} → v${newVersion} に更新し commit しました。`);
