import { themeColorAlpha } from './theme-color'

/**
 * ECharts draws on canvas and cannot resolve CSS variables, so chart guides are
 * resolved from the shared border token when the option is built.
 */
export function chartGuideColor(alpha: number): string {
  return themeColorAlpha('--border', alpha)
}
