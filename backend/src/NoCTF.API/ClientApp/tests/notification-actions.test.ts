import { sourceFile } from './support/feature-source'
import { describe, expect, test } from 'bun:test'
import { isNotificationUnread } from '../app/composables/useNotificationUnread'

describe('notification unread state', () => {
  test('shows unread only when the latest immutable notification differs from the read marker', () => {
    expect(isNotificationUnread(null, null)).toBe(false)
    expect(isNotificationUnread('notification-1', null)).toBe(true)
    expect(isNotificationUnread('notification-1', 'notification-1')).toBe(false)
    expect(isNotificationUnread('notification-2', 'notification-1')).toBe(true)
  })

  test('renders a color-independent unread indicator and marks the newest item read', async () => {
    const layout = await sourceFile(
      new URL('../app/layouts/default.vue', import.meta.url),
    ).text()
    const page = await sourceFile(
      new URL('../app/pages/notifications.vue', import.meta.url),
    ).text()
    const center = await sourceFile(
      new URL('../app/features/notifications/NotificationCenter.vue', import.meta.url),
    ).text()

    expect(layout).toContain(":aria-label=\"hasUnread ? t('ui.notificationsUnreadMessages') : t('ui.notifications')\"")
    expect(layout).toContain("<span v-if=\"hasUnread\" class=\"sr-only\">{{ t('ui.unreadNotifications') }}</span>")
    expect(page).toContain("<component :is=\"NotificationCenter\" />")
    expect(center).toContain('markAllRead(items.value[0]?.id)')
    expect(center).toContain("scope: 'Inbox'")
    expect(center).not.toContain("ui.officialAnnouncementsAndMessagesDirectlyRelatedToYourAccountTeam")
  })

  test('reuses the shared choice sidebar, card and scroll surface for the message center', async () => {
    const view = await sourceFile(
      new URL('../app/components/views/notifications/NotificationCenterView.vue', import.meta.url),
    ).text()
    const sidebar = await sourceFile(
      new URL('../app/components/ui/sidebar/ChoiceSidebar.vue', import.meta.url),
    ).text()

    expect(view).toContain('<ChoiceSidebar')
    expect(view).toContain('<Card id="notification-center-detail"')
    expect(view).toContain('<ScrollSurface axis="y"')
    expect(view).toContain('<MotionSwap :identity="selectedId || \'\'" preset="film-up">')
    expect(view).toContain('class="notification-detail-card')
    expect(view).not.toContain('<ActionButton')
    expect(sidebar).toContain('<slot name="footer" />')
  })
})

describe('competition announcement access', () => {
  test('exposes the generated announcement SDK to judges and uses participant delivery by default', async () => {
    const page = await sourceFile(
      new URL('../app/pages/admin/competitions/[id].vue', import.meta.url),
    ).text()
    const endpoint = await sourceFile(
      new URL('../../Endpoints/Administration/Competitions/CreateCompetitionAnnouncementEndpoint.cs', import.meta.url),
    ).text()

    expect(page).toContain("const canAnnounce = computed(() => role.value !== 'Observer')")
    expect(page).toContain("adminCreateCompetitionAnnouncement({")
    expect(page).toContain("('Participants')")
    expect(endpoint).toContain('authorizer.CanJudgeAsync(')
  })

  test('renders both the announcement title and its public body', async () => {
    const labels = await sourceFile(
      new URL('../app/utils/labels.ts', import.meta.url),
    ).text()

    expect(labels).toContain("const announcementBody = typeof payload.body === 'string'")
    expect(labels).toContain('`${announcementTitle}：${announcementBody}`')
  })
})
