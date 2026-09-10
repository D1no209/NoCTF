import { themeColor, themeColorAlpha } from '../utils/theme-color'
import * as THREE from 'three'
import { CSS2DObject, CSS2DRenderer } from 'three/addons/renderers/CSS2DRenderer.js'
import {
  fitLiveCityFrame,
  LIVE_CITY_CELL_SIZE,
  LIVE_CITY_FOV,
  LIVE_CITY_LABEL_OFFSET,
  liveCityBuildingHeight,
  liveCityPixelRatio,
  liveCitySlots,
} from './live-city-layout'

/**
 * 3D 实时大屏城市场景:每道赛题是一栋带程序化生成立面纹理的建筑,
 * 未攻陷为蓝色、已攻陷为红色;顶部浮标(CSS2D)展示题名/分值/血榜。
 * 解题聚焦时相机飞向建筑并触发光柱、冲击环、粒子爆发等特效。
 *
 * 本模块不持有任何中文文案,浮标文本全部由调用方格式化后传入。
 */

export interface LiveCityBlood {
  /** 已本地化的血名,如「一血」。 */
  label: string
  teamName: string
  tone: 'first' | 'second' | 'third'
  points: number
}

export interface LiveCityChallengeState {
  /** 归一化题目 id(小写无分隔符)。 */
  id: string
  title: string
  score: number
  solveCount: number
  /** 「12 解出」等已本地化文本,可为空。 */
  solvesText: string
  /** 是否已被任意队伍攻陷。 */
  solved: boolean
  bloods: LiveCityBlood[]
}

interface CameraRig {
  azimuth: number
  radius: number
  height: number
  lookX: number
  lookY: number
  lookZ: number
}

interface BuildingRecord {
  id: string
  group: THREE.Group
  tiers: THREE.Mesh[]
  edges: THREE.LineSegments[]
  beacon: THREE.Mesh
  beaconMaterial: THREE.MeshBasicMaterial
  glowMaterial: THREE.MeshBasicMaterial
  label: CSS2DObject
  labelName: HTMLElement
  labelPts: HTMLElement
  labelSolves: HTMLElement
  labelBloods: HTMLElement
  height: number
  solved: boolean
  variant: number
  blinkPhase: number
  /** 生长动画起始时刻(场景时钟)。 */
  birthAt: number
  riseDone: boolean
}

interface Comet {
  line: THREE.Line
  material: THREE.LineBasicMaterial
  position: THREE.Vector3
  direction: THREE.Vector3
  speed: number
  length: number
}

interface DataColumn {
  line: THREE.Line
  material: THREE.LineBasicMaterial
  x: number
  z: number
  phase: number
}

interface TimedEffect {
  update: (dt: number) => boolean
  dispose: () => void
}


const TWO_PI = Math.PI * 2

function hashId(id: string): number {
  let hash = 2166136261
  for (let index = 0; index < id.length; index++) {
    hash ^= id.charCodeAt(index)
    hash = Math.imul(hash, 16777619)
  }
  return hash >>> 0
}

function mulberry32(seed: number): () => number {
  let state = seed >>> 0
  return () => {
    state = (state + 0x6d2b79f5) >>> 0
    let value = state
    value = Math.imul(value ^ (value >>> 15), value | 1)
    value ^= value + Math.imul(value ^ (value >>> 7), value | 61)
    return ((value ^ (value >>> 14)) >>> 0) / 4294967296
  }
}

function easeInOutCubic(t: number): number {
  return t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2
}

function lerpAngle(from: number, to: number, t: number): number {
  let delta = (to - from) % TWO_PI
  if (delta > Math.PI) delta -= TWO_PI
  if (delta < -Math.PI) delta += TWO_PI
  return from + delta * t
}

function makeFacadeTextures(variant: number, solved: boolean, themeSource: Element): { map: THREE.CanvasTexture; emissive: THREE.CanvasTexture } {
  const width = 128
  const height = 256
  const stateColor = solved ? themeColor('--live-city-solved', themeSource) : themeColor('--live-city-locked', themeSource)
  const stateSoft = solved ? themeColorAlpha('--live-city-solved', 0.28, themeSource) : themeColorAlpha('--live-city-locked', 0.28, themeSource)
  const rand = mulberry32(variant * 7919 + (solved ? 13 : 7))

  const mapCanvas = document.createElement('canvas')
  mapCanvas.width = width
  mapCanvas.height = height
  const mapCtx = mapCanvas.getContext('2d')!
  const gradient = mapCtx.createLinearGradient(0, 0, 0, height)
  gradient.addColorStop(0, themeColor('--live-city-facade-top', themeSource))
  gradient.addColorStop(1, themeColor('--live-city-facade-bottom', themeSource))
  mapCtx.fillStyle = gradient
  mapCtx.fillRect(0, 0, width, height)

  const emissiveCanvas = document.createElement('canvas')
  emissiveCanvas.width = width
  emissiveCanvas.height = height
  const emissiveCtx = emissiveCanvas.getContext('2d')!
  emissiveCtx.fillStyle = '#000000'
  emissiveCtx.fillRect(0, 0, width, height)

  const cols = 5 + variant * 2
  const rows = 20
  const cellW = width / cols
  const cellH = height / rows
  for (let row = 0; row < rows; row++) {
    for (let col = 0; col < cols; col++) {
      const x = col * cellW + cellW * 0.22
      const y = row * cellH + cellH * 0.26
      const w = cellW * 0.56
      const h = cellH * 0.46
      const lit = rand() < (solved ? 0.5 : 0.38)
      mapCtx.fillStyle = lit ? stateSoft : themeColor('--live-city-facade-bottom', themeSource)
      mapCtx.fillRect(x, y, w, h)
      if (lit) {
        emissiveCtx.globalAlpha = 0.5 + rand() * 0.5
        emissiveCtx.fillStyle = stateColor
        emissiveCtx.fillRect(x, y, w, h)
        emissiveCtx.globalAlpha = 1
      }
    }
  }
  // 立面两侧的竖向结构线,增强体积感。
  mapCtx.fillStyle = 'rgba(255,255,255,0.06)'
  mapCtx.fillRect(0, 0, 3, height)
  mapCtx.fillRect(width - 3, 0, 3, height)
  emissiveCtx.fillStyle = stateColor
  emissiveCtx.globalAlpha = 0.55
  emissiveCtx.fillRect(0, 0, 2, height)
  emissiveCtx.fillRect(width - 2, 0, 2, height)
  emissiveCtx.globalAlpha = 1

  const map = new THREE.CanvasTexture(mapCanvas)
  map.colorSpace = THREE.SRGBColorSpace
  const emissive = new THREE.CanvasTexture(emissiveCanvas)
  emissive.colorSpace = THREE.SRGBColorSpace
  return { map, emissive }
}

