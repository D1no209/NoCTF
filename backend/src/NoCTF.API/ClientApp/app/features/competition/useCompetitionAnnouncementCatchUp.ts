
import { api } from '../../lib/api'
import type { Ref } from 'vue'


import type { NoCTFAPIEndpointsNotificationsNotificationResponse } from '../../api/models'
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
  notifications: readonly NoCTFAPIEndpointsNotificationsNotificationResponse[],
  seenIds: readonly string[],
): NoCTFAPIEndpointsNotificationsNotificationResponse[] {
  const seen = new Set(seenIds)
  return notifications
    .filter(notification => notification.kind === 'CompetitionAnnouncement'
      && notification.id
      && !seen.has(notification.id))
    .toReversed()
}

export function mergeSeenCompetitionAnnouncementIds(
  current: readonly string[],
  notifications: readonly NoCTFAPIEndpointsNotificationsNotificationResponse[],
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

  async function loadMissedAnnouncements(): Promise<void> {
    const userId = user.value?.userId
    const requestedCompetitionId = competitionId.value
    if (!userId || !requestedCompetitionId) return

    let error: unknown;
    const data = await api.api.v1.notifications.get({ queryParameters: {
        competitionId: requestedCompetitionId,
        scope: 'Inbox',
        offset: 0,
        limit: 200,
        desc: true,
      } }).catch(cause => { error = cause; return undefined });
    if (error || !data || user.value?.userId !== userId || competitionId.value !== requestedCompetitionId)
      return

    const key = announcementStorageKey(userId, requestedCompetitionId)
    const seenIds = parseSeenCompetitionAnnouncementIds(safeLocalStorage.getItem(key))
    const notifications = data.items ?? []
    const missed = missedCompetitionAnnouncements(notifications, seenIds)
    for (const notification of missed)
      showNotificationNotice(notification)

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
