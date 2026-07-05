<script setup lang="ts">
import type {
  AwdpChallengeStatus,
  AwdpScreenConnectionStatus,
  AwdpScreenEvent,
  AwdpTeamScore,
} from '@/types/awdpScreen'
import { onMounted, onUnmounted, ref, watch } from 'vue'

const props = defineProps<{
  events: AwdpScreenEvent[]
  teams: AwdpTeamScore[]
  challenges: AwdpChallengeStatus[]
  connectionStatus: AwdpScreenConnectionStatus
}>()

const canvasRef = ref<HTMLCanvasElement | null>(null)

let ctx: CanvasRenderingContext2D | null = null
let frame = 0
let resizeObserver: ResizeObserver | null = null
let motionQuery: MediaQueryList | null = null
let reducedMotion = false

interface NodePoint {
  id: string
  x: number
  y: number
  weight: number
}

interface Palette {
  break: string
  fix: string
  warn: string
  error: string
  grid: string
  text: string
  border: string
}

onMounted(() => {
  const canvas = canvasRef.value
  if (!canvas)
    return

  ctx = canvas.getContext('2d', { alpha: true })
  motionQuery = window.matchMedia('(prefers-reduced-motion: reduce)')
  reducedMotion = motionQuery.matches
  motionQuery.addEventListener('change', handleMotionChange)

  resizeObserver = new ResizeObserver(() => {
    resizeCanvas()
    draw()
  })
  resizeObserver.observe(canvas)

  resizeCanvas()
  start()
})

onUnmounted(() => {
  stop()
  resizeObserver?.disconnect()
  resizeObserver = null
  motionQuery?.removeEventListener('change', handleMotionChange)
  motionQuery = null
})

watch(
  () => [
    props.events.length,
    props.teams.length,
    props.challenges.length,
    props.connectionStatus,
  ],
  () => {
    if (reducedMotion)
      draw()
  },
)

function handleMotionChange(event: MediaQueryListEvent) {
  reducedMotion = event.matches
  if (reducedMotion) {
    stop()
    draw()
  }
  else {
    start()
  }
}

function start() {
  stop()
  if (reducedMotion) {
    draw()
    return
  }

  const tick = () => {
    draw()
    frame = requestAnimationFrame(tick)
  }
  frame = requestAnimationFrame(tick)
}

function stop() {
  if (frame)
    cancelAnimationFrame(frame)
  frame = 0
}

function resizeCanvas() {
  const canvas = canvasRef.value
  if (!canvas || !ctx)
    return

  const rect = canvas.getBoundingClientRect()
  const dpr = Math.min(2, window.devicePixelRatio || 1)
  const width = Math.max(1, Math.round(rect.width * dpr))
  const height = Math.max(1, Math.round(rect.height * dpr))
  if (canvas.width !== width || canvas.height !== height) {
    canvas.width = width
    canvas.height = height
  }
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0)
}

function draw() {
  const canvas = canvasRef.value
  if (!canvas || !ctx)
    return

  const rect = canvas.getBoundingClientRect()
  const width = rect.width
  const height = rect.height
  if (width <= 0 || height <= 0)
    return

  const palette = readPalette(canvas)
  const now = Date.now()
  ctx.clearRect(0, 0, width, height)

  drawField(ctx, width, height, now, palette)
  drawServiceHeat(ctx, width, height, palette)
  drawNodeMesh(ctx, width, height, palette)
  drawEventVectors(ctx, width, height, now, palette)
  drawConnectionBand(ctx, width, height, palette)
}

