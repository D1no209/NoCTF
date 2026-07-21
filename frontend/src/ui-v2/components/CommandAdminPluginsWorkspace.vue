<script setup lang="ts">
import type { FunctionalComponent } from 'vue'
import { CheckCircle2, Gamepad2, HardDrive, Plug, Puzzle, ShieldCheck } from 'lucide-vue-next'
import CommandBadge from '../primitives/CommandBadge.vue'
import CommandButton from '../primitives/CommandButton.vue'
import CommandPanel from '../primitives/CommandPanel.vue'
import CommandSignal from '../primitives/CommandSignal.vue'
import CommandPageHeader from './CommandPageHeader.vue'

export interface CommandAdminPlugin {
  name: string
  type: string
  version: string
}

const props = defineProps<{
  state: 'loading' | 'error' | 'ready'
  errorMessage?: string
  plugins: CommandAdminPlugin[]
}>()

const emit = defineEmits<{
  retry: []
}>()

function pluginIcon(type: string): FunctionalComponent {
  const value = type.toLowerCase()
  if (value.includes('gamemode'))
    return Gamepad2
  if (value.includes('challenge'))
    return Puzzle
  if (value.includes('container'))
    return HardDrive
  return Plug
}

function pluginTone(type: string): 'primary' | 'info' | 'default' {
  const value = type.toLowerCase()
  if (value.includes('gamemode'))
    return 'primary'
  if (value.includes('challenge'))
    return 'info'
  return 'default'
}
</script>

<template>
  <section class="admin-plugins">
    <CommandPageHeader
      signal-label="Admin API / plugin registry"
      signal-tone="warning"
      title="Installed plugins"
      description="Review the game mode, challenge, and container plugins registered in the core engine."
      :stat-icon="Plug"
      :stat-value="String(props.plugins.length).padStart(2, '0')"
      stat-label="plugins"
    />

    <CommandPanel v-if="props.plugins.length" class="admin-plugins__summary">
      <div class="admin-plugins__summary-item">
        <span>Total plugins</span>
        <CommandBadge :label="String(props.plugins.length)" tone="primary" />
      </div>
      <span class="admin-plugins__summary-divider" aria-hidden="true" />
      <div class="admin-plugins__summary-item">
        <ShieldCheck class="size-4 text-[var(--v2-cyan)]" />
        <span>Core engine</span>
        <span class="admin-plugins__summary-version">v1.0.0</span>
      </div>
    </CommandPanel>

    <CommandPanel v-if="props.state === 'error'" class="admin-plugins__state" tone="warning">
      <h2>Unable to load plugins</h2>
      <p>{{ props.errorMessage || 'The service did not return a usable plugin list.' }}</p>
      <CommandButton label="Retry" tone="outline" @click="emit('retry')" />
    </CommandPanel>

    <CommandPanel v-else-if="props.state === 'loading'" class="admin-plugins__state" aria-busy="true">
      <CommandSignal label="Synchronizing" tone="info" />
      <p>Loading the plugin registry.</p>
    </CommandPanel>

    <CommandPanel v-else-if="props.plugins.length === 0" class="admin-plugins__state">
      <Plug class="size-8 text-[var(--v2-text-faint)]" />
      <h2>No plugins installed</h2>
      <p>Register a plugin assembly with the core engine to extend the platform.</p>
    </CommandPanel>

    <div v-else class="admin-plugins__grid">
      <CommandPanel v-for="plugin in props.plugins" :key="plugin.name" class="admin-plugins__card">
        <div class="admin-plugins__card-head">
          <span class="admin-plugins__card-icon">
            <component :is="pluginIcon(plugin.type)" class="size-5" />
          </span>
          <CommandBadge :label="`v${plugin.version}`" />
        </div>
        <div class="admin-plugins__card-body">
          <strong>{{ plugin.name }}</strong>
          <span class="admin-plugins__card-type">
            <span class="admin-plugins__card-dot" aria-hidden="true" />
            {{ plugin.type }}
          </span>
        </div>
        <div class="admin-plugins__card-foot">
          <CommandBadge label="Active" :tone="pluginTone(plugin.type)" />
          <span class="admin-plugins__verified">
            <CheckCircle2 class="size-3 text-[var(--v2-cyan)]" />
            Verified
          </span>
        </div>
      </CommandPanel>
    </div>
  </section>
</template>

<style scoped>
.admin-plugins { display: grid; gap: 16px; }
.admin-plugins__summary { display: flex; flex-wrap: wrap; align-items: center; gap: 18px; padding: 14px 18px; }
.admin-plugins__summary-item { display: flex; align-items: center; gap: 8px; color: var(--v2-text-muted); font-size: 13px; }
.admin-plugins__summary-divider { width: 1px; height: 18px; background: var(--v2-surface); box-shadow: var(--v2-inset); }
.admin-plugins__summary-version { color: var(--v2-text-faint); font-family: var(--v2-font-mono); font-size: 11px; }
.admin-plugins__state { display: grid; min-height: 160px; align-content: center; justify-items: start; gap: 10px; padding: 24px; }
.admin-plugins__state h2 { margin: 0; color: var(--v2-text); font-size: 17px; font-weight: 600; }
.admin-plugins__state p { margin: 0; color: var(--v2-text-muted); font-size: 13px; }
.admin-plugins__grid { display: grid; gap: 14px; grid-template-columns: repeat(auto-fill, minmax(230px, 1fr)); }
.admin-plugins__card { display: grid; gap: 14px; padding: 16px; }
.admin-plugins__card-head { display: flex; align-items: center; justify-content: space-between; }
.admin-plugins__card-icon { display: grid; width: 40px; height: 40px; place-items: center; border-radius: 12px; color: var(--v2-primary); background: var(--v2-surface); box-shadow: var(--v2-inset); }
.admin-plugins__card-body { display: grid; gap: 5px; min-width: 0; }
.admin-plugins__card-body strong { overflow: hidden; color: var(--v2-text); font-size: 14px; font-weight: 600; text-overflow: ellipsis; white-space: nowrap; }
.admin-plugins__card-type { display: flex; align-items: center; gap: 6px; color: var(--v2-text-faint); font-size: 11px; }
.admin-plugins__card-dot { width: 5px; height: 5px; border-radius: 999px; background: var(--v2-primary); }
.admin-plugins__card-foot { display: flex; align-items: center; justify-content: space-between; }
.admin-plugins__verified { display: flex; align-items: center; gap: 5px; color: var(--v2-text-faint); font-size: 10px; }
</style>
