<script setup lang="ts">
import { toRefs } from 'vue'
import type { LiveSoloTournamentMapState } from '~/features/live-solo/useLiveSoloTournamentMap'
const props = defineProps<{ state: LiveSoloTournamentMapState }>()
const { selectedStage, focusTeam, zoom, root, isFullscreen, toggle, setRoot, stageOptions, stage, diagram, standings, highlighted,
  teamOptions, zoomIn, zoomOut, resetZoom, locate, enter, clearFocus, Maximize, Minimize, ZoomIn, ZoomOut, LocateFixed, groups, showStandings } = toRefs(props.state)
</script>
<template>
  <section :ref="setRoot" class="flex min-w-0 flex-col gap-4" :class="{ 'h-full bg-background p-6': isFullscreen }">
    <header class="flex flex-wrap items-center gap-3">
      <h2 class="text-display text-lg">{{ $t('liveSolo.tournament.overview') }}</h2>
      <Select v-model="selectedStage"><SelectTrigger class="w-64" :aria-label="$t('liveSolo.tournament.selectStage')"><SelectValue /></SelectTrigger>
        <SelectContent :portal-target="isFullscreen ? root ?? undefined : undefined"><SelectGroup><SelectItem v-for="item in stageOptions" :key="item.value" :value="item.value">{{ item.name || $t('liveSolo.tournament.stageNumber',{number:item.number}) }} · {{ $t(item.formatKey) }}</SelectItem></SelectGroup></SelectContent>
      </Select>
      <Badge v-if="stage?.complete" variant="secondary">{{ $t('liveSolo.state.completed') }}</Badge>
      <div class="ml-auto flex items-center gap-1">
        <Button size="icon-sm" variant="ghost" :aria-label="$t('liveSolo.tournament.locate')" @click="locate"><component :is="LocateFixed" /></Button>
        <Button size="icon-sm" variant="ghost" :disabled="zoom <= 0.35" :aria-label="$t('liveSolo.tournament.zoomOut')" @click="zoomOut"><component :is="ZoomOut" /></Button>
        <Button size="sm" variant="ghost" :aria-label="$t('liveSolo.tournament.resetZoom')" @click="resetZoom">{{ Math.round(zoom * 100) }}%</Button>
        <Button size="icon-sm" variant="ghost" :disabled="zoom >= 1.5" :aria-label="$t('liveSolo.tournament.zoomIn')" @click="zoomIn"><component :is="ZoomIn" /></Button>
        <Button size="icon-sm" variant="ghost" :aria-label="$t('liveSolo.tournament.fullscreen')" @click="toggle"><component :is="isFullscreen ? Minimize : Maximize" /></Button>
      </div>
    </header>
    <div class="flex flex-wrap items-center gap-3"><div class="w-64"><ChoicePicker v-model="focusTeam" :portal-target="isFullscreen ? root ?? undefined : undefined" :items="teamOptions" :label="$t('liveSolo.tournament.focusTeam')" :search-label="$t('liveSolo.bracket.searchTeam')" :empty-label="$t('liveSolo.bracket.noTeam')" /></div>
      <Button v-if="focusTeam" variant="ghost" size="sm" @click="clearFocus">{{ $t('common.label.clear') }}</Button>
      <p class="text-xs text-muted-foreground">{{ $t(stage?.rules?.format === 'Swiss' ? 'liveSolo.tournament.swissLegend' : 'liveSolo.tournament.eliminationLegend') }}</p>
    </div>
    <div class="grid min-h-0 gap-4 xl:grid-cols-[minmax(0,1fr)_16rem]" :class="{ 'flex-1': isFullscreen }">
    <ScrollSurface v-if="diagram.nodes.length" data-tournament-viewport axis="both" :reset-key="selectedStage" class="rounded-lg border bg-background/30 p-4" :class="{ 'h-full min-h-0': isFullscreen, 'h-[64dvh] min-h-80': !isFullscreen }">
      <div :style="{ width: `${diagram.width * zoom}px`, height: `${diagram.height * zoom}px` }" class="relative">
        <div class="relative origin-top-left" :style="{ width: `${diagram.width}px`, height: `${diagram.height}px`, transform: `scale(${zoom})` }">
          <svg class="pointer-events-none absolute inset-0 size-full" aria-hidden="true">
            <path v-for="edge in diagram.edges" :key="edge.id" :d="edge.path" fill="none" class="stroke-border" :class="{ 'stroke-primary': focusTeam && highlighted.has(edge.from) && highlighted.has(edge.to), 'opacity-20': focusTeam && (!highlighted.has(edge.from) || !highlighted.has(edge.to)) }" :stroke-dasharray="edge.loser ? '5 5' : undefined" stroke-width="1.5" />
          </svg>
          <div v-for="heading in diagram.headings" :key="heading.id" class="absolute flex w-62 items-center justify-between gap-2 text-sm" :style="{ left: `${heading.x}px`, top: `${heading.y}px` }">
            <span class="font-medium">{{ $t(heading.laneKey) }} · {{ $t('liveSolo.tournament.round',{number:heading.round}) }}</span>
            <Badge v-if="heading.wins != null" variant="secondary">{{ $t('liveSolo.tournament.record',{wins:heading.wins,losses:heading.losses ?? 0}) }}</Badge>
          </div>
          <Button v-for="node in diagram.nodes" :key="node.id" type="button" :data-match-id="node.id"
            :variant="node.row.match?.state === 'Running' ? 'secondary' : 'outline'"
            class="absolute flex h-24 w-62 flex-col items-stretch justify-start gap-1.5 p-3 text-left"
            :class="{ 'opacity-30': focusTeam && !highlighted.has(node.id) }"
            :style="{ left: `${node.x}px`, top: `${node.y}px` }" @click="enter(node.id)">
            <span class="flex items-center justify-between gap-2 text-xs text-muted-foreground"><span class="font-mono">{{ $t('liveSolo.tournament.matchBo',{number:node.number,bo:(node.row.match?.requiredWins ?? 1) * 2 - 1}) }}</span><span>{{ $t(node.stateKey) }}</span></span>
            <span v-for="side in node.sides" :key="side.key" class="flex min-w-0 items-center gap-2" :class="{ 'text-primary font-semibold': side.winner }">
              <span v-if="side.seed" class="w-5 shrink-0 font-mono text-xs text-muted-foreground">{{ side.seed }}</span>
              <span class="min-w-0 flex-1 truncate">{{ side.name || (side.source === 'Bye' ? $t('liveSolo.bracket.bye') : side.sourceNumber ? $t(side.source === 'Loser' ? 'liveSolo.bracket.fromLoser' : 'liveSolo.bracket.fromWinner',{match:side.sourceNumber}) : $t('liveSolo.bracket.awaitingSource')) }}</span>
              <span v-if="side.teamId" class="font-mono tabular-nums">{{ side.score }}</span>
            </span>
            <Badge v-if="node.row.match?.pendingCorrectionId" variant="destructive" class="absolute -top-3 right-2">{{ $t('liveSolo.tournament.correction') }}</Badge>
          </Button>
        </div>
      </div>
    </ScrollSurface>
    <Empty v-else><EmptyHeader><EmptyTitle>{{ $t('liveSolo.tournament.pendingStage') }}</EmptyTitle></EmptyHeader></Empty>
    <ScrollSurface v-if="standings.length" axis="y" class="max-h-[64dvh] min-h-0 border-l pl-4" :class="{ 'max-h-full': isFullscreen }">
      <div v-for="group in groups" :key="group.key" class="flex flex-col gap-3 pb-6">
        <h3 class="flex items-center justify-between font-medium"><span>{{ $t(group.key) }}</span><Badge variant="secondary">{{ group.rows.length }}</Badge></h3>
        <div v-for="row in group.rows" :key="row.teamId" class="flex items-center justify-between gap-2 text-sm"><span class="truncate">{{ row.name }}</span><span class="shrink-0 font-mono text-xs tabular-nums text-muted-foreground">{{ row.wins }} : {{ row.losses }}</span></div>
        <p v-if="!group.rows.length" class="text-xs text-muted-foreground">{{ $t('common.label.symbol') }}</p>
      </div>
    </ScrollSurface>
    </div>
    <Collapsible v-if="standings.length && !isFullscreen" v-model:open="showStandings" class="flex flex-col gap-3"><CollapsibleTrigger as-child><Button variant="ghost" class="self-start">{{ $t('liveSolo.tournament.standings') }}</Button></CollapsibleTrigger><CollapsibleContent>
      <Table><TableHeader><TableRow><TableHead>{{ $t('common.label.team') }}</TableHead><TableHead>{{ $t('liveSolo.tournament.recordTitle') }}</TableHead><TableHead v-if="stage?.rules?.format === 'Swiss'">{{ $t('liveSolo.tournament.buchholz') }}</TableHead><TableHead>{{ $t('common.label.status') }}</TableHead></TableRow></TableHeader>
        <TableBody><TableRow v-for="row in standings" :key="row.teamId"><TableCell>{{ row.name }}</TableCell><TableCell class="font-mono tabular-nums">{{ row.wins }} : {{ row.losses }}</TableCell><TableCell v-if="stage?.rules?.format === 'Swiss'" class="font-mono tabular-nums">{{ row.buchholz }}</TableCell><TableCell><Badge variant="secondary">{{ $t(row.stateKey) }}</Badge></TableCell></TableRow></TableBody>
      </Table>
    </CollapsibleContent></Collapsible>
  </section>
</template>
