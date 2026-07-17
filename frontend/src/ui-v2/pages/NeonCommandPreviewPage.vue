<script setup lang="ts">
import { RadioTower, RefreshCw } from 'lucide-vue-next'
import { onMounted, ref } from 'vue'
import CommandEventFeed from '../components/CommandEventFeed.vue'
import CommandMetricStrip from '../components/CommandMetricStrip.vue'
import CommandNetworkTrace from '../components/CommandNetworkTrace.vue'
import CommandRankTable from '../components/CommandRankTable.vue'
import CommandServiceMatrix from '../components/CommandServiceMatrix.vue'
import CommandIconButton from '../primitives/CommandIconButton.vue'
import CommandPanel from '../primitives/CommandPanel.vue'
import CommandSignal from '../primitives/CommandSignal.vue'
import { loadSharedThemePreviewMock, type CommandPreviewModel } from '../mock/shared-theme-mock'

const preview = ref<CommandPreviewModel | null>(null)
const isRefreshing = ref(false)

async function refreshPreview() {
  isRefreshing.value = true
  try {
    preview.value = await loadSharedThemePreviewMock()
  }
  finally {
    isRefreshing.value = false
  }
}

onMounted(refreshPreview)
</script>

<template>
  <template v-if="preview">
    <section class="command-page-heading">
      <div>
        <CommandSignal label="NoCTF / Operational overview" tone="success" />
        <h1>Competition command center</h1>
        <p>Shared development mock from the first UI package, rendered through the independent Neon Command UI package.</p>
      </div>
      <div class="command-page-heading__actions">
        <span><RadioTower class="size-4" /> snapshot {{ preview.snapshotTime }}</span>
        <CommandIconButton :icon="RefreshCw" :label="isRefreshing ? 'Refreshing mock' : 'Refresh mock'" @click="refreshPreview" />
      </div>
    </section>

    <CommandMetricStrip :metrics="preview.metrics" />

    <div class="command-dashboard">
      <div class="command-dashboard__primary">
        <CommandNetworkTrace :trace="preview.trace" />
        <CommandEventFeed :events="preview.events" />
      </div>
      <div class="command-dashboard__secondary">
        <CommandRankTable :teams="preview.teams" />
        <CommandServiceMatrix :services="preview.services" />
      </div>
    </div>
  </template>

  <CommandPanel v-else class="command-preview-state">
    <CommandSignal label="Development preview unavailable" tone="warning" />
    <h1>Shared mock is only available in development.</h1>
  </CommandPanel>
</template>

<style scoped>
.command-page-heading {
  display: flex;
  align-items: flex-end;
  justify-content: space-between;
  gap: 20px;
  margin-bottom: 20px;
  padding: 4px 2px 0;
}

.command-page-heading h1 {
  margin: 8px 0 0;
  color: var(--v2-text);
  font-size: 24px;
  font-weight: 600;
  letter-spacing: 0;
}

.command-page-heading p {
  max-width: 720px;
  margin: 8px 0 0;
  color: var(--v2-text-muted);
  font-size: 13px;
}

.command-page-heading__actions {
  display: flex;
  align-items: center;
  gap: 12px;
}

.command-page-heading__actions > span {
  display: inline-flex;
  align-items: center;
  gap: 7px;
  border-radius: 999px;
  padding: 6px 12px;
  background: var(--v2-surface);
  box-shadow: var(--v2-inset);
  color: var(--v2-text-muted);
  font-family: var(--v2-font-mono);
  font-size: 11px;
}

.command-dashboard {
  display: grid;
  grid-template-columns: minmax(0, 1.15fr) minmax(390px, 0.85fr);
  gap: 16px;
  margin-top: 16px;
}

.command-dashboard__primary,
.command-dashboard__secondary {
  display: grid;
  min-width: 0;
  gap: 16px;
}

.command-dashboard__primary { grid-template-rows: minmax(300px, 0.9fr) minmax(350px, 1.1fr); }
.command-dashboard__secondary { grid-template-rows: minmax(330px, 1fr) minmax(300px, 0.9fr); }

.command-preview-state {
  display: grid;
  min-height: 220px;
  place-content: center;
  gap: 10px;
  padding: 26px;
}

.command-preview-state h1 {
  margin: 0;
  color: var(--v2-text);
  font-size: 17px;
  font-weight: 600;
}

@media (max-width: 1120px) {
  .command-dashboard { grid-template-columns: 1fr; }
  .command-dashboard__primary,
  .command-dashboard__secondary { grid-template-rows: auto; }
}

@media (max-width: 680px) {
  .command-page-heading { align-items: flex-start; flex-direction: column; }
  .command-page-heading__actions { width: 100%; justify-content: space-between; }
}
</style>
