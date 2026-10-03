# AWD Starter Kit

这是一个可运行的 NoCTF AWD 出题骨架，包含：

```text
awd/
├─ NOCTF-DELIVERY.md
├─ README.md
├─ checker/
│  ├─ Dockerfile
│  └─ check.sh
├─ platform/
│  └─ CONFIGURATION.md
├─ runtime/
│  ├─ Dockerfile
│  └─ server.py
└─ tests/
   ├─ callback/
   │  ├─ Dockerfile
   │  └─ server.py
   └─ smoke.sh
```

## 作者操作顺序

1. 先执行 `./tests/smoke.sh`，确认示例镜像、动态 Flag 注入和 Checker 回调闭环可用。
2. 用正式题目服务替换 `runtime/`，但保留“运行期间读取最新 Flag”的能力。
3. 用真实业务探针替换 `checker/check.sh`，禁止只检测端口存活。
4. 按 `platform/CONFIGURATION.md` 在 NoCTF 题库和比赛题目规则中配置。
5. 填完 `NOCTF-DELIVERY.md`，执行至少两队的完整 AWD E2E 后再交付。

本样例的 `/flag` 端点故意直接返回当前 Flag，仅用于证明轮换与攻击链；正式题必须将 Flag 放在预期漏洞之后。

## 本地冒烟测试

需要 Docker Engine 和 POSIX shell：

```sh
chmod +x checker/check.sh tests/smoke.sh
./tests/smoke.sh
```

脚本只清理自己创建且带唯一后缀的容器、网络和本地测试镜像，不执行全局 prune。
