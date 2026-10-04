import { afterEach, describe, expect, test } from 'bun:test'
import { mkdtempSync, mkdirSync, writeFileSync, readFileSync, rmSync } from 'node:fs'
import { join, dirname, basename, resolve } from 'node:path'
import { tmpdir } from 'node:os'
import { parseCatalog, validateCatalog } from './Validate-Translations'
import { finishTranslations, prepareTranslations } from './Sync-Translations'

const roots: string[] = []
afterEach(() => {
  for (const root of roots.splice(0)) {
    if (dirname(resolve(root)) !== resolve(tmpdir()) || !basename(root).startsWith('noctf-translations-'))
      throw new Error('Unexpected test cleanup path')
    rmSync(root, { recursive: true, force: true })
  }
})
const backend = 'backend/src/NoCTF.API/Localization/Catalogs'
const frontend = 'backend/src/NoCTF.API/ClientApp/app/locales/catalogs'
function write(root: string, file: string, content: string) {
  const path = join(root, file)
  mkdirSync(dirname(path), { recursive: true })
  writeFileSync(path, content)
}
function fixture() {
  const root = mkdtempSync(join(tmpdir(), 'noctf-translations-'))
  roots.push(root)
  write(root, 'crowdin.yml', JSON.stringify({ files: [{ source: `${backend}/en/api.json` }, { source: `${frontend}/en/*.json` }] }))
  for (const directory of [backend, frontend]) {
    write(root, `${directory}/en/${directory === backend ? 'api' : 'core'}.json`, '{"auth.login.required":"Sign in {name}","common.action.save":"Save"}')
    write(root, `${directory}/zh-CN/${directory === backend ? 'api' : 'core'}.json`, '{"auth.login.required":"登录 {name}","common.action.save":"保存"}')
  }
  return { root, stage: join(root, '.crowdin-stage') }
}

describe('translation resource contracts', () => {
  test('rejects duplicate keys instead of accepting JSON last-write-wins', () => {
    expect(() => parseCatalog('{"common.action.save":"Save","common.action.save":"Overwrite"}')).toThrow('duplicate')
  })
  test('rejects malformed JSON, sentence keys, numbered variants and non-string entries', () => {
    for (const text of ['{"common.action.save":"Save",}', '{"Save this item":"Save"}', '{"common.action.save2":"Save"}', '{"common.action.save":2}'])
      expect(() => parseCatalog(text)).toThrow()
  })
  test('accepts incomplete translations and validates nonempty parameter sets', () => {
    const source = { 'auth.login.required': 'Sign in {name}' }
    expect(() => validateCatalog(source, {}, 'test')).not.toThrow()
    expect(() => validateCatalog(source, { 'auth.login.required': '' }, 'test')).not.toThrow()
    expect(() => validateCatalog(source, { 'auth.login.required': '登录 {user}' }, 'test')).toThrow('parameters')
    expect(() => validateCatalog(source, { 'unknown.message': 'Unknown' }, 'test')).toThrow('unknown')
  })
})

describe('build-time Crowdin synchronization', () => {
  test('no configuration and download failures preserve the complete repository set', () => {
    const { root, stage } = fixture()
    const original = readFileSync(join(root, `${backend}/zh-CN/api.json`), 'utf8')
    expect(finishTranslations(root, stage, false, 'disabled')).toContain('not configured')
    expect(finishTranslations(root, stage, false, 'failure')).toContain('failed')
    expect(readFileSync(join(root, `${backend}/zh-CN/api.json`), 'utf8')).toBe(original)
  })
  test('valid downloads replace build inputs and untranslated strings remain absent for English fallback', () => {
    const { root, stage } = fixture()
    prepareTranslations(root, stage, false)
    write(stage, `${backend}/zh-CN/api.json`, '{"auth.login.required":"请登录 {name}"}')
    write(stage, `${frontend}/zh-CN/core.json`, '{"common.action.save":"保存更改"}')
    expect(finishTranslations(root, stage, false, 'success')).toContain('applied')
    expect(JSON.parse(readFileSync(join(root, `${backend}/zh-CN/api.json`), 'utf8'))).toEqual({ 'auth.login.required': '请登录 {name}' })
    expect(JSON.parse(readFileSync(join(root, `${frontend}/zh-CN/core.json`), 'utf8'))).toEqual({ 'common.action.save': '保存更改' })
    expect(JSON.parse(readFileSync(join(root, '.crowdin-stage.yml'), 'utf8')).base_path).toBe('.crowdin-stage')
  })
  test('one invalid download rejects the entire batch without applying valid sibling catalogs', () => {
    const { root, stage } = fixture()
    prepareTranslations(root, stage, false)
    write(stage, `${backend}/zh-CN/api.json`, '{"common.action.save":"最新保存"}')
    write(stage, `${frontend}/zh-CN/core.json`, '{"auth.login.required":"登录 {unknown}"}')
    expect(finishTranslations(root, stage, false, 'success')).toContain('validation failed')
    expect(JSON.parse(readFileSync(join(root, `${backend}/zh-CN/api.json`), 'utf8'))['common.action.save']).toBe('保存')
  })
  test('changed source and unexpected files reject a candidate', () => {
    const { root, stage } = fixture()
    prepareTranslations(root, stage, false)
    write(stage, `${backend}/en/api.json`, '{"common.action.save":"Changed"}')
    expect(finishTranslations(root, stage, false, 'success')).toContain('validation failed')
    prepareTranslations(root, stage, false)
    write(stage, `${backend}/zh-CN/unexpected.json`, '{}')
    expect(finishTranslations(root, stage, false, 'success')).toContain('validation failed')
  })
  test('backend-only synchronization leaves frontend resources untouched', () => {
    const { root, stage } = fixture()
    prepareTranslations(root, stage, true)
    const original = readFileSync(join(root, `${frontend}/zh-CN/core.json`), 'utf8')
    write(stage, `${backend}/zh-CN/api.json`, '{"common.action.save":"最新保存"}')
    expect(finishTranslations(root, stage, true, 'success')).toContain('applied')
    expect(readFileSync(join(root, `${frontend}/zh-CN/core.json`), 'utf8')).toBe(original)
    expect(JSON.parse(readFileSync(join(root, '.crowdin-stage.yml'), 'utf8')).files).toHaveLength(1)
  })
  test('invalid repository resources fail even when Crowdin is unavailable', () => {
    const { root, stage } = fixture()
    write(root, `${backend}/en/api.json`, '{"common.action.save":""}')
    expect(() => finishTranslations(root, stage, true, 'failure')).toThrow('empty English')
  })
})
