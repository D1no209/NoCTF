# 人机验证 Provider 与两项策略

入口：平台设置 → 邮件与人机验证的验证分区。Provider 支持 None、Cap、Turnstile，公开参数与 secret 分别保存。

## 三层设置

| 设置 | 控制范围 |
| --- | --- |
| 全局启用 + 有效 Provider | 开启基础验证要求 |
| Runtime Enabled | 玩家启动、重置、停止和续期验证 |
| Evaluation Enabled | 玩家 Flag 提交/只读 Break 判定验证 |

Runtime 与 Evaluation 彼此独立。关闭 Runtime 不自动关闭 Evaluation；关闭 Evaluation 不代表注册/登录不需要全局验证。管理员操作和内部 Checker 回调按自己的授权边界处理。

客户端分别依据 runtimeRequired/evaluationRequired capability 展示验证，服务器仍最终检查。不能靠隐藏弹窗绕过策略。

## Cap 配置

1. 部署独立 Cap，服务加入 `noctf-proxy`，Valkey 留在内部网。
2. 创建站点，配置精确浏览器 Origin，确认 HTTPS/WASM 可访问。
3. 在平台表单填浏览器 Cap Server URL、Site Key。
4. 单独替换该 Provider 的 verification secret。
5. API 的内部 Backend URL 与 dedicated management API key 由部署配置提供，不用 ADMIN_KEY。
6. 保存、启用前检查真实 siteverify，再测试登录和所需玩家操作。

Cap 工作量区可读取/刷新 difficulty 和 challengeCount，再保存新值。当前范围分别 1–8 与 1–500；更改只作用于新签发 challenge，不能要求已经签发的 challenge 使用新成本。

管理 key 故障可能只影响工作量查询/统计，而 verification secret 故障影响实际验证；两者分开定位。Cap 不参与 NoCTF readiness 自动重启判断。

## Turnstile 配置

填写正确 Site Key，将允许 hostname 按行列出，不填 scheme/path。生产使用实际域名和生产 secret；本机验证使用官方测试 key，生产不接受 loopback hostname。

替换 secret 单独保存，再进行真实浏览器 challenge 与后端 siteverify 测试。Kubernetes 配对应外呼策略，不能只让浏览器能加载 widget。

## 停用和更换

设置 None 会停用 Provider；全局关闭保留配置用于后续。换 Provider 后同步公开参数、secret 和允许主机，再核对基础、Runtime、Evaluation 三条路径。

当前数据库设置保存后是事实源，部署 HumanVerification 回退值不能被当作覆盖所有管理员保存的配置。旧 token 每次请求后丢弃，不在日志/业务表保存。

## 验收用例

分别测试普通登录/注册、Runtime、Flag/Break，切换两个独立策略确认只影响目标操作；再用管理员操作/内部回调验证对应豁免。记录当前 Provider 与配置状态，不公开 token、key 或 secret。
