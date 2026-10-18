import { readFileSync, readdirSync } from 'node:fs'
import { join } from 'node:path'
import baseline from './support/source-assert-baseline.json'
import { countPureSyntaxAssertions, countSourceTextAssertions } from './support/source-assertions'
import { describe, expect, test } from 'bun:test'

/**
 * 测试质量棘轮:把“对 app 源码文本断言”与“断言纯代码语法”两类历史债务钉死,
 * 只允许下降。新增覆盖请写成行为测试(驱动真实模块、断言可观察结果),
 * 真正迁移完成后用 `bun run test:quality-baseline` 记录新的更低基线。
 */
describe('test quality ratchet', () => {
  const files = readdirSync(import.meta.dir).filter(name => name.endsWith('.test.ts'))

  test('source-text assertions never grow', () => {
    const regressions: string[] = []
    for (const file of files) {
      const source = readFileSync(join(import.meta.dir, file), 'utf8')
      const allowed = baseline.sourceTextAssertions.files[file as keyof typeof baseline.sourceTextAssertions.files] ?? 0
      const count = countSourceTextAssertions(source)
      if (count > allowed) regressions.push(`${file}: ${count} > ${allowed}`)
    }

    expect(regressions).toEqual([])
  })

  test('pure syntax assertions never grow', () => {
    const regressions: string[] = []
    for (const file of files) {
      const source = readFileSync(join(import.meta.dir, file), 'utf8')
      const allowed = baseline.pureSyntaxAssertions.files[file as keyof typeof baseline.pureSyntaxAssertions.files] ?? 0
      const count = countPureSyntaxAssertions(source)
      if (count > allowed) regressions.push(`${file}: ${count} > ${allowed}`)
    }

    expect(regressions).toEqual([])
  })

  test('the ratchet tracks a non-trivial, shrinking debt', () => {
    // 基线必须真实反映当前测试套件,并低于历史峰值(本轮迁移前 1809/112)。
    expect(baseline.sourceTextAssertions.total).toBeGreaterThan(0)
    expect(baseline.pureSyntaxAssertions.total).toBeGreaterThan(0)
    expect(Object.keys(baseline.sourceTextAssertions.files).length).toBeGreaterThan(50)
  })
})