function readPalette(canvas: HTMLCanvasElement): Palette {
  const styles = getComputedStyle(canvas)
  return {
    break: readCssVar(styles, '--awdp-break', 'oklch(0.73 0.13 205)'),
    fix: readCssVar(styles, '--awdp-fix', 'oklch(0.74 0.13 158)'),
    warn: readCssVar(styles, '--awdp-warn', 'oklch(0.78 0.15 68)'),
    error: readCssVar(styles, '--awdp-error', 'oklch(0.67 0.19 24)'),
    grid: readCssVar(styles, '--awdp-screen-grid', 'oklch(0.78 0.035 240 / 0.045)'),
    text: readCssVar(styles, '--sidebar-foreground', 'oklch(0.95 0.012 255)'),
    border: readCssVar(styles, '--awdp-screen-border', 'oklch(0.78 0.035 240 / 0.16)'),
  }
}

function readCssVar(styles: CSSStyleDeclaration, name: string, fallback: string) {
  return styles.getPropertyValue(name).trim() || fallback
}

function drawField(
  context: CanvasRenderingContext2D,
  width: number,
  height: number,
  now: number,
  palette: Palette,
) {
  const scanOffset = reducedMotion ? 0 : (now / 42) % 30
  context.save()
  context.lineWidth = 1
  context.strokeStyle = alphaColor(palette.text, 0.035)
  for (let y = -30 + scanOffset; y < height + 30; y += 30) {
    context.beginPath()
    context.moveTo(0, y)
    context.lineTo(width, y + width * 0.025)
    context.stroke()
  }

  context.strokeStyle = alphaColor(palette.break, 0.09)
  context.beginPath()
  context.moveTo(width * 0.08, height * 0.18)
  context.bezierCurveTo(width * 0.34, height * 0.02, width * 0.62, height * 0.02, width * 0.92, height * 0.18)
  context.stroke()

  context.strokeStyle = alphaColor(palette.fix, 0.08)
  context.beginPath()
  context.moveTo(width * 0.09, height * 0.83)
  context.bezierCurveTo(width * 0.34, height * 0.98, width * 0.64, height * 0.98, width * 0.91, height * 0.83)
  context.stroke()

  const cx = width * 0.5
  const cy = height * 0.52
  context.strokeStyle = alphaColor(palette.border, 0.38)
  for (const scale of [0.18, 0.29, 0.42]) {
    context.beginPath()
    context.ellipse(cx, cy, width * scale, height * scale * 0.62, 0, 0, Math.PI * 2)
    context.stroke()
  }
  context.restore()
}

function drawServiceHeat(
  context: CanvasRenderingContext2D,
  width: number,
  height: number,
  palette: Palette,
) {
  if (!props.challenges.length)
    return

  const rows = props.challenges.slice(0, 12)
  const barWidth = Math.max(20, width * 0.012)
  const gap = Math.max(5, height * 0.006)
  const rowHeight = (height * 0.52 - gap * (rows.length - 1)) / rows.length
  const x = width * 0.965 - barWidth
  const yStart = height * 0.24

  context.save()
  rows.forEach((challenge, index) => {
    const heat = Math.max(0.08, Math.min(1, challenge.attackHeat / 100))
    const y = yStart + index * (rowHeight + gap)
    context.fillStyle = alphaColor(palette.border, 0.26)
    context.fillRect(x, y, barWidth, rowHeight)
    context.fillStyle = alphaColor(challenge.defenseFailedCount > challenge.defensePassedCount ? palette.warn : palette.break, 0.22 + heat * 0.38)
    context.fillRect(x, y + rowHeight * (1 - heat), barWidth, rowHeight * heat)
  })
  context.restore()
}

function drawNodeMesh(
  context: CanvasRenderingContext2D,
  width: number,
  height: number,
  palette: Palette,
) {
  const teams = buildTeamNodes(width, height)
  const services = buildChallengeNodes(width, height)
  const center = { x: width * 0.5, y: height * 0.52 }

  context.save()
  context.lineWidth = 1
  context.strokeStyle = alphaColor(palette.text, 0.055)
  for (const team of teams) {
    context.beginPath()
    context.moveTo(team.x, team.y)
    context.quadraticCurveTo(width * 0.31, center.y, center.x, center.y)
    context.stroke()
  }
  for (const service of services) {
    context.beginPath()
    context.moveTo(center.x, center.y)
    context.quadraticCurveTo(width * 0.69, center.y, service.x, service.y)
    context.stroke()
  }

  drawNodes(context, teams, palette.fix)
  drawNodes(context, services, palette.break)
  drawCore(context, center.x, center.y, palette)
  context.restore()
}

