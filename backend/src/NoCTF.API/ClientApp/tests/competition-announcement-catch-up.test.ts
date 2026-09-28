import { describe, expect, test } from 'bun:test'

import type { NoCtfapiEndpointsNotificationsNotificationResponse } from '../app/api'
import {
  mergeSeenCompetitionAnnouncementIds,
  missedCompetitionAnnouncements,
  parseSeenCompetitionAnnouncementIds,
} from '../app/features/competition/useCompetitionAnnouncementCatchUp'
import { sourceFile } from './support/feature-source'

function notification(
  id: string,
  kind: NoCtfapiEndpointsNotificationsNotificationResponse['kind'] = 'CompetitionAnnouncement',
): NoCtfapiEndpointsNotificationsNotificationResponse {
  return { id, kind }
}

describe('competition announcement catch-up', () => {
  test('selects unseen announcements in chronological display order', () => {
    const notifications = [
      notification('newest'),
      notification('message', 'Message'),
      notification('seen'),
      notification('oldest'),
    ]

    expect(missedCompetitionAnnouncements(notifications, ['seen']).map(item => item.id))
      .toEqual(['oldest', 'newest'])
  })

  test('persists a bounded, deduplicated set and tolerates invalid storage', () => {
    expect(parseSeenCompetitionAnnouncementIds('not-json')).toEqual([])
    expect(parseSeenCompetitionAnnouncementIds('["one",1,null,"two"]')).toEqual(['one', 'two'])
    expect(mergeSeenCompetitionAnnouncementIds(
      ['older', 'same'],
      [notification('newest'), notification('same'), notification('ignored', 'Message')],
    )).toEqual(['newest', 'same', 'older'])
  })

  test('loads the authorized competition inbox on entry and refreshes for live announcements', async () => {
    const [catchUp, competitionPage] = await Promise.all([
      sourceFile('app/features/competition/useCompetitionAnnouncementCatchUp.ts').text(),
      sourceFile('app/features/routes/competitions/useCompetitionsByIdPage.ts').text(),
    ])

    expect(catchUp).toContain('competitionId: requestedCompetitionId')
    expect(catchUp).toContain("scope: 'Inbox'")
    expect(catchUp).toContain('showNotificationNotice(notification)')
    expect(catchUp).toContain('safeLocalStorage.setItem(')
    expect(competitionPage).toContain('await refreshMissedAnnouncements()')
    expect(competitionPage).toContain("if (event.kind === 'AnnouncementPublished')")
    expect(competitionPage).toContain('void refreshMissedAnnouncements()')
  })
})
