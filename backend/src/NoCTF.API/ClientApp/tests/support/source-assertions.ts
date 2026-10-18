/**
 * 统计“对源码文本而不是运行时行为”的断言数量。
 *
 * 测试策略要求优先断言行为;历史上大量测试读取 `app/` 源码再断言代码形态,
 * 这类断言在重构时会无意义地失败,也无法证明行为正确。本模块提供统一口径,
 * 供质量棘轮测试(src/test-quality-ratchet.test.ts)记录并阻止其增长。
 */

const SOURCE_READ_RE = /(?:const|let)\s+(\w+)\s*=\s*(?:await\s+)?(?:sourceFile\(|readFeatureSource\(|readSource\(|Bun\.file\(new URL\('[^']*app\/)/g

/** 纯语法形态:断言字符串本身是代码结构(brace/分号/缩进/箭头/关键字),与业务语义无关。 */
const PURE_SYNTAX_RE = /^(?:\s*(?:\}|\)|\]|;|=>|do \{|\} while|\} else|\} catch|try \{|await |const |let |return |if \(|for \(|while \(|switch |case |break|\.\.\.|\/\/|import |export |function )|\s{2,}\S)/

const ASSERTION_RE = /\.(?:toContain|not\.toContain|toMatch|not\.toMatch)\(/g

function escapeForRegExp(value: string): string {
  return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
}

/** 读取 app 源码的局部变量名。 */
export function sourceTextVariables(source: string): string[] {
  const names = new Set<string>()
  for (const match of source.matchAll(SOURCE_READ_RE))
    names.add(match[1]!)
  return [...names]
}

/** 对源码文本变量做的断言数量。 */
export function countSourceTextAssertions(source: string): number {
  let count = 0
  for (const name of sourceTextVariables(source)) {
    const pattern = new RegExp(`expect\\(\\s*${escapeForRegExp(name)}\\s*\\)[\\s\\S]{0,40}?\\.(?:toContain|not\\.toContain|toMatch|not\\.toMatch)`, 'g')
    count += (source.match(pattern) ?? []).length
  }
  return count
}

/** 断言字符串为纯代码形态(不可辩护的一类)的数量。 */
export function countPureSyntaxAssertions(source: string): number {
  const literalRe = /\.(?:toContain|not\.toContain)\(\s*(['"`])([^'"`]{1,60})\1\s*\)/g
  let count = 0
  for (const match of source.matchAll(literalRe)) {
    if (PURE_SYNTAX_RE.test(match[2]!)) count += 1
  }
  return count
}

export function totalAssertions(source: string): number {
  return (source.match(ASSERTION_RE) ?? []).length
}
