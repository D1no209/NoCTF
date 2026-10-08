import { listChallengesEndpoint, listMyChallengeWriteUps } from '~/api'
import type { NoCtfapiEndpointsChallengesChallengeSummaryResponse } from '~/api'
import type { WriteUp } from './writeup-state'
import { challengeWriteUpPath, writeUpStatusKey } from './writeup-state'
import { message } from '~/utils/i18n'
import type { UiMessage } from '~/utils/i18n'

export function useMySingleWriteUps(props: Readonly<{ competitionId: string }>) {
  const route = useRoute(), router = useRouter()
  const loading = ref(true), error = ref<UiMessage | null>(null), search = ref('')
  const challenges = ref<NoCtfapiEndpointsChallengesChallengeSummaryResponse[]>([]), writeUps = ref<WriteUp[]>([])
  const rows = computed(() => challenges.value.filter(x => `${x.title ?? ''} ${x.direction ?? ''}`.toLocaleLowerCase()
    .includes(search.value.toLocaleLowerCase())).map(challenge => {
    const writeUp = writeUps.value.find(x => x.competitionChallengeId === challenge.id)
    return { challenge, writeUp, statusKey: writeUpStatusKey(writeUp) }
  }))
  let sequence = 0, unwatch: (() => void) | undefined
  async function load() {
    const request = ++sequence; loading.value = challenges.value.length === 0; error.value = null
    const [catalog, mine] = await Promise.all([listChallengesEndpoint({ path: { competitionId: props.competitionId } }),
      listMyChallengeWriteUps({ path: { competitionId: props.competitionId } })])
    if (request !== sequence) return
    loading.value = false
    if (catalog.error || mine.error) { error.value = parseApiError(catalog.error ?? mine.error, message('challengeWriteUp.loadFailed')).displayMessage; return }
    challenges.value = catalog.data?.items ?? []; writeUps.value = mine.data ?? []
  }
  function open(id?: string) { if (id) void router.push({ path: `${challengeWriteUpPath(props.competitionId, id)}/mine`, query: route.query }) }
  onMounted(() => { void load(); unwatch = watchCompetition(props.competitionId, { competitionEventChanged: event => {
    if (['ChallengeWriteUpSubmitted', 'ChallengeWriteUpPublished', 'ChallengeWriteUpRejected', 'ChallengeUpdated'].includes(event.kind)) void load()
  }, onReconnected: () => void load() }) })
  onBeforeUnmount(() => { sequence++; unwatch?.() })
  return { loading, error, search, rows, load, open }
}
export type MySingleWriteUpsState = import('vue').ShallowUnwrapRef<ReturnType<typeof useMySingleWriteUps>>