function makeGlowTexture(): THREE.CanvasTexture {
  const size = 128
  const canvas = document.createElement('canvas')
  canvas.width = size
  canvas.height = size
  const ctx = canvas.getContext('2d')!
  const gradient = ctx.createRadialGradient(size / 2, size / 2, 0, size / 2, size / 2, size / 2)
  gradient.addColorStop(0, 'rgba(255,255,255,1)')
  gradient.addColorStop(0.35, 'rgba(255,255,255,0.5)')
  gradient.addColorStop(1, 'rgba(255,255,255,0)')
  ctx.fillStyle = gradient
  ctx.fillRect(0, 0, size, size)
  return new THREE.CanvasTexture(canvas)
}

export class LiveCityScene {
  private readonly container: HTMLElement
  private readonly renderer: THREE.WebGLRenderer
  private readonly labelRenderer: CSS2DRenderer
  private readonly scene = new THREE.Scene()
  private readonly camera: THREE.PerspectiveCamera
  private readonly clock = new THREE.Clock()
  private readonly resizeObserver: ResizeObserver
  private readonly labelResizeObserver: ResizeObserver
  private readonly buildings = new Map<string, BuildingRecord>()
  private readonly effects: TimedEffect[] = []
  private readonly facadeCache = new Map<string, { map: THREE.CanvasTexture; emissive: THREE.CanvasTexture }>()
  private readonly materialCache = new Map<string, THREE.MeshStandardMaterial>()
  private readonly edgeMaterials: Record<'locked' | 'solved', THREE.LineBasicMaterial>
  private readonly cityGroup = new THREE.Group()
  private readonly particles: THREE.Points[] = []
  private readonly orbitRings: THREE.Mesh[] = []
  private readonly comets: Comet[] = []
  private readonly dataColumns: DataColumn[] = []
  private readonly backdropGroup = new THREE.Group()
  private readonly silhouetteMaterial: THREE.MeshBasicMaterial
  private readonly silhouetteEdgeMaterial: THREE.LineBasicMaterial
  private readonly radar: THREE.Mesh
  private readonly radarMaterial: THREE.MeshBasicMaterial
  private readonly glowTexture = makeGlowTexture()
  private readonly groundMaterial = new THREE.MeshStandardMaterial()
  private readonly podiumMaterial = new THREE.MeshStandardMaterial()
  private readonly grid: THREE.GridHelper
  private readonly themeTokens = [
    '--live-city-background',
    '--live-city-facade-top',
    '--live-city-facade-bottom',
    '--live-city-locked',
    '--live-city-solved',
    '--live-city-accent',
    '--live-city-grid-major',
    '--live-city-grid-minor',
    '--live-city-backdrop',
    '--live-city-radar',
  ]
  private themeColors = new Map<string, string>()
  private readonly themeObserver: MutationObserver
  private themeTimer: ReturnType<typeof setTimeout> | undefined

  private rig: CameraRig = { azimuth: 0.7, radius: 96, height: 101, lookX: 0, lookY: 5, lookZ: 0 }
  private cruise: CameraRig = { ...this.rig }
  private tween: { from: CameraRig; to: CameraRig; elapsed: number; duration: number } | null = null
  private focusing = false
  private focusedId: string | null = null
  private worldRadius = 16
  private worldHeight = 34
  private frameId = 0
  private disposed = false
  private citySpan = 48

  constructor(container: HTMLElement) {
    this.container = container
    this.silhouetteMaterial = new THREE.MeshBasicMaterial({
      color: this.color('--live-city-background'),
      transparent: true,
      opacity: 0.96,
      depthWrite: false,
    })
    this.silhouetteEdgeMaterial = new THREE.LineBasicMaterial({
      color: new THREE.Color(this.color('--live-city-backdrop')),
      transparent: true,
      opacity: 0.3,
    })
    this.themeTokens.forEach(token => this.themeColors.set(new THREE.Color(this.color(token)).getHexString(), token))
    this.themeObserver = new MutationObserver(() => {
      if (this.themeTimer !== undefined) clearTimeout(this.themeTimer)
      this.themeTimer = setTimeout(() => { this.themeTimer = undefined; if (!this.disposed) this.refreshTheme() }, 150)
    })
    this.themeObserver.observe(document.documentElement, { attributes: true, attributeFilter: ['class', 'style'] })
    this.renderer = new THREE.WebGLRenderer({ antialias: true, alpha: false })
    this.renderer.toneMapping = THREE.ACESFilmicToneMapping
    this.renderer.toneMappingExposure = 1.05
    this.renderer.domElement.classList.add('live-city-canvas')
    container.appendChild(this.renderer.domElement)

    this.labelRenderer = new CSS2DRenderer()
    this.labelRenderer.domElement.classList.add('live-city-labels')
    container.appendChild(this.labelRenderer.domElement)

    this.camera = new THREE.PerspectiveCamera(LIVE_CITY_FOV, 1, 0.1, 600)
    this.scene.background = new THREE.Color(this.color('--live-city-background'))
    this.scene.fog = new THREE.FogExp2(new THREE.Color(this.color('--live-city-background')), 0.0032)

    this.scene.add(new THREE.HemisphereLight(this.color('--live-city-locked'), this.color('--live-city-background'), 0.55))
    const keyLight = new THREE.DirectionalLight(this.color('--live-city-locked'), 0.85)
    keyLight.position.set(70, 110, 40)
    this.scene.add(keyLight)
    const rimLight = new THREE.DirectionalLight(this.color('--live-city-accent'), 0.35)
    rimLight.position.set(-60, 50, -70)
    this.scene.add(rimLight)

    this.groundMaterial.color.set(this.color('--live-city-background'))
    this.groundMaterial.roughness = 0.92
    this.groundMaterial.metalness = 0.25
    const ground = new THREE.Mesh(new THREE.CircleGeometry(240, 72), this.groundMaterial)
    ground.rotation.x = -Math.PI / 2
    this.scene.add(ground)

    this.grid = new THREE.GridHelper(340, 68, this.color('--live-city-grid-major'), this.color('--live-city-grid-minor'))
    const gridMaterial = this.grid.material as THREE.Material
    gridMaterial.transparent = true
    gridMaterial.opacity = 0.38
    this.grid.position.y = 0.05
    this.scene.add(this.grid)

    this.podiumMaterial.color.set(this.color('--live-city-facade-bottom'))
    this.podiumMaterial.roughness = 0.7
    this.podiumMaterial.metalness = 0.45

    this.edgeMaterials = {
      locked: new THREE.LineBasicMaterial({ color: new THREE.Color(this.color('--live-city-locked')), transparent: true, opacity: 0.48 }),
      solved: new THREE.LineBasicMaterial({ color: new THREE.Color(this.color('--live-city-solved')), transparent: true, opacity: 0.62 }),
    }

    this.scene.add(this.cityGroup)
    this.scene.add(this.backdropGroup)
    this.buildParticles()
    this.buildOrbitRings()
    this.buildComets()
    this.buildDataColumns()

    this.radarMaterial = new THREE.MeshBasicMaterial({
      color: new THREE.Color(this.color('--live-city-radar')),
      transparent: true,
      opacity: 0.35,
      side: THREE.DoubleSide,
      blending: THREE.AdditiveBlending,
      depthWrite: false,
    })
    this.radar = new THREE.Mesh(new THREE.RingGeometry(0.96, 1, 64), this.radarMaterial)
    this.radar.rotation.x = -Math.PI / 2
    this.radar.position.y = 0.12
    this.scene.add(this.radar)

    this.resizeObserver = new ResizeObserver(() => this.resize())
    this.labelResizeObserver = new ResizeObserver(() => this.refitCamera(false))
    this.resizeObserver.observe(container)
    window.addEventListener('resize', this.resize)
    this.resize()
    this.tick()
  }

