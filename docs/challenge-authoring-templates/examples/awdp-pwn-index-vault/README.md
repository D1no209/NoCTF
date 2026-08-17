# AWDP PWN 示例题：index-vault

`index-vault` 演示 NoCTF 的完整 AWDP 产品语义：每队拥有一个 `Player` 攻击实例，
平台按 Runtime generation 生成精确 Flag 并注入 `FLAG` 环境变量；Fix 则在独立的
`AwdpTarget` 中只执行和检查一次。

服务是一个存在越界读的 TCP note vault：

- `PING` 返回 `PONG`；
- `READ 0` 返回 `VALUE:training-service-online`；
- 漏洞版本的 `READ 4` 返回运行时注入的动态值；
- 合法 Fix 保留正常业务，并让 `READ 4` 返回 `ERR range`。

镜像不包含比赛 Flag。缺少 `FLAG` 时只使用不可被误判为正确答案的
`NOCTF_RUNTIME_FLAG_UNAVAILABLE`。

## 文件树

```text
awdp-pwn-index-vault/
├─ DEPLOYMENT.md
├─ NOCTF-DELIVERY.md
├─ README.md
├─ checker/{Dockerfile,checker.py}
├─ deploy/docker-compose.attack.yml.example
├─ fixes/{defense-succeeded,exploit-succeeded,service-abnormal-bypass,service-abnormal-down,nonzero,timeout}/fix.sh
├─ scripts/{build-fix-packages.sh,build-invalid-fix-packages.py,package-delivery.sh}
├─ target/{Dockerfile,src/pwn_note.c}
├─ tests/{callback/,invalid-archives.sh,smoke.sh}
└─ tools/exploit.py
```

## 本地验证

需要 POSIX shell、Python 3 和 Docker Engine：

```sh
chmod +x scripts/*.sh scripts/*.py tests/*.sh fixes/*/fix.sh tools/exploit.py
./scripts/build-fix-packages.sh
./tests/invalid-archives.sh
./tests/smoke.sh
./scripts/package-delivery.sh
```

生成的 `artifacts/`、`dist/` 和所有二进制归档均被 `.gitignore` 忽略。

## 六种合法归档结果

| 归档 | 平台结果 |
| --- | --- |
| `defense-succeeded.tar.gz` | `DefenseSucceeded` |
| `exploit-succeeded.tar.gz` | `ExploitSucceeded` |
| `service-abnormal-bypass.tar.gz` | `ServiceAbnormal` |
| `service-abnormal-down.tar.gz` | `ServiceAbnormal` |
| `nonzero.tar.gz` | `AwdpPatchFailed` |
| `timeout.tar.gz` | `AwdpPatchTimeout` |

`build-invalid-fix-packages.py` 还会生成空归档、缺少入口、绝对路径、路径穿越、
符号链接、硬链接、重复路径、非 gzip tar 和超限上传九类拒绝样本。

`deploy/docker-compose.attack.yml.example` 只用于作者离线 smoke。正式比赛的攻击入口
必须由 NoCTF Player Runtime 提供，不能以工作人员额外运行的 compose 靶机替代。
