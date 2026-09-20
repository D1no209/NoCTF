# BOT Provider 开发指南

NoCTF BOT 的聊天框架边界由 `NoCTF.Bot.Core` 中的 `IChatProvider` 定义。Core 不引用任何
Provider SDK；适配器也不得引用 NoCTF 平台项目。

## 契约

Provider 必须提供唯一、稳定、大小写不敏感的 `Id`，并实现：

- `GetIdentityAsync`：探测当前聊天账号；
- `ReadGroupMessagesAsync`：提供可取消的异步群消息流；
- `GetGroupMemberRoleAsync`：映射 Member/Admin/Owner；
- `SendGroupTextAsync`：发送纯文本并返回 Provider 消息 ID；
- `TryNormalizeUserId`：验证并规范化 master/admin 标识；
- `ChatProviderCapabilities`：声明群消息、角色查询和最大文本长度。

Provider 必须在自己的项目中注册 `IChatProvider`、强类型配置和 `HttpClient`。一个宿主进程只选择
一个 Provider；不要通过目录扫描或反射加载任意 DLL。新增适配器应仿照
`NoCTF.Bot.Providers.Milky` 建立独立项目，并由宿主显式引用和注册。

## 约束

- Core 中的用户、群和消息标识始终是非空字符串；数值协议只在适配器边界转换。
- 入站事件必须过滤 BOT 自己发送的消息，保留稳定 Provider 消息 ID 供去重。
- 适配器不得解释 NoCTF 命令、访问 SQLite 或生成赛事文本。
- 适配器不得记录 Access Token、原始授权头或完整敏感事件载荷。
- 不支持群角色解析的框架不能承载当前权限模型，必须在启动时失败。
- 网络断开应通过异常结束消息流，由 Core 统一退避重连；不得在适配器内部无限忙循环。

## 测试

每个 Provider 至少覆盖身份探测、ID 规范化、群角色映射、入站文本组合、自己消息过滤、Bearer
认证和发送契约。Core 测试使用 Fake Provider，确保任何 Core 源码和项目文件都不包含具体框架依赖。