  private color(token: string): string {
    return themeColor(token, this.container)
  }

  private refreshTheme(): void {
    if (this.disposed) return
    const updateColor = (color: THREE.Color) => {
      const token = this.themeColors.get(color.getHexString())
      if (token) color.set(this.color(token))
    }
    this.scene.traverse(object => {
      if (object instanceof THREE.Light) updateColor(object.color)
      if (object instanceof THREE.HemisphereLight) updateColor(object.groundColor)
      if (object instanceof THREE.Mesh || object instanceof THREE.Line || object instanceof THREE.Points) {
        for (const material of Array.isArray(object.material) ? object.material : [object.material]) {
          if ('color' in material && material.color instanceof THREE.Color) updateColor(material.color)
          if ('emissive' in material && material.emissive instanceof THREE.Color) updateColor(material.emissive)
        }
      }
    })
    this.scene.background = new THREE.Color(this.color('--live-city-background'))
    if (this.scene.fog) this.scene.fog.color.set(this.color('--live-city-background'))
    for (const [key, material] of this.materialCache) {
      const [variant, solved] = key.split(':')
      const old = this.facadeCache.get(key)
      const textures = makeFacadeTextures(Number(variant), solved === '1', this.container)
      this.facadeCache.set(key, textures)
      material.map = textures.map
      material.emissiveMap = textures.emissive
      material.needsUpdate = true
      old?.map.dispose()
      old?.emissive.dispose()
    }
    const replacement = new THREE.GridHelper(340, 68, this.color('--live-city-grid-major'), this.color('--live-city-grid-minor'))
    this.grid.geometry.dispose()
    this.grid.geometry = replacement.geometry
    const materials = Array.isArray(replacement.material) ? replacement.material : [replacement.material]
    materials.forEach(material => material.dispose())
    this.themeColors.clear()
    this.themeTokens.forEach(token => this.themeColors.set(new THREE.Color(this.color(token)).getHexString(), token))
  }

  /** 更新题目集合;题目 id 集合变化时整体重建城市,否则原地刷新状态。 */
  setChallenges(states: readonly LiveCityChallengeState[]): void {
    if (this.disposed) return
    const incoming = new Set(states.map(state => state.id))
    const rebuild = incoming.size !== this.buildings.size
      || [...incoming].some(id => !this.buildings.has(id))
    if (rebuild) this.rebuild(states)
    else for (const state of states) this.applyState(state)
  }

  /** 聚焦某题建筑:相机飞近 + 光柱/冲击环/粒子爆发。 */
  focus(id: string): void {
    const building = this.buildings.get(id)
    if (!building || this.disposed) return
    this.focusing = true
    this.focusedId = id
    this.startTween(this.frameBuilding(building), 1.15)
    const color = building.solved ? new THREE.Color(this.color('--live-city-solved')) : new THREE.Color(this.color('--live-city-locked'))
    this.spawnBeam(building, color)
    this.spawnShockRings(building, color)
    this.spawnBurst(building, color)
    this.spawnGlow(building, color)
    this.spawnHaloRings(building, color)
  }

  /** 聚焦期间环绕建筑的倾斜光环。 */
  private spawnHaloRings(building: BuildingRecord, color: THREE.Color): void {
    const specs = [
      { radius: 6.4, tilt: Math.PI / 2 + 0.32, speed: 2.4 },
      { radius: 7.6, tilt: Math.PI / 2 - 0.38, speed: -1.7 },
    ]
    for (const spec of specs) {
      const material = new THREE.MeshBasicMaterial({
        color,
        transparent: true,
        opacity: 0,
        blending: THREE.AdditiveBlending,
        depthWrite: false,
      })
      const ring = new THREE.Mesh(new THREE.TorusGeometry(spec.radius, 0.09, 8, 72), material)
      ring.position.set(
        building.group.position.x,
        building.height * 0.55,
        building.group.position.z,
      )
      ring.rotation.x = spec.tilt
      this.scene.add(ring)
      let life = 0
      const duration = 4.4
      this.effects.push({
        update: (dt) => {
          life += dt
          const t = life / duration
          ring.rotation.z += dt * spec.speed
          material.opacity = t < 0.1 ? t / 0.1 * 0.85 : t > 0.7 ? (1 - (t - 0.7) / 0.3) * 0.85 : 0.85
          return t < 1
        },
        dispose: () => {
          ring.removeFromParent()
          ring.geometry.dispose()
          material.dispose()
        },
      })
    }
  }

