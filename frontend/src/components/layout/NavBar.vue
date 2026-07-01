<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink, useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { useScoreStore } from '@/stores/score'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import LanguageSwitch from '@/components/LanguageSwitch.vue'
import { Bell, LayoutDashboard, Menu } from 'lucide-vue-next'
import { Sheet, SheetContent, SheetHeader, SheetTitle, SheetTrigger } from '@/components/ui/sheet'

const { t } = useI18n()
const auth = useAuthStore()
const scoreStore = useScoreStore()
const router = useRouter()

const displayScore = computed(() => scoreStore.myTeamScore ?? 0)
const displayName = computed(() => scoreStore.myTeamName ?? auth.user?.userName ?? '')
const canManage = computed(() => ['Admin', 'Organizer'].includes(auth.userRole))

async function handleLogout() {
  auth.logout()
  scoreStore.reset()
  await router.push('/login')
}
</script>

<template>
  <header class="sticky top-0 z-50 w-full border-b border-slate-900/5 bg-white/85 backdrop-blur-xl">
    <div class="mx-auto flex h-16 max-w-[1600px] items-center justify-between gap-4 px-4 md:px-6">
      <div class="flex items-center gap-6">
        <RouterLink to="/competitions" class="flex items-center gap-2 font-bold text-lg tracking-tight hover:opacity-80 transition-all active:scale-95">
          <span class="noctf-logo size-8" />
          <span>NoCTF</span>
        </RouterLink>
        <nav class="hidden items-center gap-2 text-sm font-medium sm:flex">
          <RouterLink
            to="/competitions"
            class="relative rounded-lg px-3 py-2 text-muted-foreground transition-colors hover:bg-accent hover:text-foreground"
            active-class="bg-primary text-primary-foreground shadow-md shadow-primary/20 hover:bg-primary hover:text-primary-foreground"
          >
            {{ t('nav.competitions') }}
          </RouterLink>
          <RouterLink
            v-if="canManage"
            to="/admin"
            class="relative inline-flex items-center gap-1 rounded-lg px-3 py-2 text-muted-foreground transition-colors hover:bg-accent hover:text-foreground"
            active-class="bg-primary text-primary-foreground shadow-md shadow-primary/20 hover:bg-primary hover:text-primary-foreground"
          >
            <LayoutDashboard class="size-4" />
            {{ t('nav.admin') }}
          </RouterLink>
        </nav>
      </div>

      <div class="flex items-center gap-3">
        <div class="hidden sm:block">
          <LanguageSwitch />
        </div>

        <Button variant="ghost" size="icon-sm" class="hidden rounded-lg sm:inline-flex">
          <Bell class="size-4" />
        </Button>
        
        <template v-if="auth.isAuthenticated">
          <div v-if="displayName" class="flex items-center gap-2 rounded-xl border bg-white/80 px-2 py-1.5 text-sm shadow-sm">
            <span class="text-muted-foreground hidden lg:inline">{{ displayName }}</span>
            <Badge variant="default" class="font-mono tabular-nums">
              {{ displayScore }} <span class="ml-1 text-[10px] uppercase opacity-60">{{ t('nav.score') }}</span>
            </Badge>
          </div>
          <Button variant="ghost" size="sm" class="hidden sm:inline-flex" @click="handleLogout">{{ t('auth.logout') }}</Button>
        </template>
        <template v-else>
          <RouterLink to="/login" class="hidden sm:block">
            <Button variant="outline" size="sm">{{ t('auth.login') }}</Button>
          </RouterLink>
        </template>

        <!-- Mobile Menu -->
        <Sheet>
          <SheetTrigger as-child>
            <Button variant="ghost" size="icon" class="sm:hidden">
              <Menu class="size-5" />
            </Button>
          </SheetTrigger>
          <SheetContent side="right" class="w-[280px] bg-white">
            <SheetHeader class="text-left">
              <SheetTitle>NoCTF</SheetTitle>
            </SheetHeader>
            <div class="flex flex-col gap-4 py-8">
              <RouterLink to="/competitions" class="text-lg font-medium hover:text-primary transition-colors">
                {{ t('nav.competitions') }}
              </RouterLink>
              <RouterLink v-if="canManage" to="/admin" class="text-lg font-medium hover:text-primary transition-colors">
                {{ t('nav.admin') }}
              </RouterLink>
              
              <div class="h-px bg-border my-2" />
              
              <div class="flex items-center justify-between">
                <span class="text-sm text-muted-foreground">{{ t('common.language') }}</span>
                <LanguageSwitch />
              </div>

              <template v-if="auth.isAuthenticated">
                <div class="flex flex-col gap-1 py-2">
                  <span class="text-xs text-muted-foreground">{{ t('common.team') }}</span>
                  <span class="font-medium">{{ displayName }}</span>
                  <div class="flex items-center justify-between mt-1">
                    <span class="text-sm">{{ t('nav.score') }}</span>
                    <Badge variant="secondary">{{ displayScore }}</Badge>
                  </div>
                </div>
                <Button variant="outline" class="w-full mt-4" @click="handleLogout">{{ t('auth.logout') }}</Button>
              </template>
              <template v-else>
                <RouterLink to="/login">
                  <Button class="w-full">{{ t('auth.login') }}</Button>
                </RouterLink>
              </template>
            </div>
          </SheetContent>
        </Sheet>
      </div>
    </div>
  </header>
</template>
