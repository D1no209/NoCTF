import { computed, ref, watch } from 'vue'
import { builtInThemePackages, defaultThemeId } from '@/themes/presets'
import { readThemeArchiveEntries } from '@/themes/theme-archive'
import {
  ACTIVE_THEME_STORAGE_KEY,
  isThemeTokenName,
  isUiPackageId,
  THEME_PACKAGE_FORMAT,
  THEME_PACKAGE_FORMAT_VERSION,
  THEME_STORAGE_KEY,
  type ThemePackage,
  type ThemePackageDraft,
  type ThemePackageExport,
  type ThemePreview,
} from '@/themes/theme-package'

const MAX_THEME_NAME_LENGTH = 48
const MAX_THEME_DESCRIPTION_LENGTH = 500
const MAX_THEME_VERSION_LENGTH = 32
const MAX_THEME_TOKEN_VALUE_LENGTH = 512

export interface ThemeActivation {
  theme: ThemePackage
  persisted: boolean
}

function getDefaultTokens() {
  return builtInThemePackages.find(theme => theme.id === defaultThemeId)!.tokens
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return Boolean(value) && !Array.isArray(value) && typeof value === 'object'
}

function isBoundedString(value: unknown, maxLength: number): value is string {
  return typeof value === 'string' && value.length > 0 && value.length <= maxLength
}

function normalizeTokens(tokens: unknown, strict = false): Record<string, string> | undefined {
  if (!isRecord(tokens))
    return

  const normalized: Record<string, string> = {}
  for (const [token, value] of Object.entries(tokens)) {
    const valid = isThemeTokenName(token) && isBoundedString(value, MAX_THEME_TOKEN_VALUE_LENGTH)
    if (!valid) {
      if (strict)
        return
      continue
    }
    normalized[token] = value
  }
  return normalized
}

function getThemePreview(tokens: Record<string, string>, preview?: ThemePreview): ThemePreview {
  const defaults = getDefaultTokens()
  return {
    background: tokens['--background'] || preview?.background || defaults['--background'],
    surface: tokens['--card'] || preview?.surface || defaults['--card'],
    primary: tokens['--primary'] || preview?.primary || defaults['--primary'],
    accent: tokens['--accent'] || preview?.accent || defaults['--accent'],
  }
}

function normalizePreview(preview: unknown, tokens: Record<string, string>): ThemePreview | undefined {
  if (!isRecord(preview))
    return

  const values = ['background', 'surface', 'primary', 'accent'] as const
  if (!values.every(key => isBoundedString(preview[key], MAX_THEME_TOKEN_VALUE_LENGTH)))
    return

  return getThemePreview(tokens, {
    background: preview.background as string,
    surface: preview.surface as string,
    primary: preview.primary as string,
    accent: preview.accent as string,
  })
}

function normalizePackage(packageData: unknown, options: { requireId: boolean, strictTokens: boolean }): ThemePackage | undefined {
  if (!isRecord(packageData))
    return

  const { id, name, description, version, preview } = packageData
  const normalizedName = typeof name === 'string' ? name.trim() : ''
  const normalizedVersion = typeof version === 'string' ? version.trim() : ''
  if (
    (options.requireId && !isBoundedString(id, 128))
    || !isBoundedString(normalizedName, MAX_THEME_NAME_LENGTH)
    || (description !== undefined && (typeof description !== 'string' || description.length > MAX_THEME_DESCRIPTION_LENGTH))
    || (version !== undefined && !isBoundedString(normalizedVersion, MAX_THEME_VERSION_LENGTH))
  ) {
    return
  }

  const customTokens = normalizeTokens(packageData.tokens, options.strictTokens)
  if (!customTokens)
    return
  const tokens = { ...getDefaultTokens(), ...customTokens }
  const normalizedPreview = normalizePreview(preview, tokens)
  if (!normalizedPreview)
    return

  return {
    id: options.requireId ? id as string : '',
    name: normalizedName,
    description: typeof description === 'string' ? description.trim() : '',
    version: normalizedVersion || '1.0.0',
    builtIn: false,
    uiPackage: isUiPackageId(packageData.uiPackage) ? packageData.uiPackage : 'v1',
    tokens,
    preview: normalizedPreview,
  }
}

