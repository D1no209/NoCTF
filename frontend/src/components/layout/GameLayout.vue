<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, RouterView, RouterLink } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { useAuthStore } from '@/stores/auth'
import { useScoreStore } from '@/stores/score'
import { Badge } from '@/components/ui/badge'
import LanguageSwitch from '@/components/LanguageSwitch.vue'
import { ChevronLeft, Trophy } from 'lucide-vue-next'

const { t } = useI18n()
const route = useRoute()
const auth = useAuthStore()
const scoreStore = useScoreStore()

const competitionId = computed(() => route.params.id as string)
const displayName = computed(() => scoreStore.myTeamName ?? auth.user?.userName ?? '')
const displayScore = computed(() => scoreStore.myTeamScore ?? 0)
</script>

<template>
  <div class="min-h-screen flex flex-col bg-background">
    <!-- Immersive Header -->
    <header class="sticky top-0 z-50 w-full border-b bg-background/95 backdrop-blur supports-[backdrop-filter]:bg-background/60">
      <div class="flex h-14 items-center justify-between px-4 gap-4">
        <div class="flex items-center gap-4">
          <RouterLink 
            :to="`/competitions/${competitionId}`" 
            class="flex items-center gap-2 text-muted-foreground hover:text-foreground transition-colors"
          >
            <ChevronLeft class="size-4" />
            <span class="text-sm font-medium hidden sm:inline">{{ t('nav.back') }}</span>
          </RouterLink>
          
          <div class="h-4 w-px bg-border hidden sm:block" />
          
          <div class="flex items-center gap-2 font-bold tracking-tight">
            <Trophy class="size-5 text-primary" />
            <span>NoCTF <span class="text-muted-foreground font-normal ml-1">Game Shell</span></span>
          </div>
        </div>

        <div class="flex items-center gap-3">
          <div v-if="displayName" class="flex items-center gap-2 px-3 py-1 rounded-full bg-muted/50 border">
            <span class="text-xs font-medium text-muted-foreground hidden md:inline">{{ displayName }}</span>
            <Badge variant="secondary" class="font-mono tabular-nums text-[10px] h-5 px-1.5">
              {{ displayScore }}
            </Badge>
          </div>
          
          <div class="h-4 w-px bg-border" />
          
          <LanguageSwitch />
        </div>
      </div>
    </header>

    <!-- Main Game Area -->
    <main class="flex-1 relative">
      <transition
        name="fade"
        mode="out-in"
      >
        <RouterView :key="route.fullPath" />
      </transition>
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
