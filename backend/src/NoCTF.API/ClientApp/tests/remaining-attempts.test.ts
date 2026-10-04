import { expect, test } from 'bun:test'
import { sourceFile } from './support/feature-source'

test('remaining attempt emphasis changes only the number below five', async () => {
  const [component, css] = await Promise.all([
    sourceFile(new URL('../app/components/ui/status/RemainingAttempts.vue', import.meta.url)).text(),
    Bun.file(new URL('../app/assets/css/main.css', import.meta.url)).text(),
  ])
  expect(component).toContain(':data-critical="count < 5 || undefined"')
  expect(component).toContain("$t('attempts.remainingPrefix')")
  expect(component).toContain("$t('attempts.remainingSuffix')")
  expect(component).toContain("$t('common.label.submissionsRemaining', { count })")
  expect(css).toContain('--critical-attempt: #ff0000;')
  expect(css).toContain("[data-slot='remaining-attempt-count'][data-critical='true']")
})

test('hint quote delegates content to the sanitized Markdown renderer', async () => {
  const quote = await sourceFile(new URL('../app/components/ui/markdown/MarkdownQuote.vue', import.meta.url)).text()
  expect(quote).toContain('<blockquote data-slot="markdown-quote">')
  expect(quote).toContain('<MarkdownContent :source="source" />')
  expect(quote).not.toContain('v-html')
})
