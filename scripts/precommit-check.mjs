import { spawnSync } from "node:child_process";
import { resolve } from "node:path";
import { pathToFileURL } from "node:url";

const SIZE_LIMIT = 500 * 1024;
const SIZE_EXCEPTIONS = ["vendor/", "docs/images/"];
const PERSONAL_PATH_RE = /[a-z]:[\\/]+Users[\\/]+(?!example\b|username\b|yourname\b|<)[^\\/\s"'<>`]+/i;

function git(root, args) {
  const result = spawnSync("git", args, {
    cwd: root,
    encoding: "utf8",
    windowsHide: true,
    timeout: 30_000,
    maxBuffer: 64 * 1024 * 1024,
    env: { ...process.env, GIT_OPTIONAL_LOCKS: "0" },
  });
  if (result.status !== 0) throw result.error ?? new Error(result.stderr.trim() || `git: ${result.status}`);
  return result.stdout;
}

export function checkStaged(root, report = console.log) {
  const fields = git(root, ["diff", "--cached", "--raw", "--no-abbrev", "--no-renames", "-z"]).split("\0");
  let failures = 0;
  for (let i = 0; i < fields.length - 1; i += 2) {
    const [, mode, , oid, status] = fields[i].split(/\s+/);
    const path = fields[i + 1];
    if (status === "D") continue;
    const fail = (reason) => { failures++; report(`拒否 ${JSON.stringify(path)}: ${reason}`); };
    if (status === "U") { fail("競合が未解決です。"); continue; }
    if (mode === "160000") { fail("サブモジュールは内容を検査できません。"); continue; }
    const bytes = Number(git(root, ["cat-file", "-s", oid]).trim());
    if (bytes > SIZE_LIMIT && !SIZE_EXCEPTIONS.some((prefix) => path.startsWith(prefix))) {
      fail(`500KB（512000バイト）を超えています: ${bytes}バイト。`);
    }
    if (status === "A") {
      if (/\.(log|tmp|orig|bak|rej)$/i.test(path)) {
        fail("ログ・一時ファイル・バックアップは登録できません。");
      }
      if (/^(artifacts|dist|\.claude|\.codex|\.tmp)\//.test(path)) {
        fail("作業用・AI設定フォルダのファイルです。");
      }
    }
    if (path === "scripts/precommit-check.mjs") continue;
    const patch = git(root, [
      "diff", "--cached", "--no-ext-diff", "--no-textconv", "--no-renames",
      "--text", "--unified=0", "--", `:(literal)${path}`,
    ]);
    let inHunk = false;
    const lines = patch.split("\n").filter((line) => {
      if (line.startsWith("@@ ")) inHunk = true;
      return inHunk && line.startsWith("+");
    });
    if (lines.some((line) => PERSONAL_PATH_RE.test(line.replaceAll("\0", "")))) {
      fail("追加行にPCのユーザーホームの絶対パスがあります。相対パスに直してください。");
    }
  }
  return failures ? 1 : 0;
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  try { process.exitCode = checkStaged(process.cwd()); }
  catch (error) { console.error(`検査失敗: ${error.message}`); process.exitCode = 1; }
}
