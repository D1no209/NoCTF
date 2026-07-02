<script setup lang="ts">
import { computed } from 'vue'
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

const displayName = computed(() => scoreStore.myTeamName ?? auth.user?.userName ?? '')
const displayScore = computed(() => scoreStore.myTeamScore ?? 0)
</script>

<template>
  <div class="min-h-[100dvh] flex flex-col bg-background">
    <header class="noctf-dark-shell sticky top-0 z-50 w-full border-b border-white/10 text-white">
      <div class="mx-auto flex h-20 max-w-[1800px] items-center justify-between gap-4 px-4 md:px-6">
        <div class="flex items-center gap-4">
          <RouterLink 
            to="/competitions"
            class="flex items-center gap-2 rounded-lg border border-white/10 bg-white/5 px-4 py-3 text-sm font-semibold text-white transition-colors hover:bg-white/10"
          >
            <ChevronLeft class="size-4" />
            <span class="text-sm font-medium hidden sm:inline">{{ t('nav.back') }}</span>
          </RouterLink>
        </div>

        <div class="absolute left-1/2 flex -translate-x-1/2 items-center gap-3 text-2xl font-bold tracking-tight">
          <span class="noctf-logo size-10" />
          <span>NoCTF <span class="text-slate-400">/ Game Shell</span></span>
        </div>

        <div class="flex items-center gap-3">
          <div v-if="displayName" class="flex items-center gap-3 rounded-lg border border-white/10 bg-white/5 px-3 py-2">
            <Users class="size-4 text-blue-400" />
            <span class="hidden text-sm font-medium md:inline">{{ displayName }}</span>
            <Badge variant="default" class="font-mono tabular-nums">
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
        <transition
          name="fade"
          mode="out-in"
        >
          <component :is="Component" :key="route.fullPath" />
        </transition>
      </RouterView>
    </main>
  </div>
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
