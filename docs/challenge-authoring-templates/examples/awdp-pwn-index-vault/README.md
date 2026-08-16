# AWDP PWN 示例题：index-vault

`index-vault` 是一套基于 NoCTF AWDP Starter Kit 约定制作的完整 PWN 示例题。目录中包含一个存在越界读漏洞的 TCP note 服务、可信 Checker、多种预期结果的 Fix 归档、本地烟测脚本，以及用于测试环境部署的说明文档。

本题演示一个简单的越界读漏洞：

- `READ 0` 是正常业务路径，修复后必须继续可用。
- `READ 4` 在漏洞版本中会读出进程的 `FLAG` 环境变量。
- 合法 Fix 会在保留 `READ 0` 的同时，让 `READ 4` 返回 `ERR range`。

## 目录结构

```text
awdp-pwn-index-vault/
├─ DEPLOYMENT.md
├─ NOCTF-DELIVERY.md
├─ README.md
├─ checker/
│  ├─ Dockerfile
│  └─ checker.py
├─ deploy/
│  └─ docker-compose.attack.yml.example
├─ fixes/
│  ├─ fixed/fix.sh
│  ├─ nonzero/fix.sh
│  ├─ rule-violation/fix.sh
│  ├─ service-unavailable/fix.sh
│  ├─ still-vulnerable/fix.sh
│  └─ timeout/fix.sh
├─ scripts/
│  ├─ build-fix-packages.sh
│  └─ package-delivery.sh
├─ target/
│  ├─ Dockerfile
│  └─ src/pwn_note.c
├─ tests/
│  ├─ callback/
│  └─ smoke.sh
└─ tools/
   └─ exploit.py
```

## 快速开始

需要 POSIX shell 和 Docker Engine：

```sh
chmod +x scripts/*.sh tests/smoke.sh fixes/*/fix.sh tools/exploit.py
./scripts/build-fix-packages.sh
./tests/smoke.sh
./scripts/package-delivery.sh
```

生成的 Fix 归档位于 `artifacts/fixes/`。完整交付包位于 `dist/noctf-awdp-pwn-index-vault.tar.gz`。

## 预期结果

| Fix 归档 | 平台预期结果 |
| --- | --- |
| `fixed.tar.gz` | `Fixed` / 修复成功 |
| `still-vulnerable.tar.gz` | `StillVulnerable` / 漏洞仍存在 |
| `rule-violation.tar.gz` | `RuleViolation` / 正常业务路径被破坏 |
| `service-unavailable.tar.gz` | `ServiceUnavailable` / 目标服务不可用 |
| `nonzero.tar.gz` | Patch 脚本非零退出 |
| `timeout.tar.gz` | Patch 脚本执行超时 |

在 NoCTF 当前 AWDP 语义中，Fix 验证使用的是内部一次性 target 与 checker 镜像。如果还需要用浏览器和选手账号手工验证 Break 流程，可以由工作人员使用 `deploy/docker-compose.attack.yml.example` 在受控主机上单独启动一个可攻击靶机，并在 NoCTF 中配置相同的静态精确 Flag。
