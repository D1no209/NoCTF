import { expect, test } from 'bun:test'
import { gatewayHost, gatewayOrigin, publicGatewayFailure, publicGatewayState } from '../app/utils/public-gateway'

test('hosts use the backend ASCII DNS and canonical IPv4 contract', () => {
  for (const value of ['example.com', 'EXAMPLE.com.', 'my-host.local', 'localhost', '192.0.2.1', '0.0.0.0', 'a'.repeat(63) + '.com'])
    expect(gatewayHost(value)).toBe(true)
  for (const value of ['a..b', '-a.com', 'a-.com', 'a.com-', 'a_b.com', '999.999.999.999', '192.168.01.1', '127.1', '::1', 'https://example.com', 'example.com:80', 'example.com\n', '', 'a'.repeat(64) + '.com', Array(4).fill('a'.repeat(63)).join('.')])
    expect(gatewayHost(value)).toBe(false)
})

test('gateway origins reject credentials paths query fragments and insecure public origins', () => {
  expect(gatewayOrigin('https://Gateway.Example.test:443/', true)).toBe('https://gateway.example.test')
  for (const value of ['http://gateway.test', 'https://user:password@gateway.test', 'https://gateway.test/path', 'https://gateway.test/?x=1', 'https://gateway.test/#x'])
    expect(gatewayOrigin(value, true)).toBeNull()
  expect(gatewayOrigin('http://192.0.2.1:8080')).toBe('http://192.0.2.1:8080')
})
test('independent gateway failures have specific user-facing explanations', () => {
  expect(publicGatewayFailure('GatewayDisabled')).toContain('已关闭')
  expect(publicGatewayFailure('RuntimeBindingUnavailable')).toContain('端口绑定')
  expect(publicGatewayState('Ready')).toContain('已就绪')
})
test('gateway page uses generated contracts and preserves async application and field errors', async () => {
  const page = await Bun.file(new URL('../app/pages/admin/platform/public-gateway.vue', import.meta.url)).text()
  expect(page).toContain('adminPlatformGetPublicGatewayStatus')
  expect(page).toContain('adminPlatformUpdatePublicGateway')
  expect(page).toContain('usePolling(readStatus')
  expect(page).toContain('finally { saving.value = false }')
  expect(page).toContain('FieldError')
  expect(page).toContain('gatewayHost(form.publicRuntimeHost)')
  expect(page).toContain('gatewayHost(form.directRuntimeHostOverride)')
  expect(page).toContain('namespaceIsolationAvailable')
  expect(page).toContain("$t('内网穿透')")
  expect(page).not.toContain("$t('公网访问')")
  expect(page).toContain("$t('直连端口')")
  expect(page).toContain("$t('公网端口')")
  expect(page).toContain("endpoint.publicPort ?? '—'")
  expect(page).not.toContain('endpoint.publicPort ?? endpoint.hostPort')
  expect(page).not.toContain('webServer.password')
  const runtime = await Bun.file(new URL('../app/components/challenges/RuntimeCard.vue', import.meta.url)).text()
  expect(runtime).toContain("runtime.value?.access?.route !== 'Gateway'")
  expect(runtime).toContain('clearInterval(publicTimer)')
})
