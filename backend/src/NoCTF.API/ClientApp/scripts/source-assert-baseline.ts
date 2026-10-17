/**
 * 重新生成源码断言基线(tests/support/source-assert-baseline.json)。
 *
 * 仅在把断言真正迁移成行为测试、或测试文件被重命名/删除后运行:
 *   bun run test:quality-baseline
 * 该基线只允许下降;质量棘轮测试会拒绝任何增长。
 */
import { readdirSync, readFileSync, writeFileSync } from 'node:fs'
import { join } from 'node:path'
import { countPureSyntaxAssertions, countSourceTextAssertions, totalAssertions } from '../tests/support/source-assertions'

const testDir = join(import.meta.dir, '..', 'tests')
const files = readdirSync(testDir).filter(name => name.endsWith('.test.ts')).sort()

const sourceAssertions: Record<string, number> = {}
const pureSyntax: Record<string, number> = {}
let assertions = 0
for (const file of files) {
  const source = readFileSync(join(testDir, file), 'utf8')
  assertions += totalAssertions(source)
  const text = countSourceTextAssertions(source)
  if (text > 0) sourceAssertions[file] = text
  const syntax = countPureSyntaxAssertions(source)
  if (syntax > 0) pureSyntax[file] = syntax
}

const baseline = {
  note: '由 scripts/source-assert-baseline.ts 生成;棘轮只允许这些数字下降。',
  textAssertions: assertions,
  sourceTextAssertions: { total: Object.values(sourceAssertions).reduce((sum, value) => sum + value, 0), files: sourceAssertions },
  pureSyntaxAssertions: { total: Object.values(pureSyntax).reduce((sum, value) => sum + value, 0), files: pureSyntax },
}
writeFileSync(join(testDir, 'support', 'source-assert-baseline.json'), `${JSON.stringify(baseline, null, 2)}\n`)
console.log(`baseline: ${baseline.sourceTextAssertions.total} 源码断言 / ${baseline.pureSyntaxAssertions.total} 纯语法断言 (共 ${assertions} 断言)`)
