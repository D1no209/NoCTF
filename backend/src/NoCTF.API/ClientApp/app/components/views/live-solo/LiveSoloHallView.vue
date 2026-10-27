<script setup lang="ts">
import { toRefs } from 'vue'
import type { LiveSoloHallState } from '~/features/live-solo/useLiveSoloHall'
const props = defineProps<{ state: LiveSoloHallState }>()
const { competitionId, options, selected, current, configuration, loading, error, select, enter, recordings, postgame, finished, staff, management, reviewMode, hasStaffAccess, manage, load, stateKey, Workspace, activityRows, endReasonKey,
  canManage, deleteOpen, deleteLabel, deleting, deleteError, askDelete, setDeleteOpen, removeMatch } = toRefs(props.state)
</script>
<template>
  <component :is="Workspace" :competition-id="competitionId" :show-challenge-navigator="false" :show-supporting-panels="!management" :content-scroll="!management">
    <div class="flex min-h-full min-w-0 flex-col gap-4">
      <header class="flex flex-wrap items-center justify-between gap-3"><h1 class="text-display text-2xl">{{ $t(reviewMode ? management ? 'liveSolo.navigation.postgameReview' : 'liveSolo.postgame.title' : management ? 'liveSolo.navigation.matches' : 'liveSolo.hall') }}</h1><div class="grid grid-cols-2 gap-2 sm:flex sm:flex-wrap"><Button v-if="!management && hasStaffAccess" variant="outline" @click="manage">{{ $t('liveSolo.navigation.manage') }}</Button><Button variant="ghost" :disabled="loading" @click="load">{{ $t('common.label.refresh') }}</Button></div></header>
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
            <div class="grid grid-cols-[minmax(0,1fr)_auto_minmax(0,1fr)] items-center gap-3 text-center"><div class="flex min-w-0 flex-col items-center gap-2"><Avatar class="size-16"><AvatarImage v-if="current.leftAvatarUrl" :src="current.leftAvatarUrl" :alt="current.leftTeamName ?? ''" /><AvatarFallback>{{ current.leftTeamName?.slice(0, 2) }}</AvatarFallback></Avatar><h2 class="min-w-0 break-words text-base font-semibold md:text-xl">{{ current.leftTeamName || '—' }}</h2></div><div class="font-mono text-2xl font-bold md:text-4xl tabular-nums text-primary">{{ current.leftWins ?? 0 }} : {{ current.rightWins ?? 0 }}</div><div class="flex min-w-0 flex-col items-center gap-2"><Avatar class="size-16"><AvatarImage v-if="current.rightAvatarUrl" :src="current.rightAvatarUrl" :alt="current.rightTeamName ?? ''" /><AvatarFallback>{{ current.rightTeamName?.slice(0, 2) }}</AvatarFallback></Avatar><h2 class="min-w-0 break-words text-base font-semibold md:text-xl">{{ current.rightTeamName || '—' }}</h2></div></div>
            <p v-if="['Completed', 'Canceled'].includes(current.state ?? '')" class="text-center text-sm text-muted-foreground">{{ $t(endReasonKey) }}</p>
            <div class="flex flex-wrap justify-center gap-3"><Button :disabled="reviewMode && !finished" @click="enter">{{ $t(reviewMode ? 'liveSolo.postgame.title' : 'liveSolo.enterMatch') }}</Button><Button v-if="finished && !reviewMode" variant="outline" @click="postgame">{{ $t('liveSolo.postgame.title') }}</Button><Button v-if="current.hasMedia && (staff || current.state === 'Completed')" variant="outline" @click="recordings">{{ $t(staff ? 'liveSolo.recording.staffTitle' : 'liveSolo.recording.replays') }}</Button></div>
            <div v-if="canManage && !reviewMode" class="flex justify-center"><Button variant="destructive" :disabled="deleting" @click="askDelete">{{ $t('liveSolo.matchDeletion.action') }}</Button></div>
            <p v-if="reviewMode && !finished" class="text-center text-sm text-muted-foreground">{{ $t('liveSolo.navigation.postgameAfterFinish') }}</p><section v-if="activityRows.length" class="flex flex-col gap-3 border-t pt-4"><h3 class="font-semibold">{{ $t('liveSolo.activity.title') }}</h3><ol class="flex flex-col gap-3"><li v-for="row in activityRows" :key="row.id" class="flex flex-wrap items-center justify-between gap-2 text-sm"><span>{{ $message(row.label) }}</span><time v-if="row.time" :datetime="row.time" class="text-xs text-muted-foreground">{{ formatDateTime(row.time) }}</time></li></ol></section>
          </template>
          <Empty v-else><EmptyHeader><EmptyTitle>{{ $t('liveSolo.noMatches') }}</EmptyTitle><EmptyDescription>{{ $t('liveSolo.noMatchesDescription') }}</EmptyDescription></EmptyHeader></Empty>
        </CardContent></Card>
      </div>
    </div>
    <AlertDialog :open="deleteOpen" @update:open="setDeleteOpen"><AlertDialogContent>
      <AlertDialogHeader><AlertDialogTitle>{{ $t('liveSolo.matchDeletion.title', { match: deleteLabel }) }}</AlertDialogTitle><AlertDialogDescription>{{ $t('liveSolo.matchDeletion.description') }}</AlertDialogDescription></AlertDialogHeader>
      <Alert v-if="deleteError" variant="destructive"><AlertDescription>{{ $message(deleteError) }}</AlertDescription></Alert>
      <AlertDialogFooter><AlertDialogCancel :disabled="deleting">{{ $t('common.action.cancel') }}</AlertDialogCancel><Button variant="destructive" :disabled="deleting" @click="removeMatch"><Spinner v-if="deleting" />{{ $t('liveSolo.matchDeletion.action') }}</Button></AlertDialogFooter>
    </AlertDialogContent></AlertDialog>
  </component>
</template>
