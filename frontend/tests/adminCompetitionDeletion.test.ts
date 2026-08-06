import { describe, expect, test } from 'bun:test'

const workspaceSource = await Bun.file(
  new URL('../src/components/admin/competitions/AdminCompetitionsWorkspace.vue', import.meta.url),
).text()
const dialogSource = await Bun.file(
  new URL('../src/components/admin/competitions/AdminCompetitionDeleteDialog.vue', import.meta.url),
).text()
const apiSource = await Bun.file(new URL('../src/api/noctf.ts', import.meta.url)).text()
const zh = await Bun.file(new URL('../src/locales/zh-CN.json', import.meta.url)).json()
const en = await Bun.file(new URL('../src/locales/en.json', import.meta.url)).json()

describe('admin competition deletion interaction', () => {
  test('separates archive from permanent deletion', () => {
    expect(workspaceSource).toContain('@select="openArchive(row.original)"')
    expect(workspaceSource).toContain('@select="openDelete(row.original)"')
    expect(workspaceSource).not.toContain('@click="openDelete(row.original)"')
    expect(workspaceSource).toContain(`row.original.status === 'Finished'`)
    expect(apiSource).toContain('generatedSdk.adminDeleteCompetition')
    expect(apiSource).toContain('generatedSdk.adminHardDeleteCompetition')
  })

  test('confirms only when a competition remains selected', () => {
    expect(workspaceSource).toContain('function deleteSelectedCompetition()')
    expect(workspaceSource).toContain('@confirm="deleteSelectedCompetition"')
    expect(workspaceSource).not.toContain('selectedComp!.id')
  })

  test('requires a second confirmation and explains physical deletion', () => {
    expect(dialogSource).toContain(`@click="emit('confirm')"`)
    expect(dialogSource).toContain(`mode: 'archive' | 'delete'`)
    expect(zh.admin.competitions.deleteDialogDescription).toContain('物理删除')
    expect(zh.admin.competitions.deleteDialogDescription).toContain('全局题库模板不会被删除')
    expect(en.admin.competitions.deleteDialogDescription).toContain('physically deleted')
    expect(zh.admin.competitions.archiveDialogDescription).toContain('可在之后恢复')
  })
})
