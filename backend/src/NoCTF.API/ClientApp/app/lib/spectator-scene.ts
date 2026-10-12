import * as THREE from 'three'
import { themeColor } from '../utils/theme-color'
import { createSpectatorRibbon } from '../motion/spectator-ribbon'
import { setFlagTransfer } from '../motion/spectator-flag'
import { createTerrain, terrainHeight, terrainFragments } from './spectator-terrain'
import { createSpectatorRock } from './spectator-rock'
import rockTextureUrl from '../assets/images/spectator/koh-stratified-rock.png'

export interface SpectatorNode { id: string; name: string; owner?: number; ownerName?: string; controllerName?: string; health?: 'Healthy' | 'Down' | 'Unknown'; services?: ('Healthy' | 'Down' | 'Unknown')[] }
export interface SpectatorBeam { id: string; source: string; target: string; service: number }
export interface SpectatorSceneState { mode: 'Awd' | 'Koh'; nodes: SpectatorNode[]; focus: string; highlighted?: string[]; serviceFocus?: number; reduced: boolean; fps: 30 | 60; locked: boolean }
export interface SpectatorSceneMetrics { frames: number; p95: number; active: number; queued: number; geometries: number; textures: number; calls: number; triangles: number; cpuP95: number; renderDuty: number }

/** WebGL owns its frame loop. Vue receives no per-frame mutations. */
export class SpectatorScene {
  private renderer: THREE.WebGLRenderer
  private disposed = false
  private ambient!: THREE.HemisphereLight
  private keyLight!: THREE.DirectionalLight
  private fillLight!: THREE.DirectionalLight
  private scene = new THREE.Scene()
  private perspective = new THREE.PerspectiveCamera(39, 1, .1, 200)
  private orthographic = new THREE.OrthographicCamera(-16, 16, 8, -8, .1, 200)
  private camera: THREE.PerspectiveCamera | THREE.OrthographicCamera = this.perspective
  private aspect = 1
  private world = new THREE.Group()
  private geometries: THREE.BufferGeometry[] = []
  private materials: THREE.Material[] = []
  private textures: THREE.Texture[] = []
  private controlLines = new Map<string, THREE.LineSegments>()
  private nodes = new Map<string, THREE.Group>()
  private queue: SpectatorBeam[] = []
  private beams: { mesh: ReturnType<typeof createSpectatorRibbon>; start: number }[] = []
  private raf = 0
  private previous = 0
  private wasActive = false
  private frames = 0
  private timings: number[] = []
  private cpuTimings: number[] = []
  private cpuTime = 0
  private createdAt = performance.now()
  private signature = ''
  private lamps: { mesh: THREE.InstancedMesh; node: string; service: number; index: number }[] = []
  private flags = new Map<string, { group: THREE.Group; material: THREE.MeshStandardMaterial; owner?: number; name?: string; start: number; leaving: boolean; texture: THREE.CanvasTexture }>()
  private batch = new Map<THREE.Material, THREE.Matrix4[]>()
  private grid!: THREE.GridHelper
  private state!: SpectatorSceneState
  private colors: Record<string, THREE.Color>
  private resizeObserver: ResizeObserver
  private angle = 0
  private dragging = false
  private lastX = 0
  private labels: HTMLDivElement[] = []

