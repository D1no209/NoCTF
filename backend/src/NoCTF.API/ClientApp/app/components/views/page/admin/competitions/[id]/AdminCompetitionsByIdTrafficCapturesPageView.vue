<script setup lang="ts">
import { toRefs } from 'vue'
import type { AdminCompetitionsByIdTrafficCapturesPageViewState } from '~/features/routes/admin/competitions/[id]/useAdminCompetitionsByIdTrafficCapturesPage'

const viewProps = defineProps<{ state: AdminCompetitionsByIdTrafficCapturesPageViewState }>()
const { adminTeamPath, adminChallengePath, adminRuntimePath, competitionId, Download, RefreshCw, Trash2, canWrite, competition, challengeOptions, teamOptions, filterChallenge, filterTeam, filterRuntime, filterTruncated, selectedRuntimeIds, exporting, deleteTarget, deleting, items, loading, listError, page, pageCount, total, pageLimit, loadPage, setPageSize, formatBytes, adminFormatDateTime, applyFilters, onToggleSelected, isSelected, openDelete, setDeleteOpen, downloadCapture, exportSelected, confirmDelete, challengeTitle, teamName } = toRefs(viewProps.state)
</script>

<template>
  <Card class="gap-0">
    <CardHeader>
      <div class="flex flex-wrap items-start justify-between gap-3">
        <div>
          <CardTitle>{{ $t('runtime.trafficCaptures') }}</CardTitle>
          <CardDescription>{{ $t('runtime.trafficCapturesDescription') }}</CardDescription>
        </div>
        <Button variant="outline" :disabled="selectedRuntimeIds.length === 0 || exporting" @click="exportSelected">
          <Spinner v-if="exporting" data-icon="inline-start" />
          <Download v-else data-icon="inline-start" />
          {{ $t('runtime.exportSelectedCaptures') }}
        </Button>
      </div>
    </CardHeader>
    <CardContent class="grid gap-5">
      <Alert v-if="competition?.runtimeAccessMode === 'DirectAndWsrx' && competition.trafficCaptureEnabled">
        <AlertDescription>{{ $t('runtime.captureDirectBypassWarning') }}</AlertDescription>
      </Alert>
      <div class="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
        <Select v-model="filterChallenge">
          <SelectTrigger :aria-label="$t('common.label.challenge.pageTitle')"><SelectValue :placeholder="$t('common.label.challenge.pageTitle')" /></SelectTrigger>
          <SelectContent>
            <SelectItem value="all">{{ $t('runtime.allChallenges') }}</SelectItem>
            <SelectItem v-for="option in challengeOptions" :key="option.id" :value="option.id">{{ option.title }}</SelectItem>
          </SelectContent>
        </Select>
        <Select v-model="filterTeam">
          <SelectTrigger :aria-label="$t('common.label.team')"><SelectValue :placeholder="$t('common.label.team')" /></SelectTrigger>
          <SelectContent>
            <SelectItem value="all">{{ $t('runtime.allTeams') }}</SelectItem>
            <SelectItem v-for="option in teamOptions" :key="option.id" :value="option.id">{{ option.name }}</SelectItem>
          </SelectContent>
        </Select>
        <Input v-model="filterRuntime" :placeholder="$t('runtime.runtimeId')" />
        <Select v-model="filterTruncated">
          <SelectTrigger :aria-label="$t('runtime.captureState')"><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem value="all">{{ $t('runtime.allCaptureStates') }}</SelectItem>
            <SelectItem value="complete">{{ $t('runtime.captureComplete') }}</SelectItem>
            <SelectItem value="truncated">{{ $t('runtime.captureTruncated') }}</SelectItem>
          </SelectContent>
        </Select>
      </div>
      <div class="flex justify-end">
        <Button variant="outline" :disabled="loading" @click="applyFilters">
          <RefreshCw data-icon="inline-start" />{{ $t('common.label.applyFilters') }}
        </Button>
      </div>
      <Alert v-if="listError" variant="destructive"><AlertDescription>{{ listError }}</AlertDescription></Alert>
      <Table v-else>
        <TableHeader>
          <TableRow>
            <TableHead class="w-10"><span class="sr-only">{{ $t('runtime.selectCapture') }}</span></TableHead>
            <TableHead>{{ $t('common.label.challenge.pageTitle') }}</TableHead>
            <TableHead>{{ $t('common.label.team') }}</TableHead>
            <TableHead>{{ $t('administration.label.runtime') }}</TableHead>
            <TableHead>{{ $t('common.label.fileSize') }}</TableHead>
            <TableHead>{{ $t('runtime.captureConnections') }}</TableHead>
            <TableHead>{{ $t('runtime.captureState') }}</TableHead>
            <TableHead>{{ $t('administration.label.updateTime') }}</TableHead>
            <TableHead class="text-right">{{ $t('common.label.actions') }}</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          <TableRow v-for="item in items" :key="item.runtimeInstanceId">
            <TableCell>
              <Checkbox :model-value="isSelected(item.runtimeInstanceId)" @update:model-value="onToggleSelected(item, $event)" />
            </TableCell>
            <TableCell><NuxtLink v-if="item.competitionChallengeId" :to="adminChallengePath(competitionId, item.competitionChallengeId)" class="hover:underline">{{ challengeTitle(item) }}</NuxtLink><span v-else>{{ challengeTitle(item) }}</span></TableCell>
            <TableCell><NuxtLink v-if="item.teamId" :to="adminTeamPath(competitionId, item.teamId)" class="hover:underline">{{ teamName(item) }}</NuxtLink><span v-else>{{ teamName(item) }}</span></TableCell>
            <TableCell class="font-mono text-xs"><NuxtLink :to="adminRuntimePath(competitionId, item.runtimeInstanceId)" class="hover:underline">{{ item.runtimeInstanceId }}</NuxtLink></TableCell>
            <TableCell class="font-mono tabular-nums">{{ formatBytes(item.byteLength) }}</TableCell>
            <TableCell class="font-mono tabular-nums">{{ item.segmentCount ?? 0 }}</TableCell>
            <TableCell>
              <Badge :variant="item.truncated ? 'destructive' : 'secondary'">
                {{ item.truncated ? $t('runtime.captureTruncated') : $t('runtime.captureComplete') }}
              </Badge>
            </TableCell>
            <TableCell class="font-mono text-xs">{{ adminFormatDateTime(item.updatedAt) }}</TableCell>
            <TableCell>
              <div class="flex justify-end gap-1">
                <Button size="icon" variant="ghost" :aria-label="$t('writeups.label.download')" @click="downloadCapture(item)">
                  <Download class="size-4" />
                </Button>
                <Button v-if="canWrite" size="icon" variant="ghost" :disabled="item.runtimeState === 'Running' || item.runtimeState === 'Provisioning' || item.runtimeState === 'Queued' || item.runtimeState === 'Stopping'" :aria-label="$t('common.action.delete')" @click="openDelete(item)">
                  <Trash2 class="size-4" />
                </Button>
              </div>
            </TableCell>
          </TableRow>
          <TableRow v-if="!loading && items.length === 0">
            <TableCell colspan="9"><Empty><EmptyHeader><EmptyTitle>{{ $t('runtime.noTrafficCaptures') }}</EmptyTitle></EmptyHeader></Empty></TableCell>
          </TableRow>
        </TableBody>
      </Table>
      <OffsetPagination :page="page" :page-count="pageCount" :total="total" :limit="pageLimit" :loading="loading" @update:page="loadPage" @update:limit="setPageSize" />
    </CardContent>
  </Card>

  <AlertDialog :open="!!deleteTarget" @update:open="setDeleteOpen">
    <AlertDialogContent>
      <AlertDialogHeader>
        <AlertDialogTitle>{{ $t('runtime.deleteCapture') }}</AlertDialogTitle>
        <AlertDialogDescription>{{ $t('runtime.deleteCaptureDescription') }}</AlertDialogDescription>
      </AlertDialogHeader>
      <AlertDialogFooter>
        <AlertDialogCancel :disabled="deleting">{{ $t('common.action.cancel') }}</AlertDialogCancel>
        <Button variant="destructive" :disabled="deleting" @click="confirmDelete">
          <Spinner v-if="deleting" data-icon="inline-start" />{{ $t('common.action.delete') }}
        </Button>
      </AlertDialogFooter>
    </AlertDialogContent>
  </AlertDialog>
</template>
