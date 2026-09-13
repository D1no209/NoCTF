import { readdirSync, readFileSync } from 'node:fs'
import { basename, join, relative, resolve } from 'node:path'
import { parse as parseSfc } from '@vue/compiler-sfc'
import { parse as parseTemplate } from '@vue/compiler-dom'
import ts from 'typescript'
import { chineseMessages } from '../app/locales/zh-CN'
import { englishMessages } from '../app/locales/en'

export interface ArchitectureIssue { file: string; rule: string; detail: string }
const visibleAttributes = new Set(['title', 'aria-label', 'aria-description', 'alt', 'placeholder', 'label', 'description'])
const nativeControls = new Set(['form', 'button', 'input', 'textarea', 'select', 'option', 'label', 'a', 'table', 'hr', 'progress', 'dialog', 'details', 'summary', 'datalist', 'meter', 'fieldset', 'legend'])
const nativePickerTypes = new Set(['number', 'date', 'datetime-local', 'time', 'month', 'week', 'file', 'color', 'range'])
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
    if (ts.isImportDeclaration(node) && /native-select/.test((node.moduleSpecifier as ts.StringLiteral).text))
      report('native-defaults', 'Use the themed Select primitive, including inside composite controls')
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
          if (/^NativeSelect/.test(node.tag)) report('native-defaults', 'Native Select is not part of the design system')
          if (isView) {
            const marker = node.props.some((prop: any) => prop.type === 6 && prop.name === 'data-scroll-surface')
            const classes = node.props.find((prop: any) => prop.type === 6 && prop.name === 'class')?.value?.content ?? ''
            if (/overflow-(?:x-|y-)?(?:auto|scroll)/.test(classes) && node.tag !== 'ScrollSurface' && !marker)
              report('scroll-boundary', 'Declare a shared scroll surface for constrained overflow')
            if (['article', 'aside', 'div', 'section'].includes(node.tag) && /\bshadow-inner\b/.test(classes) && /\bbg-/.test(classes))
              report('surface-boundary', 'Compose panel surfaces with the shared Card primitive')
          }
          for (const prop of node.props) {
            if (isView && prop.type === 7 && prop.name === 'html')
              report('primitive-boundary', 'Render user markup through the sanitized Markdown primitive')
            if (isView && prop.type === 6 && prop.name === 'type' && nativePickerTypes.has(prop.value?.content))
              report('native-defaults', `Use the dedicated themed control for ${prop.value.content}`)
            if (isView && prop.type === 7 && prop.name === 'bind' && prop.arg?.content === 'title' && !['component', 'DefinitionSection'].includes(node.tag))
              report('native-defaults', 'Use Hint for DOM tooltips; do not expose browser title tooltips')
            if (prop.type === 6 && visibleAttributes.has(prop.name) && prop.value?.content.trim())
              report('i18n', `Literal ${prop.name}: ${prop.value.content}`)
            if (prop.type === 7 && prop.exp) {
              if (isView && prop.name === 'on' && containsEventProcessing(prop.exp.content))
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
      function containsEventProcessing(value: string): boolean {
        const expression = ts.createSourceFile('event-expression.ts', `const value = (${value})`, ts.ScriptTarget.Latest, true)
        let processing = false
        const visit = (node: ts.Node) => {
          if (ts.isArrowFunction(node) || ts.isFunctionExpression(node)) {
            processing = true
            return
          }
          if (ts.isBinaryExpression(node)
            && node.operatorToken.kind >= ts.SyntaxKind.FirstAssignment
            && node.operatorToken.kind <= ts.SyntaxKind.LastAssignment) {
            processing = true
            return
          }
          if ((ts.isPrefixUnaryExpression(node) || ts.isPostfixUnaryExpression(node))
            && (node.operator === ts.SyntaxKind.PlusPlusToken || node.operator === ts.SyntaxKind.MinusMinusToken)) {
            processing = true
            return
          }
          ts.forEachChild(node, visit)
        }
        visit(expression)
        return processing
      }
      visit(template)
    } catch (error) { report('syntax', String(error)) }
  }
  if (isView) for (const style of descriptor.styles) {
    if (/#[\da-f]{3,8}\b|\b(?:rgb|rgba|hsl|hsla)\(\s*[\d.]/i.test(style.content))
      report('theme-boundary', 'View colors must come from semantic theme tokens')
    for (const shadow of style.content.matchAll(/(?:box-shadow|text-shadow)\s*:\s*([^;}]+)/g))
      if (!/^(?:var\(|none\b)/.test(shadow[1]!.trim())) report('theme-boundary', 'Use the shared shadow tokens')
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
  for (const file of walk(appRoot).filter(file => file.endsWith('.ts') && !relative(appRoot, file).replaceAll('\\', '/').startsWith('api/'))) {
    const source = readFileSync(file, 'utf8')
    if (/\bwindow\.(?:alert|confirm|prompt)\s*\(/.test(source))
      issues.push({ file: relative(appRoot, file), rule: 'native-defaults', detail: 'Route app confirmations through the shared dialog system' })
  }
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
