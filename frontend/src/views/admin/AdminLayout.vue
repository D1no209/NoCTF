<script setup lang="ts">
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink, RouterView, useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import LanguageSwitch from '@/components/LanguageSwitch.vue'
import {
  Activity,
  ClipboardList,
  Container,
  FileText,
  Handshake,
  Plug,
  Puzzle,
  Trophy,
  User,
  Users,
  Menu,
} from 'lucide-vue-next'

const { t } = useI18n()
const auth = useAuthStore()
const router = useRouter()
const mobileNavOpen = ref(false)

const isAdmin = computed(() => auth.userRole === 'Admin')

const navItems = computed(() => {
  const items = [
    { to: '/admin/competitions', label: t('admin.nav.competitions'), icon: Trophy },
    { to: '/admin/teams', label: t('admin.nav.teams'), icon: Users },
    { to: '/admin/collaborators', label: t('admin.nav.collaborators'), icon: Handshake },
    { to: '/admin/challenges', label: t('admin.nav.challenges'), icon: Puzzle },
    { to: '/admin/containers', label: t('admin.nav.containers'), icon: Container },
    { to: '/admin/plugins', label: t('admin.nav.plugins'), icon: Plug },
    { to: '/admin/audit-logs', label: t('admin.nav.auditLogs'), icon: ClipboardList },
    { to: '/admin/health', label: t('admin.nav.health'), icon: Activity },
    { to: '/admin/logs', label: t('admin.nav.liveLogs'), icon: FileText },
  ]
  if (isAdmin.value) {
    items.unshift({ to: '/admin/users', label: t('admin.nav.users'), icon: User })
  }
  return items
})

async function handleLogout() {
  auth.logout()
  await router.push('/login')
}
</script>

<template>
  <div class="min-h-screen bg-muted/20 lg:flex">
    <header class="sticky top-0 z-50 flex h-14 items-center justify-between border-b bg-background px-4 lg:hidden">
      <div class="flex items-center gap-2">
        <span class="font-semibold tracking-tight">NoCTF</span>
        <Badge variant="secondary" class="text-xs">{{ t('nav.admin') }}</Badge>
      </div>
      <Button variant="outline" size="sm" @click="mobileNavOpen = !mobileNavOpen">
        <Menu class="size-4" />
      </Button>
    </header>

    <!-- Sidebar -->
    <aside
      class="border-r bg-card flex-col shrink-0 lg:flex lg:min-h-screen lg:w-56"
      :class="mobileNavOpen ? 'flex' : 'hidden'"
    >
      <div class="p-4 border-b">
        <div class="flex items-center gap-2">
          <span class="text-lg font-bold tracking-tight">NoCTF</span>
          <Badge variant="secondary" class="text-xs">{{ t('nav.admin') }}</Badge>
        </div>
        <p class="text-xs text-muted-foreground mt-1 truncate">{{ auth.user?.userName }}</p>
      </div>

      <nav class="flex-1 p-3 space-y-1">
        <RouterLink
          v-for="item in navItems"
          :key="item.to"
          :to="item.to"
          class="flex items-center gap-2.5 px-3 py-2 rounded-md text-sm transition-colors hover:bg-accent hover:text-accent-foreground"
          active-class="bg-accent text-accent-foreground font-medium"
          @click="mobileNavOpen = false"
        >
          <component :is="item.icon" class="size-4 shrink-0" />
          <span>{{ item.label }}</span>
        </RouterLink>
      </nav>

      <div class="p-3 border-t space-y-2">
        <div class="px-3">
          <LanguageSwitch />
        </div>
        <RouterLink
          to="/competitions"
          class="flex items-center gap-2 px-3 py-2 rounded-md text-sm text-muted-foreground hover:bg-accent hover:text-accent-foreground transition-colors"
        >
          ← {{ t('nav.backToApp') }}
        </RouterLink>
        <Button variant="ghost" size="sm" class="w-full justify-start text-muted-foreground" @click="handleLogout">
          {{ t('auth.logout') }}
        </Button>
      </div>
    </aside>

    <!-- Main content -->
    <main class="min-w-0 flex-1 overflow-auto">
      <RouterView />
    </main>
  </div>
</template>
