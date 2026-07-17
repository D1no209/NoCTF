<script setup lang="ts">
import { computed } from 'vue'
import { Bell, Blocks, Command, LayoutDashboard, Palette, Search, Settings, UsersRound } from 'lucide-vue-next'
import { useRoute, useRouter } from 'vue-router'
import CommandIconButton from '../primitives/CommandIconButton.vue'
import CommandSignal from '../primitives/CommandSignal.vue'

const route = useRoute()
const router = useRouter()
const navigation = [
  { label: 'Overview', icon: LayoutDashboard, to: '/' },
  { label: 'Competitions', icon: Blocks, to: '/competitions' },
  { label: 'Teams', icon: UsersRound, to: '/teams' },
  { label: 'Theme packages', icon: Palette, to: '/admin/theme-packs' },
]

const activeItem = computed(() => {
  if (route.name === 'competitions' || route.name === 'competition-detail' || route.name === 'competition-register' || route.name === 'awd-dashboard')
    return navigation[1]!
  if (route.name === 'teams')
    return navigation[2]!
  return navigation.find(item => item.to === route.path) ?? navigation[0]!
})
const sectionTitle = computed(() => activeItem.value.label.toUpperCase())
const contextSignal = computed(() => {
  if (route.name === 'competitions' || route.name === 'competition-detail' || route.name === 'competition-register' || route.name === 'awd-dashboard')
    return { label: 'Competition API / contract data', tone: 'success' as const }
  if (route.name === 'teams')
    return { label: 'Team API / account data', tone: 'success' as const }
  if (route.name === 'admin-theme-packs')
    return { label: 'Local package registry', tone: 'info' as const }
  return { label: 'Development mock / AWDP snapshot', tone: 'warning' as const }
})

function navigate(to: string) {
  if (route.path !== to)
    router.push(to)
}
</script>

<template>
  <div class="ui-v2">
    <aside class="v2-rail">
      <div class="v2-rail__brand">
        <img src="/logo.png" alt="NoCTF" />
        <span>COMMAND</span>
      </div>
      <nav class="v2-rail__nav" aria-label="Command navigation">
        <CommandIconButton
          v-for="item in navigation"
          :key="item.label"
          :icon="item.icon"
          :label="item.label"
          :active="item === activeItem"
          @click="navigate(item.to)"
        />
      </nav>
      <div class="v2-rail__footer">
        <CommandIconButton :icon="Settings" label="Theme packages" compact @click="navigate('/admin/theme-packs')" />
      </div>
    </aside>

    <div class="v2-main">
      <header class="v2-command-bar">
        <div class="v2-command-bar__title">
          <Command class="size-4 text-[var(--v2-primary)]" />
          <span>{{ sectionTitle }}</span>
          <span class="v2-command-bar__divider" aria-hidden="true" />
          <CommandSignal :label="contextSignal.label" :tone="contextSignal.tone" />
        </div>
        <div class="v2-command-bar__actions">
          <CommandIconButton :icon="Search" label="Search" compact />
          <CommandIconButton :icon="Bell" label="Notifications" compact />
          <span class="v2-command-bar__operator">OP-01</span>
        </div>
      </header>
      <main class="v2-main__content">
        <slot />
      </main>
    </div>
  </div>
</template>

<style scoped>
.v2-rail {
  position: fixed;
  z-index: 10;
  top: 0;
  bottom: 0;
  left: 0;
  display: flex;
  width: 216px;
  flex-direction: column;
  border-right: 1px solid var(--v2-line);
  background: rgb(5 8 22 / 0.95);
}

.v2-rail__brand {
  display: flex;
  height: 72px;
  align-items: center;
  gap: 12px;
  border-bottom: 1px solid var(--v2-line);
  padding: 0 18px;
}

.v2-rail__brand img { width: 96px; height: auto; filter: brightness(0) invert(1) sepia(0.2) saturate(2.2) hue-rotate(173deg); }
.v2-rail__brand span { color: var(--v2-cyan); font-family: ui-monospace, SFMono-Regular, Menlo, monospace; font-size: 9px; font-weight: 800; letter-spacing: 0.12em; }
.v2-rail__nav { display: grid; gap: 4px; padding: 14px 10px; }
.v2-rail__nav :deep(.command-icon-button) { width: 100%; }
.v2-rail__footer { margin-top: auto; display: flex; justify-content: flex-end; border-top: 1px solid var(--v2-line); padding: 12px; }

.v2-main { min-height: 100dvh; padding-left: 216px; }
.v2-command-bar { position: sticky; z-index: 5; top: 0; display: flex; min-height: 48px; align-items: center; justify-content: space-between; gap: 16px; border-bottom: 1px solid var(--v2-line); background: rgb(5 8 22 / 0.9); padding: 0 20px; backdrop-filter: blur(14px); }
.v2-command-bar__title, .v2-command-bar__actions { display: flex; min-width: 0; align-items: center; gap: 10px; }
.v2-command-bar__title > span:first-of-type { color: var(--v2-text); font-family: ui-monospace, SFMono-Regular, Menlo, monospace; font-size: 11px; font-weight: 700; letter-spacing: 0.08em; }
.v2-command-bar__divider { width: 1px; height: 16px; background: var(--v2-line); }
.v2-command-bar__operator { border-left: 1px solid var(--v2-line); padding-left: 12px; color: var(--v2-text-muted); font-family: ui-monospace, SFMono-Regular, Menlo, monospace; font-size: 11px; }
.v2-main__content { width: min(1720px, 100%); margin: 0 auto; padding: 20px; }

@media (max-width: 880px) {
  .v2-rail { width: 58px; }
  .v2-rail__brand { justify-content: center; padding: 0; }
  .v2-rail__brand img, .v2-rail__brand span, .v2-rail__nav :deep(.command-icon-button__label) { display: none; }
  .v2-rail__nav { padding: 12px; }
  .v2-rail__nav :deep(.command-icon-button) { width: 34px; padding: 0; justify-content: center; }
  .v2-main { padding-left: 58px; }
}

@media (max-width: 680px) {
  .v2-command-bar__title .command-signal { display: none; }
  .v2-main__content { padding: 12px; }
}
</style>
