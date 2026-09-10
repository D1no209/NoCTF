import { expect, test } from 'bun:test'
import { executeHomeTerminalInput, homeTerminalCommands, homeTerminalIdentity, parseHomeTerminalCommand } from '../app/features/routes/home-terminal'

test('home terminal accepts only its four commands without keeping command history', async () => {
  expect(homeTerminalCommands).toEqual(['help', 'ls', 'status', 'whoami'])
  expect(parseHomeTerminalCommand(' STATUS ')).toBe('status')
  expect(parseHomeTerminalCommand('whoami')).toBe('whoami')
  expect(parseHomeTerminalCommand('history')).toBeNull()
  expect(executeHomeTerminalInput(' LS ')).toEqual({ input: '', command: 'ls', unknownCommand: '' })
  expect(executeHomeTerminalInput('history')).toEqual({ input: '', command: null, unknownCommand: 'history' })
})

test('home terminal returns the current user or the requested anonymous spelling', () => {
  expect(homeTerminalIdentity('Mock Player')).toBe('Mock Player')
  expect(homeTerminalIdentity('  ')).toBe('unknow')
  expect(homeTerminalIdentity(null)).toBe('unknow')
})
