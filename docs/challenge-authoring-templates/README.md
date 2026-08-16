# AWD、AWDP 出题模板

本目录提供可直接复制到题目仓库的交付模板：

- [AWD 出题模板](awd-challenge-template.md)
- [AWDP 出题模板](awdp-challenge-template.md)

使用方法：

1. 复制对应 Markdown 文件到题目资产仓库根目录，建议命名为 `NOCTF-DELIVERY.md`。
2. 删除不适用的可选项，填写所有“待填写”位置。
3. 把 Runtime、Checker、示例补丁和测试资产放在模板约定的目录中。
4. 由题目 Owner 完成作者自测，再由另一名工作人员完成复核。
5. 将最终模板和资产一同提交；不要提交真实比赛 Flag、回调 Token、平台地址或仓库凭据。

模板是交付清单，不代替 [AWD、AWDP 出题规范](../challenge-authoring-awd-awdp.md)。字段含义、限制和判定语义以规范为准。

推荐题目资产布局：

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
