import { readdirSync, readFileSync } from 'node:fs'
import { basename, join, relative, resolve } from 'node:path'
import { parse as parseSfc } from '@vue/compiler-sfc'
import { parse as parseTemplate } from '@vue/compiler-dom'
import ts from 'typescript'
import { chineseMessages } from '../app/locales/zh-CN'
import { englishMessages } from '../app/locales/en'

export interface ArchitectureIssue { file: string; rule: string; detail: string }
const visibleAttributes = new Set(['title', 'aria-label', 'aria-description', 'alt', 'placeholder', 'label', 'description'])
const nativeControls = new Set(['button', 'input', 'textarea', 'select', 'option', 'label', 'a', 'table', 'hr', 'progress', 'dialog'])
const messages = chineseMessages as Record<string, string>

/** No allowlist of existing violations: new and existing files obey the same rules. */
export function auditVueSource(file: string, source: string): ArchitectureIssue[] {
  const issues: ArchitectureIssue[] = []
  const report = (rule: string, detail: string) => issues.push({ file, rule, detail })
  const { descriptor, errors } = parseSfc(source, { filename: file })
  for (const error of errors) report('syntax', String(error))
  const isView = file.startsWith('components/views/')
  const isPrimitive = file.startsWith('components/ui/')
  const isShell = /^(pages|layouts)\//.test(file) || file === 'app.vue'
  const setup = descriptor.scriptSetup?.content ?? ''
  const ast = ts.createSourceFile(file + '.ts', setup, ts.ScriptTarget.Latest, true)

  function inspectScript(node: ts.Node) {
    if ((ts.isStringLiteral(node) || ts.isNoSubstitutionTemplateLiteral(node)) && /\p{Script=Han}/u.test(node.text))
      report('i18n', 'UI text must live in the locale catalog, including defaults and accessibility text')
    if (isView && ts.isImportDeclaration(node) && !node.importClause?.isTypeOnly) {
      const path = (node.moduleSpecifier as ts.StringLiteral).text
      if (path !== 'vue') report('render-only', `View runtime import: ${path}`)
    }
    if (isView && (ts.isFunctionDeclaration(node) || ts.isArrowFunction(node) || ts.isFunctionExpression(node)))
      report('render-only', 'Declare processing and event handlers in the feature controller')
    if (isView && ts.isCallExpression(node) && !['defineProps', 'defineOptions', 'toRefs'].includes(node.expression.getText(ast)))
      report('render-only', `View setup call: ${node.expression.getText(ast)}`)
    if (isShell && ts.isCallExpression(node) && node.expression.getText(ast) !== 'definePageMeta')
      report('route-shell', 'Route and layout shells only compose a feature and route metadata')
    if (isPrimitive && /(?:features|api|composables)\//.test(ts.isImportDeclaration(node) ? (node.moduleSpecifier as ts.StringLiteral).text : ''))
      report('primitive-boundary', 'Shared primitives cannot import product features, API clients or business composables')
    ts.forEachChild(node, inspectScript)
  }
  inspectScript(ast)

  if (descriptor.template) {
    try {
      const template = parseTemplate(descriptor.template.content)
      function visit(node: any) {
        if (node.type === 2 && /[\p{L}\p{N}]/u.test(node.content)) report('i18n', `Literal template text: ${node.content.trim()}`)
        if (node.type === 1) {
          if (!isPrimitive && nativeControls.has(node.tag)) report('primitive-boundary', `Use a shared primitive instead of <${node.tag}>`)
          for (const prop of node.props) {
            if (prop.type === 6 && visibleAttributes.has(prop.name) && prop.value?.content.trim())
              report('i18n', `Literal ${prop.name}: ${prop.value.content}`)
            if (prop.type === 7 && prop.exp) {
              if (isView && prop.name === 'on' && /(?:\s=\s|=>\s*\{|;|\+\+|--)/.test(prop.exp.content))
                report('render-only', 'Event processing belongs to the feature; forward the event to a command')
              inspectExpression(prop.exp.content)
            }
          }
        }
        if (node.type === 5) inspectExpression(node.content.content)
        node.children?.forEach(visit)
      }
      function inspectExpression(value: string) {
        const expression = ts.createSourceFile('expression.ts', `const value = (${value})`, ts.ScriptTarget.Latest, true)
        const visit = (node: ts.Node) => {
          if ((ts.isStringLiteral(node) || ts.isNoSubstitutionTemplateLiteral(node)) && /\p{Script=Han}/u.test(node.text)) report('i18n', 'Literal UI text inside a template expression')
          if (ts.isCallExpression(node) && ['$t', 't', 'translate'].includes(node.expression.getText(expression))) {
            const key = node.arguments[0]
            if (key && ts.isStringLiteral(key) && !(key.text in messages)) report('i18n-key', `Unknown message key: ${key.text}`)
          }
          ts.forEachChild(node, visit)
        }
        visit(expression)
      }
      visit(template)
    } catch (error) { report('syntax', String(error)) }
  }
  return issues
}

export function auditArchitecture(appRoot = resolve('app')): ArchitectureIssue[] {
  const walk = (directory: string): string[] => readdirSync(directory, { withFileTypes: true }).flatMap(entry => {
    const path = join(directory, entry.name)
    return entry.isDirectory() ? walk(path) : [path]
  })
  const vueFiles = walk(appRoot).filter(file => file.endsWith('.vue'))
  const issues = vueFiles.flatMap(file => auditVueSource(relative(appRoot, file).replaceAll('\\', '/'), readFileSync(file, 'utf8')))
  const primitives = new Set(vueFiles.filter(file => relative(appRoot, file).replaceAll('\\', '/').startsWith('components/ui/')).map(file => basename(file, '.vue')))
  const frameworkComponents = new Set(['NuxtPage', 'NuxtLayout', 'NuxtLink', 'ClientOnly', 'Transition', 'TransitionGroup', 'KeepAlive', 'Suspense', 'Teleport'])
  for (const file of vueFiles.filter(file => relative(appRoot, file).replaceAll('\\', '/').startsWith('components/views/'))) {
    const { descriptor } = parseSfc(readFileSync(file, 'utf8'))
    const bindings = new Set(descriptor.scriptSetup?.content.match(/const \{ (.*?) \} = toRefs/)?.[1]?.split(', ') ?? [])
    for (const match of descriptor.template?.content.matchAll(/<([A-Z]\w*)(?=[\s/>])/g) ?? []) {
      const name = match[1]!
      if (!primitives.has(name) && !bindings.has(name) && !frameworkComponents.has(name))
        issues.push({ file: relative(appRoot, file), rule: 'component-resolution', detail: `Unregistered component: ${name}` })
    }
  }
  const zhKeys = Object.keys(chineseMessages).sort(), enKeys = Object.keys(englishMessages).sort()
  if (JSON.stringify(zhKeys) !== JSON.stringify(enKeys)) issues.push({ file: 'locales', rule: 'i18n-key', detail: 'Locale catalogs must have exactly the same keys' })
  for (const key of zhKeys) {
    const tokens = (s: string) => [...s.matchAll(/\{(\w+)\}/g)].map(match => match[1]).sort()
    if (JSON.stringify(tokens(messages[key]!)) !== JSON.stringify(tokens(englishMessages[key as keyof typeof englishMessages])))
      issues.push({ file: 'locales', rule: 'i18n-parameters', detail: `Interpolation parameters differ: ${key}` })
  }
  return issues
}

if (import.meta.main) {
  const issues = auditArchitecture()
  for (const issue of issues) console.error(`${issue.file} [${issue.rule}] ${issue.detail}`)
  console.log(`Architecture audit: ${issues.length} violation(s)`)
  process.exitCode = issues.length ? 1 : 0
}
