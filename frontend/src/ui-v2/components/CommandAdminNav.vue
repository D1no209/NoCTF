<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useChromeSession } from '@/features/chrome/useChromeSession'

interface AdminNavItem {
  label: string
  routeName: string
  adminOnly?: boolean
  activeRoutes: string[]
}

const adminNavigation: AdminNavItem[] = [
  { label: 'Competitions', routeName: 'admin-competitions', activeRoutes: ['admin-competitions', 'admin-competition-detail', 'admin-competition-operations'] },
  { label: 'Challenges', routeName: 'admin-challenges', activeRoutes: ['admin-challenges', 'admin-challenge-create'] },
  { label: 'Users', routeName: 'admin-users', adminOnly: true, activeRoutes: ['admin-users'] },
  { label: 'Teams', routeName: 'admin-teams', activeRoutes: ['admin-teams'] },
  { label: 'Collaborators', routeName: 'admin-collaborators', activeRoutes: ['admin-collaborators'] },
  { label: 'Containers', routeName: 'admin-containers', activeRoutes: ['admin-containers'] },
  { label: 'Plugins', routeName: 'admin-plugins', activeRoutes: ['admin-plugins'] },
  { label: 'Theme packs', routeName: 'admin-theme-packs', adminOnly: true, activeRoutes: ['admin-theme-packs'] },
  { label: 'Infrastructure', routeName: 'admin-infrastructure', adminOnly: true, activeRoutes: ['admin-infrastructure'] },
  { label: 'QQ Bot', routeName: 'admin-qqbot', adminOnly: true, activeRoutes: ['admin-qqbot'] },
  { label: 'Email', routeName: 'admin-email-verification', adminOnly: true, activeRoutes: ['admin-email-verification'] },
  { label: 'Audit logs', routeName: 'admin-audit-logs', adminOnly: true, activeRoutes: ['admin-audit-logs'] },
  { label: 'Health', routeName: 'admin-health', activeRoutes: ['admin-health'] },
  { label: 'Logs', routeName: 'admin-logs', activeRoutes: ['admin-logs'] },
]

const route = useRoute()
const router = useRouter()
const { isAdmin } = useChromeSession()

const visibleNavigation = computed(() =>
  adminNavigation.filter(item => !item.adminOnly || isAdmin.value))

const activeRouteName = computed(() => String(route.name ?? ''))

function navigate(item: AdminNavItem) {
  if (route.name !== item.routeName)
    router.push({ name: item.routeName })
}
</script>

<template>
  <nav class="command-admin-nav" aria-label="Administration sections">
    <button
      v-for="item in visibleNavigation"
      :key="item.routeName"
      type="button"
      class="command-admin-nav__item"
      :class="{ 'command-admin-nav__item--active': item.activeRoutes.includes(activeRouteName) }"
      @click="navigate(item)"
    >
      {{ item.label }}
    </button>
  </nav>
</template>

<style scoped>
.command-admin-nav { display: flex; min-width: 0; align-items: center; gap: 4px; overflow-x: auto; scrollbar-width: none; }
.command-admin-nav::-webkit-scrollbar { display: none; }

.command-admin-nav__item {
  flex: none;
  border: 0;
  border-radius: 999px;
  padding: 5px 11px;
  background: transparent;
  color: var(--v2-text-muted);
  cursor: pointer;
  font-family: var(--v2-font-mono);
  font-size: 10px;
  font-weight: 600;
  letter-spacing: 0.04em;
  transition: box-shadow 160ms ease, color 160ms ease, background-color 160ms ease;
  white-space: nowrap;
}

.command-admin-nav__item:hover { color: var(--v2-text); }

.command-admin-nav__item--active {
  background: var(--v2-surface);
  box-shadow: var(--v2-inset);
  color: var(--v2-primary);
}
</style>
