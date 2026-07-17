<script setup lang="ts">
import { ArrowLeft, Route } from 'lucide-vue-next'
import { useRouter } from 'vue-router'
import CommandButton from '../primitives/CommandButton.vue'
import CommandPanel from '../primitives/CommandPanel.vue'
import CommandSignal from '../primitives/CommandSignal.vue'

const router = useRouter()

function returnToCompetitions() {
  router.push({ name: 'competitions' })
}
</script>

<template>
  <section class="route-mismatch">
    <CommandPanel class="route-mismatch__panel" tone="warning">
      <Route class="size-6 text-[var(--v2-warning)]" />
      <div>
        <CommandSignal label="V2 route registration mismatch" tone="warning" />
        <h1>Route is not registered in this UI package</h1>
        <p>The selected UI package did not provide a page for this route.</p>
      </div>
      <CommandButton label="Competition registry" tone="outline" @click="returnToCompetitions">
        <template #icon><ArrowLeft class="size-4" /></template>
      </CommandButton>
    </CommandPanel>
  </section>
</template>

<style scoped>
.route-mismatch__panel { display: grid; min-height: 220px; grid-template-columns: auto minmax(0, 1fr) auto; align-items: center; gap: 18px; padding: 24px; }
.route-mismatch__panel h1 { margin: 7px 0 0; color: var(--v2-text); font-size: 18px; font-weight: 650; }
.route-mismatch__panel p { margin: 7px 0 0; color: var(--v2-text-muted); font-size: 12px; }
@media (max-width: 600px) { .route-mismatch__panel { grid-template-columns: auto minmax(0, 1fr); } .route-mismatch__panel :deep(.command-button) { grid-column: 2; justify-self: start; } }
</style>