  constructor(private element: HTMLElement, private onContextLost?: () => void) {
    this.renderer = new THREE.WebGLRenderer({ antialias: true, alpha: false, powerPreference: 'low-power' })
    this.renderer.setPixelRatio(Math.min(devicePixelRatio, 1.5))
    this.renderer.outputColorSpace = THREE.SRGBColorSpace
    this.renderer.shadowMap.enabled = true; this.renderer.shadowMap.type = THREE.PCFShadowMap
    this.renderer.shadowMap.autoUpdate = false
    this.renderer.setClearColor(themeColor('--spectator-ground', element))
    this.colors = Object.fromEntries(['metal', 'edge', 'attack', 'healthy', 'unknown', 'rock', 'rock-lit', 'snow', 'flag-a', 'flag-b', 'flag-c', 'flag-d'].map(key => [key, new THREE.Color(themeColor(`--spectator-${key}`, element))]))
    element.append(this.renderer.domElement)
    this.renderer.domElement.addEventListener('webglcontextlost', this.contextLost)
    this.scene.add(this.world)
    this.ambient = new THREE.HemisphereLight(this.colors.snow!, this.colors.metal!, 1.15)
    this.scene.add(this.ambient)
    const light = new THREE.DirectionalLight(this.colors.snow!, 2.4)
    light.position.set(-18, 24, 12); light.castShadow = true
    light.shadow.mapSize.set(2048, 2048); light.shadow.camera.left = -32; light.shadow.camera.right = 32
    light.shadow.camera.top = 32; light.shadow.camera.bottom = -32; light.shadow.normalBias = .04
    this.scene.add(light); this.keyLight = light
    const fill = new THREE.DirectionalLight(this.colors.edge!, .9)
    fill.position.set(10, 3, -10); this.scene.add(fill); this.fillLight = fill
    const grid = new THREE.GridHelper(55, 44, this.colors.edge!, this.colors.metal!)
    grid.material.transparent = true; grid.material.opacity = .23
    this.geometries.push(grid.geometry); this.materials.push(grid.material)
    this.scene.add(grid); this.grid = grid
    this.resizeObserver = new ResizeObserver(() => this.resize())
    this.resizeObserver.observe(element)
    document.addEventListener('visibilitychange', this.visibility)
    element.addEventListener('pointerdown', this.down)
    element.addEventListener('pointermove', this.move)
    element.addEventListener('pointerup', this.up)
    element.addEventListener('pointercancel', this.up)
    this.resize()
  }
  private material(color: THREE.Color, metalness = 0) {
    const material = new THREE.MeshStandardMaterial({ color, roughness: .86, metalness })
    this.materials.push(material); return material
  }
  private box = new THREE.BoxGeometry(1, 1, 1)
  private addBox(group: THREE.Group, material: THREE.Material, x: number, y: number, z: number, w: number, h: number, d: number) {
    const matrix = new THREE.Matrix4().compose(new THREE.Vector3(x, y, z).add(group.position), new THREE.Quaternion(), new THREE.Vector3(w, h, d))
    const matrices = this.batch.get(material) ?? []; matrices.push(matrix); this.batch.set(material, matrices)
  }
  update(state: SpectatorSceneState) {
    this.state = state
    const signature = JSON.stringify([state.mode, state.nodes.map(n => [n.id, n.services?.length])])
    const rebuilt = signature !== this.signature
    if (rebuilt) {
      this.signature = signature; this.clearWorld()
      this.grid.visible = state.mode === 'Awd'
      this.renderer.shadowMap.enabled = state.mode === 'Koh'
      this.ambient.intensity = state.mode === 'Koh' ? .75 : 1.15
      this.ambient.groundColor.copy(state.mode === 'Koh' ? this.colors.rock! : this.colors.metal!)
      this.keyLight.intensity = state.mode === 'Koh' ? 2.8 : 2.4
      this.keyLight.color.copy(state.mode === 'Koh' ? this.colors['rock-lit']! : this.colors.snow!)
      this.fillLight.intensity = state.mode === 'Koh' ? .35 : .9
      this.fillLight.color.copy(state.mode === 'Koh' ? this.colors['rock-lit']! : this.colors.edge!)
      const metal = this.material(this.colors.metal!, .5), edge = this.material(this.colors.edge!, .3)
      const lampMaterial = this.material(new THREE.Color(1, 1, 1))
      const count = state.nodes.length, columns = Math.ceil(Math.sqrt(count * (state.mode === 'Koh' ? 1 : 1.5))), rows = Math.ceil(count / columns)
      const compact = state.mode === 'Awd' && count > 12
      const peaks: THREE.Vector3[] = []
      state.nodes.forEach((node, index) => {
        const group = new THREE.Group()
        group.position.set((index % columns - (columns - 1) / 2) * (state.mode === 'Koh' ? 9.5 : 7), 0, (Math.floor(index / columns) - (rows - 1) / 2) * (state.mode === 'Koh' ? 9 : 7.8))
        if (state.mode === 'Koh' && count === 3 && index === 2) group.position.x = 0
        group.userData.id = node.id
        group.userData.servicePositions = []
        if (state.mode === 'Awd') {
          this.addBox(group, metal, 0, .2, 0, compact ? 3.6 : 4.3, .4, compact ? 1.8 : 3.3)
          this.addBox(group, edge, 0, .43, compact ? .88 : 1.62, compact ? 3.5 : 4.2, .035, .03)
          const services = node.services ?? []
          services.forEach((_, i) => {
            const cols = compact ? 8 : 4
            const x = (i % cols - (Math.min(cols, services.length) - 1) / 2) * (compact ? .4 : .95)
            const z = Math.floor(i / cols) * (compact ? .32 : 1.4) - (compact ? .2 : .65)
            if (!compact) this.addBox(group, metal, x, 1, z, .7, 1.4, .85)
            ;(group.userData.servicePositions as THREE.Vector3[]).push(new THREE.Vector3(x, compact ? .5 : 1.1, z).add(group.position))
            const matrix = new THREE.Matrix4().compose(new THREE.Vector3(x, compact ? .48 : 1.1, z + (compact ? 0 : .43)).add(group.position), new THREE.Quaternion(), new THREE.Vector3(compact ? .25 : .5, compact ? .08 : .055, compact ? .19 : .018))
            const index = this.batch.get(lampMaterial)?.length ?? 0
            const matrices = this.batch.get(lampMaterial) ?? []; matrices.push(matrix); this.batch.set(lampMaterial, matrices)
            this.lamps.push({ mesh: null!, node: node.id, service: i, index })
          })
        } else peaks.push(group.position.clone())
        this.world.add(group); this.nodes.set(node.id, group)
        const label = document.createElement('div'); label.className = 'spectator-scene-label'
        label.dataset.nodeId = node.id; this.element.append(label); this.labels.push(label)
      })
      if (state.mode === 'Koh' && peaks.length) {
        const { geometry, contours, controlContours } = createTerrain(peaks, columns * 9.5 + 96, rows * 9 + 96)
        this.geometries.push(geometry, contours, ...controlContours)
        const ready = { value: 0 }
        const texture = new THREE.TextureLoader().load(rockTextureUrl, loaded => {
          if (!this.disposed && this.textures.includes(loaded)) { ready.value = 1; this.invalidate() }
        })
        texture.wrapS = texture.wrapT = THREE.RepeatWrapping
        texture.colorSpace = THREE.SRGBColorSpace
        texture.anisotropy = Math.min(8, this.renderer.capabilities.getMaxAnisotropy())
        this.textures.push(texture)
        const rock = createSpectatorRock(texture, this.colors['rock-lit']!.clone().lerp(this.colors.rock!, .18), this.colors.rock!, ready)
        this.materials.push(rock)
        const terrain = new THREE.Mesh(geometry, rock); terrain.castShadow = true; terrain.receiveShadow = true; this.world.add(terrain)
        const fragments = terrainFragments(peaks)
        const rubbleGeometry = new THREE.IcosahedronGeometry(1, 0)
        this.geometries.push(rubbleGeometry)
        const rubbleMaterial = this.material(this.colors.rock!.clone().multiplyScalar(.85))
        rubbleMaterial.flatShading = true; rubbleMaterial.roughness = 1
        const rubble = new THREE.InstancedMesh(rubbleGeometry, rubbleMaterial, fragments.length)
        fragments.forEach((fragment, i) => {
          rubble.setMatrixAt(i, new THREE.Matrix4().compose(fragment.position,
            new THREE.Quaternion().setFromEuler(new THREE.Euler(.15, fragment.rotation, .1)), fragment.scale))
          rubble.setColorAt(i, new THREE.Color(fragment.shade, fragment.shade, fragment.shade))
        })
        rubble.instanceMatrix.needsUpdate = true
        rubble.castShadow = true; rubble.receiveShadow = true; this.world.add(rubble)
        this.renderer.shadowMap.needsUpdate = true
        controlContours.forEach((g, i) => {
          const m = new THREE.LineBasicMaterial({ color: this.colors.edge, transparent: true, depthWrite: false, opacity: .68 })
          this.materials.push(m); const line = new THREE.LineSegments(g, m); this.world.add(line); this.controlLines.set(state.nodes[i]!.id, line)
        })
        const contourMaterial = new THREE.LineBasicMaterial({ color: this.colors.snow!.clone().lerp(this.colors.rock!, .4), transparent: true, depthWrite: false, opacity: .29, vertexColors: true })
        this.materials.push(contourMaterial); this.world.add(new THREE.LineSegments(contours, contourMaterial))
        for (const node of state.nodes) {
          const group = this.nodes.get(node.id)!, y = terrainHeight(group.position.x, group.position.z, peaks)
          group.userData.height = y
          this.addBox(group, edge, 0, y + .9, 0, .035, 1.8, .035)
          const flagGroup = new THREE.Group(); flagGroup.position.y = y; group.add(flagGroup)
          const material = this.material(this.colors.unknown!)
          const cloth = new THREE.PlaneGeometry(1.65, .65, 12, 2)
          const cp = cloth.attributes.position!
          for (let i = 0; i < cp.count; i++) cp.setZ(i, Math.sin((cp.getX(i) + .825) * 5) * .065)
          cloth.computeVertexNormals(); this.geometries.push(cloth)
          const canvas = document.createElement('canvas'); canvas.width = 512; canvas.height = 192
          const texture = new THREE.CanvasTexture(canvas); texture.colorSpace = THREE.SRGBColorSpace
          this.textures.push(texture); material.map = texture; material.side = THREE.DoubleSide; material.color.setRGB(1, 1, 1)
          const flag = new THREE.Mesh(cloth, material)
          flag.position.set(.825, 1.52, 0); flagGroup.add(flag)
          this.flags.set(node.id, { group: flagGroup, material, owner: undefined, start: 0, leaving: false, texture })
        }
      }
      for (const [material, matrices] of this.batch) {
        const mesh = new THREE.InstancedMesh(this.box, material, matrices.length)
        matrices.forEach((matrix, i) => mesh.setMatrixAt(i, matrix))
        mesh.instanceMatrix.needsUpdate = true
        if (material === lampMaterial) this.lamps.forEach(lamp => { lamp.mesh = mesh })
        this.world.add(mesh)
      }
      this.batch.clear()
    }
    const byId = new Map(state.nodes.map(n => [n.id, n]))
    for (const lamp of this.lamps) {
      const status = byId.get(lamp.node)?.services?.[lamp.service]
      const color = (status === 'Healthy' ? this.colors.healthy : status === 'Down' ? this.colors.attack : this.colors.unknown)!.clone()
      if (state.serviceFocus !== undefined && state.serviceFocus >= 0 && lamp.service !== state.serviceFocus) color.multiplyScalar(.2)
      lamp.mesh.setColorAt(lamp.index, color); lamp.mesh.instanceColor!.needsUpdate = true
    }
    for (const [id, flag] of this.flags) {
      const node = byId.get(id)!
      if (node.owner !== flag.owner) {
        flag.leaving = node.owner === undefined; flag.start = rebuilt || state.reduced || node.health === 'Unknown' && node.owner === undefined ? 0 : performance.now()
        flag.owner = node.owner

      }
      if (flag.name !== node.ownerName) {
        flag.name = node.ownerName
        const canvas = flag.texture.image as HTMLCanvasElement, context = canvas.getContext('2d')!
        context.fillStyle = (node.owner === undefined ? this.colors.unknown : this.colors[['flag-a', 'flag-b', 'flag-c', 'flag-d'][node.owner % 4]!]!)!.getStyle()
        context.fillRect(0, 0, 512, 192); context.fillStyle = this.colors.snow!.getStyle()
        context.font = '700 52px Microsoft YaHei, sans-serif'; context.textBaseline = 'middle'; context.fillText(node.controllerName ?? '', 22, 96, 468)
        flag.texture.needsUpdate = true
      }
      const contour = this.controlLines.get(id)
      if (contour) { contour.visible = node.owner !== undefined; (contour.material as THREE.LineBasicMaterial).color.copy(this.colors[['flag-a', 'flag-b', 'flag-c', 'flag-d'][(node.owner ?? 0) % 4]!]!) }
      flag.group.visible = node.owner !== undefined || flag.start > 0
      if (!flag.start || state.reduced) { flag.group.scale.y = 1; flag.start = 0; flag.group.visible = node.owner !== undefined }
    }
    for (const label of this.labels) {
      const node = byId.get(label.dataset.nodeId!)!
      label.replaceChildren()
      const title = document.createElement('strong'); title.textContent = node.name; label.append(title)
      if (state.mode === 'Koh') { const owner = document.createElement('span'); owner.textContent = node.ownerName ?? ''; owner.dataset.health = node.health; label.append(owner) }
      label.dataset.highlight = String(state.highlighted?.includes(node.id) ?? false)
    }
    if (state.reduced) this.clearBeams()
    this.positionCamera(); this.invalidate()
  }
  enqueue(attacks: SpectatorBeam[]) {
    if (this.state?.reduced || document.hidden) return
    this.queue.push(...attacks.slice(-30)); this.queue = this.queue.slice(-30); this.invalidate()
  }
  private positionCamera() {
    const focused = this.nodes.get(this.state?.focus)?.position
    const selected = (this.state?.highlighted ?? []).map(id => this.nodes.get(id)?.position).filter((p): p is THREE.Vector3 => !!p)
    const target = focused?.clone() ?? (selected.length ? selected.reduce((a, p) => a.add(p), new THREE.Vector3()).divideScalar(selected.length) : new THREE.Vector3())
    if (this.state?.mode === 'Koh') {
      this.camera = this.orthographic
      target.y = 1.3
      if (!focused) target.z += 1
      const height = (focused ? 10 : Math.max(15, Math.sqrt(this.nodes.size) * 6.6)) * Math.max(1, 1.65 / this.aspect)
      this.orthographic.left = -height * this.aspect / 2; this.orthographic.right = height * this.aspect / 2
      this.orthographic.top = height / 2; this.orthographic.bottom = -height / 2
      this.orthographic.updateProjectionMatrix()
      this.camera.position.set(target.x + Math.sin(this.angle) * 36, target.y + 30, target.z + Math.cos(this.angle) * 36)
    } else {
      this.camera = this.perspective
      const size = focused ? 12 : Math.max(17, Math.sqrt(this.nodes.size) * 6.5) * Math.max(1, 1.65 / this.aspect)
      this.camera.position.set(target.x + Math.sin(this.angle) * size, size * .95, target.z + Math.cos(this.angle) * size)
    }
    this.camera.lookAt(target); this.camera.updateMatrixWorld(); this.placeLabels()
  }
  private placeLabels() {
    for (const label of this.labels) {
      const node = this.nodes.get(label.dataset.nodeId!)!
      const point = node.position.clone().add(new THREE.Vector3(0, this.state?.mode === 'Koh' ? (node.userData.height as number) + 1.95 : .2, this.state?.mode === 'Koh' ? 0 : 1.45)).project(this.camera)
      const neighbor = node.position.clone().add(new THREE.Vector3(5.5, .2, 2.1)).project(this.camera)
      label.style.maxWidth = `${Math.max(45, Math.abs(neighbor.x - point.x) * this.element.clientWidth / 2)}px`
      label.style.left = `${(point.x + 1) * 50}%`; label.style.top = `${(1 - point.y) * 50}%`
      label.hidden = Math.abs(point.x) > .95 || Math.abs(point.y) > .95 || point.z > 1
    }
  }
  private resize() {
    const w = this.element.clientWidth, h = this.element.clientHeight
    if (!w || !h) return
    this.renderer.setSize(w, h); this.aspect = w / h; this.perspective.aspect = this.aspect; this.perspective.updateProjectionMatrix()
    this.positionCamera(); this.invalidate()
  }
  private invalidate() { if (!this.disposed && !this.raf && !document.hidden) this.raf = requestAnimationFrame(this.draw) }
  private draw = (time: number) => {
    this.raf = 0
    if (document.hidden) return
    const interval = 1000 / (this.state?.fps ?? 60)
    if (time - this.previous < interval - 1) { this.raf = requestAnimationFrame(this.draw); return }
    if (this.previous && this.wasActive) { this.timings.push(time - this.previous); if (this.timings.length > 1200) this.timings.shift() }
    const cpuStart = performance.now()
    this.previous = time
    while (this.beams.length < 12 && this.queue.length) {
      const event = this.queue.shift()!, source = this.nodes.get(event.source), target = this.nodes.get(event.target)
      if (!source || !target) continue
      const from = source.position.clone().add(new THREE.Vector3(0, 1.4, 0))
      const to = (target.userData.servicePositions as THREE.Vector3[])[event.service]?.clone() ?? target.position.clone().add(new THREE.Vector3(0, .5, 0))
      const mesh = createSpectatorRibbon(from, to, this.colors.attack!)
      this.scene.add(mesh); this.beams.push({ mesh, start: time })
    }
    this.beams = this.beams.filter(beam => {
      const phase = (time - beam.start) / 1700
      beam.mesh.material.uniforms.phase!.value = phase
      if (phase < 1) return true
      this.scene.remove(beam.mesh); beam.mesh.geometry.dispose(); beam.mesh.material.dispose(); return false
    })
    let transferring = false
    for (const flag of this.flags.values()) {
      if (!flag.start) continue
      if (setFlagTransfer(flag.group, time - flag.start, flag.leaving)) transferring = true
      else { flag.start = 0; flag.group.visible = !flag.leaving }
    }
    this.renderer.render(this.scene, this.camera); this.frames++
    const cpu = performance.now() - cpuStart; this.cpuTime += cpu; this.cpuTimings.push(cpu); if (this.cpuTimings.length > 1200) this.cpuTimings.shift()
    this.wasActive = transferring || this.beams.length > 0 || this.queue.length > 0
    if (this.wasActive) this.raf = requestAnimationFrame(this.draw)
  }
  metrics(): SpectatorSceneMetrics {
    const sorted = [...this.timings].sort((a, b) => a - b)
    const cpu = [...this.cpuTimings].sort((a, b) => a - b)
    return { cpuP95: Math.round((cpu[Math.floor(cpu.length * .95)] ?? 0) * 100) / 100, renderDuty: Math.round(this.cpuTime / (performance.now() - this.createdAt) * 10000) / 100, frames: this.frames, p95: sorted[Math.floor(sorted.length * .95)] ?? 0, active: this.beams.length, queued: this.queue.length, ...this.renderer.info.memory, calls: this.renderer.info.render.calls, triangles: this.renderer.info.render.triangles }
  }
  private contextLost = (event: Event) => { event.preventDefault(); cancelAnimationFrame(this.raf); this.raf = 0; this.clearBeams(); this.onContextLost?.() }
  private visibility = () => { if (document.hidden) { cancelAnimationFrame(this.raf); this.raf = 0; this.clearBeams() } else this.invalidate() }
  private down = (event: PointerEvent) => { if (this.state.locked) return; this.dragging = true; this.lastX = event.clientX; this.element.setPointerCapture(event.pointerId) }
  private move = (event: PointerEvent) => { if (!this.dragging) return; this.angle += (event.clientX - this.lastX) * .004; this.lastX = event.clientX; this.positionCamera(); this.invalidate() }
  private up = () => { this.dragging = false }
  private clearBeams() { for (const beam of this.beams) { this.scene.remove(beam.mesh); beam.mesh.geometry.dispose(); beam.mesh.material.dispose() }; this.beams = []; this.queue = [] }
  private clearWorld() {
    this.clearBeams(); this.world.traverse(o => { if (o instanceof THREE.InstancedMesh) o.dispose() }); this.world.clear(); this.controlLines.clear(); this.textures.forEach(t => t.dispose()); this.textures = []; this.flags.clear(); this.lamps = []; this.batch.clear(); this.nodes.clear(); this.labels.forEach(x => x.remove()); this.labels = []
    // Dispose only per-layout resources. Grid and shared box survive layout changes.
    while (this.geometries.length > 1) this.geometries.pop()!.dispose()
    while (this.materials.length > 1) this.materials.pop()!.dispose()
  }
  dispose() {
    this.disposed = true
    cancelAnimationFrame(this.raf); this.resizeObserver.disconnect(); this.clearWorld()
    document.removeEventListener('visibilitychange', this.visibility)
    this.element.removeEventListener('pointerdown', this.down); this.element.removeEventListener('pointermove', this.move)
    this.element.removeEventListener('pointerup', this.up); this.element.removeEventListener('pointercancel', this.up)
    this.geometries.forEach(x => x.dispose()); this.materials.forEach(x => x.dispose()); this.box.dispose()
    this.renderer.domElement.removeEventListener('webglcontextlost', this.contextLost)
    this.scene.traverse(object => { if (object instanceof THREE.DirectionalLight) object.shadow.dispose() })
    this.renderer.dispose(); this.renderer.domElement.remove()
  }
}
