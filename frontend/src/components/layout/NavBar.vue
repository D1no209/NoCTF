<script setup lang="ts">
import { Bell, Home, LayoutDashboard, Menu, Users } from 'lucide-vue-next'
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink, useRouter } from 'vue-router'
import LanguageSwitch from '@/components/LanguageSwitch.vue'
import { Button } from '@/components/ui/button'
import { Sheet, SheetContent, SheetHeader, SheetTitle, SheetTrigger } from '@/components/ui/sheet'
import { useAuthStore } from '@/stores/auth'
import { useScoreStore } from '@/stores/score'

const { t } = useI18n()
const auth = useAuthStore()
const scoreStore = useScoreStore()
const router = useRouter()

const displayName = computed(() => auth.user?.userName ?? '')
const canManage = computed(() => ['Admin', 'Organizer'].includes(auth.userRole))

async function handleLogout() {
  auth.logout()
  scoreStore.reset()
  await router.push('/login')
}
</script>

<template>
  <header class="sticky top-0 z-50 w-full border-b border-border/90 bg-background/95">
    <div
      class="mx-auto flex h-14 max-w-[1600px] items-center justify-between gap-2 px-3 sm:h-16 sm:gap-4 sm:px-4 md:px-6"
    >
      <div class="flex min-w-0 items-center gap-4 lg:gap-6">
        <RouterLink
          to="/"
          class="flex min-w-0 items-center gap-2 text-lg font-bold tracking-tight transition-[opacity,transform] duration-[var(--motion-fast)] ease-[var(--ease-out-quint)] hover:opacity-80 active:scale-[0.985]"
        >
          <span class="noctf-logo size-8" />
          <span class="truncate">NoCTF</span>
        </RouterLink>
        <nav class="hidden items-center gap-2 text-sm font-medium sm:flex">
          <RouterLink
            to="/"
            class="relative inline-flex items-center gap-1 rounded-lg px-3 py-2 text-muted-foreground transition-[background-color,color,box-shadow] duration-[var(--motion-fast)] ease-[var(--ease-out-quint)] hover:bg-accent hover:text-foreground"
            active-class="bg-primary text-primary-foreground hover:bg-primary hover:text-primary-foreground"
          >
            <Home class="size-4" />
            {{ t('nav.home') }}
          </RouterLink>
          <RouterLink
            to="/competitions"
            class="relative rounded-lg px-3 py-2 text-muted-foreground transition-[background-color,color,box-shadow] duration-[var(--motion-fast)] ease-[var(--ease-out-quint)] hover:bg-accent hover:text-foreground"
            active-class="bg-primary text-primary-foreground hover:bg-primary hover:text-primary-foreground"
          >
            {{ t('nav.competitions') }}
          </RouterLink>
          <RouterLink
            to="/teams"
            class="relative inline-flex items-center gap-1 rounded-lg px-3 py-2 text-muted-foreground transition-[background-color,color,box-shadow] duration-[var(--motion-fast)] ease-[var(--ease-out-quint)] hover:bg-accent hover:text-foreground"
            active-class="bg-primary text-primary-foreground hover:bg-primary hover:text-primary-foreground"
          >
            <Users class="size-4" />
            {{ t('nav.teams') }}
          </RouterLink>
          <RouterLink
            v-if="canManage"
            to="/admin"
            class="relative inline-flex items-center gap-1 rounded-lg px-3 py-2 text-muted-foreground transition-[background-color,color,box-shadow] duration-[var(--motion-fast)] ease-[var(--ease-out-quint)] hover:bg-accent hover:text-foreground"
            active-class="bg-primary text-primary-foreground hover:bg-primary hover:text-primary-foreground"
          >
            <LayoutDashboard class="size-4" />
            {{ t('nav.admin') }}
          </RouterLink>
        </nav>
      </div>

      <div class="flex shrink-0 items-center gap-2 sm:gap-3">
        <div class="shrink-0">
          <LanguageSwitch />
        </div>

        <Button variant="ghost" size="icon-sm" class="hidden rounded-lg sm:inline-flex">
          <Bell class="size-4" />
        </Button>

        <template v-if="auth.isAuthenticated">
          <div
            v-if="displayName"
            class="flex items-center gap-2 rounded-md border bg-card px-2 py-1.5 text-sm"
          >
            <span class="text-muted-foreground hidden lg:inline">{{ displayName }}</span>
          </div>
          <Button variant="ghost" size="sm" class="hidden sm:inline-flex" @click="handleLogout">
            {{ t('auth.logout') }}
          </Button>
        </template>
        <template v-else>
          <RouterLink to="/login" class="hidden sm:block">
            <Button variant="outline" size="sm">
              {{ t('auth.login') }}
            </Button>
          </RouterLink>
        </template>

        <!-- Mobile Menu -->
        <Sheet>
          <SheetTrigger as-child>
            <Button variant="ghost" size="icon" class="sm:hidden">
              <Menu class="size-5" />
            </Button>
          </SheetTrigger>
          <SheetContent side="right" class="w-[min(20rem,calc(100vw-1.5rem))] bg-background">
            <SheetHeader class="text-left">
              <SheetTitle>NoCTF</SheetTitle>
            </SheetHeader>
            <div class="flex flex-col gap-1 py-6">
              <RouterLink
                to="/"
                class="flex min-h-11 items-center rounded-md px-2 text-base font-medium transition-[background-color,color] duration-[var(--motion-fast)] ease-[var(--ease-out-quint)] hover:bg-accent hover:text-primary"
              >
                {{ t('nav.home') }}
              </RouterLink>
              <RouterLink
                to="/competitions"
                class="flex min-h-11 items-center rounded-md px-2 text-base font-medium transition-[background-color,color] duration-[var(--motion-fast)] ease-[var(--ease-out-quint)] hover:bg-accent hover:text-primary"
              >
                {{ t('nav.competitions') }}
              </RouterLink>
              <RouterLink
                to="/teams"
                class="flex min-h-11 items-center rounded-md px-2 text-base font-medium transition-[background-color,color] duration-[var(--motion-fast)] ease-[var(--ease-out-quint)] hover:bg-accent hover:text-primary"
              >
                {{ t('nav.teams') }}
              </RouterLink>
              <RouterLink
                v-if="canManage"
                to="/admin"
                class="flex min-h-11 items-center rounded-md px-2 text-base font-medium transition-[background-color,color] duration-[var(--motion-fast)] ease-[var(--ease-out-quint)] hover:bg-accent hover:text-primary"
              >
                {{ t('nav.admin') }}
              </RouterLink>

              <template v-if="auth.isAuthenticated">
                <div class="mt-3 flex flex-col gap-1 border-t border-border/80 py-3">
                  <span class="text-xs text-muted-foreground">{{ t('common.user') }}</span>
                  <span class="font-medium">{{ displayName }}</span>
                </div>
                <Button variant="outline" class="mt-2 w-full" @click="handleLogout">
                  {{ t('auth.logout') }}
                </Button>
              </template>
              <template v-else>
                <RouterLink to="/login">
                  <Button class="w-full">
                    {{ t('auth.login') }}
                  </Button>
                </RouterLink>
              </template>
            </div>
          </SheetContent>
        </Sheet>
      </div>
    </div>
  </header>
</template>