function readStorage(key: string) {
  if (typeof window === 'undefined')
    return null
  try {
    return window.localStorage.getItem(key)
  }
  catch {
    return null
  }
}

function writeStorage(key: string, value: string) {
  if (typeof window === 'undefined')
    return false
  try {
    window.localStorage.setItem(key, value)
    return true
  }
  catch {
    return false
  }
}

function loadCustomPackages(): ThemePackage[] {
  const stored = readStorage(THEME_STORAGE_KEY)
  if (!stored)
    return []

  try {
    const parsed: unknown = JSON.parse(stored)
    if (!Array.isArray(parsed))
      return []
    return parsed
      .map(theme => normalizePackage(theme, { requireId: true, strictTokens: false }))
      .filter((theme): theme is ThemePackage => Boolean(theme))
  }
  catch {
    return []
  }
}

const customPackages = ref<ThemePackage[]>(loadCustomPackages())
const storedActiveThemeId = readStorage(ACTIVE_THEME_STORAGE_KEY)
const activeThemeId = ref(storedActiveThemeId || defaultThemeId)
let appliedTokenNames = new Set<string>()

function getPackages() {
  return [...builtInThemePackages, ...customPackages.value]
}

function findTheme(id: string) {
  return getPackages().find(theme => theme.id === id)
}

function resolveActiveTheme() {
  return findTheme(activeThemeId.value) || findTheme(defaultThemeId)!
}

const activeTheme = computed(resolveActiveTheme)

function applyTokens(theme: ThemePackage) {
  if (typeof document === 'undefined')
    return

  const tokens = { ...getDefaultTokens(), ...theme.tokens }
  appliedTokenNames.forEach((token) => {
    if (!(token in tokens))
      document.documentElement.style.removeProperty(token)
  })
  Object.entries(tokens).forEach(([token, value]) => document.documentElement.style.setProperty(token, value))
  appliedTokenNames = new Set(Object.keys(tokens))
  document.documentElement.dataset.themePackage = theme.id
}

function persistCustomPackages(next: ThemePackage[]) {
  return writeStorage(THEME_STORAGE_KEY, JSON.stringify(next))
}

function persistActiveTheme(id: string) {
  return writeStorage(ACTIVE_THEME_STORAGE_KEY, id)
}

function createId() {
  return typeof crypto !== 'undefined' && 'randomUUID' in crypto
    ? crypto.randomUUID()
    : `theme-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`
}

function buildTokens(draft: ThemePackageDraft) {
  return {
    ...getDefaultTokens(),
    ...(normalizeTokens(draft.tokens) || {}),
  }
}

function buildTheme(draft: ThemePackageDraft, id: string, version = '1.0.0'): ThemePackage | undefined {
  const name = draft.name.trim()
  const description = draft.description.trim()
  if (!isBoundedString(name, MAX_THEME_NAME_LENGTH) || description.length > MAX_THEME_DESCRIPTION_LENGTH)
    return

  const tokens = buildTokens(draft)
  const preview = getThemePreview(tokens, draft.preview)
  return {
    id,
    name,
    description,
    version,
    builtIn: false,
    uiPackage: draft.uiPackage,
    tokens,
    preview,
  }
}

function applyTheme(id: string): ThemeActivation | undefined {
  const theme = findTheme(id)
  if (!theme)
    return

  activeThemeId.value = theme.id
  return {
    theme,
    persisted: persistActiveTheme(theme.id),
  }
}

function createTheme(draft: ThemePackageDraft) {
  const theme = buildTheme(draft, createId())
  if (!theme)
    return

  const next = [...customPackages.value, theme]
  if (!persistCustomPackages(next))
    return
  customPackages.value = next
  return theme
}

function duplicateTheme(id: string) {
  const source = findTheme(id)
  if (!source)
    return

  const theme: ThemePackage = {
    ...source,
    id: createId(),
    name: `${source.name} Copy`.slice(0, MAX_THEME_NAME_LENGTH),
    builtIn: false,
    preview: { ...source.preview },
    tokens: { ...source.tokens },
  }
  const next = [...customPackages.value, theme]
  if (!persistCustomPackages(next))
    return
  customPackages.value = next
  return theme
}

