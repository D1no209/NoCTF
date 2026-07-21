<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, RouterView, RouterLink } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { Badge } from '@/ui-v1/components/ui/badge'
import { Button } from '@/ui-v1/components/ui/button'
import LanguageSwitch from '@/ui-v1/components/LanguageSwitch.vue'
import { ChevronLeft, Users } from 'lucide-vue-next'
import { useGameLayoutScore } from '@/features/chrome/useGameLayoutScore'

const { t } = useI18n()
const route = useRoute()
const { displayName, displayScore } = useGameLayoutScore()

const titleText = computed(() => `NoCTF / ${t('common.live')}`)
</script>

<template>
  <div class="min-h-[100dvh] flex flex-col bg-transparent">
    <header class="sticky top-0 z-50 w-full border-b-2 border-border bg-muted">
      <div class="mx-auto flex h-16 max-w-[1600px] items-center justify-between gap-4 px-4 md:px-6">
        <div class="flex min-w-0 items-center gap-4">
          <Button variant="outline" size="sm" as-child>
            <RouterLink to="/competitions" class="flex items-center gap-2">
              <ChevronLeft class="size-4" />
              <span class="hidden sm:inline">{{ t('nav.back') }}</span>
            </RouterLink>
          </Button>

          <div class="flex min-w-0 items-center gap-3 text-lg font-bold tracking-[0.08em] md:text-2xl">
            <span class="truncate">{{ titleText }}</span>
          </div>
        </div>

        <div class="flex min-w-0 items-center gap-3">
          <div
            v-if="displayName"
            class="flex min-w-0 items-center gap-2 border-2 border-border bg-card px-2 py-1.5 text-sm"
          >
            <Users class="size-4 text-foreground" />
            <span class="hidden truncate font-medium md:inline">{{ displayName }}</span>
            <Badge v-if="displayScore !== null" variant="default" class="font-mono tabular-nums">
              {{ displayScore }}
            </Badge>
          </div>
          <div class="shrink-0">
            <LanguageSwitch />
          </div>
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
