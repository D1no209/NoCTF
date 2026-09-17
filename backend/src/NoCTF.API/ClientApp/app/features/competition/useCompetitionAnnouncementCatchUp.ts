import type { Ref } from 'vue'

import { listNotificationsEndpoint } from '../../api'
import type { NoCtfapiEndpointsNotificationsNotificationResponse } from '../../api'
import { createTrailingRefresh } from '../../lib/latest-page-refresh'
import { safeLocalStorage } from '../../lib/safe-storage'
import { showNotificationNotice } from '../notifications/showNotificationNotice'

const maximumRememberedAnnouncements = 256

export function parseSeenCompetitionAnnouncementIds(raw: string | null): string[] {
  if (!raw) return []
  try {
    const value = JSON.parse(raw) as unknown
    if (!Array.isArray(value)) return []
    return value.filter((id): id is string => typeof id === 'string' && id.length > 0)
  }
  catch {
    return []
  }
}

export function missedCompetitionAnnouncements(
  notifications: readonly NoCtfapiEndpointsNotificationsNotificationResponse[],
  seenIds: readonly string[],
): NoCtfapiEndpointsNotificationsNotificationResponse[] {
  const seen = new Set(seenIds)
  return notifications
    .filter(notification => notification.kind === 'CompetitionAnnouncement'
      && notification.id
      && !seen.has(notification.id))
    .toReversed()
}

export function mergeSeenCompetitionAnnouncementIds(
  current: readonly string[],
  notifications: readonly NoCtfapiEndpointsNotificationsNotificationResponse[],
): string[] {
  const ids = notifications
    .flatMap(notification => notification.kind === 'CompetitionAnnouncement' && notification.id
      ? [notification.id]
      : [])
  return [...new Set([...ids, ...current])].slice(0, maximumRememberedAnnouncements)
}

function announcementStorageKey(userId: string, competitionId: string): string {
  return `noctf:competition-announcements:seen:${userId}:${competitionId}`
}

export function useCompetitionAnnouncementCatchUp(competitionId: Readonly<Ref<string>>) {
  const { user } = useAuth()
  let enteredCompetitionId: string | null = null

  async function loadMissedAnnouncements(): Promise<void> {
    const userId = user.value?.userId
    const requestedCompetitionId = competitionId.value
    if (!userId || !requestedCompetitionId) return

    const { data, error } = await listNotificationsEndpoint({
      query: {
        competitionId: requestedCompetitionId,
        scope: 'Inbox',
        limit: 200,
      },
    })
    if (error || !data || user.value?.userId !== userId || competitionId.value !== requestedCompetitionId)
      return

    const key = announcementStorageKey(userId, requestedCompetitionId)
    const seenIds = parseSeenCompetitionAnnouncementIds(safeLocalStorage.getItem(key))
    const notifications = data.items ?? []
    const missed = missedCompetitionAnnouncements(notifications, seenIds)
    const isEntryCatchUp = enteredCompetitionId !== requestedCompetitionId
    for (const notification of missed)
      showNotificationNotice(notification, {
        idPrefix: isEntryCatchUp ? 'competition-announcement' : 'notification',
      })

    enteredCompetitionId = requestedCompetitionId

    if (missed.length > 0) {
      safeLocalStorage.setItem(
        key,
        JSON.stringify(mergeSeenCompetitionAnnouncementIds(seenIds, notifications)),
      )
    }
  }

  return {
    refreshMissedAnnouncements: createTrailingRefresh(loadMissedAnnouncements),
  }
}
