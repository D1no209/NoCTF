<script setup lang="ts">
import { Plus, Save, Trash2 } from '@lucide/vue'
import { toast } from 'vue-sonner'
import {
  adminCompetitionTracksGet,
  adminCompetitionTracksUpdate,
} from '~/api'
import type {
  NoCtfapiEndpointsAdministrationCompetitionsUpdateCompetitionTrackRequest,
} from '~/api'
import { useCompetitionAdmin } from '~/lib/admin-competition'
import { competitionTrackErrorMessage } from '~/lib/competition-track'

definePageMeta({ middleware: 'auth' })

const { competitionId, canWrite } = useCompetitionAdmin()
const revision = ref(0)
const mode = ref<'Ctf' | 'Awd' | 'Awdp' | 'Koh'>('Ctf')
const frozen = ref(false)
const tracks = ref<NoCtfapiEndpointsAdministrationCompetitionsUpdateCompetitionTrackRequest[]>([])
const loading = ref(true)
const saving = ref(false)
const error = ref<string | null>(null)

async function load() {
  loading.value = true
  const { data, error: requestError } = await adminCompetitionTracksGet({ path: { competitionId } })
  loading.value = false
  if (requestError || !data) {
    error.value = competitionTrackErrorMessage(requestError, translate('加载赛道配置失败'))
    return
  }
  revision.value = data.revision ?? 0
  mode.value = data.mode ?? 'Ctf'
  frozen.value = data.isFrozen ?? false
  tracks.value = (data.items ?? []).map(item => ({
    key: item.key ?? '',
    name: item.name ?? '',
    isDefault: item.isDefault ?? false,
    isPublicSelectable: item.isPublicSelectable ?? false,
    isInternal: item.isInternal ?? false,
    earnsScore: item.earnsScore ?? false,
    earnsBlood: item.earnsBlood ?? false,
    affectsDynamicChallengeScore: item.affectsDynamicChallengeScore ?? false,
    visibleOnLeaderboard: item.visibleOnLeaderboard ?? false,
    affectsCompetitiveResults: item.affectsCompetitiveResults ?? false,
  }))
  error.value = null
}

function addTrack() {
  const ordinal = tracks.value.length + 1
  tracks.value.push({
    key: `track-${ordinal}`,
    name: translate('新赛道 {ordinal}', { ordinal }),
    isDefault: false,
    isPublicSelectable: true,
    isInternal: false,
    earnsScore: true,
    earnsBlood: mode.value === 'Ctf',
    affectsDynamicChallengeScore: mode.value === 'Ctf',
    visibleOnLeaderboard: true,
    affectsCompetitiveResults: true,
  })
}

function removeTrack(index: number) {
  if (tracks.value[index]?.isDefault) return
  tracks.value.splice(index, 1)
}

function setDefault(index: number) {
  tracks.value.forEach((track, trackIndex) => { track.isDefault = trackIndex === index })
}

function setInternal(track: NoCtfapiEndpointsAdministrationCompetitionsUpdateCompetitionTrackRequest, internal: boolean) {
  track.isInternal = internal
  if (!internal) return
  track.isPublicSelectable = false
  track.earnsScore = false
  track.earnsBlood = false
  track.affectsDynamicChallengeScore = false
  track.visibleOnLeaderboard = false
  track.affectsCompetitiveResults = false
}

async function save() {
  if (saving.value || frozen.value || !canWrite.value) return
  saving.value = true
  const { data, error: requestError } = await adminCompetitionTracksUpdate({
    path: { competitionId },
    body: { expectedRevision: revision.value, tracks: tracks.value },
  })
  saving.value = false
  if (requestError || !data) {
    error.value = competitionTrackErrorMessage(requestError, translate('保存赛道配置失败'))
    toast.error(error.value)
    return
  }
  toast.success(translate('赛道配置已保存'))
  await load()
}

onMounted(load)
</script>