function updateTheme(id: string, draft: ThemePackageDraft) {
  const source = customPackages.value.find(theme => theme.id === id)
  if (!source)
    return

  const updated = buildTheme(draft, source.id, source.version)
  if (!updated)
    return
  const next = customPackages.value.map(theme => theme.id === id ? updated : theme)
  if (!persistCustomPackages(next))
    return
  customPackages.value = next
  return updated
}

function removeTheme(id: string) {
  const source = customPackages.value.find(theme => theme.id === id)
  if (!source)
    return false

  const next = customPackages.value.filter(theme => theme.id !== id)
  if (!persistCustomPackages(next))
    return false

  customPackages.value = next
  if (activeThemeId.value === id)
    applyTheme(defaultThemeId)
  return true
}

function unwrapImportedTheme(packageData: unknown) {
  if (!isRecord(packageData))
    return

  if ('format' in packageData || 'formatVersion' in packageData) {
    if (
      packageData.format !== THEME_PACKAGE_FORMAT
      || packageData.formatVersion !== THEME_PACKAGE_FORMAT_VERSION
    ) {
      return
    }
    return packageData.theme
  }

  return packageData
}

function importTheme(packageData: unknown) {
  const source = unwrapImportedTheme(packageData)
  const normalized = normalizePackage(source, { requireId: false, strictTokens: true })
  if (!normalized)
    return

  const theme: ThemePackage = {
    ...normalized,
    id: createId(),
  }
  const next = [...customPackages.value, theme]
  if (!persistCustomPackages(next))
    return
  customPackages.value = next
  return theme
}

function exportTheme(theme: ThemePackage): ThemePackageExport {
  return {
    format: THEME_PACKAGE_FORMAT,
    formatVersion: THEME_PACKAGE_FORMAT_VERSION,
    theme: {
      name: theme.name,
      description: theme.description,
      version: theme.version,
      uiPackage: theme.uiPackage,
      preview: { ...theme.preview },
      tokens: { ...theme.tokens },
    },
  }
}

export interface ThemeArchiveImportResult {
  imported: ThemePackage[]
  skipped: Array<{ name: string, reason: string }>
}

// Zip import: every `.json` entry is imported independently; failures skip the
// single entry and are reported by name instead of aborting the batch.
// Archive-level problems (not a zip, no JSON entries, over the caps) throw.
async function importThemeArchive(data: ArrayBuffer): Promise<ThemeArchiveImportResult> {
  const entries = await readThemeArchiveEntries(data)
  if (!entries.length)
    throw new Error('The archive does not contain any theme package files.')

  const result: ThemeArchiveImportResult = { imported: [], skipped: [] }
  for (const entry of entries) {
    let payload: unknown
    try {
      payload = JSON.parse(entry.text)
    }
    catch {
      result.skipped.push({ name: entry.name, reason: 'Invalid JSON.' })
      continue
    }
    const imported = importTheme(payload)
    if (imported)
      result.imported.push(imported)
    else
      result.skipped.push({ name: entry.name, reason: 'Not a compatible theme package.' })
  }
  return result
}

if (!findTheme(activeThemeId.value)) {
  activeThemeId.value = defaultThemeId
  persistActiveTheme(defaultThemeId)
}

watch(activeTheme, applyTokens, { immediate: true, flush: 'sync' })

if (typeof window !== 'undefined') {
  window.addEventListener('storage', (event) => {
    if (event.key === THEME_STORAGE_KEY)
      customPackages.value = loadCustomPackages()
    if (event.key === ACTIVE_THEME_STORAGE_KEY) {
      const incomingThemeId = event.newValue || defaultThemeId
      activeThemeId.value = incomingThemeId
    }
  })
}

export function useThemePackages() {
  return {
    activeTheme,
    activeThemeId: computed(() => activeThemeId.value),
    themePackages: computed(getPackages),
    applyTheme,
    createTheme,
    duplicateTheme,
    updateTheme,
    removeTheme,
    importTheme,
    importThemeArchive,
    exportTheme,
  }
}
