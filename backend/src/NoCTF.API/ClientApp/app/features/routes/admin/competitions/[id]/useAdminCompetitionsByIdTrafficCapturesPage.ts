
import { api, nativeResponse } from '../../../../../lib/api'
import { message as describeMessage } from '../../../../../utils/i18n'
import { adminTeamPath, adminChallengePath, adminRuntimePath } from '~/features/admin/admin-navigation'
import { Download, RefreshCw, Trash2 } from '@lucide/vue'
import { toast } from '../../../../../utils/message-toast'

import type { NoCTFAPIEndpointsAdministrationRuntimeRuntimeTrafficCaptureResponse } from '../../../../../api/models'
import { useOffsetPagination } from '../../../../../composables/useOffsetPagination'
import { useCompetitionAdmin } from '../../../../../lib/admin-competition'
import { downloadSdkFile } from '../../../../../utils/download'
import { formatBytes } from '../../../../../utils/labels'

type Capture = NoCTFAPIEndpointsAdministrationRuntimeRuntimeTrafficCaptureResponse

/** Owns state, effects and commands for the competition traffic-capture monitor. */
export function useAdminCompetitionsByIdTrafficCapturesPage() {
  const { competitionId, canWrite, competition } = useCompetitionAdmin()
  const challengeOptions = ref<{ id: string; title: string }[]>([])
  const teamOptions = ref<{ id: string; name: string }[]>([])
  const filterChallenge = ref('all')
  const filterTeam = ref('all')
  const filterRuntime = ref('')
  const filterTruncated = ref('all')
  const selectedRuntimeIds = ref<string[]>([])
  const exporting = ref(false)
  const deleteTarget = ref<Capture | null>(null)
  const deleting = ref(false)

  const pagination = useOffsetPagination<Capture>(async ({ offset, limit }) => {
    let error: unknown;
    const data = await api.api.v1.admin.competitions.byCompetitionId(competitionId).trafficCaptures.get({ queryParameters: {
        competitionChallengeId: filterChallenge.value === 'all' ? undefined : filterChallenge.value,
        teamId: filterTeam.value === 'all' ? undefined : filterTeam.value,
        runtimeInstanceId: filterRuntime.value || undefined,
        truncated: (filterTruncated.value === 'all'
          ? null
          : filterTruncated.value === 'truncated') ?? undefined,
        offset,
        limit,
        desc: true,
      } }).catch(cause => { error = cause; return undefined });
    if (error || !data) throw parseApiError(error)
    return { items: data.items ?? [], total: data.total ?? 0 }
  }, { initialPageSize: 50, initialDesc: true })

  async function loadReferences() {
    const settledRequests = await Promise.allSettled([
      api.api.v1.admin.competitions.byCompetitionId(competitionId).challenges.get({ queryParameters: { includeDeleted: false } }),
      api.api.v1.admin.competitions.byCompetitionId(competitionId).teams.get({ queryParameters: { keyword: undefined, offset: 0, limit: 200, desc: false } }),
    ]);
    const challenges = settledRequests[0].status === 'fulfilled' ? settledRequests[0].value : undefined;
    const teams = settledRequests[1].status === 'fulfilled' ? settledRequests[1].value : undefined;

    challengeOptions.value = (challenges?.items ?? []).map(item => ({
      id: item.id!,
      title: item.title ?? item.id!,
    }))
    teamOptions.value = (teams?.items ?? []).map(item => ({
      id: item.id!,
      name: item.name ?? item.id!,
    }))
  }

  async function applyFilters() {
    selectedRuntimeIds.value = []
    pagination.reset()
    await pagination.loadPage(1)
  }

  function toggleSelected(runtimeId: string, checked: boolean) {
    selectedRuntimeIds.value = checked
      ? [...new Set([...selectedRuntimeIds.value, runtimeId])]
      : selectedRuntimeIds.value.filter(id => id !== runtimeId)
  }

  function isSelected(runtimeId?: string | null): boolean {
    return !!runtimeId && selectedRuntimeIds.value.includes(runtimeId)
  }

  function onToggleSelected(item: Capture, value: boolean | 'indeterminate') {
    if (item.runtimeInstanceId)
      toggleSelected(item.runtimeInstanceId, value === true)
  }

  function openDelete(item: Capture) {
    deleteTarget.value = item
  }

  function setDeleteOpen(open: boolean) {
    if (!open && !deleting.value) deleteTarget.value = null
  }

  async function downloadCapture(item: Capture) {
    const runtimeInstanceId = item.runtimeInstanceId
    if (!runtimeInstanceId) return
    try {
      await downloadSdkFile(nativeResponse(responseOptions => api.api.v1.admin.competitions.byCompetitionId(competitionId).trafficCaptures.byRuntimeInstanceId(runtimeInstanceId).file.get({ options: [...responseOptions] })), `runtime-${item.runtimeInstanceId}.pcapng`)
    }
    catch (error) {
      toast.error(parseApiError(error).displayMessage)
    }
  }

  async function exportSelected() {
    if (selectedRuntimeIds.value.length === 0 || exporting.value) return
    exporting.value = true
    try {
      await downloadSdkFile(nativeResponse(responseOptions => api.api.v1.admin.competitions.byCompetitionId(competitionId).trafficCaptures.exportEscaped.post({ runtimeInstanceIds: selectedRuntimeIds.value }, { options: [...responseOptions] })), 'runtime-traffic.zip')
    }
    catch (error) {
      toast.error(parseApiError(error).displayMessage)
    }
    finally {
      exporting.value = false
    }
  }

  async function confirmDelete() {
    const runtimeId = deleteTarget.value?.runtimeInstanceId
    if (!runtimeId || deleting.value) return
    deleting.value = true
    try {

      await api.api.v1.admin.competitions.byCompetitionId(competitionId).trafficCaptures.byRuntimeInstanceId(runtimeId).delete();
      toast.success(describeMessage('runtime.captureDeleted'))
      deleteTarget.value = null
      selectedRuntimeIds.value = selectedRuntimeIds.value.filter(id => id !== runtimeId)
      await pagination.loadPage(pagination.page.value)
    }
    catch (error) {
      toast.error(parseApiError(error).displayMessage)
    }
    finally {
      deleting.value = false
    }
  }

  function challengeTitle(item: Capture): string {
    return item.challengeTitle
      ?? challengeOptions.value.find(option => option.id === item.competitionChallengeId)?.title
      ?? item.competitionChallengeId
      ?? '-'
  }

  function teamName(item: Capture): string {
    return item.teamName
      ?? teamOptions.value.find(option => option.id === item.teamId)?.name
      ?? item.teamId
      ?? translate('runtime.sharedEnvironment')
  }

  onMounted(async () => {
    await Promise.all([loadReferences(), pagination.loadPage(1)])
  })

  return {
      adminTeamPath, adminChallengePath, adminRuntimePath,
      competitionId,
    Download,
    RefreshCw,
    Trash2,
    canWrite,
    competition,
    challengeOptions,
    teamOptions,
    filterChallenge,
    filterTeam,
    filterRuntime,
    filterTruncated,
    selectedRuntimeIds,
    exporting,
    deleteTarget,
    deleting,
    items: pagination.items,
    loading: pagination.loading,
    listError: pagination.error,
    page: pagination.page,
    pageCount: pagination.pageCount,
    total: pagination.total,
    pageLimit: pagination.limit,
    loadPage: pagination.loadPage,
    setPageSize: pagination.setPageSize,
    formatBytes,
    adminFormatDateTime,
    applyFilters,
    toggleSelected,
    onToggleSelected,
    isSelected,
    openDelete,
    setDeleteOpen,
    downloadCapture,
    exportSelected,
    confirmDelete,
    challengeTitle,
    teamName,
  }
}

export type AdminCompetitionsByIdTrafficCapturesPageViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useAdminCompetitionsByIdTrafficCapturesPage>>>
