
import { api } from '../../../../../../lib/api'
import { message as describeMessage } from '../../../../../../utils/i18n'
import type { UiMessage } from '../../../../../../utils/i18n'
import { challengeTagOptions, uniqueTags, validChallengeTags } from '~/lib/challenge-tags'
import { proxyRefs } from 'vue'

import { Plus } from '@lucide/vue'
import { toast } from '../../../../../../utils/message-toast'

import type { NoCTFAPIEndpointsAdministrationChallengeBankChallengeTemplateSummaryResponse, NoCTFAPIEndpointsChallengesChallengeSummaryResponse } from '../../../../../../api/models'
import { useCompetitionAdmin } from '../../../../../../lib/admin-competition'
import { competitionChallengeConflictMessage } from '../../../../../../lib/competition-challenge-conflict'

type ChallengeStatusFilter = 'all' | 'published' | 'unpublished' | 'deleted'

/** Owns state, effects and commands for AdminCompetitionsByIdChallengesIndexPage. */
export function useAdminCompetitionsByIdChallengesIndexPage() {
  const { competitionId, competition, canWrite } = useCompetitionAdmin()

  const items = ref<NoCTFAPIEndpointsChallengesChallengeSummaryResponse[]>([])

  const loading = ref(true)

  const error = ref<UiMessage | null>(null)

  const includeDeleted = ref(false)

  const search = ref('')

  const directionFilter = ref('all')

  const statusFilter = ref<ChallengeStatusFilter>('all')

  const directionOptions = computed(() => [...new Set(items.value
    .map(item => item.direction)
    .filter((direction): direction is string => Boolean(direction)))]
    .map(value => ({ value, label: value }))
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

  const page = ref(1)

  const pageLimit = ref(10)

  const total = computed(() => filteredItems.value.length)

  const pageCount = computed(() => Math.max(1, Math.ceil(total.value / pageLimit.value)))

  const pageItems = computed(() => filteredItems.value.slice(
    (page.value - 1) * pageLimit.value,
    page.value * pageLimit.value,
  ))

  function loadPage(targetPage: number): void {
    if (!Number.isFinite(targetPage)) return
    page.value = Math.min(pageCount.value, Math.max(1, Math.floor(targetPage)))
  }

  function setPageSize(value: number): void {
    if (!Number.isInteger(value) || value < 1 || value === pageLimit.value) return
    pageLimit.value = value
    page.value = 1
  }

  watch([search, directionFilter, statusFilter, includeDeleted], () => { page.value = 1 })
  watch(pageCount, count => { if (page.value > count) page.value = count })

  const pendingId = ref<string | null>(null)

  async function load() {
    loading.value = true
    error.value = null
    let e: unknown;
    const data = await api.api.v1.admin.competitions.byCompetitionId(competitionId).challenges.get({ queryParameters: { includeDeleted: includeDeleted.value } }).catch(cause => { e = cause; return undefined });
    if (e) error.value = parseApiError(e).displayMessage
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

  const templates = ref<NoCTFAPIEndpointsAdministrationChallengeBankChallengeTemplateSummaryResponse[]>([])

  const templatesLoading = ref(false)

  const selectedTemplateId = ref<string>('')

  const templateSearch = ref('')

  const hideAddedTemplates = ref(false)

  const newCustomTitle = ref('')

  const newOrder = ref(0)

  const adding = ref(false)

  const addError = ref<UiMessage | null>(null)

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

  const newTags = ref<string[]>([])
  const tagOptions = computed(() => challengeTagOptions(items.value.filter(item => !item.deletedAt)))
  function updateNewTags(tags: string[]) {
    if (!validChallengeTags(tags)) { toast.error(describeMessage('challengeTags.invalid')); return }
    newTags.value = uniqueTags(tags)
  }

  async function openAdd() {
    addOpen.value = true
    addError.value = null
    selectedTemplateId.value = ''
    templateSearch.value = ''
    hideAddedTemplates.value = false
    newCustomTitle.value = ''
    newTags.value = []
    newOrder.value = (items.value.filter(i => !i.deletedAt).map(i => i.order ?? 0).reduce((m, o) => Math.max(m, o), 0) || 0) + 1
    templatesLoading.value = true
    let e: unknown;
    const data = await api.api.v1.admin.challenges.get({ queryParameters: { includeDeleted: false, direction: undefined, keyword: undefined, offset: 0, limit: 200, desc: false } }).catch(cause => { e = cause; return undefined });
    if (e) addError.value = parseApiError(e).displayMessage
    else templates.value = data?.items ?? []
    templatesLoading.value = false
  }

  async function addChallenge() {
    if (!selectedTemplateId.value) {
      addError.value = describeMessage("administration.competitionsBy.description.selectQuestionBankTemplate")
      return
    }
    adding.value = true
    addError.value = null
    try {
      let error: unknown;
      await api.api.v1.admin.competitions.byCompetitionId(competitionId).challenges.post({
          challengeId: selectedTemplateId.value,
          customTitle: newCustomTitle.value.trim() || null,
          order: newOrder.value,
          tags: newTags.value,
        }).catch(cause => { error = cause; return undefined });
      if (error) {
        addError.value = competitionChallengeConflictMessage(error) ?? parseApiError(error).displayMessage
        return
      }
      toast.success(describeMessage("administration.label.questionAdded"))
      addOpen.value = false
      await load()
    }
    catch (e) {
      addError.value = competitionChallengeConflictMessage(e) ?? parseApiError(e).displayMessage
    }
    finally {
      adding.value = false
    }
  }

  const deleteTarget = ref<NoCTFAPIEndpointsChallengesChallengeSummaryResponse | null>(null)

  const deletePending = ref(false)

  const deleteError = ref<UiMessage | null>(null)

  function closeDeleteDialog(open: boolean) {
    if (!open && !deletePending.value) {
      deleteTarget.value = null
      deleteError.value = null
    }
  }

  function beginDeleteChallenge(c: NoCTFAPIEndpointsChallengesChallengeSummaryResponse) {
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

      await api.api.v1.admin.competitions.byCompetitionId(competitionId).challenges.byCompetitionChallengeId(target.id).delete();
      toast.success(describeMessage("administration.label.questionDeleted"))
      deleteTarget.value = null
      deleteError.value = null
      await load()
    }
    catch (e) {
      deleteError.value = parseApiError(e).displayMessage
      toast.error(deleteError.value)
    }
    finally {
      deletePending.value = false
      pendingId.value = null
    }
  }

  async function restoreChallenge(c: NoCTFAPIEndpointsChallengesChallengeSummaryResponse) {
    if (!c.id) return
    pendingId.value = c.id
    try {
      await api.api.v1.admin.competitions.byCompetitionId(competitionId).challenges.byCompetitionChallengeId(c.id).restore.post();
      toast.success(describeMessage("administration.label.questionRestored"))
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
    challenge: NoCTFAPIEndpointsChallengesChallengeSummaryResponse,
    published: boolean,
  ) {
    if (!challenge.id || challenge.deletedAt || pendingId.value !== null) return
    pendingId.value = challenge.id
    try {
      const data = await api.api.v1.admin.competitions.byCompetitionId(competitionId).challenges.byCompetitionChallengeId(challenge.id).patch({
          presentation: {
            customTitle: challenge.customTitle ?? null,
            order: challenge.order ?? 0,
            isPublished: published,
          },
        });
      const updated = data?.challenge
      if (updated) {
        items.value = items.value.map(item => item.id === updated.id ? updated : item)
      }
      else {
        await load()
      }
      toast.success(describeMessage(
        published ? 'common.label.challengeWasPublished' : 'common.label.challengeWasUnpublished',
        { challenge: challenge.title ?? challenge.customTitle ?? '-' },
      ))
    }
    catch (e) {
      toast.error(competitionChallengeConflictMessage(e) ?? parseApiError(e).displayMessage)
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
      pageItems,
      page,
      pageLimit,
      pageCount,
      total,
      loadPage,
      setPageSize,
      pendingId,
      addOpen,
      templatesLoading,
      selectedTemplateId,
      templateSearch,
      hideAddedTemplates,
      newCustomTitle,
      newTags, tagOptions, updateNewTags,
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
