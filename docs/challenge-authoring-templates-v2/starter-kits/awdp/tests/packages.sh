#!/bin/sh
set -eu

root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
scratch=$(mktemp -d "${TMPDIR:-/tmp}/noctf-v2-packages.XXXXXXXX")
trap 'rm -rf "$scratch"' EXIT
mkdir -p "$scratch/scripts" "$scratch/private/examples" "$scratch/attachment"
cp "$root/scripts/build-fix-packages.sh" "$root/scripts/build-attachments.sh" "$scratch/scripts/"
cp "$root/attachment/README.md" "$root/attachment/public.txt" "$scratch/attachment/"
cp -R "$root/attachment/patch-template" "$scratch/attachment/patch-template"
cp -R "$root/private/examples/fixes" "$scratch/private/examples/fixes"

sh "$scratch/scripts/build-fix-packages.sh"
sh "$scratch/scripts/build-attachments.sh"
package="$scratch/attachment/dist/patch-template.tar.gz"
test "$(tar -tzf "$package")" = "$(printf 'README.md\nfix.sh')"
test "$(find "$scratch/attachment/dist" -maxdepth 1 -type f | wc -l | tr -d ' ')" = 3
test "$(find "$scratch/private/artifacts/fixes" -maxdepth 1 -name '*.tar.gz' | wc -l | tr -d ' ')" = 3
grep -q '(patch-template.tar.gz)' "$scratch/attachment/dist/README.md"

# 验证内部样例和公开模板都以 fix.sh 为归档入口，不携带 private 目录。
for archive in "$scratch"/private/artifacts/fixes/*.tar.gz; do
  tar -tzf "$archive" | grep -qx fix.sh
done
tar -xOf "$package" fix.sh | grep -qx 'exit 1'
if tar -xOf "$package" fix.sh | grep -q 'policy.txt'; then
  printf '%s\n' '公开模板意外包含内部修补实现。' >&2
  exit 1
fi

# 固定时间、权限和归档头，重复构建内容必须一致。
before=$(sha256sum "$package" "$scratch"/private/artifacts/fixes/*.tar.gz)
sh "$scratch/scripts/build-fix-packages.sh"
sh "$scratch/scripts/build-attachments.sh"
after=$(sha256sum "$package" "$scratch"/private/artifacts/fixes/*.tar.gz)
test "$before" = "$after"

# 符号链接不能把私有文件伪装成白名单附件。
mv "$scratch/attachment/public.txt" "$scratch/attachment/public.saved"
ln -s "$scratch/private/examples/fixes/defense-succeeded/fix.sh" "$scratch/attachment/public.txt"
if sh "$scratch/scripts/build-attachments.sh" >/dev/null 2>&1; then
  printf '%s\n' '公开附件构建错误地接受了符号链接。' >&2
  exit 1
fi
rm "$scratch/attachment/public.txt"
mv "$scratch/attachment/public.saved" "$scratch/attachment/public.txt"

# 已有未知输出不能被混入发布包，也不能被静默删除。
printf '%s\n' '仅用于测试的私有哨兵' > "$scratch/attachment/dist/unreviewed.txt"
if sh "$scratch/scripts/build-attachments.sh" >/dev/null 2>&1; then
  printf '%s\n' '公开附件构建错误地接受了未经审核的输出。' >&2
  exit 1
fi
test -f "$scratch/attachment/dist/unreviewed.txt"
printf '%s\n' 'V2 附件目录、私有隔离、模板入口和可重复构建检查通过。'
