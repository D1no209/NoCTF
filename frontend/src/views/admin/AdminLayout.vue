<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink, RouterView, useRouter, useRoute } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import LanguageSwitch from '@/components/LanguageSwitch.vue'
import {
  Sidebar,
  SidebarContent,
  SidebarFooter,
  SidebarHeader,
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
  SidebarProvider,
  SidebarTrigger,
  SidebarInset,
} from '@/components/ui/sidebar'
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
  LogOut,
  Home,
} from 'lucide-vue-next'

const { t } = useI18n()
const auth = useAuthStore()
const router = useRouter()
const route = useRoute()

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
  <SidebarProvider>
    <Sidebar collapsible="icon">
      <SidebarHeader class="border-b h-14 flex items-center px-4 justify-between">
        <div class="flex items-center gap-2 overflow-hidden">
          <div class="flex size-8 items-center justify-center rounded-lg bg-primary text-primary-foreground shrink-0">
            <Trophy class="size-4" />
          </div>
          <div class="grid flex-1 text-left text-sm leading-tight group-data-[collapsible=icon]:hidden">
            <span class="truncate font-semibold">NoCTF Admin</span>
            <span class="truncate text-xs text-muted-foreground">{{ auth.user?.userName }}</span>
          </div>
        </div>
      </SidebarHeader>

      <SidebarContent class="py-4">
        <SidebarMenu>
          <SidebarMenuItem v-for="item in navItems" :key="item.to">
            <SidebarMenuButton 
              as-child 
              :tooltip="item.label"
              :active="route.path === item.to"
            >
              <RouterLink :to="item.to" class="flex items-center gap-3">
                <component :is="item.icon" class="size-4" />
                <span>{{ item.label }}</span>
              </RouterLink>
            </SidebarMenuButton>
          </SidebarMenuItem>
        </SidebarMenu>
      </SidebarContent>

      <SidebarFooter class="border-t p-4 space-y-4 group-data-[collapsible=icon]:p-2">
        <div class="group-data-[collapsible=icon]:hidden">
           <LanguageSwitch />
        </div>
        
        <SidebarMenu>
          <SidebarMenuItem>
            <SidebarMenuButton as-child tooltip="Back to App">
              <RouterLink to="/competitions" class="flex items-center gap-3">
                <Home class="size-4" />
                <span>{{ t('nav.backToApp') }}</span>
              </RouterLink>
            </SidebarMenuButton>
          </SidebarMenuItem>
          <SidebarMenuItem>
            <SidebarMenuButton @click="handleLogout" tooltip="Logout">
              <LogOut class="size-4" />
              <span>{{ t('auth.logout') }}</span>
            </SidebarMenuButton>
          </SidebarMenuItem>
        </SidebarMenu>
      </SidebarFooter>
    </Sidebar>

    <SidebarInset>
      <header class="flex h-14 shrink-0 items-center gap-2 border-b bg-background px-4 sticky top-0 z-10">
        <SidebarTrigger class="-ml-1" />
        <div class="h-4 w-px bg-border mx-2" />
        <h1 class="text-sm font-semibold truncate">
          {{ navItems.find(i => i.to === route.path)?.label || 'Admin' }}
        </h1>
      </header>
      
      <main class="flex-1 p-4 md:p-6 lg:p-8">
        <transition
          name="fade"
          mode="out-in"
        >
          <RouterView :key="route.fullPath" />
        </transition>
      </main>
    </SidebarInset>
  </SidebarProvider>
</template>

<style scoped>
.fade-enter-active,
.fade-leave-active {
  transition: opacity 0.2s ease;
}

.fade-enter-from,
.fade-leave-to {
  opacity: 0;
}
</style>
