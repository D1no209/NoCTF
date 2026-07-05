<script setup lang="ts">
import { computed, watch } from 'vue'
import { useRoute, RouterView, RouterLink } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { useAuthStore } from '@/stores/auth'
import { useScoreStore } from '@/stores/score'
import { Badge } from '@/components/ui/badge'
import LanguageSwitch from '@/components/LanguageSwitch.vue'
import { ChevronLeft, Users } from 'lucide-vue-next'

const { t } = useI18n()
const route = useRoute()
const auth = useAuthStore()
const scoreStore = useScoreStore()

const competitionId = computed(() => route.params.id as string)
const displayName = computed(() => scoreStore.myTeamName ?? auth.user?.userName ?? '')
const displayScore = computed(() => scoreStore.myTeamScore ?? 0)

watch(
  competitionId,
  (id) => {
    if (scoreStore.competitionId !== id) {
      scoreStore.setCurrentTeamScore(id, null, null, null)
    }
  },
  { immediate: true },
)
</script>

<template>
  <div class="min-h-[100dvh] flex flex-col bg-background">
    <header
      class="noctf-dark-shell sticky top-0 z-50 w-full border-b border-sidebar-foreground/10 text-sidebar-foreground"
    >
      <div
        class="mx-auto flex min-h-14 max-w-[1800px] flex-wrap items-center justify-between gap-2 px-3 py-2 sm:min-h-[4.5rem] sm:gap-3 sm:px-4 sm:py-3 md:flex-nowrap md:px-6"
      >
        <div class="flex min-w-0 items-center gap-2 sm:gap-4">
          <RouterLink
            to="/competitions"
            class="flex min-h-11 items-center gap-2 rounded-lg border border-sidebar-foreground/10 bg-sidebar-foreground/5 px-2.5 py-2 text-sm font-semibold text-sidebar-foreground transition-colors duration-[var(--motion-fast)] ease-[var(--ease-out-quint)] hover:bg-sidebar-foreground/10 sm:px-3 sm:py-2.5"
          >
            <ChevronLeft class="size-4" />
            <span class="text-sm font-medium hidden sm:inline">{{ t('nav.back') }}</span>
          </RouterLink>

          <div
            class="flex min-w-0 items-center gap-2 text-base font-bold tracking-tight sm:gap-3 sm:text-lg md:text-2xl"
          >
            <span class="noctf-logo size-8 md:size-10" />
            <span class="truncate"
              >NoCTF <span class="text-sidebar-foreground/60">/ {{ t('common.live') }}</span></span
            >
          </div>
        </div>

        <div class="flex min-w-0 items-center gap-2 sm:gap-3">
          <div
            v-if="displayName"
            class="flex min-w-0 items-center gap-2 rounded-lg border border-sidebar-foreground/10 bg-sidebar-foreground/5 px-2 py-2 sm:gap-3 sm:px-3"
          >
            <Users class="size-4 text-info" />
            <span class="hidden truncate text-sm font-medium md:inline">{{ displayName }}</span>
            <Badge variant="info" class="font-mono tabular-nums">
              {{ displayScore }}
            </Badge>
          </div>
          <LanguageSwitch />
        </div>
      </div>
    </header>

    <!-- Main Game Area -->
    <main class="relative flex-1">
      <RouterView v-slot="{ Component }">
        <transition name="fade" mode="out-in">
          <component :is="Component" :key="route.fullPath" />
        </transition>
      </RouterView>
    </main>
  </div>
</template>

<style scoped>
.fade-enter-active,
.fade-leave-active {
  transition:
    opacity var(--motion-fast) var(--ease-out-quint),
    transform var(--motion-fast) var(--ease-out-quint);
}

.fade-enter-from {
  opacity: 0;
  transform: translateY(4px);
}

.fade-leave-to {
  opacity: 0;
  transform: translateY(-2px);
}
</style>
