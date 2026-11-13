# 修补包制作

修改 `patch-template/resolver.py`，保留公开文件和嵌套文档的正常读取。`fix.sh` 使用自己的目录定位替换文件。

在 `patch-template` 目录执行：

```sh
tar -czf fix.tar.gz fix.sh resolver.py
```

归档根目录包含这两个文件。使用普通文件，不包含链接、绝对路径或上级目录。补丁以目标镜像的用户运行；请勿依赖安装软件、宿主目录或额外容器权限。
