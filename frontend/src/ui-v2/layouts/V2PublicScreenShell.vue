<script setup lang="ts">
import { ArrowLeft, Command } from 'lucide-vue-next'
import { useRouter } from 'vue-router'
import CommandButton from '../primitives/CommandButton.vue'

const router = useRouter()

function returnToCompetition() {
  router.back()
}
</script>

<template>
  <div class="ui-v2 v2-public-screen">
    <header class="v2-public-screen__bar">
      <div class="v2-public-screen__brand">
        <img src="/logo.png" alt="NoCTF" />
        <span>LIVE OPERATIONS DISPLAY</span>
      </div>
      <CommandButton label="Return" tone="ghost" @click="returnToCompetition">
        <template #icon>
          <ArrowLeft class="size-4" />
        </template>
      </CommandButton>
    </header>
    <main class="v2-public-screen__content">
      <slot />
    </main>
    <footer class="v2-public-screen__footer">
      <Command class="size-3.5" />
      <span>NOCTF / AWDP TELEMETRY</span>
    </footer>
  </div>
</template>

<style scoped>
.v2-public-screen {
  display: grid;
  min-height: 100dvh;
  grid-template-rows: 60px minmax(0, 1fr) 34px;
}

.v2-public-screen__bar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0 20px;
  background: var(--v2-canvas);
  box-shadow: 0 1px 0 var(--v2-shadow-light), 0 8px 16px rgb(184 188 194 / 0.3);
}

.v2-public-screen__brand {
  display: flex;
  min-width: 0;
  align-items: center;
  gap: 12px;
}

.v2-public-screen__brand img {
  width: 86px;
  height: auto;
  filter: brightness(0) opacity(0.72);
}

.v2-public-screen__brand span,
.v2-public-screen__footer {
  color: var(--v2-text-muted);
  font-family: var(--v2-font-mono);
  font-size: 10px;
  font-weight: 600;
  letter-spacing: 0.05em;
}

.v2-public-screen__content {
  min-width: 0;
  padding: 18px;
}

.v2-public-screen__footer {
  display: flex;
  align-items: center;
  justify-content: flex-start;
  gap: 8px;
  padding: 0 20px;
  background: var(--v2-canvas);
  box-shadow: 0 -1px 0 var(--v2-shadow-light), 0 -8px 16px rgb(184 188 194 / 0.24);
}

@media (max-width: 560px) {
  .v2-public-screen__content { padding: 12px; }
  .v2-public-screen__brand span { display: none; }
}
</style>
