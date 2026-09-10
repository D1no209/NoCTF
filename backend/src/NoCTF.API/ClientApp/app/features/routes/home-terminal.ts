export const homeTerminalCommands = ['help', 'ls', 'status', 'whoami'] as const

export type HomeTerminalCommand = typeof homeTerminalCommands[number]

export function parseHomeTerminalCommand(value: string): HomeTerminalCommand | null {
  const normalized = value.trim().toLowerCase()
  return homeTerminalCommands.find(command => command === normalized) ?? null
}

export function executeHomeTerminalInput(value: string) {
  const entered = value.trim()
  const command = parseHomeTerminalCommand(entered)
  return {
    input: '',
    command,
    unknownCommand: command ? '' : entered,
  }
}

export function homeTerminalIdentity(userName?: string | null): string {
  return userName?.trim() || 'unknow'
}
