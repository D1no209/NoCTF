<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink, RouterView, useRouter, useRoute } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import LanguageSwitch from '@/components/LanguageSwitch.vue'
import BrandLogo from '@/components/BrandLogo.vue'
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
  BadgeCheck,
  Activity,
  ClipboardList,
  Container,
  FileText,
  Handshake,
  Plug,
  Puzzle,
  Network,
  Bot,
  MailCheck,
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
    { to: '/admin/infrastructure', label: t('admin.nav.infrastructure'), icon: Network, adminOnly: true },
    { to: '/admin/qqbot', label: t('admin.nav.qqBot'), icon: Bot, adminOnly: true },
    { to: '/admin/email-verification', label: t('admin.nav.emailVerification'), icon: MailCheck, adminOnly: true },
    { to: '/admin/audit-logs', label: t('admin.nav.auditLogs'), icon: ClipboardList, adminOnly: true },
    { to: '/admin/health', label: t('admin.nav.health'), icon: Activity },
    { to: '/admin/logs', label: t('admin.nav.liveLogs'), icon: FileText },
  ]
  if (isAdmin.value) {
    items.unshift({ to: '/admin/users', label: t('admin.nav.users'), icon: User })
  }
  return items.filter(item => !item.adminOnly || isAdmin.value)
})

async function handleLogout() {
  auth.logout()
  await router.push('/login')
}
</script>

<template>
  <SidebarProvider>
    <Sidebar collapsible="icon" class="border-r-0">
      <SidebarHeader class="h-20 flex items-center border-b border-white/10 px-4">
        <div class="flex items-center gap-2 overflow-hidden">
          <BrandLogo class="h-10" />
          <div class="grid flex-1 text-left text-sm leading-tight group-data-[collapsible=icon]:hidden">
            <span class="mt-0.5 inline-flex w-fit rounded-md bg-white/10 px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wide text-slate-300">{{ t('nav.admin') }}</span>
          </div>
        </div>
      </SidebarHeader>

      <SidebarContent class="py-5">
        <SidebarMenu>
          <SidebarMenuItem v-for="item in navItems" :key="item.to">
            <SidebarMenuButton
              as-child
              :tooltip="item.label"
              :is-active="route.path === item.to"
            >
              <RouterLink :to="item.to" class="flex items-center gap-3">
                <component :is="item.icon" class="size-4" />
                <span>{{ item.label }}</span>
              </RouterLink>
            </SidebarMenuButton>
          </SidebarMenuItem>
        </SidebarMenu>
      </SidebarContent>

      <SidebarFooter class="space-y-4 border-t border-white/10 p-4 group-data-[collapsible=icon]:p-2">
        <div class="group-data-[collapsible=icon]:hidden">
          <div class="mb-3 rounded-xl border border-white/10 bg-white/5 p-3">
            <div class="flex items-center gap-3">
              <div class="flex size-9 items-center justify-center rounded-full bg-white text-slate-900 font-semibold">
                {{ auth.user?.userName?.charAt(0)?.toUpperCase() ?? 'A' }}
              </div>
              <div class="min-w-0">
                <div class="truncate text-sm font-semibold text-white">{{ auth.user?.userName ?? t('nav.admin') }}</div>
                <div class="text-xs text-slate-400">{{ auth.userRole }}</div>
              </div>
            </div>
          </div>
        </div>

        <SidebarMenu>
          <SidebarMenuItem>
            <SidebarMenuButton as-child :tooltip="t('nav.backToApp')">
              <RouterLink to="/competitions" class="flex items-center gap-3">
                <Home class="size-4" />
                <span>{{ t('nav.backToApp') }}</span>
              </RouterLink>
            </SidebarMenuButton>
          </SidebarMenuItem>
          <SidebarMenuItem>
            <SidebarMenuButton @click="handleLogout" :tooltip="t('auth.logout')">
              <LogOut class="size-4" />
              <span>{{ t('auth.logout') }}</span>
            </SidebarMenuButton>
          </SidebarMenuItem>
        </SidebarMenu>
      </SidebarFooter>
    </Sidebar>

    <SidebarInset>
      <header class="sticky top-0 z-10 flex h-16 shrink-0 items-center gap-2 border-b bg-white/[0.85] px-4 backdrop-blur-xl">
        <SidebarTrigger class="-ml-1" />
        <div class="h-4 w-px bg-border mx-2" />
        <div class="flex min-w-0 flex-1 items-center gap-3">
          <h1 class="truncate text-xl font-bold tracking-tight">
            {{ navItems.find(i => i.to === route.path)?.label || t('nav.admin') }}
          </h1>
          <span v-if="isAdmin" class="inline-flex items-center gap-1 rounded-md bg-red-50 px-2 py-1 text-xs font-semibold text-red-600">
            <BadgeCheck class="size-3.5" />
            {{ t('nav.admin') }}
          </span>
        </div>
        <div class="flex items-center gap-4">
          <RouterLink to="/competitions" class="hidden text-sm font-medium text-muted-foreground transition-colors hover:text-foreground md:inline">
            {{ t('nav.backToApp') }}
          </RouterLink>
          <div class="hidden h-5 w-px bg-border md:block" />
          <LanguageSwitch />
        </div>
      </header>
      
      <main class="flex-1 p-4 md:p-6 lg:p-8">
        <RouterView v-slot="{ Component }">
          <transition
            name="fade"
            mode="out-in"
          >
            <component :is="Component" :key="route.fullPath" />
          </transition>
        </RouterView>
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