function drawNodes(context: CanvasRenderingContext2D, nodes: NodePoint[], color: string) {
  for (const node of nodes) {
    const size = 4 + node.weight * 3
    context.fillStyle = alphaColor(color, 0.34)
    context.fillRect(node.x - size / 2, node.y - size / 2, size, size)
    context.strokeStyle = alphaColor(color, 0.42)
    context.strokeRect(node.x - size - 2, node.y - size - 2, size * 2 + 4, size * 2 + 4)
  }
}

function drawCore(context: CanvasRenderingContext2D, x: number, y: number, palette: Palette) {
  context.strokeStyle = alphaColor(palette.text, 0.18)
  context.lineWidth = 1
  context.beginPath()
  context.rect(x - 28, y - 28, 56, 56)
  context.stroke()

  context.strokeStyle = alphaColor(palette.break, 0.24)
  context.beginPath()
  context.moveTo(x - 42, y)
  context.lineTo(x - 18, y)
  context.moveTo(x + 18, y)
  context.lineTo(x + 42, y)
  context.moveTo(x, y - 42)
  context.lineTo(x, y - 18)
  context.moveTo(x, y + 18)
  context.lineTo(x, y + 42)
  context.stroke()
}

function drawEventVectors(
  context: CanvasRenderingContext2D,
  width: number,
  height: number,
  now: number,
  palette: Palette,
) {
  const teams = buildTeamNodes(width, height)
  const services = buildChallengeNodes(width, height)
  const teamById = new Map(teams.map(node => [node.id, node]))
  const serviceById = new Map(services.map(node => [node.id, node]))
  const center = { x: width * 0.5, y: height * 0.52, weight: 1, id: 'core' }
  const events = props.events.slice(0, 18)

  context.save()
  events.forEach((event, index) => {
    const createdAt = Date.parse(event.createdAt)
    const age = Number.isFinite(createdAt) ? now - createdAt : index * 280
    if (age > 120_000)
      return

    const from = event.teamId ? (teamById.get(event.teamId) ?? fallbackNode(event.teamId, width, height, 'team')) : center
    const to = event.challengeId ? (serviceById.get(event.challengeId) ?? fallbackNode(event.challengeId, width, height, 'service')) : center
    const color = eventColor(event, palette)
    const progress = reducedMotion ? 1 : ((age + index * 190) % 5200) / 5200
    const alpha = reducedMotion ? 0.2 : 0.12 + Math.sin(progress * Math.PI) * 0.46
    const lift = height * (event.type.startsWith('DEFENSE') ? 0.12 : -0.12)
    drawVector(context, from, to, lift, progress, color, alpha)
  })
  context.restore()
}

function drawVector(
  context: CanvasRenderingContext2D,
  from: NodePoint,
  to: NodePoint,
  lift: number,
  progress: number,
  color: string,
  alpha: number,
) {
  const control = {
    x: (from.x + to.x) / 2,
    y: (from.y + to.y) / 2 + lift,
  }
  const current = quadraticPoint(from, control, to, progress)

  context.lineWidth = 1.35
  context.strokeStyle = alphaColor(color, alpha * 0.42)
  context.beginPath()
  context.moveTo(from.x, from.y)
  context.quadraticCurveTo(control.x, control.y, current.x, current.y)
  context.stroke()

  context.fillStyle = alphaColor(color, alpha)
  context.beginPath()
  context.arc(current.x, current.y, 3.2, 0, Math.PI * 2)
  context.fill()

  context.strokeStyle = alphaColor(color, alpha * 0.52)
  context.beginPath()
  context.arc(current.x, current.y, 8.5, 0, Math.PI * 2)
  context.stroke()
}