  /** 标记当前聚焦的浮标,供 CSS 淡化其他浮标。 */
  setLabelFocus(id: string | null): void {
    for (const building of this.buildings.values()) {
      building.label.element.dataset.focus = building.id === id ? 'true' : 'false'
    }
  }

  /** 结束聚焦,相机回到巡航轨道。 */
  endFocus(): void {
    if (this.disposed) return
    this.focusing = false
    this.focusedId = null
    this.cruise.azimuth = this.rig.azimuth
    this.startTween({ ...this.cruise }, 1.4)
  }

  dispose(): void {
    this.disposed = true
    this.themeObserver.disconnect()
    if (this.themeTimer !== undefined) clearTimeout(this.themeTimer)
    cancelAnimationFrame(this.frameId)
    this.resizeObserver.disconnect()
    this.labelResizeObserver.disconnect()
    window.removeEventListener('resize', this.resize)
    for (const effect of this.effects) effect.dispose()
    this.effects.length = 0
    this.clearCity()
    for (const { map, emissive } of this.facadeCache.values()) {
      map.dispose()
      emissive.dispose()
    }
    this.facadeCache.clear()
    for (const material of this.materialCache.values()) material.dispose()
    this.materialCache.clear()
    this.edgeMaterials.locked.dispose()
    this.edgeMaterials.solved.dispose()
    this.silhouetteMaterial.dispose()
    this.silhouetteEdgeMaterial.dispose()
    for (const comet of this.comets) {
      comet.line.geometry.dispose()
      comet.material.dispose()
    }
    for (const column of this.dataColumns) {
      column.line.geometry.dispose()
      column.material.dispose()
    }
    this.glowTexture.dispose()
    this.groundMaterial.dispose()
    this.podiumMaterial.dispose()
    this.radarMaterial.dispose()
    this.radar.geometry.dispose()
    this.scene.traverse((object) => {
      if (object instanceof THREE.Mesh || object instanceof THREE.Points || object instanceof THREE.LineSegments) {
        object.geometry.dispose()
      }
    })
    this.renderer.dispose()
    this.renderer.domElement.remove()
    this.labelRenderer.domElement.remove()
  }

  private facadeMaterial(variant: number, solved: boolean): THREE.MeshStandardMaterial {
    const key = `${variant}:${solved ? 1 : 0}`
    const cached = this.materialCache.get(key)
    if (cached) return cached
    const texturesKey = key
    let textures = this.facadeCache.get(texturesKey)
    if (!textures) {
      textures = makeFacadeTextures(variant, solved, this.container)
      this.facadeCache.set(texturesKey, textures)
    }
    const material = new THREE.MeshStandardMaterial({
      map: textures.map,
      emissiveMap: textures.emissive,
      emissive: solved ? new THREE.Color(this.color('--live-city-solved')).clone() : new THREE.Color(this.color('--live-city-locked')).clone(),
      emissiveIntensity: 1.5,
      color: new THREE.Color(this.color('--live-city-locked')),
      roughness: 0.82,
      metalness: 0.2,
    })
    material.userData.base = solved ? 1.55 : 1.38
    material.userData.phase = solved ? 2.1 : 0
    this.materialCache.set(key, material)
    return material
  }

  private rebuild(states: readonly LiveCityChallengeState[]): void {
    this.clearCity()

    const sorted = [...states].sort((a, b) => b.score - a.score)
    const maxScore = Math.max(1, ...sorted.map(state => state.score))
    // 分数最高的建筑放在靠近中心的位置。
    const positions = liveCitySlots(sorted.length)
    this.citySpan = Math.max(1, Math.ceil(Math.sqrt(sorted.length))) * LIVE_CITY_CELL_SIZE
    this.buildBackdrop(this.citySpan)
    this.worldRadius = 16
    this.worldHeight = 34

    sorted.forEach((state, index) => {
      const slot = positions[index]!
      const rand = mulberry32(hashId(state.id))
      const record = this.createBuilding(state, rand, maxScore, sorted.length, this.clock.elapsedTime + Math.min(index * 0.06, 1.2))
      record.group.position.set(
        slot.x + (rand() - 0.5) * 2.4,
        0,
        slot.z + (rand() - 0.5) * 2.4,
      )
      record.group.rotation.y = (rand() - 0.5) * 0.5
      record.group.scale.y = 0.001
      this.cityGroup.add(record.group)
      this.buildings.set(state.id, record)
      this.labelResizeObserver.observe(record.label.element)
      // Half-diagonal of the largest podium, independent of its random rotation.
      this.worldRadius = Math.max(this.worldRadius, Math.hypot(record.group.position.x, record.group.position.z) + 9.2)
      this.worldHeight = Math.max(this.worldHeight, record.height + LIVE_CITY_LABEL_OFFSET)
    })
    if (this.focusedId && !this.buildings.has(this.focusedId)) {
      this.focusedId = null
      this.focusing = false
    }
    this.setLabelFocus(this.focusedId)
    this.refitCamera()
  }

