import { describe, expect, test } from 'bun:test'
import { fileURLToPath } from 'node:url'
import { auditArchitecture, auditVueSource } from '../scripts/check-architecture'

describe('frontend architecture', () => {
  test('all existing surfaces obey the rendering, primitive and locale boundaries', () => {
    expect(auditArchitecture(fileURLToPath(new URL('../app', import.meta.url)))).toEqual([])
  })

  test('rejects English/CJK copy, accessible-name literals and missing keys', () => {
    const issues = auditVueSource('components/views/Example.vue', `<template><Button aria-label="Delete">删除 {{ $t('missing.key') }}</Button></template>`)
    expect(issues.filter(issue => issue.rule === 'i18n')).toHaveLength(2)
    expect(issues.some(issue => issue.rule === 'i18n-key')).toBe(true)
  })

  test('rejects feature hooks, API imports, private controls and template processing in views', () => {
    const issues = auditVueSource('components/views/Example.vue', `<script setup lang="ts">import { fetchData } from '~/api'; const load = () => fetchData()</script><template><button @click="open = true" /></template>`)
    expect(issues.some(issue => issue.rule === 'render-only')).toBe(true)
    expect(issues.some(issue => issue.rule === 'primitive-boundary')).toBe(true)
  })
})
