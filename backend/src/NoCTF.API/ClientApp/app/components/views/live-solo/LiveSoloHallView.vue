<script setup lang="ts">
import { toRefs } from 'vue'
import type { LiveSoloHallState } from '~/features/live-solo/useLiveSoloHall'
const props = defineProps<{ state: LiveSoloHallState }>()
const { competitionId, options, selected, current, configuration, loading, error, select, enter, recordings, settings, staff, load, stateKey, Workspace } = toRefs(props.state)
</script>
<template>
  <component :is="Workspace" :competition-id="competitionId" :show-challenge-navigator="false" :content-scroll="false">
    <div class="flex h-full min-h-0 flex-col gap-4">
      <header class="flex flex-wrap items-center justify-between gap-3"><h1 class="text-display text-2xl">{{ $t('liveSolo.hall') }}</h1><div class="flex gap-3"><Button v-if="staff" variant="outline" @click="settings">{{ $t('liveSolo.settings.title') }}</Button><Button variant="ghost" :disabled="loading" @click="load">{{ $t('common.label.refresh') }}</Button></div></header>
      <Alert v-if="error" variant="destructive"><AlertDescription>{{ $message(error) }}</AlertDescription></Alert>
      <Card v-if="configuration?.enabled === false"><CardContent><Empty><EmptyHeader><EmptyTitle>{{ $t('liveSolo.disabled') }}</EmptyTitle></EmptyHeader></Empty></CardContent></Card>
      <div v-else class="grid min-h-0 flex-1 gap-6 lg:grid-cols-[14rem_minmax(0,1fr)]">
        <ChoiceSidebar contained :items="options" :model-value="selected" :loading="loading" :label="$t('liveSolo.matches')" :loading-label="$t('liveSolo.matches')" :empty-label="$t('liveSolo.noMatches')" @update:model-value="select">
          <template #item="{ item }"><span class="flex min-w-0 flex-col gap-1"><span class="truncate font-semibold">{{ item.row.leftTeamName || '—' }}</span><span class="truncate">{{ item.row.rightTeamName || '—' }}</span><span class="text-xs text-muted-foreground">{{ $t(item.stateKey) }}</span></span></template>
        </ChoiceSidebar>
        <Card class="flex min-h-0 flex-col"><CardContent class="flex min-h-0 flex-1 flex-col justify-center gap-8 py-8">
          <Skeleton v-if="loading && !current" class="h-48" />
          <template v-else-if="current"><div class="flex flex-wrap items-center justify-center gap-3"><Badge>{{ $t(stateKey) }}</Badge><span class="text-sm text-muted-foreground">{{ $t('liveSolo.firstTo', { wins: current.requiredWins ?? 2 }) }}</span></div>
            <div class="grid grid-cols-[1fr_auto_1fr] items-center gap-3 text-center"><h2 class="min-w-0 break-words text-xl font-semibold">{{ current.leftTeamName || '—' }}</h2><div class="font-mono text-4xl font-bold tabular-nums text-primary">{{ current.leftWins ?? 0 }} : {{ current.rightWins ?? 0 }}</div><h2 class="min-w-0 break-words text-xl font-semibold">{{ current.rightTeamName || '—' }}</h2></div>
            <div class="flex flex-wrap justify-center gap-3"><Button @click="enter">{{ $t('liveSolo.enterMatch') }}</Button><Button v-if="staff || current.state === 'Completed'" variant="outline" @click="recordings">{{ $t(staff ? 'liveSolo.recording.staffTitle' : 'liveSolo.recording.replays') }}</Button></div>
          </template>
          <Empty v-else><EmptyHeader><EmptyTitle>{{ $t('liveSolo.noMatches') }}</EmptyTitle><EmptyDescription>{{ $t('liveSolo.noMatchesDescription') }}</EmptyDescription></EmptyHeader></Empty>
        </CardContent></Card>
      </div>
    </div>
  </component>
</template>