<template>
  <div class="flex flex-col gap-5">
    <header class="flex flex-wrap items-end justify-between gap-3 border-b pb-4">
      <div class="space-y-1">
        <h2 class="text-xl font-semibold">{{ $t('赛道配置') }}</h2>
        <p class="text-sm text-muted-foreground">
          {{ $t('为同一场比赛划分独立排名、计分和公开范围。赛道及队伍归属在首次开赛后冻结。') }}
        </p>
      </div>
      <div v-if="canWrite && !frozen" class="flex gap-2">
        <Button variant="outline" @click="addTrack">
          <Plus data-icon="inline-start" /> {{ $t('新增赛道') }}
        </Button>
        <Button :disabled="saving || !tracks.length" @click="save">
          <Spinner v-if="saving" data-icon="inline-start" />
          <Save v-else data-icon="inline-start" /> {{ $t('保存配置') }}
        </Button>
      </div>
    </header>

    <Alert v-if="frozen">
      <AlertDescription>{{ $t('比赛已开始，赛道定义与队伍归属已冻结。') }}</AlertDescription>
    </Alert>
    <Alert v-if="error" variant="destructive">
      <AlertDescription class="flex items-center justify-between gap-3">
        <span>{{ error }}</span>
        <Button size="sm" variant="outline" @click="load">{{ $t('重试') }}</Button>
      </AlertDescription>
    </Alert>
    <Skeleton v-if="loading" class="h-64 w-full" />

    <div v-else class="overflow-x-auto border">
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead class="min-w-40">{{ $t('赛道标识') }}</TableHead>
            <TableHead class="min-w-40">{{ $t('显示名称') }}</TableHead>
            <TableHead>{{ $t('默认') }}</TableHead>
            <TableHead>{{ $t('公开选择') }}</TableHead>
            <TableHead>{{ $t('内部') }}</TableHead>
            <TableHead>{{ $t('计分') }}</TableHead>
            <TableHead>{{ $t('血榜') }}</TableHead>
            <TableHead>{{ $t('动态分值') }}</TableHead>
            <TableHead>{{ $t('排行榜可见') }}</TableHead>
            <TableHead>{{ $t('参与竞争') }}</TableHead>
            <TableHead v-if="canWrite && !frozen" class="w-14"><span class="sr-only">{{ $t('操作') }}</span></TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          <TableRow v-for="(track, index) in tracks" :key="`${track.key}-${index}`">
            <TableCell><Input v-model="track.key" :disabled="!canWrite || frozen" maxlength="64" class="font-mono" /></TableCell>
            <TableCell><Input v-model="track.name" :disabled="!canWrite || frozen" maxlength="80" /></TableCell>
            <TableCell><Checkbox :model-value="track.isDefault" :disabled="!canWrite || frozen" @update:model-value="value => value && setDefault(index)" /></TableCell>
            <TableCell><Checkbox v-model="track.isPublicSelectable" :disabled="!canWrite || frozen || track.isInternal" /></TableCell>
            <TableCell><Checkbox :model-value="track.isInternal" :disabled="!canWrite || frozen" @update:model-value="value => setInternal(track, value === true)" /></TableCell>
            <TableCell><Checkbox v-model="track.earnsScore" :disabled="!canWrite || frozen || track.isInternal" /></TableCell>
            <TableCell><Checkbox v-model="track.earnsBlood" :disabled="!canWrite || frozen || track.isInternal || mode !== 'Ctf'" /></TableCell>
            <TableCell><Checkbox v-model="track.affectsDynamicChallengeScore" :disabled="!canWrite || frozen || track.isInternal || mode !== 'Ctf'" /></TableCell>
            <TableCell><Checkbox v-model="track.visibleOnLeaderboard" :disabled="!canWrite || frozen || track.isInternal" /></TableCell>
            <TableCell><Checkbox v-model="track.affectsCompetitiveResults" :disabled="!canWrite || frozen || track.isInternal" /></TableCell>
            <TableCell v-if="canWrite && !frozen">
              <Button variant="ghost" size="icon" :disabled="track.isDefault" :aria-label="$t('删除赛道')" @click="removeTrack(index)">
                <Trash2 class="size-4" />
              </Button>
            </TableCell>
          </TableRow>
        </TableBody>
      </Table>
    </div>
    <p v-if="!loading" class="text-xs text-muted-foreground">
      {{ $t('血榜奖励要求启用计分；内部赛道固定不公开、不计分且不参与竞争。') }}
    </p>
  </div>
</template>
