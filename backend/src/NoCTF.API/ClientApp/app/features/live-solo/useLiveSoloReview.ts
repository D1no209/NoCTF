import { computed, onScopeDispose, ref, watch } from 'vue'
import { getLiveSoloReviewQuestion, listLiveSoloReviewQuestions, prepareLiveSoloReviewAttachmentDownload } from '~/api'
import type { NoCtfapiEndpointsLiveSoloLiveSoloReviewContentResponse as Content, NoCtfapiEndpointsLiveSoloLiveSoloReviewQuestionResponse as Question } from '~/api'
import { parseLiveSoloError } from './live-solo-errors'
import { message, type UiMessage } from '~/utils/i18n'
import { assertCredentialSafeUrl, startAttachmentBrowserDownload } from '~/utils/download'
import { getRealtimeAccessToken } from '~/lib/session'

export function useLiveSoloReview(props: Readonly<{ competitionId: string; matchId: string; staff: boolean; roundId?: string }>) {
  const route = useRoute(), router = useRouter(), { user } = useAuth()
  const questions = ref<Question[]>([]), selected = ref<string | null>(null), content = ref<Content | null>(null)
  const loading = ref(true), reading = ref(false), downloading = ref(false), error = ref<UiMessage | null>(null)
  const previewSource = ref<string | null>(null), previewHeaders = ref<Record<string, string>>({}), previewId = ref<string | null>(null)
  const options = computed(() => questions.value.filter(x => x.id).map(x => ({ value: x.id!, label: x.title || translate('liveSolo.review.unrecorded'), row: x })))
  const current = computed(() => questions.value.find(x => x.id === selected.value) ?? null)
  let generation = 0, disposed = false
  const scope = () => ({ competitionId: props.competitionId, matchId: props.matchId })
  async function read() {
    const questionId = selected.value, id = ++generation, actor = user.value?.userId
    content.value = null; previewSource.value = null; previewHeaders.value = {}; previewId.value = null; reading.value = true; error.value = null
    try {
      if (!questionId || !actor || current.value?.contentRecorded === false) return
      const response = await getLiveSoloReviewQuestion({ path: { ...scope(), questionId }, query: { staff: props.staff } })
      if (disposed || id !== generation || actor !== user.value?.userId) return
      if (response.error || !response.data) throw parseLiveSoloError(response.error, message('liveSolo.review.failed'))
      content.value = response.data
    } catch (cause) { if (!disposed && id === generation) error.value = parseLiveSoloError(cause, message('liveSolo.review.failed')).displayMessage }
    finally { if (id === generation) reading.value = false }
  }
  async function load() {
    const id = ++generation, actor = user.value?.userId
    content.value = null; loading.value = true; error.value = null
    try {
      if (!actor) return
      const response = await listLiveSoloReviewQuestions({ path: scope(), query: { staff: props.staff } })
      if (disposed || id !== generation || actor !== user.value?.userId) return
      if (response.error || !response.data) throw parseLiveSoloError(response.error, message('liveSolo.review.failed'))
      questions.value = (response.data.items ?? []).filter(x => !props.roundId || x.roundId === props.roundId)
      const query = typeof route.query.reviewQuestion === 'string' ? route.query.reviewQuestion : selected.value
      selected.value = questions.value.some(x => x.id === query) ? query ?? null : questions.value.find(x => x.id)?.id ?? null
      await read()
    } catch (cause) { if (!disposed && id === generation) { questions.value = []; error.value = parseLiveSoloError(cause, message('liveSolo.review.failed')).displayMessage } }
    finally { loading.value = false }
  }
  async function select(id: string) {
    if (!questions.value.some(x => x.id === id)) return
    selected.value = id
    await router.replace({ query: { ...route.query, reviewQuestion: id } }); await read()
  }
  async function download(attachmentId: string) {
    const questionId = selected.value, epoch = generation, actor = user.value?.userId
    if (!questionId || downloading.value) return
    downloading.value = true; error.value = null
    try {
      const response = await prepareLiveSoloReviewAttachmentDownload({ path: { ...scope(), questionId, attachmentId }, body: { staff: props.staff } })
      if (disposed || epoch !== generation || actor !== user.value?.userId) return
      if (response.error || !response.data?.downloadUrl) throw parseLiveSoloError(response.error, message('liveSolo.review.failed'))
      startAttachmentBrowserDownload(response.data.downloadUrl)
    } catch (cause) { if (!disposed && epoch === generation) error.value = parseLiveSoloError(cause, message('liveSolo.review.failed')).displayMessage }
    finally { downloading.value = false }
  }
  async function preview(attachmentId: string) {
    const questionId = selected.value, epoch = generation, actor = user.value?.userId
    if (!questionId || downloading.value) return
    downloading.value = true; error.value = null; previewSource.value = null
    try {
      const response = await prepareLiveSoloReviewAttachmentDownload({ path: { ...scope(), questionId, attachmentId }, body: { staff: props.staff } })
      const access = await getRealtimeAccessToken()
      if (disposed || epoch !== generation || actor !== user.value?.userId) return
      if (response.error || !response.data?.downloadUrl || !access) throw parseLiveSoloError(response.error, message('liveSolo.review.failed'))
      // 预览会把 Bearer 头交给 pdf.js,必须先确认 URL 指向本源,避免凭证外泄。
      assertCredentialSafeUrl(response.data.downloadUrl)
      previewHeaders.value = { Authorization: `Bearer ${access}` }; previewId.value = attachmentId; previewSource.value = response.data.downloadUrl
    } catch (cause) { if (!disposed && epoch === generation) error.value = parseLiveSoloError(cause, message('liveSolo.review.failed')).displayMessage }
    finally { downloading.value = false }
  }
  watch([() => user.value?.userId, () => props.competitionId, () => props.matchId, () => props.staff, () => props.roundId], () => {
    generation++; questions.value = []; selected.value = null; content.value = null; previewSource.value = null; previewHeaders.value = {}; previewId.value = null; void load()
  }, { immediate: true })
  onScopeDispose(() => { disposed = true; generation++; content.value = null; previewHeaders.value = {} })
  return { staff: computed(() => props.staff), loading, reading, downloading, error, options, selected, current, content, select, load, read, download,
    previewSource, previewHeaders, previewId, preview }
}
export type LiveSoloReviewState = import('vue').ShallowUnwrapRef<ReturnType<typeof useLiveSoloReview>>
