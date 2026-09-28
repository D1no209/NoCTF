import { describe, expect, test } from 'bun:test'
import { fileURLToPath } from 'node:url'
import { auditArchitecture, auditAssetPath, auditCssSource, auditVueSource } from '../scripts/check-architecture'

describe('frontend architecture', () => {
  test('rejects default pickers, browser tooltips, private scrollbars and literal view colors', () => {
    const issues = auditVueSource('components/views/Example.vue', `<template><div class="overflow-y-auto"><Input type="datetime-local" :title="label" /></div></template><style scoped>.panel { color: #fff; box-shadow: 2px 4px black; }</style>`)
    expect(issues.some(issue => issue.rule === 'native-defaults')).toBe(true)
    expect(issues.some(issue => issue.rule === 'scroll-boundary')).toBe(true)
    expect(issues.some(issue => issue.rule === 'theme-boundary')).toBe(true)
  })
  test('all existing surfaces obey the rendering, primitive and locale boundaries', () => {
    expect(auditArchitecture(fileURLToPath(new URL('../app', import.meta.url)))).toEqual([])
  })

  test('shared cards and focus states use shadows instead of visible rings or outlines', async () => {
    const card = await Bun.file(new URL('../app/components/ui/card/Card.vue', import.meta.url)).text()
    const css = await Bun.file(new URL('../app/assets/css/main.css', import.meta.url)).text()
    const cardSurface = css.slice(css.indexOf('.card-surface.card-surface'), css.indexOf('.card-surface.card-surface > img'))

    expect(card).not.toContain('ring-1')
    expect(cardSurface).toContain('border: 0')
    expect(cardSurface).toContain('background: color-mix(in oklch, var(--card) var(--card-opacity), transparent)')
    expect(cardSurface).toContain('backdrop-filter: blur(var(--card-blur))')
    expect(cardSurface).toContain('isolation: isolate')
    expect(css).toContain('--card-blur: 12px')
    expect(css).toContain('4px 6px 16px -5px')
    expect(css).toContain('6px 10px 24px -6px')
    expect(css).toContain("outline: none; box-shadow: 0 0 14px color-mix(in oklch, var(--ring) 28%, transparent)")
  })

  test('rejects English/CJK copy, accessible-name literals and missing keys', () => {
    const issues = auditVueSource('components/views/Example.vue', `<template><Button aria-label="Delete">删除 {{ $t('missing.key') }}</Button></template>`)
    expect(issues.filter(issue => issue.rule === 'i18n')).toHaveLength(2)
    expect(issues.some(issue => issue.rule === 'i18n-key')).toBe(true)
  })

  test('rejects feature hooks, API imports, private controls and template processing in views', () => {
    const issues = auditVueSource('components/views/Example.vue', `<script setup lang="ts">import { fetchData } from '~/api'; const load = () => fetchData()</script><template><button @click="open = true" /><section class="rounded-xl bg-muted/35 shadow-inner" /></template>`)
    expect(issues.some(issue => issue.rule === 'render-only')).toBe(true)
    expect(issues.some(issue => issue.rule === 'primitive-boundary')).toBe(true)
    expect(issues.some(issue => issue.rule === 'surface-boundary')).toBe(true)
  })

  test('rejects compound assignments and inline event functions in views', () => {
    const issues = auditVueSource('components/views/Example.vue', `<template><Button @click="count += 1" @focus="value ??= 1" @update:model-value="next => save(next)" /></template>`)
    expect(issues.filter(issue => issue.rule === 'render-only')).toHaveLength(3)
  })

  test('keeps feature composition, motion ownership and static SVG assets physically separated', () => {
    const featureIssues = auditVueSource('features/Example.vue', '<template><Card /></template><style scoped>.card { opacity: 1; }</style>')
    expect(featureIssues.filter(issue => issue.rule === 'logic-ui-boundary')).toHaveLength(2)
    expect(auditCssSource('assets/css/main.css', '.page { view-transition-name: page; }')[0]?.rule).toBe('motion-boundary')
    expect(auditCssSource('motion/motion.css', '.page { view-transition-name: page; }')).toEqual([])
    expect(auditAssetPath('components/ui/icon.svg')[0]?.rule).toBe('svg-boundary')
    expect(auditAssetPath('assets/svg/status/icon.svg')).toEqual([])
  })

  test('keeps PDF parsing inside the shared Canvas renderer', () => {
    const issues = auditVueSource('components/views/Example.vue', `<script setup lang="ts">import { getDocument } from 'pdfjs-dist'</script><template><iframe /></template>`)
    expect(issues.filter(issue => issue.rule === 'pdf-boundary')).toHaveLength(2)
  })
})
