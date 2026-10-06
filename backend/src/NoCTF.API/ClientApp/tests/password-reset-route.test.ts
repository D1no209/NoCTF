import { expect, test } from 'bun:test'
import { createMemoryHistory, createRouter } from 'vue-router'

test('password reset emails point to an existing frontend page and preserve the encoded token', async () => {
  const delivery = await Bun.file(new URL('../../../NoCTF.Infrastructure/Authentication/SmtpEmailVerificationDelivery.cs', import.meta.url)).text()
  const relativePath = delivery.match(/var resetUrl = new Uri\(\s*publicBaseUri,\s*\$"([^"?]+)\?token=/)![1]!
  const pageFile = Bun.file(new URL(`../app/pages/${relativePath}.vue`, import.meta.url))
  expect(await pageFile.exists()).toBe(true)
  const page = await pageFile.text()
  const script = page.match(/<script setup lang="ts">([\s\S]*?)<\/script>/)![1]!
  const compiled = new Bun.Transpiler({ loader: 'ts' }).transformSync(script)
    .replace(/^import[\s\S]*?from ["'][^"']+["'];?\s*$/gm, '')
  let metadata: { middleware: string } = { middleware: '' }
  new Function('definePageMeta', compiled)((value: typeof metadata) => { metadata = value })
  const router = createRouter({ history: createMemoryHistory(), routes: [{
    path: '/auth/password-reset', component: { render: () => null }, meta: metadata,
  }] })
  const token = 'test+token/with?reserved&characters='
  await router.push(`/${relativePath}?token=${encodeURIComponent(token)}`)
  expect(router.currentRoute.value.matched[0]!.path).toBe('/auth/password-reset')
  expect(router.currentRoute.value.query.token).toBe(token)
  expect(router.currentRoute.value.meta.middleware).toBe('guest')
})
