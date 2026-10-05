import { sourceFile } from './support/feature-source'
import { describe, expect, test } from 'bun:test'

describe('competition challenge deletion', () => {
  test('allows publication to be toggled directly from the challenge list', async () => {
    const page = await sourceFile(
      new URL('../app/pages/admin/competitions/[id]/challenges/index.vue', import.meta.url),
    ).text()

    expect(page).toContain('async function setChallengePublished(')
    expect(page).toContain('adminPatchCompetitionChallenge({')
    expect(page).toContain('isPublished: published')
    expect(page).toContain('@update:model-value="setChallengePublished(c, $event)"')
    expect(page).toContain("published ? 'common.label.challengeWasPublished' : 'common.label.challengeWasUnpublished'")
  })

  test('can search templates and hide templates already added to the competition', async () => {
    const page = await sourceFile(
      new URL('../app/pages/admin/competitions/[id]/challenges/index.vue', import.meta.url),
    ).text()

    expect(page).toContain("const templateSearch = ref('')")
    expect(page).toContain('const hideAddedTemplates = ref(false)')
    expect(page).toContain('const addedTemplateIds = computed(() => new Set(')
    expect(page).toContain('const visibleModeTemplates = computed(() => {')
    expect(page).toContain('addedTemplateIds.value.has(template.id)')
    expect(page).toContain('v-model="templateSearch"')
    expect(page).toContain('v-model="hideAddedTemplates"')
    expect(page).toContain('v-for="t in visibleModeTemplates"')
  })

  test('filters competition challenge rows by keyword direction and status', async () => {
    const page = await sourceFile(
      new URL('../app/pages/admin/competitions/[id]/challenges/index.vue', import.meta.url),
    ).text()

    expect(page).toContain("const search = ref('')")
    expect(page).toContain("const directionFilter = ref('all')")
    expect(page).toContain("const statusFilter = ref<ChallengeStatusFilter>('all')")
    expect(page).toContain('const filteredItems = computed(() => {')
    expect(page).toContain("statusFilter.value === 'published'")
    expect(page).toContain("statusFilter.value === 'unpublished'")
    expect(page).toContain("statusFilter.value === 'deleted'")
    expect(page).toContain('v-model="search"')
    expect(page).toContain('v-model="directionFilter"')
    expect(page).toContain('v-model="statusFilter"')
    expect(page).toContain('v-for="c in pageItems"')
    expect(page).toContain("$t('administration.label.matchingCompetitionChallenges')")
  })

  test('paginates matching rows and resets the page when filters change', async () => {
    const page = await sourceFile(
      new URL('../app/pages/admin/competitions/[id]/challenges/index.vue', import.meta.url),
    ).text()

    expect(page).toContain('const pageLimit = ref(10)')
    expect(page).toContain('const total = computed(() => filteredItems.value.length)')
    expect(page).toContain('const pageItems = computed(() => filteredItems.value.slice(')
    expect(page).toContain('watch([search, directionFilter, statusFilter, includeDeleted]')
    expect(page).toContain('watch(pageCount, count => { if (page.value > count) page.value = count })')
    expect(page).toContain('<OffsetPagination')
    expect(page).toContain('@update:page="loadPage"')
    expect(page).toContain('@update:limit="setPageSize"')
  })

  test('keeps the selected challenge while deletion is pending or fails', async () => {
    const page = await sourceFile(
      new URL('../app/pages/admin/competitions/[id]/challenges/index.vue', import.meta.url),
    ).text()

    const deleteSection = page.slice(page.indexOf('function closeDeleteDialog'))

    expect(deleteSection).toContain('function closeDeleteDialog(open: boolean)')
    expect(deleteSection).toContain('if (!open && !deletePending.value)')
    expect(deleteSection).toContain('function beginDeleteChallenge(c: NoCtfapiEndpointsChallengesChallengeSummaryResponse)')
    expect(deleteSection).toContain('@click="beginDeleteChallenge(c)"')
    expect(deleteSection).toContain('const target = deleteTarget.value')
    expect(deleteSection).toContain('if (!target?.id || deletePending.value) return')
    expect(deleteSection).toContain('deletePending.value = true')
    expect(deleteSection).toContain('deleteError.value = parseApiError(e).displayMessage')
    expect(deleteSection).toContain('<Alert v-if="deleteError" variant="destructive">')
    expect(deleteSection).toContain(':disabled="deletePending"')
    expect(deleteSection).toContain('@click="removeChallenge"')
    expect(deleteSection).not.toMatch(/<AlertDialogAction[\s\S]*?removeChallenge/)
  })

  test('calls the generated delete SDK without a stale-write token', async () => {
    const page = await sourceFile(
      new URL('../app/pages/admin/competitions/[id]/challenges/index.vue', import.meta.url),
    ).text()

    const deleteHandler = page.slice(page.indexOf('async function removeChallenge()'), page.indexOf('async function restoreChallenge'))

    expect(deleteHandler).toContain('adminDeleteCompetitionChallenge')
    expect(deleteHandler).toContain('path: { competitionId, competitionChallengeId: target.id }')
    expect(deleteHandler).not.toContain('query:')
    expect(deleteHandler).toContain("toast.success(describeMessage(\"administration.label.questionDeleted\"))")
    expect(deleteHandler).toContain('deleteTarget.value = null')
  })
})
