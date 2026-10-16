/**
 * 认证/身份切换后的回跳只接受站内绝对路径。
 * 拒绝协议相对 URL(//host)、反斜杠变体、控制字符与空白,避免开放重定向。
 */
export function safeInternalPath(value: unknown, fallback = '/'): string {
  return typeof value === 'string'
    && value.startsWith('/')
    && !value.startsWith('//')
    && !value.includes('\\')
    && !/[\u0000-\u0020\u007f]/.test(value)
    ? value
    : fallback
}