  private createBuilding(
    state: LiveCityChallengeState,
    rand: () => number,
    maxScore: number,
    count: number,
    birthAt: number,
  ): BuildingRecord {
    const group = new THREE.Group()
    const variant = Math.floor(rand() * 3)
    const height = liveCityBuildingHeight(state.score, maxScore, count)
    const baseWidth = 6.8 + rand() * 3.2
    const baseDepth = 6.8 + rand() * 3.2

    const podium = new THREE.Mesh(
      new THREE.BoxGeometry(baseWidth + 3, 1.1, baseDepth + 3),
      this.podiumMaterial,
    )
    podium.position.y = 0.55
    group.add(podium)

    const tiers: THREE.Mesh[] = []
    const edges: THREE.LineSegments[] = []
    const material = this.facadeMaterial(variant, state.solved)

    const tierSpecs: { w: number; d: number; h: number }[] = [{ w: baseWidth, d: baseDepth, h: height * 0.62 }]
    if (rand() < 0.68) tierSpecs.push({ w: baseWidth * 0.72, d: baseDepth * 0.72, h: height * 0.26 })
    if (rand() < 0.4) tierSpecs.push({ w: baseWidth * 0.5, d: baseDepth * 0.5, h: height * 0.14 })
    // 保证总高度达到映射高度。
    const specTotal = tierSpecs.reduce((sum, spec) => sum + spec.h, 0)
    const scale = height / specTotal

    let cursor = 1.1
    for (const spec of tierSpecs) {
      const tierHeight = spec.h * scale
      const geometry = new THREE.BoxGeometry(spec.w, tierHeight, spec.d)
      const tier = new THREE.Mesh(geometry, material)
      tier.position.y = cursor + tierHeight / 2
      group.add(tier)
      tiers.push(tier)
      const edge = new THREE.LineSegments(
        new THREE.EdgesGeometry(geometry),
        state.solved ? this.edgeMaterials.solved : this.edgeMaterials.locked,
      )
      edge.position.copy(tier.position)
      group.add(edge)
      edges.push(edge)
      cursor += tierHeight
    }

    const topY = cursor
    const glowMaterial = new THREE.MeshBasicMaterial({
      color: state.solved ? new THREE.Color(this.color('--live-city-solved')) : new THREE.Color(this.color('--live-city-locked')),
      transparent: true,
      opacity: 0.18,
      blending: THREE.AdditiveBlending,
      depthWrite: false,
    })
    const glow = new THREE.Mesh(new THREE.CircleGeometry(7.5, 36), glowMaterial)
    glow.rotation.x = -Math.PI / 2
    glow.position.y = 0.09
    group.add(glow)

    const beaconMaterial = new THREE.MeshBasicMaterial({
      color: state.solved ? new THREE.Color(this.color('--live-city-solved')) : new THREE.Color(this.color('--live-city-locked')),
      transparent: true,
      opacity: 0.95,
    })
    const beacon = new THREE.Mesh(new THREE.SphereGeometry(0.34, 12, 12), beaconMaterial)
    if (rand() < 0.62) {
      const antenna = new THREE.Mesh(
        new THREE.CylinderGeometry(0.07, 0.11, 3.2, 6),
        this.podiumMaterial,
      )
      antenna.position.y = topY + 1.6
      group.add(antenna)
      beacon.position.y = topY + 3.35
    }
    else {
      beacon.position.y = topY + 0.42
    }
    group.add(beacon)

    const labelEls = this.createLabel(state)
    labelEls.root.style.opacity = '0'
    labelEls.root.style.transition = 'opacity .45s ease'
    const label = new CSS2DObject(labelEls.root)
    label.position.set(0, topY + LIVE_CITY_LABEL_OFFSET, 0)
    group.add(label)

    return {
      id: state.id,
      group,
      tiers,
      edges,
      beacon,
      beaconMaterial,
      glowMaterial,
      label,
      labelName: labelEls.name,
      labelPts: labelEls.pts,
      labelSolves: labelEls.solves,
      labelBloods: labelEls.bloods,
      height: topY,
      solved: state.solved,
      variant,
      blinkPhase: rand() * TWO_PI,
      birthAt,
      riseDone: false,
    }
  }

  private createLabel(state: LiveCityChallengeState) {
    const root = document.createElement('div')
    root.className = 'live-label'
    root.dataset.state = state.solved ? 'solved' : 'locked'

    const name = document.createElement('div')
    name.className = 'live-label-name'
    const meta = document.createElement('div')
    meta.className = 'live-label-meta'
    const pts = document.createElement('span')
    pts.className = 'live-label-pts'
    const solves = document.createElement('span')
    solves.className = 'live-label-solves'
    meta.append(pts, solves)
    const bloods = document.createElement('div')
    bloods.className = 'live-label-bloods'
    root.append(name, meta, bloods)

    this.fillLabel({ labelName: name, labelPts: pts, labelSolves: solves, labelBloods: bloods }, state)
    return { root, name, pts, solves, bloods }
  }

  private fillLabel(
    els: Pick<BuildingRecord, 'labelName' | 'labelPts' | 'labelSolves' | 'labelBloods'>,
    state: LiveCityChallengeState,
  ): void {
    els.labelName.textContent = state.title
    els.labelPts.textContent = `${state.score} pts`
    els.labelSolves.textContent = state.solvesText
    els.labelBloods.replaceChildren()
    for (const blood of state.bloods.slice(0, 3)) {
      const badge = document.createElement('span')
      badge.className = `live-label-blood live-label-blood-${blood.tone}`
      badge.textContent = `${blood.label} ${blood.teamName} +${blood.points} pts`
      els.labelBloods.appendChild(badge)
    }
  }

  private applyState(state: LiveCityChallengeState): void {
    const building = this.buildings.get(state.id)
    if (!building) return
    if (building.solved !== state.solved) {
      building.solved = state.solved
      const material = this.facadeMaterial(building.variant, state.solved)
      for (const tier of building.tiers) tier.material = material
      const edgeMaterial = state.solved ? this.edgeMaterials.solved : this.edgeMaterials.locked
      for (const edge of building.edges) edge.material = edgeMaterial
      building.beaconMaterial.color.set(state.solved ? new THREE.Color(this.color('--live-city-solved')) : new THREE.Color(this.color('--live-city-locked')))
      building.glowMaterial.color.set(state.solved ? new THREE.Color(this.color('--live-city-solved')) : new THREE.Color(this.color('--live-city-locked')))
      const root = building.label.element
      root.dataset.state = state.solved ? 'solved' : 'locked'
    }
    this.fillLabel(building, state)
  }

  private clearCity(): void {
    for (const building of this.buildings.values()) {
      this.labelResizeObserver.unobserve(building.label.element)
      building.label.removeFromParent()
      building.beaconMaterial.dispose()
      building.glowMaterial.dispose()
      building.group.traverse((object) => {
        if (object instanceof THREE.Mesh || object instanceof THREE.LineSegments) object.geometry.dispose()
      })
      building.group.removeFromParent()
    }
    this.buildings.clear()
  }

