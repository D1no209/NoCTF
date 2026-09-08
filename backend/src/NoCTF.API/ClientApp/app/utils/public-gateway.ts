import type { NoCtfapiEndpointsRuntimePublicAccessFailureProtocol, NoCtfapiEndpointsRuntimePublicAccessStateProtocol } from '~/api'
import { translate } from './i18n'

const failures = {
  GatewayDisabled: '题目内网穿透已关闭',
  ConnectorOffline: '公网连接器暂时离线',
  PublicPortUnavailable: '该端口不在允许范围或已被占用',
  RuntimeBindingUnavailable: '无法确认该实例的端口绑定',
  UnsupportedRuntimeKind: '该运行类型暂不支持内网穿透',
  AccessDisplayUnsupported: '连接文案需要包含 {HOST} 和 {PORT}',
  GatewayCapacityExceeded: '公网端口配额已满',
  GatewayIdentityRejected: '公网实例身份校验失败',
  GatewaySafetyCheckFailed: '公网安全预检未通过',
  GatewayReconciliationPending: '公网连接正在准备或更新',
} satisfies Record<NoCtfapiEndpointsRuntimePublicAccessFailureProtocol, string>
const states = {
  Disabled: '题目内网穿透已关闭', Pending: '公网连接正在准备或更新', Ready: '公网入口已就绪',
  Unavailable: '公网入口暂不可用', Revoking: '正在关闭公网入口', Unsupported: '该运行类型暂不支持内网穿透',
} satisfies Record<NoCtfapiEndpointsRuntimePublicAccessStateProtocol, string>

export function publicGatewayFailure(value?: NoCtfapiEndpointsRuntimePublicAccessFailureProtocol | null): string {
  return value ? translate(failures[value]) : ''
}
export function publicGatewayState(value?: NoCtfapiEndpointsRuntimePublicAccessStateProtocol): string {
  return value ? translate(states[value]) : translate('公网状态待确认')
}
export function gatewayOrigin(value: string, https = false): string | null {
  try {
    const url = new URL(value)
    return (https ? url.protocol === 'https:' : ['http:', 'https:'].includes(url.protocol))
      && !url.username && !url.password && url.pathname === '/' && !url.search && !url.hash ? url.origin.toLowerCase() : null
  }
  catch { return null }
}

/** Same ASCII DNS / canonical IPv4 contract as PublicGatewayPolicyRules.Host. */
export function gatewayHost(value: string): boolean {
  if (!value.length || value.length > 253) return false
  if (/^[0-9.]+$/.test(value)) {
    const octets = value.split('.')
    return octets.length === 4 && octets.every(octet => /^(0|[1-9][0-9]{0,2})$/.test(octet) && Number(octet) <= 255)
  }
  const host = value.endsWith('.') ? value.slice(0, -1) : value
  return host.split('.').every(label => label.length > 0 && label.length <= 63
    && /^[a-z0-9](?:[a-z0-9-]*[a-z0-9])?$/i.test(label))
}
