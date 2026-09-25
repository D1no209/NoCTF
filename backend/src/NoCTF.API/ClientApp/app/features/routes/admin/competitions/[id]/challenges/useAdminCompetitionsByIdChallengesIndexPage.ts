import { proxyRefs } from 'vue'

import { Plus } from '@lucide/vue'
import { toast } from 'vue-sonner'
import { adminChallengeBankListTemplates, adminCreateCompetitionChallenge, adminDeleteCompetitionChallenge, adminListCompetitionChallenges, adminPatchCompetitionChallenge, adminRestoreCompetitionChallenge } from '../../../../../../api'
import type { NoCtfapiEndpointsAdministrationChallengeBankChallengeTemplateSummaryResponse, NoCtfapiEndpointsChallengesChallengeSummaryResponse } from '../../../../../../api'
import { useCompetitionAdmin } from '../../../../../../lib/admin-competition'
import { competitionChallengeConflictMessage } from '../../../../../../lib/competition-challenge-conflict'

type ChallengeStatusFilter = 'all' | 'published' | 'unpublished' | 'deleted'

/** Owns state, effects and commands for AdminCompetitionsByIdChallengesIndexPage. */
export function useAdminCompetitionsByIdChallengesIndexPage() {
  const { competitionId, competition, canWrite } = useCompetitionAdmin()

  const items = ref<NoCtfapiEndpointsChallengesChallengeSummaryResponse[]>([])

  const loading = ref(true)

  const error = ref<string | null>(null)

  const includeDeleted = ref(false)

  const search = ref('')

  const directionFilter = ref('all')

  const statusFilter = ref<ChallengeStatusFilter>('all')

  const directionOptions = computed(() => [...new Set(items.value
    .map(item => item.direction)
    .filter((direction): direction is string => Boolean(direction)))]
    .map(value => ({ value, label: directionLabel(value) }))
    .sort((left, right) => left.label.localeCompare(right.label)))

  const filteredItems = computed(() => {
    const keyword = search.value.trim().toLocaleLowerCase()
    return items.value.filter((item) => {
      if (directionFilter.value !== 'all' && item.direction !== directionFilter.value) return false
      if (statusFilter.value === 'published' && (item.deletedAt || !item.isPublished)) return false
      if (statusFilter.value === 'unpublished' && (item.deletedAt || item.isPublished)) return false
      if (statusFilter.value === 'deleted' && !item.deletedAt) return false
      if (!keyword) return true
      return [item.title, item.customTitle, item.direction, directionLabel(item.direction)]
        .some(value => value?.toLocaleLowerCase().includes(keyword))
    })
  })

  const pendingId = ref<string | null>(null)

  async function load() {
    loading.value = true
    error.value = null
    const { data, error: e } = await adminListCompetitionChallenges({
      path: { competitionId },
      query: { includeDeleted: includeDeleted.value },
    })
    if (e) error.value = parseApiError(e).message
    else items.value = [...(data?.items ?? [])].sort((a, b) => (a.order ?? 0) - (b.order ?? 0))
    loading.value = false
  }

  watch(includeDeleted, (value) => {
    if (!value && statusFilter.value === 'deleted') statusFilter.value = 'all'
    void load()
  })

  watch(directionOptions, (options) => {
    if (directionFilter.value !== 'all'
      && !options.some(option => option.value === directionFilter.value)) {
      directionFilter.value = 'all'
    }
  })

  onMounted(load)

  const addOpen = ref(false)

  const templates = ref<NoCtfapiEndpointsAdministrationChallengeBankChallengeTemplateSummaryResponse[]>([])

  const templatesLoading = ref(false)

  const selectedTemplateId = ref<string>('')

  const templateSearch = ref('')

  const hideAddedTemplates = ref(false)

  const newCustomTitle = ref('')

  const newOrder = ref(0)

  const adding = ref(false)

  const addError = ref<string | null>(null)

  const modeTemplates = computed(() =>
    templates.value.filter(t => t.mode === competition.value?.mode && !t.deletedAt),
  )

  const addedTemplateIds = computed(() => new Set(
    items.value
      .filter(item => !item.deletedAt && item.challengeId)
      .map(item => item.challengeId!),
  ))

  const visibleModeTemplates = computed(() => {
    const search = templateSearch.value.trim().toLocaleLowerCase()
    return modeTemplates.value.filter((template) => {
      if (hideAddedTemplates.value && template.id && addedTemplateIds.value.has(template.id)) return false
      if (!search) return true
      return [template.title, template.direction]
        .some(value => value?.toLocaleLowerCase().includes(search))
    })
  })

  watch(hideAddedTemplates, (hidden) => {
    if (hidden && addedTemplateIds.value.has(selectedTemplateId.value)) selectedTemplateId.value = ''
  })

  async function openAdd() {
    addOpen.value = true
    addError.value = null
    selectedTemplateId.value = ''
    templateSearch.value = ''
    hideAddedTemplates.value = false
    newCustomTitle.value = ''
    newOrder.value = (items.value.filter(i => !i.deletedAt).map(i => i.order ?? 0).reduce((m, o) => Math.max(m, o), 0) || 0) + 1
    templatesLoading.value = true
    const { data, error: e } = await adminChallengeBankListTemplates({ query: { includeDeleted: false, direction: null, keyword: null, offset: 0, limit: 200, desc: false } })
    if (e) addError.value = parseApiError(e).message
    else templates.value = data?.items ?? []
    templatesLoading.value = false
  }

  async function addChallenge() {
    if (!selectedTemplateId.value) {
      addError.value = translate("ui.pleaseSelectAQuestionBankTemplate")
      return
    }
    adding.value = true
    addError.value = null
    try {
      const { error } = await adminCreateCompetitionChallenge({
        path: { competitionId },
        body: {
          challengeId: selectedTemplateId.value,
          customTitle: newCustomTitle.value.trim() || null,
          order: newOrder.value,
        },
      })
      if (error) {
        addError.value = competitionChallengeConflictMessage(error) ?? parseApiError(error).message
        return
      }
      toast.success(translate("ui.questionHasBeenAdded"))
      addOpen.value = false
      await load()
    }
    catch (e) {
      addError.value = competitionChallengeConflictMessage(e) ?? parseApiError(e).message
    }
    finally {
      adding.value = false
    }
  }

  const deleteTarget = ref<NoCtfapiEndpointsChallengesChallengeSummaryResponse | null>(null)

  const deletePending = ref(false)

  const deleteError = ref<string | null>(null)

  function closeDeleteDialog(open: boolean) {
    if (!open && !deletePending.value) {
      deleteTarget.value = null
      deleteError.value = null
    }
  }

  function beginDeleteChallenge(c: NoCtfapiEndpointsChallengesChallengeSummaryResponse) {
    deleteTarget.value = c
    deleteError.value = null
  }

  async function removeChallenge() {
    const target = deleteTarget.value
    if (!target?.id || deletePending.value) return
    deletePending.value = true
    deleteError.value = null
    pendingId.value = target.id
    try {
      const { error } = await adminDeleteCompetitionChallenge({
        path: { competitionId, competitionChallengeId: target.id },
      })
      if (error) throw error
      toast.success(translate("ui.questionHasBeenDeleted"))
      deleteTarget.value = null
      deleteError.value = null
      await load()
    }
    catch (e) {
      deleteError.value = parseApiError(e).message
      toast.error(deleteError.value)
    }
    finally {
      deletePending.value = false
      pendingId.value = null
    }
  }

  async function restoreChallenge(c: NoCtfapiEndpointsChallengesChallengeSummaryResponse) {
    if (!c.id) return
    pendingId.value = c.id
    try {
      const { error } = await adminRestoreCompetitionChallenge({
        path: { competitionId, competitionChallengeId: c.id },
      })
      if (error) throw error
      toast.success(translate("ui.questionHasBeenRestored"))
      await load()
    }
    catch (e) {
      toastWriteError(e)
    }
    finally {
      pendingId.value = null
    }
  }

  async function setChallengePublished(
    challenge: NoCtfapiEndpointsChallengesChallengeSummaryResponse,
    published: boolean,
  ) {
    if (!challenge.id || challenge.deletedAt || pendingId.value !== null) return
    pendingId.value = challenge.id
    try {
      const { data, error: requestError } = await adminPatchCompetitionChallenge({
        path: { competitionId, competitionChallengeId: challenge.id },
        body: {
          presentation: {
            customTitle: challenge.customTitle ?? null,
            order: challenge.order ?? 0,
            isPublished: published,
          },
        },
      })
      if (requestError) throw requestError
      const updated = data?.challenge
      if (updated) {
        items.value = items.value.map(item => item.id === updated.id ? updated : item)
      }
      else {
        await load()
      }
      toast.success(translate(
        published ? 'ui.challengeWasPublished' : 'ui.challengeWasUnpublished',
        { challenge: challenge.title ?? challenge.customTitle ?? '-' },
      ))
    }
    catch (e) {
      toast.error(competitionChallengeConflictMessage(e) ?? parseApiError(e).message)
    }
    finally {
      pendingId.value = null
    }
  }

  const viewBindings = {
      Plus,
      competitionId,
      competition,
      canWrite,
      items,
      loading,
      error,
      includeDeleted,
      search,
      directionFilter,
      directionOptions,
      statusFilter,
      filteredItems,
      pendingId,
      addOpen,
      templatesLoading,
      selectedTemplateId,
      templateSearch,
      hideAddedTemplates,
      newCustomTitle,
      newOrder,
      adding,
      addError,
      modeTemplates,
      visibleModeTemplates,
      openAdd,
      addChallenge,
      deleteTarget,
      deletePending,
      deleteError,
      closeDeleteDialog,
      beginDeleteChallenge,
      removeChallenge,
      restoreChallenge,
      setChallengePublished,
    }
  const viewState = proxyRefs(viewBindings)

  function onClickAddOpen(value: typeof viewState.addOpen) {
    viewState.addOpen = value
  }

  return { ...viewBindings, onClickAddOpen }
}

export type AdminCompetitionsByIdChallengesIndexPageViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useAdminCompetitionsByIdChallengesIndexPage>>>
