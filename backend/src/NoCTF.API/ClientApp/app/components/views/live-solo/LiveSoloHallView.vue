<script setup lang="ts">
import { toRefs } from 'vue'
import type { LiveSoloHallState } from '~/features/live-solo/useLiveSoloHall'
const props = defineProps<{ state: LiveSoloHallState }>()
const { competitionId, options, selected, current, configuration, loading, error, select, enter, recordings, settings, bracket, groups, postgame, finished, staff, load, stateKey, Workspace } = toRefs(props.state)
</script>
<template>
  <component :is="Workspace" :competition-id="competitionId" :show-challenge-navigator="false" :content-scroll="true">
    <div class="flex min-h-full min-w-0 flex-col gap-4">
      <header class="flex flex-wrap items-center justify-between gap-3"><h1 class="text-display text-2xl">{{ $t('liveSolo.hall') }}</h1><div class="grid grid-cols-2 gap-2 sm:flex sm:flex-wrap"><Button v-if="staff" variant="outline" @click="groups">{{ $t('liveSolo.groups.title') }}</Button><Button v-if="staff" variant="outline" @click="bracket">{{ $t('liveSolo.bracket.title') }}</Button><Button v-if="staff" variant="outline" @click="settings">{{ $t('liveSolo.settings.title') }}</Button><Button variant="ghost" :disabled="loading" @click="load">{{ $t('common.label.refresh') }}</Button></div></header>
      <Alert v-if="error" variant="destructive"><AlertDescription>{{ $message(error) }}</AlertDescription></Alert>
      <Card v-if="configuration?.enabled === false"><CardContent><Empty><EmptyHeader><EmptyTitle>{{ $t('liveSolo.disabled') }}</EmptyTitle></EmptyHeader></Empty></CardContent></Card>
      <div v-else class="grid min-w-0 flex-1 items-start gap-4 lg:grid-cols-[14rem_minmax(0,1fr)]">
        <div class="min-w-0 lg:hidden"><ChoicePicker :items="options" :model-value="selected" :label="$t('liveSolo.matches')" :search-label="$t('liveSolo.bracket.searchTeam')" :empty-label="$t('liveSolo.noMatches')" @update:model-value="select" /></div>
        <div class="hidden h-96 min-w-0 lg:block"><ChoiceSidebar contained :items="options" :model-value="selected" :loading="loading" :label="$t('liveSolo.matches')" :loading-label="$t('liveSolo.matches')" :empty-label="$t('liveSolo.noMatches')" @update:model-value="select">
          <template #item="{ item }"><span class="flex min-w-0 flex-col gap-1"><span class="truncate font-semibold">{{ item.row.leftTeamName || '—' }}</span><span class="truncate">{{ item.row.rightTeamName || '—' }}</span><span class="text-xs text-muted-foreground">{{ $t(item.stateKey) }}</span></span></template>
        </ChoiceSidebar></div>
        <Card class="min-w-0 lg:min-h-96"><CardContent class="flex flex-col justify-center gap-6 py-6">
          <Skeleton v-if="loading && !current" class="h-48" />
          <template v-else-if="current"><div class="flex flex-wrap items-center justify-center gap-3"><Badge>{{ $t(stateKey) }}</Badge><span class="text-sm text-muted-foreground">{{ $t('liveSolo.firstTo', { wins: current.requiredWins ?? 2 }) }}</span></div>
            <div class="grid grid-cols-[minmax(0,1fr)_auto_minmax(0,1fr)] items-center gap-3 text-center"><h2 class="min-w-0 break-words text-base font-semibold md:text-xl">{{ current.leftTeamName || '—' }}</h2><div class="font-mono text-2xl font-bold md:text-4xl tabular-nums text-primary">{{ current.leftWins ?? 0 }} : {{ current.rightWins ?? 0 }}</div><h2 class="min-w-0 break-words text-base font-semibold md:text-xl">{{ current.rightTeamName || '—' }}</h2></div>
            <div class="flex flex-wrap justify-center gap-3"><Button @click="enter">{{ $t('liveSolo.enterMatch') }}</Button><Button v-if="finished" variant="outline" @click="postgame">{{ $t('liveSolo.postgame.title') }}</Button><Button v-if="staff || current.state === 'Completed'" variant="outline" @click="recordings">{{ $t(staff ? 'liveSolo.recording.staffTitle' : 'liveSolo.recording.replays') }}</Button></div>
          </template>
          <Empty v-else><EmptyHeader><EmptyTitle>{{ $t('liveSolo.noMatches') }}</EmptyTitle><EmptyDescription>{{ $t('liveSolo.noMatchesDescription') }}</EmptyDescription></EmptyHeader></Empty>
        </CardContent></Card>
      </div>
    </div>
  </component>
</template>
