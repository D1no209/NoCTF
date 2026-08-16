# NoCTF AWD / AWDP 出题 Starter Kit

本目录把出题交付模板具象为两个可以直接复制、测试和打包的目录：

- `awd/`：长期队伍 Runtime、动态 Flag 热注入和服务状态 Checker。
- `awdp/`：一次性漏洞目标、Fix 归档样例和 Fix 结果 Checker。

目录中的服务是最小合约样例，不是正式赛题。出题人应替换业务逻辑、漏洞、Checker 判据和镜像名称，但保留目录边界、环境变量和回调合约。

## 直接打包

Windows PowerShell：

```powershell
./pack.ps1
```

Linux、WSL 或 macOS：

```sh
sh ./pack.sh
```

产物位于 `dist/`：

```text
dist/
├─ noctf-awd-authoring-kit.tar.gz
└─ noctf-awdp-authoring-kit.tar.gz
```

也可以只打包一个目录：

```sh
tar -czf noctf-awd-authoring-kit.tar.gz -C starter-kits awd
tar -czf noctf-awdp-authoring-kit.tar.gz -C starter-kits awdp
```

发给出题人前，组织者应先解包一次并确认顶层目录、文件权限和清单完整。不要把真实比赛 Flag、回调 Token、平台地址、仓库凭据、私有镜像凭据或本地构建缓存放进交付包。
