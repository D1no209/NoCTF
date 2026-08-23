import type { NoCtfapiEndpointsChallengesQuestionsCompetitionQuestionResponse } from '~/api'
import { competitionQuestionUnreadCount } from '~/lib/competition-question'
import { safeLocalStorage } from '~/lib/safe-storage'

type Question = NoCtfapiEndpointsChallengesQuestionsCompetitionQuestionResponse

export function useCompetitionQuestionReadState(competitionId: string) {
  const { user } = useAuth()
  const seenUpdatedAt = ref<Record<string, string>>({})

  function storageKey(): string | null {
    return user.value?.userId
      ? `noctf:competition-questions:seen:${user.value.userId}:${competitionId}`
      : null
  }

  function load(): void {
    const key = storageKey()
    if (!key) return
    try {
      const stored = JSON.parse(safeLocalStorage.getItem(key) ?? '{}') as Record<string, unknown>
      seenUpdatedAt.value = Object.fromEntries(
        Object.entries(stored).filter((entry): entry is [string, string] => typeof entry[1] === 'string'),
      )
    }
    catch {
      seenUpdatedAt.value = {}
    }
  }

  function unreadCount(question: Question): number {
    const questionId = question.id
    return competitionQuestionUnreadCount(
      question.updatedAt,
      questionId ? seenUpdatedAt.value[questionId] : undefined,
      question.lastActorRole,
      question.access,
    )
  }

  function markRead(question: Question): void {
    if (!question.id) return
    seenUpdatedAt.value = {
      ...seenUpdatedAt.value,
      [question.id]: question.updatedAt ?? new Date().toISOString(),
    }
    const key = storageKey()
    if (key)
      safeLocalStorage.setItem(key, JSON.stringify(seenUpdatedAt.value))
  }

  onMounted(load)

  return { markRead, unreadCount }
}
