# 可复用能力模板

以下 definition 用于题库创建、编辑契约。题库模板不选择比赛模式；比赛部署时另行保存所需规则。Provider 与 RunnerPool 使用平台配置。

## Break、Fix、Check 与多个命名服务

```json
{
  "runtime": {
    "kind": "Container",
    "container": {
      "services": [
        {
          "name": "web",
          "image": "registry.example/noctf/web:1",
          "cpuCores": 0.25,
          "memoryMiB": 128,
          "internalPorts": [8080]
        },
        {
          "name": "database",
          "image": "registry.example/noctf/database:1",
          "cpuCores": 0.25,
          "memoryMiB": 256,
          "internalPorts": [5432]
        }
      ]
    },
    "urlBindings": [
      { "serviceName": "web", "containerPort": 8080, "exposure": "OwnerOnly", "urlTemplate": "http://{HOST}:{PORT}/" }
    ]
  },
  "capabilities": [
    {
      "kind": "Break",
      "break": {
        "environmentTargets": [{ "serviceName": "web", "variableName": "FLAG" }],
        "flagStores": []
      }
    },
    {
      "kind": "Fix",
      "fix": {
        "targetServiceName": "web",
        "entrypoint": "fix.sh",
        "command": ["/bin/sh", "{entrypoint}"],
        "timeoutSeconds": 60,
        "maximumUploadBytes": 268435456
      }
    },
    {
      "kind": "Check",
      "check": {
        "checker": {
          "image": "registry.example/noctf/checker:1",
          "targetServiceName": "web",
          "command": ["/checker/check"],
          "environment": {},
          "timeoutSeconds": 60
        },
        "requirements": ["Connection", "Functionality", "Vulnerability"],
        "readyTimeoutSeconds": 30,
        "acceptsFixInput": false
      }
    }
  ]
}
```

web 与 database 通过服务名称通信。Fix、Check 显式选择 web；其他服务继续属于同一 Runtime 的构造。CTF 部署只提供 Break。AWDP 部署提供 Break 和独立环境中的 Fix→Check；比赛规则分别配置提交次数、Break 前置条件与分数。题库中的 Fix 单项测试只执行修补，FixAndCheck 明确执行两项能力并回收独立环境。

## 独立 Questionnaire

```json
{
  "runtime": null,
  "capabilities": [
    {
      "kind": "Questionnaire",
      "questionnaire": {
        "revisionId": "a6f6edbc-4570-498f-a29f-b4457b9aab60",
        "revisionNumber": 1,
        "questions": [
          {
            "id": "3232a122-3ef4-4336-bd44-5e60befc210a",
            "sourceId": "3232a122-3ef4-4336-bd44-5e60befc210a",
            "kind": "SingleChoice",
            "difficulty": "Basic",
            "title": "参数化查询",
            "body": "参数化查询主要用于防御哪种攻击？",
            "explanation": "参数值与 SQL 语句结构分开处理。",
            "options": [
              { "id": "064c00d3-29c5-4c83-8b4a-8ad3ef455ae0", "content": "SQL 注入", "isCorrect": true, "truthValue": null },
              { "id": "de861a68-9606-4f88-855f-15b7daa41e3e", "content": "流量拥塞", "isCorrect": false, "truthValue": null }
            ],
            "classifications": [{ "kind": "Knowledge", "name": "Web 安全" }],
            "fileIds": []
          }
        ]
      }
    }
  ]
}
```

新建问卷时为版本、小题、来源和选项生成真实 UUID，样例 UUID 在复制创建时应当替换。Questionnaire 测试使用保存后的 questionId、optionId；正式活动发布固定候选版本，发卷固定顺序。更新模板后，已发 Session 继续使用原有版本。Questionnaire 只用于独立考试和练习。

## Checker 回调

读取 NOCTF_CALLBACK_URL 与 NOCTF_CALLBACK_TOKEN，在期限内向 `/api/internal/v1/capabilities/check-results` 提交证据。回调目标和身份来自平台，Checker 不自行选择 GameplayFact、测试执行、队伍或 Runtime。

```json
{
  "probes": [
    { "kind": "Connection", "result": "Passed", "reason": "None" },
    { "kind": "Functionality", "result": "Passed", "reason": "None" },
    { "kind": "Vulnerability", "result": "Passed", "reason": "None" }
  ],
  "flagContexts": [],
  "observedAt": "2026-11-05T12:00:00Z"
}
```

observedAt 使用真实观察时间。漏洞探针 Passed 表示要求的漏洞修复验证通过。功能失败导致依赖探针无法执行时，保留 TargetFailure 与明确的 NotExecuted 原因；证据缺失、无效回调和平台错误必须如实返回。Flag 证据携带服务端分配的 FlagId；私有读取上下文只提供给授权 Checker。
