import { sourceFile } from './support/feature-source'
import { describe, expect, test } from 'bun:test'

const page = () => sourceFile(new URL('../app/pages/admin/competitions/[id]/challenges/[ccId].vue', import.meta.url)).text()

describe('admin competition challenge navigation', () => {
  test('places the challenge sections in the shared right sidebar', async () => {
    const source = await page()
    const css = await sourceFile(new URL('../app/components/views/app/settings-workspace.css', import.meta.url)).text()

    expect(source).toContain('<ChoiceSidebar')
    expect(source).toContain('v-model="activeSection"')
    expect(source).toContain('data-side="right"')
    expect(source).toContain(':items="sectionOptions"')
    expect(source).toContain('controls="competition-challenge-editor-content"')
    expect(source).not.toContain('<TabsList')
    expect(source).not.toContain('<TabsTrigger')
    expect(css).toContain('[data-challenge-editor-workspace] > [data-side=\'right\'] { grid-column: 2; grid-row: 1; }')
    expect(css).toContain('transform-origin: right center;')
  })

  test('lists every team and all paged challenge facts before allowing a judge to correct scoring', async () => {
    const source = await page()

    expect(source).toContain('adminListTeams')
    expect(source).toContain('adminListGameplayFacts')
    expect(source).toContain('getLeaderboardEndpoint')
    expect(source).toContain('getScoreboardSchemaEndpoint')
    expect(source).toContain('do {')
    expect(source).toContain('} while (cursor)')
    expect(source).toContain('seenCursors.has(cursor)')
    expect(source).toContain('v-for="row in scoringRows"')
    expect(source).toContain('v-if="canJudge"')
    expect(source).toContain('adminCreateManualAdjustment')
    expect(source).toContain('body: { teamId, competitionChallengeId: ccId, delta: adjustmentDelta.value }')
    expect(source).toContain('if (!teamId || !adjustmentValid.value || adjustmentPending.value) return')

    const catchStart = source.indexOf('catch (requestError)', source.indexOf('async function submitAdjustment'))
    const finallyStart = source.indexOf('finally', catchStart)
    const catchBody = source.slice(catchStart, finallyStart)
    expect(catchBody).not.toContain('adjustmentTarget.value = null')
    expect(catchBody).not.toContain('adjustmentDelta.value = 0')
  })
})

describe('challenge template list navigation', () => {
  test('restores the previous list before refreshing it in the background', async () => {
    const source = await sourceFile(
      new URL('../app/pages/admin/challenges/index.vue', import.meta.url),
    ).text()

    expect(source).toContain('const templateListCache = new Map<boolean, ChallengeTemplate[]>()')
    expect(source).toContain('const templates = ref<ChallengeTemplate[]>([...(templateListCache.get(includeDeleted.value) ?? [])])')
    expect(source).toContain('templateListCache.set(requestedIncludeDeleted, [...nextTemplates])')
    expect(source).toContain('templates.value = [...(templateListCache.get(value) ?? [])]')
    expect(source).toContain('if (generation !== loadGeneration) return')
    expect(source).toContain('v-if="loading && templates.length === 0"')
  })

  test('creates templates in a modal with a bounded direction selector', async () => {
    const index = await sourceFile(
      new URL('../app/pages/admin/challenges/index.vue', import.meta.url),
    ).text()
    const route = await Bun.file(
      new URL('../app/pages/admin/challenges/new.vue', import.meta.url),
    ).text()
    const dialog = await sourceFile(
      new URL('../app/features/admin/ChallengeTemplateCreateDialog.vue', import.meta.url),
    ).text()

    expect(index).toContain('@click="setCreateOpen(true)"')
    expect(index).toContain(':is="ChallengeTemplateCreateDialog"')
    expect(index).not.toContain('to="/admin/challenges/new"')
    expect(route).toContain("AdminChallengesIndexPage.vue")
    expect(dialog).toContain('<Dialog :open="open"')
    expect(dialog).toContain('<Select v-model="direction">')
    expect(dialog).toContain('v-for="option in directionOptions"')
    expect(dialog).toContain('challengeDirectionOptions')
    expect(dialog).not.toMatch(/<Input[\s\S]{0,160}v-model="direction"/)
    expect(dialog).toContain('class="px-1"')
    expect(dialog).toContain('overscroll-contain')
  })

  test('filters the challenge library by direction', async () => {
    const index = await sourceFile(
      new URL('../app/pages/admin/challenges/index.vue', import.meta.url),
    ).text()

    expect(index).toContain("const directionFilter = ref('all')")
    expect(index).toContain('const filteredTemplates = computed(')
    expect(index).toContain('v-model="directionFilter"')
    expect(index).toContain("$t('ui.allDirections')")
    expect(index).toContain('v-for="template in filteredTemplates"')
  })
})
