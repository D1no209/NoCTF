
import { api } from '../../../../../lib/api'
import { message as describeMessage } from '../../../../../utils/i18n'
import type { UiMessage } from '../../../../../utils/i18n'
import { computed, onMounted, onScopeDispose, ref, watch } from 'vue'
import { toast } from '../../../../../utils/message-toast'

import type { NoCTFAPIEndpointsAdministrationCompetitionsAnnouncementAudience, NoCTFAPIEndpointsAdministrationCompetitionsManagedAnnouncementResponse } from '~/api/models'
import { useCompetitionAdmin } from '~/lib/admin-competition'
import { useOffsetPagination } from '~/composables/useOffsetPagination'
import { watchNotifications } from '~/composables/useNotificationHub'
import { createTrailingRefresh } from '~/lib/latest-page-refresh'
import { adminUserPath } from '~/features/admin/admin-navigation'
import { adminFormatDateTime } from '~/utils/admin-format'
import { parseApiError } from '~/utils/api-error'

type Announcement = NoCTFAPIEndpointsAdministrationCompetitionsManagedAnnouncementResponse
type Audience = NoCTFAPIEndpointsAdministrationCompetitionsAnnouncementAudience

export function useAdminCompetitionsByIdAnnouncementsPage() {
  const { competitionId, canJudge } = useCompetitionAdmin()
  const includeWithdrawn = ref(false)
  const editingId = ref<string | null>(null)
  const title = ref('')
  const body = ref('')
  const audience = ref<Audience>('Participants')
  const saving = ref(false)
  const formError = ref<UiMessage | null>(null)
  const deleting = ref(false)
  const deleteTarget = ref<Announcement | null>(null)
  const deleteError = ref<UiMessage | null>(null)
  const previewTarget = ref<Announcement | null>(null)
  const valid = computed(() => title.value.trim().length > 0 && title.value.trim().length <= 160
    && body.value.trim().length > 0 && body.value.trim().length <= 16_000)

  const pagination = useOffsetPagination<Announcement>(async ({ offset, limit, desc }) => {
    let error: unknown;
    const data = await api.api.v1.admin.competitions.byCompetitionId(competitionId).announcements.get({ queryParameters: { includeWithdrawn: includeWithdrawn.value, offset, limit, desc } }).catch(cause => { error = cause; return undefined });
    if (error || !data) throw parseApiError(error, describeMessage('announcements.loadFailed'))
    return { items: data.items ?? [], total: data.total ?? 0 }
  }, { initialDesc: true })
  const refresh = createTrailingRefresh(() => pagination.loadPage())
  watch(includeWithdrawn, () => { pagination.reset(); void pagination.loadPage(1) })
  watch(pagination.items, (items) => {
    if (!previewTarget.value?.id) return
    previewTarget.value = items.find(item => item.id === previewTarget.value?.id) ?? null
  })

  function resetEditor() {
    if (saving.value) return
    editingId.value = null
    title.value = ''
    body.value = ''
    audience.value = 'Participants'
    formError.value = null
  }
  function edit(item: Announcement) {
    if (!canJudge.value || saving.value || deleting.value || item.state !== 'Published' || !item.id) return
    editingId.value = item.id
    title.value = item.title ?? ''
    body.value = item.body ?? ''
    audience.value = item.audience ?? 'Participants'
    formError.value = null
    document.getElementById('competition-announcement-editor')?.scrollIntoView({ block: 'start' })
  }
  async function save() {
    if (!canJudge.value || saving.value || deleting.value) return
    if (!valid.value) { formError.value = describeMessage('announcements.invalidContent'); return }
    const id = editingId.value
    saving.value = true
    formError.value = null
    try {
      const content = { title: title.value.trim(), body: body.value.trim() }
      let resultError: unknown;
      await (id
        ? api.api.v1.admin.competitions.byCompetitionId(competitionId).announcements.byAnnouncementId(id).patch(content)
        : api.api.v1.admin.competitions.byCompetitionId(competitionId).announcements.post({ ...content, audience: audience.value })).catch(cause => { resultError = cause; return undefined });
      if (resultError) throw resultError
      toast.success(describeMessage(id ? 'announcements.updated' : 'administration.competitionsBy.label.competitionNoticeReleased'))
      saving.value = false
      resetEditor()
      if (!id) pagination.reset()
      await pagination.loadPage(id ? pagination.page.value : 1)
    }
    catch (error) {
      formError.value = parseApiError(error, describeMessage('announcements.saveFailed')).displayMessage
    }
    finally { saving.value = false }
  }
  function requestDelete(item: Announcement) {
    if (!canJudge.value || saving.value || deleting.value || item.state !== 'Published') return
    deleteError.value = null
    deleteTarget.value = item
  }
  function setDeleteOpen(open: boolean) {
    if (!open && !deleting.value) { deleteTarget.value = null; deleteError.value = null }
  }
  async function remove() {
    const target = deleteTarget.value
    if (!target?.id || !canJudge.value || deleting.value) return
    deleting.value = true
    deleteError.value = null
    try {

      await api.api.v1.admin.competitions.byCompetitionId(competitionId).announcements.byAnnouncementId(target.id).delete();
      toast.success(describeMessage('announcements.deleted'))
      if (editingId.value === target.id) resetEditor()
      if (previewTarget.value?.id === target.id) previewTarget.value = null
      deleteTarget.value = null
      await pagination.loadPage()
    }
    catch (error) { deleteError.value = parseApiError(error, describeMessage('announcements.deleteFailed')).displayMessage }
    finally { deleting.value = false }
  }
  function preview(item: Announcement) { previewTarget.value = item }
  function setPreviewOpen(open: boolean) { if (!open) previewTarget.value = null }
  function audienceKey(value?: Audience | null) { return value === 'Collaborators' ? 'common.label.eventStaff' : 'administration.label.contestants' }
  let stopNotifications: (() => void) | undefined
  onMounted(() => {
    void pagination.loadPage(1)
    stopNotifications = watchNotifications({ notificationChanged: () => void refresh(), onReconnected: () => void refresh() })
  })
  onScopeDispose(() => { stopNotifications?.(); pagination.reset() })

  return { canJudge, includeWithdrawn, editingId, title, body, audience, saving, formError, valid,
    deleting, deleteTarget, deleteError, previewTarget, resetEditor, edit, save, requestDelete, setDeleteOpen, remove,
    preview, setPreviewOpen, refresh, audienceKey, adminUserPath, adminFormatDateTime,
    items: pagination.items, loading: pagination.loading, error: computed(() => pagination.error.value?.message ?? null),
    initialized: pagination.initialized, page: pagination.page, pageCount: pagination.pageCount, total: pagination.total,
    pageLimit: pagination.limit, loadPage: pagination.loadPage, setPageSize: pagination.setPageSize }
}
export type AdminCompetitionsByIdAnnouncementsPageViewState = import('vue').ShallowUnwrapRef<ReturnType<typeof useAdminCompetitionsByIdAnnouncementsPage>>
