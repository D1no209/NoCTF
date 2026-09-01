import { parseApiError } from '../utils/api-error'
import { currentLocale, translate } from '../utils/i18n'

const definitionDiagnostics: Record<string, string> = {
  'Runtime image is required.': '容器镜像不能为空',
  'Runtime image cannot exceed 512 characters.': '容器镜像最多 512 个字符',
  'Runtime internal ports must be between 1 and 65535.': '内部端口必须是 1 到 65535 之间的整数',
  'Runtime internal ports cannot contain duplicates.': '内部端口不能重复',
  'Runtime security must drop all capabilities.': '容器安全选项必须在 cap-drop 中包含 ALL',
  'Runtime TtlSeconds must be between 1 and 604800 when configured.': '实例存活时间必须在 1 到 604800 秒之间',
  'Runtime OperationTimeoutSeconds must be between 1 and 300 when configured.': '运行环境操作超时必须在 1 到 300 秒之间',
  'Runtime resource limits are required.': '运行环境必须配置资源限制',
  'Runtime resource limits must be positive.': '运行环境的内存、CPU 和进程数上限必须为正数',
  'Runtime URL bindings require a valid exposure and template.': '入口必须填写有效的暴露范围与模板',
  'Runtime URL bindings only allow HOST and PORT placeholders.': '访问入口的显示模板只能使用 {HOST} 和 {PORT} 占位符',
  'Runtime URL bindings must expand to an absolute URI.': '控制检查入口的 URL 模板必须能展开为完整 URL',
  'Runtime URL binding ports must be between 1 and 65535.': '访问入口端口必须是 1 到 65535 之间的整数',
  'Container URL bindings require ContainerPort.': '每个容器访问入口都必须填写容器端口',
  'Container URL bindings require a dynamic port mapping.': '访问入口端口必须同时存在于对外端口列表',
  'PerTeam Container runtimes require FlagEnvironmentVariableName.': '每队独立 Flag 必须填写 Flag 环境变量名',
  'Flag environment variables cannot use the NOCTF_ prefix.': 'Flag 环境变量名不能使用 NOCTF_ 前缀',
  'CTF runtimes must use PerTeam allocation.': 'CTF 运行环境必须采用每队独立分配',
  'CTF runtimes must use PerTeam flags injected into the runtime environment.': 'CTF 容器题必须使用每队独立 Flag',
  'CTF runtime URL bindings must use OwnerOnly exposure.': 'CTF 访问入口必须设为仅队伍自己可见',
  'AWD runtimes must use PerTeam allocation.': 'AWD 运行环境必须采用每队独立分配',
  'AWD runtimes only support Container or Compose.': 'AWD 只支持单容器或 Docker Compose 运行环境',
  'FlagInjection is required when Runtime is configured.': '启用 AWD 运行环境后必须配置 Flag 注入命令',
  'FlagInjection.Command must be a non-empty raw template containing ${FLAG}.': 'AWD Flag 注入命令必须包含 ${FLAG}',
  'FlagInjection.TimeoutSeconds must be positive.': 'AWD Flag 注入超时必须为正数',
  'FlagInjection.ServiceName is required for Compose runtimes.': 'Compose 运行环境必须填写 Flag 注入目标服务名',
  'Runtime is required when Checker is configured.': '启用 Checker 前必须先启用运行环境',
  'Checker.Image is required.': 'Checker 镜像不能为空',
  'Checker.TargetServiceName is required only for Compose Runtime.': 'Compose Checker 必须填写目标服务名',
  'AWDP player Runtime allocation must be PerTeam.': 'AWDP 运行环境必须采用每队独立分配',
  'AWDP player Runtime FlagSource must be PerTeam.': 'AWDP 运行环境必须使用每队独立 Flag',
  'AWDP requires a Docker or Kubernetes Container runtime.': 'AWDP 只支持单容器运行环境',
  'AWDP target Runtime must declare exactly one InternalPort.': 'AWDP 必须且只能填写 1 个内部端口',
  'AWDP player Runtime must publish exactly one attack port.': 'AWDP 必须且只能填写 1 个对外端口',
  'AWDP player Runtime must publish its single checker target port.': 'AWDP 的内部端口与对外端口必须相同',
  'AWDP player Runtime must publish an OwnerOnly access URL.': 'AWDP 必须添加至少 1 个仅队伍自己可见的访问入口',
  'AWDP player Runtime URL bindings must use OwnerOnly exposure.': 'AWDP 访问入口必须设为仅队伍自己可见',
  'AWDP player Runtime URL bindings must target its checker port.': 'AWDP 访问入口端口必须与内部端口相同',
  'PatchEntrypoint is required.': '补丁入口不能为空',
  'PatchEntrypoint cannot exceed 256 characters.': '补丁入口最多 256 个字符',
  'PatchEntrypoint must be a safe relative path.': '补丁入口必须是安全的相对路径',
  'PatchTimeoutSeconds must be positive when configured.': '补丁超时必须为正数',
  'ReadyTimeoutSeconds must be positive when configured.': '就绪超时必须为正数',
}

function splitDiagnostics(message: string): string[] {
  return message
    .split(/(?<=\.)\s+(?=[A-Z])/)
    .map(item => item.trim())
    .filter(Boolean)
}

function localizeDiagnostic(message: string): string {
  const direct = definitionDiagnostics[message]
  if (direct) return translate(direct)
  if (/definition|schemaVersion|JSON/i.test(message))
    return translate('题目定义版本或 JSON 格式无效')
  if (currentLocale() === 'en') return message
  return translate('后端返回了未识别的校验原因：{reason}', { reason: message })
}

export function challengeTemplateWriteErrorMessages(error: unknown): string[] {
  const problem = error && typeof error === 'object'
    ? error as { code?: string, detail?: string, title?: string, errors?: Record<string, string[]> }
    : null
  switch (problem?.code) {
    case 'ResourceIdConflict':
      return [translate('题目模板资源标识冲突，请重新创建')]
    case 'ActiveCompetitionModeConflict':
      return [translate('该模板正被进行中的比赛引用，不能修改游戏模式')]
    case 'OwnerIncludedInManagerSet':
      return [translate('模板负责人不能同时出现在协作者列表中')]
    case 'UserNotFound':
      return [translate('协作者中包含不存在的用户')]
    case 'RoleNotEligible':
      return [translate('协作者必须具备组织者或管理员角色')]
  }
  const diagnostics = [
    problem?.detail,
    ...Object.values(problem?.errors ?? {}).flat(),
  ]
    .filter((message): message is string => typeof message === 'string' && !!message.trim())
    .flatMap(splitDiagnostics)
    .map(localizeDiagnostic)
  if (diagnostics.length > 0) return [...new Set(diagnostics)]

  return [parseApiError(error).message]
}

export function challengeTemplateWriteErrorMessage(error: unknown): string {
  return challengeTemplateWriteErrorMessages(error).join(translate('；'))
}
