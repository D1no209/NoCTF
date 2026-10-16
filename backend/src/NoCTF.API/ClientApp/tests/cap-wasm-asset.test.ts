import { expect, test } from 'bun:test'
import { createHash } from 'node:crypto'

/** 自托管副本的 SHA-256:替换文件时必须同步更新,防止意外引入其他版本。 */
const vendoredHashwxSha256 = 'B1A0DBB3EF444D3C7069E0A5E0A0273FFA4CF8FEF62CBBE43761C02F7CD6AFF5'

test('the self-hosted hashwx wasm is a valid module with the recorded digest', async () => {
  const file = Bun.file(new URL('../app/assets/wasm/hashwx.wasm', import.meta.url))
  expect(await file.exists()).toBe(true)
  const bytes = new Uint8Array(await file.arrayBuffer())
  // WebAssembly magic: \0asm
  expect(Array.from(bytes.slice(0, 4))).toEqual([0x00, 0x61, 0x73, 0x6d])
  expect(createHash('sha256').update(bytes).digest('hex').toUpperCase()).toBe(vendoredHashwxSha256)
})

test('the vendored wasm stays in sync with the widget and never falls back to a CDN', async () => {
  const feature = await Bun.file(new URL('../app/features/security/useHumanVerification.ts', import.meta.url)).text()
  const widget = await Bun.file(new URL('../node_modules/@cap.js/widget/src/cap.js', import.meta.url)).text()

  const widgetVersion = /const WASM_VERSION = "([^"]+)"/.exec(widget)?.[1]
  const pinnedVersion = /const capHashwxWasmVersion = '([^']+)'/.exec(feature)?.[1]
  expect(pinnedVersion).toBeDefined()
  expect(pinnedVersion).toBe(widgetVersion)

  expect(feature).toContain("import hashwxWasmUrl from '~/assets/wasm/hashwx.wasm?url'")
  expect(feature).toContain('window.CAP_CUSTOM_HASHWX_URL = hashwxWasmUrl')
  // 主 WASM 仍沿用后端探针校验过的 Cap 服务端资源路径。
  expect(feature).toContain("'../assets/cap_wasm_bg.wasm'")

  // 两个覆盖必须在创建 Cap 实例之前生效,否则控件会走 CDN 回退。
  const instantiation = feature.indexOf('new Cap(')
  expect(instantiation).toBeGreaterThan(0)
  expect(feature.indexOf('window.CAP_CUSTOM_WASM_URL =')).toBeLessThan(instantiation)
  expect(feature.indexOf('window.CAP_CUSTOM_HASHWX_URL =')).toBeLessThan(instantiation)
})
