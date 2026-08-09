import type { NoCtfapiEndpointsChallengesQuestionsCompetitionQuestionResponse } from '~/api'
import { competitionQuestionUnreadCount } from '~/lib/competition-question'

type Question = NoCtfapiEndpointsChallengesQuestionsCompetitionQuestionResponse

export function useCompetitionQuestionReadState(competitionId: string) {
  const { user } = useAuth()
  const seenRevisions = ref<Record<string, number>>({})

  function storageKey(): string | null {
    return user.value?.userId
      ? `noctf:competition-questions:seen:${user.value.userId}:${competitionId}`
      : null
  }

  function load(): void {
    const key = storageKey()
    if (!key || !import.meta.client) return
    try {
      const stored = JSON.parse(localStorage.getItem(key) ?? '{}') as Record<string, unknown>
      seenRevisions.value = Object.fromEntries(
        Object.entries(stored).filter((entry): entry is [string, number] => Number.isInteger(entry[1])),
      )
    }
    catch {
      seenRevisions.value = {}
    }
  }

  function unreadCount(question: Question): number {
    const questionId = question.id
    return competitionQuestionUnreadCount(
      question.revision,
      questionId ? seenRevisions.value[questionId] : undefined,
      question.lastActorRole,
      question.access,
    )
  }

  function markRead(question: Question): void {
    if (!question.id) return
    seenRevisions.value = {
      ...seenRevisions.value,
      [question.id]: Math.max(0, question.revision ?? 0),
    }
    const key = storageKey()
    if (key && import.meta.client)
      localStorage.setItem(key, JSON.stringify(seenRevisions.value))
  }

  onMounted(load)

  return { markRead, unreadCount }
}
