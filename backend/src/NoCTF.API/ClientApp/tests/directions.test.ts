import { sourceFile } from './support/feature-source'
import { describe, expect, test } from 'bun:test'
import { Bot, CircuitBoard, Cloud, Flag, Link2, ScanSearch, Search } from '@lucide/vue'
import { directionGlyph, directionIcon, directionKey, directionLabel, directionTextClass, directionBadgeClass, directionWatermarkClass } from '../app/utils/directions'
import { scoreboardDirectionGroups } from '../app/utils/scoreboard'

describe('challenge direction iconography', () => {
  test('watermarks retain category foreground colors without badge surfaces', () => {
    for (const direction of ['Misc', 'WEB', 'Crypto', 'custom']) {
      const foreground = directionWatermarkClass(direction).split(' ')
      expect(foreground.every(token => token.startsWith('text-') || token.startsWith('dark:text-'))).toBe(true)
      expect(foreground.every(token => directionBadgeClass(direction).split(' ').includes(token))).toBe(true)
    }
    expect(directionWatermarkClass(' MISC ')).toBe(directionWatermarkClass('misc'))
  })
  test('maps common security directions to semantic icons', () => {
    expect(directionIcon('OSINT')).toBe(Search)
    expect(directionIcon('Open Source Intelligence')).toBe(Search)
    expect(directionIcon('AI')).toBe(Bot)
    expect(directionIcon('Machine Learning')).toBe(Bot)
    expect(directionIcon('Blockchain')).toBe(Link2)
    expect(directionIcon('Web3')).toBe(Link2)
    expect(directionIcon('Forensics')).toBe(ScanSearch)
    expect(directionIcon('Hardware')).toBe(CircuitBoard)
    expect(directionIcon('Cloud')).toBe(Cloud)
  })

  test('maps every supported direction to a standalone SVG asset', async () => {
    const expected = new Map([
      ['Misc', 'puzzle'], ['Web', 'globe'], ['Crypto', 'key'], ['Pwn', 'chip'],
      ['Reverse', 'unwind'], ['Forensics', 'scan'], ['OSINT', 'radar'], ['AI', 'neural'],
      ['Mobile', 'phone'], ['IoT', 'wireless'], ['Hardware', 'circuit'], ['Cloud', 'cloud'],
      ['Blockchain', 'chain'], ['Penetration', 'target'], ['Custom Research', 'flag'],
    ])
    for (const [direction, asset] of expected) {
      expect(directionGlyph(direction)).toBe(asset)
      const svg = await Bun.file(new URL(`../app/assets/svg/directions/${asset}.svg`, import.meta.url)).text()
      expect(svg).toStartWith('<svg ')
    }
    expect(directionGlyph('ML')).toBe('neural')
    expect(directionGlyph('DFIR')).toBe('scan')
    expect(directionGlyph('Web3')).toBe('chain')
    expect(directionGlyph('Embedded')).toBe('circuit')
    expect(directionGlyph('Pentest')).toBe('target')
  })

  test('keeps static SVG path data out of Vue and TypeScript sources', async () => {
    const sources = await Promise.all([
      sourceFile(new URL('../app/components/ui/icons/TechnicalIcon.vue', import.meta.url)).text(),
      sourceFile(new URL('../app/components/ui/card/CardOrnament.vue', import.meta.url)).text(),
      sourceFile(new URL('../app/components/ui/sonner/NoticeIcon.vue', import.meta.url)).text(),
      sourceFile(new URL('../app/components/ui/icons/technical-icons.ts', import.meta.url)).text(),
    ])
    for (const source of sources) {
      expect(source).not.toContain('<svg')
      expect(source).not.toContain('<path')
    }
  })

  test('keeps unknown open-text directions on the explicit fallback', () => {
    expect(directionIcon('Custom Research')).toBe(Flag)
    expect(directionIcon('constructor')).toBe(Flag)
    expect(directionIcon('__proto__')).toBe(Flag)
    expect(directionLabel('constructor')).toBe('Constructor')
    expect(directionLabel('__proto__')).toBe('__proto__')
  })
})

describe('case-insensitive challenge directions', () => {
  test('uses one display spelling for every case variant and keeps approved abbreviations', () => {
    for (const [inputs, expected] of [
      [['PWN', 'Pwn', 'pwn', ' pWn '], 'Pwn'],
      [['WEB', 'Web', 'web'], 'Web'],
      [['AI', 'Ai', 'aI', 'ai'], 'AI'],
      [['OSINT', 'osint'], 'OSINT'],
      [['iot', 'IOT', 'IoT'], 'IoT'],
      [['ml', 'Ml', 'ML'], 'ML'],
      [['llm', 'Llm', 'LLM'], 'LLM'],
      [['dfir', 'Dfir', 'DFIR'], 'DFIR'],
      [['CRYPTO', 'Crypto', 'crypto'], 'Crypto'],
      [['REVERSE', 'Reverse', 'reverse'], 'Reverse'],
      [['FORENSICS', 'Forensics', 'forensics'], 'Forensics'],
      [['MISC', 'misc'], 'Misc'],
      [['BLOCKCHAIN', 'Blockchain'], 'Blockchain'],
      [['Custom Research', 'custom research'], 'Custom research'],
      [['其他'], '其他'],
    ] as const) {
      for (const value of inputs) {
        expect(directionLabel(value)).toBe(expected)
        expect(directionKey(value)).toBe(directionKey(expected))
        expect(directionIcon(value)).toBe(directionIcon(expected))
        expect(directionTextClass(value)).toBe(directionTextClass(expected))
      }
    }
    for (const empty of [undefined, null, '', '   ']) {
      expect(directionKey(empty)).toBe('')
      expect(directionLabel(empty)).toBe('')
    }
  })

  test('merges mixed-case radar groups and keeps unrelated direction names separate', () => {
    const groups = scoreboardDirectionGroups(['PWN', 'Pwn', 'pwn', 'WEB', 'web', 'ai', 'AI', 'Rev', 'Reverse'].map((direction, index) => ({
      competitionChallengeId: String(index), challenge: { direction }, columns: [],
    })))
    expect(groups.map(group => ({ key: group.key, name: group.name, count: group.groups.length }))).toEqual([
      { key: 'pwn', name: 'Pwn', count: 3 },
      { key: 'web', name: 'Web', count: 2 },
      { key: 'ai', name: 'AI', count: 2 },
      { key: 'rev', name: 'Rev', count: 1 },
      { key: 'reverse', name: 'Reverse', count: 1 },
    ])
  })

  test('normalizes navigator groups before collapse/filter operations and normalizes editor values', async () => {
    const navigator = await sourceFile(new URL('../app/features/competition/CompetitionChallengeNavigator.vue', import.meta.url)).text()
    expect(navigator).toContain("const direction = directionLabel(item.direction) || translate(\"ui.uncategorized\")")
    expect(navigator).toContain('grouped.get(direction)')
    expect(navigator).toContain('@update:model-value="selectChallenge"')
    const editor = await sourceFile(new URL('../app/pages/admin/challenges/[id].vue', import.meta.url)).text()
    expect(editor).toContain('form.direction = directionLabel(value.direction)')
    expect(editor).toContain('direction: directionLabel(form.direction)')
  })
})
