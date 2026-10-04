import { existsSync, mkdirSync, readFileSync, readdirSync, writeFileSync, appendFileSync } from 'node:fs'
import { resolve, join, dirname, relative } from 'node:path'
import { readCatalog, validateCatalog, validateTranslations } from './Validate-Translations'

const backendCatalog = 'backend/src/NoCTF.API/Localization/Catalogs'
const frontendCatalog = 'backend/src/NoCTF.API/ClientApp/app/locales/catalogs'

export function sourceFiles(root: string, backendOnly: boolean): string[] {
  return [backendCatalog, ...(backendOnly ? [] : [frontendCatalog])].flatMap(directory =>
    readdirSync(join(root, directory, 'en')).filter(file => file.endsWith('.json')
      && !(directory === frontendCatalog && file === 'api.json'))
      .map(file => `${directory}/en/${file}`))
}

export function prepareTranslations(root: string, stage: string, backendOnly: boolean): void {
  validateTranslations(root, backendOnly)
  for (const file of sourceFiles(root, backendOnly)) {
    const target = join(stage, file)
    mkdirSync(dirname(target), { recursive: true })
    writeFileSync(target, readFileSync(join(root, file)))
  }
  const config = JSON.parse(readFileSync(join(root, 'crowdin.yml'), 'utf8'))
  // The official Action runs inside /github/workspace, not the runner's host path.
  config.base_path = relative(root, stage).replaceAll('\\', '/')
  if (backendOnly) config.files = config.files.filter((file: { source: string }) => file.source.startsWith(backendCatalog))
  writeFileSync(join(root, '.crowdin-stage.yml'), JSON.stringify(config, null, 2) + '\n')
}

/** Validate the complete candidate before touching any build input. Roll back IO failures too. */
export function applyTranslations(root: string, stage: string, backendOnly: boolean): void {
  const changes: Array<{ path: string; content: string; original: string }> = []
  for (const file of sourceFiles(root, backendOnly)) {
    const source = readCatalog(join(root, file))
    const stagedSource = readCatalog(join(stage, file))
    if (JSON.stringify(source) !== JSON.stringify(stagedSource)) throw new Error(`English source changed during synchronization: ${file}`)
    const relative = file.replace('/en/', '/zh-CN/')
    const candidate = join(stage, relative)
    const translated = existsSync(candidate) ? readCatalog(candidate) : {}
    validateCatalog(source, translated, candidate)
    const path = join(root, relative)
    changes.push({ path, content: JSON.stringify(translated, null, 2) + '\n', original: readFileSync(path, 'utf8') })
  }
  for (const directory of [backendCatalog, ...(backendOnly ? [] : [frontendCatalog])]) {
    const path = join(stage, directory, 'zh-CN')
    const allowed = new Set(sourceFiles(root, backendOnly).map(file => file.replace('/en/', '/zh-CN/')))
    if (existsSync(path)) for (const file of readdirSync(path)) {
      if (!allowed.has(`${directory}/zh-CN/${file}`)) throw new Error(`Unexpected downloaded catalog: ${file}`)
    }
  }
  try { for (const change of changes) writeFileSync(change.path, change.content) }
  catch (error) {
    for (const change of changes) writeFileSync(change.path, change.original)
    throw error
  }
}

export function finishTranslations(root: string, stage: string, backendOnly: boolean, outcome: string): string {
  // Repository errors are not remote synchronization failures and must stop the build.
  validateTranslations(root, backendOnly)
  if (outcome !== 'success') return outcome === 'disabled'
    ? 'Crowdin is not configured; using repository translations.'
    : 'Crowdin upload/download failed; using repository translations.'
  try {
    applyTranslations(root, stage, backendOnly)
    return 'Latest Crowdin translations validated and applied. Missing translations fall back to English.'
  }
  catch (error) {
    return `Crowdin catalog validation failed; using repository translations. ${String(error)}`
  }
}

if (import.meta.main) {
  const mode = process.argv[2]
  const root = resolve(process.argv[3] ?? '.')
  const stage = join(root, '.crowdin-stage')
  const backendOnly = process.argv.includes('--backend-only')
  if (mode === 'prepare') {
    validateTranslations(root, backendOnly)
    const enabled = /^\d+$/.test(process.env.CROWDIN_PROJECT_ID ?? '') && process.env.CROWDIN_TOKEN_AVAILABLE === 'true'
    if (enabled) prepareTranslations(root, stage, backendOnly)
    if (process.env.GITHUB_OUTPUT) appendFileSync(process.env.GITHUB_OUTPUT, `enabled=${enabled}\n`)
    console.log(enabled ? 'Crowdin synchronization prepared' : 'Crowdin is not configured; using repository translations')
  }
  else if (mode === 'apply') {
    const summary = finishTranslations(root, stage, backendOnly, process.env.CROWDIN_OUTCOME ?? 'disabled')
    console.log(summary)
    if (process.env.GITHUB_STEP_SUMMARY) appendFileSync(process.env.GITHUB_STEP_SUMMARY, `\n${summary}\n`)
    if (summary.includes('failed')) console.log(`::warning::${summary.replace(/[\r\n]/g, ' ')}`)
  }
  else throw new Error('Usage: bun backend/scripts/Sync-Translations.ts prepare|apply [root] [--backend-only]')
}
