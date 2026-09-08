<script setup lang="ts">
import { toRefs } from 'vue'
import type { PointsDecayCurveViewState } from '~/features/admin/usePointsDecayCurve'

const viewProps = defineProps<{ state: PointsDecayCurveViewState }>()
const { ScoreDecayMode, width, height, inset, tooltipBox, hoveredCount, preview, formatInteger, tooltipTransform, onPointerMove, onPointerLeave, onKeydown, setSvgElementRef, curve } = toRefs(viewProps.state)
</script>

<template>
  <figure v-if="preview" class="col-span-full border border-border bg-muted/20 px-4 py-3">
    <figcaption class="mb-2 flex flex-wrap items-center justify-between gap-2 text-xs text-muted-foreground">
      <span>{{ $t('ui.scoreDecayCurvePreview') }} · {{ $t(preview.modeLabel) }}</span>
      <span class="font-mono tabular-nums">
        {{ formatInteger(preview.initialPoints) }} → {{ formatInteger(preview.minimumPoints) }} {{ $t('ui.pts2') }}
      </span>
    </figcaption>
    <svg
      :ref="setSvgElementRef"
      class="h-72 w-full touch-none overflow-visible outline-none focus-visible:ring-2 focus-visible:ring-primary"
      :viewBox="`0 0 ${width} ${height}`"
      role="img"
      tabindex="0"
      :aria-label="$t('ui.theScoreDecaysAsMoreTeamsSolveTheChallengeUse')"
      preserveAspectRatio="xMidYMid meet"
      @pointermove="onPointerMove"
      @pointerleave="onPointerLeave"
      @focus="hoveredCount ??= 1"
      @keydown="onKeydown"
    >
      <g aria-hidden="true">
        <line
          v-for="(tick, index) in preview.yTicks"
          :key="`y-${tick.score}`"
          :x1="inset.left"
          :x2="width - inset.right"
          :y1="tick.y"
          :y2="tick.y"
          :class="index === 0 || index === preview.yTicks.length - 1 ? 'stroke-muted-foreground/70' : 'stroke-border'"
          stroke-dasharray="3 4"
        />
        <line
          v-for="tick in preview.xTicks"
          :key="`x-${tick.count}`"
          :x1="tick.x"
          :x2="tick.x"
          :y1="inset.top"
          :y2="height - inset.bottom"
          class="stroke-border/80"
          stroke-dasharray="2 5"
        />
        <line :x1="inset.left" :x2="inset.left" :y1="inset.top" :y2="height - inset.bottom" class="stroke-muted-foreground" />
        <line :x1="inset.left" :x2="width - inset.right" :y1="height - inset.bottom" :y2="height - inset.bottom" class="stroke-muted-foreground" />
        <text
          v-for="tick in preview.yTicks"
          :key="`yl-${tick.score}`"
          :x="inset.left - 10"
          :y="tick.y + 4"
          text-anchor="end"
          class="fill-muted-foreground text-[12px] font-mono tabular-nums"
        >{{ formatInteger(tick.score) }}</text>
        <text
          v-for="tick in preview.xTicks"
          :key="`xl-${tick.count}`"
          :x="tick.x"
          :y="height - inset.bottom + 20"
          text-anchor="middle"
          class="fill-muted-foreground text-[12px] font-mono tabular-nums"
        >{{ tick.count }}</text>
        <text :x="inset.left" :y="inset.top - 13" class="fill-muted-foreground text-[12px] font-medium">{{ $t('ui.roundedScore') }} {{ $t('ui.pts3') }}</text>
        <text :x="width - inset.right" :y="height - 13" text-anchor="end" class="fill-muted-foreground text-[12px] font-medium">{{ $t('ui.solvedTeams') }}</text>
      </g>
      <polyline
        :points="preview.points"
        fill="none"
        class="stroke-primary"
        stroke-width="3"
        stroke-linecap="round"
        stroke-linejoin="round"
        vector-effect="non-scaling-stroke"
      />
      <line
        :x1="preview.active.x"
        :x2="preview.active.x"
        :y1="inset.top"
        :y2="height - inset.bottom"
        class="stroke-primary/60"
        stroke-dasharray="4 4"
        vector-effect="non-scaling-stroke"
      />
      <circle :cx="preview.active.x" :cy="preview.active.y" r="5" class="fill-background stroke-primary" stroke-width="3" />
      <g :transform="tooltipTransform">
        <rect :width="tooltipBox.width" :height="tooltipBox.height" rx="4" class="fill-popover stroke-border" />
        <text x="11" y="19" class="fill-muted-foreground text-[11px]">{{ $t('ui.solvedTeam', { count: preview.active.count }) }}</text>
        <text x="11" y="38" class="fill-popover-foreground text-[14px] font-mono font-semibold tabular-nums">{{ formatInteger(preview.active.score) }} {{ $t('ui.pts2') }}</text>
      </g>
    </svg>
    <p class="mt-1 text-xs text-muted-foreground">
      {{ $t('ui.hoverTheCurveToInspectTheRoundedScoreAtEach2') }}
    </p>
  </figure>
  <div v-else-if="curve.decayMode === ScoreDecayMode.Custom" class="border border-dashed border-border bg-muted/20 px-4 py-3 text-xs text-muted-foreground">
    {{ $t('ui.theServerValidatesEveryPointOfACustomFormulaIt') }}
  </div>
</template>