  private buildParticles(): void {
    const specs = [
      { color: new THREE.Color(this.color('--live-city-accent')), count: 420, size: 0.55 },
      { color: new THREE.Color(this.color('--live-city-locked')), count: 420, size: 0.7 },
    ]
    for (const spec of specs) {
      const positions = new Float32Array(spec.count * 3)
      const rand = mulberry32(spec.count)
      for (let index = 0; index < spec.count; index++) {
        positions[index * 3] = (rand() - 0.5) * 260
        positions[index * 3 + 1] = 3 + rand() * 85
        positions[index * 3 + 2] = (rand() - 0.5) * 260
      }
      const geometry = new THREE.BufferGeometry()
      geometry.setAttribute('position', new THREE.BufferAttribute(positions, 3))
      const material = new THREE.PointsMaterial({
        color: spec.color,
        size: spec.size,
        transparent: true,
        opacity: 0.5,
        blending: THREE.AdditiveBlending,
        depthWrite: false,
      })
      const points = new THREE.Points(geometry, material)
      this.particles.push(points)
      this.scene.add(points)
    }
  }

  private buildOrbitRings(): void {
    const specs = [
      { radius: 30, color: new THREE.Color(this.color('--live-city-locked')), tilt: 0.42, speed: 0.05 },
      { radius: 40, color: new THREE.Color(this.color('--live-city-accent')), tilt: -0.3, speed: -0.034 },
    ]
    for (const spec of specs) {
      const ring = new THREE.Mesh(
        new THREE.TorusGeometry(spec.radius, 0.07, 8, 128),
        new THREE.MeshBasicMaterial({
          color: spec.color,
          transparent: true,
          opacity: 0.3,
          blending: THREE.AdditiveBlending,
          depthWrite: false,
        }),
      )
      ring.rotation.x = Math.PI / 2 + spec.tilt
      ring.position.y = 16
      ring.userData.speed = spec.speed
      this.orbitRings.push(ring)
      this.scene.add(ring)
    }
  }

  private buildComets(): void {
    const rand = mulberry32(0xc0ffee)
    for (let index = 0; index < 9; index++) {
      const geometry = new THREE.BufferGeometry()
      geometry.setAttribute('position', new THREE.BufferAttribute(new Float32Array(6), 3))
      const material = new THREE.LineBasicMaterial({
        color: index % 2 ? new THREE.Color(this.color('--live-city-locked')) : new THREE.Color(this.color('--live-city-accent')),
        transparent: true,
        opacity: 0.75,
        blending: THREE.AdditiveBlending,
        depthWrite: false,
      })
      const line = new THREE.Line(geometry, material)
      const comet: Comet = {
        line,
        material,
        position: new THREE.Vector3(),
        direction: new THREE.Vector3(),
        speed: 0,
        length: 0,
      }
      this.respawnComet(comet, rand, true)
      this.comets.push(comet)
      this.scene.add(line)
    }
  }

  private respawnComet(comet: Comet, rand: () => number, scatter = false): void {
    const horizontal = rand() * TWO_PI
    comet.direction.set(Math.cos(horizontal), (rand() - 0.5) * 0.12, Math.sin(horizontal)).normalize()
    comet.speed = 22 + rand() * 30
    comet.length = 9 + rand() * 12
    const distance = scatter ? rand() * 150 : 150
    comet.position.set(
      -comet.direction.x * distance + (rand() - 0.5) * 120,
      24 + rand() * 48,
      -comet.direction.z * distance + (rand() - 0.5) * 120,
    )
  }

  private buildDataColumns(): void {
    const rand = mulberry32(0xda7a)
    for (let index = 0; index < 20; index++) {
      const geometry = new THREE.BufferGeometry()
      geometry.setAttribute('position', new THREE.BufferAttribute(new Float32Array(6), 3))
      const material = new THREE.LineBasicMaterial({
        color: index % 3 ? new THREE.Color(this.color('--live-city-radar')) : new THREE.Color(this.color('--live-city-accent')),
        transparent: true,
        opacity: 0,
        blending: THREE.AdditiveBlending,
        depthWrite: false,
      })
      const line = new THREE.Line(geometry, material)
      const angle = rand() * TWO_PI
      const radius = 24 + rand() * 110
      this.dataColumns.push({
        line,
        material,
        x: Math.cos(angle) * radius,
        z: Math.sin(angle) * radius,
        phase: rand(),
      })
      this.scene.add(line)
    }
  }

  private buildBackdrop(maxScoreSpan: number): void {
    for (const child of [...this.backdropGroup.children]) {
      if (child instanceof THREE.Mesh || child instanceof THREE.LineSegments) child.geometry.dispose()
      child.removeFromParent()
    }
    const rand = mulberry32(Math.floor(maxScoreSpan) * 31 + 5)
    const rings = [
      { count: 26, from: 1.18, to: 1.55, minHeight: 6, maxHeight: 24 },
      { count: 36, from: 1.65, to: 2.15, minHeight: 4, maxHeight: 18 },
    ]
    for (const ring of rings) {
      for (let index = 0; index < ring.count; index++) {
        const angle = (index / ring.count) * TWO_PI + rand() * 0.24
        const radius = maxScoreSpan * (ring.from + rand() * (ring.to - ring.from))
        const width = 5 + rand() * 8
        const depth = 5 + rand() * 8
        const height = ring.minHeight + rand() * (ring.maxHeight - ring.minHeight)
        const geometry = new THREE.BoxGeometry(width, height, depth)
        const tower = new THREE.Mesh(geometry, this.silhouetteMaterial)
        tower.position.set(Math.cos(angle) * radius, height / 2, Math.sin(angle) * radius)
        tower.rotation.y = rand() * Math.PI
        this.backdropGroup.add(tower)
        const edge = new THREE.LineSegments(new THREE.EdgesGeometry(geometry), this.silhouetteEdgeMaterial)
        edge.position.copy(tower.position)
        edge.rotation.copy(tower.rotation)
        this.backdropGroup.add(edge)
      }
    }
  }

  private startTween(to: CameraRig, duration: number): void {
    this.tween = { from: { ...this.rig }, to, elapsed: 0, duration }
  }

