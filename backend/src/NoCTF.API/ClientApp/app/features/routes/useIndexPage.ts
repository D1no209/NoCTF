import { markRaw } from 'vue'

import { ArrowRight, Crosshair, Flag, Mountain, Swords } from '@lucide/vue'
import { cn } from '../../lib/utils'
import { listCompetitionsEndpoint } from '../../api'
import type { NoCtfapiEndpointsCompetitionsCompetitionResponse } from '../../api'
import CompetitionCardComponent from '../competitions/CompetitionCard.vue'

/** Owns state, effects and commands for IndexPage. */
export function useIndexPage() {
  const { configuration } = usePlatform()

  const { isLoggedIn } = useAuth()

  const items = ref<NoCtfapiEndpointsCompetitionsCompetitionResponse[]>([])

  const competitionsLoading = ref(true)

  const competitionsError = ref<string | null>(null)

  async function loadCompetitions(): Promise<void> {
    competitionsLoading.value = true
    competitionsError.value = null
    const { data, error } = await listCompetitionsEndpoint()
    competitionsLoading.value = false
    if (error || !data) {
      competitionsError.value = parseApiError(error, translate("ui.failedToLoadRecentCompetitions")).message
      return
    }
    items.value = data?.items ?? []
  }

  onMounted(() => void loadCompetitions())

  const activeStatuses: string[] = [
    'Running',
    'Paused',
    'Published',
    'Visible',
  ]

  const recent = computed(() =>
    items.value
      .filter((c) => activeStatuses.includes(String(c.status)))
      .sort((a, b) => (a.endTime ?? '').localeCompare(b.endTime ?? ''))
      .slice(0, 6),
  )

  const liveCount = computed(
    () => items.value.filter((c) => c.status === 'Running').length,
  )

  const upcomingCount = computed(
    () =>
      items.value.filter((c) =>
        c.status === 'Published' || c.status === 'Visible',
      ).length,
  )

  const modes = [
    { key: 'Ctf', icon: Flag, title: "ui.ctfProblemSolvingCompetition", description: "ui.webPwnCryptoReverseMultiDirectionalQuestionsSolveTheProblem", featured: true },
    { key: 'Awd', icon: Swords, title: "ui.awdAttackAndDefenseGame", description: "ui.realTimeConfrontationIntegratingAttackAndDefenseDoubleTestOf", featured: false },
    { key: 'Awdp', icon: Crosshair, title: "ui.awdpAttackAndDefenseEnhancement", description: "ui.introduceTheRepairLinkOnTopOfAwdAndAttack", featured: false },
    { key: 'Koh', icon: Mountain, title: "ui.kohIsTheKingOfTheMountain", description: "ui.continueToOccupyTheTargetAccumulatePointsOverTimeAnd", featured: true },
  ]

  const CompetitionCard = markRaw(CompetitionCardComponent)

  return {
      ArrowRight,
      cn,
      configuration,
      isLoggedIn,
      items,
      competitionsLoading,
      competitionsError,
      loadCompetitions,
      recent,
      liveCount,
      upcomingCount,
      modes,
      CompetitionCard
    }
}

export type IndexPageViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useIndexPage>>>
