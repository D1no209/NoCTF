#!/bin/sh
set -eu

root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
source="$root/attachment"
output="$source/dist"

# 使用明确白名单，不递归打包题目根目录；新增公开文件须先审核再调整本脚本。
for directory in "$source" "$source/patch-template"; do
  if [ ! -d "$directory" ] || [ -L "$directory" ]; then
    printf '公开附件目录缺失或为符号链接：%s\n' "$directory" >&2
    exit 1
  fi
done
for file in README.md public.txt patch-template/README.md patch-template/fix.sh; do
  if [ ! -f "$source/$file" ] || [ -L "$source/$file" ]; then
    printf '公开附件缺失或为符号链接：%s\n' "$file" >&2
    exit 1
  fi
done
if [ -L "$output" ]; then
  printf '%s\n' '公开附件输出目录不能为符号链接。' >&2
  exit 1
fi
mkdir -p "$output"
if [ -n "$(find "$output" -mindepth 1 -maxdepth 1 ! -name README.md ! -name public.txt ! -name patch-template.tar.gz -print)" ]; then
  printf '%s\n' '公开附件输出目录含有未知文件，请人工检查后再构建。' >&2
  exit 1
fi
for file in README.md public.txt patch-template.tar.gz; do
  if [ -L "$output/$file" ] || { [ -e "$output/$file" ] && [ ! -f "$output/$file" ]; }; then
    printf '公开附件输出路径不是普通文件：%s\n' "$file" >&2
    exit 1
  fi
done

# 发布说明随打包产物一起移动后，仍需指向存在的 Patch 归档。
sed 's|(patch-template/README.md)|(patch-template.tar.gz)|g' "$source/README.md" > "$output/README.md"
cp "$source/public.txt" "$output/public.txt"
tar --format=posix --mtime='UTC 2020-01-01' --owner=0 --group=0 --numeric-owner --mode=0644 \
  --pax-option=exthdr.name=%d/PaxHeaders/%f,delete=atime,delete=ctime \
  --no-recursion -czf "$output/patch-template.tar.gz" \
  -C "$source/patch-template" README.md fix.sh

printf '选手附件已生成到 %s，仅包含 README.md、public.txt、patch-template.tar.gz。\n' "$output"
