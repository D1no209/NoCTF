<script setup lang="ts">
import { toRefs } from 'vue'
import type { LiveSoloTournamentMapState } from '~/features/live-solo/useLiveSoloTournamentMap'
const props = defineProps<{ state: LiveSoloTournamentMapState }>()
const { selectedStage, focusTeam, zoom, root, isFullscreen, toggle, setRoot, stageOptions, stage, diagram, standings, highlighted,
  teamOptions, zoomIn, zoomOut, resetZoom, locate, enter, clearFocus, Maximize, Minimize, ZoomIn, ZoomOut, LocateFixed, Trophy, groups, showStandings,
  canvasWidth, canvasHeight, syncKey } = toRefs(props.state)
</script>
<template>
  <section :ref="setRoot" class="flex min-w-0 flex-col gap-4" :class="{ 'h-full bg-background p-6': isFullscreen }">
    <header class="flex flex-wrap items-center gap-3">
      <h2 class="text-display text-lg">{{ $t('liveSolo.tournament.overview') }}</h2>
      <Select v-model="selectedStage"><SelectTrigger class="w-64" :aria-label="$t('liveSolo.tournament.selectStage')"><SelectValue /></SelectTrigger>
        <SelectContent :portal-target="isFullscreen ? root ?? undefined : undefined"><SelectGroup><SelectItem v-for="item in stageOptions" :key="item.value" :value="item.value">{{ item.name || $t('liveSolo.tournament.stageNumber',{number:item.number}) }} · {{ $t(item.formatKey) }}</SelectItem></SelectGroup></SelectContent>
      </Select>
      <Badge v-if="stage?.complete" variant="secondary">{{ $t('liveSolo.state.completed') }}</Badge>
      <Badge variant="secondary" aria-live="polite">{{ $t(syncKey) }}</Badge>
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
    <div class="min-h-0" :class="{ 'flex-1': isFullscreen }">
    <ScrollSurface v-if="diagram.nodes.length" data-tournament-viewport axis="both" :reset-key="selectedStage" class="rounded-lg border bg-background/30 p-4" :class="{ 'h-full min-h-0': isFullscreen, 'h-[64dvh] min-h-80': !isFullscreen }">
      <div :style="{ width: `${canvasWidth * zoom}px`, height: `${canvasHeight * zoom}px` }" class="relative">
        <div class="relative origin-top-left" :style="{ width: `${canvasWidth}px`, height: `${canvasHeight}px`, transform: `scale(${zoom})` }">
          <svg class="pointer-events-none absolute inset-0 size-full" aria-hidden="true">
            <path v-for="edge in diagram.edges" :key="edge.id" :d="edge.path" fill="none" class="stroke-border" :class="{ 'stroke-primary': focusTeam && (edge.teamIds ? edge.teamIds.includes(focusTeam) : highlighted.has(edge.from) && highlighted.has(edge.to)), 'opacity-20': focusTeam && (edge.teamIds ? !edge.teamIds.includes(focusTeam) : !highlighted.has(edge.from) || !highlighted.has(edge.to)) }" :stroke-dasharray="edge.loser ? '5 5' : undefined" stroke-width="1.5" />
          </svg>
          <div v-for="heading in diagram.headings" :key="heading.id" class="absolute flex w-76 items-center justify-between gap-2 text-sm" :style="{ left: `${heading.x}px`, top: `${heading.y}px` }">
            <span class="font-medium">{{ $t(heading.laneKey) }} · {{ $t('liveSolo.tournament.round',{number:heading.round}) }}</span>
            <Badge v-if="heading.wins != null" variant="secondary">{{ $t('liveSolo.tournament.record',{wins:heading.wins,losses:heading.losses ?? 0}) }}</Badge>
          </div>
          <Button v-for="node in diagram.nodes" :key="node.id" type="button" :data-match-id="node.id"
            :variant="node.row.match?.state === 'Running' ? 'secondary' : 'outline'"
            :aria-label="$t('liveSolo.tournament.matchLabel',{number:node.number,left:node.sides[0]?.name || $t('liveSolo.bracket.awaitingSource'),right:node.sides[1]?.name || $t('liveSolo.bracket.awaitingSource'),state:$t(node.stateKey)})"
            class="absolute flex h-24 w-76 flex-col items-stretch justify-start gap-2 p-3 text-left"
            :class="{ 'opacity-30': focusTeam && !highlighted.has(node.id) }"
            :style="{ left: `${node.x}px`, top: `${node.y}px` }" @click="enter(node.id)">
            <span class="flex items-center justify-between gap-2 text-xs text-muted-foreground"><span class="font-mono">{{ $t('liveSolo.tournament.matchBo',{number:node.number,bo:(node.row.match?.requiredWins ?? 1) * 2 - 1}) }}</span><span>{{ $t(node.stateKey) }}</span></span>
            <span class="flex min-w-0 flex-1 items-center gap-2">
              <template v-for="(side,index) in node.sides" :key="side.key">
              <span v-if="index === 1" class="flex w-12 shrink-0 flex-col items-center justify-center gap-0.5 text-center">
                <span class="font-mono text-base font-semibold text-muted-foreground">{{ $t('liveSolo.tournament.versus') }}</span>
                <span v-if="node.showScore" class="font-mono text-xs tabular-nums">{{ node.sides[0]?.score }} : {{ node.sides[1]?.score }}</span>
              </span>
              <span class="flex min-w-0 flex-1 items-center gap-2" :class="{ 'flex-row-reverse text-right': side.key === 'Left', 'text-primary font-semibold': side.winner, 'text-muted-foreground': !side.winner && node.row.match?.state === 'Completed' }">
                <Avatar :class="{ 'ring-2 ring-primary': side.winner }"><AvatarImage v-if="side.avatarUrl" :src="side.avatarUrl" :alt="side.name ?? ''" loading="lazy" /><AvatarFallback>{{ side.initials || $t('common.label.symbol') }}</AvatarFallback></Avatar>
                <span class="flex min-w-0 flex-1 flex-col gap-0.5">
                  <span class="flex items-center gap-1 text-xs" :class="{ 'justify-end': side.key === 'Left' }"><span v-if="side.seed" class="font-mono text-muted-foreground">{{ $t('liveSolo.bracket.seed',{seed:side.seed}) }}</span><component :is="Trophy" v-if="side.winner" :aria-label="$t('liveSolo.tournament.winner')" /></span>
                  <span class="truncate text-sm">{{ side.name || (side.source === 'Bye' ? $t('liveSolo.bracket.bye') : side.sourceNumber ? $t(side.source === 'Loser' ? 'liveSolo.bracket.fromLoser' : 'liveSolo.bracket.fromWinner',{match:side.sourceNumber}) : $t('liveSolo.bracket.awaitingSource')) }}</span>
                </span>
              </span>
              </template>
            </span>
            <Badge v-if="node.row.match?.pendingCorrectionId" variant="destructive" class="absolute -top-3 right-2">{{ $t('liveSolo.tournament.correction') }}</Badge>
          </Button>
          <aside v-if="standings.length" class="absolute top-0 flex w-64 flex-col gap-6" :style="{ left: `${diagram.width + 8}px` }">
            <section v-for="group in groups" :key="group.key" class="flex flex-col gap-2">
              <h3 class="flex items-center justify-between rounded-sm px-3 py-2 text-sm font-medium" :class="group.key === 'liveSolo.tournament.qualified' ? 'bg-primary text-primary-foreground' : 'bg-muted'"><span>{{ $t(group.key) }}</span><span class="font-mono tabular-nums">{{ group.rows.length }}</span></h3>
              <div v-for="row in group.rows" :key="row.teamId" class="flex min-w-0 items-center gap-2 px-3 py-1 text-sm"><Avatar size="sm"><AvatarImage v-if="row.avatarUrl" :src="row.avatarUrl" :alt="row.name" loading="lazy" /><AvatarFallback>{{ row.initials }}</AvatarFallback></Avatar><span class="min-w-0 flex-1 truncate">{{ row.name }}</span><span class="shrink-0 font-mono text-xs tabular-nums text-muted-foreground">{{ row.wins }} : {{ row.losses }}</span></div>
              <p v-if="!group.rows.length" class="px-3 py-1 text-xs text-muted-foreground">{{ $t('common.label.symbol') }}</p>
            </section>
          </aside>
        </div>
      </div>
    </ScrollSurface>
    <Empty v-else><EmptyHeader><EmptyTitle>{{ $t('liveSolo.tournament.pendingStage') }}</EmptyTitle></EmptyHeader></Empty>
    </div>
    <Collapsible v-if="standings.length && !isFullscreen" v-model:open="showStandings" class="flex flex-col gap-3"><CollapsibleTrigger as-child><Button variant="ghost" class="self-start">{{ $t('liveSolo.tournament.standings') }}</Button></CollapsibleTrigger><CollapsibleContent>
      <Table><TableHeader><TableRow><TableHead>{{ $t('common.label.team') }}</TableHead><TableHead>{{ $t('liveSolo.tournament.recordTitle') }}</TableHead><TableHead v-if="stage?.rules?.format === 'Swiss'">{{ $t('liveSolo.tournament.buchholz') }}</TableHead><TableHead>{{ $t('common.label.status') }}</TableHead></TableRow></TableHeader>
        <TableBody><TableRow v-for="row in standings" :key="row.teamId"><TableCell>{{ row.name }}</TableCell><TableCell class="font-mono tabular-nums">{{ row.wins }} : {{ row.losses }}</TableCell><TableCell v-if="stage?.rules?.format === 'Swiss'" class="font-mono tabular-nums">{{ row.buchholz }}</TableCell><TableCell><Badge variant="secondary">{{ $t(row.stateKey) }}</Badge></TableCell></TableRow></TableBody>
      </Table>
    </CollapsibleContent></Collapsible>
  </section>
</template>