  private spawnBeam(building: BuildingRecord, color: THREE.Color): void {
    const material = new THREE.MeshBasicMaterial({
      color,
      transparent: true,
      opacity: 0,
      blending: THREE.AdditiveBlending,
      side: THREE.DoubleSide,
      depthWrite: false,
    })
    const beam = new THREE.Mesh(new THREE.CylinderGeometry(0.5, 1.4, 150, 16, 1, true), material)
    beam.position.set(building.group.position.x, 75, building.group.position.z)
    this.scene.add(beam)
    let life = 0
    const duration = 4.4
    this.effects.push({
      update: (dt) => {
        life += dt
        const t = life / duration
        material.opacity = t < 0.12 ? t / 0.12 * 0.75 : t > 0.7 ? (1 - (t - 0.7) / 0.3) * 0.75 : 0.75
        beam.rotation.y += dt * 1.6
        return t < 1
      },
      dispose: () => {
        beam.removeFromParent()
        beam.geometry.dispose()
        material.dispose()
      },
    })
  }

  private spawnShockRings(building: BuildingRecord, color: THREE.Color): void {
    for (let index = 0; index < 3; index++) {
      const material = new THREE.MeshBasicMaterial({
        color,
        transparent: true,
        opacity: 0,
        side: THREE.DoubleSide,
        blending: THREE.AdditiveBlending,
        depthWrite: false,
      })
      const ring = new THREE.Mesh(new THREE.RingGeometry(0.9, 1, 48), material)
      ring.rotation.x = -Math.PI / 2
      ring.position.set(building.group.position.x, 0.15, building.group.position.z)
      this.scene.add(ring)
      const delay = index * 0.22
      const duration = 1.6
      let life = -delay
      this.effects.push({
        update: (dt) => {
          life += dt
          if (life < 0) return true
          const t = life / duration
          const scaleV = 1 + t * 17
          ring.scale.setScalar(scaleV)
          material.opacity = Math.max(0, (1 - t) * 0.8)
          return t < 1
        },
        dispose: () => {
          ring.removeFromParent()
          ring.geometry.dispose()
          material.dispose()
        },
      })
    }
  }

  private spawnBurst(building: BuildingRecord, color: THREE.Color): void {
    const count = 90
    const positions = new Float32Array(count * 3)
    const velocities = new Float32Array(count * 3)
    const rand = mulberry32(hashId(building.id) ^ 0x9e3779b9)
    const origin = new THREE.Vector3(
      building.group.position.x,
      building.height + 1,
      building.group.position.z,
    )
    for (let index = 0; index < count; index++) {
      positions.set([origin.x, origin.y, origin.z], index * 3)
      const theta = rand() * TWO_PI
      const speed = 5 + rand() * 12
      velocities[index * 3] = Math.cos(theta) * speed
      velocities[index * 3 + 1] = 4 + rand() * 11
      velocities[index * 3 + 2] = Math.sin(theta) * speed
    }
    const geometry = new THREE.BufferGeometry()
    geometry.setAttribute('position', new THREE.BufferAttribute(positions, 3))
    const material = new THREE.PointsMaterial({
      color,
      size: 0.85,
      transparent: true,
      opacity: 1,
      blending: THREE.AdditiveBlending,
      depthWrite: false,
    })
    const points = new THREE.Points(geometry, material)
    this.scene.add(points)
    let life = 0
    const duration = 1.6
    this.effects.push({
      update: (dt) => {
        life += dt
        const t = life / duration
        const attribute = geometry.getAttribute('position') as THREE.BufferAttribute
        for (let index = 0; index < count; index++) {
          attribute.setXYZ(
            index,
            attribute.getX(index) + velocities[index * 3]! * dt,
            attribute.getY(index) + velocities[index * 3 + 1]! * dt,
            attribute.getZ(index) + velocities[index * 3 + 2]! * dt,
          )
          velocities[index * 3 + 1]! -= 22 * dt
        }
        attribute.needsUpdate = true
        material.opacity = Math.max(0, 1 - t)
        return t < 1
      },
      dispose: () => {
        points.removeFromParent()
        geometry.dispose()
        material.dispose()
      },
    })
  }

  private spawnGlow(building: BuildingRecord, color: THREE.Color): void {
    const material = new THREE.SpriteMaterial({
      map: this.glowTexture,
      color,
      transparent: true,
      opacity: 0.9,
      blending: THREE.AdditiveBlending,
      depthWrite: false,
    })
    const sprite = new THREE.Sprite(material)
    sprite.position.set(building.group.position.x, building.height + 2.5, building.group.position.z)
    this.scene.add(sprite)
    let life = 0
    const duration = 4.2
    this.effects.push({
      update: (dt) => {
        life += dt
        const t = life / duration
        const pulse = 6 + Math.sin(life * 5.2) * 1.6
        sprite.scale.setScalar(pulse * (1 + t * 0.6))
        material.opacity = t < 0.1 ? t / 0.1 * 0.85 : (1 - t) * 0.85
        return t < 1
      },
      dispose: () => {
        sprite.removeFromParent()
        material.dispose()
      },
    })
  }

  private resize = (): void => {
    if (this.disposed) return
    const width = this.container.clientWidth
    const height = this.container.clientHeight
    if (width <= 0 || height <= 0) return
    this.renderer.setPixelRatio(liveCityPixelRatio(width, height, window.devicePixelRatio))
    this.renderer.setSize(width, height, false)
    this.labelRenderer.setSize(width, height)
    this.camera.aspect = width / height
    this.refitCamera()
  }

  private labelSize(): { width: number; height: number } {
    let width = 0
    let height = 0
    for (const building of this.buildings.values()) {
      width = Math.max(width, building.label.element.offsetWidth)
      height = Math.max(height, building.label.element.offsetHeight)
    }
    return { width: width || 136, height: height || 60 }
  }

  private frameBuilding(building: BuildingRecord): CameraRig {
    const label = this.labelSize()
    const frame = fitLiveCityFrame(9.2, building.height + LIVE_CITY_LABEL_OFFSET,
      this.container.clientWidth, this.container.clientHeight, label.width, label.height)
    return {
      azimuth: Math.atan2(building.group.position.x, building.group.position.z),
      radius: frame.radius,
      height: frame.height,
      lookX: building.group.position.x,
      lookY: frame.lookY,
      lookZ: building.group.position.z,
    }
  }

