<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink, useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { useScoreStore } from '@/stores/score'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import LanguageSwitch from '@/components/LanguageSwitch.vue'
import { LayoutDashboard } from 'lucide-vue-next'

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
  <header class="sticky top-0 z-50 w-full border-b bg-background/95 backdrop-blur supports-[backdrop-filter]:bg-background/60">
    <div class="mx-auto flex h-14 max-w-7xl items-center justify-between gap-4 px-4 md:px-6">
      <div class="flex items-center gap-6">
        <RouterLink to="/competitions" class="font-bold text-lg tracking-tight hover:opacity-80 transition-opacity">
          NoCTF
        </RouterLink>
        <nav class="hidden sm:flex items-center gap-4 text-sm text-muted-foreground">
          <RouterLink
            to="/competitions"
            class="hover:text-foreground transition-colors"
            active-class="text-foreground font-medium"
          >
            {{ t('nav.competitions') }}
          </RouterLink>
          <RouterLink
            v-if="canManage"
            to="/admin"
            class="inline-flex items-center gap-1 hover:text-foreground transition-colors"
            active-class="text-foreground font-medium"
          >
            <LayoutDashboard class="size-4" />
            {{ t('nav.admin') }}
          </RouterLink>
        </nav>
      </div>

      <div class="flex items-center gap-3">
        <LanguageSwitch />
        <template v-if="auth.isAuthenticated">
          <div v-if="displayName" class="flex items-center gap-2 text-sm">
            <span class="text-muted-foreground hidden sm:inline">{{ displayName }}</span>
            <Badge variant="secondary" class="font-mono tabular-nums">
              {{ displayScore }} {{ t('nav.score') }}
            </Badge>
          </div>
          <Button variant="outline" size="sm" @click="handleLogout">{{ t('auth.logout') }}</Button>
        </template>
        <template v-else>
          <RouterLink to="/login">
            <Button variant="outline" size="sm">{{ t('auth.login') }}</Button>
          </RouterLink>
        </template>
      </div>
    </div>
  </header>
</template>