function drawConnectionBand(
  context: CanvasRenderingContext2D,
  width: number,
  height: number,
  palette: Palette,
) {
  const color = props.connectionStatus === 'connected'
    ? palette.fix
    : props.connectionStatus === 'disconnected'
      ? palette.error
      : props.connectionStatus === 'mock'
        ? palette.warn
        : palette.break

  context.save()
  context.fillStyle = alphaColor(color, 0.14)
  context.fillRect(0, 0, width, 2)
  context.fillRect(0, height - 2, width, 2)
  context.restore()
}

function buildTeamNodes(width: number, height: number): NodePoint[] {
  const teams = props.teams.slice(0, 14)
  const count = Math.max(teams.length, 1)
  return teams.map((team, index) => ({
    id: team.teamId,
    x: width * 0.105 + hashToUnit(team.teamId, 3) * width * 0.04,
    y: height * 0.18 + (height * 0.64 * index) / count + hashToUnit(team.teamId, 5) * height * 0.035,
    weight: Math.min(1, Math.max(0.12, team.currentRoundScore / Math.max(1, team.totalScore || 1))),
  }))
}

function buildChallengeNodes(width: number, height: number): NodePoint[] {
  const challenges = props.challenges.slice(0, 14)
  const count = Math.max(challenges.length, 1)
  return challenges.map((challenge, index) => ({
    id: challenge.challengeId,
    x: width * 0.86 + hashToUnit(challenge.challengeId, 7) * width * 0.04,
    y: height * 0.18 + (height * 0.64 * index) / count + hashToUnit(challenge.challengeId, 11) * height * 0.035,
    weight: Math.min(1, Math.max(0.12, challenge.attackHeat / 100)),
  }))
}

function fallbackNode(id: string, width: number, height: number, side: 'team' | 'service'): NodePoint {
  return {
    id,
    x: side === 'team' ? width * (0.1 + hashToUnit(id, 17) * 0.08) : width * (0.82 + hashToUnit(id, 17) * 0.08),
    y: height * (0.18 + hashToUnit(id, 23) * 0.64),
    weight: 0.3,
  }
}

function eventColor(event: AwdpScreenEvent, palette: Palette) {
  if (event.type.startsWith('ATTACK'))
    return event.type === 'ATTACK_ACCEPTED' ? palette.break : palette.warn
  if (event.type.startsWith('DEFENSE') || event.type === 'PATCH_UPLOADED')
    return event.level === 'danger' ? palette.error : palette.fix
  if (event.type === 'SERVICE_ERROR')
    return palette.error
  return palette.text
}

function quadraticPoint(
  from: { x: number; y: number },
  control: { x: number; y: number },
  to: { x: number; y: number },
  progress: number,
) {
  const p = Math.max(0, Math.min(1, progress))
  const inverse = 1 - p
  return {
    x: inverse * inverse * from.x + 2 * inverse * p * control.x + p * p * to.x,
    y: inverse * inverse * from.y + 2 * inverse * p * control.y + p * p * to.y,
  }
}

function hashToUnit(value: string, salt: number) {
  let hash = salt
  for (let index = 0; index < value.length; index += 1)
    hash = (hash * 31 + value.charCodeAt(index)) >>> 0
  return (hash % 10_000) / 10_000
}

function alphaColor(color: string, alpha: number) {
  const clean = color.trim()
  if (clean.startsWith('oklch(') && !clean.includes('/'))
    return clean.replace(/\)$/, ` / ${Math.max(0, Math.min(1, alpha))})`)
  return clean
}
</script>

<template>
  <canvas ref="canvasRef" class="awdp-atmosphere-canvas" aria-hidden="true" />
</template>

<style scoped>
.awdp-atmosphere-canvas {
  position: absolute;
  inset: 0;
  z-index: 0;
  width: 100%;
  height: 100%;
  pointer-events: none;
}
</style>