  private refitCamera(immediate = true): void {
    if (this.disposed || this.container.clientWidth <= 0 || this.container.clientHeight <= 0) return
    const labelSpace = this.container.clientWidth / Math.max(1, Math.sqrt(this.buildings.size))
    this.container.dataset.compactLabels = String(labelSpace < 180)
    this.container.style.setProperty('--live-label-width', `${Math.max(80, Math.min(144, labelSpace))}px`)
    const label = this.labelSize()
    const frame = fitLiveCityFrame(this.worldRadius, this.worldHeight,
      this.container.clientWidth, this.container.clientHeight, label.width, label.height)
    this.cruise = {
      radius: frame.radius,
      height: frame.height,
      lookY: frame.lookY,
      azimuth: this.rig.azimuth,
      lookX: 0,
      lookZ: 0,
    }
    const focused = this.focusedId ? this.buildings.get(this.focusedId) : undefined
    const target = focused ? this.frameBuilding(focused) : { ...this.cruise }
    if (immediate) {
      // Viewport resize must not retain an old, clipped framing.
      this.tween = null
      this.rig = target
    }
    else {
      // Expanded labels must not cut short the focus/return flight with a camera jump.
      const remaining = this.tween ? this.tween.duration - this.tween.elapsed : 0.3
      this.startTween(target, Math.max(0.3, remaining))
    }
    this.camera.far = frame.far
    this.camera.updateProjectionMatrix()
    const distance = Math.hypot(frame.radius, frame.height - frame.lookY)
    const fog = this.scene.fog as THREE.FogExp2
    fog.density = Math.min(0.0032, 0.5 / distance)
  }

  private tick = (): void => {
    if (this.disposed) return
    this.frameId = requestAnimationFrame(this.tick)
    const dt = Math.min(this.clock.getDelta(), 0.1)
    const time = this.clock.elapsedTime

    if (this.tween) {
      this.tween.elapsed += dt
      const t = easeInOutCubic(Math.min(1, this.tween.elapsed / this.tween.duration))
      const { from, to } = this.tween
      this.rig.azimuth = lerpAngle(from.azimuth, to.azimuth, t)
      this.rig.radius = from.radius + (to.radius - from.radius) * t
      this.rig.height = from.height + (to.height - from.height) * t
      this.rig.lookX = from.lookX + (to.lookX - from.lookX) * t
      this.rig.lookY = from.lookY + (to.lookY - from.lookY) * t
      this.rig.lookZ = from.lookZ + (to.lookZ - from.lookZ) * t
      if (this.tween.elapsed >= this.tween.duration) this.tween = null
    }
    else if (!this.focusing) {
      this.rig.azimuth += dt * 0.055
      this.rig.radius = this.cruise.radius
      this.rig.height = this.cruise.height
    }

    this.camera.position.set(
      this.rig.lookX + Math.sin(this.rig.azimuth) * this.rig.radius,
      this.rig.height,
      this.rig.lookZ + Math.cos(this.rig.azimuth) * this.rig.radius,
    )
    this.camera.lookAt(this.rig.lookX, this.rig.lookY, this.rig.lookZ)

    const backdropOpacity = this.focusing ? 0.08 : 0.96
    const backdropEdgeOpacity = this.focusing ? 0.04 : 0.3
    const backdropEase = Math.min(1, dt * 7)
    this.silhouetteMaterial.opacity += (backdropOpacity - this.silhouetteMaterial.opacity) * backdropEase
    this.silhouetteEdgeMaterial.opacity += (backdropEdgeOpacity - this.silhouetteEdgeMaterial.opacity) * backdropEase

    for (const building of this.buildings.values()) {
      const pulse = 0.72 + Math.sin(time * 2.4 + building.blinkPhase) * 0.28
      building.beaconMaterial.opacity = pulse
      building.beacon.scale.setScalar(0.85 + pulse * 0.35)
      if (!building.riseDone) {
        const rise = Math.min(1, Math.max(0, (time - building.birthAt) / 0.9))
        building.group.scale.y = Math.max(0.001, 1 - Math.pow(1 - rise, 3))
        if (rise >= 1) {
          building.riseDone = true
          building.group.scale.y = 1
          building.label.element.style.opacity = '1'
        }
      }
    }
    for (const material of this.materialCache.values()) {
      material.emissiveIntensity = (material.userData.base as number)
        + Math.sin(time * 1.4 + (material.userData.phase as number)) * 0.22
    }
    this.particles[0]!.rotation.y += dt * 0.011
    this.particles[1]!.rotation.y -= dt * 0.008
    for (const ring of this.orbitRings) ring.rotation.z += (ring.userData.speed as number) * dt

    const cometRand = mulberry32(Math.floor(time * 997) + 17)
    for (const comet of this.comets) {
      comet.position.addScaledVector(comet.direction, comet.speed * dt)
      if (Math.abs(comet.position.x) > 170 || Math.abs(comet.position.z) > 170) {
        this.respawnComet(comet, cometRand)
      }
      const attribute = comet.line.geometry.getAttribute('position') as THREE.BufferAttribute
      attribute.setXYZ(0, comet.position.x, comet.position.y, comet.position.z)
      attribute.setXYZ(
        1,
        comet.position.x - comet.direction.x * comet.length,
        comet.position.y - comet.direction.y * comet.length,
        comet.position.z - comet.direction.z * comet.length,
      )
      attribute.needsUpdate = true
    }
    for (const column of this.dataColumns) {
      const cycle = (time * 0.28 + column.phase) % 1
      const base = cycle * 52
      const attribute = column.line.geometry.getAttribute('position') as THREE.BufferAttribute
      attribute.setXYZ(0, column.x, base, column.z)
      attribute.setXYZ(1, column.x, base + 7, column.z)
      attribute.needsUpdate = true
      column.material.opacity = Math.sin(cycle * Math.PI) * 0.5
    }

    const radarCycle = (time % 5) / 5
    this.radar.scale.setScalar(4 + radarCycle * this.citySpan * 0.9)
    this.radarMaterial.opacity = (1 - radarCycle) * 0.45

    for (let index = this.effects.length - 1; index >= 0; index--) {
      const effect = this.effects[index]!
      if (!effect.update(dt)) {
        effect.dispose()
        this.effects.splice(index, 1)
      }
    }

    this.renderer.render(this.scene, this.camera)
    this.labelRenderer.render(this.scene, this.camera)
  }
}
