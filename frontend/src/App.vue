<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import PixelBlast from '@/components/PixelBlast.vue'
import TargetCursor from '@/components/TargetCursor.vue'
import { Toaster } from '@/components/ui/sonner'
import { useThemePackages } from '@/composables/useThemePackages'
import V2Application from '@/ui-v2/V2Application.vue'

const { activeTheme } = useThemePackages()
const route = useRoute()
const v2RouteNames = new Set([
  'home',
  'competitions',
  'competition-detail',
  'competition-register',
  'teams',
  'awdp-screen',
  'awd-dashboard',
  'admin-theme-packs',
])
const isV2Package = computed(() => activeTheme.value.uiPackage === 'v2' && v2RouteNames.has(String(route.name)))
const effectColor = computed(() => activeTheme.value.tokens['--app-effect-color'] || activeTheme.value.tokens['--primary'])
const effectOpacity = computed(() => Number(activeTheme.value.tokens['--app-effect-opacity'] || '0.28'))
</script>

<template>
  <div class="app-pixel-shell" :class="{ 'app-pixel-shell--v2': isV2Package }">
    <div v-if="!isV2Package" class="app-pixel-bg" :style="{ opacity: effectOpacity }" aria-hidden="true">
      <PixelBlast
        variant="square"
        :pixel-size="6"
        :color="effectColor"
        :pattern-scale="2.6"
        :pattern-density="1.05"
        :pixel-size-jitter="0.22"
        :enable-ripples="true"
        :ripple-speed="0.28"
        :ripple-thickness="0.1"
        :ripple-intensity-scale="0.7"
        :liquid="false"
        :speed="0.42"
        :edge-fade="0.12"
        :transparent="true"
      />
    </div>

    <div class="app-pixel-content">
      <V2Application v-if="isV2Package" />
      <RouterView v-else />
      <Toaster position="top-right" rich-colors close-button />
    </div>
    <TargetCursor
      v-if="!isV2Package"
      target-selector=".cursor-target, button, a, input, select, textarea, [role='button']"
      :spin-duration="2.4"
      :hide-default-cursor="true"
      :hover-duration="0.18"
      :parallax-on="true"
    />
  </div>
</template>

<style scoped>
.app-pixel-shell {
  position: relative;
  min-height: 100dvh;
}

.app-pixel-bg {
  position: fixed;
  inset: 0;
  z-index: 0;
  pointer-events: none;
  background: linear-gradient(180deg, var(--app-background-start) 0%, var(--app-background-end) 100%);
}

.app-pixel-content {
  position: relative;
  z-index: 1;
}
</style>
