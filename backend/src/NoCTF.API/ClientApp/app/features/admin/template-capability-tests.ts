import type { AdminChallengeBankGetCapabilityTestResponse } from '../../api'

export function templateCapabilityTestFinished(state: AdminChallengeBankGetCapabilityTestResponse['state']): boolean {
  return state === 'Completed' || state === 'PlatformFailed' || state === 'Canceled'
}

export function selectTemplateTestOption(selected: readonly string[], optionId: string, multiple: boolean, checked: boolean): string[] {
  if (!checked) return selected.filter(id => id !== optionId)
  return multiple ? [...new Set([...selected, optionId])] : [optionId]
}
