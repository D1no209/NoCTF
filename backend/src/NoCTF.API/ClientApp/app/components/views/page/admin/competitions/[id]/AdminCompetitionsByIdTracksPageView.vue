<script setup lang="ts">
import { toRefs } from 'vue'
import type { AdminCompetitionsByIdTracksPageViewState } from '~/features/routes/admin/competitions/[id]/useAdminCompetitionsByIdTracksPage'

const viewProps = defineProps<{ state: AdminCompetitionsByIdTracksPageViewState }>()
const { Plus, Save, Trash2, canWrite, mode, frozen, tracks, loading, saving, error, load, addTrack, removeTrack, setDefault, setPublicSelectable, setInternal, setInvitationRequired, save } = toRefs(viewProps.state)
</script>

<template>
  <div class="flex flex-col gap-5">
    <header class="flex flex-wrap items-end justify-between gap-3 border-b pb-4">
      <div class="space-y-1">
        <h2 class="text-xl font-semibold">{{ $t('ui.trackConfiguration') }}</h2>
        <p class="text-sm text-muted-foreground">
          {{ $t('ui.createIndependentRankingsScoringAndVisibilityGroupsWithinOneCompetition') }}
        </p>
      </div>
      <div v-if="canWrite && !frozen" class="flex gap-2">
        <Button variant="outline" @click="addTrack">
          <Plus data-icon="inline-start" /> {{ $t('ui.addTrack') }}
        </Button>
        <Button :disabled="saving || !tracks.length" @click="save">
          <Spinner v-if="saving" data-icon="inline-start" />
          <Save v-else data-icon="inline-start" /> {{ $t('ui.saveConfiguration') }}
        </Button>
      </div>
    </header>

    <Alert v-if="frozen">
      <AlertDescription>{{ $t('ui.theCompetitionHasStartedTrackDefinitionsAreFrozenWhileTeam') }}</AlertDescription>
    </Alert>
    <Alert v-if="error" variant="destructive">
      <AlertDescription class="flex items-center justify-between gap-3">
        <span>{{ $message(error) }}</span>
        <Button size="sm" variant="outline" @click="load">{{ $t('ui.retry') }}</Button>
      </AlertDescription>
    </Alert>
    <Skeleton v-if="loading" class="h-64 w-full" />

    <div v-else class="overflow-x-auto border">
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead class="min-w-40">{{ $t('ui.trackKey') }}</TableHead>
            <TableHead class="min-w-40">{{ $t('ui.displayName') }}</TableHead>
            <TableHead>{{ $t('ui.default') }}</TableHead>
            <TableHead>{{ $t('ui.publicSelection') }}</TableHead>
            <TableHead>{{ $t('ui.internal') }}</TableHead>
            <TableHead>{{ $t('ui.scoring') }}</TableHead>
            <TableHead>{{ $t('ui.bloodAwards') }}</TableHead>
            <TableHead>{{ $t('ui.dynamicScoring') }}</TableHead>
            <TableHead>{{ $t('ui.leaderboardVisibility') }}</TableHead>
            <TableHead>{{ $t('ui.competitiveResults') }}</TableHead>
            <TableHead class="min-w-56">{{ $t('ui.trackInvitationCode') }}</TableHead>
            <TableHead v-if="canWrite && !frozen" class="w-14"><span class="sr-only">{{ $t('ui.actions') }}</span></TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          <TableRow v-for="(track, index) in tracks" :key="track.clientId">
            <TableCell><Input v-model="track.key" :disabled="!canWrite || frozen" maxlength="64" class="font-mono" /></TableCell>
            <TableCell><Input v-model="track.name" :disabled="!canWrite || frozen" maxlength="80" /></TableCell>
            <TableCell><Checkbox :model-value="track.isDefault" :disabled="!canWrite || frozen" @update:model-value="value => value && setDefault(index)" /></TableCell>
            <TableCell><Checkbox :model-value="track.isPublicSelectable" :disabled="!canWrite || frozen || track.isInternal" @update:model-value="value => setPublicSelectable(track, value === true)" /></TableCell>
            <TableCell><Checkbox :model-value="track.isInternal" :disabled="!canWrite || frozen" @update:model-value="value => setInternal(track, value === true)" /></TableCell>
            <TableCell><Checkbox v-model="track.earnsScore" :disabled="!canWrite || frozen || track.isInternal" /></TableCell>
            <TableCell><Checkbox v-model="track.earnsBlood" :disabled="!canWrite || frozen || track.isInternal || mode !== 'Ctf'" /></TableCell>
            <TableCell><Checkbox v-model="track.affectsDynamicChallengeScore" :disabled="!canWrite || frozen || track.isInternal || mode !== 'Ctf'" /></TableCell>
            <TableCell><Checkbox v-model="track.visibleOnLeaderboard" :disabled="!canWrite || frozen || track.isInternal" /></TableCell>
            <TableCell><Checkbox v-model="track.affectsCompetitiveResults" :disabled="!canWrite || frozen || track.isInternal" /></TableCell>
            <TableCell>
              <div class="flex items-center gap-2">
                <Checkbox
                  :model-value="track.requiresInvitationCode"
                  :disabled="!canWrite || frozen || track.isInternal || !track.isPublicSelectable"
                  :aria-label="$t('ui.requireATrackInvitationCode')"
                  @update:model-value="value => setInvitationRequired(track, value === true)"
                />
                <Input
                  v-if="track.requiresInvitationCode"
                  v-model="track.invitationCode"
                  type="password"
                  minlength="8"
                  maxlength="128"
                  :disabled="!canWrite || frozen"
                  :placeholder="track.invitationCodeConfigured ? $t('ui.leaveBlankToKeepTheCurrentInvitationCode') : $t('ui.enterAn8128CharacterInvitationCode')"
                />
                <span v-else class="text-xs text-muted-foreground">{{ $t('ui.noRestriction') }}</span>
              </div>
            </TableCell>
            <TableCell v-if="canWrite && !frozen">
              <Button variant="ghost" size="icon" :disabled="track.isDefault" :aria-label="$t('ui.deleteTrack')" @click="removeTrack(index)">
                <Trash2 class="size-4" />
              </Button>
            </TableCell>
          </TableRow>
        </TableBody>
      </Table>
    </div>
    <p v-if="!loading" class="text-xs text-muted-foreground">
      {{ $t('ui.bloodAwardsRequireScoringInternalTracksAreAlwaysPrivateUnscored') }}
    </p>
  </div>
</template>
