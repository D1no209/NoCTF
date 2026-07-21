export interface ThemePreview {
  background: string
  surface: string
  primary: string
  accent: string
}

export type UiPackageId = 'v1' | 'v2'

export interface ThemePackage {
  id: string
  name: string
  description: string
  version: string
  builtIn: boolean
  uiPackage: UiPackageId
  tokens: Record<string, string>
  preview: ThemePreview
}

export interface ThemePackageExport {
  format: typeof THEME_PACKAGE_FORMAT
  formatVersion: typeof THEME_PACKAGE_FORMAT_VERSION
  theme: Omit<ThemePackage, 'id' | 'builtIn'>
}

export interface ThemePackageDraft {
  name: string
  description: string
  uiPackage: UiPackageId
  preview: ThemePreview
  tokens: Record<string, string>
}

export interface ThemeTokenGroup {
  id: string
  label: string
  fields: Array<{
    token: string
    label: string
  }>
}

const themeTokenGroupDefinitions = [
  {
    id: 'foundation',
    label: 'Foundation',
    tokens: [
      '--radius',
      '--background',
      '--foreground',
      '--card',
      '--card-foreground',
      '--popover',
      '--popover-foreground',
      '--primary',
      '--primary-foreground',
      '--secondary',
      '--secondary-foreground',
      '--muted',
      '--muted-foreground',
      '--accent',
      '--accent-foreground',
      '--destructive',
      '--border',
      '--input',
      '--ring',
      '--chart-1',
      '--chart-2',
      '--chart-3',
      '--chart-4',
      '--chart-5',
    ],
  },
  {
    id: 'surfaces',
    label: 'Navigation and surfaces',
    tokens: [
      '--sidebar',
      '--sidebar-foreground',
      '--sidebar-primary',
      '--sidebar-primary-foreground',
      '--sidebar-accent',
      '--sidebar-accent-foreground',
      '--sidebar-border',
      '--sidebar-ring',
      '--card-border-color',
      '--card-shadow',
      '--panel-shadow',
      '--panel-shadow-dark',
      '--float-shadow',
      '--panel-border',
      '--panel-dark-border',
      '--panel-face-start',
      '--panel-face-end',
      '--panel-dark-face-start',
      '--panel-dark-face-end',
      '--panel-base-border',
      '--panel-base-surface',
      '--panel-base-shadow',
      '--panel-dark-base-border',
      '--panel-dark-base-surface',
      '--panel-dark-base-shadow',
    ],
  },
  {
    id: 'controls',
    label: 'Control states',
    tokens: [
      '--button-primary-border',
      '--button-primary-shadow',
      '--button-primary-hover',
      '--button-destructive-border',
      '--button-destructive-shadow',
      '--button-destructive-hover',
      '--button-outline-border',
      '--button-outline-surface',
      '--button-outline-shadow',
      '--button-outline-hover',
      '--button-ghost-border',
      '--button-ghost-hover',
    ],
  },
  {
    id: 'experience',
    label: 'Shared experience',
    tokens: [
      '--page-background-start',
      '--page-background-end',
      '--page-grid-color',
      '--app-background-start',
      '--app-background-end',
      '--app-effect-color',
      '--app-effect-opacity',
      '--card-decoration',
      '--dialog-overlay',
      '--dialog-surface',
      '--cursor-color',
      '--admin-header-background',
      '--admin-chrome-border',
      '--admin-account-surface',
      '--admin-account-muted',
    ],
  },
  {
    id: 'semantic',
    label: 'Status colors',
    tokens: [
      '--semantic-info',
      '--semantic-info-soft',
      '--semantic-info-border',
      '--semantic-success',
      '--semantic-success-soft',
      '--semantic-success-border',
      '--semantic-warning',
      '--semantic-warning-soft',
      '--semantic-warning-border',
      '--semantic-danger',
      '--semantic-danger-soft',
      '--semantic-danger-border',
      '--semantic-neutral',
      '--semantic-neutral-soft',
      '--semantic-neutral-border',
    ],
  },
  {
    id: 'auth',
    label: 'Authentication',
    tokens: [
      '--auth-border',
      '--auth-surface',
      '--auth-surface-strong',
      '--auth-surface-muted',
      '--auth-grid',
      '--auth-grid-soft',
      '--auth-background-start',
      '--auth-background-end',
      '--auth-foreground',
      '--auth-accent',
      '--auth-accent-soft',
      '--auth-muted',
    ],
  },
  {
    id: 'challenges',
    label: 'Challenge labels',
    tokens: [
      '--neon-web',
      '--neon-pwn',
      '--neon-misc',
      '--neon-reverse',
      '--neon-mobile',
      '--neon-crypto',
      '--neon-forensics',
      '--neon-ai',
      '--neon-blockchain',
      '--neon-hardware',
      '--neon-osint',
      '--neon-cloud',
      '--neon-default',
      '--neon-foreground',
    ],
  },
  {
    id: 'awdp',
    label: 'AWD screen',
    tokens: [
      '--awdp-surface',
      '--awdp-surface-muted',
      '--awdp-surface-deep',
      '--awdp-text',
      '--awdp-text-muted',
      '--awdp-text-inverse',
      '--awdp-border',
      '--awdp-star',
      '--awdp-nebula-start',
      '--awdp-nebula-mid',
      '--awdp-nebula-end',
      '--awdp-node-fill',
      '--awdp-node-stroke',
      '--awdp-category-web',
      '--awdp-category-pwn',
      '--awdp-category-ai',
      '--awdp-category-crypto',
      '--awdp-category-reverse',
      '--awdp-category-misc',
    ],
  },
  {
    id: 'leaderboard',
    label: 'Leaderboard',
    tokens: [
      '--leaderboard-paper',
      '--leaderboard-border',
      '--leaderboard-line-1',
      '--leaderboard-line-2',
      '--leaderboard-line-3',
      '--leaderboard-line-4',
      '--leaderboard-line-5',
      '--leaderboard-line-6',
      '--leaderboard-line-7',
      '--leaderboard-line-8',
      '--leaderboard-line-9',
      '--leaderboard-line-10',
      '--leaderboard-gold',
      '--leaderboard-gold-foreground',
      '--leaderboard-silver',
      '--leaderboard-silver-foreground',
      '--leaderboard-bronze',
    ],
  },
] as const

