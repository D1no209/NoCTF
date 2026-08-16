# AWDP Starter Kit

这是一个可运行的 NoCTF AWDP 出题骨架，包含：

```text
awdp/
├─ NOCTF-DELIVERY.md
├─ README.md
├─ checker/
│  ├─ Dockerfile
│  └─ check.sh
├─ examples/fixes/
│  ├─ valid/fix.sh
│  ├─ still-vulnerable/fix.sh
│  ├─ rule-violation/fix.sh
│  ├─ service-unavailable/fix.sh
│  ├─ nonzero/fix.sh
│  └─ timeout/fix.sh
├─ platform/CONFIGURATION.md
├─ scripts/build-fix-packages.sh
├─ target/
│  ├─ Dockerfile
│  └─ server.py
└─ tests/
   ├─ callback/
   │  ├─ Dockerfile
   │  └─ server.py
   └─ smoke.sh
```

## 作者操作顺序

1. 执行 `./scripts/build-fix-packages.sh`，生成只用于作者验收的 Fix 归档。
2. 执行 `./tests/smoke.sh`，确认目标、Checker 和合法 Fix 的最小闭环。
3. 用正式漏洞服务替换 `target/`，保持目标只有一个内部端口且没有公网入口。
4. 用真实漏洞、正常业务与规则校验替换 `checker/check.sh`。
5. 把六类 Fix 样例替换为本题真实资产，并填完 `NOCTF-DELIVERY.md`。
6. 按 `platform/CONFIGURATION.md` 配置题库与比赛规则，再运行完整 AWDP E2E。

`examples/fixes/` 下的文件只能交给工作人员验收，不能作为选手附件公开。生成的 `artifacts/` 不进入 Git，也不会被外层 Starter Kit 打包脚本自动生成。

## 本地冒烟测试

需要 Docker Engine、POSIX shell 和 `tar`：

```sh
chmod +x checker/check.sh scripts/build-fix-packages.sh tests/smoke.sh
./tests/smoke.sh
```

脚本只清理自己创建且带唯一后缀的容器、网络和本地测试镜像，不执行全局 prune。
