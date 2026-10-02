import { adminTeamPath, adminChallengePath, adminRuntimePath } from '~/features/admin/admin-navigation'
import { Download, RefreshCw, Trash2 } from '@lucide/vue'
import { toast } from 'vue-sonner'
import {
  adminDeleteRuntimeTrafficCapture,
  adminDownloadRuntimeTrafficCapture,
  adminExportRuntimeTrafficCaptures,
  adminListCompetitionChallenges,
  adminListRuntimeTrafficCaptures,
  adminListTeams,
} from '../../../../../api'
import type { NoCtfapiEndpointsAdministrationRuntimeRuntimeTrafficCaptureResponse } from '../../../../../api'
import { useOffsetPagination } from '../../../../../composables/useOffsetPagination'
import { useCompetitionAdmin } from '../../../../../lib/admin-competition'
import { downloadSdkFile } from '../../../../../utils/download'
import { formatBytes } from '../../../../../utils/labels'

type Capture = NoCtfapiEndpointsAdministrationRuntimeRuntimeTrafficCaptureResponse

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
    const { data, error } = await adminListRuntimeTrafficCaptures({
      path: { competitionId },
      query: {
        competitionChallengeId: filterChallenge.value === 'all' ? null : filterChallenge.value,
        teamId: filterTeam.value === 'all' ? null : filterTeam.value,
        runtimeInstanceId: filterRuntime.value || null,
        truncated: filterTruncated.value === 'all'
          ? null
          : filterTruncated.value === 'truncated',
        offset,
        limit,
        desc: true,
      },
    })
    if (error || !data) throw parseApiError(error)
    return { items: data.items ?? [], total: data.total ?? 0 }
  }, { initialPageSize: 50, initialDesc: true })

  async function loadReferences() {
    const [challenges, teams] = await Promise.all([
      adminListCompetitionChallenges({ path: { competitionId }, query: { includeDeleted: false } }),
      adminListTeams({ path: { competitionId }, query: { keyword: null, offset: 0, limit: 200, desc: false } }),
    ])
    challengeOptions.value = (challenges.data?.items ?? []).map(item => ({
      id: item.id!,
      title: item.title ?? item.id!,
    }))
    teamOptions.value = (teams.data?.items ?? []).map(item => ({
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

  function isSelected(runtimeId?: string): boolean {
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
    if (!item.runtimeInstanceId) return
    try {
      await downloadSdkFile(adminDownloadRuntimeTrafficCapture({
        path: {
          competitionId,
          runtimeInstanceId: item.runtimeInstanceId,
        },
        parseAs: 'blob',
      }), `runtime-${item.runtimeInstanceId}.pcapng`)
    }
    catch (error) {
      toast.error(parseApiError(error).message)
    }
  }

  async function exportSelected() {
    if (selectedRuntimeIds.value.length === 0 || exporting.value) return
    exporting.value = true
    try {
      await downloadSdkFile(adminExportRuntimeTrafficCaptures({
        path: { competitionId },
        body: { runtimeInstanceIds: selectedRuntimeIds.value },
        parseAs: 'blob',
      }), 'runtime-traffic.zip')
    }
    catch (error) {
      toast.error(parseApiError(error).message)
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
      const { error } = await adminDeleteRuntimeTrafficCapture({
        path: { competitionId, runtimeInstanceId: runtimeId },
      })
      if (error) throw error
      toast.success(translate('runtime.captureDeleted'))
      deleteTarget.value = null
      selectedRuntimeIds.value = selectedRuntimeIds.value.filter(id => id !== runtimeId)
      await pagination.loadPage(pagination.page.value)
    }
    catch (error) {
      toast.error(parseApiError(error).message)
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
