# AWD、AWDP 出题模板

本目录提供可直接复制到题目仓库的交付模板：

- [AWD 出题模板](awd-challenge-template.md)
- [AWDP 出题模板](awdp-challenge-template.md)
- [可直接打包的 AWD / AWDP Starter Kit](starter-kits/README.md)
- [AWDP PWN 示例题 index-vault](examples/awdp-pwn-index-vault/README.md)

使用方法：

1. 优先复制 `starter-kits/awd` 或 `starter-kits/awdp` 整个目录；目录已经包含交付单、可构建服务、Checker、测试和配置抄录表。
2. 删除不适用的可选项，填写 `NOCTF-DELIVERY.md` 中所有“待填写”位置。
3. 替换 Runtime、Checker 和示例 Fix，执行目录内的 `tests/smoke.sh`。
4. 由题目 Owner 完成作者自测，再由另一名工作人员完成复核。
5. 使用 `starter-kits/pack.ps1` 或 `starter-kits/pack.sh` 生成 tar.gz；解包复验后交付。
6. 不要提交或打包真实比赛 Flag、回调 Token、平台地址或仓库凭据。

模板是交付清单，不代替 [AWD、AWDP 出题规范](../challenge-authoring-awd-awdp.md)。字段含义、限制和判定语义以规范为准。

最小题目资产布局如下；完整目录已经在 Starter Kit 中创建：

```text
challenge/
├─ NOCTF-DELIVERY.md
├─ runtime/
│  ├─ Dockerfile
│  └─ ...
├─ checker/
│  ├─ Dockerfile
│  ├─ check.sh
│  └─ ...
├─ examples/
│  └─ ...
└─ tests/
   └─ ...
```

AWDP 题目可以额外包含只用于验收的补丁样例；不要把平台将直接发给选手的标准答案放入公开附件。