function formatTokenLabel(token: string) {
  return token
    .slice(2)
    .split('-')
    .map(part => part.length <= 2 ? part.toUpperCase() : `${part[0]?.toUpperCase()}${part.slice(1)}`)
    .join(' ')
}

export const themeTokenGroups: ThemeTokenGroup[] = themeTokenGroupDefinitions.map(group => ({
  id: group.id,
  label: group.label,
  fields: group.tokens.map(token => ({ token, label: formatTokenLabel(token) })),
}))

export const THEME_TOKEN_NAMES = new Set(themeTokenGroups.flatMap(group => group.fields.map(field => field.token)))
export const THEME_PACKAGE_FORMAT = 'noctf-theme-package'
export const THEME_PACKAGE_FORMAT_VERSION = 1
export const THEME_STORAGE_KEY = 'noctf-theme-packages-v1'
export const ACTIVE_THEME_STORAGE_KEY = 'noctf-active-theme-package-v1'

export function isThemeTokenName(token: string) {
  return THEME_TOKEN_NAMES.has(token)
}

export function isUiPackageId(value: unknown): value is UiPackageId {
  return value === 'v1' || value === 'v2'
}

export function assertThemeTokenCoverage(tokens: Record<string, string>) {
  const missing = [...THEME_TOKEN_NAMES].filter(token => !(token in tokens))
  const unsupported = Object.keys(tokens).filter(token => !isThemeTokenName(token))
  if (missing.length || unsupported.length) {
    throw new Error(`Theme token manifest is out of sync. Missing: ${missing.join(', ') || 'none'}. Unsupported: ${unsupported.join(', ') || 'none'}.`)
  }
}
