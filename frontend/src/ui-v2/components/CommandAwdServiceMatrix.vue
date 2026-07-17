<script setup lang="ts">
import { Activity } from 'lucide-vue-next'
import { computed } from 'vue'
import type { CommandAwdService } from './awd-contract'
import CommandPanel from '../primitives/CommandPanel.vue'
import CommandSignal from '../primitives/CommandSignal.vue'

const props = defineProps<{
  services: CommandAwdService[]
}>()

const challenges = computed(() => uniqueBy(props.services, service => service.challengeId, service => ({
  id: service.challengeId,
  name: service.challengeName,
})))
const teams = computed(() => uniqueBy(props.services, service => service.teamId, service => ({
  id: service.teamId,
  name: service.teamName,
})))

function getStatus(teamId: string, challengeId: string) {
  return props.services.find(service => service.teamId === teamId && service.challengeId === challengeId)?.status ?? 'unknown'
}

function labelFor(status: CommandAwdService['status']) {
  if (status === 'healthy')
    return 'UP'
  if (status === 'down')
    return 'DOWN'
  return 'N/A'
}

function toneFor(status: CommandAwdService['status']) {
  if (status === 'healthy')
    return 'success' as const
  if (status === 'down')
    return 'danger' as const
  return 'warning' as const
}

function uniqueBy<T, R>(items: T[], key: (item: T) => string, map: (item: T) => R) {
  const seen = new Set<string>()
  return items.filter((item) => {
    const value = key(item)
    if (!value || seen.has(value))
      return false
    seen.add(value)
    return true
  }).map(map)
}
</script>

<template>
  <CommandPanel class="awd-service-matrix">
    <header class="awd-service-matrix__header">
      <div>
        <CommandSignal label="Checker telemetry" tone="success" />
        <h2>Service matrix</h2>
      </div>
      <Activity class="size-4 text-[var(--v2-cyan)]" />
    </header>

    <div v-if="teams.length === 0 || challenges.length === 0" class="awd-service-matrix__empty">
      Waiting for service health data.
    </div>

    <div v-else class="awd-service-matrix__viewport">
      <div
        class="awd-service-matrix__table"
        :style="{ gridTemplateColumns: `minmax(128px, 1fr) repeat(${challenges.length}, minmax(56px, 0.45fr))` }"
      >
        <div class="awd-service-matrix__label">Team</div>
        <div v-for="challenge in challenges" :key="challenge.id" class="awd-service-matrix__label" :title="challenge.name">
          {{ challenge.name }}
        </div>
        <template v-for="team in teams" :key="team.id">
          <div class="awd-service-matrix__team" :title="team.name">{{ team.name }}</div>
          <div v-for="challenge in challenges" :key="challenge.id" class="awd-service-matrix__cell">
            <CommandSignal
              :label="labelFor(getStatus(team.id, challenge.id))"
              :tone="toneFor(getStatus(team.id, challenge.id))"
            />
          </div>
        </template>
      </div>
    </div>
  </CommandPanel>
</template>

<style scoped>
.awd-service-matrix { display: flex; min-height: 0; flex-direction: column; }
.awd-service-matrix__header { display: flex; min-height: 68px; align-items: center; justify-content: space-between; border-bottom: 1px solid var(--v2-line); padding: 12px 14px; }
.awd-service-matrix__header h2 { margin: 5px 0 0; font-size: 16px; font-weight: 650; }
.awd-service-matrix__viewport { overflow-x: auto; }
.awd-service-matrix__table { display: grid; min-width: 420px; }
.awd-service-matrix__label,
.awd-service-matrix__team,
.awd-service-matrix__cell { display: flex; min-width: 0; align-items: center; border-right: 1px solid rgb(26 58 103 / 0.65); border-bottom: 1px solid rgb(26 58 103 / 0.65); padding: 0 10px; }
.awd-service-matrix__label { min-height: 34px; justify-content: center; overflow: hidden; color: var(--v2-text-faint); font-size: 9px; font-weight: 700; letter-spacing: 0.06em; text-align: center; text-overflow: ellipsis; text-transform: uppercase; white-space: nowrap; }
.awd-service-matrix__label:first-child { justify-content: flex-start; }
.awd-service-matrix__team { min-height: 42px; overflow: hidden; color: var(--v2-text); font-family: ui-monospace, SFMono-Regular, Menlo, monospace; font-size: 11px; font-weight: 650; text-overflow: ellipsis; white-space: nowrap; }
.awd-service-matrix__cell { min-height: 42px; justify-content: center; padding: 0 5px; }
.awd-service-matrix__cell :deep(.command-signal) { font-size: 9px; letter-spacing: 0.05em; }
.awd-service-matrix__empty { display: grid; min-height: 220px; place-items: center; padding: 18px; color: var(--v2-text-muted); font-size: 12px; }
</style>
